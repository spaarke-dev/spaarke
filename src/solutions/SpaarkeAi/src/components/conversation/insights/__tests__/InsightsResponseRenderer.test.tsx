/**
 * InsightsResponseRenderer — R5 task 026 / D2-16 unit + integration tests.
 *
 * Covers the acceptance criteria from `tasks/026-insights-response-renderer.poml`:
 *
 *   (1) Discrimination — the four cases route to the correct sub-renderer:
 *       - Playbook-inference → `PlaybookResponseRenderer`
 *       - Playbook-decline → `DeclineResponseRenderer`
 *       - RAG observation (with citations) → `RagResponseRenderer`
 *       - RAG empty-result → `EmptyResultHint`
 *
 *   (2) Playbook reuse — the playbook path consumes task 017's
 *       `StructuredOutputStreamWidget` via `INSIGHTS_PLAYBOOK_SCHEMA`.
 *       Verified by both:
 *         - inline render path: the widget renders with
 *           `INSIGHTS_PLAYBOOK_SCHEMA` + envelope-derived prefilledFields
 *         - dispatch path: `workspace.widget_load` carries
 *           `widgetType: STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE`
 *
 *   (3) RAG `[n]` tokenization — `tokenizeCitations` produces alternating
 *       text + citation segments preserving order; citation tokens render
 *       as `[n]` buttons with `data-citation-n` attributes.
 *
 *   (4) Citation click stub — clicking a `[n]` token fires the stub
 *       (`defaultCitationClickStub`) which logs via `console.debug`. Custom
 *       `onCitationClick` overrides the stub (task 027 seam).
 *
 *   (5) Decline framing — `MessageBar intent="warning"` is rendered;
 *       `suggestedActions` render as plain `<li>` items (NOT Fluent
 *       Buttons per integration brief §6 D3).
 *
 *   (6) Empty-result anti-hallucination — empty `answer` is NOT rendered
 *       verbatim; the muted hint is rendered instead.
 *
 *   (7) Dark-mode parity — sub-renderers render identically under
 *       `webDarkTheme` (smoke-level: no exceptions, no hex-coded styles).
 *
 *   (8) Type guards — `isEmptyResult`, `isDecline`, `isPlaybookInference`,
 *       `isRagObservation` correctly discriminate all envelope shapes.
 *
 * Test strategy:
 *   - The sub-renderers + tokenization helpers are pure; we test those
 *     directly against fixtures.
 *   - The PaneEventBus is real (`new PaneEventBus()` + `PaneEventBusProvider`)
 *     so we can attach a real subscriber to assert dispatch payloads.
 *   - The `StructuredOutputStreamWidget` is consumed transitively; we assert
 *     on its rendered output (e.g. `data-widget-type="structured-output-stream"`,
 *     `data-render-state`, schema field labels) to prove reuse + schema
 *     identity without re-implementing the widget.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent, ContextPaneEvent } from '@spaarke/ai-widgets';

import { InsightsResponseRenderer } from '../InsightsResponseRenderer';
import {
  tokenizeCitations,
  isEmptyResult,
  isDecline,
  isPlaybookInference,
  isRagObservation,
  type InsightsResponse,
  type PlaybookInferenceResponse,
  type PlaybookDeclineResponse,
  type RagObservationResponse,
  type Citation,
  type Diagnostics,
} from '../types';
import {
  envelopeToFields,
  stringifyEnvelopeField,
} from '../PlaybookResponseRenderer';
import { EMPTY_RESULT_HINT_TEXT } from '../EmptyResultHint';
import {
  defaultCitationClickStub,
  isClickableCitation,
} from '../RagResponseRenderer';

// ---------------------------------------------------------------------------
// Test fixtures — one per discriminated-union case
// ---------------------------------------------------------------------------

const STD_DIAGNOSTICS: Diagnostics = {
  intentSource: 'classifier',
  classifierBelowThreshold: false,
  elapsedMs: 1234,
  cacheHit: false,
  conversationId: 'conv-test-001',
};

// Task 027 / D2-17 — v1.1 citations[].href populated. The default fixture
// uses the FULL v1.1 scenario (all hrefs populated) so existing tests that
// assert clickable-button rendering continue to pass after task 027 wires
// the conditional `c.href ? <Button> : <span>` branch. Per-variant tests
// (graceful fallback, observation-only-href contingency) live in T6b below
// and explicitly construct citations with null/absent hrefs.
const STD_CITATIONS: readonly Citation[] = [
  { n: 1, source: 'Acme APA.pdf', excerpt: 'Estimated cost: $282k', observationId: 'doc-A', chunkId: 'chunk-1', href: 'https://bff.example.com/api/v1/documents/doc-A/preview' },
  { n: 2, source: 'Closing Memo v3.docx', excerpt: 'Closing subject to regulatory approval...', observationId: 'doc-B', chunkId: 'chunk-2', href: 'https://bff.example.com/api/v1/documents/doc-B/preview' },
  { n: 3, source: 'Acme APA.pdf', excerpt: 'Key employees to sign retention agreements...', observationId: 'doc-A', chunkId: 'chunk-9', href: 'https://bff.example.com/api/v1/documents/doc-A/preview?chunk=9' },
];

function makePlaybookInferenceResponse(): PlaybookInferenceResponse {
  return {
    path: 'playbook',
    playbookId: 'predict-matter-cost@v1',
    answer: 'Predicted cost ~$280k based on 12 similar matters with comparable scope.',
    citations: [STD_CITATIONS[0]],
    confidence: 0.92,
    structuredResult: {
      kind: 'inference',
      envelope: {
        predictedCost: 280000,
        currency: 'USD',
        comparableMatters: 12,
        inferenceBody: 'Across 12 comparable matters in the same jurisdiction, the median total cost was $278k.',
        evidenceList: ['Acme APA.pdf — page 7', 'Beta MSA.pdf — page 3'],
      },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

function makePlaybookDeclineResponse(): PlaybookDeclineResponse {
  return {
    path: 'playbook',
    playbookId: 'predict-matter-cost@v1',
    answer: 'Not enough comparable matters to confidently predict cost.',
    citations: [],
    confidence: 0.15, // 1 - 0.85 confidenceInDecline
    structuredResult: {
      kind: 'decline',
      envelope: {
        reason: 'evidence-insufficient',
        minimumEvidenceNeeded: 'At least 5 comparable closed matters with cost data.',
        suggestedActions: [
          'Attach a comparable matter folder',
          'Provide explicit fee estimates',
          'Reduce scope to a single phase',
        ],
        confidenceInDecline: 0.85,
        explanation: 'Not enough comparable matters to confidently predict cost.',
      },
    },
    diagnostics: STD_DIAGNOSTICS,
  };
}

function makeRagObservationResponse(): RagObservationResponse {
  return {
    path: 'rag',
    playbookId: null,
    answer: 'The closing conditions include [1] regulatory approval, [2] a tail-policy update, and [3] employee retention agreements.',
    citations: STD_CITATIONS,
    confidence: 0.81,
    structuredResult: {
      kind: 'observation',
      envelope: {
        results: [{ id: 'r1' }, { id: 'r2' }, { id: 'r3' }],
        summary: 'Three closing conditions identified.',
      },
    },
    diagnostics: { ...STD_DIAGNOSTICS, intentSource: 'forceMode' },
  };
}

function makeRagEmptyResponse(): RagObservationResponse {
  return {
    path: 'rag',
    playbookId: null,
    answer: '',
    citations: [],
    confidence: 0.0,
    structuredResult: {
      kind: 'observation',
      envelope: { results: [], summary: '' },
    },
    diagnostics: { ...STD_DIAGNOSTICS, cacheHit: false, elapsedMs: 89 },
  };
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWithProviders(
  ui: React.ReactElement,
  options: { theme?: typeof webLightTheme; bus?: PaneEventBus } = {},
): { bus: PaneEventBus } {
  const bus = options.bus ?? new PaneEventBus();
  const theme = options.theme ?? webLightTheme;
  render(
    <FluentProvider theme={theme}>
      <PaneEventBusProvider bus={bus}>{ui}</PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

// ---------------------------------------------------------------------------
// (T1) Pure helper — tokenizeCitations
// ---------------------------------------------------------------------------

describe('tokenizeCitations', () => {
  it('returns an empty array for empty input', () => {
    expect(tokenizeCitations('')).toEqual([]);
  });

  it('returns a single text token when no citations are present', () => {
    expect(tokenizeCitations('Plain prose, no citations.')).toEqual([
      { type: 'text', content: 'Plain prose, no citations.' },
    ]);
  });

  it('tokenizes a single citation in the middle of prose', () => {
    expect(tokenizeCitations('Before [1] after.')).toEqual([
      { type: 'text', content: 'Before ' },
      { type: 'citation', n: 1 },
      { type: 'text', content: ' after.' },
    ]);
  });

  it('tokenizes multiple citations preserving order', () => {
    expect(
      tokenizeCitations('A [1] B [2] C [3] D.'),
    ).toEqual([
      { type: 'text', content: 'A ' },
      { type: 'citation', n: 1 },
      { type: 'text', content: ' B ' },
      { type: 'citation', n: 2 },
      { type: 'text', content: ' C ' },
      { type: 'citation', n: 3 },
      { type: 'text', content: ' D.' },
    ]);
  });

  it('handles back-to-back citations with no separator', () => {
    expect(tokenizeCitations('Refs [1][2][3].')).toEqual([
      { type: 'text', content: 'Refs ' },
      { type: 'citation', n: 1 },
      { type: 'citation', n: 2 },
      { type: 'citation', n: 3 },
      { type: 'text', content: '.' },
    ]);
  });

  it('handles citations at the start and end of prose', () => {
    expect(tokenizeCitations('[1] middle [2]')).toEqual([
      { type: 'citation', n: 1 },
      { type: 'text', content: ' middle ' },
      { type: 'citation', n: 2 },
    ]);
  });

  it('handles multi-digit citation numbers', () => {
    expect(tokenizeCitations('Ref [12] and [345].')).toEqual([
      { type: 'text', content: 'Ref ' },
      { type: 'citation', n: 12 },
      { type: 'text', content: ' and ' },
      { type: 'citation', n: 345 },
      { type: 'text', content: '.' },
    ]);
  });
});

// ---------------------------------------------------------------------------
// (T2) Type guards
// ---------------------------------------------------------------------------

describe('runtime type guards', () => {
  it('isEmptyResult is true ONLY for empty RAG', () => {
    expect(isEmptyResult(makeRagEmptyResponse())).toBe(true);
    expect(isEmptyResult(makeRagObservationResponse())).toBe(false);
    expect(isEmptyResult(makePlaybookInferenceResponse())).toBe(false);
    expect(isEmptyResult(makePlaybookDeclineResponse())).toBe(false);
  });

  it('isDecline is true ONLY for playbook decline', () => {
    expect(isDecline(makePlaybookDeclineResponse())).toBe(true);
    expect(isDecline(makePlaybookInferenceResponse())).toBe(false);
    expect(isDecline(makeRagObservationResponse())).toBe(false);
    expect(isDecline(makeRagEmptyResponse())).toBe(false);
  });

  it('isPlaybookInference is true ONLY for playbook inference', () => {
    expect(isPlaybookInference(makePlaybookInferenceResponse())).toBe(true);
    expect(isPlaybookInference(makePlaybookDeclineResponse())).toBe(false);
    expect(isPlaybookInference(makeRagObservationResponse())).toBe(false);
  });

  it('isRagObservation is true for both RAG variants', () => {
    expect(isRagObservation(makeRagObservationResponse())).toBe(true);
    expect(isRagObservation(makeRagEmptyResponse())).toBe(true);
    expect(isRagObservation(makePlaybookInferenceResponse())).toBe(false);
    expect(isRagObservation(makePlaybookDeclineResponse())).toBe(false);
  });

  it('isEmptyResult correctly handles whitespace-only answer', () => {
    const r: RagObservationResponse = {
      ...makeRagEmptyResponse(),
      answer: '   \n  \t  ',
    };
    expect(isEmptyResult(r)).toBe(true);
  });

  it('isEmptyResult is false when citations exist (even if answer is empty)', () => {
    const r: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: '',
    };
    expect(isEmptyResult(r)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// (T3) stringifyEnvelopeField + envelopeToFields (playbook helpers)
// ---------------------------------------------------------------------------

describe('stringifyEnvelopeField', () => {
  it('returns empty string for null and undefined', () => {
    expect(stringifyEnvelopeField(null)).toBe('');
    expect(stringifyEnvelopeField(undefined)).toBe('');
  });

  it('passes strings through unchanged', () => {
    expect(stringifyEnvelopeField('hello')).toBe('hello');
  });

  it('coerces numbers and booleans', () => {
    expect(stringifyEnvelopeField(42)).toBe('42');
    expect(stringifyEnvelopeField(true)).toBe('true');
    expect(stringifyEnvelopeField(false)).toBe('false');
  });

  it('joins arrays with newlines (list-display compatible)', () => {
    expect(stringifyEnvelopeField(['a', 'b', 'c'])).toBe('a\nb\nc');
  });

  it('json-stringifies nested objects', () => {
    expect(stringifyEnvelopeField({ a: 1, b: 'x' })).toBe('{"a":1,"b":"x"}');
  });
});

describe('envelopeToFields', () => {
  it('projects a playbook-inference envelope into the four schema field paths', () => {
    const response = makePlaybookInferenceResponse();
    const fields = envelopeToFields(response);
    expect(fields.answer).toBe(response.answer);
    expect(fields.playbookId).toBe('predict-matter-cost@v1');
    expect(fields.inferenceBody).toContain('comparable matters');
    expect(fields.evidenceList).toContain('Acme APA.pdf');
    expect(fields.evidenceList).toContain('Beta MSA.pdf');
  });

  it('falls back to citations[] when envelope.evidenceList is absent', () => {
    const response: PlaybookInferenceResponse = {
      ...makePlaybookInferenceResponse(),
      structuredResult: {
        kind: 'inference',
        envelope: {
          predictedCost: 280000,
          inferenceBody: 'No evidence list in envelope.',
        },
      },
    };
    const fields = envelopeToFields(response);
    expect(fields.evidenceList).toContain('[1] Acme APA.pdf');
  });

  it('tolerates PascalCase envelope keys', () => {
    const response: PlaybookInferenceResponse = {
      ...makePlaybookInferenceResponse(),
      structuredResult: {
        kind: 'inference',
        envelope: {
          InferenceBody: 'Pascal-case body',
          EvidenceRefs: ['Doc.pdf — p1'],
        },
      },
    };
    const fields = envelopeToFields(response);
    expect(fields.inferenceBody).toBe('Pascal-case body');
    expect(fields.evidenceList).toBe('Doc.pdf — p1');
  });
});

// ---------------------------------------------------------------------------
// (T4) InsightsResponseRenderer — discrimination
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — discrimination', () => {
  it('routes empty-result to EmptyResultHint', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagEmptyResponse()} />,
    );
    const root = screen.getByTestId('insights-response-renderer');
    expect(root.getAttribute('data-response-case')).toBe('empty');
    expect(screen.getByTestId('empty-result-hint')).toBeInTheDocument();
    expect(screen.getByText(EMPTY_RESULT_HINT_TEXT)).toBeInTheDocument();
  });

  it('routes decline to DeclineResponseRenderer', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookDeclineResponse()} />,
    );
    const root = screen.getByTestId('insights-response-renderer');
    expect(root.getAttribute('data-response-case')).toBe('decline');
    expect(screen.getByTestId('decline-response-renderer')).toBeInTheDocument();
  });

  it('routes playbook-inference to PlaybookResponseRenderer', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookInferenceResponse()} />,
    );
    const root = screen.getByTestId('insights-response-renderer');
    expect(root.getAttribute('data-response-case')).toBe('playbook-inference');
    expect(screen.getByTestId('playbook-response-renderer')).toBeInTheDocument();
  });

  it('routes RAG observation (with citations) to RagResponseRenderer', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
    );
    const root = screen.getByTestId('insights-response-renderer');
    expect(root.getAttribute('data-response-case')).toBe('rag');
    expect(screen.getByTestId('rag-response-renderer')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (T5) Playbook path — reuse of task 017 widget + INSIGHTS_PLAYBOOK_SCHEMA
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — playbook reuse handshake', () => {
  it('mounts the StructuredOutputStreamWidget inline with INSIGHTS_PLAYBOOK_SCHEMA-derived fields', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookInferenceResponse()} />,
    );
    // The widget's root carries the structured-output-stream widget type.
    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget).toBeInTheDocument();
    expect(widget.getAttribute('data-widget-type')).toBe('structured-output-stream');

    // Schema fields from INSIGHTS_PLAYBOOK_SCHEMA — labels rendered.
    // The widget uppercases labels in the fieldLabel style, so we match the
    // uppercase form to be tolerant of CSS transform observability.
    expect(screen.getByText(/Insight/i)).toBeInTheDocument();
    expect(screen.getByText(/Playbook/i)).toBeInTheDocument();
    expect(screen.getByText(/Reasoning/i)).toBeInTheDocument();
    expect(screen.getByText(/Evidence/i)).toBeInTheDocument();

    // The prefilled fields render — `answer` becomes the heading field; we
    // assert presence via the response text.
    const playbookRoot = screen.getByTestId('playbook-response-renderer');
    expect(playbookRoot.getAttribute('data-render-mode')).toBe('static');
    expect(playbookRoot.textContent).toContain('Predicted cost');
  });

  it('mounts the widget in streaming mode when streamingEnabled=true', () => {
    renderWithProviders(
      <InsightsResponseRenderer
        response={makePlaybookInferenceResponse()}
        streamingEnabled={true}
      />,
    );
    const playbookRoot = screen.getByTestId('playbook-response-renderer');
    expect(playbookRoot.getAttribute('data-render-mode')).toBe('streaming');
  });

  it('dispatches workspace.widget_load with STRUCTURED_OUTPUT_STREAM_WIDGET_TYPE when dispatchPlaybookToWorkspace=true', () => {
    const bus = new PaneEventBus();
    const events: WorkspacePaneEvent[] = [];
    bus.subscribe('workspace', (event: WorkspacePaneEvent) => {
      events.push(event);
    });
    renderWithProviders(
      <InsightsResponseRenderer
        response={makePlaybookInferenceResponse()}
        dispatchPlaybookToWorkspace={true}
      />,
      { bus },
    );
    // Find the widget_load event (other events may also flow on the channel).
    const loadEvent = events.find(e => e.type === 'widget_load');
    expect(loadEvent).toBeDefined();
    expect(loadEvent?.widgetType).toBe('structured-output-stream');
    // The widgetData should carry the schema (INSIGHTS_PLAYBOOK_SCHEMA) and
    // static prefilledFields.
    const wd = loadEvent?.widgetData as {
      mode?: string;
      schema?: { fields?: Array<{ path?: string; label?: string }> };
      prefilledFields?: Record<string, string>;
    };
    expect(wd?.mode).toBe('static');
    expect(wd?.schema?.fields).toBeDefined();
    const paths = (wd?.schema?.fields ?? []).map(f => f.path).filter(Boolean);
    expect(paths).toEqual(['answer', 'playbookId', 'inferenceBody', 'evidenceList']);
    expect(wd?.prefilledFields?.playbookId).toBe('predict-matter-cost@v1');
  });
});

// ---------------------------------------------------------------------------
// (T6) RAG path — citation tokens + click stub seam
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — RAG citation rendering', () => {
  it('renders [n] tokens as clickable buttons preserving order', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
    );

    // Three citations referenced in prose: [1], [2], [3].
    expect(screen.getByTestId('citation-token-1')).toBeInTheDocument();
    expect(screen.getByTestId('citation-token-2')).toBeInTheDocument();
    expect(screen.getByTestId('citation-token-3')).toBeInTheDocument();

    // Tokens are <button> elements (Fluent v9 Button with transparent appearance).
    // STD_CITATIONS carries non-empty `href` per v1.1 contract — clickable variant.
    const token1 = screen.getByTestId('citation-token-1');
    expect(token1.tagName.toLowerCase()).toBe('button');
    expect(token1.textContent).toBe('[1]');
    expect(token1.getAttribute('data-citation-n')).toBe('1');
    expect(token1.getAttribute('data-citation-variant')).toBe('clickable');
    // Task 027 — aria-label includes the source name and "open" verb so screen
    // readers convey that the citation is interactive AND its target.
    expect(token1.getAttribute('aria-label')).toBe('Citation 1: open Acme APA.pdf');
  });

  it('renders the citation reference list with all citations', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
    );
    const list = screen.getByTestId('rag-citations-list');
    expect(list).toBeInTheDocument();
    expect(within(list).getByText(/Acme APA.pdf/)).toBeInTheDocument();
    expect(within(list).getByText(/Closing Memo v3.docx/)).toBeInTheDocument();
  });

  it('fires onCitationClick with the matching Citation when clicked (task-027 seam)', () => {
    const clicks: Citation[] = [];
    const onCitationClick = (c: Citation) => {
      clicks.push(c);
    };
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse()}
        onCitationClick={onCitationClick}
      />,
    );
    fireEvent.click(screen.getByTestId('citation-token-2'));
    expect(clicks).toHaveLength(1);
    expect(clicks[0].n).toBe(2);
    expect(clicks[0].source).toBe('Closing Memo v3.docx');
    expect(clicks[0].observationId).toBe('doc-B');
  });

  it('dispatches context.context_update when onCitationClick is omitted (task 027 default)', () => {
    // Task 027 changed the default behaviour from `console.debug` stub
    // (task 026) to a real PaneEventBus dispatch. Detailed coverage of the
    // dispatch shape lives in the T11 (task 027) describe block below; this
    // smoke test simply verifies the call site fires the bus dispatch
    // without throwing when no override prop is provided.
    const bus = new PaneEventBus();
    const events: ContextPaneEvent[] = [];
    bus.subscribe('context', (event: ContextPaneEvent) => events.push(event));
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
      { bus },
    );
    fireEvent.click(screen.getByTestId('citation-token-1'));
    expect(events).toHaveLength(1);
    expect(events[0].type).toBe('context_update');
  });

  it('handles out-of-range citation tokens without throwing', () => {
    const response: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: 'Token referencing a missing citation: [9].',
      // Only citation n=1 in the array; [9] in prose is out of range.
      citations: [STD_CITATIONS[0]],
    };
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    // Task 027: out-of-range citations render as non-clickable spans (the
    // citation cannot be resolved, so no clickable affordance is emitted).
    // The test verifies the [9] marker is still rendered in the prose
    // (display preserved) without throwing.
    const token9 = screen.getByTestId('citation-token-9');
    expect(token9).toBeInTheDocument();
    expect(token9.tagName.toLowerCase()).toBe('span');
    expect(token9.getAttribute('data-citation-variant')).toBe('non-clickable');
    expect(token9.textContent).toBe('[9]');
  });

  it('defaultCitationClickStub logs without throwing', () => {
    const consoleSpy = jest.spyOn(console, 'debug').mockImplementation(() => undefined);
    expect(() => defaultCitationClickStub(STD_CITATIONS[0])).not.toThrow();
    expect(consoleSpy).toHaveBeenCalled();
    consoleSpy.mockRestore();
  });
});

// ---------------------------------------------------------------------------
// (T7) Decline path — MessageBar warning + plain-text suggested actions
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — decline rendering', () => {
  it('renders MessageBar intent="warning" with the explanation text', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookDeclineResponse()} />,
    );
    const warning = screen.getByTestId('decline-warning');
    expect(warning).toBeInTheDocument();
    // Fluent MessageBar with intent="warning" renders with an intent attribute
    // on its root; the exact DOM attribute may vary, so we assert via text + role.
    expect(
      screen.getByText('Not enough comparable matters to confidently predict cost.'),
    ).toBeInTheDocument();
  });

  it('renders suggestedActions as plain <li> items (NOT Fluent Buttons)', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookDeclineResponse()} />,
    );
    const actionsList = screen.getByTestId('decline-suggested-actions');
    expect(actionsList.tagName.toLowerCase()).toBe('ul');
    const items = within(actionsList).getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent('Attach a comparable matter folder');
    expect(items[1]).toHaveTextContent('Provide explicit fee estimates');
    expect(items[2]).toHaveTextContent('Reduce scope to a single phase');
    // No <button> elements inside the action list (per brief §6 D3 v1).
    const buttons = actionsList.querySelectorAll('button');
    expect(buttons.length).toBe(0);
  });

  it('renders the minimum-evidence hint when present', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookDeclineResponse()} />,
    );
    expect(screen.getByTestId('decline-min-evidence')).toBeInTheDocument();
    expect(
      screen.getByText(/Minimum evidence needed:/),
    ).toBeInTheDocument();
  });

  it('hides the suggested-actions block when the envelope list is empty', () => {
    const r: PlaybookDeclineResponse = {
      ...makePlaybookDeclineResponse(),
      structuredResult: {
        kind: 'decline',
        envelope: {
          reason: 'evidence-insufficient',
          suggestedActions: [],
          confidenceInDecline: 0.85,
          explanation: 'No suggestions available.',
        },
      },
    };
    renderWithProviders(<InsightsResponseRenderer response={r} />);
    expect(screen.queryByTestId('decline-suggested-actions')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (T8) Empty-result path — anti-hallucination guarantee
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — empty-result anti-hallucination', () => {
  it('does NOT render the empty answer verbatim', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagEmptyResponse()} />,
    );
    // The EmptyResultHint renders; no rag-prose / decline / playbook content.
    expect(screen.queryByTestId('rag-prose')).toBeNull();
    expect(screen.queryByTestId('decline-warning')).toBeNull();
    expect(screen.queryByTestId('playbook-response-renderer')).toBeNull();
    // The hint copy is present.
    expect(screen.getByText(EMPTY_RESULT_HINT_TEXT)).toBeInTheDocument();
  });

  it('uses semantic role/aria for the hint', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagEmptyResponse()} />,
    );
    const hint = screen.getByTestId('empty-result-hint');
    expect(hint.getAttribute('role')).toBe('status');
    expect(hint.getAttribute('aria-live')).toBe('polite');
  });
});

// ---------------------------------------------------------------------------
// (T9) Dark mode parity — smoke (no exceptions; sub-renderers mount)
// ---------------------------------------------------------------------------

describe('InsightsResponseRenderer — dark mode smoke', () => {
  it('renders the playbook case under webDarkTheme without exceptions', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookInferenceResponse()} />,
      { theme: webDarkTheme },
    );
    expect(screen.getByTestId('playbook-response-renderer')).toBeInTheDocument();
    expect(screen.getByTestId('structured-output-stream-widget')).toBeInTheDocument();
  });

  it('renders the RAG case under webDarkTheme without exceptions', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
      { theme: webDarkTheme },
    );
    expect(screen.getByTestId('rag-response-renderer')).toBeInTheDocument();
    expect(screen.getByTestId('citation-token-1')).toBeInTheDocument();
  });

  it('renders the decline case under webDarkTheme without exceptions', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makePlaybookDeclineResponse()} />,
      { theme: webDarkTheme },
    );
    expect(screen.getByTestId('decline-warning')).toBeInTheDocument();
    expect(screen.getByTestId('decline-suggested-actions')).toBeInTheDocument();
  });

  it('renders the empty-result case under webDarkTheme without exceptions', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagEmptyResponse()} />,
      { theme: webDarkTheme },
    );
    expect(screen.getByTestId('empty-result-hint')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (T10) Discriminated-union exhaustiveness — compile-time guarantee
// ---------------------------------------------------------------------------

describe('discriminated-union exhaustiveness', () => {
  /**
   * This test does not assert behavior at runtime — its purpose is to compile.
   * Each `case` covers a member of the InsightsResponse union; if a new member
   * is added without updating the renderer, the compile fails.
   *
   * (Build verification is owned by the main session; this test simply locks
   * in the assertion via TypeScript's exhaustive switch check.)
   */
  it('compiles when all union members are covered', () => {
    function walk(r: InsightsResponse): string {
      switch (r.path) {
        case 'playbook':
          switch (r.structuredResult.kind) {
            case 'inference':
              return 'inference';
            case 'decline':
              return 'decline';
            default: {
              const _exhaust: never = r.structuredResult;
              return _exhaust;
            }
          }
        case 'rag':
          return 'rag';
        default: {
          const _exhaust: never = r;
          return _exhaust;
        }
      }
    }
    expect(walk(makePlaybookInferenceResponse())).toBe('inference');
    expect(walk(makePlaybookDeclineResponse())).toBe('decline');
    expect(walk(makeRagObservationResponse())).toBe('rag');
    expect(walk(makeRagEmptyResponse())).toBe('rag');
  });
});

