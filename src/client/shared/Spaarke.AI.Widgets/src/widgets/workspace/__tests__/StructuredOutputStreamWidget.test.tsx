/**
 * StructuredOutputStreamWidget — unit tests for R6 task 040 schema-aware
 * ARRAY-typed field rendering (Pillar 5, fixes R5 SC-18 TL;DR bug / Gap C).
 *
 * Verifies:
 *   (a) Schema-aware array dispatch: `outputSchema` declares `tldr: string[]`;
 *       on `streaming_complete`, the accumulated JSON array parses and renders
 *       as a Fluent v9 `<ul>` with one `<li>` per array element.
 *   (b) Backward compatibility (NFR-11): no `outputSchema` → widget renders
 *       via the legacy `displayHint` path with no regression.
 *   (c) Malformed JSON handling: accumulated chunk is not valid JSON →
 *       widget renders an inline error surface; does NOT crash; other fields
 *       continue to render normally.
 *   (d) Empty-array handling: outputSchema declares array but content is `[]`
 *       → renders an empty `<ul data-empty="true"/>`.
 *   (e) Streaming in-progress: mid-stream (before `streaming_complete`), the
 *       schema-aware path is GATED off; legacy skeleton/cursor flow continues
 *       to render. The schema-aware dispatch only activates post-complete.
 *   (f) Object-typed fields (task 041 scope) currently fall through to legacy
 *       displayHint rendering — verified explicitly so task 041 has a
 *       baseline to flip.
 *   (g) ADR-021 dark-mode compliance: no hard-coded hex colors in the
 *       schema-aware renderer's output (verified via DOM inline-style scan).
 *
 * R5 SC-18 bug reproduction: the rendered `tldr` field MUST NOT contain raw
 * JSON token fragments (e.g., `["first`, `\\"first`); it MUST contain the
 * parsed string values cleanly. Negative assertion captured.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, render, screen, within } from '@testing-library/react';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import type { WorkspaceWidgetProps } from '../../../types/widget-types';
import StructuredOutputStreamWidget, {
  SUMMARIZE_SCHEMA,
  type JsonSchema,
  type StructuredOutputStreamWidgetData,
} from '../StructuredOutputStreamWidget';

// ---------------------------------------------------------------------------
// Helpers
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

/**
 * SUM-CHAT@v1 outputSchema mirror (R6 Phase B Wave B-G2 task 032). This is
 * the canonical R6 contract — the widget receives this shape via widgetData.
 */
const SUM_CHAT_OUTPUT_SCHEMA: JsonSchema = {
  type: 'object',
  properties: {
    tldr: { type: 'array', items: { type: 'string' } },
    summary: { type: 'string' },
    keywords: { type: 'string' },
    entities: {
      type: 'object',
      properties: {
        organizations: { type: 'array', items: { type: 'string' } },
        persons: { type: 'array', items: { type: 'string' } },
      },
    },
  },
};

const STREAM_ID = 'test-stream-040';

/**
 * Drive the streaming reducer end-to-end by dispatching the three lifecycle
 * events that `usePaneEvent('workspace', ...)` consumes. Wrapped in `act()`
 * so React state updates flush before assertions.
 */
function streamFieldComplete(bus: PaneEventBus, fieldPath: string, content: string): void {
  act(() => {
    bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'field_delta',
      streamId: STREAM_ID,
      fieldPath,
      fieldContent: content,
      sequence: 1,
    });
  });
  act(() => {
    bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
  });
}

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
// (a) Schema-aware array dispatch — happy path (fixes R5 SC-18)
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — schema-aware array dispatch (R6 task 040)', () => {
  it('renders an array-typed field as a Fluent v9 bulleted list on streaming_complete', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(
      bus,
      'tldr',
      JSON.stringify(['First key point', 'Second key point', 'Third key point'])
    );

    // The tldr field block exists with the schema-aware render hint.
    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock).not.toBeNull();
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toBe('First key point');
    expect(items[1].textContent).toBe('Second key point');
    expect(items[2].textContent).toBe('Third key point');
  });

  it('renders parsed bullet text cleanly — no raw JSON fragments (R5 SC-18 negative assertion)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(bus, 'tldr', JSON.stringify(['alpha', 'beta']));

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    const visibleText = tldrBlock!.textContent ?? '';
    // R5 SC-18 bug: raw streaming JSON token fragments leaked into the UI.
    // Post-fix, the rendered DOM must contain only parsed string values.
    expect(visibleText).not.toMatch(/\["/);
    expect(visibleText).not.toMatch(/"\]/);
    expect(visibleText).not.toMatch(/\\"/);
    expect(visibleText).toContain('alpha');
    expect(visibleText).toContain('beta');
  });

  it('renders schema-aware array immediately in mode: "static" (no streaming gate)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: {
        tldr: JSON.stringify(['static-1', 'static-2']),
      },
    };
    renderWidget(data);

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toBe('static-1');
  });
});

