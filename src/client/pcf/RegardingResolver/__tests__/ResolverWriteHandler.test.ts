/**
 * ResolverWriteHandler tests.
 *
 * # What these drive (changed 2026-09-04, unified-access-control-r2 task 051)
 *
 * These tests run against the REAL shared `PolymorphicResolverService` (mapped to
 * the shared TS source by `jest.config.js`), NOT a stub of it. Only the two I/O
 * seams are faked: the `IPolymorphicWebApi` shim and `fetch` (nav-prop metadata).
 *
 * That is deliberate. FR-26's whole claim is "the core-ancestor stamp reaches the
 * payload, and a reparent replaces it". A suite that mocks
 * `buildRegardingSelectionPayload` can only prove the PCF *called something* — it
 * cannot prove the stamp is there, cannot catch an ordering regression where the
 * pre-clear nulls the stamp it just set, and cannot catch a missing clear. The
 * previous version of this file mocked the `@spaarke/ui-components` BARREL, which
 * the control stopped importing when it moved to ADR-012 deep imports — so every
 * assertion here had been running against a specifier nothing imported.
 *
 * Asserted constraints:
 *   1. FR-21 / ADR-024 — the shared builder owns payload assembly. The handler
 *      contributes no derivation and no ordering of its own (asserted
 *      structurally: no ancestor/derivation identifiers in the handler source).
 *   2. FR-22 — host entity is a parameter. No `sprk_todo` / `sprk_communication`
 *      literals in the handler.
 *   3. FR-13 — clear-and-set: selecting X nulls every OTHER regarding lookup that
 *      exists on the host.
 *   4. FR-26 SET — a child-class target with core ancestor M stamps M in the SAME
 *      updateRecord call as the target lookup.
 *   5. FR-26 REPARENT — moving between targets with different ancestors leaves
 *      exactly the new stamp; the old one is nulled in the same payload.
 *   6. FR-26 CLEAR — clearRegarding nulls the ancestor lookups too, including a
 *      core lookup the host catalog does not list (`sprk_regardingservicerequest`).
 *   7. FR-26 CREATE — no updateRecord; stamps + clears are returned for staging.
 *   8. NFR-01 fail-closed — a derivation error writes NOTHING.
 */

import * as fs from 'fs';
import * as path from 'path';

