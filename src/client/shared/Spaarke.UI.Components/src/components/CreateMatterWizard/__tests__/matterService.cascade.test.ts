/**
 * MatterService — `sprk_searchindexname` BU cascade tests
 *
 * Scope: the FR-WIZ-01 / FR-WIZ-08 extension to `CreateMatterWizard.matterService`
 * landed by spaarke-multi-container-multi-index-r1.
 *
 * Contract (refactored 2026-06-08 to match the CreateProjectWizard dependency-injection
 * pattern): `matterService.createMatter` accepts `cascadeDefaults?: IUserBuCascadeDefaults`
 * as a 4th parameter. The caller (typically `CreateMatterWizard.tsx` via the
 * `resolveUserBuDefaults` prop) is responsible for resolving the defaults using the
 * host's `Xrm.Utility.getGlobalContext().userSettings.userId` API. The previous inline
 * implementation called the non-existent `Xrm.Utility.getUserId()` and silently
 * skipped the cascade in the Code Page iframe runtime — fixed by moving the lookup
 * upstream and passing the resolved values in.
 *
 * Covered:
 *   - Cascade: `sprk_searchindexname` is added to the `createRecord` payload from the
 *     caller-provided `cascadeDefaults.searchIndexName` (FR-WIZ-01).
 *   - INV-5 preservation: the helper guards explicit values — covered comprehensively
 *     by `EntityCreationService.cascade.test.ts`; this file verifies the matterService
 *     correctly invokes the helper.
 *   - Regression: existing `sprk_containerid` cascade behavior unchanged.
 *   - Graceful degradation: when the caller omits `cascadeDefaults`, matter creation
 *     still succeeds and `sprk_searchindexname` is simply left unset.
 *   - NULL BU value: when defaults.searchIndexName is undefined, the field is unset.
 */

import { MatterService } from '../matterService';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { IUserBuCascadeDefaults } from '../../../services/EntityCreationService';

const MOCK_BU_ID = 'bu-guid-xyz';
const MOCK_CONTAINER_ID = 'spe-container-from-host';
const MOCK_BU_SEARCH_INDEX = 'spaarke-knowledge-index-v2';

function makeForm(overrides?: Partial<ICreateMatterFormState>): ICreateMatterFormState {
  return {
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    matterName: 'Test Matter',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    summary: '',
    ...overrides,
  };
}

function makeDataService(): {
  dataService: IDataService;
  createCalls: Array<{ entity: string; payload: Record<string, unknown> }>;
} {
  const createCalls: Array<{ entity: string; payload: Record<string, unknown> }> = [];

  const dataService: IDataService = {
    createRecord: jest.fn().mockImplementation(async (entity: string, payload: Record<string, unknown>) => {
      createCalls.push({ entity, payload });
      return 'created-record-guid';
    }),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };

  return { dataService, createCalls };
}

const noopAuthFetch = jest.fn().mockResolvedValue({
  ok: true,
  status: 200,
  statusText: 'OK',
  json: async () => ({}),
  text: async () => '',
} as unknown as Response);
const noopBffBase = 'https://example.test';

describe('MatterService — sprk_searchindexname BU cascade (FR-WIZ-01)', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('adds sprk_searchindexname to the createRecord payload from caller-provided cascadeDefaults (FR-WIZ-01)', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    expect(createCalls).toHaveLength(1);
    expect(createCalls[0].entity).toBe('sprk_matter');
    expect(createCalls[0].payload['sprk_searchindexname']).toBe(MOCK_BU_SEARCH_INDEX);
    // Regression: existing containerId behavior unchanged (host-injected wins per INV-5)
    expect(createCalls[0].payload['sprk_containerid']).toBe(MOCK_CONTAINER_ID);
  });

  it('leaves sprk_searchindexname unset when cascadeDefaults.searchIndexName is undefined (Phase A.5 ordering scenario)', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: undefined,
    };

    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    expect('sprk_searchindexname' in createCalls[0].payload).toBe(false);
    // Container ID cascade still happens (host-injected)
    expect(createCalls[0].payload['sprk_containerid']).toBe(MOCK_CONTAINER_ID);
  });

  it('leaves sprk_searchindexname unset when cascadeDefaults is omitted entirely (graceful degradation)', async () => {
    const { dataService, createCalls } = makeDataService();

    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {});

    // Matter creation still succeeds — cascade is best-effort
    expect(result.status).toBe('success');
    expect('sprk_searchindexname' in createCalls[0].payload).toBe(false);
    expect(createCalls[0].payload['sprk_containerid']).toBe(MOCK_CONTAINER_ID);
    // BU lookup must NOT have happened (caller didn't pass defaults)
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.anything(), expect.anything());
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('systemuser', expect.anything(), expect.anything());
  });

  it('preserves existing sprk_containerid cascade behavior when no host container is provided but cascadeDefaults provides one', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    // No containerId passed to MatterService constructor — falls through to cascade.
    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, undefined);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    // sprk_containerid should be set from cascadeDefaults (no host-injection to win INV-5)
    expect(createCalls[0].payload['sprk_containerid']).toBe(MOCK_CONTAINER_ID);
    expect(createCalls[0].payload['sprk_searchindexname']).toBe(MOCK_BU_SEARCH_INDEX);
  });

  it('does not bypass INV-5: if host container is set, cascadeDefaults.containerId does NOT overwrite', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: 'cascade-container-should-be-ignored',
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    // Host injects a container — INV-5 says this wins over cascade
    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    expect(createCalls[0].payload['sprk_containerid']).toBe(MOCK_CONTAINER_ID);
    // searchIndexName has no host injection — cascade wins
    expect(createCalls[0].payload['sprk_searchindexname']).toBe(MOCK_BU_SEARCH_INDEX);
  });
});
