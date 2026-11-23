/**
 * projectService BU-cascade unit tests — converted by field-mapping-server-write-path-r1 task 022 (FR-16, FR-38/FR-40).
 *
 * Before task 022 these tests asserted that `ProjectService.createProject` cascaded `sprk_searchindexname` from the
 * user's business unit onto the create payload (FR-WIZ-02 / G2). Both index columns are now the SERVER's on a project
 * (`RecordTableRule.SearchIndexed`; the index follows the content type, FR-40), and the create goes through the BFF
 * child-records route, so the same scenarios now prove the cascade NEVER reaches the body (as task 021 did for the
 * matter, and task 076 for the container):
 *
 *   - `sprk_searchindexname`, the `sprk_ai_search_index` bind and `sprk_containerid` never appear, whatever the BU has;
 *   - `sprk_issecure` never appears (task 150: field-secured; a secure project is asked for in the query string);
 *   - `ProjectService` no longer calls `EntityCreationService.applyUserBuDefaults`;
 *   - the error path is unchanged by the cascade: a refused create is a failed create.
 *
 * The payload is read from the POST body to the route.
 */

import { ProjectService } from '../projectService';
import { EntityCreationService, type IUserBuCascadeDefaults } from '../../../services/EntityCreationService';
import type { ICreateProjectFormState } from '../projectFormTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const BFF = 'https://bff.example.test';
const ROUTE = `${BFF}/api/v1/child-records/sprk_project`;
const NEW_ID = '00000000-0000-0000-0000-000000000001';

const EMPTY_FORM: ICreateProjectFormState = {
  projectTypeId: '',
  projectTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  projectName: 'Test Project',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  description: '',
  isSecure: false,
};

function makeDataService(): { service: IDataService; createSpy: jest.Mock } {
  const createSpy = jest.fn().mockResolvedValue(NEW_ID);
  const service: IDataService = {
    createRecord: createSpy,
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
  return { service, createSpy };
}

/** The host's authenticated fetch: the create route answers 201 with the new id. */
function makeBff(): jest.Mock {
  return jest.fn(async () => ({ ok: true, status: 201, json: async () => ({ id: NEW_ID }) }) as unknown as Response);
}

/** The body of the one create the route received. */
function sentBody(bff: jest.Mock): Record<string, unknown> {
  const calls = bff.mock.calls.filter(([url]) => String(url).startsWith(ROUTE));
  expect(calls).toHaveLength(1);
  return JSON.parse((calls[0][1] as RequestInit).body as string) as Record<string, unknown>;
}

const CASCADE_KEYS = /searchindex|search_index|containerid/i;

// Mock the EntityDefinitions metadata fetch (nav-prop discovery). Offering the sprk_ai_search_index lookup proves that
// the old `sprk_AI_Search_Index` bind is not written even when its navigation property exists.
beforeEach(() => {
  _resetNavPropCacheForTests();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).fetch = jest.fn().mockResolvedValue({
    ok: true,
    json: async () => ({
      value: [
        {
          ReferencingAttribute: 'sprk_ai_search_index',
          ReferencingEntityNavigationPropertyName: 'sprk_AI_Search_Index',
          ReferencedEntity: 'sprk_aisearchindex',
        },
      ],
    }),
  });
  jest.spyOn(console, 'info').mockImplementation(() => undefined);
  jest.spyOn(console, 'warn').mockImplementation(() => undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  _resetNavPropCacheForTests();
});

// ---------------------------------------------------------------------------
// The BU cascade never reaches the body
// ---------------------------------------------------------------------------

describe("ProjectService.createProject — the BU cascade is the server's (task 022, FR-38/FR-40)", () => {
  it('writes neither sprk_searchindexname, the AI search index bind nor sprk_containerid when the BU has all three', async () => {
    const { service, createSpy } = makeDataService();
    const bff = makeBff();

    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
      searchIndexId: 'bu-index-guid',
    };

    const result = await new ProjectService(service, bff, BFF).createProject(EMPTY_FORM, defaults);
    expect(result.success).toBe(true);
    expect(createSpy).not.toHaveBeenCalled();

    const payload = sentBody(bff);
    expect(Object.keys(payload).filter(k => CASCADE_KEYS.test(k))).toEqual([]);
    expect(JSON.stringify(payload)).not.toMatch(/spaarke-knowledge-index-v2|bu-index-guid|bu-container-abc/);
  });

  it('writes neither field when BU has containerId but no searchIndexName (Spaarke Dev 1 / Test 1 scenario)', async () => {
    const { service } = makeDataService();
    const bff = makeBff();

    await new ProjectService(service, bff, BFF).createProject(EMPTY_FORM, {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: undefined,
    });

    expect(Object.keys(sentBody(bff)).filter(k => CASCADE_KEYS.test(k))).toEqual([]);
  });

  // Task 076 — the case W1 endangered: a project the user marked SECURE must not be born pointing at the shared
  // business-unit container.
  it('writes no sprk_containerid on a SECURE project even when the BU supplies one', async () => {
    const { service } = makeDataService();
    const bff = makeBff();

    const result = await new ProjectService(service, bff, BFF).createProject(
      { ...EMPTY_FORM, isSecure: true },
      {
        businessUnitId: 'bu-guid-1',
        containerId: 'shared-bu-container-abc',
        searchIndexName: 'spaarke-knowledge-index-v2',
      }
    );
    expect(result.success).toBe(true);

    const payload = sentBody(bff);
    expect(payload).not.toHaveProperty('sprk_containerid');
    expect(JSON.stringify(payload)).not.toContain('shared-bu-container-abc');
  });

  // Task 150: sprk_issecure is field-secured — only the BFF may write it. Secure is asked for in the route's query
  // string (`secure=true`, task 049 fix round 2), never in this payload.
  it.each([true, false])('never writes sprk_issecure on the create payload (secure requested: %s)', async isSecure => {
    const { service } = makeDataService();
    const bff = makeBff();

    const result = await new ProjectService(service, bff, BFF).createProject({ ...EMPTY_FORM, isSecure });
    expect(result.success).toBe(true);

    expect(Object.keys(sentBody(bff)).map(k => k.toLowerCase())).not.toContain('sprk_issecure');
  });

  it.each([
    ['omitted', undefined],
    ['with both fields undefined (BU exists but unset)', { businessUnitId: 'bu-guid-1' } as IUserBuCascadeDefaults],
  ])("the body is the user's values only when cascadeDefaults is %s", async (_label, defaults) => {
    const { service } = makeDataService();
    const bff = makeBff();

    await new ProjectService(service, bff, BFF).createProject(EMPTY_FORM, defaults);

    expect(sentBody(bff)).toEqual({ sprk_projectname: 'Test Project' });
  });
});