import {
  applyRegardingSelection,
  clearRegarding,
  discoverHostNavProps,
  resolveAllowedCatalog,
  _resetNavPropCacheForTests,
} from '../RegardingResolver/handlers/ResolverWriteHandler';
import {
  _resetNavPropCacheForTests as _resetSharedNavPropCache,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '@spaarke/ui-components/dist/services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const HOST_TODO = '22222222-2222-2222-2222-222222222222';
const MATTER_1 = '33333333-3333-3333-3333-333333333333';
const MATTER_2 = '44444444-4444-4444-4444-444444444444';
/** A Communication whose core ancestor is MATTER_1. */
const COMM_1 = 'c0000001-0000-0000-0000-000000000001';
/** A Communication whose core ancestor is MATTER_2. */
const COMM_2 = 'c0000002-0000-0000-0000-000000000002';
/** A Communication with no core ancestor at all. */
const COMM_ORPHAN = 'c0000003-0000-0000-0000-000000000003';

type NavPropRow = {
  ReferencingAttribute: string;
  ReferencingEntityNavigationPropertyName: string;
  ReferencedEntity: string;
};

/** PascalCase the nav-prop the way Dataverse does (`sprk_RegardingMatter`). */
function navRow(column: string, referencedEntity: string, pascalSuffix: string): NavPropRow {
  return {
    ReferencingAttribute: column,
    ReferencingEntityNavigationPropertyName: `sprk_Regarding${pascalSuffix}`,
    ReferencedEntity: referencedEntity,
  };
}

/**
 * A WIDE host: all 12 catalog targets + all four core-ancestor lookups (the
 * service-request one is not in the catalog) + the record-type ref.
 *
 * ⚠️ Corrected 2026-09-04. An earlier revision of this fixture omitted
 * `sprk_regardingservicerequest` on the strength of
 * `notes/phase3-derivation-rules.md` F-050-1, which said `sprk_todo` has no such
 * column. **Live metadata says it does** (confirmed by the task-052 agent against
 * the real environment; `CoreAncestorResolver.cs:90` and `:287` carry the same
 * false claim). No production code depended on it — column presence is resolved
 * from discovered nav-props at runtime, never from an assumed list, which is
 * exactly why the wrong belief did not become a wrong behaviour — but a fixture
 * that encodes a false schema fact teaches the next reader the wrong thing.
 *
 * The narrow-host cases below therefore use a CONSTRUCTED host rather than
 * naming a real entity, since what matters is the runtime resolution, not any
 * particular entity's column list.
 */
const FULL_NAV_PROPS: NavPropRow[] = [
  navRow('sprk_regardingmatter', 'sprk_matter', 'Matter'),
  navRow('sprk_regardingproject', 'sprk_project', 'Project'),
  navRow('sprk_regardingevent', 'sprk_event', 'Event'),
  navRow('sprk_regardingcommunication', 'sprk_communication', 'Communication'),
  navRow('sprk_regardingworkassignment', 'sprk_workassignment', 'WorkAssignment'),
  navRow('sprk_regardinginvoice', 'sprk_invoice', 'Invoice'),
  navRow('sprk_regardingbudget', 'sprk_budget', 'Budget'),
  navRow('sprk_regardinganalysis', 'sprk_analysis', 'Analysis'),
  navRow('sprk_regardingorganization', 'sprk_organization', 'Organization'),
  navRow('sprk_regardingcontact', 'contact', 'Contact'),
  navRow('sprk_regardingdocument', 'sprk_document', 'Document'),
  navRow('sprk_regardingreportcard', 'sprk_reportcard', 'ReportCard'),
  navRow('sprk_regardingservicerequest', 'sprk_servicerequest', 'ServiceRequest'),
  navRow('sprk_regardingrecordtype', 'sprk_recordtype_ref', 'RecordType'),
];

/**
 * A CONSTRUCTED narrow host — carries a communication lookup but NO
 * service-request column. Not a claim about any real entity's schema; it exists
 * to exercise the "host cannot store this ancestor" branch, which must stay
 * correct for whatever the schema actually turns out to be.
 */
const NARROW_NAV_PROPS: NavPropRow[] = [
  navRow('sprk_regardingmatter', 'sprk_matter', 'Matter'),
  navRow('sprk_regardingproject', 'sprk_project', 'Project'),
  navRow('sprk_regardingcommunication', 'sprk_communication', 'Communication'),
  navRow('sprk_regardingworkassignment', 'sprk_workassignment', 'WorkAssignment'),
  navRow('sprk_regardinginvoice', 'sprk_invoice', 'Invoice'),
  navRow('sprk_regardingrecordtype', 'sprk_recordtype_ref', 'RecordType'),
];

/** An even narrower host with no communication lookup (SRFR-048 guard). */
const EVENT_NAV_PROPS: NavPropRow[] = [
  navRow('sprk_regardingmatter', 'sprk_matter', 'Matter'),
  navRow('sprk_regardingproject', 'sprk_project', 'Project'),
  navRow('sprk_regardinginvoice', 'sprk_invoice', 'Invoice'),
  navRow('sprk_regardingworkassignment', 'sprk_workassignment', 'WorkAssignment'),
  navRow('sprk_regardingrecordtype', 'sprk_recordtype_ref', 'RecordType'),
];

/**
 * A `fetch` fake for the ONE endpoint the resolver uses it for: the
 * `EntityDefinitions(...)/ManyToOneRelationships` metadata read. Nav-props are
 * returned per entity, so the TARGET's column set can differ from the HOST's —
 * which is exactly the condition FR-26 derivation has to survive.
 */
function makeFetch(navPropsByEntity: Record<string, NavPropRow[]>): jest.Mock {
  return jest.fn(async (url: string) => {
    const match = /LogicalName='([^']+)'/.exec(String(url));
    const entity = match?.[1] ?? '';
    const rows = navPropsByEntity[entity];
    if (!rows) {
      return { ok: false, status: 404, json: async () => ({}) };
    }
    return { ok: true, json: async () => ({ value: rows }) };
  });
}

