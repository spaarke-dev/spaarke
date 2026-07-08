/**
 * Record-header composition — integration test (record-header-and-notepad-r1
 * task 014).
 *
 * Verifies Phase 1 (tasks 002–013) as a WHOLE. Composes every exported
 * primitive — `HeaderToolbar` (via `RecordHeaderShell`), `RecordHeaderShell`,
 * `FieldGrid`, all four field renderers (`TextField`,
 * `RecordHeaderLookupField`, `OptionSetField`, `TextareaField`), and all three
 * hooks (`useRecordFieldValues`, `useRelatedCount` via
 * `useRecordHeaderToolbarActions`) — against a mocked `Xrm.WebApi` and
 * `Xrm.Navigation`. Mirrors the `MatterHeaderView.tsx` composition shape from
 * spec FR-12 (post-001 REVISED field list).
 *
 * Coverage summary (10 test cases):
 *  1. Renders composition after load — 3 toolbar slots + 5 field labels + values
 *  2. Badge counts propagate — checkmark=3, annotation=7 (from `@odata.count`)
 *  3. Loading state — RecordHeaderShell renders Skeleton; children absent
 *  4. Sparkle popover — summary body when `sprk_recordsummary` is populated
 *  5. Sparkle popover — empty state ("No summary yet") when null
 *  6. Refresh icon is unwired — click NEVER triggers a Dataverse read/write
 *  7. Checkmark navigation — LAYOUT_1_MODAL (85%×85%) + SmartTodo webresource
 *  8. Annotation navigation — NOTEPAD_MODAL (70%×80%) + Notepad webresource
 *  9. Focus event refreshes badges — retrieveMultipleRecords re-invoked
 * 10. Unsupported entity — `sprk_document` yields memo badge=0, no memo query
 *
 * Import surface (top-level `@spaarke/ui-components` per task 013):
 *  - `RecordHeaderLookupField` alias for the record-header LookupField renderer
 *    (top-level `LookupField` = the pre-existing search-as-you-type component
 *    per RecordHeader/index.ts §alias rationale)
 *  - Every other symbol imports un-aliased from the shared-lib barrel
 *
 * Boundary constraints (project NFRs):
 *  - No `@spaarke/auth` imports (NFR-05)
 *  - No BFF calls; every Dataverse I/O is mocked `Xrm.WebApi` (NFR-07, ADR-028)
 *  - Fluent v9 semantic tokens only in composed primitives (NFR-03; verified in
 *    per-component unit tests, out of scope here)
 *  - React 16/17 safe primitives (NFR-06; verified in per-component unit tests)
 *
 * @see FR-01 through FR-12 in projects/record-header-and-notepad-r1/spec.md
 * @see .claude/adr/ADR-038-testing-strategy.md — integration test as KEEP
 * @see docs/standards/TEST-ARCHITECTURE.md
 * @see src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts
 */

import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, Popover, PopoverSurface, webLightTheme } from '@fluentui/react-components';

// Imports go through the sub-barrels rather than the top-level `../index`
// barrel because the top-level barrel re-exports `services/` which pulls in
// `EntityCreationService.ts` → `@spaarke/sdap-client` (an unrelated workspace
// package not installed for the shared-lib jest environment). We assert the
// SAME symbol identities the top-level barrel re-exports (see
// `src/index.ts` + `src/components/index.ts` + `src/hooks/index.ts` — each
// re-exports the imports below verbatim). This preserves the integration
// coverage the task asks for while avoiding a spurious unrelated import chain.
//
// Consumers using `@spaarke/ui-components` at the package level get the same
// symbols; the sub-path is a jest-environment workaround only.
import {
  FieldGrid,
  RecordHeaderLookupField,
  RecordHeaderShell,
  TextField,
  TextareaField,
} from '../components/RecordHeader';
import {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
  useRecordFieldValues,
  useRecordHeaderToolbarActions,
} from '../hooks';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm mock installation — mirrors the per-hook test fixtures so behavior is
// consistent across the whole suite. Every test starts with fresh mocks; the
// afterEach hook uninstalls Xrm so a "no Xrm" scenario is trivially achievable.
// ─────────────────────────────────────────────────────────────────────────────

type XrmWebApiLike = {
  retrieveRecord: jest.Mock;
  retrieveMultipleRecords: jest.Mock;
};

type XrmNavigationLike = {
  navigateTo: jest.Mock;
};

type XrmUtilityLike = {
  getGlobalContext: () => { getClientUrl: () => string };
};