// ---------------------------------------------------------------------------
// (b) Backward compatibility — outputSchema absent → legacy rendering (NFR-11)
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — backward compatibility (NFR-11)', () => {
  it('falls back to legacy displayHint rendering when outputSchema is absent', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      // No outputSchema — legacy path.
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(bus, 'tldr', 'This is the TL;DR string from a legacy action.');

    // No schema-array list — the legacy path uses the displayHint renderer.
    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock!.querySelector('ul[data-display-hint="schema-array"]')).toBeNull();
    // Legacy `displayHint: 'heading'` for tldr → <h2 data-display-hint="heading">.
    const heading = tldrBlock!.querySelector('[data-display-hint="heading"]');
    expect(heading).not.toBeNull();
    expect(heading!.textContent).toContain('This is the TL;DR string from a legacy action.');
  });

  it('legacy mode: "static" without outputSchema renders prefilled string directly', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      prefilledFields: {
        tldr: 'Static TL;DR string',
      },
    };
    renderWidget(data);

    expect(document.querySelector('ul[data-display-hint="schema-array"]')).toBeNull();
    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock!.textContent).toContain('Static TL;DR string');
  });
});

// ---------------------------------------------------------------------------
// (c) Malformed JSON handling — error state, no crash
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — malformed JSON in schema-aware field', () => {
  // R6 Hotfix Wave B-G10c (2026-06-10): When the server streams VALUE content per
  // field (not full JSON syntax), schema-aware strict parse fails for content like
  // "The international..." (a plain string for an `array: string` field). The widget
  // now FALLS BACK to splitListContent (legacy R5 path) instead of showing an error
  // surface. Tests below verify the new graceful behavior.
  it('falls back to splitListContent for malformed array chunks (B-G10c)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    // Malformed JSON: missing closing bracket — splitListContent's JSON branch
    // skips (not a complete `[...]` envelope), then falls through to comma split.
    streamFieldComplete(bus, 'tldr', '["first", "second"');

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock).not.toBeNull();
    // No error surface — fallback path renders bullets.
    expect(tldrBlock!.querySelector('[data-display-hint="schema-array-error"]')).toBeNull();
    // Bulleted list renders (content imperfect but user-visible).
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    expect(list!.querySelectorAll('li').length).toBeGreaterThan(0);
    // Widget envelope still mounted (no crash).
    expect(screen.getByTestId('structured-output-stream-widget')).toBeInTheDocument();
  });

  it('falls back to splitListContent for non-array JSON like a quoted string (B-G10c)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(bus, 'tldr', JSON.stringify('not an array — a string'));

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    // No error surface — fallback renders the string as a single-item list.
    expect(tldrBlock!.querySelector('[data-display-hint="schema-array-error"]')).toBeNull();
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    expect(list!.querySelectorAll('li').length).toBeGreaterThanOrEqual(1);
  });

  it('B-G10c fallback in one field does not break sibling field rendering', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: {
        tldr: '[malformed',
        summary: 'Healthy summary text',
      },
    };
    renderWidget(data);

    // tldr falls back gracefully — no error surface.
    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock!.querySelector('[data-display-hint="schema-array-error"]')).toBeNull();
    expect(tldrBlock!.querySelector('ul[data-display-hint="schema-array"]')).not.toBeNull();
    // summary still renders via legacy paragraph path.
    const summaryBlock = document.querySelector('[data-field-path="summary"]');
    expect(summaryBlock!.querySelector('[data-display-hint="paragraph"]')).not.toBeNull();
    expect(summaryBlock!.textContent).toContain('Healthy summary text');
  });
});

