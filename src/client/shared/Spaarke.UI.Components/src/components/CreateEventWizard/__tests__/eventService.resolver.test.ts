/**
 * EventService — ADR-024 Polymorphic Resolver migration tests (task 014)
 *
 * Scope: verify that `EventService.createEvent`, when given a regarding parent,
 * writes the ADR-024 dual-field payload via the shared `applyResolverFields`
 * primitive:
 *   - exactly ONE entity-specific `@odata.bind` lookup (mutual exclusion), and
 *   - ALL 5 denormalized resolver fields
 *       (`sprk_regardingrecordtype` → sprk_recordtype_ref,
 *        `sprk_regardingrecordid`, `sprk_regardingrecordname`,
 *        `sprk_regardingrecordurl`, `sprk_regardingrecordnumber`).
 *
 * These assert on the REAL create payload captured from the mocked
 * `IDataService.createRecord` — no `Mock<HttpMessageHandler>`, no DI-registration
 * assertions, no ctor null-checks (ADR-038).
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md (5-field write, amended by #549)
 * @see src/client/shared/.../services/PolymorphicResolverService.ts (applyResolverFields)
 * @see eventService.cascade.test.ts (sibling FR-WIZ-05 BU-cascade coverage)
 */

import type { IDataService } from '../../../types/serviceInterfaces';
import { EventService } from '../eventService';
import type { ICreateEventFormState } from '../formTypes';
import {
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const NEW_EVENT_GUID = '33333333-3333-3333-3333-333333333333';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
// Braced/upper GUID from the picker — applyResolverFields must normalize it.
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const PROJECT_ID_CLEAN = '55555555-6666-7777-8888-999999999999';

/**
 * sprk_recordtype_ref catalog + target-record data, keyed by parent logical
 * name. Only `sprk_matter` is populated; `sprk_project` is intentionally
 * ABSENT to exercise the NFR-06 graceful-blank degradation path.
 */
const CATALOG: Record<
  string,
  {
    recordTypeId: string;
    recordTypeName: string;
    numberField: string;
    displayNameField: string;
    target: Record<string, unknown>;
  }
> = {
  sprk_matter: {
    recordTypeId: RT_MATTER_GUID,
    recordTypeName: 'Matter',
    numberField: 'sprk_matternumber',
    displayNameField: 'sprk_mattername',
    target: { sprk_matternumber: 'MAT-2026-0042', sprk_mattername: 'Smith v. Jones' },
  },
};

/**
 * Build a mock IDataService that:
 *   - captures each create payload, and
 *   - answers the sprk_recordtype_ref catalog reads + target-record read that
 *     `applyResolverFields` performs, driven by CATALOG.
 */
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

    retrieveMultipleRecords: jest.fn(async (entityName: string, options?: string) => {
      const query = options ?? '';

      if (entityName === 'sprk_recordtype_ref') {
        const logical = /sprk_recordlogicalname eq '([^']+)'/.exec(query)?.[1];
        const entry = logical ? CATALOG[logical] : undefined;
        if (!entry) return { entities: [] };
        if (query.includes('sprk_recordtype_refid')) {
          return {
            entities: [{ sprk_recordtype_refid: entry.recordTypeId, sprk_recorddisplayname: entry.recordTypeName }],
          };
        }
        if (query.includes('sprk_regardingrecordnumberfield')) {
          return { entities: [{ sprk_regardingrecordnumberfield: entry.numberField }] };
        }
        if (query.includes('sprk_recorddisplaynamefield')) {
          return { entities: [{ sprk_recorddisplaynamefield: entry.displayNameField }] };
        }
        return { entities: [] };
      }

      // Target-record read (entityName === parent logical name).
      const entry = CATALOG[entityName];
      return entry ? { entities: [entry.target] } : { entities: [] };
    }),

    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** Rich nav-prop metadata for sprk_event (entity-specific lookups + recordtype ref). */
function stubFetchEventNavProps() {
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
          {
            ReferencingAttribute: 'sprk_regardingproject',
            ReferencingEntityNavigationPropertyName: 'sprk_RegardingProject',
            ReferencedEntity: 'sprk_project',
          },
          {
            ReferencingAttribute: 'sprk_regardingrecordtype',
            ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
            ReferencedEntity: 'sprk_recordtype_ref',
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
    regardingRecordId: '',
    regardingRecordName: '',
    ...overrides,
  };
}

// Skip the BU cascade so the captured payload isolates the resolver behavior.
const NO_CASCADE = { getCurrentUserId: () => null };

