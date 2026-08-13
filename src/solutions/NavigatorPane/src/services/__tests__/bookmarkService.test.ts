/**
 * bookmarkService Tests (task 051, spec FR-08 / OQ-7)
 *
 * Verifies the task-051 closed acceptance-criteria set + `<ui-tests>`:
 *   - `pinCurrentPage` bookmarks the current page with NO typing — reads
 *     `getPageContext()` and writes `sprk_source=Captured`.
 *   - `pinCurrentPage` on a non-resolvable page (dashboard/custom, or an
 *     entitylist page with no `viewId`) throws {@link BookmarkError} and
 *     creates NO row.
 *   - `addBookmark` with an MDA record URL writes a labeled record target,
 *     `sprk_source=Manual`.
 *   - `addBookmark` with an MDA entitylist/view URL writes a `viewid` target.
 *   - `addBookmark` with an external URL writes a raw `sprk_url` weblink row.
 *   - `addBookmark` with an unparseable non-URL string throws
 *     {@link BookmarkError}, no row created.
 *   - **HARD assertion**: every write across this suite only ever touches
 *     `sprk_navitem` — `sprk_monitor` is never read/created/updated/deleted.
 *
 * `window.Xrm` is installed directly (mirrors `pinService.test.ts`'s
 * convention) so `bookmarkService` drives the REAL `pinService`/
 * `navItemRepository` code paths against a small in-memory fake
 * `sprk_navitem` table.
 *
 * @see ../bookmarkService.ts
 * @see ../urlParse.ts
 * @see ../pinService.ts (task 050) — the pin-write path this module reuses
 */

import { addBookmark, pinCurrentPage, BookmarkError } from '../bookmarkService';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const OWNER_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const NavItemType = { History: 100000000, Pin: 100000001 } as const;
const NavItemSource = { Captured: 100000000, Manual: 100000001 } as const;
const NavItemPageType = {
  EntityRecord: 100000000,
  EntityList: 100000001,
  Custom: 100000002,
  WebLink: 100000003,
} as const;

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const VIEW_ID = '22222222-2222-2222-2222-222222222222';

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm — a tiny in-memory `sprk_navitem` table (mirrors pinService.test.ts).
// ─────────────────────────────────────────────────────────────────────────────

interface FakeNavItemRow {
  sprk_navitemid: string;
  _ownerid_value: string;
  sprk_type: number;
  sprk_targetlogicalname: string | null;
  sprk_targetid: string | null;
  [key: string]: unknown;
}

const NAVITEM_ENTITY = 'sprk_navitem';
const FORBIDDEN_ENTITY = 'sprk_monitor';

interface FakePageContextInput {
  entityName?: string;
  entityId?: string;
  entityRecordName?: string;
  pageType?: 'entityrecord' | 'entitylist' | 'dashboard' | 'webresource' | 'custom';
  viewId?: string;
}

interface BuildFakeXrmOptions {
  pageContext?: FakePageContextInput;
  seed?: FakeNavItemRow[];
  /** Primary-name field per entity, for `resolveRecordDisplayName` coverage. */
  primaryNameField?: Record<string, string>;
  /** `{entity}:{id}` -> record data, for `retrieveRecord` coverage. */
  recordsById?: Record<string, Record<string, unknown>>;
}

function buildFakeXrm(options: BuildFakeXrmOptions = {}) {
  const table: FakeNavItemRow[] = [...(options.seed ?? [])];
  let nextId = 1;

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
      _ownerid_value: OWNER_ID,
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
  const retrieveRecord = jest.fn(async (entity: string, id: string) => {
    return options.recordsById?.[`${entity}:${id}`] ?? {};
  });
  const getEntityMetadata = jest.fn(async (entity: string) => ({
    PrimaryNameAttribute: options.primaryNameField?.[entity],
  }));

  return {
    WebApi: { retrieveMultipleRecords, retrieveRecord, createRecord, updateRecord, deleteRecord },
    Utility: {
      getGlobalContext: jest.fn(() => ({
        userSettings: { userId: OWNER_ID, userName: 'Test User', languageId: 1033 },
        getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
        getVersion: () => '9.2',
      })),
      getPageContext: jest.fn(() => (options.pageContext ? { input: options.pageContext } : {})),
      getEntityMetadata,
    },
    __table: table,
  };
}