// ---------------------------------------------------------------------------
// (d) Empty-array handling
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — empty array', () => {
  it('renders an empty <ul> when the array parses to []', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(bus, 'tldr', '[]');

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    expect(list!.getAttribute('data-empty')).toBe('true');
    expect(list!.querySelectorAll('li')).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// (e) Streaming in-progress — schema-aware dispatch GATED off until complete
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — streaming in-progress gate', () => {
  it('does NOT activate schema-aware rendering until streaming_complete fires', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    // Mid-stream: streaming_started + field_delta but NO streaming_complete yet.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'field_delta',
        streamId: STREAM_ID,
        fieldPath: 'tldr',
        fieldContent: '["first", "sec',
        sequence: 1,
      });
    });

    // Schema-aware list MUST NOT render (mid-stream content is unparseable).
    expect(document.querySelector('ul[data-display-hint="schema-array"]')).toBeNull();
    // Legacy displayHint path takes over for mid-stream rendering — the partial
    // raw content surfaces via the heading renderer (the existing behaviour
    // task 040 explicitly preserves until streaming_complete arrives).
    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-state')).toBe('streaming');

    // Now streaming_complete fires with the FULL parseable content.
    act(() => {
      bus.dispatch('workspace', {
        type: 'field_delta',
        streamId: STREAM_ID,
        fieldPath: 'tldr',
        fieldContent: 'ond"]',
        sequence: 2,
      });
    });
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
    });

    // Now the schema-aware list renders.
    expect(widget.getAttribute('data-render-state')).toBe('complete');
    const list = document.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toBe('first');
    expect(items[1].textContent).toBe('second');
  });
});

