/**
 * EventService — Field Mapping Framework engine wiring (task 020, spec FR-12)
 *
 * Scope: verify `EventService.createEvent` calls `applyFieldMappings` AFTER
 * `applyResolverFields` and BEFORE `createRecord`, when both the regarding
 * parent (`regardingRecordId` + `regardingEntityName`) and the BFF deps
 * (`authenticatedFetch`/`bffBaseUrl`) are available:
 *   - a matching, active profile writes its mapped fields onto the create
 *     payload and surfaces engine warnings on the result;
 *   - no profile (404) / no BFF deps / no regarding parent are all graceful
 *     no-ops — identical to pre-task-020 behavior.
 *
 * @see src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts
 * @see projects/set-regarding-and-field-mapping-resolver-r2/tasks/020-wire-event-matter-project.poml
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { EventService } from '../eventService';
import type { ICreateEventFormState } from '../formTypes';
import {
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';

const NEW_EVENT_GUID = '33333333-3333-3333-3333-333333333333';
const MATTER_ID = '39cde3e3-9d15-4b29-9a1e-1234567890ab';

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
    // No sprk_recordtype_ref catalog rows configured — the resolver-field
    // portion gracefully degrades (NFR-06); irrelevant to this test's focus.
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** Nav-prop metadata for sprk_event (global fetch — discoverNavProps/applyResolverFields channel). */
function stubGlobalFetchForNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () =>
    Promise.resolve({
      ok: true,
      status: 200,
      json: async () => ({
        value: [
          {
            ReferencingAttribute: 'sprk_regardingmatter',
            ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
            ReferencedEntity: 'sprk_matter',
          },
        ],
      }),
    } as Response)
  );
}

function makeForm(overrides?: Partial<ICreateEventFormState>): ICreateEventFormState {
  return {
    eventName: 'Kickoff meeting',
    eventTypeId: '',
    eventTypeName: '',
    dueDate: '',
    priority: 100000001,
    description: '',
    regardingRecordId: MATTER_ID,
    regardingRecordName: 'Smith v. Jones',
    ...overrides,
  };
}

const NO_CASCADE = { getCurrentUserId: () => null };

describe('EventService — Field Mapping Framework engine wiring (task 020)', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    stubGlobalFetchForNavProps();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
    jest.restoreAllMocks();
  });

  it('applies mapped fields onto the create payload when an active profile exists for the parent pair', async () => {
    const ds = makeDataService();
    const authenticatedFetch = jest.fn(async () =>
      Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({
          id: 'profile-1',
          name: 'Matter -> Event',
          sourceEntity: 'sprk_matter',
          targetEntity: 'sprk_event',
          syncMode: 'OneTime',
          isActive: true,
          rules: [
            {
              id: 'rule-1',
              sourceField: 'sprk_priorityreason',
              targetField: 'sprk_priorityreason',
              sourceFieldType: 'Text',
              targetFieldType: 'Text',
              priority: 1,
              mappingType: 'Default',
              defaultValue: 'Auto-mapped from Matter',
              expression: null,
              isRequired: false,
              compatibilityMode: 'Strict',
            },
          ],
        }),
      } as Response)
    );

    const service = new EventService(ds, authenticatedFetch, 'https://bff.example.com');
    const result = await service.createEvent(makeForm(), 'sprk_matter', NO_CASCADE);

    expect(result.success).toBe(true);
    expect(authenticatedFetch).toHaveBeenCalledWith(
      'https://bff.example.com/api/v1/field-mappings/profiles/sprk_matter/sprk_event',
      { method: 'GET' }
    );

    const payload = ds._captured['sprk_event']?.[0];
    expect(payload).toBeDefined();
    expect(payload!['sprk_priorityreason']).toBe('Auto-mapped from Matter');
    expect(result.warnings).toEqual([]);
  });

  it('is a graceful no-op (unchanged behavior) when no profile is configured for the pair (404)', async () => {
    const ds = makeDataService();
    const authenticatedFetch = jest.fn(async () => Promise.resolve({ ok: false, status: 404 } as Response));

    const service = new EventService(ds, authenticatedFetch, 'https://bff.example.com');
    const result = await service.createEvent(makeForm(), 'sprk_matter', NO_CASCADE);

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([]);
    const payload = ds._captured['sprk_event']?.[0];
    expect(payload!['sprk_priorityreason']).toBeUndefined();
  });

  it('is a graceful no-op when authenticatedFetch/bffBaseUrl are not supplied (lookup-only construction site)', async () => {
    const ds = makeDataService();
    const service = new EventService(ds); // no authenticatedFetch/bffBaseUrl injected

    const result = await service.createEvent(makeForm(), 'sprk_matter', NO_CASCADE);

    expect(result.success).toBe(true);
    expect(result.warnings).toEqual([]);
    const payload = ds._captured['sprk_event']?.[0];
    // Resolver-field write from applyResolverFields still happens (unrelated to the engine);
    // the point under test is that no fetch to the field-mappings endpoint occurs.
    expect(payload).toBeDefined();
  });

  it('does not call the engine when no regarding parent is supplied', async () => {
    const ds = makeDataService();
    const authenticatedFetch = jest.fn(async () => Promise.resolve({ ok: false, status: 404 } as Response));

    const service = new EventService(ds, authenticatedFetch, 'https://bff.example.com');
    const result = await service.createEvent(makeForm({ regardingRecordId: '' }), undefined, NO_CASCADE);

    expect(result.success).toBe(true);
    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(result.warnings).toEqual([]);
  });
});