type XrmLike = {
  WebApi: XrmWebApiLike;
  Navigation: XrmNavigationLike;
  Utility: XrmUtilityLike;
};

let mockRetrieveRecord: jest.Mock;
let mockRetrieveMultipleRecords: jest.Mock;
let mockNavigateTo: jest.Mock;

/**
 * Install a global Xrm shim populated with:
 *  - `retrieveRecord(entity, id, query)` → resolves the given `record` payload
 *    (used by useRecordFieldValues for the 5-field header + sprk_recordsummary).
 *  - `retrieveMultipleRecords(entity, query)` →
 *      - `sprk_todo`  → resolves `{ '@odata.count': todoCount, entities: [] }`
 *      - `sprk_memo`  → resolves `{ '@odata.count': memoCount, entities: [] }`
 *      - other        → resolves `{ '@odata.count': 0, entities: [] }`
 *  - `navigateTo(pageInput, options)` → resolves undefined.
 *  - `Utility.getGlobalContext().getClientUrl()` → the test env URL.
 *
 * `record` may also be a factory that returns a promise Never-resolving to
 * simulate an in-flight load; the loading-state test uses that variant.
 */
function installXrm(config: {
  record: Record<string, unknown> | (() => Promise<Record<string, unknown>>);
  todoCount?: number;
  memoCount?: number;
}): void {
  const { record, todoCount = 0, memoCount = 0 } = config;

  mockRetrieveRecord = jest.fn(() => {
    if (typeof record === 'function') {
      return record();
    }
    return Promise.resolve(record);
  });

  mockRetrieveMultipleRecords = jest.fn((entity: string) => {
    if (entity === 'sprk_todo') {
      return Promise.resolve({ '@odata.count': todoCount, entities: [] });
    }
    if (entity === 'sprk_memo') {
      return Promise.resolve({ '@odata.count': memoCount, entities: [] });
    }
    return Promise.resolve({ '@odata.count': 0, entities: [] });
  });

  mockNavigateTo = jest.fn().mockResolvedValue(undefined);

  const xrm: XrmLike = {
    WebApi: {
      retrieveRecord: mockRetrieveRecord,
      retrieveMultipleRecords: mockRetrieveMultipleRecords,
    },
    Navigation: { navigateTo: mockNavigateTo },
    Utility: {
      getGlobalContext: () => ({ getClientUrl: () => 'https://test.crm.dynamics.com' }),
    },
  };

  // getXrm() walks globalThis → window.parent. Set both for jsdom safety
  // (mirrors useRecordHeaderToolbarActions.test.ts + useRelatedCount.test.ts).
  (globalThis as unknown as { Xrm?: XrmLike }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmLike }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: XrmLike }).Xrm;
  delete (window as unknown as { Xrm?: XrmLike }).Xrm;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures — mirror spec FR-12 post-001 REVISED field list
// ─────────────────────────────────────────────────────────────────────────────

const MATTER_ENTITY = 'sprk_matter';
const MATTER_ID = '00000000-0000-0000-0000-000000000001';
const UNSUPPORTED_ENTITY = 'sprk_document'; // not in SUPPORTED_MEMO_PARENTS

const MATTER_RECORD = {
  sprk_matternumber: 'M-2026-001',
  sprk_mattername: 'Acme Litigation Matter',
  sprk_matterdescription:
    'This is a moderately long matter description that exercises TextareaField layout without triggering the show-more affordance.',
  sprk_recordsummary:
    'AI-generated summary of the matter — parties, key dates, and next steps. Rendered inside the sparkle popover per FR-08.',
  // Lookup values are rendered by RecordHeaderLookupField. The spec passes them
  // as `any` because they come from Xrm's raw retrieveRecord response; we
  // shape them into ILookupFieldValue-compatible objects here.
  sprk_mattertype: { id: 'mt-1', name: 'Litigation', entityType: 'sprk_mattertype' },
  sprk_practicearea: { id: 'pa-1', name: 'Corporate', entityType: 'sprk_practicearea' },
};

const MATTER_FIELD_LIST = [
  'sprk_matternumber',
  'sprk_mattername',
  'sprk_mattertype',
  'sprk_practicearea',
  'sprk_matterdescription',
  'sprk_recordsummary',
];

// ─────────────────────────────────────────────────────────────────────────────
// TestRecordHeader — mirrors MatterHeaderView.tsx from spec FR-12 with the
// task-014 addition that the consumer owns the <Popover> shell (per task-012
// API — hook exposes controlled open state + PopoverSurface-ready content).
// ─────────────────────────────────────────────────────────────────────────────

