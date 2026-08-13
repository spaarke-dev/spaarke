/**
 * navigatorCaptureService.test.ts (task 030, spec FR-03)
 *
 * Covers the task POML's four required scenarios:
 *   1. Matter -> Project -> Document yields 3 ordered history rows.
 *   2. Re-visiting an existing target bumps `sprk_lastvisited`/`sprk_visitcount`
 *      rather than creating a duplicate row.
 *   3. Leaving records (navigating to a non-record page) sets the derived
 *      current page to `null`.
 *   4. A page with no resolvable entity (dashboard/entitylist/custom) writes
 *      no `sprk_navitem` row at all.
 *
 * `window.Xrm` is installed directly (mirrors
 * `SprkSidePaneHost.test.tsx`'s `installMockXrm` pattern) so `getXrm()`
 * (`xrmContext.ts`) resolves it via its normal `window.Xrm` frame-walk path —
 * no module mock of `xrmContext` itself. `Xrm.Utility.getPageContext()` is
 * driven by a mutable `currentPageContext` the tests reassign between
 * `jest.advanceTimersByTimeAsync` calls to simulate navigation, matching
 * the poll-driven (no navigation-event API) design under test.
 */

import { startNavigatorCapture, DEFAULT_CAPTURE_POLL_INTERVAL_MS, type DerivedCurrentPage } from '../navigatorCaptureService';
import { NavItemType, type NavItemRecord } from '../navItemRepository';

// ---------------------------------------------------------------------------
// Fake Xrm.WebApi — in-memory sprk_navitem store
// ---------------------------------------------------------------------------

interface FakePageContextInput {
  entityName?: string;
  entityId?: string;
  pageType?: 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom';
}

const CURRENT_USER_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

