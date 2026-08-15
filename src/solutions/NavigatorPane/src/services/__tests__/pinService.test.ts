/**
 * pinService Tests (task 050, spec FR-07)
 *
 * Verifies the task-050 closed acceptance-criteria set + `<ui-tests>`:
 *   - `pin()` creates a per-user `sprk_type=Pin` `sprk_navitem` when none
 *     exists for the target.
 *   - `pin()` dedupes: an already-pinned target does NOT create a duplicate
 *     row (double-star safety, including a realistic sequential re-star
 *     against the same in-memory "database").
 *   - `unpin()` / `unpinById()` DELETE the pin row; unpinning an
 *     already-unpinned target is a safe no-op.
 *   - **HARD assertion**: across every pin/unpin call in this suite, the only
 *     `Xrm.WebApi` entity ever touched is `sprk_navitem` — `sprk_monitor` is
 *     NEVER read, created, updated, or deleted by this gesture (project HARD
 *     MUST-NOT).
 *   - Owner-scoping isolates users: another user's `sprk_navitem`/pin state
 *     (standing in for "another user's `sprk_monitor` toggle" — this gesture
 *     structurally cannot see or be affected by `sprk_monitor` at all, since
 *     it never queries that entity) does not leak into this user's pin set.
 *
 * `window.Xrm` is installed directly (mirrors `RecentTab.test.tsx` /
 * `navItemRepository`'s own convention) so `pinService` drives the REAL
 * `navItemRepository` code paths (`findPinItem`/`createPinItem`/
 * `deleteNavItem`) against a small in-memory fake `sprk_navitem` table — not
 * a module mock of `navItemRepository` itself, so this suite also proves
 * `pinService` genuinely reuses (does not duplicate) that CRUD path.
 *
 * @see ../pinService.ts
 * @see navItemRepository.ts (task 030/041/050)
 * @see ADR-021 Fluent UI v9 design system (N/A here — no rendering in this suite)
 */

import { findPin, pin, unpin, unpinById, type PinTarget } from '../pinService';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const OWNER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const OTHER_OWNER_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

const NavItemType = { History: 100000000, Pin: 100000001 } as const;
const NavItemPageType = { EntityRecord: 100000000 } as const;

const MATTER_TARGET: PinTarget = {
  targetLogicalName: 'sprk_matter',
  targetId: '11111111-1111-1111-1111-111111111111',
  pageType: NavItemPageType.EntityRecord,
  displayName: 'Acme v. Widget Co',
};

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm — a tiny in-memory `sprk_navitem` table so dedupe-before-create
// (a real create followed by a real re-query) is exercised faithfully rather
// than stubbed away.
// ─────────────────────────────────────────────────────────────────────────────

interface FakeNavItemRow {
  sprk_navitemid: string;
  _ownerid_value: string;
  sprk_type: number;
  sprk_targetlogicalname: string | null;
  sprk_targetid: string | null;
  [key: string]: unknown;
}

/** Entity name every mocked WebApi call is asserted against (HARD assertion). */
const NAVITEM_ENTITY = 'sprk_navitem';
const FORBIDDEN_ENTITY = 'sprk_monitor';

