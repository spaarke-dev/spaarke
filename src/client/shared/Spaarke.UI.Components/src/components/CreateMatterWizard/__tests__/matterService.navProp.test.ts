/**
 * MatterService — nav-prop convergence payload-equivalence (task 016)
 *
 * Task 016 converged matterService onto the shared `discoverNavProps`
 * (PolymorphicResolverService) via the `toNavPropMap` adapter, replacing its
 * former private map-form `_discoverNavProps`. The BINDING constraint is that
 * matter's create payload stays byte-identical: same `@odata.bind` keys, same
 * values, same order.
 *
 * These tests are the equivalence proof:
 *   1. `toNavPropMap` reproduces the exact `columnName → navProp` map matter's
 *      old private discovery built (including last-write-wins on a duplicate
 *      column — the object-key overwrite behavior of the pre-change code).
 *   2. End-to-end: `MatterService.createMatter` emits the same PascalCase
 *      `@odata.bind` keys/values/order it did before convergence, given the
 *      same ManyToOneRelationships metadata. The golden baseline below is the
 *      output of the pre-change algorithm
 *      (`map[ReferencingAttribute] = NavPropName`, then
 *      `entity[`${navProp}@odata.bind`] = `/${entitySet}(${cleanGuid(guid)})`).
 */

import { MatterService } from '../matterService';
import { toNavPropMap, _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import type { INavPropEntry } from '../../../services/PolymorphicResolverService';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// 1. Adapter-level equivalence — toNavPropMap reproduces the map form exactly
// ---------------------------------------------------------------------------

describe("toNavPropMap — reproduces matter's former map-form discovery", () => {
  it('returns {} for an empty entry set', () => {
    expect(toNavPropMap([])).toEqual({});
  });

  it('maps each columnName to its navPropName', () => {
    const entries: INavPropEntry[] = [
      { columnName: 'sprk_mattertype', navPropName: 'sprk_MatterType', referencedEntity: 'sprk_mattertype_ref' },
      { columnName: 'sprk_assignedattorney1', navPropName: 'sprk_AssignedAttorney1', referencedEntity: 'contact' },
    ];
    expect(toNavPropMap(entries)).toEqual({
      sprk_mattertype: 'sprk_MatterType',
      sprk_assignedattorney1: 'sprk_AssignedAttorney1',
    });
  });

  it('applies last-write-wins on a duplicate columnName (matches the object-key overwrite of the old code)', () => {
    // Two relationships referencing the same column — the pre-change code did
    // `map[col] = navProp` in iteration order, so the LAST entry won. The
    // adapter must preserve that exact behavior.
    const entries: INavPropEntry[] = [
      { columnName: 'sprk_dupe', navPropName: 'sprk_First', referencedEntity: 'a' },
      { columnName: 'sprk_dupe', navPropName: 'sprk_Second', referencedEntity: 'b' },
    ];
    expect(toNavPropMap(entries)).toEqual({ sprk_dupe: 'sprk_Second' });
  });
});

// ---------------------------------------------------------------------------
// 2. End-to-end payload equivalence — createMatter emits identical binds
// ---------------------------------------------------------------------------

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

/** ManyToOneRelationships metadata for sprk_matter (PascalCase nav props). */
const MATTER_RELATIONSHIPS = [
  {
    ReferencingAttribute: 'sprk_mattertype',
    ReferencingEntityNavigationPropertyName: 'sprk_MatterType',
    ReferencedEntity: 'sprk_mattertype_ref',
  },
  {
    ReferencingAttribute: 'sprk_practicearea',
    ReferencingEntityNavigationPropertyName: 'sprk_PracticeArea',
    ReferencedEntity: 'sprk_practicearea_ref',
  },
  {
    ReferencingAttribute: 'sprk_assignedattorney1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney1',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedparalegal1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedParalegal1',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedlawfirm1',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedLawFirm1',
    ReferencedEntity: 'sprk_organization',
  },
];

const BFF_BASE = 'https://example.test';

describe('MatterService — nav-prop convergence create-payload equivalence (task 016)', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    globalThis.fetch = jest.fn(async (url: string) => {
      if (typeof url === 'string' && url.includes('sprk_matter')) {
        return { ok: true, status: 200, json: async () => ({ value: MATTER_RELATIONSHIPS }) } as unknown as Response;
      }
      return { ok: false, status: 404, json: async () => ({ value: [] }) } as unknown as Response;
    }) as unknown as typeof fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    _resetNavPropCacheForTests();
    jest.clearAllMocks();
  });

  it('emits the exact PascalCase @odata.bind keys/values/order (byte-identical to pre-convergence output)', async () => {
    const { dataService, createCalls } = makeDataService();
    const authFetch = jest.fn();

    const service = new MatterService(dataService, authFetch, BFF_BASE);

    // All five lookups populated. `assignedOutsideCounselId` is deliberately a
    // braced, upper-case GUID to prove `cleanGuid` still normalizes it after
    // the convergence (strip braces + lowercase).
    const result = await service.createMatter(
      makeForm({
        matterTypeId: 'mt-guid-1',
        practiceAreaId: 'pa-guid-1',
        assignedAttorneyId: 'att-guid-1',
        assignedParalegalId: 'para-guid-1',
        assignedOutsideCounselId: '{OC-GUID-1}',
      }),
      [],
      {}
    );

    expect(result.status).toBe('success');
    const payload = createCalls[0].payload;

    // Golden baseline — the pre-change algorithm's output for these lookups.
    const expectedBinds: Record<string, string> = {
      'sprk_MatterType@odata.bind': '/sprk_mattertype_refs(mt-guid-1)',
      'sprk_PracticeArea@odata.bind': '/sprk_practicearea_refs(pa-guid-1)',
      'sprk_AssignedAttorney1@odata.bind': '/contacts(att-guid-1)',
      'sprk_AssignedParalegal1@odata.bind': '/contacts(para-guid-1)',
      'sprk_AssignedLawFirm1@odata.bind': '/sprk_organizations(oc-guid-1)',
    };

    // Values byte-identical.
    for (const [key, value] of Object.entries(expectedBinds)) {
      expect(payload[key]).toBe(value);
    }

    // Keys + INSERTION ORDER byte-identical (same order the lookups[] array
    // pushes them — matter's create-payload contract).
    const emittedBindKeys = Object.keys(payload).filter(k => k.endsWith('@odata.bind'));
    expect(emittedBindKeys).toEqual(Object.keys(expectedBinds));
  });

  it('falls back to the column logical name when metadata discovery yields nothing (unchanged behavior)', async () => {
    // Force discovery to return [] → toNavPropMap({}) → `map[col] ?? col`
    // falls back to the bare column name, exactly as the old code did.
    globalThis.fetch = jest.fn(
      async () => ({ ok: false, status: 500, json: async () => ({}) }) as unknown as Response
    ) as unknown as typeof fetch;

    const { dataService, createCalls } = makeDataService();
    const service = new MatterService(dataService, jest.fn(), BFF_BASE);

    await service.createMatter(makeForm({ assignedAttorneyId: 'att-guid-1' }), [], {});

    const payload = createCalls[0].payload;
    // No PascalCase nav prop available → column-name fallback.
    expect(payload['sprk_assignedattorney1@odata.bind']).toBe('/contacts(att-guid-1)');
    expect(payload['sprk_AssignedAttorney1@odata.bind']).toBeUndefined();
  });
});
