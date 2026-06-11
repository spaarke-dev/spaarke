/**
 * StructuredOutputStreamWidget — integration regression test for
 * R6 Hotfix Wave B-G9a (2026-06-10).
 *
 * Origin bug (Phase B walkthrough, Spaarke Dev):
 *   - `tldr` (schema: `array of string`) rendered as a BOLD paragraph with
 *     literal text like `"A method...","The system uses..."` — comma-quoted
 *     array contents as text, NOT bullets per task 040 contract.
 *   - `entities` (schema: `object`) rendered as bullets containing raw JSON
 *     syntax fragments like `organizations":[]` and `"persons":[]` — NOT
 *     labeled blocks per task 041 contract.
 *
 * Root cause: `dispatchSummarizeOnly` in `FilePreviewContextWidget.tsx`
 * constructed the widget payload WITHOUT `outputSchema`. The widget's
 * `classifySchemaField()` consequently returned `'legacy'` for every field,
 * so the legacy `displayHint`-based renderers ran:
 *   - `tldr` displayHint `'heading'` → `<h2>` containing the raw JSON array
 *     string (`["a", "b"]`).
 *   - `entities` displayHint `'list'` → `splitListContent()` comma fallback
 *     splits the raw JSON object on commas → `['{"organizations":["X"]',
 *      '"persons":["Y"]}']` → bullets with raw JSON syntax fragments.
 *
 * The R6 task 040/041 unit tests (StructuredOutputStreamWidget.test.tsx)
 * PASSED because they always set `outputSchema` explicitly in the test
 * fixture. They did NOT exercise the production dispatcher payload.
 *
 * This regression test asserts the FULL contract that production exercises:
 *   - The dispatcher's payload SHAPE (now includes `outputSchema` matching
 *     SUM_CHAT_OUTPUT_SCHEMA exported from the widget module). This contract
 *     is asserted via a shape mirror of the SUM-CHAT@v1 widgetData rather
 *     than by importing `FilePreviewContextWidget.tsx` directly (importing
 *     that widget pulls in `@spaarke/ui-components` → `d3-force` ESM which
 *     ts-jest cannot transform without bespoke config; matches the
 *     pre-existing `FilePreviewContextWidget.summarize-only.test.tsx`
 *     execution constraint). The contract is held canonical by the
 *     `dispatchSummarizeOnly` source code review check below.
 *   - End-to-end widget rendering of the four SUM-CHAT@v1 fields against
 *     the dispatcher payload SHAPE (mounted directly so the test executes
 *     in the same module-resolution domain as the existing unit tests).
 *
 * Why this test would have caught the production bug:
 *   - Pre-fix, the dispatcher's widget payload had `outputSchema: undefined`.
 *     The widget then took the legacy displayHint path for every field.
 *   - Post-fix, the dispatcher passes `outputSchema: SUM_CHAT_OUTPUT_SCHEMA`.
 *     The widget dispatches `tldr` to `<SchemaAwareArrayRenderer />` and
 *     `entities` to `<SchemaAwareObjectRenderer />`.
 *   - The positive assertions (schema-array bullets; schema-object labeled
 *     blocks) FAIL when `outputSchema` is missing → the legacy path runs.
 *   - The negative assertions (no raw JSON syntax in bullet text; no
 *     `<h2>` containing the JSON array literal) ALSO fail when the legacy
 *     path runs against an array/object payload.
 *
 * Coverage:
 *   (a) Dispatcher payload shape: `outputSchema` matches `SUM_CHAT_OUTPUT_SCHEMA`.
 *   (b) Widget renders `tldr` as bulleted `<ul><li>` items (task 040 contract).
 *   (c) Widget renders `entities` as labeled blocks (task 041 contract).
 *   (d) Section headers (TL;DR / Summary / Keywords / Entities) all render.
 *   (e) `summary` (string) renders as a paragraph (unchanged legacy path).
 *   (f) `keywords` (string) renders as colored Badges (unchanged legacy path).
 *   (g) Negative assertion: the pre-fix raw-JSON failure modes are ABSENT
 *       (no bold paragraph containing the array literal; no bullet item with
 *       raw `organizations":[]` syntax).
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { act, render, within } from '@testing-library/react';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import StructuredOutputStreamWidget, {
  SUMMARIZE_SCHEMA,
  SUM_CHAT_OUTPUT_SCHEMA,
  type StructuredOutputStreamWidgetData,
} from '../StructuredOutputStreamWidget';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const STREAM_ID = 'sess-r6-b-g9a';

/**
 * Realistic SUM-CHAT@v1 envelope shape. These are the EXACT final-state
 * payloads Azure OpenAI Structured Outputs would deliver per field after
 * `streaming_complete` (per task 006 spike: declaration-order arrival).
 *
 * These literal shapes are what triggered the production bug — the legacy
 * renderer fed the raw JSON strings into the displayHint paths.
 */
