/**
 * monitoredService.test.ts (task 052, spec FR-09 / OQ-1b)
 *
 * Covers the task POML's closed acceptance-criteria set:
 *   - Lists the current user's `sprk_monitor=true` records, OWNER-scoped
 *     (see `monitoredService.ts` module docblock "Scoping" — assigned-to-me
 *     is escalated/deferred for r1, not implemented here).
 *   - Merges N per-entity queries across `MONITOR_ENTITY_SET`.
 *   - Negative: a record with `sprk_monitor=true` NOT owned by the current
 *     user does not appear (the filter excludes it server-side).
 *   - Negative: an entity with no matching results / a throwing entity
 *     contributes nothing, with no error propagated.
 *   - No Xrm / no current-user id: resolves to `[]`, never throws.
 *   - Every query filters BOTH `sprk_monitor eq true` AND
 *     `_ownerid_value eq {userId}` — never a monitor-only or owner-only query.
 *   - `sprk_communication` (which carries a DIFFERENT field, `sprk_ismonitored`)
 *     is never queried by this module.
 *   - Read-only: no `createRecord`/`updateRecord`/`deleteRecord` call is ever
 *     issued by this module.
 *
 * `window.Xrm` is installed directly (mirrors `editedByMeService.test.ts`) so
 * `getXrm()` resolves it via its normal frame-walk — no module mock of
 * `xrmContext` itself.
 *
 * @see ../monitoredService.ts
 * @see projects/spaarke-side-pane-navigation-history-r1/notes/task-052-monitored-scope-escalation.md
 */

import {
  MONITOR_ENTITY_SET,
  MONITORED_TOP_PER_ENTITY,
  listMonitoredByMe,
} from '../monitoredService';

// ─────────────────────────────────────────────────────────────────────────────
// Fake Xrm
// ─────────────────────────────────────────────────────────────────────────────

const CURRENT_USER_ID = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
const OTHER_USER_ID = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';

const PRIMARY_NAME_FIELD: Record<string, string> = {
  sprk_matter: 'sprk_matternumber',
  sprk_project: 'sprk_projectnumber',
  sprk_document: 'sprk_documentname',
  sprk_todo: 'sprk_name',
  sprk_event: 'sprk_eventname',
  sprk_workassignment: 'sprk_workassignmentnumber',
  sprk_invoice: 'sprk_name',
};

interface FakeRecord {
  id: string;
  name?: string;
  /** The record's actual owner — used by the fake server to decide whether
   * the `_ownerid_value eq {userId}` filter clause would match it. */
  ownerId?: string;
  /** The record's actual `sprk_monitor` value — used by the fake server to
   * decide whether the `sprk_monitor eq true` filter clause would match it. */
  monitor?: boolean;
}

