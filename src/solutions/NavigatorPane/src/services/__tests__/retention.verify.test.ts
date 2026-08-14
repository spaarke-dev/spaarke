/**
 * retention.verify.test.ts — task 081, spec FR-05 verification / Success
 * Criterion 7 (spaarke-side-pane-navigation-history-r1, Phase 8 Security &
 * retention verification).
 *
 * VERIFICATION ONLY — this test does NOT modify `navigatorCaptureService.ts`
 * or `navItemRepository.ts` (the task-031 prune-on-write retention logic). It
 * exercises the REAL, already-built 30-day retention behavior from the
 * `@spaarke/ui-components` package the way an actual NavigatorPane consumer
 * imports it (`@spaarke/ui-components/services/navigator/...`, same subpath
 * style `RecentTab.tsx`/`PinnedTab.tsx` already use — see those files'
 * `import ... from '@spaarke/ui-components/services/navigator/navItemRepository'`
 * lines), rather than importing across the package boundary via a relative
 * `../../../../client/shared/...` path.
 *
 * CANONICAL-REFERENCE CORRECTION (task 081 dispatch): the task POML's
 * `relevant-files` cites a `NavigatorPane/src/services/retentionService.ts`
 * that was never created by task 031. The prune-on-write retention logic
 * actually lives in the shared lib:
 *   - `navItemRepository.ts`'s `deleteHistoryItemsOlderThan(ownerId, cutoff)`
 *     — the owner+History-scoped OData delete
 *     (`_ownerid_value eq {ownerId} and sprk_type eq 100000000 and
 *     sprk_lastvisited lt {iso}`).
 *   - `navigatorCaptureService.ts`'s `startNavigatorCapture` poll loop, which
 *     calls that delete inline, ONLY after a successful history upsert this
 *     tick, with `HISTORY_RETENTION_DAYS = 30`; a prune failure routes
 *     through `options.onError` and is non-fatal.
 * This file verifies THAT actual code, not an imagined `retentionService.ts`.
 * (`navigatorCaptureService.test.ts` in the shared lib already carries the
 * task-031 unit-level retention describe block this test complements at the
 * NavigatorPane-consumer/package-boundary level with an explicit isolation
 * scenario matrix — old-history-pruned / pin-survives / other-user-untouched
 * — asserted together in one seeded scenario per Success Criterion 7.)
 *
 * Harness: mirrors `navigatorCaptureService.test.ts`'s fake `Xrm.WebApi` (a
 * mutable `store` array; `retrieveMultipleRecords` parses the same OData
 * filter shape the real repository emits, honoring `_ownerid_value eq`,
 * `sprk_type eq`, and `sprk_lastvisited lt`; `createRecord`/`deleteRecord`
 * mutate `store` directly; every created row is owner-stamped to the
 * signed-in user, matching real Dataverse's host-context ownership
 * behavior). Dates are fully deterministic — every seeded row's
 * `sprk_lastvisited` is computed from a fixed `NOW` constant, never
 * `Date.now()`/`new Date()` read at assertion time — so this test's outcome
 * cannot depend on real wall-clock.
 */

import { startNavigatorCapture, type DerivedCurrentPage } from '@spaarke/ui-components/services/navigator/navigatorCaptureService';
import { NavItemType, type NavItemRecord } from '@spaarke/ui-components/services/navigator/navItemRepository';

const MS_PER_DAY = 24 * 60 * 60 * 1_000;
const HISTORY_RETENTION_DAYS = 30;

// A fixed instant — never real wall-clock. All seeded `sprk_lastvisited`
// values are derived from this so the test is fully deterministic.
const NOW = new Date('2026-08-12T12:00:00.000Z').getTime();

const CURRENT_USER_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const OTHER_USER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

// ---------------------------------------------------------------------------
// Fake Xrm.WebApi — in-memory sprk_navitem store (mirrors
// navigatorCaptureService.test.ts's harness so the OData filter the real
// repository builds is honored identically).
// ---------------------------------------------------------------------------

interface FakePageContextInput {
  entityName?: string;
  entityId?: string;
  pageType?: 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom';
}

type FakeStoredNavItem = NavItemRecord & { _ownerid_value: string };