// ---------------------------------------------------------------------------
// (f) Schema-aware OBJECT dispatch (R6 task 041 — fixes R5 SC-18 entities bug)
//
// Task 040 asserted that object-typed fields fall through to the legacy
// `displayHint: 'list'` renderer; task 041 FLIPS that — `entities` now
// renders as labeled key-value blocks via `<SchemaAwareObjectRenderer />`,
// with nested arrays reusing task 040's bulleted-list code path.
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — schema-aware object dispatch (R6 task 041)', () => {
  it('renders an object-typed field as labeled key-value blocks (fixes R5 SC-18 entities bug)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(
      bus,
      'entities',
      JSON.stringify({ organizations: ['Acme Corp'], persons: ['Jane Doe'] })
    );

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    expect(entitiesBlock).not.toBeNull();

    // Top-level schema-aware object container takes over.
    const objectContainer = entitiesBlock!.querySelector(
      'div[data-display-hint="schema-object"][data-field-path="entities"]'
    );
    expect(objectContainer).not.toBeNull();

    // The legacy `displayHint: 'list'` path MUST NOT activate (task 040's
    // fall-through behaviour is replaced by task 041).
    expect(entitiesBlock!.querySelector('ul[data-display-hint="list"]')).toBeNull();

    // Labeled rows per nested property, in schema declaration order.
    const rows = objectContainer!.querySelectorAll('[data-prop-key]');
    expect(rows).toHaveLength(2);
    expect(rows[0].getAttribute('data-prop-key')).toBe('organizations');
    expect(rows[1].getAttribute('data-prop-key')).toBe('persons');

    // Labels humanized via prettyName (`organizations` → `Organizations`).
    expect(rows[0].textContent).toContain('Organizations');
    expect(rows[1].textContent).toContain('Persons');
  });

  it('reuses task 040 array path for nested array properties (no duplicate implementation)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(
      bus,
      'entities',
      JSON.stringify({
        organizations: ['Acme Corp', 'Beta Industries'],
        persons: ['Jane Doe', 'John Smith', 'Alice Brown'],
      })
    );

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');

    // Each nested array property renders via SchemaAwareArrayRenderer (same
    // `data-display-hint="schema-array"` attribute as top-level array fields).
    const orgsList = entitiesBlock!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="entities.organizations"]'
    );
    expect(orgsList).not.toBeNull();
    const orgsItems = orgsList!.querySelectorAll('li');
    expect(orgsItems).toHaveLength(2);
    expect(orgsItems[0].textContent).toBe('Acme Corp');
    expect(orgsItems[1].textContent).toBe('Beta Industries');

    const personsList = entitiesBlock!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="entities.persons"]'
    );
    expect(personsList).not.toBeNull();
    const personItems = personsList!.querySelectorAll('li');
    expect(personItems).toHaveLength(3);
    expect(personItems[0].textContent).toBe('Jane Doe');
    expect(personItems[2].textContent).toBe('Alice Brown');
  });

  it('renders parsed object cleanly — no raw JSON literal in DOM (R5 SC-18 negative assertion)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(
      bus,
      'entities',
      JSON.stringify({ organizations: ['Acme'], persons: ['Alice'] })
    );

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    const visibleText = entitiesBlock!.textContent ?? '';

    // R5 SC-18 bug: raw object literal `{"organizations":["Acme Corp"]...}`
    // leaked into the UI. Post-fix, the rendered DOM contains parsed values
    // organized under labels — NOT JSON syntax characters in adjacency.
    expect(visibleText).not.toMatch(/\{"organizations":/);
    expect(visibleText).not.toMatch(/"persons":/);
    expect(visibleText).not.toMatch(/\\"/);
    // Positive: parsed entity values are present.
    expect(visibleText).toContain('Acme');
    expect(visibleText).toContain('Alice');
  });

  it('renders an empty array under its label when a nested property is []', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: {
        entities: JSON.stringify({ organizations: [], persons: ['Alice'] }),
      },
    };
    renderWidget(data);

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    // Empty array under organizations renders as empty <ul data-empty="true">.
    const orgsList = entitiesBlock!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="entities.organizations"]'
    );
    expect(orgsList).not.toBeNull();
    expect(orgsList!.getAttribute('data-empty')).toBe('true');
    expect(orgsList!.querySelectorAll('li')).toHaveLength(0);

    // Sibling renders normally.
    const personsList = entitiesBlock!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="entities.persons"]'
    );
    expect(personsList!.querySelectorAll('li')).toHaveLength(1);
  });

  // R6 Hotfix Wave B-G10c (2026-06-10): same fallback strategy as the array path
  // above — when strict JSON parse fails, the widget renders a raw-text fallback
  // (with intermediate wrap-in-braces retry) so users see SOMETHING rather than
  // an error surface. Tests below verify the new graceful behavior.
  it('renders raw-text fallback for malformed object JSON (B-G10c)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    // Malformed JSON: missing closing brace. The B-G10c retry wraps in `{}` and
    // tries again; if still malformed, falls through to raw-text fallback.
    streamFieldComplete(bus, 'entities', '{"organizations": ["Acme"');

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    // No error surface.
    expect(entitiesBlock!.querySelector('[data-display-hint="schema-object-error"]')).toBeNull();
    // Either the wrap-in-braces retry succeeded (renders labeled blocks) OR raw-text fallback.
    const labeledBlocks = entitiesBlock!.querySelector('[data-display-hint="schema-object"]');
    const rawFallback = entitiesBlock!.querySelector('[data-display-hint="schema-object-raw-fallback"]');
    expect(labeledBlocks !== null || rawFallback !== null).toBe(true);
    // Widget envelope still mounted (no crash).
    expect(screen.getByTestId('structured-output-stream-widget')).toBeInTheDocument();
  });

  it('renders raw-text fallback for non-object JSON like an array (B-G10c)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    streamFieldComplete(bus, 'entities', JSON.stringify(['not', 'an', 'object']));

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    // No error surface.
    expect(entitiesBlock!.querySelector('[data-display-hint="schema-object-error"]')).toBeNull();
    // Raw-text fallback (array isn't a valid object even after wrap-in-braces).
    expect(entitiesBlock!.querySelector('[data-display-hint="schema-object-raw-fallback"]')).not.toBeNull();
  });

  it('does NOT activate schema-aware object rendering mid-stream (gate matches array path)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    // Mid-stream: streaming_started + partial field_delta, NO streaming_complete.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'field_delta',
        streamId: STREAM_ID,
        fieldPath: 'entities',
        fieldContent: '{"organizations":["Acm',
        sequence: 1,
      });
    });

    // Schema-aware object container MUST NOT render — gate is post-complete.
    expect(document.querySelector('[data-display-hint="schema-object"]')).toBeNull();

    // Complete the stream.
    act(() => {
      bus.dispatch('workspace', {
        type: 'field_delta',
        streamId: STREAM_ID,
        fieldPath: 'entities',
        fieldContent: 'e"],"persons":["Alice"]}',
        sequence: 2,
      });
    });
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
    });

    // Now the object dispatch activates.
    const objectContainer = document.querySelector(
      'div[data-display-hint="schema-object"][data-field-path="entities"]'
    );
    expect(objectContainer).not.toBeNull();
    const orgs = objectContainer!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="entities.organizations"] li'
    );
    expect(orgs!.textContent).toBe('Acme');
  });

  it('preserves task 040 array dispatch alongside task 041 object dispatch (sibling isolation)', () => {
    // SUM-CHAT@v1 has BOTH tldr: string[] AND entities: object — verify both
    // schema-aware paths activate together and don't interfere.
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: {
        tldr: JSON.stringify(['Key point one', 'Key point two']),
        entities: JSON.stringify({ organizations: ['Acme'], persons: ['Alice'] }),
      },
    };
    renderWidget(data);

    // tldr — task 040 array path.
    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    const tldrList = tldrBlock!.querySelector(
      'ul[data-display-hint="schema-array"][data-field-path="tldr"]'
    );
    expect(tldrList).not.toBeNull();
    expect(tldrList!.querySelectorAll('li')).toHaveLength(2);

    // entities — task 041 object path.
    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    const entitiesContainer = entitiesBlock!.querySelector(
      'div[data-display-hint="schema-object"][data-field-path="entities"]'
    );
    expect(entitiesContainer).not.toBeNull();
  });

  it('humanizes property keys via prettyName (camelCase + snake_case)', () => {
    // Inject a synthetic schema with mixed-case keys to exercise prettyName.
    const synthSchema: JsonSchema = {
      type: 'object',
      properties: {
        contactPersons: {
          type: 'object',
          properties: {
            firstName: { type: 'string' },
            last_name: { type: 'string' },
          },
        },
      },
    };
    const synthDisplaySchema = { fields: [{ path: 'contactPersons', label: 'Contacts', displayHint: 'list' as const, order: 10 }] };
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: synthDisplaySchema,
      outputSchema: synthSchema,
      prefilledFields: {
        contactPersons: JSON.stringify({ firstName: 'Alice', last_name: 'Smith' }),
      },
    };
    renderWidget(data);

    const block = document.querySelector('[data-field-path="contactPersons"]');
    const firstRow = block!.querySelector('[data-prop-key="firstName"]');
    expect(firstRow).not.toBeNull();
    expect(firstRow!.textContent).toContain('First Name');

    const lastRow = block!.querySelector('[data-prop-key="last_name"]');
    expect(lastRow).not.toBeNull();
    expect(lastRow!.textContent).toContain('Last Name');
  });
});