function installMockXrm(options?: BuildFakeXrmOptions) {
  const fakeXrm = buildFakeXrm(options);
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

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

describe('bookmarkService', () => {
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
  // pinCurrentPage — "Pin this page" (captured, no typing)
  // ───────────────────────────────────────────────────────────────────────

  it('pinCurrentPage_OnEntityRecordPage_CreatesCapturedPinWithNoTyping', async () => {
    const fakeXrm = installMockXrm({
      pageContext: { pageType: 'entityrecord', entityName: 'sprk_matter', entityId: MATTER_ID },
    });

    const result = await pinCurrentPage(OWNER_ID);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledTimes(1);
    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_type: NavItemType.Pin,
        sprk_source: NavItemSource.Captured,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: MATTER_ID,
        sprk_pagetype: NavItemPageType.EntityRecord,
      })
    );
    expect(result.created).toBe(true);
  });

  it('pinCurrentPage_UsesEntityRecordNameAsDisplayNameWhenAvailable', async () => {
    const fakeXrm = installMockXrm({
      pageContext: {
        pageType: 'entityrecord',
        entityName: 'sprk_matter',
        entityId: MATTER_ID,
        entityRecordName: 'Acme v. Widget Co',
      },
    });

    await pinCurrentPage(OWNER_ID);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({ sprk_displayname: 'Acme v. Widget Co' })
    );
  });

  it('pinCurrentPage_OnEntityListPageWithViewId_CreatesCapturedViewPin', async () => {
    const fakeXrm = installMockXrm({
      pageContext: { pageType: 'entitylist', entityName: 'sprk_matter', viewId: VIEW_ID },
    });

    const result = await pinCurrentPage(OWNER_ID);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_source: NavItemSource.Captured,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: VIEW_ID,
        sprk_pagetype: NavItemPageType.EntityList,
      })
    );
    expect(result.created).toBe(true);
  });

  it('pinCurrentPage_AlreadyPinned_DedupesLikeTheStarGesture', async () => {
    const seed: FakeNavItemRow[] = [
      {
        sprk_navitemid: 'existing-pin',
        _ownerid_value: OWNER_ID,
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: MATTER_ID,
      },
    ];
    const fakeXrm = installMockXrm({
      pageContext: { pageType: 'entityrecord', entityName: 'sprk_matter', entityId: MATTER_ID },
      seed,
    });

    const result = await pinCurrentPage(OWNER_ID);

    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
    expect(result.created).toBe(false);
    expect(result.navItemId).toBe('existing-pin');
  });

  it('pinCurrentPage_NonResolvablePage_Dashboard_ThrowsAndCreatesNoRow', async () => {
    const fakeXrm = installMockXrm({ pageContext: { pageType: 'dashboard' } });

    await expect(pinCurrentPage(OWNER_ID)).rejects.toThrow(BookmarkError);
    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
  });

  it('pinCurrentPage_EntityListWithoutViewId_ThrowsAndCreatesNoRow', async () => {
    const fakeXrm = installMockXrm({ pageContext: { pageType: 'entitylist', entityName: 'sprk_matter' } });

    await expect(pinCurrentPage(OWNER_ID)).rejects.toThrow(BookmarkError);
    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
  });

  it('pinCurrentPage_EntityRecordWithoutId_ThrowsAndCreatesNoRow', async () => {
    const fakeXrm = installMockXrm({ pageContext: { pageType: 'entityrecord', entityName: 'sprk_matter' } });

    await expect(pinCurrentPage(OWNER_ID)).rejects.toThrow(BookmarkError);
    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
  });

  // ───────────────────────────────────────────────────────────────────────
  // addBookmark — "+ Add bookmark" (manual, URL-parsing)
  // ───────────────────────────────────────────────────────────────────────

  it('addBookmark_MdaRecordUrl_CreatesLabeledManualRecordPin', async () => {
    const fakeXrm = installMockXrm();
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=' + MATTER_ID;

    const result = await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_type: NavItemType.Pin,
        sprk_source: NavItemSource.Manual,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: MATTER_ID,
        sprk_pagetype: NavItemPageType.EntityRecord,
      })
    );
    expect(result.kind).toBe('record');
    expect(result.created).toBe(true);
  });

  it('addBookmark_MdaViewUrl_CreatesManualViewPinKeyedOnViewId', async () => {
    const fakeXrm = installMockXrm();
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entitylist&etn=sprk_matter&viewid=' + VIEW_ID;

    const result = await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_source: NavItemSource.Manual,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: VIEW_ID,
        sprk_pagetype: NavItemPageType.EntityList,
      })
    );
    expect(result.kind).toBe('view');
  });

  it('addBookmark_ExternalUrl_CreatesRawWeblinkPinThatOpensNewTab', async () => {
    const fakeXrm = installMockXrm();
    const url = 'https://example.com/kb/some-article';

    const result = await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({
        sprk_source: NavItemSource.Manual,
        sprk_targetlogicalname: null,
        sprk_targetid: null,
        sprk_pagetype: NavItemPageType.WebLink,
        sprk_url: url,
      })
    );
    expect(result.kind).toBe('weblink');
    expect(result.created).toBe(true);
  });

  it('addBookmark_NonUrlString_ThrowsFriendlyRejectionAndCreatesNoRow', async () => {
    const fakeXrm = installMockXrm();

    await expect(addBookmark(OWNER_ID, 'notes for later')).rejects.toThrow(BookmarkError);
    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
  });

  it('addBookmark_MdaRecordUrl_ResolvesRealPrimaryNameAsDisplayName', async () => {
    const fakeXrm = installMockXrm({
      primaryNameField: { sprk_matter: 'sprk_matternumber' },
      recordsById: { [`sprk_matter:${MATTER_ID}`]: { sprk_matternumber: 'Acme v. Widget Co' } },
    });
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=' + MATTER_ID;

    await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({ sprk_displayname: 'Acme v. Widget Co' })
    );
  });

  it('addBookmark_MdaRecordUrl_NoPrimaryNameResolvable_FallsBackToGenericEntityLabel', async () => {
    const fakeXrm = installMockXrm();
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=' + MATTER_ID;

    await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).toHaveBeenCalledWith(
      NAVITEM_ENTITY,
      expect.objectContaining({ sprk_displayname: 'Matter' })
    );
  });

  it('addBookmark_AlreadyPinnedRecordUrl_DedupesRatherThanDuplicating', async () => {
    const seed: FakeNavItemRow[] = [
      {
        sprk_navitemid: 'existing-pin',
        _ownerid_value: OWNER_ID,
        sprk_type: NavItemType.Pin,
        sprk_targetlogicalname: 'sprk_matter',
        sprk_targetid: MATTER_ID,
      },
    ];
    const fakeXrm = installMockXrm({ seed });
    const url =
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=' + MATTER_ID;

    const result = await addBookmark(OWNER_ID, url);

    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
    expect(result.created).toBe(false);
    expect(result.navItemId).toBe('existing-pin');
  });

  // ───────────────────────────────────────────────────────────────────────
  // HARD MUST-NOT: sprk_monitor is never written by either gesture
  // ───────────────────────────────────────────────────────────────────────

  it('bothGestures_NeverTouchSprkMonitor_OnlySprkNavitem', async () => {
    const fakeXrm = installMockXrm({
      pageContext: { pageType: 'entityrecord', entityName: 'sprk_matter', entityId: MATTER_ID },
    });

    await pinCurrentPage(OWNER_ID);
    await addBookmark(OWNER_ID, 'https://example.com/kb/1');
    await addBookmark(
      OWNER_ID,
      'https://spaarkedev1.crm.dynamics.com/main.aspx?pagetype=entitylist&etn=sprk_matter&viewid=' + VIEW_ID
    );

    const touchedEntities = allTouchedEntities(fakeXrm);
    expect(touchedEntities.length).toBeGreaterThan(0);
    expect(touchedEntities).not.toContain(FORBIDDEN_ENTITY);
    expect(touchedEntities.every(entity => entity === NAVITEM_ENTITY)).toBe(true);
  });
});
