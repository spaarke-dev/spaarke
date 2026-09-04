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
 *   - Security (task 076, 2026-09-03): `sprk_containerid` is NEVER present on the create
 *     payload — not from the host-injected container, not from `cascadeDefaults.containerId`.
 *     Both writes were deleted; the server derives the container from the matter itself.
 *     The former "`sprk_containerid` cascade unchanged" regression coverage is now this
 *     negative assertion, which is the guarantee actually worth holding.
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
    // Task 076: the host-injected container is no longer stamped on the record. The old
    // `toBe(MOCK_CONTAINER_ID)` regression assertion was CONVERTED to this negative one.
    expect(createCalls[0].payload).not.toHaveProperty('sprk_containerid');
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
    // Task 076: no container write survives on this path either.
    expect(createCalls[0].payload).not.toHaveProperty('sprk_containerid');
  });

  it('leaves sprk_searchindexname unset when cascadeDefaults is omitted entirely (graceful degradation)', async () => {
    const { dataService, createCalls } = makeDataService();

    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {});

    // Matter creation still succeeds — cascade is best-effort
    expect(result.status).toBe('success');
    expect('sprk_searchindexname' in createCalls[0].payload).toBe(false);
    expect(createCalls[0].payload).not.toHaveProperty('sprk_containerid');
    // BU lookup must NOT have happened (caller didn't pass defaults)
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.anything(), expect.anything());
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('systemuser', expect.anything(), expect.anything());
  });

  // Converted by task 076: this test used to prove that `cascadeDefaults.containerId` filled
  // `sprk_containerid` when the host injected none. That cascade (`applyDefaultContainerId`) is
  // deleted, so the surviving — and stronger — guarantee is that the BU container is ignored.
  it('ignores cascadeDefaults.containerId entirely: no sprk_containerid even when the host injected none', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    // No containerId passed to MatterService constructor — the only container in play is the
    // caller-supplied BU cascade value, which must not be written.
    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, undefined);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    expect(createCalls[0].payload).not.toHaveProperty('sprk_containerid');
    // The search-index routing hint still cascades — only the storage location was removed.
    expect(createCalls[0].payload['sprk_searchindexname']).toBe(MOCK_BU_SEARCH_INDEX);
  });

  // Converted by task 076: was "host container wins over cascadeDefaults.containerId (INV-5)".
  // With BOTH container writes deleted, neither source may reach the payload — this covers the
  // second deleted write site (the constructor-injected container) alongside the cascade one.
  it('writes no sprk_containerid from EITHER source when the host injects one AND cascadeDefaults supplies one', async () => {
    const { dataService, createCalls } = makeDataService();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: 'cascade-container-should-be-ignored',
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    const service = new MatterService(dataService, noopAuthFetch, noopBffBase, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    expect(createCalls[0].payload).not.toHaveProperty('sprk_containerid');
    // searchIndexName has no host injection — cascade wins
    expect(createCalls[0].payload['sprk_searchindexname']).toBe(MOCK_BU_SEARCH_INDEX);
  });
});