// ---------------------------------------------------------------------------
// (f.2) Depth guard — nested object-of-object (depth ≥ 2) falls back to JSON
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — depth guard (Phase B constraint)', () => {
  it('falls back to compact JSON for depth-≥-2 nested object-of-object (no infinite recursion)', () => {
    // Synthetic schema: outer object → inner object → leaf string.
    // Phase B supports exactly ONE level of object nesting; the inner object
    // falls back to compact JSON.stringify with a documented TODO marker.
    const deepSchema: JsonSchema = {
      type: 'object',
      properties: {
        metadata: {
          type: 'object',
          properties: {
            author: {
              type: 'object',
              properties: {
                name: { type: 'string' },
              },
            },
          },
        },
      },
    };
    const synthDisplaySchema = { fields: [{ path: 'metadata', label: 'Metadata', displayHint: 'list' as const, order: 10 }] };
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: synthDisplaySchema,
      outputSchema: deepSchema,
      prefilledFields: {
        metadata: JSON.stringify({ author: { name: 'Alice' } }),
      },
    };
    renderWidget(data);

    const block = document.querySelector('[data-field-path="metadata"]');
    expect(block).not.toBeNull();

    // depth-1 object container renders normally for metadata.
    const outerContainer = block!.querySelector(
      'div[data-display-hint="schema-object"][data-field-path="metadata"]'
    );
    expect(outerContainer).not.toBeNull();

    // depth-2 inner `author` falls back to compact JSON via the
    // `schema-object-deep-fallback` hint with a `data-depth` attribute >= 2.
    const fallback = block!.querySelector('[data-display-hint="schema-object-deep-fallback"]');
    expect(fallback).not.toBeNull();
    expect(fallback!.getAttribute('data-depth')).toBe('2');
    // The compact JSON contains the inner value.
    expect(fallback!.textContent).toContain('Alice');
    // Widget envelope still mounted (no crash, no infinite recursion).
    expect(screen.getByTestId('structured-output-stream-widget')).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// (g) ADR-021 dark-mode compliance — no hard-coded colors in schema-aware DOM
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — ADR-021 dark-mode compliance', () => {
  it('schema-aware renderer DOM contains no inline hex/rgb color overrides', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: { tldr: JSON.stringify(['theme-safe']) },
    };
    renderWidget(data);

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock).not.toBeNull();
    // Scan all elements in the schema-aware subtree for inline style attributes
    // that contain hex / rgb / rgba color values. The widget's styling MUST
    // use Fluent v9 semantic tokens via makeStyles, NOT inline style colors.
    const all = tldrBlock!.querySelectorAll('*');
    for (const el of Array.from(all)) {
      const styleAttr = el.getAttribute('style');
      if (styleAttr === null) continue;
      // Conservative regex: matches `#abc123`, `rgb(`, `rgba(`. Tokens compile
      // to CSS custom-property references like `var(--colorNeutralForeground1)`
      // which never appear as hex/rgb literals on inline styles.
      expect(styleAttr).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
      expect(styleAttr).not.toMatch(/\brgb\s*\(/);
      expect(styleAttr).not.toMatch(/\brgba\s*\(/);
    }
  });

  it('object renderer DOM contains no inline hex/rgb color overrides (task 041)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'static',
      schema: SUMMARIZE_SCHEMA,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
      prefilledFields: {
        entities: JSON.stringify({ organizations: ['Acme'], persons: ['Alice'] }),
      },
    };
    renderWidget(data);

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    expect(entitiesBlock).not.toBeNull();
    const all = entitiesBlock!.querySelectorAll('*');
    for (const el of Array.from(all)) {
      const styleAttr = el.getAttribute('style');
      if (styleAttr === null) continue;
      expect(styleAttr).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
      expect(styleAttr).not.toMatch(/\brgb\s*\(/);
      expect(styleAttr).not.toMatch(/\brgba\s*\(/);
    }
  });
});

// ---------------------------------------------------------------------------
// Reducer-level sanity — verifies the schema-aware logic does not interfere
// with the existing stream reducer phases.
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — schema-aware path coexists with existing phase machine', () => {
  it('header badge transitions Streaming → Complete remain correct', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus } = renderWidget(data);

    // Pre-stream: badge is "Waiting".
    expect(within(screen.getByTestId('structured-output-stream-widget')).queryByText(/waiting/i)).not.toBeNull();

    // Stream begins.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });
    expect(within(screen.getByTestId('structured-output-stream-widget')).queryByText(/streaming/i)).not.toBeNull();

    // Stream completes with parseable content.
    act(() => {
      bus.dispatch('workspace', {
        type: 'field_delta',
        streamId: STREAM_ID,
        fieldPath: 'tldr',
        fieldContent: JSON.stringify(['done']),
        sequence: 1,
      });
    });
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
    });

    expect(within(screen.getByTestId('structured-output-stream-widget')).queryByText(/complete/i)).not.toBeNull();
  });
});