interface FakeXrmOptions {
  /** Rows "in the org" per monitor-set entity — the fake server applies the
   * query's filter clauses itself so tests assert real filtering behavior. */
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
    // Every Monitored query MUST filter on both sprk_monitor and ownerid.
    if (!query?.includes('sprk_monitor eq true') || !query?.includes('_ownerid_value eq')) {
      return { entities: [] };
    }
    const idField = `${entity}id`;
    const nameField = PRIMARY_NAME_FIELD[entity];
    const rows = recordsByEntity[entity] ?? [];
    // Simulate server-side filtering: only rows matching BOTH clauses are returned.
    const matching = rows.filter(
      r => (r.monitor ?? true) === true && (r.ownerId ?? CURRENT_USER_ID) === CURRENT_USER_ID
    );
    return {
      entities: matching.map(r => ({
        [idField]: r.id,
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

describe('monitoredService — listMonitoredByMe', () => {
  afterEach(() => {
    removeMockXrm();
    jest.clearAllMocks();
  });

  it('MONITOR_ENTITY_SET is the seven live-verified sprk_monitor-bearing entities (task 052 schema finding)', () => {
    expect(MONITOR_ENTITY_SET).toEqual([
      'sprk_matter',
      'sprk_project',
      'sprk_document',
      'sprk_todo',
      'sprk_event',
      'sprk_workassignment',
      'sprk_invoice',
    ]);
    // sprk_communication carries a DIFFERENT field (sprk_ismonitored) — never queried here.
    expect(MONITOR_ENTITY_SET).not.toContain('sprk_communication');
  });

  it('lists the user\'s owned, monitor=true records across the entity set', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_matter: [{ id: 'matter-1', name: 'MTR-001', ownerId: CURRENT_USER_ID, monitor: true }],
        sprk_document: [{ id: 'doc-1', name: 'Contract.docx', ownerId: CURRENT_USER_ID, monitor: true }],
      },
    });

    const items = await listMonitoredByMe();

    expect(items).toHaveLength(2);
    expect(items).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ targetLogicalName: 'sprk_matter', targetId: 'matter-1', displayName: 'MTR-001' }),
        expect.objectContaining({ targetLogicalName: 'sprk_document', targetId: 'doc-1', displayName: 'Contract.docx' }),
      ])
    );
  });

  it('every query filters BOTH sprk_monitor eq true AND _ownerid_value eq {userId} with a top-per-entity cap', async () => {
    const fakeXrm = installMockXrm();

    await listMonitoredByMe();

    expect(fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.length).toBe(MONITOR_ENTITY_SET.length);
    for (const call of fakeXrm.WebApi.retrieveMultipleRecords.mock.calls) {
      const [entity, query] = call as [string, string];
      expect(MONITOR_ENTITY_SET as readonly string[]).toContain(entity);
      expect(query).toContain('sprk_monitor eq true');
      expect(query).toContain(`_ownerid_value eq ${CURRENT_USER_ID}`);
      expect(query).toContain(`$top=${MONITORED_TOP_PER_ENTITY}`);
    }
  });

  it('never queries sprk_communication (different field, sprk_ismonitored) or audit', async () => {
    const fakeXrm = installMockXrm({
      recordsByEntity: { sprk_matter: [{ id: 'm1', name: 'MTR-002', ownerId: CURRENT_USER_ID, monitor: true }] },
    });

    await listMonitoredByMe();

    const queriedEntities = fakeXrm.WebApi.retrieveMultipleRecords.mock.calls.map(call => call[0]);
    expect(queriedEntities).not.toContain('sprk_communication');
    expect(queriedEntities).not.toContain('audit');
    expect(queriedEntities.sort()).toEqual([...MONITOR_ENTITY_SET].sort());
  });

  it('is read-only: never issues createRecord/updateRecord/deleteRecord', async () => {
    const fakeXrm = installMockXrm({
      recordsByEntity: { sprk_matter: [{ id: 'm1', name: 'MTR-003', ownerId: CURRENT_USER_ID, monitor: true }] },
    });

    await listMonitoredByMe();

    expect(fakeXrm.WebApi.createRecord).not.toHaveBeenCalled();
    expect(fakeXrm.WebApi.updateRecord).not.toHaveBeenCalled();
    expect(fakeXrm.WebApi.deleteRecord).not.toHaveBeenCalled();
  });

  // ───────────────────────────────────────────────────────────────────────
  // Negative: not-owned monitor=true record is excluded (task acceptance criterion)
  // ───────────────────────────────────────────────────────────────────────

  it('negative: a monitor=true record NOT owned by the current user does not appear', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_matter: [
          { id: 'mine', name: 'MTR-MINE', ownerId: CURRENT_USER_ID, monitor: true },
          { id: 'not-mine', name: 'MTR-OTHER', ownerId: OTHER_USER_ID, monitor: true },
        ],
      },
    });

    const items = await listMonitoredByMe();

    expect(items.map(i => i.targetId)).toEqual(['mine']);
  });

  it('negative: a record owned by the current user but monitor=false does not appear', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_document: [
          { id: 'monitored', name: 'Monitored.docx', ownerId: CURRENT_USER_ID, monitor: true },
          { id: 'unmonitored', name: 'Unmonitored.docx', ownerId: CURRENT_USER_ID, monitor: false },
        ],
      },
    });

    const items = await listMonitoredByMe();

    expect(items.map(i => i.targetId)).toEqual(['monitored']);
  });

  // ───────────────────────────────────────────────────────────────────────
  // Negative: per-entity failure isolation
  // ───────────────────────────────────────────────────────────────────────

  it('negative: an entity with no matching results contributes nothing, no error', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_matter: [{ id: 'm1', name: 'MTR-004', ownerId: CURRENT_USER_ID, monitor: true }],
      },
    });

    await expect(listMonitoredByMe()).resolves.toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_matter', targetId: 'm1' }),
    ]);
  });

  it('negative: an entity whose query throws contributes nothing, no error propagated', async () => {
    installMockXrm({
      recordsByEntity: {
        sprk_document: [{ id: 'd1', name: 'Doc.docx', ownerId: CURRENT_USER_ID, monitor: true }],
      },
      throwingEntities: ['sprk_matter', 'sprk_project'],
    });

    await expect(listMonitoredByMe()).resolves.toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_document', targetId: 'd1' }),
    ]);
  });

  it('falls back to a formatted entity label when getEntityMetadata / the name field is unavailable', async () => {
    installMockXrm({
      withoutEntityMetadata: true,
      recordsByEntity: {
        sprk_workassignment: [{ id: 'w1', ownerId: CURRENT_USER_ID, monitor: true }],
      },
    });

    const items = await listMonitoredByMe();

    expect(items).toEqual([
      expect.objectContaining({ targetLogicalName: 'sprk_workassignment', targetId: 'w1', displayName: 'Workassignment' }),
    ]);
  });

  it('no Xrm available: resolves to [] without throwing', async () => {
    removeMockXrm();
    await expect(listMonitoredByMe()).resolves.toEqual([]);
  });

  it('no current-user id available: resolves to [] without throwing', async () => {
    installMockXrm({ withoutUserId: true });
    await expect(listMonitoredByMe()).resolves.toEqual([]);
  });
});
