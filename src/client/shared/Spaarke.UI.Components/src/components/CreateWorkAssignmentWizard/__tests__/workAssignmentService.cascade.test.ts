/**
 * WorkAssignmentService — BU cascade integration tests (FR-WIZ-04 / INV-5)
 *
 * Scope: verify that `WorkAssignmentService.createWorkAssignment` populates BOTH
 *   - `sprk_containerid`
 *   - `sprk_searchindexname`
 * on the create payload, sourced from the current user's owning Business Unit
 * via `EntityCreationService.applyUserBuDefaults` + `resolveUserBuDefaults`,
 * with INV-5 preserved for explicit pre-seeded values.
 *
 * Notes
 *   - We mock `window.Xrm.Utility.getGlobalContext()` to provide the current user ID.
 *   - We mock `IDataService` to capture the entity payload passed to `createRecord`.
 *   - We mock global `fetch` (used by `_discoverNavProps`) to keep tests offline.
 *   - We treat ` _entityService` upload paths as no-ops (no files in these tests).
 *
 * @see spec.md FR-WIZ-04, FR-WIZ-08 (INV-5)
 * @see EntityCreationService.cascade.test.ts (lower-level cascade helper tests)
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { WorkAssignmentService } from '../workAssignmentService';
import type { ICreateWorkAssignmentFormState } from '../formTypes';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

const USER_GUID = '11111111-1111-1111-1111-111111111111';
const BU_GUID = '22222222-2222-2222-2222-222222222222';
const NEW_WA_GUID = '33333333-3333-3333-3333-333333333333';

/**
 * Build a mock IDataService whose retrieveRecord chains:
 *   systemuser(USER_GUID) → { _businessunitid_value: BU_GUID }
 *   businessunit(BU_GUID) → { sprk_containerid, sprk_searchindexname }
 */
function makeDataService(
  buFields: { sprk_containerid?: string | null; sprk_searchindexname?: string | null },
  opts?: { userHasBu?: boolean }
): IDataService & { _capturedPayloads: Record<string, Record<string, unknown>[]> } {
  const captured: Record<string, Record<string, unknown>[]> = {};

  const svc: IDataService & { _capturedPayloads: Record<string, Record<string, unknown>[]> } = {
    _capturedPayloads: captured,

    createRecord: jest.fn(async (entityName: string, data: Record<string, unknown>) => {
      captured[entityName] = captured[entityName] ?? [];
      captured[entityName].push(data);
      return NEW_WA_GUID;
    }),

    retrieveRecord: jest.fn(async (entityType: string, _id: string, _options?: string) => {
      if (entityType === 'systemuser') {
        return opts?.userHasBu === false ? {} : { _businessunitid_value: BU_GUID };
      }
      if (entityType === 'businessunit') {
        return buFields as Record<string, unknown>;
      }
      throw new Error(`Unexpected entityType: ${entityType}`);
    }),

    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };

  return svc;
}

/** Stub global Xrm so `_getCurrentUserId()` returns USER_GUID. */
function stubXrmUser(userId: string | null) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    Utility: {
      getGlobalContext: () => ({
        userSettings: { userId: userId },
      }),
    },
  };
}

function clearXrm() {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
}

/** Stub global fetch so nav-prop discovery returns an empty relationship set (offline). */
function stubFetchEmptyNavProps() {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (global as any).fetch = jest.fn(async () =>
    Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({ value: [] }),
    } as Response)
  );
}

function makeForm(overrides?: Partial<ICreateWorkAssignmentFormState>): ICreateWorkAssignmentFormState {
  return {
    name: 'WA-Test',
    description: '',
    priority: 100000001, // Normal
    responseDueDate: '',
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    recordType: '',
    recordId: '',
    recordName: '',
    ...overrides,
  } as ICreateWorkAssignmentFormState;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('WorkAssignmentService — FR-WIZ-04 BU cascade', () => {
  beforeEach(() => {
    stubFetchEmptyNavProps();
    stubXrmUser(USER_GUID);
  });

  afterEach(() => {
    clearXrm();
    jest.restoreAllMocks();
  });

  it('cascade: BU populates BOTH sprk_containerid and sprk_searchindexname when host did not pre-set container', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new WorkAssignmentService(
      dataService,
      jest.fn(),
      'https://bff.example/api',
      // No constructor _containerId — simulates host that could not resolve one.
      undefined
    );

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    expect(payload!['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('INV-5: host-provided containerId is preserved; BU only fills the searchindexname gap', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new WorkAssignmentService(
      dataService,
      jest.fn(),
      'https://bff.example/api',
      'host-explicit-container-xyz' // host already resolved a container (record-level / matter-level)
    );

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    // INV-5: host explicit value sacred
    expect(payload!['sprk_containerid']).toBe('host-explicit-container-xyz');
    // BU fills the gap on the field the host did not set
    expect(payload!['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('NULL BU index (Spaarke Dev 1 / Test 1 scenario): containerId still cascades, searchindexname left unset', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: null, // BU exists but index not yet configured (Phase A.5)
    });

    const service = new WorkAssignmentService(dataService, jest.fn(), 'https://bff.example/api', undefined);

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    // BFF tenant-default chain takes over server-side; payload field left unset
    expect('sprk_searchindexname' in payload!).toBe(false);
  });

  it('graceful degradation: when current user ID is unavailable, BU cascade is skipped and createRecord still succeeds', async () => {
    stubXrmUser(null); // simulate no Xrm context

    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    const service = new WorkAssignmentService(
      dataService,
      jest.fn(),
      'https://bff.example/api',
      'host-explicit-container-xyz'
    );

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    // Host container retained; BU cascade was a no-op (no userId), so searchindexname unset.
    expect(payload!['sprk_containerid']).toBe('host-explicit-container-xyz');
    expect('sprk_searchindexname' in payload!).toBe(false);
    // BU lookup must not have been called.
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.any(String), expect.any(String));

    warnSpy.mockRestore();
  });

  it('graceful degradation: when BU resolve throws, createRecord still succeeds with host container only', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });
    // Force the BU lookup to throw on the second retrieveRecord call.
    (dataService.retrieveRecord as jest.Mock).mockImplementation(async (entityType: string) => {
      if (entityType === 'systemuser') return { _businessunitid_value: BU_GUID };
      if (entityType === 'businessunit') throw new Error('Network down');
      throw new Error('unexpected');
    });
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    const service = new WorkAssignmentService(
      dataService,
      jest.fn(),
      'https://bff.example/api',
      'host-explicit-container-xyz'
    );

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('host-explicit-container-xyz');
    expect('sprk_searchindexname' in payload!).toBe(false);

    warnSpy.mockRestore();
  });

  it('user has no BU: cascade is a no-op; host container retained; createRecord succeeds', async () => {
    const dataService = makeDataService(
      { sprk_containerid: 'unreachable', sprk_searchindexname: 'unreachable-index' },
      { userHasBu: false }
    );

    const service = new WorkAssignmentService(
      dataService,
      jest.fn(),
      'https://bff.example/api',
      'host-explicit-container-xyz'
    );

    const result = await service.createWorkAssignment(makeForm(), [], []);
    expect(result.status).toBe('success');

    const payload = dataService._capturedPayloads['sprk_workassignment']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('host-explicit-container-xyz');
    expect('sprk_searchindexname' in payload!).toBe(false);
  });
});
