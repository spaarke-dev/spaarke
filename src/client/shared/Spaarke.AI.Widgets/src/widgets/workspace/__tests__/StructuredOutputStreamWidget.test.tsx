/**
 * StructuredOutputStreamWidget — unit tests for the schema-aware field
 * renderer (R6 tasks 040/041; fixes R5 SC-18 / Gap C).
 *
 * CUTOVER NOTE (ai-architecture-redesign-r1 task 046 / amended ADR-037): the
 * legacy per-field-delta streaming path was DELETED — streaming content is
 * section-name-keyed (see StructuredOutputStreamWidget.sections.test.tsx).
 * The schema-aware field renderer under test here now serves FINAL envelope
 * content only (static `prefilledFields`). Tests deliver content via the
 * `completeField` helper, which renders the terminal envelope exactly as a
 * host would after the dispatch stream resolves.
 *
 * Verifies:
 *   (a) Schema-aware array dispatch: `outputSchema` declares `tldr: string[]`;
 *       final content parses and renders as a Fluent v9 `<ul>`.
 *   (b) Backward compatibility (NFR-11): no `outputSchema` → widget renders
 *       via the legacy `displayHint` path with no regression.
 *   (c) Malformed JSON handling: graceful fallback; no crash; other fields
 *       continue to render normally.
 *   (d) Empty-array handling: `[]` → renders an empty `<ul data-empty>`.
 *   (e) Streaming waiting UI: mid-stream (no delivered content), skeleton
 *       placeholders render; schema-aware rendering activates on delivery.
 *   (f) Object-typed fields render as labeled key-value blocks (task 041).
 *   (g) ADR-021 dark-mode compliance: no hard-coded hex colors in the
 *       schema-aware renderer's output (verified via DOM inline-style scan).
 *
 * R5 SC-18 bug reproduction: the rendered `tldr` field MUST NOT contain raw
 * JSON token fragments; it MUST contain the parsed string values cleanly.
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
): {
  bus: PaneEventBus;
  rerender: (next: StructuredOutputStreamWidgetData) => void;
  completeField: (fieldPath: string, content: string) => void;
} {
  const { rerender } = render(
    <PaneEventBusProvider bus={bus}>
      <StructuredOutputStreamWidget data={data} widgetType="structured-output-stream" {...overrides} />
    </PaneEventBusProvider>
  );
  const doRerender = (next: StructuredOutputStreamWidgetData): void => {
    rerender(
      <PaneEventBusProvider bus={bus}>
        <StructuredOutputStreamWidget data={next} widgetType="structured-output-stream" {...overrides} />
      </PaneEventBusProvider>
    );
  };
  // Post-cutover delivery helper (task 046 / amended ADR-037): the schema-
  // aware field renderer consumes FINAL envelope content only. This mirrors
  // how a host renders the terminal result after the dispatch stream
  // resolves — re-render with the delivered field under `prefilledFields`.
  const accumulated: Record<string, string> = { ...(data.prefilledFields ?? {}) };
  const completeField = (fieldPath: string, content: string): void => {
    accumulated[fieldPath] = content;
    act(() => {
      doRerender({ ...data, mode: 'static', prefilledFields: { ...accumulated } });
    });
  };
  return { bus, rerender: doRerender, completeField };
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
    const { bus, completeField } = renderWidget(data);

    completeField('tldr', JSON.stringify(['First key point', 'Second key point', 'Third key point']));

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
    const { bus, completeField } = renderWidget(data);

    completeField('tldr', JSON.stringify(['alpha', 'beta']));

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
    const { bus, completeField } = renderWidget(data);

    completeField('tldr', 'This is the TL;DR string from a legacy action.');

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
    const { bus, completeField } = renderWidget(data);

    // Malformed JSON: missing closing bracket — splitListContent's JSON branch
    // skips (not a complete `[...]` envelope), then falls through to comma split.
    completeField('tldr', '["first", "second"');

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
    const { bus, completeField } = renderWidget(data);

    completeField('tldr', JSON.stringify('not an array — a string'));

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
    const { bus, completeField } = renderWidget(data);

    completeField('tldr', '[]');

    const tldrBlock = document.querySelector('[data-field-path="tldr"]');
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    expect(list!.getAttribute('data-empty')).toBe('true');
    expect(list!.querySelectorAll('li')).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// (e) Streaming waiting UI (post-cutover) — nothing renders mid-stream;
//     schema-aware rendering activates when the final envelope is delivered
// ---------------------------------------------------------------------------

describe('StructuredOutputStreamWidget — streaming waiting UI (post-cutover)', () => {
  it('renders no schema-aware content while streaming; activates on final delivery', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus, completeField } = renderWidget(data);

    // Mid-stream: streaming_started only — no content has been delivered
    // (the retired per-field-delta vocabulary no longer carries content;
    // streaming content is section-keyed per amended ADR-037).
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });

    // Schema-aware list MUST NOT render — nothing delivered yet; the widget
    // shows the field-plan waiting UI (skeleton placeholders).
    expect(document.querySelector('ul[data-display-hint="schema-array"]')).toBeNull();
    const widget = screen.getByTestId('structured-output-stream-widget');
    expect(widget.getAttribute('data-render-state')).toBe('streaming');
    expect(widget.getAttribute('data-render-mode')).toBe('fields');

    // The final envelope is delivered — the schema-aware list renders.
    completeField('tldr', JSON.stringify(['first', 'second']));

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
    const { bus, completeField } = renderWidget(data);

    completeField('entities', JSON.stringify({ organizations: ['Acme Corp'], persons: ['Jane Doe'] }));

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
    const { bus, completeField } = renderWidget(data);

    completeField('entities',
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
    const { bus, completeField } = renderWidget(data);

    completeField('entities', JSON.stringify({ organizations: ['Acme'], persons: ['Alice'] }));

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
    const { bus, completeField } = renderWidget(data);

    // Malformed JSON: missing closing brace. The B-G10c retry wraps in `{}` and
    // tries again; if still malformed, falls through to raw-text fallback.
    completeField('entities', '{"organizations": ["Acme"');

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
    const { bus, completeField } = renderWidget(data);

    completeField('entities', JSON.stringify(['not', 'an', 'object']));

    const entitiesBlock = document.querySelector('[data-field-path="entities"]');
    // No error surface.
    expect(entitiesBlock!.querySelector('[data-display-hint="schema-object-error"]')).toBeNull();
    // Raw-text fallback (array isn't a valid object even after wrap-in-braces).
    expect(entitiesBlock!.querySelector('[data-display-hint="schema-object-raw-fallback"]')).not.toBeNull();
  });

  it('activates schema-aware object rendering only when the final envelope is delivered (post-cutover)', () => {
    const data: StructuredOutputStreamWidgetData = {
      mode: 'streaming',
      schema: SUMMARIZE_SCHEMA,
      correlationId: STREAM_ID,
      outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    };
    const { bus, completeField } = renderWidget(data);

    // Mid-stream: streaming_started only — no delivered content yet.
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_started', streamId: STREAM_ID });
    });

    // Schema-aware object container MUST NOT render before delivery.
    expect(document.querySelector('[data-display-hint="schema-object"]')).toBeNull();

    // The final envelope is delivered.
    completeField('entities', '{"organizations":["Acme"],"persons":["Alice"]}');

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
    const tldrList = tldrBlock!.querySelector('ul[data-display-hint="schema-array"][data-field-path="tldr"]');
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
    const synthDisplaySchema = {
      fields: [{ path: 'contactPersons', label: 'Contacts', displayHint: 'list' as const, order: 10 }],
    };
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
    const synthDisplaySchema = {
      fields: [{ path: 'metadata', label: 'Metadata', displayHint: 'list' as const, order: 10 }],
    };
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
    const outerContainer = block!.querySelector('div[data-display-hint="schema-object"][data-field-path="metadata"]');
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

describe('StructuredOutputStreamWidget — phase machine (section-keyed lifecycle)', () => {
  it('header badge transitions Waiting → Streaming → Complete via the section lifecycle', () => {
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

    // Section content arrives and the stream completes (the post-cutover
    // vocabulary: section_started / section_completed / streaming_complete).
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId: STREAM_ID,
        sectionName: 'tldr',
        sectionIndex: 0,
      });
    });
    act(() => {
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId: STREAM_ID,
        sectionName: 'tldr',
        finalContent: 'done',
      });
    });
    act(() => {
      bus.dispatch('workspace', { type: 'streaming_complete', streamId: STREAM_ID, completionStatus: 'complete' });
    });

    expect(within(screen.getByTestId('structured-output-stream-widget')).queryByText(/complete/i)).not.toBeNull();
    // Section mode rendered the delivered content.
    expect(screen.getByTestId('structured-output-stream-widget').getAttribute('data-render-mode')).toBe('sections');
  });
});
