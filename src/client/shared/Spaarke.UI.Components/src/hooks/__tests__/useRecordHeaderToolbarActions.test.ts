/**
 * useRecordHeaderToolbarActions Hook Unit Tests
 *
 * FR-07 / FR-08 / FR-08a / FR-09 / FR-10 / FR-11
 * (record-header-and-notepad-r1): validates the LINCHPIN three-slot toolbar-
 * actions hook consumed by every per-entity RecordHeader PCF.
 *
 * Coverage areas:
 *  - Slot enumeration + enabled-flag filtering (FR-07)
 *  - Sparkle popover-toggle behavior (FR-08 REVISED) — no navigateTo call
 *  - Sparkle popover body content (summary vs empty state) (FR-08)
 *  - Unwired refresh icon (FR-08a) — click is no-op, tooltip states deferral
 *  - Checkmark launches SmartTodo webresource at LAYOUT_1_MODAL (FR-09)
 *  - Annotation launches Notepad webresource at NOTEPAD_MODAL (FR-10)
 *  - Badge counts propagate from useRelatedCount (FR-11)
 *  - Entity-specific memo lookup — supported vs unsupported (FR-11 + ADR-024)
 *  - Xrm-undefined resilience — sparkle still works; nav handlers no-op
 */

import * as React from 'react';
import { act, render, renderHook, screen, fireEvent } from '@testing-library/react';

import { useRecordHeaderToolbarActions, __testables } from '../useRecordHeaderToolbarActions';
import {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
} from '../toolbarLaunchDefaults';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm mocks — mirror the fixture shape from useRelatedCount.test.ts /
// useRecordFieldValues.test.ts so the whole hook suite behaves consistently.
// ─────────────────────────────────────────────────────────────────────────────

type XrmWebApiLike = {
  retrieveMultipleRecords: jest.Mock;
};
type XrmNavigationLike = {
  navigateTo: jest.Mock;
};
type XrmLike = {
  WebApi: XrmWebApiLike;
  Navigation: XrmNavigationLike;
};

let mockRetrieveMultipleRecords: jest.Mock;
let mockNavigateTo: jest.Mock;

/**
 * Install a global Xrm shim that returns `todoCount` for the sprk_todo query
 * and `memoCount` for the sprk_memo query. `memoCount = 0` when the memo
 * filter is unsupported (buildMemoFilterForParent returns null → hook idles).
 */
function installXrm(counts: { todoCount?: number; memoCount?: number } = {}): void {
  const { todoCount = 0, memoCount = 0 } = counts;
  // Mock the ACTUAL Xrm.WebApi shape: `{ entities: Array<...>, nextLink? }`.
  // Previously this mock fabricated `@odata.count` which is what let the
  // v1.0.11 → v1.0.15 badge silent-zero bug ship — real Xrm strips that
  // annotation. See `.claude/patterns/pcf/xrm-webapi-related-count.md` and
  // the parallel fixture in `useRelatedCount.test.ts` (makeEntities helper).
  const makeEntities = (n: number): Array<Record<string, string>> =>
    Array.from({ length: n }, (_, i) => ({ createdon: `2026-01-0${(i % 9) + 1}T00:00:00Z` }));
  mockRetrieveMultipleRecords = jest.fn((entity: string) => {
    if (entity === 'sprk_todo') {
      return Promise.resolve({ entities: makeEntities(todoCount) });
    }
    if (entity === 'sprk_memo') {
      return Promise.resolve({ entities: makeEntities(memoCount) });
    }
    return Promise.resolve({ entities: [] });
  });
  mockNavigateTo = jest.fn().mockResolvedValue(undefined);

  const xrm: XrmLike = {
    WebApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords },
    Navigation: { navigateTo: mockNavigateTo },
  };
  // getXrm() walks globalThis → window.Xrm — set both to be safe across
  // jsdom variants (mirrors useRelatedCount.test.ts).
  (globalThis as unknown as { Xrm?: XrmLike }).Xrm = xrm;
  (window as unknown as { Xrm?: XrmLike }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: XrmLike }).Xrm;
  delete (window as unknown as { Xrm?: XrmLike }).Xrm;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const MATTER_GUID = '00000000-0000-0000-0000-000000000001';
const MATTER_ENTITY = 'sprk_matter';
const UNSUPPORTED_ENTITY = 'sprk_document'; // NOT in SUPPORTED_MEMO_PARENTS

