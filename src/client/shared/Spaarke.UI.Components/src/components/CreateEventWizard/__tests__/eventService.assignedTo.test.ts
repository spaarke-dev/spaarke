/**
 * EventService — "Assigned To" (contact) binding tests
 * (spaarkeai-assistant-enhancements-r1 task 014 / FR-A4)
 *
 * Scope: `EventService.createEvent` binds `formValues.assignedToId` onto the
 * `sprk_event.sprk_assignedto` (contact-target) lookup via dynamic nav-prop
 * discovery — never a hardcoded SchemaName. Optional: omitting an assignee
 * must not affect event creation (P6 grounding-optional companion case).
 *
 * @see eventService.ts — the "Assigned To (contact)" binding block
 * @see eventService.resolver.test.ts — sibling ADR-024 regarding-lookup coverage
 */
import type { IDataService } from '../../../types/serviceInterfaces';
import { EventService } from '../eventService';
import type { ICreateEventFormState } from '../formTypes';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';

const NEW_EVENT_GUID = '33333333-3333-3333-3333-333333333333';
const CONTACT_ID_RAW = '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}';
const CONTACT_ID_CLEAN = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

function makeDataService(): IDataService & { _captured: Record<string, Record<string, unknown>[]> } {
  const captured: Record<string, Record<string, unknown>[]> = {};
  return {
    _captured: captured,
    createRecord: jest.fn(async (entityName: string, data: Record<string, unknown>) => {
      captured[entityName] = captured[entityName] ?? [];
      captured[entityName].push(data);
      return NEW_EVENT_GUID;
    }),
    retrieveRecord: jest.fn(async () => ({})),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** Nav-prop metadata for sprk_event including the contact-targeted assignedto lookup. */
function stubFetchNavPropsWithAssignedTo() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () =>
    Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({
        value: [
          {
            ReferencingAttribute: 'sprk_assignedto',
            ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
            ReferencedEntity: 'contact',
          },
        ],
      }),
    } as Response)
  );
}

/** Nav-prop metadata WITHOUT an assignedto->contact entry (degraded schema). */
function stubFetchNavPropsWithoutAssignedTo() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () =>
    Promise.resolve({ ok: true, status: 200, json: async () => ({ value: [] }) } as Response)
  );
}

function makeForm(overrides?: Partial<ICreateEventFormState>): ICreateEventFormState {
  return {
    eventName: 'Follow up call',
    eventTypeId: '',
    eventTypeName: '',
    dueDate: '',
    priority: 100000001,
    description: '',
    regardingRecordId: '',
    regardingRecordName: '',
    assignedToId: '',
    assignedToName: '',
    ...overrides,
  };
}

const NO_CASCADE = { getCurrentUserId: () => null };

function eventPayload(ds: ReturnType<typeof makeDataService>): Record<string, unknown> {
  const p = ds._captured['sprk_event']?.[0];
  expect(p).toBeDefined();
  return p!;
}

describe('EventService — Assigned To (contact) binding', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    _resetNavPropCacheForTests('sprk_event');
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
    jest.restoreAllMocks();
  });

  it('binds assignedToId onto the contact-targeted sprk_assignedto lookup', async () => {
    stubFetchNavPropsWithAssignedTo();
    const ds = makeDataService();
    const service = new EventService(ds);

    const result = await service.createEvent(
      makeForm({ assignedToId: CONTACT_ID_RAW, assignedToName: 'Jane Attorney' }),
      undefined,
      NO_CASCADE
    );

    expect(result.success).toBe(true);
    const payload = eventPayload(ds);
    expect(payload['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${CONTACT_ID_CLEAN})`);
  });

  it('P6 grounding-optional companion: creates the event successfully with NO assignee', async () => {
    stubFetchNavPropsWithAssignedTo();
    const ds = makeDataService();
    const service = new EventService(ds);

    const result = await service.createEvent(makeForm(), undefined, NO_CASCADE);

    expect(result.success).toBe(true);
    const payload = eventPayload(ds);
    expect('sprk_AssignedTo@odata.bind' in payload).toBe(false);
    expect(result.warnings).toEqual([]);
  });

  it('degrades gracefully (warns, never throws) when the assignedto nav-prop is not discoverable', async () => {
    stubFetchNavPropsWithoutAssignedTo();
    const ds = makeDataService();
    const service = new EventService(ds);

    const result = await service.createEvent(
      makeForm({ assignedToId: CONTACT_ID_RAW, assignedToName: 'Jane Attorney' }),
      undefined,
      NO_CASCADE
    );

    expect(result.success).toBe(true);
    expect(result.warnings.length).toBeGreaterThan(0);
  });
});
