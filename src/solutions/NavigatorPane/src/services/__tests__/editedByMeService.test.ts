/**
 * editedByMeService.test.ts (task 042, spec FR-04 / OQ-5)
 *
 * Covers the task POML's closed acceptance-criteria set:
 *   - Merges N per-entity `modifiedby=me` queries across the fixed core set,
 *     sorted `modifiedon desc`.
 *   - A record edited via a flow (not the UI) still appears — the
 *     derivation is purely `modifiedon`-based, independent of any capture
 *     history mechanism.
 *   - NO audit entity is ever queried.
 *   - Negative: an entity with no matching results contributes nothing,
 *     with no error.
 *   - Negative: an entity whose query throws (missing/no-privilege)
 *     contributes nothing, with no error propagated to the caller.
 *   - No Xrm / no current-user id: resolves to `[]`, never throws.
 *
 * `window.Xrm` is installed directly (mirrors `RecentTab.test.tsx` /
 * `navigatorCaptureService.test.ts`) so `getXrm()` resolves it via its normal
 * frame-walk — no module mock of `xrmContext` itself.
 *
 * @see ../editedByMeService.ts
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-001-spike-report.md (R2 resolution)
 */

import {
  CORE_ENTITY_SET,
  EDITED_BY_ME_TOP_PER_ENTITY,
  listEditedByMe,
} from '../editedByMeService';

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

const CURRENT_USER_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

const PRIMARY_NAME_FIELD: Record<string, string> = {
  sprk_matter: 'sprk_matternumber',
  sprk_project: 'sprk_projectnumber',
  sprk_document: 'sprk_documentname',
  sprk_todo: 'sprk_name',
  sprk_event: 'sprk_eventname',
  sprk_communication: 'sprk_name',
};

interface FakeRecord {
  id: string;
  modifiedon: string;
  name?: string;
}

interface FakeXrmOptions {
  /** Rows returned per core-set entity when its `modifiedby=me` query runs. */
  recordsByEntity?: Partial<Record<string, FakeRecord[]>>;
  /** Entities whose query should reject (simulates missing entity / no privilege). */
  throwingEntities?: string[];
  /** Omit `Utility.getEntityMetadata` entirely (simulates an older/limited host). */
  withoutEntityMetadata?: boolean;
  /** Omit the current-user id from the global context. */
  withoutUserId?: boolean;
}

function buildFakeXrm(options: FakeXrmOptions = {}) {
  const recordsByEntity = options.recordsByEntity ?? {};
  const throwingEntities = new Set(options.throwingEntities ?? []);

  const retrieveMultipleRecords = jest.fn(async (entity: string, query?: string) => {
    if (throwingEntities.has(entity)) {
      throw new Error(`Simulated failure for ${entity}`);
    }
    // Every edited-by-me query filters on _modifiedby_value — assert the
    // caller never issues a bare/unexpected query shape for these entities.
    if (!query?.includes('_modifiedby_value eq')) {
      return { entities: [] };
    }
    const idField = `${entity}id`;
    const nameField = PRIMARY_NAME_FIELD[entity];
    const rows = recordsByEntity[entity] ?? [];
    return {
      entities: rows.map(r => ({
        [idField]: r.id,
        modifiedon: r.modifiedon,
        ...(nameField && r.name !== undefined ? { [nameField]: r.name } : {}),
      })),
    };
  });

  const getEntityMetadata = jest.fn(async (entity: string) => ({
    PrimaryNameAttribute: PRIMARY_NAME_FIELD[entity] ?? 'name',
  }));

  const getGlobalContext = jest.fn(() => ({
    userSettings: options.withoutUserId
      ? ({ userName: 'Test User', languageId: 1033 } as unknown as { userId: string; userName: string; languageId: number })
      : { userId: CURRENT_USER_ID, userName: 'Test User', languageId: 1033 },
    getClientUrl: () => 'https://spaarkedev1.crm.dynamics.com',
    getCurrentAppUrl: () => 'https://spaarkedev1.crm.dynamics.com',
    getVersion: () => '9.2',
  }));

  return {
    WebApi: {
      retrieveMultipleRecords,
      retrieveRecord: jest.fn(),
      createRecord: jest.fn(),
      updateRecord: jest.fn(),
      deleteRecord: jest.fn(),
    },
    Utility: {
      getGlobalContext,
      ...(options.withoutEntityMetadata ? {} : { getEntityMetadata }),
    },
  };
}

function installMockXrm(options?: FakeXrmOptions) {
  const fakeXrm = buildFakeXrm(options);
  (window as unknown as { Xrm: unknown }).Xrm = fakeXrm;
  return fakeXrm;
}