const TLDR_PAYLOAD = JSON.stringify([
  'A method for private intersection of authenticated data.',
  'The system uses zero-knowledge proofs.',
  'Private transactions remain confidential.',
]);

const SUMMARY_PAYLOAD =
  'This patent describes a method for performing set-intersection operations on authenticated data without revealing the underlying values to either party.';

const KEYWORDS_PAYLOAD = 'cryptography, zero-knowledge, set intersection, patent, privacy';

const ENTITIES_PAYLOAD = JSON.stringify({
  organizations: ['Acme Corp.', 'Wayne Industries'],
  persons: ['Alice Smith', 'Bob Jones'],
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Build the widget data shape the production dispatcher
 * (`dispatchSummarizeOnly` in `FilePreviewContextWidget.tsx`) emits. Kept in
 * sync with that source file via the source-code contract assertion in suite
 * (a). The MIRROR is intentional — importing the dispatcher pulls in
 * `@spaarke/ui-components` which pulls in `d3-force` (ESM) that ts-jest
 * cannot transform without bespoke config. Same constraint applies to the
 * pre-existing `FilePreviewContextWidget.summarize-only.test.tsx`.
 */
function buildSumChatWidgetData(correlationId: string): StructuredOutputStreamWidgetData {
  return {
    mode: 'streaming',
    schema: SUMMARIZE_SCHEMA,
    outputSchema: SUM_CHAT_OUTPUT_SCHEMA,
    correlationId,
    title: 'Summary: Contract.pdf',
  };
}

function renderWidgetWithData(data: StructuredOutputStreamWidgetData, bus: PaneEventBus) {
  return render(
    <PaneEventBusProvider bus={bus}>
      <StructuredOutputStreamWidget data={data} widgetType="structured-output-stream" />
    </PaneEventBusProvider>
  );
}

function streamSumChatFields(bus: PaneEventBus, streamId: string): void {
  act(() => {
    bus.dispatch('workspace', { type: 'streaming_started', streamId });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'field_delta',
      streamId,
      fieldPath: 'tldr',
      fieldContent: TLDR_PAYLOAD,
      sequence: 1,
    });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'field_delta',
      streamId,
      fieldPath: 'summary',
      fieldContent: SUMMARY_PAYLOAD,
      sequence: 2,
    });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'field_delta',
      streamId,
      fieldPath: 'keywords',
      fieldContent: KEYWORDS_PAYLOAD,
      sequence: 3,
    });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'field_delta',
      streamId,
      fieldPath: 'entities',
      fieldContent: ENTITIES_PAYLOAD,
      sequence: 4,
    });
  });
  act(() => {
    bus.dispatch('workspace', {
      type: 'streaming_complete',
      streamId,
      completionStatus: 'complete',
    });
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
// (a) Source-code contract: dispatchSummarizeOnly MUST pass outputSchema
// ---------------------------------------------------------------------------

describe('Hotfix Wave B-G9a — dispatchSummarizeOnly source contract', () => {
  it('FilePreviewContextWidget.tsx dispatchSummarizeOnly references SUM_CHAT_OUTPUT_SCHEMA (regression: was absent pre-fix)', () => {
    // We read the source file directly rather than importing the module
    // (importing pulls in `@spaarke/ui-components` → `d3-force` ESM which
    // ts-jest cannot transform without bespoke config — same constraint as
    // the pre-existing `FilePreviewContextWidget.summarize-only.test.tsx`).
    // The text-level assertion is sufficient: the constant import + payload
    // assignment is a load-bearing line of production code; removing it
    // re-introduces the bug.
    const dispatcherPath = path.resolve(__dirname, '../../context/FilePreviewContextWidget.tsx');
    const source = fs.readFileSync(dispatcherPath, 'utf-8');

    // Contract 1: import of SUM_CHAT_OUTPUT_SCHEMA from the widget module.
    expect(source).toMatch(/SUM_CHAT_OUTPUT_SCHEMA/);

    // Contract 2: `outputSchema:` key present in the widgetData literal.
    expect(source).toMatch(/outputSchema:\s*SUM_CHAT_OUTPUT_SCHEMA/);

    // Contract 3: the import statement specifically resolves SUM_CHAT_OUTPUT_SCHEMA
    // from the widget module (not from some indirect path).
    expect(source).toMatch(
      /import\s*{[^}]*SUM_CHAT_OUTPUT_SCHEMA[^}]*}\s*from\s*['"]\.\.\/workspace\/StructuredOutputStreamWidget['"]/s
    );
  });

  it('SUM_CHAT_OUTPUT_SCHEMA matches the SUM-CHAT@v1 action output schema contract', () => {
    expect(SUM_CHAT_OUTPUT_SCHEMA.type).toBe('object');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties).toBeDefined();
    // tldr → array of string (drives <SchemaAwareArrayRenderer />)
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.tldr.type).toBe('array');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.tldr.items?.type).toBe('string');
    // summary → string (legacy paragraph path)
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.summary.type).toBe('string');
    // keywords → string (legacy badge path)
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.keywords.type).toBe('string');
    // entities → object with nested arrays (drives <SchemaAwareObjectRenderer />)
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.entities.type).toBe('object');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.entities.properties).toBeDefined();
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.entities.properties!.organizations.type).toBe('array');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.entities.properties!.persons.type).toBe('array');
  });
});