function buildFakeXrm(pageContextRef: { current: FakePageContextInput | undefined }) {
  const store: FakeStoredNavItem[] = [];
  let idCounter = 0;

  const retrieveMultipleRecords = jest.fn(async (_entity: string, query?: string) => {
    const ownerMatch = query?.match(/_ownerid_value eq ([^\s]+)/);
    const typeMatch = query?.match(/sprk_type eq (\d+)/);
    const targetLogicalNameMatch = query?.match(/sprk_targetlogicalname eq '([^']*)'/);
    const targetIdMatch = query?.match(/sprk_targetid eq '([^']*)'/);
    const lastVisitedLtMatch = query?.match(/sprk_lastvisited lt ([^\s&]+)/);

    const ownerId = ownerMatch?.[1];
    const type = typeMatch ? Number(typeMatch[1]) : undefined;
    const targetLogicalName = targetLogicalNameMatch?.[1];
    const targetId = targetIdMatch?.[1];
    const lastVisitedLtIso = lastVisitedLtMatch?.[1];

    const entities = store.filter(r => {
      if (ownerId !== undefined && r._ownerid_value !== ownerId) return false;
      if (type !== undefined && r.sprk_type !== type) return false;
      if (targetLogicalName !== undefined && r.sprk_targetlogicalname !== targetLogicalName) return false;
      if (targetId !== undefined && r.sprk_targetid !== targetId) return false;
      if (lastVisitedLtIso !== undefined && !(r.sprk_lastvisited < lastVisitedLtIso)) return false;
      return true;
    });
    return { entities };
  });

  const createRecord = jest.fn(async (entity: string, data: Record<string, unknown>) => {
    const id = `navitem-${++idCounter}`;
    // Real Dataverse stamps the owner from the calling (impersonated) user's
    // context on host-context Xrm.WebApi creates.
    store.push({ sprk_navitemid: id, _ownerid_value: CURRENT_USER_ID, ...data } as unknown as FakeStoredNavItem);
    return { id, entityType: entity };
  });

  const updateRecord = jest.fn(async (entity: string, id: string, data: Record<string, unknown>) => {
    const rec = store.find(r => r.sprk_navitemid === id);
    if (rec) Object.assign(rec, data);
    return { id, entityType: entity };
  });

  const retrieveRecord = jest.fn(async (_entity: string, id: string) => ({ name: `Record ${id}` }));

  const deleteRecord = jest.fn(async (_entity: string, id: string) => {
    const idx = store.findIndex(r => r.sprk_navitemid === id);
    if (idx >= 0) store.splice(idx, 1);
  });

  const getEntityMetadata = jest.fn(async (_entityLogicalName: string) => ({ PrimaryNameAttribute: 'name' }));

  const getPageContext = jest.fn(() => ({ input: pageContextRef.current }));

  const getGlobalContext = jest.fn(() => ({
    userSettings: { userId: CURRENT_USER_ID, userName: 'Test User', languageId: 1033 },
    getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
    getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
    getVersion: () => '9.2',
  }));

  return {
    WebApi: { retrieveMultipleRecords, retrieveRecord, createRecord, updateRecord, deleteRecord },
    Utility: { getGlobalContext, getPageContext, getEntityMetadata },
    store,
  };
}

function installFakeXrm() {
  const pageContextRef: { current: FakePageContextInput | undefined } = { current: undefined };
  const xrm = buildFakeXrm(pageContextRef);
  (window as unknown as { Xrm: unknown }).Xrm = xrm;
  return { xrm, pageContextRef };
}

function setPage(pageContextRef: { current: FakePageContextInput | undefined }, input: FakePageContextInput | undefined): void {
  pageContextRef.current = input;
}

/** Build a fake stored `sprk_navitem` row for direct seeding into `xrm.store`. */
function buildSeedRow(overrides: Partial<FakeStoredNavItem> & Pick<FakeStoredNavItem, 'sprk_navitemid'>): FakeStoredNavItem {
  return {
    sprk_type: NavItemType.History,
    sprk_source: 100000000,
    sprk_targetlogicalname: 'sprk_project',
    sprk_targetid: 'seed-target-id',
    sprk_pagetype: 100000000,
    sprk_url: null,
    sprk_displayname: 'Seed row',
    sprk_lastvisited: new Date(NOW).toISOString(),
    sprk_visitcount: 1,
    _ownerid_value: CURRENT_USER_ID,
    ...overrides,
  };
}

// A fresh page to navigate to in order to trigger the capture-write tick that
// invokes the inline prune path (the prune only runs on a tick that just
// wrote a history row — see navigatorCaptureService.ts module docblock).
const FRESH_PAGE = {
  entityName: 'sprk_matter',
  entityId: '99999999-9999-9999-9999-999999999999',
  pageType: 'entityrecord' as const,
};

async function advance(ms = 1_500): Promise<void> {
  await jest.advanceTimersByTimeAsync(ms);
}

