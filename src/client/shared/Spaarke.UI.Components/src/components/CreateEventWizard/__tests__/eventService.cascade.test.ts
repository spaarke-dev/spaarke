/**
 * EventService — BU cascade integration tests (FR-WIZ-05 / INV-5)
 *
 * Scope: verify that `EventService.createEvent` populates BOTH
 *   - `sprk_containerid`
 *   - `sprk_searchindexname`
 * on the create payload, sourced from the current user's owning Business Unit
 * via `EntityCreationService.applyUserBuDefaults` + `resolveUserBuDefaults`,
 * with INV-5 preserved for explicit pre-seeded values.
 *
 * Notes
 *   - We mock `window.Xrm.Utility.getGlobalContext().userSettings.userId` to provide
 *     the current user ID (matches the `_tryGetCurrentUserId` probe in eventService.ts).
 *   - We mock `IDataService` to capture the entity payload passed to `createRecord`.
 *   - We mock global `fetch` (used by `_discoverNavProps`) to keep tests offline.
 *
 * @see spec.md FR-WIZ-05, FR-WIZ-08 (INV-5)
 * @see EntityCreationService.cascade.test.ts (lower-level cascade helper tests)
 * @see workAssignmentService.cascade.test.ts (sibling FR-WIZ-04 reference shape)
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { EventService } from '../eventService';
import type { ICreateEventFormState } from '../formTypes';

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

const USER_GUID = '11111111-1111-1111-1111-111111111111';
const BU_GUID = '22222222-2222-2222-2222-222222222222';
const NEW_EVENT_GUID = '33333333-3333-3333-3333-333333333333';

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
      return NEW_EVENT_GUID;
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

/**
 * Stub global Xrm so `_tryGetCurrentUserId()` returns the supplied userId.
 * `null` simulates "no Xrm context reachable" (BU cascade skipped).
 */
function stubXrmUser(userId: string | null) {
  (window as unknown as { Xrm: unknown }).Xrm = {
    Utility: {
      getGlobalContext: () => ({
        userSettings: { userId: userId },
      }),
    },
  };
}

function clearXrm() {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

/** Stub global fetch so nav-prop discovery returns an empty relationship set (offline). */
function stubFetchEmptyNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () =>
    Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({ value: [] }),
    } as Response)
  );
}

function makeForm(overrides?: Partial<ICreateEventFormState>): ICreateEventFormState {
  return {
    eventName: 'Test Event',
    eventTypeId: '',
    eventTypeName: '',
    dueDate: '',
    priority: 100000001, // Normal
    description: '',
    regardingRecordId: '',
    regardingRecordName: '',
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('EventService — FR-WIZ-05 BU cascade', () => {
  beforeEach(() => {
    stubFetchEmptyNavProps();
    stubXrmUser(USER_GUID);
  });

  afterEach(() => {
    clearXrm();
    jest.restoreAllMocks();
  });

  it('cascade: BU populates BOTH sprk_containerid and sprk_searchindexname on a clean payload', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    expect(result.eventId).toBe(NEW_EVENT_GUID);

    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    expect(payload!['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('cascade with regarding record: BU defaults applied alongside parent-record nav-prop binding', async () => {
    // FR-WIZ-05 cascade must work whether or not the event is linked to a parent
    // matter/project. This test exercises the regarding-record path to confirm
    // the cascade block is not gated behind any other branch.
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService);
    const result = await service.createEvent(
      makeForm({ regardingRecordId: 'matter-guid-1', regardingRecordName: 'Some Matter' }),
      'sprk_matter'
    );

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    expect(payload!['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });

  it('NULL BU sprk_searchindexname (Spaarke Dev 1 / Test 1 scenario): containerid still cascades, searchindexname left unset', async () => {
    // Spaarke Dev 1 / Test 1 scenario per Phase A.5: BU exists but index name not configured.
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: null,
    });

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    // BFF tenant-default chain takes over server-side; payload field left unset
    expect('sprk_searchindexname' in payload!).toBe(false);
  });

  it('NULL BU containerid: searchindexname still cascades, containerid left unset', async () => {
    const dataService = makeDataService({
      sprk_containerid: null,
      sprk_searchindexname: 'spaarke-file-index',
    });

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect('sprk_containerid' in payload!).toBe(false);
    expect(payload!['sprk_searchindexname']).toBe('spaarke-file-index');
  });

  it('graceful degradation: when current user ID is unavailable, BU cascade is skipped and createRecord still succeeds', async () => {
    stubXrmUser(null); // simulate no Xrm context

    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });
    const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect('sprk_containerid' in payload!).toBe(false);
    expect('sprk_searchindexname' in payload!).toBe(false);
    // BU lookup must not have been called.
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.any(String), expect.any(String));

    warnSpy.mockRestore();
  });

  it('graceful degradation: when BU resolve throws, createRecord still succeeds with neither cascade field set', async () => {
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

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect('sprk_containerid' in payload!).toBe(false);
    expect('sprk_searchindexname' in payload!).toBe(false);

    warnSpy.mockRestore();
  });

  it('user has no BU: cascade is a no-op; createRecord succeeds with neither cascade field set', async () => {
    const dataService = makeDataService(
      { sprk_containerid: 'unreachable', sprk_searchindexname: 'unreachable-index' },
      { userHasBu: false }
    );

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect('sprk_containerid' in payload!).toBe(false);
    expect('sprk_searchindexname' in payload!).toBe(false);
  });

  it('test-injection seam: explicit getCurrentUserId override is honored', async () => {
    // Even without a global Xrm stub, the options.getCurrentUserId hook lets tests
    // (or specialized hosts) inject a user id directly. This is the cleanest
    // path for higher-level Code Page integration tests that don't want to
    // stub window.Xrm.
    clearXrm();

    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService);
    const result = await service.createEvent(makeForm(), undefined, {
      getCurrentUserId: () => USER_GUID,
    });

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_containerid']).toBe('bu-container-abc');
    expect(payload!['sprk_searchindexname']).toBe('spaarke-knowledge-index-v2');
  });
});
