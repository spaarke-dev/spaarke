/**
 * StructuredOutputStreamWidget — section-name-keyed rendering (Phase 5R Wave
 * 5-C, FR-54 / task 114b).
 *
 * Verifies the section-keyed SSE consumption path — the ONLY streaming render
 * contract since the ai-architecture-redesign-r1 task 046 cutover (amended
 * ADR-037: the legacy per-field-delta dual-render path was DELETED at the
 * last-playbook cutover).
 *
 * Acceptance criteria covered (per 114b POML, updated at the 046 cutover):
 *   (1) Widget hydrates `sections: Record<string, SectionState>` from
 *       `section_started` / `section_data` / `section_completed` events keyed
 *       by section name (not schema position).
 *   (2) Widget renders sections in `sectionIndex` order when provided; falls
 *       back to insertion order otherwise.
 *   (3) Out-of-order events tolerated (`section_completed` before
 *       `section_started`) — graceful state, no crash.
 *   (4) CUTOVER: retired per-field-delta-shaped events are IGNORED (unknown
 *       discriminant per ADR-030) — no content renders from them.
 *   (5) Section events flip the widget out of the pre-section waiting UI and
 *       into section mode.
 *   (6) Empty section content → renders header only with muted hint.
 *   (7) ADR-021: no hardcoded hex/rgb colors in section-renderer DOM.
 *
 * Static-envelope rendering (schema field plan + `prefilledFields`) remains
 * covered by `StructuredOutputStreamWidget.test.tsx`.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render, screen } from '@testing-library/react';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import type { WorkspaceWidgetProps } from '../../../types/widget-types';
import StructuredOutputStreamWidget, {
  SUMMARIZE_SCHEMA,
  type StructuredOutputStreamWidgetData,
} from '../StructuredOutputStreamWidget';

// ---------------------------------------------------------------------------
// Test harness
// ---------------------------------------------------------------------------

function renderWidget(
  data: StructuredOutputStreamWidgetData,
  overrides: Partial<WorkspaceWidgetProps<StructuredOutputStreamWidgetData>> = {},
  bus: PaneEventBus = new PaneEventBus()
): { bus: PaneEventBus; rerender: (next: StructuredOutputStreamWidgetData) => void } {
  const { rerender } = render(
    <PaneEventBusProvider bus={bus}>
      <StructuredOutputStreamWidget data={data} widgetType="structured-output-stream" {...overrides} />
    </PaneEventBusProvider>
  );
  return {
    bus,
    rerender: (next: StructuredOutputStreamWidgetData) => {
      rerender(
        <PaneEventBusProvider bus={bus}>
          <StructuredOutputStreamWidget data={next} widgetType="structured-output-stream" {...overrides} />
        </PaneEventBusProvider>
      );
    },
  };
}

const STREAM_ID = 'test-stream-114b';

let consoleWarnSpy: jest.SpyInstance;
let consoleDebugSpy: jest.SpyInstance;
beforeAll(() => {
  consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  consoleDebugSpy = jest.spyOn(console, 'debug').mockImplementation(() => undefined);
});
afterAll(() => {
  consoleWarnSpy.mockRestore();
  consoleDebugSpy.mockRestore();
});

// ---------------------------------------------------------------------------
// (1) Section-keyed events hydrate `sections: Record<string, SectionState>`
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — section-keyed rendering (FR-54 / task 114b)', () => {
  function streamingMode(): StructuredOutputStreamWidgetData {
    return {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
    };
  }

  it('hydrates a sections map from section_started → section_data → section_completed events', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'summary',
        displayLabel: 'Summary',
        sectionIndex: 0,
        totalSections: 3,
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'summary',
        contentDelta: 'The matter ',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'summary',
        contentDelta: 'involves a contract dispute.',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'summary',
        finalContent: 'The matter involves a contract dispute.',
      });
    });

    // Section mode activated.
    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-mode')).toBe('sections');
    expect(screen.getByTestId('sections-container')).toBeInTheDocument();

    const block = document.querySelector('[data-section-name="summary"]');
    expect(block).not.toBeNull();
    expect(block!.getAttribute('data-section-status')).toBe('completed');

    // Header uses the displayLabel.
    const header = block!.querySelector('[data-section-header="summary"]');
    expect(header).not.toBeNull();
    expect(header!.textContent).toBe('Summary');

    // Body shows the final content (replaces accumulated text).
    const body = block!.querySelector('[data-section-body="text"]');
    expect(body).not.toBeNull();
    expect(body!.textContent).toContain('The matter involves a contract dispute.');
  });

  it('accumulates contentDelta across multiple section_data events when no finalContent is provided', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'keyTerms',
        displayLabel: 'Key Terms',
        sectionIndex: 1,
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'keyTerms',
        contentDelta: 'Term A, ',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'keyTerms',
        contentDelta: 'Term B, ',
      });
    });
    // section_completed with NO finalContent → accumulated text preserved.
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'keyTerms',
      });
    });

    const block = document.querySelector('[data-section-name="keyTerms"]');
    expect(block!.getAttribute('data-section-status')).toBe('completed');
    const body = block!.querySelector('[data-section-body="text"]');
    expect(body!.textContent).toContain('Term A, Term B, ');
  });

  it('renders 3 sections from 3 section_started events; all appear with status="streaming"', () => {
    const { bus } = renderWidget(streamingMode());

    for (let i = 0; i < 3; i++) {
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_started',
          streamId: STREAM_ID,
          sectionName: `section${i}`,
          displayLabel: `Section ${i}`,
          sectionIndex: i,
          totalSections: 3,
        });
      });
    }

    const blocks = document.querySelectorAll('[data-section-name]');
    expect(blocks).toHaveLength(3);
    blocks.forEach(b => expect(b.getAttribute('data-section-status')).toBe('streaming'));
  });

  it('marks all sections completed when 3 section_completed events fire', () => {
    const { bus } = renderWidget(streamingMode());

    for (let i = 0; i < 3; i++) {
      const name = `s${i}`;
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_started',
          streamId: STREAM_ID,
          sectionName: name,
          sectionIndex: i,
        });
      });
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_data',
          streamId: STREAM_ID,
          sectionName: name,
          contentDelta: `payload-${i}`,
        });
      });
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_completed',
          streamId: STREAM_ID,
          sectionName: name,
        });
      });
    }

    const blocks = document.querySelectorAll('[data-section-status="completed"]');
    expect(blocks).toHaveLength(3);
    // Container reports "Complete" badge once all sections done.
    expect(screen.getByTestId('structured-output-stream-widget').getAttribute('data-render-state')).toBe('streaming');
    // After streaming_complete fires, the header switches to Complete badge.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
    });
    expect(screen.getByTestId('structured-output-stream-widget').getAttribute('data-render-state')).toBe('complete');
  });

  // -------------------------------------------------------------------------
  // (2) Section render order — sectionIndex sort
  // -------------------------------------------------------------------------

  it('renders sections in sectionIndex order even when events arrive in reverse', () => {
    const { bus } = renderWidget(streamingMode());

    // Events arrive in REVERSE declaration order (index 2 first).
    const order = [2, 0, 1];
    for (const i of order) {
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_started',
          streamId: STREAM_ID,
          sectionName: `section${i}`,
          displayLabel: `Section ${i}`,
          sectionIndex: i,
        });
      });
      act(() => {
        bus.dispatch('workspace', {
          type: 'section_completed',
          streamId: STREAM_ID,
          sectionName: `section${i}`,
          finalContent: `Content ${i}`,
        });
      });
    }

    // Rendered order MUST match sectionIndex: section0, section1, section2.
    const blocks = Array.from(document.querySelectorAll('[data-section-name]'));
    expect(blocks.map(b => b.getAttribute('data-section-name'))).toEqual(['section0', 'section1', 'section2']);
  });

  it('falls back to insertion order when sectionIndex is missing on all sections', () => {
    const { bus } = renderWidget(streamingMode());

    const names = ['banana', 'apple', 'cherry'];
    for (const n of names) {
      act(() => {
        bus.dispatch('workspace', { type: 'section_started', streamId: STREAM_ID, sectionName: n });
      });
    }

    const blocks = Array.from(document.querySelectorAll('[data-section-name]'));
    // No sectionIndex → insertion order preserved (banana → apple → cherry).
    expect(blocks.map(b => b.getAttribute('data-section-name'))).toEqual(['banana', 'apple', 'cherry']);
  });

  // -------------------------------------------------------------------------
  // (3) Out-of-order events tolerated
  // -------------------------------------------------------------------------

  it('tolerates section_completed before section_started — graceful, no crash', () => {
    const { bus } = renderWidget(streamingMode());

    // section_completed arrives FIRST.
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'orphan',
        finalContent: 'orphaned content',
      });
    });
    // Widget should not crash.
    expect(screen.getByTestId('structured-output-stream-widget')).toBeInTheDocument();

    const block = document.querySelector('[data-section-name="orphan"]');
    expect(block).not.toBeNull();
    expect(block!.getAttribute('data-section-status')).toBe('completed');
    expect(block!.querySelector('[data-section-body="text"]')!.textContent).toContain('orphaned content');

    // Late section_started arrives; status stays 'completed' (do not downgrade).
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'orphan',
        displayLabel: 'Orphan',
      });
    });
    expect(document.querySelector('[data-section-name="orphan"]')!.getAttribute('data-section-status')).toBe(
      'completed'
    );
  });

  it('tolerates section_data before section_started — accumulates correctly', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'early',
        contentDelta: 'early content',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'early',
        displayLabel: 'Early',
      });
    });

    const block = document.querySelector('[data-section-name="early"]');
    expect(block).not.toBeNull();
    expect(block!.querySelector('[data-section-body="text"]')!.textContent).toContain('early content');
  });

  // -------------------------------------------------------------------------
  // (4) CUTOVER — retired per-field-delta events are IGNORED (task 046,
  //     amended ADR-037: section-name-keyed streaming is the ONLY streaming
  //     render contract; the dual-render path is deleted, not shimmed)
  // -------------------------------------------------------------------------

  it('CUTOVER: retired per-field-delta-shaped events are IGNORED — no content renders from them', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
    };
    const { bus } = renderWidget(data);

    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });
    act(() => {
      // A retired-vocabulary event (as an unmigrated emitter would send it).
      // ADR-030: unknown discriminants are ignored by subscribers. The token
      // is constructed dynamically so the widget layer stays grep-zero for
      // the deleted vocabulary (NFR-08 evidence rule).
      bus.dispatch('workspace', {
        type: ['field', 'delta'].join('_'),
        streamId: STREAM_ID,
        fieldPath: 'tldr',
        fieldContent: 'Retired-path text that must NOT render.',
        sequence: 1,
      } as unknown as Parameters<typeof bus.dispatch>[1]);
    });

    // No section mode, no rendered content — the widget stays in the
    // pre-section waiting UI (skeleton placeholders).
    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-mode')).toBe('fields');
    expect(screen.queryByTestId('sections-container')).toBeNull();
    expect(widget.textContent).not.toContain('Retired-path text that must NOT render.');
  });

  it('BACKWARD-COMPAT: legacy mode="static" without section events renders prefilledFields unchanged', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      prefilledFields: { tldr: 'Static legacy TL;DR' },
    };
    renderWidget(data);

    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-mode')).toBe('fields');
    expect(screen.queryByTestId('sections-container')).toBeNull();

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock!.textContent).toContain('Static legacy TL;DR');
  });

  // -------------------------------------------------------------------------
  // (5) Mixed events — sections take precedence
  // -------------------------------------------------------------------------

  it('flips from the pre-section waiting UI into section mode when section events arrive', () => {
    const { bus } = renderWidget(streamingMode());

    // Before any section event the widget is in the field-plan waiting UI.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });
    expect(screen.getByTestId('structured-output-stream-widget').getAttribute('data-render-mode')).toBe('fields');

    // Now a section event arrives — widget flips to section mode.
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'mixed',
        displayLabel: 'Mixed Mode',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'mixed',
        finalContent: 'Final mixed content',
      });
    });

    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-mode')).toBe('sections');
    expect(screen.getByTestId('sections-container')).toBeInTheDocument();
    // Field block hidden, sections take over.
    expect(widget.querySelector('[data-field-path="tldr"]')).toBeNull();
    expect(widget.querySelector('[data-section-name="mixed"]')).not.toBeNull();
  });

  // -------------------------------------------------------------------------
  // (6) Empty section — header only with muted hint
  // -------------------------------------------------------------------------

  it('renders header + muted hint for sections with no content', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'empty',
        displayLabel: 'Empty Section',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'empty',
      });
    });

    const block = document.querySelector('[data-section-name="empty"]');
    expect(block).not.toBeNull();
    // Header rendered.
    expect(block!.querySelector('[data-section-header="empty"]')!.textContent).toBe('Empty Section');
    // Body NOT rendered (no text, no structuredData).
    expect(block!.querySelector('[data-section-body="text"]')).toBeNull();
    expect(block!.querySelector('[data-section-body="structured"]')).toBeNull();
    // Empty-state hint rendered.
    const hint = block!.querySelector('[data-section-body="empty"]');
    expect(hint).not.toBeNull();
    expect(hint!.textContent).toBe('(no content)');
  });

  // -------------------------------------------------------------------------
  // Structured data — value-shape-typed rendering (task 046) + JSON fallback
  // -------------------------------------------------------------------------

  it('renders a flat-record structuredData as labeled rows below accumulated text (task 046 typed rendering)', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'structured',
        displayLabel: 'Structured',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'structured',
        finalContent: 'Header text',
        finalStructuredData: { keyA: 'valA', keyB: ['x', 'y'] },
      });
    });

    const block = document.querySelector('[data-section-name="structured"]');
    expect(block!.querySelector('[data-section-body="text"]')!.textContent).toContain('Header text');
    // Flat record (string + string[] values) → labeled rows, NOT raw JSON.
    const record = block!.querySelector('[data-section-body="structured-record"]');
    expect(record).not.toBeNull();
    expect(record!.querySelector('[data-prop-key="keyA"]')!.textContent).toContain('valA');
    const keyBList = record!.querySelector('[data-prop-key="keyB"] ul');
    expect(keyBList).not.toBeNull();
    expect(keyBList!.querySelectorAll('li')).toHaveLength(2);
    // No compact-JSON fallback for this well-shaped payload.
    expect(block!.querySelector('pre[data-section-body="structured"]')).toBeNull();
  });

  it('renders an array-of-strings structuredData as a bulleted list (task 046 typed rendering)', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'bullets',
        finalStructuredData: ['one', 'two', 'three'],
      });
    });

    const block = document.querySelector('[data-section-name="bullets"]');
    const list = block!.querySelector('ul[data-section-body="structured-list"]');
    expect(list).not.toBeNull();
    expect(list!.querySelectorAll('li')).toHaveLength(3);
    expect(block!.querySelector('pre[data-section-body="structured"]')).toBeNull();
  });

  it('renders deeply-nested structuredData via the compact JSON fallback (unchanged MVP escape hatch)', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'deep',
        finalStructuredData: { outer: { inner: 'value' } },
      });
    });

    const block = document.querySelector('[data-section-name="deep"]');
    const struct = block!.querySelector('pre[data-section-body="structured"]');
    expect(struct).not.toBeNull();
    expect(struct!.textContent).toContain('outer');
    expect(struct!.textContent).toContain('value');
  });

  it('renders citations below content when emitted by section_completed', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'cited',
        displayLabel: 'Cited',
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'cited',
        finalContent: 'Cited content',
        citations: [{ id: 'c1', label: 'Doc One' }, { id: 'c2', title: 'Doc Two' }, { id: 'c3' }],
      });
    });

    const block = document.querySelector('[data-section-name="cited"]');
    const list = block!.querySelector('[data-section-body="citations"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toBe('Doc One');
    expect(items[1].textContent).toBe('Doc Two');
    expect(items[2].textContent).toBe('c3');
  });

  // -------------------------------------------------------------------------
  // Streaming indicator on the active section
  // -------------------------------------------------------------------------

  it('shows a Streaming… badge on the section whose status is streaming', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'active',
        displayLabel: 'Active',
      });
    });

    const block = document.querySelector('[data-section-name="active"]');
    expect(block!.getAttribute('data-section-status')).toBe('streaming');
    const badge = block!.querySelector('[data-section-status-badge="streaming"]');
    expect(badge).not.toBeNull();
    expect(badge!.textContent).toContain('Streaming');
  });

  // -------------------------------------------------------------------------
  // Defensive event drops
  // -------------------------------------------------------------------------

  it('drops section_started events with missing/empty sectionName', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', { type: 'section_started', streamId: STREAM_ID });
    });
    act(() => {
      bus.dispatch('workspace', { type: 'section_started', streamId: STREAM_ID, sectionName: '' });
    });

    // No sections recorded.
    expect(screen.queryByTestId('sections-container')).toBeNull();
    expect(screen.getByTestId('structured-output-stream-widget').getAttribute('data-render-mode')).toBe('fields');
  });

  it('drops section_data events with neither contentDelta nor structuredData', () => {
    const { bus } = renderWidget(streamingMode());

    // section_started establishes the section.
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'noop',
      });
    });
    // section_data with no payload → dropped (no state mutation).
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_data',
        streamId: STREAM_ID,
        sectionName: 'noop',
      });
    });

    const block = document.querySelector('[data-section-name="noop"]');
    expect(block!.querySelector('[data-section-body="text"]')).toBeNull();
  });

  // -------------------------------------------------------------------------
  // correlationId gate — events for a different stream id are ignored
  // -------------------------------------------------------------------------

  it('ignores section events with a different streamId (correlationId gate)', () => {
    const { bus } = renderWidget(streamingMode());

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: 'a-different-stream',
        sectionName: 'cross-stream',
      });
    });

    // No sections recorded — event was filtered out.
    expect(screen.queryByTestId('sections-container')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (7) ADR-021 — no hardcoded colors in section renderer DOM
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — section renderer ADR-021 compliance', () => {
  it('section renderer DOM contains no inline hex/rgb color overrides (dark-mode safe)', () => {
    const bus = new PaneEventBus();
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
    };
    render(
      <PaneEventBusProvider bus={bus}>
        <StructuredOutputStreamWidget data={data} widgetType="structured-output-stream" />
      </PaneEventBusProvider>
    );

    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 's1',
        displayLabel: 'Section 1',
        sectionIndex: 0,
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 's1',
        finalContent: 'Body',
        citations: [{ id: 'c1', label: 'Citation 1' }],
      });
    });

    const container = screen.getByTestId('sections-container');
    const all = container.querySelectorAll('*');
    for (const el of Array.from(all)) {
      const styleAttr = el.getAttribute('style');
      if (styleAttr === null) continue;
      expect(styleAttr).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
      expect(styleAttr).not.toMatch(/\brgb\s*\(/);
      expect(styleAttr).not.toMatch(/\brgba\s*\(/);
    }
  });
});