describe('retention.verify — end-to-end prune-on-write verification (task 081, spec FR-05, Success Criterion 7)', () => {
  let stop: (() => void) | undefined;

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(NOW);
  });

  afterEach(() => {
    stop?.();
    stop = undefined;
    jest.useRealTimers();
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  });

  it('prunes the signed-in user\'s >30-day history row, keeps the pin, and leaves another user\'s old row untouched', async () => {
    const { xrm, pageContextRef } = installFakeXrm();

    // ── Step 1: seed ────────────────────────────────────────────────────
    // (a) A history row for the signed-in user, older than the 30-day
    //     retention window (31 days old — strictly past the cutoff).
    const oldHistoryIso = new Date(NOW - (HISTORY_RETENTION_DAYS + 1) * MS_PER_DAY).toISOString();
    xrm.store.push(
      buildSeedRow({
        sprk_navitemid: 'seed-old-history',
        sprk_type: NavItemType.History,
        sprk_targetlogicalname: 'sprk_project',
        sprk_targetid: 'old-project-target',
        sprk_displayname: 'Old Project (should be pruned)',
        sprk_lastvisited: oldHistoryIso,
        _ownerid_value: CURRENT_USER_ID,
      })
    );

    // (b) A pin (sprk_type=Pin) row for the signed-in user, also far older
    //     than 30 days — pins/bookmarks never auto-expire.
    const oldPinIso = new Date(NOW - 365 * MS_PER_DAY).toISOString();
    xrm.store.push(
      buildSeedRow({
        sprk_navitemid: 'seed-pin',
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: 'sprk_document',
        sprk_targetid: 'pinned-document-target',
        sprk_displayname: 'Pinned Document (must survive)',
        sprk_lastvisited: oldPinIso,
        _ownerid_value: CURRENT_USER_ID,
      })
    );

    // (c) For isolation (NFR-03): an old history row owned by a DIFFERENT
    //     user, also past the 30-day cutoff.
    xrm.store.push(
      buildSeedRow({
        sprk_navitemid: 'seed-other-user-old-history',
        sprk_type: NavItemType.History,
        sprk_targetlogicalname: 'sprk_project',
        sprk_targetid: 'other-user-project-target',
        sprk_displayname: "Other User's Old Project (must be untouched)",
        sprk_lastvisited: oldHistoryIso,
        _ownerid_value: OTHER_USER_ID,
      })
    );

    expect(xrm.store).toHaveLength(3); // sanity: all three seed rows present before capture runs

    // ── Step 2: trigger a capture write ─────────────────────────────────
    // Drive startNavigatorCapture through a page change so its poll tick
    // upserts a fresh history row for the signed-in user — the branch that
    // invokes the inline prune path (navigatorCaptureService.ts only prunes
    // after a SUCCESSFUL history write this tick, never on a no-op tick).
    const onCurrentPageChange = jest.fn<void, [DerivedCurrentPage | null]>();
    const onError = jest.fn();
    setPage(pageContextRef, FRESH_PAGE);
    stop = startNavigatorCapture({ onCurrentPageChange, onError });
    await advance(0); // flush the immediate first tick

    // The capture write itself must have succeeded (no upsert/prune error).
    expect(onError).not.toHaveBeenCalled();
    expect(onCurrentPageChange).toHaveBeenCalledWith(
      expect.objectContaining({ entityLogicalName: FRESH_PAGE.entityName, entityId: FRESH_PAGE.entityId })
    );
    expect(xrm.WebApi.createRecord).toHaveBeenCalledWith(
      'sprk_navitem',
      expect.objectContaining({ sprk_targetlogicalname: FRESH_PAGE.entityName, sprk_type: NavItemType.History })
    );

    // ── Step 3: assert ───────────────────────────────────────────────────

    // (1) The seeded >30-day history row for the signed-in user is GONE.
    expect(xrm.WebApi.deleteRecord).toHaveBeenCalledWith('sprk_navitem', 'seed-old-history');
    expect(xrm.store.find(r => r.sprk_navitemid === 'seed-old-history')).toBeUndefined();

    // (2) The pin row SURVIVES (pins never auto-expire, regardless of age).
    expect(xrm.WebApi.deleteRecord).not.toHaveBeenCalledWith('sprk_navitem', 'seed-pin');
    const survivingPin = xrm.store.find(r => r.sprk_navitemid === 'seed-pin');
    expect(survivingPin).toBeDefined();
    expect(survivingPin?.sprk_type).toBe(NavItemType.Pin);
    expect(survivingPin?.sprk_lastvisited).toBe(oldPinIso); // untouched — no bump

    // (3) The other user's old history row is UNTOUCHED (owner-scoped prune, NFR-03).
    expect(xrm.WebApi.deleteRecord).not.toHaveBeenCalledWith('sprk_navitem', 'seed-other-user-old-history');
    const otherUserRow = xrm.store.find(r => r.sprk_navitemid === 'seed-other-user-old-history');
    expect(otherUserRow).toBeDefined();
    expect(otherUserRow?._ownerid_value).toBe(OTHER_USER_ID);
    expect(otherUserRow?.sprk_lastvisited).toBe(oldHistoryIso); // untouched — no bump, no delete

    // Final store sanity: the two seed survivors + the freshly-captured row.
    expect(xrm.store).toHaveLength(2 + 1);
    expect(xrm.store.map(r => r.sprk_navitemid).sort()).toEqual(
      ['seed-other-user-old-history', 'seed-pin', 'navitem-1'].sort()
    );
  });
});