/**
 * A `retrieveMultipleRecords` fake covering the three query shapes the shared
 * service issues: the `sprk_recordtype_ref` catalog read, the target-record
 * field read, and the FR-26 core-ancestor read.
 *
 * `ancestors` maps a communication id → the `_sprk_regarding*_value` row the
 * ancestor read should return.
 */
function makeWebApi(options?: { ancestors?: Record<string, Record<string, unknown>> }): {
  retrieveMultipleRecords: jest.Mock;
  updateRecord: jest.Mock;
} {
  const ancestors = options?.ancestors ?? {};
  return {
    updateRecord: jest.fn().mockResolvedValue({ id: 'ok' }),
    retrieveMultipleRecords: jest.fn(async (entity: string, query: string) => {
      if (entity === 'sprk_recordtype_ref') {
        // Catalog row: no record-number / display-name mapping configured, so the
        // resolver takes its NFR-06 graceful-blank paths. Keeps these tests about
        // FR-26 rather than about SRFR-020/052.
        return { entities: [{ sprk_recordtype_refid: `rt-${entity}`, sprk_name: 'RT' }] };
      }
      // FR-26 core-ancestor read — recognisable by its `_..._value` $select.
      if (/\$select=_sprk_regarding/.test(query)) {
        const idMatch = /eq ([0-9a-f-]+)/i.exec(query);
        const id = (idMatch?.[1] ?? '').toLowerCase();
        const row = ancestors[id];
        return { entities: row ? [row] : [{}] };
      }
      return { entities: [] };
    }),
  };
}

function nulledBindKeys(payload: Record<string, unknown>): string[] {
  return Object.entries(payload)
    .filter(([k, v]) => k.endsWith('@odata.bind') && v === null)
    .map(([k]) => k);
}

function boundValue(payload: Record<string, unknown>, navProp: string): unknown {
  return payload[`${navProp}@odata.bind`];
}

// ---------------------------------------------------------------------------