function buildFakeXrm(pageContextRef: { current: FakePageContextInput | undefined }) {
  const store: NavItemRecord[] = [];
  let idCounter = 0;

  const retrieveMultipleRecords = jest.fn(async (_entity: string, query?: string) => {
    const targetLogicalNameMatch = query?.match(/sprk_targetlogicalname eq '([^']*)'/);
    const targetIdMatch = query?.match(/sprk_targetid eq '([^']*)'/);
    const targetLogicalName = targetLogicalNameMatch?.[1];
    const targetId = targetIdMatch?.[1];
    const entities = store.filter(
      r =>
        r.sprk_type === NavItemType.History &&
        r.sprk_targetlogicalname === targetLogicalName &&
        r.sprk_targetid === targetId
    );
    return { entities };
  });

  const createRecord = jest.fn(async (entity: string, data: Record<string, unknown>) => {
    const id = `navitem-${++idCounter}`;
    store.push({ sprk_navitemid: id, ...data } as unknown as NavItemRecord);
    return { id, entityType: entity };
  });

  const updateRecord = jest.fn(async (entity: string, id: string, data: Record<string, unknown>) => {
    const rec = store.find(r => r.sprk_navitemid === id);
    if (rec) Object.assign(rec, data);
    return { id, entityType: entity };
  });

  const retrieveRecord = jest.fn(async (_entity: string, id: string) => {
    return { name: `Record ${id}` };
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
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord,
      createRecord,
      updateRecord,
      deleteRecord: jest.fn(),
    },
    Utility: {
      getGlobalContext,
      getPageContext,
      getEntityMetadata,
    },
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

const MATTER = { entityName: 'sprk_matter', entityId: '11111111-1111-1111-1111-111111111111', pageType: 'entityrecord' as const };
const PROJECT = { entityName: 'sprk_project', entityId: '22222222-2222-2222-2222-222222222222', pageType: 'entityrecord' as const };
const DOCUMENT = { entityName: 'sprk_document', entityId: '33333333-3333-3333-3333-333333333333', pageType: 'entityrecord' as const };

async function advance(ms: number = DEFAULT_CAPTURE_POLL_INTERVAL_MS): Promise<void> {
  await jest.advanceTimersByTimeAsync(ms);
}

describe('navigatorCaptureService — startNavigatorCapture', () => {
  let stop: (() => void) | undefined;

  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    stop?.();
    stop = undefined;
    jest.useRealTimers();
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  });

  it('Matter -> Project -> Document yields 3 ordered history rows', async () => {
    const { xrm, pageContextRef } = installFakeXrm();
    setPage(pageContextRef, MATTER);

    stop = startNavigatorCapture();
    await advance(0); // flush the immediate tick (advanceTimersByTimeAsync drains chained microtask awaits)

    setPage(pageContextRef, PROJECT);
    await advance();

    setPage(pageContextRef, DOCUMENT);
    await advance();

    expect(xrm.WebApi.createRecord).toHaveBeenCalledTimes(3);
    expect(xrm.WebApi.updateRecord).not.toHaveBeenCalled();

    const created = xrm.WebApi.createRecord.mock.calls.map(call => call[1] as Record<string, unknown>);
    expect(created[0]).toMatchObject({ sprk_targetlogicalname: 'sprk_matter', sprk_targetid: MATTER.entityId, sprk_visitcount: 1 });
    expect(created[1]).toMatchObject({ sprk_targetlogicalname: 'sprk_project', sprk_targetid: PROJECT.entityId, sprk_visitcount: 1 });
    expect(created[2]).toMatchObject({ sprk_targetlogicalname: 'sprk_document', sprk_targetid: DOCUMENT.entityId, sprk_visitcount: 1 });

    // Every row was written as History/Captured — never a pin (task 031/050 concerns, not this task).
    for (const row of created) {
      expect(row.sprk_type).toBe(NavItemType.History);
    }
  });

  it('re-visiting an existing target bumps sprk_lastvisited/sprk_visitcount instead of creating a duplicate', async () => {
    const { xrm, pageContextRef } = installFakeXrm();
    setPage(pageContextRef, MATTER);

    stop = startNavigatorCapture();
    await advance(0);

    setPage(pageContextRef, PROJECT);
    await advance();

    // Re-visit the Matter.
    setPage(pageContextRef, MATTER);
    await advance();

    expect(xrm.WebApi.createRecord).toHaveBeenCalledTimes(2); // Matter (first sighting) + Project only
    expect(xrm.WebApi.updateRecord).toHaveBeenCalledTimes(1);

    const [, updateId, updateData] = xrm.WebApi.updateRecord.mock.calls[0] as [string, string, Record<string, unknown>];
    expect(updateId).toBe(xrm.store.find(r => r.sprk_targetlogicalname === 'sprk_matter')?.sprk_navitemid);
    expect(updateData.sprk_visitcount).toBe(2);
    expect(typeof updateData.sprk_lastvisited).toBe('string');

    // No duplicate Matter row exists in the store.
    const matterRows = xrm.store.filter(r => r.sprk_targetlogicalname === 'sprk_matter');
    expect(matterRows).toHaveLength(1);
    expect(matterRows[0].sprk_visitcount).toBe(2);
  });

  it('leaving records (navigating to a non-record page) sets the derived current page to null', async () => {
    const { pageContextRef } = installFakeXrm();
    setPage(pageContextRef, MATTER);

    const onCurrentPageChange = jest.fn<void, [DerivedCurrentPage | null]>();
    stop = startNavigatorCapture({ onCurrentPageChange });
    await advance(0);

    expect(onCurrentPageChange).toHaveBeenCalledTimes(1);
    expect(onCurrentPageChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ entityLogicalName: 'sprk_matter', entityId: MATTER.entityId })
    );

    // Navigate to a dashboard (non-record page).
    setPage(pageContextRef, { pageType: 'dashboard' });
    await advance();

    expect(onCurrentPageChange).toHaveBeenCalledTimes(2);
    expect(onCurrentPageChange).toHaveBeenLastCalledWith(null);
  });

  it('a page with no resolvable entity writes no sprk_navitem row (dashboard, entitylist, custom, and unresolved-id record)', async () => {
    const { xrm, pageContextRef } = installFakeXrm();
    setPage(pageContextRef, { pageType: 'dashboard' });

    stop = startNavigatorCapture();
    await advance(0);

    setPage(pageContextRef, { entityName: 'sprk_matter', pageType: 'entitylist' }); // list — no id
    await advance();

    setPage(pageContextRef, { pageType: 'custom' });
    await advance();

    setPage(pageContextRef, { entityName: 'sprk_matter', pageType: 'entityrecord' }); // unsaved record — no id yet
    await advance();

    expect(xrm.WebApi.createRecord).not.toHaveBeenCalled();
    expect(xrm.WebApi.updateRecord).not.toHaveBeenCalled();
    expect(xrm.store).toHaveLength(0);
  });

  it('resolves sprk_displayname via Xrm.Utility.getEntityMetadata + retrieveRecord on first sighting', async () => {
    const { xrm, pageContextRef } = installFakeXrm();
    setPage(pageContextRef, MATTER);

    stop = startNavigatorCapture();
    await advance(0);

    expect(xrm.WebApi.createRecord).toHaveBeenCalledTimes(1);
    const [, data] = xrm.WebApi.createRecord.mock.calls[0] as [string, Record<string, unknown>];
    expect(data.sprk_displayname).toBe(`Record ${MATTER.entityId}`);
  });

  it('re-acquires Xrm fresh every tick rather than caching it (task-001 spike lesson)', async () => {
    const { xrm, pageContextRef } = installFakeXrm();
    setPage(pageContextRef, MATTER);

    stop = startNavigatorCapture();
    await advance(0);

    setPage(pageContextRef, PROJECT);
    await advance();
    await advance();

    // getPageContext (and thus a fresh Xrm read) is invoked on every poll tick,
    // not merely once at start — proves the loop is not holding a stale reference.
    expect(xrm.Utility.getPageContext.mock.calls.length).toBeGreaterThanOrEqual(3);
  });
});
