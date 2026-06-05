/**
 * LowConfidenceBadge — R5 task 028 / D2-18 unit + integration tests.
 *
 * Covers spec FR-15 + SC-14 acceptance criteria:
 *
 *   (T1) Badge visible when `confidence < threshold` (default 0.6) — both
 *        directly via `<LowConfidenceBadge>` and end-to-end via
 *        `<InsightsResponseRenderer>` for each response case.
 *
 *   (T2) Badge absent (no DOM node) when `confidence >= threshold` — boundary
 *        condition (== threshold) + above-threshold.
 *
 *   (T3) Badge absent for malformed `confidence` — null, undefined, NaN,
 *        negative, > 1, non-number.
 *
 *   (T4) Threshold configurable via prop AND via
 *        `setInsightsRendererConfig`. Reactive — re-render picks up the new
 *        value.
 *
 *   (T5) Exact badge text matches "Low confidence — verify before relying"
 *        (em-dash U+2014; no trailing punctuation).
 *
 *   (T6) Dark-mode smoke — badge mounts under `webDarkTheme` without
 *        exceptions and continues to expose the exact text.
 *
 *   (T7) Pure-helper `shouldShowLowConfidenceBadge` boundary tests — same
 *        contract verified at the function level so future refactors keep
 *        the defensive guard intact.
 *
 * Test framework: same setup as `InsightsResponseRenderer.test.tsx` —
 * `@testing-library/react` + Vitest (project's existing convention).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import {
  FluentProvider,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

import {
  LowConfidenceBadge,
  LOW_CONFIDENCE_BADGE_TEXT,
  shouldShowLowConfidenceBadge,
} from '../LowConfidenceBadge';
import { InsightsResponseRenderer } from '../InsightsResponseRenderer';
import type {
  InsightsResponse,
  PlaybookInferenceResponse,
  PlaybookDeclineResponse,
  RagObservationResponse,
  Citation,
  Diagnostics,
} from '../types';
import {
  DEFAULT_CONFIDENCE_THRESHOLD,
  getInsightsRendererConfig,
  resetInsightsRendererConfig,
  setInsightsRendererConfig,
} from '../../../../config/insightsRendererConfig';

// ---------------------------------------------------------------------------
// Fixtures — mirror the shapes used in InsightsResponseRenderer.test.tsx so
// the two test files stay aligned on envelope structure.
// ---------------------------------------------------------------------------

const STD_DIAGNOSTICS: Diagnostics = {
  intentSource: 'classifier',
  classifierBelowThreshold: false,
  elapsedMs: 1234,
  cacheHit: false,
  conversationId: 'conv-test-028',
};

const STD_CITATION: Citation = {
  n: 1,
  source: 'Acme APA.pdf',
  excerpt: 'Estimated cost: $282k',
  observationId: 'doc-A',
  chunkId: 'chunk-1',
};

function makePlaybookInferenceResponse(
  confidence: number,
): PlaybookInferenceResponse {
  return {
    path: 'playbook',
    playbookId: 'predict-matter-cost@v1',
    answer: 'Predicted cost ~$280k based on 12 similar matters.',
    citations: [STD_CITATION],
    confidence,
    structuredResult: {
      kind: 'inference',
      envelope: {
        predictedCost: 280000,
        currency: 'USD',
        comparableMatters: 12,
        inferenceBody: 'Twelve comparable matters.',
        evidenceList: ['Acme APA.pdf — page 7'],
      },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

function makePlaybookDeclineResponse(
  confidence: number,
): PlaybookDeclineResponse {
  return {
    path: 'playbook',
    playbookId: 'predict-matter-cost@v1',
    answer: 'Not enough comparable matters to confidently predict cost.',
    citations: [],
    confidence,
    structuredResult: {
      kind: 'decline',
      envelope: {
        reason: 'evidence-insufficient',
        minimumEvidenceNeeded: 'At least 5 comparable closed matters.',
        suggestedActions: ['Attach a comparable matter folder'],
        confidenceInDecline: 0.85,
        explanation: 'Not enough comparable matters.',
      },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

function makeRagObservationResponse(
  confidence: number,
): RagObservationResponse {
  return {
    path: 'rag',
    playbookId: null,
    answer: 'Closing conditions include [1] regulatory approval.',
    citations: [STD_CITATION],
    confidence,
    structuredResult: {
      kind: 'observation',
      envelope: {
        results: [{ id: 'r1' }],
        summary: 'One condition identified.',
      },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

function makeRagEmptyResponse(confidence: number): RagObservationResponse {
  return {
    path: 'rag',
    playbookId: null,
    answer: '',
    citations: [],
    confidence,
    structuredResult: {
      kind: 'observation',
      envelope: { results: [], summary: '' },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWithProviders(
  ui: React.ReactElement,
  options: { theme?: typeof webLightTheme } = {},
): void {
  const theme = options.theme ?? webLightTheme;
  const bus = new PaneEventBus();
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>{ui}</PaneEventBusProvider>
    </FluentProvider>,
  );
}

// ---------------------------------------------------------------------------
// Cleanup — config singleton MUST be reset between tests so config-driven
// reconfiguration cases don't leak.
// ---------------------------------------------------------------------------

afterEach(() => {
  resetInsightsRendererConfig();
});

// ---------------------------------------------------------------------------
// (T7) Pure-helper `shouldShowLowConfidenceBadge` — boundary tests first so a
// regression here is caught before any DOM-level test fires.
// ---------------------------------------------------------------------------

describe('shouldShowLowConfidenceBadge (pure helper)', () => {
  it('returns true when confidence is strictly below threshold', () => {
    expect(shouldShowLowConfidenceBadge(0.5, 0.6)).toBe(true);
    expect(shouldShowLowConfidenceBadge(0.0, 0.6)).toBe(true);
    expect(shouldShowLowConfidenceBadge(0.59999, 0.6)).toBe(true);
  });

  it('returns false at exact threshold (boundary is exclusive)', () => {
    expect(shouldShowLowConfidenceBadge(0.6, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(0.7, 0.7)).toBe(false);
  });

  it('returns false when confidence is above threshold', () => {
    expect(shouldShowLowConfidenceBadge(0.85, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(1.0, 0.6)).toBe(false);
  });

  it('returns false for null / undefined confidence', () => {
    expect(shouldShowLowConfidenceBadge(null, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(undefined, 0.6)).toBe(false);
  });

  it('returns false for NaN', () => {
    expect(shouldShowLowConfidenceBadge(Number.NaN, 0.6)).toBe(false);
  });

  it('returns false for out-of-range values', () => {
    expect(shouldShowLowConfidenceBadge(-0.1, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(1.5, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(Number.POSITIVE_INFINITY, 0.6)).toBe(false);
    expect(shouldShowLowConfidenceBadge(Number.NEGATIVE_INFINITY, 0.6)).toBe(false);
  });

  it('handles reconfigured threshold consistently', () => {
    // Higher threshold catches more responses as low-confidence.
    expect(shouldShowLowConfidenceBadge(0.7, 0.8)).toBe(true);
    expect(shouldShowLowConfidenceBadge(0.7, 0.6)).toBe(false);
    // Lower threshold catches fewer.
    expect(shouldShowLowConfidenceBadge(0.4, 0.5)).toBe(true);
    expect(shouldShowLowConfidenceBadge(0.4, 0.3)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// (T1) Badge visible below threshold — direct component + via renderer
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — visible when confidence < threshold', () => {
  it('renders the badge directly with default-config threshold (0.6)', () => {
    renderWithProviders(<LowConfidenceBadge confidence={0.5} />);
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
    expect(screen.getByTestId('low-confidence-badge')).toBeInTheDocument();
  });

  it('renders badge for playbook-inference response (path=playbook)', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makePlaybookInferenceResponse(0.5)}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('renders badge for playbook-decline response (path=playbook decline)', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makePlaybookDeclineResponse(0.15)}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('renders badge for RAG observation response (path=rag)', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse(0.4)}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('renders badge for RAG empty-result response (path=rag empty)', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagEmptyResponse(0.2)}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (T2) Badge absent when confidence >= threshold — boundary + above
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — absent when confidence >= threshold', () => {
  it('does NOT render badge at exact threshold (0.6 vs 0.6)', () => {
    renderWithProviders(<LowConfidenceBadge confidence={0.6} />);
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
    expect(screen.queryByTestId('low-confidence-badge')).toBeNull();
  });

  it('does NOT render badge when confidence is above threshold (0.85)', () => {
    renderWithProviders(<LowConfidenceBadge confidence={0.85} />);
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
  });

  it('does NOT render badge when confidence = 1.0', () => {
    renderWithProviders(<LowConfidenceBadge confidence={1.0} />);
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
  });

  it('high-confidence playbook-inference response shows NO badge', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makePlaybookInferenceResponse(0.92)}
      />,
    );
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
    // Sanity: the case wrapper still renders the response itself.
    expect(
      screen.getByTestId('insights-response-renderer'),
    ).toHaveAttribute('data-response-case', 'playbook-inference');
  });

  it('high-confidence RAG response shows NO badge', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse(0.81)}
      />,
    );
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
    expect(
      screen.getByTestId('insights-response-renderer'),
    ).toHaveAttribute('data-response-case', 'rag');
  });
});

// ---------------------------------------------------------------------------
// (T3) Defensive — null / undefined / NaN / out-of-range → no badge
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — defensive handling of malformed confidence', () => {
  it.each<[string, number | null | undefined]>([
    ['null', null],
    ['undefined', undefined],
    ['NaN', Number.NaN],
    ['negative', -0.1],
    ['above 1', 1.5],
    ['positive infinity', Number.POSITIVE_INFINITY],
    ['negative infinity', Number.NEGATIVE_INFINITY],
  ])('does NOT render badge for %s confidence', (_label, confidence) => {
    renderWithProviders(<LowConfidenceBadge confidence={confidence} />);
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
  });

  it('handles a malformed response confidence without crashing the renderer', () => {
    // Build an envelope with non-finite confidence; the badge should be absent
    // but the sub-renderer should still mount normally.
    const malformed = {
      ...makeRagObservationResponse(0.5),
      confidence: Number.NaN,
    } as unknown as InsightsResponse;

    renderWithProviders(
      <InsightsResponseRenderer response={malformed} />,
    );
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
    // Renderer still mounts the case wrapper.
    expect(
      screen.getByTestId('insights-response-renderer'),
    ).toHaveAttribute('data-response-case', 'rag');
  });
});

// ---------------------------------------------------------------------------
// (T4) Threshold configurable — via prop AND via config singleton
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — threshold configurable', () => {
  it('honors prop threshold override (e.g. 0.7)', () => {
    // 0.65 < 0.7 → badge visible
    renderWithProviders(
      <LowConfidenceBadge confidence={0.65} threshold={0.7} />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('prop threshold override flips visibility — same confidence, different threshold', () => {
    const { unmount } = render(
      <FluentProvider theme={webLightTheme}>
        <LowConfidenceBadge confidence={0.65} threshold={0.7} />
      </FluentProvider>,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
    unmount();

    // Lower threshold => 0.65 is now above => badge absent
    render(
      <FluentProvider theme={webLightTheme}>
        <LowConfidenceBadge confidence={0.65} threshold={0.6} />
      </FluentProvider>,
    );
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
  });

  it('default config exposes DEFAULT_CONFIDENCE_THRESHOLD = 0.6', () => {
    expect(DEFAULT_CONFIDENCE_THRESHOLD).toBe(0.6);
    expect(getInsightsRendererConfig().confidenceThreshold).toBe(0.6);
  });

  it('setInsightsRendererConfig reconfigures the singleton threshold', () => {
    setInsightsRendererConfig({ confidenceThreshold: 0.8 });
    // 0.7 < 0.8 with reconfigured threshold => badge visible
    renderWithProviders(<LowConfidenceBadge confidence={0.7} />);
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('config-driven threshold flows into InsightsResponseRenderer end-to-end', () => {
    setInsightsRendererConfig({ confidenceThreshold: 0.8 });
    // Confidence 0.7 was previously "above default 0.6" → no badge.
    // With reconfigured 0.8, 0.7 < 0.8 → badge visible.
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse(0.7)}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('resetInsightsRendererConfig restores default 0.6', () => {
    setInsightsRendererConfig({ confidenceThreshold: 0.2 });
    expect(getInsightsRendererConfig().confidenceThreshold).toBe(0.2);
    resetInsightsRendererConfig();
    expect(getInsightsRendererConfig().confidenceThreshold).toBe(
      DEFAULT_CONFIDENCE_THRESHOLD,
    );
  });

  it('renderer prop overrides config singleton (prop wins)', () => {
    setInsightsRendererConfig({ confidenceThreshold: 0.2 });
    // With config alone, 0.5 > 0.2 => badge absent.
    // With prop threshold 0.8, 0.5 < 0.8 => badge visible.
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse(0.5)}
        confidenceThreshold={0.8}
      />,
    );
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (T5) Exact badge text — em-dash; no trailing punctuation
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — exact badge text', () => {
  it('exports the exact spec-mandated text constant', () => {
    expect(LOW_CONFIDENCE_BADGE_TEXT).toBe(
      'Low confidence — verify before relying',
    );
  });

  it('uses em-dash U+2014 (NOT en-dash U+2013 or hyphen-minus U+002D)', () => {
    // Position 14 (zero-based) is the dash character.
    const dashCodePoint = LOW_CONFIDENCE_BADGE_TEXT.codePointAt(14);
    expect(dashCodePoint).toBe(0x2014);
  });

  it('has no trailing punctuation', () => {
    expect(LOW_CONFIDENCE_BADGE_TEXT.endsWith('.')).toBe(false);
    expect(LOW_CONFIDENCE_BADGE_TEXT.endsWith(',')).toBe(false);
    expect(LOW_CONFIDENCE_BADGE_TEXT.endsWith(';')).toBe(false);
    expect(LOW_CONFIDENCE_BADGE_TEXT.endsWith('!')).toBe(false);
    expect(LOW_CONFIDENCE_BADGE_TEXT.endsWith('?')).toBe(false);
  });

  it('rendered DOM text matches the exact constant', () => {
    renderWithProviders(<LowConfidenceBadge confidence={0.4} />);
    const el = screen.getByText(LOW_CONFIDENCE_BADGE_TEXT);
    expect(el.textContent).toBe(LOW_CONFIDENCE_BADGE_TEXT);
  });
});

// ---------------------------------------------------------------------------
// (T6) Dark-mode smoke — mount under webDarkTheme without exceptions
// ---------------------------------------------------------------------------

describe('LowConfidenceBadge — dark mode smoke', () => {
  it('mounts the badge under webDarkTheme without throwing', () => {
    expect(() => {
      renderWithProviders(<LowConfidenceBadge confidence={0.4} />, {
        theme: webDarkTheme,
      });
    }).not.toThrow();
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('mounts InsightsResponseRenderer with low-confidence response under webDarkTheme', () => {
    expect(() => {
      renderWithProviders(
        <InsightsResponseRenderer
          response={makeRagObservationResponse(0.4)}
        />,
        { theme: webDarkTheme },
      );
    }).not.toThrow();
    expect(
      screen.getByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeInTheDocument();
  });

  it('mounts the badge for the absent case under webDarkTheme (no DOM node)', () => {
    renderWithProviders(<LowConfidenceBadge confidence={0.85} />, {
      theme: webDarkTheme,
    });
    expect(
      screen.queryByText(LOW_CONFIDENCE_BADGE_TEXT),
    ).toBeNull();
  });
});