function buildFakeXrm(seed: FakeNavItemRow[] = []) {
  const table: FakeNavItemRow[] = [...seed];
  let nextId = 1;
  /** The "signed-in user" whose id `createRecord` implicitly stamps as owner — mirrors real Dataverse UserOwned-entity behavior (owner is never in the create payload). */
  let currentUser = OWNER_ID;

  function setCurrentUser(userId: string): void {
    currentUser = userId;
  }

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
    if (entity !== NAVITEM_ENTITY || !query) return { entities: [] };
    const ownerMatch = /_ownerid_value eq (\S+?)(?: and|$)/.exec(query);
    const typeMatch = /sprk_type eq (\d+)/.exec(query);
    const logicalNameMatch = /sprk_targetlogicalname eq '([^']*)'/.exec(query);
    const targetIdMatch = /sprk_targetid eq '([^']*)'/.exec(query);

    const results = table.filter(row => {
      if (ownerMatch && row._ownerid_value !== ownerMatch[1]) return false;
      if (typeMatch && row.sprk_type !== Number(typeMatch[1])) return false;
      if (logicalNameMatch && row.sprk_targetlogicalname !== logicalNameMatch[1]) return false;
      if (targetIdMatch && row.sprk_targetid !== targetIdMatch[1]) return false;
      return true;
    });
    return { entities: results };
  });

  const createRecord = jest.fn(async (_entity: string, data: Record<string, unknown>) => {
    const id = `pin-${nextId++}`;
    table.push({
      sprk_navitemid: id,
      _ownerid_value: currentUser,
      sprk_type: data.sprk_type as number,
      sprk_targetlogicalname: (data.sprk_targetlogicalname as string) ?? null,
      sprk_targetid: (data.sprk_targetid as string) ?? null,
      ...data,
    });
    return { id };
  });

  const deleteRecord = jest.fn(async (_entity: string, id: string) => {
    const idx = table.findIndex(r => r.sprk_navitemid === id);
    if (idx >= 0) table.splice(idx, 1);
    return {};
  });

  const updateRecord = jest.fn(async () => ({}));
  const retrieveRecord = jest.fn(async () => ({}));

  return {
    WebApi: { retrieveMultipleRecords, retrieveRecord, createRecord, updateRecord, deleteRecord },
    Utility: {
      getGlobalContext: jest.fn(() => ({
        userSettings: { userId: OWNER_ID, userName: 'Test User', languageId: 1033 },
        getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getVersion: () => '9.2',
      })),
    },
    __table: table,
    __setCurrentUser: setCurrentUser,
  };
}