describe('ResolverWriteHandler', () => {
  beforeEach(() => {
    _resetNavPropCacheForTests();
    _resetSharedNavPropCache();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  // -------------------------------------------------------------------------
  // FR-21 / ADR-024 — the shared builder owns payload assembly
  // -------------------------------------------------------------------------

  test('ADR-024 — the handler contains no derivation or ancestor-ordering logic of its own', () => {
    const source = fs.readFileSync(
      path.join(__dirname, '..', 'RegardingResolver', 'handlers', 'ResolverWriteHandler.ts'),
      'utf8'
    );
    // Strip comments — the file legitimately DISCUSSES derivation at length.
    const code = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '');

    // The taxonomy and the derivation entry point must never be re-implemented
    // here; they are the shared service's job (ADR-024 + notes/phase3-derivation-rules.md §6).
    expect(code).not.toMatch(/CORE_RECORD_ENTITIES|CHILD_RECORD_ENTITIES/);
    expect(code).not.toMatch(/deriveCoreAncestorStamps/);
    // ...and the handler must consume the ONE combined builder that owns ordering.
    expect(code).toMatch(/buildRegardingSelectionPayload/);
  });

  test('FR-22 — no host-entity literals in the handler source', () => {
    const source = fs.readFileSync(
      path.join(__dirname, '..', 'RegardingResolver', 'handlers', 'ResolverWriteHandler.ts'),
      'utf8'
    );
    const code = source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '');
    expect(code).not.toMatch(/'sprk_todo'|"sprk_todo"/);
    expect(code).not.toMatch(/'sprk_communication'|"sprk_communication"/);
  });

  test('FR-22 — the same handler works for sprk_todo AND sprk_communication', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });
    const selection = { entityType: 'sprk_matter', recordId: MATTER_1, recordName: 'Smith v. Jones' };

    const r1 = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      selection,
      undefined,
      fetchImpl as unknown as typeof fetch
    );
    const r2 = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_communication', hostRecordId: MATTER_2 },
      selection,
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(r1.success).toBe(true);
    expect(r2.success).toBe(true);
    expect(webApi.updateRecord).toHaveBeenNthCalledWith(1, 'sprk_todo', HOST_TODO, expect.any(Object));
    expect(webApi.updateRecord).toHaveBeenNthCalledWith(2, 'sprk_communication', MATTER_2, expect.any(Object));
  });

  test('rejects an unknown entity type without reading or writing anything', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'unknown_entity', recordId: 'x', recordName: 'y' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/Unknown entity type/);
    expect(webApi.updateRecord).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // FR-13 — clear-and-set
  // -------------------------------------------------------------------------

  test('FR-13 — selecting one target nulls every OTHER regarding lookup on the host', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS });

    await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_matter', recordId: MATTER_1, recordName: 'X' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    const [, , payload] = webApi.updateRecord.mock.calls[0];
    // 12 catalog targets (one chosen → 11 nulled) + the service-request lookup,
    // which is NOT in the catalog and reaches the payload only through the
    // core-ancestor union — the union that makes reparent safe.
    expect(nulledBindKeys(payload)).toHaveLength(12);
    expect(boundValue(payload, 'sprk_RegardingServiceRequest')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBe(`/sprk_matters(${MATTER_1})`);
  });

  test('SRFR-048 — a narrow host only nulls lookups that exist on that entity', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_event: EVENT_NAV_PROPS });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_event', hostRecordId: HOST_TODO },
      { entityType: 'sprk_matter', recordId: MATTER_1, recordName: 'X' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    const [, , payload] = webApi.updateRecord.mock.calls[0];
    const nulled = nulledBindKeys(payload);

    // Writing a column the host lacks makes Dataverse reject the whole update.
    for (const absent of ['Communication', 'Organization', 'Contact', 'Budget', 'Analysis', 'Document', 'Event']) {
      expect(nulled.some(k => k.includes(`sprk_Regarding${absent}`))).toBe(false);
    }
    for (const present of ['Project', 'Invoice', 'WorkAssignment']) {
      expect(nulled.some(k => k.includes(`sprk_Regarding${present}`))).toBe(true);
    }
  });

  // -------------------------------------------------------------------------
  // FR-26 — SET
  // -------------------------------------------------------------------------

  test('FR-26 SET — a child target stamps its core ancestor in the SAME updateRecord call', async () => {
    const webApi = makeWebApi({
      ancestors: { [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 } },
    });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'Re: discovery' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(result.ancestorStatus).toBe('derived');
    expect(webApi.updateRecord).toHaveBeenCalledTimes(1);

    const [, , payload] = webApi.updateRecord.mock.calls[0];
    // The target lookup AND the ancestor stamp ride the one call.
    expect(boundValue(payload, 'sprk_RegardingCommunication')).toBe(`/sprk_communications(${COMM_1})`);
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBe(`/sprk_matters(${MATTER_1})`);
    expect(result.ancestorStamps).toEqual([
      expect.objectContaining({
        entityType: 'sprk_matter',
        lookupAttribute: 'sprk_regardingmatter',
        recordId: MATTER_1,
      }),
    ]);
  });

  test('FR-26 SET — a CORE target stamps only itself (Matter does NOT inherit from Project)', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS, sprk_matter: FULL_NAV_PROPS });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_matter', recordId: MATTER_1, recordName: 'X' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.ancestorStatus).toBe('core-target');
    const [, , payload] = webApi.updateRecord.mock.calls[0];
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBe(`/sprk_matters(${MATTER_1})`);
    // The Matter's own Project association is NOT an access edge — inverting this
    // hands every Project holder every Matter beneath it.
    expect(boundValue(payload, 'sprk_RegardingProject')).toBeNull();
  });

  test('FR-26 — an orphan child target yields no-ancestor (distinct from error) and still writes', async () => {
    const webApi = makeWebApi({ ancestors: { [COMM_ORPHAN]: {} } });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_ORPHAN, recordName: 'Orphan' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(result.ancestorStatus).toBe('no-ancestor');
    expect(result.ancestorStamps).toEqual([]);
    expect(webApi.updateRecord).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // FR-26 — REPARENT (the transition the stamp exists for)
  // -------------------------------------------------------------------------

  test('FR-26 REPARENT — the new ancestor replaces the old one in ONE payload', async () => {
    const webApi = makeWebApi({
      ancestors: {
        [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 },
        [COMM_2]: { _sprk_regardingmatter_value: MATTER_2 },
      },
    });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });
    const ctx = { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO };

    // Parent under C1 (ancestor M1) …
    await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );
    // … then reparent to C2 (ancestor M2).
    const reparent = await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_communication', recordId: COMM_2, recordName: 'C2' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(reparent.success).toBe(true);
    const [, , payload] = webApi.updateRecord.mock.calls[1];

    // Exactly the NEW ancestor remains. If the pre-clear ran after the stamp, or
    // the stamp were skipped, this would still hold M1 — the child would stay
    // visible to M1's principals (over-grant) and invisible to M2's (under-grant).
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBe(`/sprk_matters(${MATTER_2})`);
    expect(boundValue(payload, 'sprk_RegardingMatter')).not.toBe(`/sprk_matters(${MATTER_1})`);
    expect(boundValue(payload, 'sprk_RegardingCommunication')).toBe(`/sprk_communications(${COMM_2})`);
  });

  test('FR-26 REPARENT — moving from a child ancestor to an unrelated CORE nulls the stale stamp', async () => {
    const webApi = makeWebApi({
      ancestors: { [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 } },
    });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
      sprk_project: FULL_NAV_PROPS,
    });
    const ctx = { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO };

    await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );
    // Reparent onto a Project — a DIFFERENT core entity, so the matter stamp must go.
    await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_project', recordId: MATTER_2, recordName: 'P2' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    const [, , payload] = webApi.updateRecord.mock.calls[1];
    expect(boundValue(payload, 'sprk_RegardingProject')).toBe(`/sprk_projects(${MATTER_2})`);
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingCommunication')).toBeNull();
  });

  test('FR-26 REPARENT — the stale-stamp clear reaches a core lookup the catalog does not list', async () => {
    // `TODO_REGARDING_CATALOG` has no `sprk_servicerequest` entry (F-050-3). On a
    // host that DOES carry that column, a catalog-only pre-clear would leave the
    // stamp standing after a reparent.
    const webApi = makeWebApi({ ancestors: { [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 } } });
    const fetchImpl = makeFetch({
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_communication', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    const [, , payload] = webApi.updateRecord.mock.calls[0];
    expect(boundValue(payload, 'sprk_RegardingServiceRequest')).toBeNull();
    expect(result.clearLookups).toContain('sprk_regardingservicerequest');
  });

  test('FR-26 — a derived ancestor the host cannot store is surfaced, not swallowed (F-050-2)', async () => {
    // A child regarding a Communication whose ancestor is a Service Request,
    // hosted on an entity that has no column for it. The ancestor IS derived but
    // cannot be written — a real inheritance hole that must be reported rather
    // than silently dropped.
    //
    // The host here is the CONSTRUCTED narrow fixture, not a real entity: which
    // entities lack which column is a schema question that changed under us once
    // already (see FULL_NAV_PROPS). What this test pins is the BEHAVIOUR when a
    // host lacks the column, which holds whatever the schema turns out to be.
    const webApi = makeWebApi({
      ancestors: { [COMM_1]: { _sprk_regardingservicerequest_value: MATTER_1 } },
    });
    const fetchImpl = makeFetch({
      sprk_narrowhost: NARROW_NAV_PROPS, // no service-request column
      sprk_communication: FULL_NAV_PROPS, // has one
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_narrowhost', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(result.unstampable).toContain('sprk_regardingservicerequest');
    // …and it must NOT be offered for CREATE-mode staging either.
    expect(result.ancestorStamps).toEqual([]);
  });

  // -------------------------------------------------------------------------
  // FR-26 — CLEAR
  // -------------------------------------------------------------------------

  test('FR-26 CLEAR — clearRegarding nulls the ancestor lookups along with the regarding fields', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS });

    const result = await clearRegarding(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    const payload = result.payload as Record<string, unknown>;

    // Every core-ancestor lookup the host carries is nulled — a cleared child has
    // no parent, so it must inherit nothing.
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingProject')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingWorkAssignment')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingServiceRequest')).toBeNull();
    // 12 catalog lookups + the non-catalog service-request lookup + record-type.
    expect(nulledBindKeys(payload)).toHaveLength(14);
    expect(payload['sprk_regardingrecordid']).toBeNull();
    expect(payload['sprk_regardingrecordname']).toBeNull();
    expect(payload['sprk_regardingrecordurl']).toBeNull();
    expect(webApi.updateRecord).toHaveBeenCalledTimes(1);
  });

  test('FR-26 CLEAR — a core lookup outside the catalog is nulled too (sprk_regardingservicerequest)', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_communication: FULL_NAV_PROPS });

    const result = await clearRegarding(
      { webApi, hostEntity: 'sprk_communication', hostRecordId: HOST_TODO },
      fetchImpl as unknown as typeof fetch
    );

    const payload = result.payload as Record<string, unknown>;
    expect(boundValue(payload, 'sprk_RegardingServiceRequest')).toBeNull();
    expect(result.clearLookups).toContain('sprk_regardingservicerequest');
  });

  test('FR-26 CLEAR — never writes a lookup the host does not carry (SRFR-048)', async () => {
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_event: EVENT_NAV_PROPS });

    const result = await clearRegarding(
      { webApi, hostEntity: 'sprk_event', hostRecordId: HOST_TODO },
      fetchImpl as unknown as typeof fetch
    );

    const payload = result.payload as Record<string, unknown>;
    // `sprk_event` has no service-request column; emitting it would 400 the whole
    // clear and leave the stale stamps in place.
    expect(Object.keys(payload).some(k => k.includes('ServiceRequest'))).toBe(false);
    expect(Object.keys(payload).some(k => k.includes('Communication'))).toBe(false);
    expect(boundValue(payload, 'sprk_RegardingMatter')).toBeNull();
  });

  // -------------------------------------------------------------------------
  // FR-26 — CREATE mode (staging, never a follow-up update)
  // -------------------------------------------------------------------------

  test('FR-26 CREATE — returns stamps + clears for staging and calls NO updateRecord', async () => {
    const webApi = makeWebApi({ ancestors: { [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 } } });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: undefined },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(webApi.updateRecord).not.toHaveBeenCalled();
    // The caller stages these onto form attributes so they ride ONE insert. A
    // post-create update would leave an unscoped child if it crashed in between.
    expect(result.ancestorStamps).toEqual([
      expect.objectContaining({ lookupAttribute: 'sprk_regardingmatter', recordId: MATTER_1 }),
    ]);
    // Attribute logical names (form attributes), not nav-prop names.
    expect(result.clearLookups).toContain('sprk_regardingproject');
    expect(result.clearLookups).not.toContain('sprk_regardingmatter'); // being SET
    expect(result.clearLookups).not.toContain('sprk_regardingcommunication'); // being SET
  });

  test('FR-26 CREATE — reparent before first save stages the clear of the previous pick', async () => {
    const webApi = makeWebApi({
      ancestors: {
        [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 },
        [COMM_2]: { _sprk_regardingproject_value: MATTER_2 },
      },
    });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });
    const ctx = { webApi, hostEntity: 'sprk_todo', hostRecordId: undefined };

    await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );
    const second = await applyRegardingSelection(
      ctx,
      { entityType: 'sprk_communication', recordId: COMM_2, recordName: 'C2' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    // The second pick's ancestor is a Project; the Matter staged by the first pick
    // must be staged for CLEARING or the INSERT carries both.
    expect(second.ancestorStamps).toEqual([
      expect.objectContaining({ lookupAttribute: 'sprk_regardingproject', recordId: MATTER_2 }),
    ]);
    expect(second.clearLookups).toContain('sprk_regardingmatter');
  });

  // -------------------------------------------------------------------------
  // NFR-01 — fail closed
  // -------------------------------------------------------------------------

  test('NFR-01 — a derivation error writes NOTHING and surfaces the error', async () => {
    // Target metadata unreadable → the shared derivation cannot tell "no ancestor"
    // from "could not find out", so it fails closed.
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS }); // sprk_communication → 404

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(false);
    expect(result.ancestorStatus).toBe('error');
    expect(result.error).toMatch(/derivation|nav-props/i);
    expect(webApi.updateRecord).not.toHaveBeenCalled();
    // No partial stamp is offered to the CREATE-mode bridge either.
    expect(result.ancestorStamps).toBeUndefined();
    expect(result.payload).toBeUndefined();
  });

  test('NFR-01 — an ancestor read that throws also blocks the write', async () => {
    const webApi = makeWebApi();
    webApi.retrieveMultipleRecords.mockImplementation(async (entity: string, query: string) => {
      if (/\$select=_sprk_regarding/.test(query)) throw new Error('403 forbidden');
      if (entity === 'sprk_recordtype_ref') return { entities: [] };
      return { entities: [] };
    });
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/403 forbidden/);
    expect(webApi.updateRecord).not.toHaveBeenCalled();
  });

  test('updateRecord failure surfaces the error and the derived state for diagnostics', async () => {
    const webApi = makeWebApi({ ancestors: { [COMM_1]: { _sprk_regardingmatter_value: MATTER_1 } } });
    webApi.updateRecord.mockRejectedValueOnce(new Error('403 forbidden'));
    const fetchImpl = makeFetch({
      sprk_todo: FULL_NAV_PROPS,
      sprk_communication: FULL_NAV_PROPS,
    });

    const result = await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_communication', recordId: COMM_1, recordName: 'C1' },
      undefined,
      fetchImpl as unknown as typeof fetch
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/403 forbidden/);
    expect(result.ancestorStatus).toBe('derived');
  });

  // -------------------------------------------------------------------------
  // resolveAllowedCatalog + nav-prop cache
  // -------------------------------------------------------------------------

  test('resolveAllowedCatalog returns the full catalog when input is empty', () => {
    expect(resolveAllowedCatalog(undefined)).toHaveLength(12);
    expect(resolveAllowedCatalog('')).toHaveLength(12);
    expect(resolveAllowedCatalog(null)).toHaveLength(12);
  });

  test('resolveAllowedCatalog filters to the comma-separated list', () => {
    const filtered = resolveAllowedCatalog('sprk_matter,sprk_project, contact');
    expect(filtered.map(c => c.entityType).sort()).toEqual(['contact', 'sprk_matter', 'sprk_project']);
  });

  test('a maker-restricted catalog still pre-clears the FULL lookup set', async () => {
    // `regardingTargets` governs what a user may SELECT. If it also governed the
    // pre-clear, narrowing the maker list would strand a previously-set lookup —
    // and with it a stale ancestor stamp.
    const webApi = makeWebApi();
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS });
    const restricted = resolveAllowedCatalog('sprk_matter,sprk_project');

    await applyRegardingSelection(
      { webApi, hostEntity: 'sprk_todo', hostRecordId: HOST_TODO },
      { entityType: 'sprk_matter', recordId: MATTER_1, recordName: 'X' },
      restricted,
      fetchImpl as unknown as typeof fetch
    );

    const [, , payload] = webApi.updateRecord.mock.calls[0];
    expect(boundValue(payload, 'sprk_RegardingCommunication')).toBeNull();
    expect(boundValue(payload, 'sprk_RegardingDocument')).toBeNull();
  });

  test('discoverHostNavProps caches per host entity', async () => {
    const fetchImpl = makeFetch({ sprk_todo: FULL_NAV_PROPS });
    const a = await discoverHostNavProps('sprk_todo', fetchImpl as unknown as typeof fetch);
    const b = await discoverHostNavProps('sprk_todo', fetchImpl as unknown as typeof fetch);
    expect(a).toBe(b);
    expect(fetchImpl).toHaveBeenCalledTimes(1);
  });

  test('discoverHostNavProps returns an empty array on HTTP error (graceful)', async () => {
    const fetchImpl = makeFetch({});
    const result = await discoverHostNavProps('sprk_communication', fetchImpl as unknown as typeof fetch);
    expect(result).toEqual([]);
  });
});