// ---------------------------------------------------------------------------
// (b)-(g) End-to-end widget render with the actual dispatcher payload shape
// ---------------------------------------------------------------------------

describe('Hotfix Wave B-G9a — widget renders cleanly with SUM-CHAT@v1 dispatcher payload', () => {
  it('renders tldr as a bulleted Fluent v9 list (task 040 contract; fixes R5 SC-18 / Wave B-G9a)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    const tldrBlock = container.querySelector('[data-field-path="tldr"]');
    expect(tldrBlock).not.toBeNull();

    // POSITIVE assertion: schema-aware <ul> with three <li> items.
    const list = tldrBlock!.querySelector('ul[data-display-hint="schema-array"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toBe('A method for private intersection of authenticated data.');
    expect(items[1].textContent).toBe('The system uses zero-knowledge proofs.');
    expect(items[2].textContent).toBe('Private transactions remain confidential.');

    // NEGATIVE assertion (production bug repro): no legacy `<h2>` containing
    // the raw JSON array literal. Pre-fix, the HeadingRenderer rendered the
    // raw JSON-encoded string into an <h2>, producing a bold paragraph with
    // literal text like `"A method...","The system uses..."`.
    const legacyHeading = tldrBlock!.querySelector('h2[data-display-hint="heading"]');
    expect(legacyHeading).toBeNull();
    const tldrText = tldrBlock!.textContent ?? '';
    expect(tldrText).not.toMatch(/\["/); // no raw JSON-array opening
    expect(tldrText).not.toMatch(/"\]/); // no raw JSON-array closing
    expect(tldrText).not.toMatch(/\\"/); // no escaped-quote noise
  });

  it('renders entities as labeled key-value blocks (task 041 contract; fixes Wave B-G9a)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    const entitiesBlock = container.querySelector('[data-field-path="entities"]');
    expect(entitiesBlock).not.toBeNull();

    // POSITIVE assertion: schema-object container with one row per declared
    // property (organizations, persons), each containing a SchemaAwareArrayRenderer.
    const objectContainer = entitiesBlock!.querySelector('div[data-display-hint="schema-object"]');
    expect(objectContainer).not.toBeNull();

    const orgsRow = entitiesBlock!.querySelector('[data-prop-key="organizations"]');
    expect(orgsRow).not.toBeNull();
    expect(orgsRow!.textContent).toContain('Organizations');
    const orgsList = orgsRow!.querySelector('ul[data-display-hint="schema-array"]');
    expect(orgsList).not.toBeNull();
    const orgsItems = orgsList!.querySelectorAll('li');
    expect(orgsItems).toHaveLength(2);
    expect(orgsItems[0].textContent).toBe('Acme Corp.');
    expect(orgsItems[1].textContent).toBe('Wayne Industries');

    const personsRow = entitiesBlock!.querySelector('[data-prop-key="persons"]');
    expect(personsRow).not.toBeNull();
    expect(personsRow!.textContent).toContain('Persons');
    const personsList = personsRow!.querySelector('ul[data-display-hint="schema-array"]');
    expect(personsList).not.toBeNull();
    const personsItems = personsList!.querySelectorAll('li');
    expect(personsItems).toHaveLength(2);
    expect(personsItems[0].textContent).toBe('Alice Smith');
    expect(personsItems[1].textContent).toBe('Bob Jones');

    // NEGATIVE assertion (production bug repro): no legacy `<ul>` with raw
    // JSON-syntax bullet text. Pre-fix, the ListRenderer.splitListContent
    // comma-split fallback turned the object into bullets like
    // `{"organizations":["Acme"]` / `"persons":["Bob"]}`.
    const entitiesText = entitiesBlock!.textContent ?? '';
    expect(entitiesText).not.toMatch(/"organizations":\s*\[/);
    expect(entitiesText).not.toMatch(/"persons":\s*\[/);
    // The schema-aware render must NOT contain a legacy <ul> with the
    // displayHint="list" attribute (that would mean the legacy ListRenderer
    // ran instead of the SchemaAwareObjectRenderer).
    const legacyList = entitiesBlock!.querySelector('ul[data-display-hint="list"]');
    expect(legacyList).toBeNull();
  });

  it('renders summary as a paragraph (legacy displayHint path; unchanged)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    const summaryBlock = container.querySelector('[data-field-path="summary"]');
    expect(summaryBlock).not.toBeNull();
    const paragraph = summaryBlock!.querySelector('p[data-display-hint="paragraph"]');
    expect(paragraph).not.toBeNull();
    expect(paragraph!.textContent).toContain('This patent describes a method');
  });

  it('renders keywords as Fluent v9 Badges (legacy displayHint path; unchanged)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    const keywordsBlock = container.querySelector('[data-field-path="keywords"]');
    expect(keywordsBlock).not.toBeNull();
    const badgeRow = keywordsBlock!.querySelector('[data-display-hint="badge"]');
    expect(badgeRow).not.toBeNull();
    // Comma-split of the keywords string produces five tokens.
    const badgeText = badgeRow!.textContent ?? '';
    expect(badgeText).toContain('cryptography');
    expect(badgeText).toContain('zero-knowledge');
    expect(badgeText).toContain('set intersection');
    expect(badgeText).toContain('patent');
    expect(badgeText).toContain('privacy');
  });

  it('renders all four section headers (TL;DR / Summary / Keywords / Entities)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    // Headers are emitted as Text nodes with `fieldLabel` class; assert via
    // visible text within the widget root.
    const widgetRoot = container.querySelector('[data-testid="structured-output-stream-widget"]')!;
    const visibleText = widgetRoot.textContent ?? '';
    expect(visibleText).toContain('TL;DR');
    expect(visibleText).toContain('Summary');
    expect(visibleText).toContain('Keywords');
    expect(visibleText).toContain('Entities');
  });

  it('end-to-end widget render-state ends in "complete" after streaming finishes', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    streamSumChatFields(bus, STREAM_ID);

    const widgetRoot = container.querySelector('[data-testid="structured-output-stream-widget"]')!;
    expect(widgetRoot.getAttribute('data-render-state')).toBe('complete');
    expect(within(widgetRoot as HTMLElement).queryByText(/complete/i)).not.toBeNull();
  });
});