// ---------------------------------------------------------------------------
// INV-5: the helper is no longer called by ProjectService (nothing of the cascade is written)
// ---------------------------------------------------------------------------

describe('ProjectService.createProject — no client BU cascade (was INV-5 wiring, FR-WIZ-08)', () => {
  it('does not call EntityCreationService.applyUserBuDefaults', async () => {
    const applySpy = jest.spyOn(EntityCreationService, 'applyUserBuDefaults');
    const { service } = makeDataService();

    await new ProjectService(service, makeBff(), BFF).createProject(EMPTY_FORM, {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    });

    expect(applySpy).not.toHaveBeenCalled();
  });

  // The static helper itself is unchanged and still INV-5-safe for its other callers (EntityCreationService.cascade.test.ts
  // is canonical); re-asserted here because this file used to own the project caller boundary.
  it('static applyUserBuDefaults preserves explicit values on the payload (INV-5)', () => {
    const entity: Record<string, unknown> = {
      sprk_projectname: 'Protected Project',
      sprk_containerid: 'explicit-override-container',
      sprk_searchindexname: 'spaarke-file-index',
    };
    const defaults: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    };

    const applied = EntityCreationService.applyUserBuDefaults(entity, defaults);

    expect(applied).toEqual({ searchIndexNameSet: false });
    expect(entity['sprk_containerid']).toBe('explicit-override-container');
    expect(entity['sprk_searchindexname']).toBe('spaarke-file-index');
  });
});

// ---------------------------------------------------------------------------
// Error path — the cascade does not affect failure handling
// ---------------------------------------------------------------------------

describe('ProjectService.createProject — error handling unchanged by cascade', () => {
  it('returns success=false when the create is refused (cascade does not mask the error)', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const { service } = makeDataService();
    const bff = jest.fn(async (): Promise<Response> => {
      throw apiErrorFor(500, { title: 'Record not saved', status: 500, detail: 'Dataverse boom' });
    });

    const result = await new ProjectService(service, bff, BFF).createProject(EMPTY_FORM, {
      businessUnitId: 'bu-guid-1',
      containerId: 'bu-container-abc',
      searchIndexName: 'spaarke-knowledge-index-v2',
    });

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('Dataverse boom');
  });
});