// Wait for microtasks (in-flight Xrm.WebApi promises) to settle. Using a
// non-jest-fake timer with await lets useEffect + async setState finish.
async function flushPromises(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('useRecordHeaderToolbarActions', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
  });

  // ── FR-07 Slot enumeration + enabled-flag filtering ───────────────────────
  //
  // v1.0.10 note: the `sparkle` slot was RETIRED from this hook — the AI
  // summary popover is now the shared `<AiSummaryPopover>` component
  // rendered by consumers alongside the toolbar (see SHARED-UI-COMPONENTS
  // -GUIDE.md §AiSummaryPopover). The hook emits ONLY `checkmark` +
  // `annotation`. Old sparkle-slot tests (7 of them, including sparkle
  // popover content + refresh icon) were deleted in the /test-diet pass
  // on 2026-07-05 per ADR-038 §7 (they asserted removed behavior — B10
  // coverage-filler + B6 mirror to removed function).

  it('emits checkmark + annotation slots by default (sparkle is now a sibling AiSummaryPopover, not a hook slot)', async () => {
    installXrm();
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
        recordSummary: null,
      })
    );

    await flushPromises();

    const keys = result.current.toolbarProps.iconSlots.map(s => s.key);
    expect(keys).toEqual(['checkmark', 'annotation']);
    expect(result.current.toolbarProps.iconSlots).toHaveLength(2);
  });

  it('omits the checkmark slot when enabled.checkmark=false', async () => {
    installXrm();
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
        enabled: { checkmark: false },
      })
    );

    await flushPromises();

    const keys = result.current.toolbarProps.iconSlots.map(s => s.key);
    expect(keys).toEqual(['annotation']);
  });

  it('omits the annotation slot when enabled.annotation=false', async () => {
    installXrm();
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
        enabled: { annotation: false },
      })
    );

    await flushPromises();

    const keys = result.current.toolbarProps.iconSlots.map(s => s.key);
    expect(keys).toEqual(['checkmark']);
  });

  // ── FR-09 Checkmark → SmartTodo webresource + LAYOUT_1_MODAL ──────────────

  it('checkmark onClick calls Xrm.Navigation.navigateTo with SmartTodo webresource + LAYOUT_1_MODAL', async () => {
    installXrm({ todoCount: 3 });
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();

    const checkmarkSlot = result.current.toolbarProps.iconSlots.find(s => s.key === 'checkmark');
    expect(checkmarkSlot).toBeDefined();

    act(() => {
      void checkmarkSlot!.onClick();
    });

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('webresource');
    // Fix (2026-07-04): source uses `webresourceName` (per PCF pageType gotcha
    // documented above buildNotepadLaunchData); test previously asserted `.name`
    // which was pre-existing broken. Aligned with source contract.
    expect(pageInput.webresourceName).toBe(SMARTTODO_WEBRESOURCE_NAME);
    // DEF-11 (2026-07-04): checkmark now emits SmartTodo's `openTodos` contract
    // so the Kanban pre-filters to the current record's related to-dos. Data is
    // a URL-encoded query STRING, not an object (see PCF gotcha above).
    expect(pageInput.data).toBe(
      `action=openTodos&regardingType=${encodeURIComponent(MATTER_ENTITY)}&regardingId=${encodeURIComponent(MATTER_GUID)}`
    );
    // Layout 1: 85% × 85% modal (target=2, position=1).
    expect(navOptions).toEqual(LAYOUT_1_MODAL);
  });

  // ── FR-10 Annotation → Notepad webresource + NOTEPAD_MODAL ────────────────

  it('annotation onClick calls Xrm.Navigation.navigateTo with Notepad webresource + NOTEPAD_MODAL', async () => {
    installXrm({ memoCount: 5 });
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();

    const annotationSlot = result.current.toolbarProps.iconSlots.find(s => s.key === 'annotation');
    expect(annotationSlot).toBeDefined();

    act(() => {
      void annotationSlot!.onClick();
    });

    expect(mockNavigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = mockNavigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('webresource');
    // Fix (2026-07-04): source uses `webresourceName` (per PCF pageType gotcha);
    // test previously asserted `.name` which was pre-existing broken.
    expect(pageInput.webresourceName).toBe(NOTEPAD_WEBRESOURCE_NAME);
    // Notepad's data is the plain `regardingEntity=<entity>&regardingId=<id>`
    // form per spec NFR-09 launch contract (string, not object).
    expect(pageInput.data).toBe(
      `regardingEntity=${encodeURIComponent(MATTER_ENTITY)}&regardingId=${encodeURIComponent(MATTER_GUID)}`
    );
    // Notepad modal is 70% × 80% (distinct from Layout 1).
    expect(navOptions).toEqual(NOTEPAD_MODAL);
  });

  // ── FR-11 Badge counts (todo + memo — supported entity) ───────────────────

  it('checkmark badge reflects the mocked sprk_todo count from useRelatedCount', async () => {
    installXrm({ todoCount: 7 });
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    // Wait for the count fetch to resolve + state to propagate.
    await flushPromises();
    await flushPromises();

    const checkmark = result.current.toolbarProps.iconSlots.find(s => s.key === 'checkmark');
    expect(checkmark?.badge).toBe(7);
  });

  // ── v1.0.2 regression guards: sprk_todo uses ADR-024 dual-field ──────────

  it('checkmark badge query uses the entity-specific sprk_todo lookup (NOT the polymorphic regardingobjectid)', async () => {
    // v1.0.0 bug: hook built `_regardingobjectid_value eq {guid}` filter for
    // sprk_todo, but sprk_todo does not have a polymorphic regarding column
    // (verified via Dataverse MCP describe 2026-07-03). Dataverse returned
    // 400 "Could not find a property named '_regardingobjectid_value'".
    installXrm({ todoCount: 3 });
    renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();
    await flushPromises();

    const todoCallLog = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_todo');
    expect(todoCallLog.length).toBeGreaterThan(0);
    // Positive assertion: entity-specific ADR-024 lookup is used.
    expect(todoCallLog[0][1]).toContain('_sprk_regardingmatter_value eq');
    // Negative assertion: legacy polymorphic filter is NOT used.
    expect(todoCallLog[0][1]).not.toContain('_regardingobjectid_value');
  });

  it('checkmark badge = 0 for an UNSUPPORTED parent (playbook is not in SUPPORTED_TODO_PARENTS)', async () => {
    installXrm({ todoCount: 999 });
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: 'sprk_playbook',
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();
    await flushPromises();

    const checkmark = result.current.toolbarProps.iconSlots.find(s => s.key === 'checkmark');
    expect(checkmark?.badge).toBe(0);

    // No sprk_todo query issued when the parent is unsupported.
    const todoCallLog = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_todo');
    expect(todoCallLog).toHaveLength(0);
  });

  // ── v1.0.2 title prop propagates to toolbarProps ─────────────────────────

  it('forwards the optional title prop through to toolbarProps.title', async () => {
    installXrm();
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
        title: 'Matter #A-2026-42',
      })
    );

    await flushPromises();
    expect(result.current.toolbarProps.title).toBe('Matter #A-2026-42');
  });

  it('leaves toolbarProps.title undefined when the option is omitted', async () => {
    installXrm();
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();
    expect(result.current.toolbarProps.title).toBeUndefined();
  });

  it('annotation badge reflects the mocked sprk_memo count for a SUPPORTED parent (sprk_matter)', async () => {
    installXrm({ memoCount: 4 });
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();
    await flushPromises();

    const annotation = result.current.toolbarProps.iconSlots.find(s => s.key === 'annotation');
    expect(annotation?.badge).toBe(4);

    // Verify the memo count query actually issued (supported parent path).
    const memoCallLog = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo');
    expect(memoCallLog.length).toBeGreaterThan(0);
    // The query MUST use the entity-specific ADR-024 lookup — verify by
    // substring match on the query URL.
    expect(memoCallLog[0][1]).toContain('_sprk_regardingmatter_value eq');
  });

  // ── FR-11 + ADR-024 Unsupported memo parent → memo badge = 0, no query ───

  it('annotation badge = 0 for an UNSUPPORTED parent (sprk_document not in SUPPORTED_MEMO_PARENTS)', async () => {
    installXrm({ memoCount: 999 }); // even if the mock returns 999, we should NOT read it
    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: UNSUPPORTED_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();
    await flushPromises();

    const annotation = result.current.toolbarProps.iconSlots.find(s => s.key === 'annotation');
    expect(annotation?.badge).toBe(0);

    // Verify NO sprk_memo query was issued — the hook must idle when the
    // parent entity is not in SUPPORTED_MEMO_PARENTS (memo filter = null).
    const memoCallLog = mockRetrieveMultipleRecords.mock.calls.filter(c => c[0] === 'sprk_memo');
    expect(memoCallLog).toHaveLength(0);
  });

  // ── Xrm unavailable resilience — checkmark + annotation clicks silently no-op ───
  //
  // v1.0.10 note: sparkle was retired from the hook, so its "still toggles"
  // clause from the pre-v1.0.10 version of this test was dropped in the
  // 2026-07-05 /test-diet pass. What remains is the invariant that matters
  // for a broken host: nav-related clicks must NOT throw.

  it('when Xrm is undefined, checkmark + annotation clicks silently no-op (no throw)', async () => {
    // Explicitly NO installXrm() — simulate unit-test / non-MDA host.
    uninstallXrm();

    const { result } = renderHook(() =>
      useRecordHeaderToolbarActions({
        entity: MATTER_ENTITY,
        recordId: MATTER_GUID,
      })
    );

    await flushPromises();

    const checkmark = result.current.toolbarProps.iconSlots.find(s => s.key === 'checkmark');
    const annotation = result.current.toolbarProps.iconSlots.find(s => s.key === 'annotation');

    // Nav-related clicks must NOT throw; they silently no-op.
    expect(() => {
      act(() => {
        void checkmark!.onClick();
        void annotation!.onClick();
      });
    }).not.toThrow();
  });
});