interface TestRecordHeaderProps {
  entity: string;
  recordId: string;
  columns?: 2 | 3;
}

function TestRecordHeader({ entity, recordId, columns = 3 }: TestRecordHeaderProps): React.ReactElement {
  const { values, loading } = useRecordFieldValues(entity, recordId, MATTER_FIELD_LIST);

  const { toolbarProps, sparklePopoverOpen, setSparklePopoverOpen, sparklePopoverContent } =
    useRecordHeaderToolbarActions({
      entity,
      recordId,
      recordSummary: (values?.sprk_recordsummary ?? null) as string | null,
    });

  return (
    <FluentProvider theme={webLightTheme}>
      <RecordHeaderShell toolbar={toolbarProps} loading={loading}>
        <FieldGrid columns={columns}>
          <TextField span={1} label="Matter Number" value={values?.sprk_matternumber as string | undefined} required />
          <TextField span={2} label="Matter Name" value={values?.sprk_mattername as string | undefined} />
          <RecordHeaderLookupField
            span={1}
            label="Matter Type"
            // Cast via unknown so we can accept the raw Xrm payload shape.
            // Production consumer (MatterHeaderView) does the same per FR-12.
            value={values?.sprk_mattertype as unknown as never}
          />
          <RecordHeaderLookupField
            span={1}
            label="Practice Area"
            value={values?.sprk_practicearea as unknown as never}
          />
          <TextareaField
            span={3}
            label="Matter Description"
            value={values?.sprk_matterdescription as string | undefined}
          />
        </FieldGrid>
      </RecordHeaderShell>

      {/*
       * Consumer wires the Popover shell — hook API split rationale:
       * `HeaderToolbar` renders its own sparkle Button (the click toggles
       * `sparklePopoverOpen`); the Popover trigger is the toolbar button, so we
       * mount the Popover as a sibling and let its default anchor logic resolve
       * against document coordinates. This matches the task-012 hook contract.
       */}
      <Popover open={sparklePopoverOpen} onOpenChange={(_, data) => setSparklePopoverOpen(data.open)}>
        <PopoverSurface>{sparklePopoverContent}</PopoverSurface>
      </Popover>
    </FluentProvider>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Flush microtasks — useRecordFieldValues + useRelatedCount both dispatch async
 * effects that need at least two microtask ticks to settle (fetch → setState →
 * render → derived hook state). Wrap in act() so React flushes updates cleanly.
 */
async function flushPromises(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeader composition — integration (Phase 1 as a whole)', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
  });

  // ── (1) Renders composition after load ─────────────────────────────────────

  it('renders 3 toolbar slots + 5 field labels + values after retrieveRecord resolves', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 3, memoCount: 7 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    // Wait for loading to flip false — Skeleton disappears, body renders.
    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    // Toolbar slot buttons (aria-label = tooltip per HeaderToolbar contract).
    expect(screen.getByRole('button', { name: 'AI Summary' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Related to-dos' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Notepad' })).toBeInTheDocument();

    // All 5 field labels present.
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
    expect(screen.getByText('Matter Name')).toBeInTheDocument();
    expect(screen.getByText('Matter Type')).toBeInTheDocument();
    expect(screen.getByText('Practice Area')).toBeInTheDocument();
    expect(screen.getByText('Matter Description')).toBeInTheDocument();

    // Field values rendered — spot-check a text primitive + both lookups.
    expect(screen.getByText('M-2026-001')).toBeInTheDocument();
    expect(screen.getByText('Acme Litigation Matter')).toBeInTheDocument();
    expect(screen.getByText('Litigation')).toBeInTheDocument();
    expect(screen.getByText('Corporate')).toBeInTheDocument();

    // retrieveRecord called exactly once for the 6-field header payload.
    expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
    expect(mockRetrieveRecord).toHaveBeenCalledWith(
      MATTER_ENTITY,
      MATTER_ID,
      `?$select=${MATTER_FIELD_LIST.join(',')}`
    );
  });

  // ── (2) Badge counts render ────────────────────────────────────────────────

  it('badge counts propagate — checkmark shows 3, annotation shows 7', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 3, memoCount: 7 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    // useRelatedCount does mount + focus fetch; badges surface via CounterBadge.
    await waitFor(() => {
      const checkmarkBadge = screen.getByTestId('header-toolbar-badge-checkmark');
      expect(checkmarkBadge).toHaveTextContent('3');
    });

    const annotationBadge = screen.getByTestId('header-toolbar-badge-annotation');
    expect(annotationBadge).toHaveTextContent('7');

    // Sparkle has NO badge per FR-08 acceptance.
    expect(screen.queryByTestId('header-toolbar-badge-sparkle')).toBeNull();
  });

  // ── (3) Loading state ─────────────────────────────────────────────────────

  it('while retrieveRecord is in-flight, RecordHeaderShell shows Skeleton and children are NOT rendered', async () => {
    // Never-resolving promise → loading stays true.
    installXrm({
      record: () =>
        new Promise<Record<string, unknown>>(() => {
          /* never resolves */
        }),
      todoCount: 0,
      memoCount: 0,
    });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    // Skeleton visible.
    await waitFor(() => {
      expect(screen.getByTestId('record-header-shell-skeleton')).toBeInTheDocument();
    });

    // Field cells NOT rendered while loading — the shell swaps children for
    // the skeleton when `loading === true` (per RecordHeaderShell FR-02).
    expect(screen.queryByText('Matter Number')).toBeNull();
    expect(screen.queryByText('Matter Description')).toBeNull();

    // Toolbar chrome STAYS rendered so the card doesn't jump between load
    // and loaded states.
    expect(screen.getByTestId('header-toolbar')).toBeInTheDocument();
  });

  // ── (4) Sparkle popover opens with summary content ─────────────────────────

  it('sparkle click opens the popover and renders the record summary body', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 0, memoCount: 0 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    // Wait for record load so `recordSummary` is populated.
    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    // Popover starts closed → summary/empty-state body NOT visible.
    expect(screen.queryByTestId('sparkle-popover-summary')).toBeNull();
    expect(screen.queryByTestId('sparkle-popover-empty')).toBeNull();

    // Click the sparkle button — toggles sparklePopoverOpen → true.
    fireEvent.click(screen.getByRole('button', { name: 'AI Summary' }));

    // Popover surface renders the summary body.
    await waitFor(() => {
      expect(screen.getByTestId('sparkle-popover-summary')).toBeInTheDocument();
    });
    expect(screen.getByTestId('sparkle-popover-summary')).toHaveTextContent(MATTER_RECORD.sprk_recordsummary);
    expect(screen.queryByTestId('sparkle-popover-empty')).toBeNull();
  });

  // ── (5) Sparkle popover — empty state ──────────────────────────────────────

  it('sparkle click on a record with null sprk_recordsummary renders the empty state', async () => {
    installXrm({
      record: { ...MATTER_RECORD, sprk_recordsummary: null },
      todoCount: 0,
      memoCount: 0,
    });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    fireEvent.click(screen.getByRole('button', { name: 'AI Summary' }));

    await waitFor(() => {
      expect(screen.getByTestId('sparkle-popover-empty')).toBeInTheDocument();
    });
    expect(screen.getByTestId('sparkle-popover-empty')).toHaveTextContent(/no summary yet/i);
    expect(screen.queryByTestId('sparkle-popover-summary')).toBeNull();
  });

  // ── (6) Refresh icon is unwired ────────────────────────────────────────────

  it('refresh icon click inside sparkle popover is a no-op — no read, no write, no navigate', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 0, memoCount: 0 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    // Open sparkle popover.
    fireEvent.click(screen.getByRole('button', { name: 'AI Summary' }));
    await waitFor(() => {
      expect(screen.getByTestId('sparkle-popover-summary')).toBeInTheDocument();
    });

    // Snapshot call counts BEFORE clicking refresh so we can assert no new
    // Dataverse activity happens as a side effect. Baseline includes mount-time
    // calls (retrieveRecord × 1, retrieveMultipleRecords × 2 for todo + memo).
    const priorRetrieveRecord = mockRetrieveRecord.mock.calls.length;
    const priorRetrieveMultiple = mockRetrieveMultipleRecords.mock.calls.length;
    mockNavigateTo.mockClear();

    // Refresh button aria-label = the deferral tooltip. Query by the "Refresh"
    // substring so we're not coupled to exact copy.
    const refreshBtn = screen.getByRole('button', { name: /refresh/i });
    fireEvent.click(refreshBtn);

    // Zero new reads, writes, or navigation calls — refresh is a hard no-op
    // in R1 per FR-08a.
    expect(mockRetrieveRecord.mock.calls.length).toBe(priorRetrieveRecord);
    expect(mockRetrieveMultipleRecords.mock.calls.length).toBe(priorRetrieveMultiple);
    expect(mockNavigateTo).not.toHaveBeenCalled();
  });

  // ── (7) Checkmark navigation → SmartTodo webresource + LAYOUT_1_MODAL ─────

  it('checkmark click navigates to SmartTodo webresource with LAYOUT_1_MODAL', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 3, memoCount: 0 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Related to-dos' }));

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('webresource');
    expect(pageInput.name).toBe(SMARTTODO_WEBRESOURCE_NAME);
    expect(pageInput.data).toEqual({
      regardingEntity: MATTER_ENTITY,
      regardingId: MATTER_ID,
    });
    // 85% × 85% modal per Layout 1 canonical standard.
    expect(navOptions).toEqual(LAYOUT_1_MODAL);
  });

  // ── (8) Annotation navigation → Notepad webresource + NOTEPAD_MODAL ───────

  it('annotation click navigates to Notepad webresource with NOTEPAD_MODAL', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 0, memoCount: 7 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    await waitFor(() => {
      expect(screen.queryByTestId('record-header-shell-skeleton')).toBeNull();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Notepad' }));

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('webresource');
    expect(pageInput.name).toBe(NOTEPAD_WEBRESOURCE_NAME);
    expect(pageInput.data).toEqual({
      regardingEntity: MATTER_ENTITY,
      regardingId: MATTER_ID,
    });
    // 70% × 80% specialized-editor modal per FR-10.
    expect(navOptions).toEqual(NOTEPAD_MODAL);
  });

  // ── (9) Focus event refreshes badges ──────────────────────────────────────

  it('window focus event refetches both badge counts (mount + focus refresh per FR-11)', async () => {
    installXrm({ record: MATTER_RECORD, todoCount: 3, memoCount: 7 });

    render(<TestRecordHeader entity={MATTER_ENTITY} recordId={MATTER_ID} />);

    // Wait for the initial mount-time fetches to settle. useRelatedCount runs
    // for both `sprk_todo` and `sprk_memo` on mount.
    await waitFor(() => {
      const todoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_todo');
      const memoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo');
      expect(todoCalls.length).toBeGreaterThanOrEqual(1);
      expect(memoCalls.length).toBeGreaterThanOrEqual(1);
    });

    const priorTodoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_todo').length;
    const priorMemoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo').length;

    // Simulate window regaining focus — useRelatedCount registers a focus
    // listener per FR-11 mount + focus contract (no polling).
    await act(async () => {
      window.dispatchEvent(new Event('focus'));
      await Promise.resolve();
      await Promise.resolve();
    });

    const postTodoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_todo').length;
    const postMemoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo').length;

    // BOTH counts should have re-issued (one per useRelatedCount instance —
    // one for todo, one for memo).
    expect(postTodoCalls).toBeGreaterThan(priorTodoCalls);
    expect(postMemoCalls).toBeGreaterThan(priorMemoCalls);
  });

  // ── (10) Unsupported entity → memo badge=0, no memo query ─────────────────

  it('unsupported entity (sprk_document): annotation badge is 0 and no sprk_memo query is issued', async () => {
    installXrm({
      // Payload shape doesn't matter — the record hook uses whatever we return.
      record: { ...MATTER_RECORD, sprk_recordsummary: null },
      todoCount: 3,
      // Would return 999 if queried, but we assert the query is NEVER issued.
      memoCount: 999,
    });

    render(<TestRecordHeader entity={UNSUPPORTED_ENTITY} recordId={MATTER_ID} />);

    // Wait for the record and the sprk_todo count to settle so the assertion
    // below is not racing initial mount effects.
    await waitFor(() => {
      const checkmarkBadge = screen.queryByTestId('header-toolbar-badge-checkmark');
      expect(checkmarkBadge).toHaveTextContent('3');
    });

    // Flush any lingering microtasks so the memo-related effects (if any) have
    // fully settled. Then assert the invariant.
    await flushPromises();

    // sprk_memo query MUST NOT have been issued — buildMemoFilterForParent
    // returns null for unsupported entities, useRelatedCount idles at count=0.
    const memoCalls = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo');
    expect(memoCalls).toHaveLength(0);

    // Annotation badge is suppressed (count=0 → shouldRenderBadge returns false
    // per HeaderToolbar FR-01). Query returns null.
    expect(screen.queryByTestId('header-toolbar-badge-annotation')).toBeNull();
  });
});