function eventPayload(ds: ReturnType<typeof makeDataService>): Record<string, unknown> {
  const p = ds._captured['sprk_event']?.[0];
  expect(p).toBeDefined();
  return p!;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('EventService — ADR-024 resolver migration', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    stubFetchEventNavProps();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
    jest.restoreAllMocks();
  });

  it('writes the entity-specific lookup AND all 5 resolver fields for a Matter parent', async () => {
    const ds = makeDataService();
    const service = new EventService(ds);

    const result = await service.createEvent(
      makeForm({ regardingRecordId: MATTER_ID_RAW, regardingRecordName: 'picker-provided fallback' }),
      'sprk_matter',
      NO_CASCADE
    );

    expect(result.success).toBe(true);
    const payload = eventPayload(ds);

    // Entity-specific lookup (normalized GUID, correct entity set).
    expect(payload['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);

    // 5 resolver fields:
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBe(`/sprk_recordtype_refs(${RT_MATTER_GUID})`);
    expect(payload['sprk_regardingrecordid']).toBe(MATTER_ID_CLEAN);
    // Display name resolved from the target record (SRFR-052), NOT the picker fallback.
    expect(payload['sprk_regardingrecordname']).toBe('Smith v. Jones');
    expect(payload['sprk_regardingrecordnumber']).toBe('MAT-2026-0042');
    expect(typeof payload['sprk_regardingrecordurl']).toBe('string');
    expect(payload['sprk_regardingrecordurl'] as string).toContain(MATTER_ID_CLEAN);
  });

  it('populates only ONE entity-specific regarding lookup (ADR-024 mutual exclusion)', async () => {
    const ds = makeDataService();
    const service = new EventService(ds);

    await service.createEvent(
      makeForm({ regardingRecordId: MATTER_ID_RAW, regardingRecordName: 'x' }),
      'sprk_matter',
      NO_CASCADE
    );

    const payload = eventPayload(ds);
    expect('sprk_RegardingMatter@odata.bind' in payload).toBe(true);
    expect('sprk_RegardingProject@odata.bind' in payload).toBe(false);

    // Exactly one entity-specific regarding lookup bind (excluding the resolver
    // discriminator bind sprk_RegardingRecordType).
    const regardingLookupBinds = Object.keys(payload).filter(
      k => k.endsWith('@odata.bind') && k.startsWith('sprk_Regarding') && k !== 'sprk_RegardingRecordType@odata.bind'
    );
    expect(regardingLookupBinds).toEqual(['sprk_RegardingMatter@odata.bind']);
  });

  it('writes NO resolver fields when no regarding parent is supplied', async () => {
    const ds = makeDataService();
    const service = new EventService(ds);

    const result = await service.createEvent(makeForm(), undefined, NO_CASCADE);

    expect(result.success).toBe(true);
    const payload = eventPayload(ds);
    expect('sprk_regardingrecordid' in payload).toBe(false);
    expect('sprk_regardingrecordname' in payload).toBe(false);
    expect('sprk_regardingrecordnumber' in payload).toBe(false);
    expect('sprk_RegardingMatter@odata.bind' in payload).toBe(false);
    expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
  });

  it('graceful degradation (NFR-06): missing catalog still links + falls back on name, skips number/type, never throws', async () => {
    const ds = makeDataService();
    const service = new EventService(ds);

    // sprk_project is intentionally absent from CATALOG.
    const result = await service.createEvent(
      makeForm({ regardingRecordId: PROJECT_ID_CLEAN, regardingRecordName: 'Fallback Project Name' }),
      'sprk_project',
      NO_CASCADE
    );

    expect(result.success).toBe(true);
    const payload = eventPayload(ds);

    // Entity-specific lookup + id/url/name still written (event stays linked).
    expect(payload['sprk_RegardingProject@odata.bind']).toBe(`/sprk_projects(${PROJECT_ID_CLEAN})`);
    expect(payload['sprk_regardingrecordid']).toBe(PROJECT_ID_CLEAN);
    expect(payload['sprk_regardingrecordname']).toBe('Fallback Project Name'); // fallback to picker name
    expect(typeof payload['sprk_regardingrecordurl']).toBe('string');

    // Type discriminator + record number are skipped (no catalog row).
    expect('sprk_RegardingRecordType@odata.bind' in payload).toBe(false);
    expect('sprk_regardingrecordnumber' in payload).toBe(false);
  });
});