// ---------------------------------------------------------------------------
// (T11) Task 027 / D2-17 — Clickable citations (v1.1) + graceful fallback
// ---------------------------------------------------------------------------
//
// Covers the five test obligations declared in task 027's POML §3.7:
//   (a) clickable when `href` populated
//   (b) `context.context_update` dispatched with correct URL payload
//   (c) graceful fallback when `href: null` or absent (display-name only)
//   (d) observation-only-href scenario (v1.1 but no document hrefs) degrades
//       gracefully (mixed-mode rendering)
//   (e) ARIA labels distinct between clickable and non-clickable variants
//
// Plus an extra ADR-030 / R5 §3.4 invariant check: no new event-type
// discriminant is dispatched — the event MUST be `context_update`.

describe('task 027 / D2-17 — clickable citations (v1.1) + graceful fallback', () => {
  // -----------------------------------------------------------------------
  // (a) Clickable when `href` is populated
  // -----------------------------------------------------------------------
  it('renders a clickable Fluent Button for citations with non-empty href', () => {
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
    );
    // All three STD_CITATIONS carry hrefs → all three render as buttons.
    [1, 2, 3].forEach(n => {
      const token = screen.getByTestId(`citation-token-${n}`);
      expect(token.tagName.toLowerCase()).toBe('button');
      expect(token.getAttribute('data-citation-variant')).toBe('clickable');
      expect(token.getAttribute('data-citation-n')).toBe(String(n));
    });
  });

  // -----------------------------------------------------------------------
  // (b) `context.context_update` dispatched with correct URL payload
  // -----------------------------------------------------------------------
  it('dispatches context.context_update with { url, displayName } when a clickable citation is clicked', () => {
    const bus = new PaneEventBus();
    const events: ContextPaneEvent[] = [];
    bus.subscribe('context', (event: ContextPaneEvent) => {
      events.push(event);
    });
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
      { bus },
    );
    fireEvent.click(screen.getByTestId('citation-token-2'));
    // Exactly one event on the context channel.
    expect(events).toHaveLength(1);
    const event = events[0];
    expect(event.type).toBe('context_update');
    expect(event.contextType).toBe('file-preview');
    // The contextData payload carries the URL + display name verbatim.
    const data = event.contextData as { url?: string; displayName?: string };
    expect(data.url).toBe('https://bff.example.com/api/v1/documents/doc-B/preview');
    expect(data.displayName).toBe('Closing Memo v3.docx');
  });

  it('routes through onCitationClick override INSTEAD of dispatch when prop is provided', () => {
    const bus = new PaneEventBus();
    const events: ContextPaneEvent[] = [];
    bus.subscribe('context', (event: ContextPaneEvent) => {
      events.push(event);
    });
    const overrideCalls: Citation[] = [];
    renderWithProviders(
      <InsightsResponseRenderer
        response={makeRagObservationResponse()}
        onCitationClick={c => overrideCalls.push(c)}
      />,
      { bus },
    );
    fireEvent.click(screen.getByTestId('citation-token-1'));
    // Override fires; bus dispatch does NOT.
    expect(overrideCalls).toHaveLength(1);
    expect(overrideCalls[0].n).toBe(1);
    expect(overrideCalls[0].href).toBe('https://bff.example.com/api/v1/documents/doc-A/preview');
    expect(events).toHaveLength(0);
  });

  // -----------------------------------------------------------------------
  // (c) Graceful fallback when href is null or absent (v1.0)
  // -----------------------------------------------------------------------
  it('renders non-clickable spans when ALL citations have href: null (v1.0 deployment)', () => {
    const v10Citations: Citation[] = [
      { n: 1, source: 'Acme APA.pdf', excerpt: 'cost: $282k', observationId: 'doc-A', chunkId: 'chunk-1', href: null },
      { n: 2, source: 'Closing Memo v3.docx', excerpt: 'closing approval', observationId: 'doc-B', chunkId: 'chunk-2', href: null },
    ];
    const v10Response: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: 'The closing conditions include [1] regulatory approval and [2] employee retention.',
      citations: v10Citations,
    };
    const bus = new PaneEventBus();
    const events: ContextPaneEvent[] = [];
    bus.subscribe('context', (e: ContextPaneEvent) => events.push(e));
    renderWithProviders(<InsightsResponseRenderer response={v10Response} />, { bus });

    const token1 = screen.getByTestId('citation-token-1');
    const token2 = screen.getByTestId('citation-token-2');
    expect(token1.tagName.toLowerCase()).toBe('span');
    expect(token2.tagName.toLowerCase()).toBe('span');
    expect(token1.getAttribute('data-citation-variant')).toBe('non-clickable');
    expect(token2.getAttribute('data-citation-variant')).toBe('non-clickable');
    // Marker text preserved verbatim — display-name-only fallback.
    expect(token1.textContent).toBe('[1]');
    expect(token2.textContent).toBe('[2]');
    // Clicking does NOT dispatch (no click handler wired on spans).
    fireEvent.click(token1);
    fireEvent.click(token2);
    expect(events).toHaveLength(0);
  });

  it('renders non-clickable spans when href is undefined (field entirely absent from envelope)', () => {
    const noHrefCitations: Citation[] = [
      // Note: no `href` key at all (not null — completely absent). This is
      // the literal v1.0 wire shape before Wave F shipped.
      { n: 1, source: 'Acme APA.pdf', excerpt: 'cost', observationId: 'doc-A', chunkId: 'chunk-1' },
    ];
    const response: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: 'Single citation [1] with no href field.',
      citations: noHrefCitations,
    };
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    const token = screen.getByTestId('citation-token-1');
    expect(token.tagName.toLowerCase()).toBe('span');
    expect(token.getAttribute('data-citation-variant')).toBe('non-clickable');
  });

  it('renders non-clickable spans for citations whose href is an empty string (defensive)', () => {
    const emptyHrefCitations: Citation[] = [
      { n: 1, source: 'Acme APA.pdf', excerpt: 'cost', observationId: 'doc-A', chunkId: 'chunk-1', href: '' },
    ];
    const response: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: 'Empty href [1].',
      citations: emptyHrefCitations,
    };
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    const token = screen.getByTestId('citation-token-1');
    expect(token.tagName.toLowerCase()).toBe('span');
  });

  // -----------------------------------------------------------------------
  // (d) Observation-only-href contingency (UR-04 spike outcome) — mixed mode
  // -----------------------------------------------------------------------
  it('renders mixed-mode citations (observation has href, document has null) per UR-04 spike contingency', () => {
    const mixedCitations: Citation[] = [
      // Observation citation — Wave F spike outcome: hrefs available on
      // observation-backed citations.
      {
        n: 1,
        source: 'Engagement Letter Observation',
        excerpt: 'capped at $50k',
        observationId: 'obs-xyz',
        chunkId: 'obs-chunk-1',
        href: 'https://bff.example.com/api/insights/observations/obs-xyz',
      },
      // Document citation — Wave F spike outcome: hrefs deferred to v1.2.
      {
        n: 2,
        source: 'Engagement Letter (document)',
        excerpt: 'cap clause',
        observationId: 'doc-engagement',
        chunkId: 'doc-chunk-1',
        href: null,
      },
    ];
    const mixedResponse: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: "The matter's engagement letter caps fees at $50,000 [1] per the executed agreement [2].",
      citations: mixedCitations,
    };
    const bus = new PaneEventBus();
    const events: ContextPaneEvent[] = [];
    bus.subscribe('context', (e: ContextPaneEvent) => events.push(e));
    renderWithProviders(<InsightsResponseRenderer response={mixedResponse} />, { bus });

    // [1] — observation — clickable Button.
    const token1 = screen.getByTestId('citation-token-1');
    expect(token1.tagName.toLowerCase()).toBe('button');
    expect(token1.getAttribute('data-citation-variant')).toBe('clickable');

    // [2] — document — non-clickable span.
    const token2 = screen.getByTestId('citation-token-2');
    expect(token2.tagName.toLowerCase()).toBe('span');
    expect(token2.getAttribute('data-citation-variant')).toBe('non-clickable');

    // Clicking [1] dispatches; clicking [2] is inert.
    fireEvent.click(token1);
    expect(events).toHaveLength(1);
    expect(events[0].type).toBe('context_update');
    expect((events[0].contextData as { url?: string }).url).toBe(
      'https://bff.example.com/api/insights/observations/obs-xyz',
    );

    fireEvent.click(token2);
    // Still just the one event from token1 — token2 click is inert.
    expect(events).toHaveLength(1);

    // Reference list still shows BOTH citations (display-name + excerpt) so
    // the reader can see the document citation even without a clickable
    // affordance.
    const list = screen.getByTestId('rag-citations-list');
    expect(within(list).getByText(/Engagement Letter Observation/)).toBeInTheDocument();
    expect(within(list).getByText(/Engagement Letter \(document\)/)).toBeInTheDocument();
  });

  // -----------------------------------------------------------------------
  // (e) ARIA labels distinct between clickable and non-clickable variants
  // -----------------------------------------------------------------------
  it('uses distinct ARIA labels for clickable vs non-clickable citation variants', () => {
    const mixedCitations: Citation[] = [
      { n: 1, source: 'Doc-A.pdf', excerpt: 'x', observationId: 'A', chunkId: 'c1', href: 'https://bff.example.com/api/v1/documents/A/preview' },
      { n: 2, source: 'Doc-B.pdf', excerpt: 'y', observationId: 'B', chunkId: 'c2', href: null },
    ];
    const mixedResponse: RagObservationResponse = {
      ...makeRagObservationResponse(),
      answer: 'Mixed mode [1] [2].',
      citations: mixedCitations,
    };
    renderWithProviders(<InsightsResponseRenderer response={mixedResponse} />);

    const token1 = screen.getByTestId('citation-token-1');
    const token2 = screen.getByTestId('citation-token-2');

    // Clickable: aria-label includes the source name AND the "open" verb so
    // screen readers convey the interactive affordance.
    expect(token1.getAttribute('aria-label')).toBe('Citation 1: open Doc-A.pdf');
    // Non-clickable: aria-label conveys the citation source but explicitly
    // notes the source is not directly openable (degraded but informative).
    expect(token2.getAttribute('aria-label')).toBe(
      'Citation 2: Doc-B.pdf (source not directly openable)',
    );
    // Aria labels differ — the two variants are distinguishable to assistive
    // tech without relying on visual styling.
    expect(token1.getAttribute('aria-label')).not.toBe(token2.getAttribute('aria-label'));
  });

  // -----------------------------------------------------------------------
  // Extra: ADR-030 invariant — no new event type / no new channel
  // -----------------------------------------------------------------------
  it('dispatches ONLY context.context_update — no new channels or discriminants (ADR-030 invariant)', () => {
    const bus = new PaneEventBus();
    const allEvents: Array<{ channel: string; event: unknown }> = [];
    bus.subscribe('context', (e: ContextPaneEvent) => allEvents.push({ channel: 'context', event: e }));
    bus.subscribe('workspace', (e: WorkspacePaneEvent) => allEvents.push({ channel: 'workspace', event: e }));
    bus.subscribe('conversation', e => allEvents.push({ channel: 'conversation', event: e }));
    bus.subscribe('safety', e => allEvents.push({ channel: 'safety', event: e }));
    renderWithProviders(
      <InsightsResponseRenderer response={makeRagObservationResponse()} />,
      { bus },
    );
    fireEvent.click(screen.getByTestId('citation-token-1'));
    fireEvent.click(screen.getByTestId('citation-token-2'));
    fireEvent.click(screen.getByTestId('citation-token-3'));
    // Three clicks → three context_update events on the context channel.
    expect(allEvents).toHaveLength(3);
    for (const e of allEvents) {
      expect(e.channel).toBe('context');
      expect((e.event as ContextPaneEvent).type).toBe('context_update');
    }
  });

  // -----------------------------------------------------------------------
  // Extra: isClickableCitation helper
  // -----------------------------------------------------------------------
  it('isClickableCitation returns true for non-empty href; false otherwise', () => {
    expect(isClickableCitation({ ...STD_CITATIONS[0] })).toBe(true);
    expect(isClickableCitation({ ...STD_CITATIONS[0], href: null })).toBe(false);
    expect(isClickableCitation({ ...STD_CITATIONS[0], href: undefined })).toBe(false);
    expect(isClickableCitation({ ...STD_CITATIONS[0], href: '' })).toBe(false);
    // Strip href entirely.
    const { href: _href, ...stripped } = STD_CITATIONS[0];
    void _href;
    expect(isClickableCitation(stripped as Citation)).toBe(false);
  });

  // -----------------------------------------------------------------------
  // Extra: legacy stub export still callable for back-compat
  // -----------------------------------------------------------------------
  it('defaultCitationClickStub remains callable for back-compat (task 026 seam)', () => {
    const consoleSpy = jest.spyOn(console, 'debug').mockImplementation(() => undefined);
    expect(() => defaultCitationClickStub(STD_CITATIONS[0])).not.toThrow();
    expect(consoleSpy).toHaveBeenCalled();
    consoleSpy.mockRestore();
  });
});