function installMockXrm(seed: FakeNavItemRow[] = []) {
  const fakeXrm = buildFakeXrm(seed);
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

/** Collects every entity name any WebApi mutation/read call touched, across the whole fake. */
function allTouchedEntities(fakeXrm: ReturnType<typeof buildFakeXrm>): string[] {
  const calls = [
    ...fakeXrm.WebApi.retrieveMultipleRecords.mock.calls,
    ...fakeXrm.WebApi.createRecord.mock.calls,
    ...fakeXrm.WebApi.updateRecord.mock.calls,
    ...fakeXrm.WebApi.deleteRecord.mock.calls,
    ...fakeXrm.WebApi.retrieveRecord.mock.calls,
  ];
  return calls.map(call => call[0] as string);
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('pinService', () => {
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm) {
      (window as unknown as { Xrm: unknown }).Xrm = originalXrm;
    } else {
      removeMockXrm();
    }
    jest.clearAllMocks();
  });

  // ───────────────────────────────────────────────────────────────────────
  // pin() — create when none exists
  // ───────────────────────────────────────────────────────────────────────

  it('pin_NoExistingPin_CreatesNewPinRow', async () => {
    const fakeXrm = installMockXrm();

    const result = await pin(OWNER_ID, MATTER_TARGET);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledTimes(1);
    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: MATTER_TARGET.targetLogicalName,
        sprk_targetid: MATTER_TARGET.targetId,
        sprk_displayname: MATTER_TARGET.displayName,
      })
    );
    expect(result.created).toBe(true);
    expect(result.navItemId).toBe('pin-1');
  });

  // ───────────────────────────────────────────────────────────────────────
  // pin() — dedupe-before-create (idempotency)
  // ───────────────────────────────────────────────────────────────────────

  it('pin_AlreadyPinnedTarget_DoesNotCreateDuplicateRow', async () => {
    const seed: FakeNavItemRow[] = [
      {
        sprk_navitemid: 'existing-pin',
        _ownerid_value: OWNER_ID,
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: MATTER_TARGET.targetLogicalName,
        sprk_targetid: MATTER_TARGET.targetId,
      },
    ];
    const fakeXrm = installMockXrm(seed);

    const result = await pin(OWNER_ID, MATTER_TARGET);

    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
    expect(result.created).toBe(false);
    expect(result.navItemId).toBe('existing-pin');
  });

  it('pin_DoubleStarSequential_SecondCallCreatesNoDuplicate', async () => {
    const fakeXrm = installMockXrm();

    const first = await pin(OWNER_ID, MATTER_TARGET);
    const second = await pin(OWNER_ID, MATTER_TARGET);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledTimes(1);
    expect(first.created).toBe(true);
    expect(second.created).toBe(false);
    expect(second.navItemId).toBe(first.navItemId);
    expect(fakeXrm.__table.filter(r => r.sprk_type === NavItemType.Pin)).toHaveLength(1);
  });

  // ───────────────────────────────────────────────────────────────────────
  // unpin() / unpinById()
  // ───────────────────────────────────────────────────────────────────────

  it('unpin_ExistingPin_DeletesTheRow', async () => {
    const seed: FakeNavItemRow[] = [
      {
        sprk_navitemid: 'existing-pin',
        _ownerid_value: OWNER_ID,
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: MATTER_TARGET.targetLogicalName,
        sprk_targetid: MATTER_TARGET.targetId,
      },
    ];
    const fakeXrm = installMockXrm(seed);

    const result = await unpin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);

    expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalledWith(NAVITEM_ENTITY, 'existing-pin');
    expect(result).toBe(true);
    expect(fakeXrm.__table).toHaveLength(0);
  });

  it('unpin_NoExistingPin_IsSafeNoOp', async () => {
    const fakeXrm = installMockXrm();

    const result = await unpin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);

    expect(fakeXrm.WebApi.deleteRecord).not.toHaveBeenCalled();
    expect(result).toBe(false);
  });

  it('unpinById_DeletesGivenIdDirectlyWithoutALookupQuery', async () => {
    const fakeXrm = installMockXrm();

    await unpinById('some-pin-id');

    expect(fakeXrm.WebApi.deleteRecord).toHaveBeenCalledWith(NAVITEM_ENTITY, 'some-pin-id');
    expect(fakeXrm.WebApi.retrieveMultipleRecords).not.toHaveBeenCalled();
  });

  it('pinThenUnpin_RoundTrip_LeavesNoRowBehind', async () => {
    const fakeXrm = installMockXrm();

    const pinned = await pin(OWNER_ID, MATTER_TARGET);
    expect(fakeXrm.__table).toHaveLength(1);

    const unpinned = await unpin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);
    expect(unpinned).toBe(true);
    expect(pinned.created).toBe(true);
    expect(fakeXrm.__table).toHaveLength(0);
  });

  // ───────────────────────────────────────────────────────────────────────
  // HARD MUST-NOT: sprk_monitor is never written by this gesture
  // ───────────────────────────────────────────────────────────────────────

  it('gesture_NeverTouchesSprkMonitor_OnlySprkNavitem', async () => {
    const fakeXrm = installMockXrm();

    await pin(OWNER_ID, MATTER_TARGET);
    await pin(OWNER_ID, MATTER_TARGET); // dedupe path
    await unpin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);
    await pin(OWNER_ID, MATTER_TARGET);
    await unpinById((await findPin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId))!.sprk_navitemid);

    const touchedEntities = allTouchedEntities(fakeXrm);
    expect(touchedEntities.length).toBeGreaterThan(0);
    expect(touchedEntities).not.toContain(FORBIDDEN_ENTITY);
    expect(touchedEntities.every(entity => entity === NAVITEM_ENTITY)).toBe(true);
  });

  // ───────────────────────────────────────────────────────────────────────
  // Owner-scoping isolates users — the structural reason another user's
  // sprk_monitor toggle (or any other user's action) cannot add/remove THIS
  // user's pin: every read/write is filtered/scoped to the calling owner.
  // ───────────────────────────────────────────────────────────────────────

  it('anotherUsersPinState_DoesNotLeakIntoOrAffectThisUsersPin', async () => {
    const seed: FakeNavItemRow[] = [
      {
        sprk_navitemid: 'other-users-pin',
        _ownerid_value: OTHER_OWNER_ID,
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: MATTER_TARGET.targetLogicalName,
        sprk_targetid: MATTER_TARGET.targetId,
      },
    ];
    const fakeXrm = installMockXrm(seed);

    // This user has no pin for the same target — the other user's pin row
    // (standing in for any other user's per-user state, e.g. a sprk_monitor
    // toggle this gesture would never even query) must not be visible.
    const mine = await findPin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);
    expect(mine).toBeNull();

    // Starring creates THIS user's own row rather than reusing the other
    // user's row (which would incorrectly "unpin" it for them on my unstar).
    const result = await pin(OWNER_ID, MATTER_TARGET);
    expect(result.created).toBe(true);
    expect(result.navItemId).not.toBe('other-users-pin');
    expect(fakeXrm.__table).toHaveLength(2);

    // Unstarring mine leaves the other user's row untouched.
    await unpin(OWNER_ID, MATTER_TARGET.targetLogicalName, MATTER_TARGET.targetId);
    expect(fakeXrm.__table.map(r => r.sprk_navitemid)).toEqual(['other-users-pin']);
  });
});
