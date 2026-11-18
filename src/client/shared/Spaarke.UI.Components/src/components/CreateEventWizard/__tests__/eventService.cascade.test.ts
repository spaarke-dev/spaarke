/**
 * EventService — the BU cascade is gone (field-mapping-server-write-path-r1 task 023, FR-16)
 *
 * Converted from the FR-WIZ-05 / INV-5 cascade tests, the way task 076 converted the container half and task 021
 * converted the matter cascade test. The Event wizard used to write `sprk_searchindexname` (and bind
 * `sprk_ai_search_index`) from the current user's owning Business Unit. Both index columns are the server's for
 * `sprk_event` (`RecordTableRule.SearchIndexed`, FR-38/FR-40): a caller's value is dropped at step 2, so a client
 * default is dead weight and a payload key the user did not enter. The same scenarios (full BU defaults, a null index
 * name, a null container, no user, a failing BU read, no BU, the injected user id) now assert that NOTHING the cascade
 * wrote ever reaches the create body, and that the BU is never read.
 *
 * Notes
 *   - We stub `window.Xrm.Utility.getGlobalContext().userSettings.userId` so the old probe WOULD have found a user.
 *   - We mock `IDataService` to capture the entity payload the BFF fake passes to `createRecord`.
 *   - We mock global `fetch` (used by nav-prop discovery) to keep tests offline.
 *
 * @see spec.md FR-16, FR-38
 * @see CreateMatterWizard/__tests__/matterService.cascade.test.ts (the same conversion for the matter)
 */

// UAC-r2 task 147 r1: child creates go through the BFF; the fake answers its routes through the mock data service.
import { bffChildWriteFetch, FAKE_BFF_BASE_URL } from '../../../__mocks__/bffChildWriteFake';
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
 * Nothing in the wizard reads either any more; the chain is here so a regression that read them would find values.
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
 * Stub global Xrm so a user id probe WOULD return the supplied userId.
 * `null` simulates "no Xrm context reachable".
 */
function stubXrmUser(userId: string | null) {
  (window as unknown as { Xrm: unknown }).Xrm = {
    // `WebApi` is required by the shared `getXrm()` walker (task 081 / C-8).
    WebApi: {},
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
    assignedToId: '',
    assignedToName: '',
    ...overrides,
  };
}

/** The cascade's columns, in every shape it wrote them: the name, the lookup bind, the container. */
function expectNoCascadeColumns(payload: Record<string, unknown>) {
  const keys = Object.keys(payload);
  expect(keys.filter(k => /container|searchindex|search_index|aisearchindex/i.test(k))).toEqual([]);
}

function expectBuNeverRead(dataService: IDataService) {
  expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('systemuser', expect.anything(), expect.anything());
  expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.anything(), expect.anything());
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('EventService — the BU cascade no longer writes the search index (task 023, FR-16)', () => {
  beforeEach(() => {
    stubFetchEmptyNavProps();
    stubXrmUser(USER_GUID);
  });

  afterEach(() => {
    clearXrm();
    jest.restoreAllMocks();
  });

  // Converted: was "BU populates sprk_searchindexname only -- no sprk_containerid -- on a clean payload".
  it('a BU with a container and an index name: neither reaches the payload (the server owns both)', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    expect(result.eventId).toBe(NEW_EVENT_GUID);

    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expect(JSON.stringify(payload)).not.toContain('spaarke-knowledge-index-v2');
    expect(JSON.stringify(payload)).not.toContain('bu-container-abc');
    expectBuNeverRead(dataService);
  });

  // Converted: was "cascade with regarding record: BU defaults applied alongside parent-record nav-prop binding".
  it('with a regarding record: the parent binding is written, the BU defaults are not', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(
      makeForm({ regardingRecordId: 'matter-guid-1', regardingRecordName: 'Some Matter' }),
      'sprk_matter'
    );

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    // The regarding key is still the user's/route's; the cascade columns are not.
    expect(payload!['sprk_regardingrecordid']).toBe('matter-guid-1');
    expectNoCascadeColumns(payload!);
    expectBuNeverRead(dataService);
  });

  // Converted: was "NULL BU sprk_searchindexname ... neither cascade field is written".
  it('NULL BU sprk_searchindexname (Spaarke Dev 1 / Test 1 scenario): neither cascade field is written', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: null,
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expectBuNeverRead(dataService);
  });

  // Converted: was "NULL BU containerid: searchindexname still cascades, containerid left unset".
  it('NULL BU containerid: the index name no longer cascades either', async () => {
    const dataService = makeDataService({
      sprk_containerid: null,
      sprk_searchindexname: 'spaarke-file-index',
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expect(JSON.stringify(payload)).not.toContain('spaarke-file-index');
  });

  it('no current user (no Xrm context): createRecord still succeeds with no cascade field', async () => {
    stubXrmUser(null); // simulate no Xrm context

    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expectBuNeverRead(dataService);
  });

  it('a BU read that WOULD throw is never attempted: createRecord succeeds and no warning is raised about it', async () => {
    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });
    // Force the BU lookup to throw; with no cascade it is never reached.
    (dataService.retrieveRecord as jest.Mock).mockImplementation(async (entityType: string) => {
      if (entityType === 'systemuser') return { _businessunitid_value: BU_GUID };
      if (entityType === 'businessunit') throw new Error('Network down');
      throw new Error('unexpected');
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([]);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
  });

  it('user has no BU: createRecord succeeds with no cascade field', async () => {
    const dataService = makeDataService(
      { sprk_containerid: 'unreachable', sprk_searchindexname: 'unreachable-index' },
      { userHasBu: false }
    );

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm());

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
  });

  // Converted: was "test-injection seam: explicit getCurrentUserId override is honored".
  it('an injected getCurrentUserId is ignored: no BU read, no cascade field', async () => {
    clearXrm();

    const dataService = makeDataService({
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
    });

    const service = new EventService(dataService, bffChildWriteFetch(dataService), FAKE_BFF_BASE_URL);
    const result = await service.createEvent(makeForm(), undefined, {
      getCurrentUserId: () => USER_GUID,
    });

    expect(result.success).toBe(true);
    const payload = dataService._capturedPayloads['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expectNoCascadeColumns(payload!);
    expectBuNeverRead(dataService);
  });
});
