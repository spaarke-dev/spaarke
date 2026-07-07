/**
 * StructuredOutputStreamWidget — integration regression test for the
 * "Summarize this only" dispatch flow.
 *
 * Post-cutover contract (ai-architecture-redesign-r1 task 046 / FR-P3-07,
 * amended ADR-037): the ONE client dispatch helper (`dispatchConsumer` in
 * `@spaarke/ui-components`) bridges the terminal `complete` AnalysisChunk
 * onto SECTION events — one `section_started` + `section_completed` pair per
 * top-level result key. Strings ride `finalContent`; arrays/objects ride
 * `finalStructuredData`. The widget renders sections via `SectionRenderer`
 * with value-shape-typed structured rendering:
 *   - array of strings → bulleted list  (heir to the task 040 contract)
 *   - flat record      → labeled rows   (heir to the task 041 contract)
 *
 * Origin bug lineage (R6 Hotfix Wave B-G9a, 2026-06-10): tldr/entities once
 * rendered as raw JSON text because the render pipeline lacked type-aware
 * dispatch. The negative assertions below keep that failure mode dead in the
 * section pipeline: NO raw JSON syntax may appear in the rendered DOM for
 * SUM-CHAT@v1-shaped payloads.
 *
 * The event sequence below mirrors `dispatchConsumer.consumeChunk`'s
 * synthesized emissions for a terminal complete chunk carrying
 * `{ tldr: string[], summary: string, keywords: string, entities: object }`
 * (declaration order preserved via `sectionIndex`). Kept in sync with that
 * source via the source-contract suite (a) — importing the dispatcher would
 * pull `@spaarke/ui-components` → `d3-force` ESM which ts-jest cannot
 * transform (same constraint as `FilePreviewContextWidget.summarize-only.
 * test.tsx`).
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
// Test fixtures — the SUM-CHAT@v1 terminal result payload
// ---------------------------------------------------------------------------

const STREAM_ID = 'sess-046-dispatch';

const TLDR_ITEMS = [
  'A method for private intersection of authenticated data.',
  'The system uses zero-knowledge proofs.',
  'Private transactions remain confidential.',
];

const SUMMARY_TEXT =
  'This patent describes a method for performing set-intersection operations on authenticated data without revealing the underlying values to either party.';

const KEYWORDS_TEXT = 'cryptography, zero-knowledge, set intersection, patent, privacy';

const ENTITIES_OBJECT = {
  organizations: ['Acme Corp.', 'Wayne Industries'],
  persons: ['Alice Smith', 'Bob Jones'],
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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

/**
 * Mirror of `dispatchConsumer.consumeChunk`'s section bridge for a terminal
 * complete chunk carrying the SUM-CHAT@v1 result: streaming_started, then one
 * section pair per top-level key (declaration order; strings → finalContent,
 * arrays/objects → finalStructuredData), then streaming_complete.
 */
function dispatchTerminalEnvelope(bus: PaneEventBus, streamId: string): void {
  const result: Record<string, unknown> = {
    tldr: TLDR_ITEMS,
    summary: SUMMARY_TEXT,
    keywords: KEYWORDS_TEXT,
    entities: ENTITIES_OBJECT,
  };
  act(() => {
    bus.dispatch('workspace', { type: 'streaming_started', streamId });
    let index = 0;
    for (const [key, value] of Object.entries(result)) {
      bus.dispatch('workspace', {
        type: 'section_started',
        streamId,
        sectionName: key,
        sectionIndex: index++,
      });
      bus.dispatch('workspace', {
        type: 'section_completed',
        streamId,
        sectionName: key,
        ...(typeof value === 'string' ? { finalContent: value } : { finalStructuredData: value }),
      });
    }
    bus.dispatch('workspace', { type: 'streaming_complete', streamId, completionStatus: 'complete' });
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
// (a) Source-code contracts — dispatcher payload + section bridge
// ---------------------------------------------------------------------------

describe('dispatchSummarizeOnly + dispatchConsumer — source contracts (task 046)', () => {
  it('FilePreviewContextWidget.tsx dispatchSummarizeOnly still passes outputSchema (B-G9a regression anchor)', () => {
    const dispatcherPath = path.resolve(__dirname, '../../context/FilePreviewContextWidget.tsx');
    const source = fs.readFileSync(dispatcherPath, 'utf-8');
    expect(source).toMatch(/SUM_CHAT_OUTPUT_SCHEMA/);
    expect(source).toMatch(/outputSchema:\s*SUM_CHAT_OUTPUT_SCHEMA/);
  });

  it('dispatchConsumer bridges the terminal complete chunk onto section events (NOT the retired per-field vocabulary)', () => {
    // Source-level contract on the ONE dispatch helper: it emits
    // section_started/section_completed and contains no retired-vocabulary
    // emission. Importing it would drag the ui-components barrel into the
    // ts-jest graph (d3-force ESM), so assert at source level.
    const dispatcherPath = path.resolve(
      __dirname,
      '../../../../../Spaarke.UI.Components/src/services/dispatchConsumer.ts'
    );
    const source = fs.readFileSync(dispatcherPath, 'utf-8');
    expect(source).toMatch(/type:\s*'section_started'/);
    expect(source).toMatch(/type:\s*'section_completed'/);
    // Retired token constructed dynamically so the widget layer stays
    // grep-zero for the deleted vocabulary (NFR-08 evidence rule).
    const retiredToken = ['field', 'delta'].join('_');
    expect(source).not.toContain(`'${retiredToken}'`);
  });

  it('SUM_CHAT_OUTPUT_SCHEMA still mirrors the SUM-CHAT@v1 action output schema contract', () => {
    expect(SUM_CHAT_OUTPUT_SCHEMA.type).toBe('object');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.tldr.type).toBe('array');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.summary.type).toBe('string');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.keywords.type).toBe('string');
    expect(SUM_CHAT_OUTPUT_SCHEMA.properties!.entities.type).toBe('object');
  });
});