function removeMockXrm(): void {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

// ─────────────────────────────────────────────────────────────────────────────
// Suite
// ─────────────────────────────────────────────────────────────────────────────

describe('editedByMeService — listEditedByMe', () => {
  afterEach(() => {
    removeMockXrm();
    jest.clearAllMocks();
  });

  it('CORE_ENTITY_SET matches the spec FR-04 / OQ-5 fixed core set', () => {
    expect(CORE_ENTITY_SET).toEqual([
      'sprk_matter',
      'sprk_project',
      'sprk_document',
      'sprk_todo',
      'sprk_event',
      'sprk_communication',
    ]);
  });

  it('merges per-entity modifiedby=me results and sorts modifiedon desc', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_matter: [{ id: 'matter-1', modifiedon: '2026-08-10T10:00:00.000Z', name: 'MTR-001' }],
        sprk_document: [{ id: 'doc-1', modifiedon: '2026-08-12T09:00:00.000Z', name: 'Contract.docx' }],
        sprk_todo: [{ id: 'todo-1', modifiedon: '2026-08-11T08:00:00.000Z', name: 'Follow up' }],
      },
    });

    const items = await listEditedByMe();

    expect(items.map(i => i.targetId)).toEqual(['doc-1', 'todo-1', 'matter-1']);
    expect(items[0]).toMatchObject({
      targetLogicalName: 'sprk_document',
      targetId: 'doc-1',
      displayName: 'Contract.docx',
      modifiedOn: '2026-08-12T09:00:00.000Z',
    });
  });

  it('a flow-edited record (modifiedon-derived, no capture history involved) appears', async () => {
    // No sprk_navitem / capture entity touched at all — this record was
    // never "viewed" through the Navigator, only modified by a flow. The
    // merge is driven purely by the entity's own `modifiedon`.
    installMockXrm({
      recordsByEntity: {
        sprk_event: [{ id: 'event-flow-1', modifiedon: '2026-08-13T00:00:00.000Z', name: 'Auto-scheduled hearing' }],
      },
    });

    const items = await listEditedByMe();

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({
      targetLogicalName: 'sprk_event',
      targetId: 'event-flow-1',
      displayName: 'Auto-scheduled hearing',
    });
  });

  it('NEVER queries the audit entity', async () => {
    const fakeXrm = installMockXrm({
      recordsByEntity: { sprk_matter: [{ id: 'm1', modifiedon: '2026-08-12T00:00:00.000Z', name: 'MTR-002' }] },
    });

    await listEditedByMe();

    const queriedEntities = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.map(call => call[0]);
    expect(queriedEntities).not.toContain('audit');
    expect(queriedEntities.sort()).toEqual([...CORE_ENTITY_SET].sort());
  });

  it('queries every core-set entity with a top-per-entity cap', async () => {
    const fakeXrm = installMockXrm();

    await listEditedByMe();

    for (const call of fakeXrm.WebApi.retrieveMultipleRecords.mock.calls) {
      const [, query] = call as [string, string];
      expect(query).toContain(`_modifiedby_value eq ${CURRENT_USER_ID}`);
      expect(query).toContain(`$top=${EDITED_BY_ME_TOP_PER_ENTITY}`);
      expect(query).toContain('$orderby=modifiedon desc');
    }
  });

  it('negative: an entity with no matching results contributes nothing, no error', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_matter: [{ id: 'm1', modifiedon: '2026-08-12T00:00:00.000Z', name: 'MTR-003' }],
        // sprk_project, sprk_document, sprk_todo, sprk_event, sprk_communication
        // all default to []. Nothing throws.
      },
    });

    await expect(listEditedByMe()).resolves.toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_matter', targetId: 'm1' }),
    ]);
  });

  it('negative: an entity whose query throws contributes nothing, no error propagated', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_document: [{ id: 'd1', modifiedon: '2026-08-12T00:00:00.000Z', name: 'Doc.docx' }],
      },
      throwingEntities: ['sprk_matter', 'sprk_project'],
    });

    await expect(listEditedByMe()).resolves.toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_document', targetId: 'd1' }),
    ]);
  });

  it('falls back to a formatted entity label when getEntityMetadata / the name field is unavailable', async () => {
    installMockXrm({
      withoutEntityMetadata: true,
      recordsByEntity: {
        sprk_communication: [{ id: 'c1', modifiedon: '2026-08-12T00:00:00.000Z' }],
      },
    });

    const items = await listEditedByMe();

    expect(items).toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_communication', targetId: 'c1', displayName: 'Communication' }),
    ]);
  });

  it('no Xrm available: resolves to [] without throwing', async () => {
    removeMockXrm();
    await expect(listEditedByMe()).resolves.toEqual([]);
  });

  it('no current-user id available: resolves to [] without throwing', async () => {
    installMockXrm({ withoutUserId: true });
    await expect(listEditedByMe()).resolves.toEqual([]);
  });
});