// ---------------------------------------------------------------------------
// (b)-(g) End-to-end widget render with the section-bridged terminal envelope
// ---------------------------------------------------------------------------

describe('Summarize-this-only dispatch payload renders cleanly via sections (task 046)', () => {
  it('renders tldr (string[]) as a bulleted list — NOT raw JSON (B-G9a heir)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const tldrSection = container.querySelector('[data-section-name="tldr"]');
    expect(tldrSection).not.toBeNull();

    // POSITIVE: value-shape-typed bulleted list.
    const list = tldrSection!.querySelector('ul[data-section-body="structured-list"]');
    expect(list).not.toBeNull();
    const items = list!.querySelectorAll('li');
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toBe(TLDR_ITEMS[0]);
    expect(items[2].textContent).toBe(TLDR_ITEMS[2]);

    // NEGATIVE (B-G9a failure mode stays dead): no raw JSON syntax and no
    // compact-JSON fallback for this well-shaped payload.
    expect(tldrSection!.querySelector('pre[data-section-body="structured"]')).toBeNull();
    const text = tldrSection!.textContent ?? '';
    expect(text).not.toMatch(/\["/);
    expect(text).not.toMatch(/"\]/);
    expect(text).not.toMatch(/\\"/);
  });

  it('renders entities (flat record) as labeled rows with nested lists — NOT raw JSON (B-G9a heir)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const entitiesSection = container.querySelector('[data-section-name="entities"]');
    expect(entitiesSection).not.toBeNull();

    const record = entitiesSection!.querySelector('[data-section-body="structured-record"]');
    expect(record).not.toBeNull();

    const orgsRow = record!.querySelector('[data-prop-key="organizations"]');
    expect(orgsRow).not.toBeNull();
    expect(orgsRow!.textContent).toContain('Organizations');
    const orgsItems = orgsRow!.querySelectorAll('li');
    expect(orgsItems).toHaveLength(2);
    expect(orgsItems[0].textContent).toBe('Acme Corp.');
    expect(orgsItems[1].textContent).toBe('Wayne Industries');

    const personsRow = record!.querySelector('[data-prop-key="persons"]');
    expect(personsRow).not.toBeNull();
    expect(personsRow!.textContent).toContain('Persons');
    expect(personsRow!.querySelectorAll('li')).toHaveLength(2);

    // NEGATIVE: no raw JSON syntax anywhere in the section.
    const text = entitiesSection!.textContent ?? '';
    expect(text).not.toMatch(/"organizations":\s*\[/);
    expect(text).not.toMatch(/"persons":\s*\[/);
    expect(entitiesSection!.querySelector('pre[data-section-body="structured"]')).toBeNull();
  });

  it('renders summary (string) as section text', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const summarySection = container.querySelector('[data-section-name="summary"]');
    expect(summarySection).not.toBeNull();
    const body = summarySection!.querySelector('[data-section-body="text"]');
    expect(body).not.toBeNull();
    expect(body!.textContent).toContain('This patent describes a method');
  });

  it('renders keywords (string) as section text', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const keywordsSection = container.querySelector('[data-section-name="keywords"]');
    expect(keywordsSection).not.toBeNull();
    expect(keywordsSection!.textContent).toContain('cryptography');
    expect(keywordsSection!.textContent).toContain('privacy');
  });

  it('renders all four section headers in declaration order (sectionIndex)', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const headers = Array.from(container.querySelectorAll('[data-section-header]')).map(el =>
      el.getAttribute('data-section-header')
    );
    expect(headers).toEqual(['tldr', 'summary', 'keywords', 'entities']);
    // Humanized labels visible.
    const visibleText = container.textContent ?? '';
    expect(visibleText).toContain('Summary');
    expect(visibleText).toContain('Keywords');
    expect(visibleText).toContain('Entities');
  });

  it('end-to-end widget lands in section mode with render-state "complete"', () => {
    const bus = new PaneEventBus();
    const { container } = renderWidgetWithData(buildSumChatWidgetData(STREAM_ID), bus);
    dispatchTerminalEnvelope(bus, STREAM_ID);

    const widgetRoot = container.querySelector('[data-testid="structured-output-stream-widget"]')!;
    expect(widgetRoot.getAttribute('data-render-state')).toBe('complete');
    expect(widgetRoot.getAttribute('data-render-mode')).toBe('sections');
    expect(within(widgetRoot as HTMLElement).queryByText(/complete/i)).not.toBeNull();
  });
});
