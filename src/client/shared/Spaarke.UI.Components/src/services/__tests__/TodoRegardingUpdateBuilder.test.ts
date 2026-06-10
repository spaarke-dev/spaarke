/**
 * TodoRegardingUpdateBuilder Tests
 *
 * Verifies the FR-13 helper that produces Web API `updateRecord` payloads for
 * `sprk_todo` regarding edits — wraps `PolymorphicResolverService.applyResolverFields`
 * with clear-and-set semantics across all 11 entity-specific lookups (ADR-024).
 *
 * Coverage:
 *   - null → Matter: all 4 resolver fields populated, sprk_regardingmatter bound,
 *     other 10 lookups cleared (null @odata.bind)
 *   - Matter → Project: matter cleared, project bound, all 4 resolver fields
 *     refreshed
 *   - Clear: all 15 fields (11 lookups + 4 resolver) nulled
 *   - All 11 targets recognized
 *   - Unknown entity type throws
 *
 * @see spec.md FR-13 (TodoDetail regarding edit)
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 */

import {
  buildTodoRegardingUpdate,
  buildTodoRegardingClear,
  discoverTodoNavProps,
  TODO_REGARDING_CATALOG,
  _resetTodoNavPropCacheForTests,
} from '../TodoRegardingUpdateBuilder';
import type { INavPropEntry, IPolymorphicWebApi } from '../PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/**
 * Build a representative `sprk_todo` nav-props array. Names follow Dataverse
 * conventions (PascalCase nav-prop, lowercase column). Real environments
 * may differ in casing; the helper accepts whatever the metadata endpoint
 * returns.
 */
function buildTodoNavProps(): INavPropEntry[] {
  return [
    { columnName: 'sprk_regardingmatter', navPropName: 'sprk_RegardingMatter', referencedEntity: 'sprk_matter' },
    { columnName: 'sprk_regardingproject', navPropName: 'sprk_RegardingProject', referencedEntity: 'sprk_project' },
    { columnName: 'sprk_regardingevent', navPropName: 'sprk_RegardingEvent', referencedEntity: 'sprk_event' },
    {
      columnName: 'sprk_regardingcommunication',
      navPropName: 'sprk_RegardingCommunication',
      referencedEntity: 'sprk_communication',
    },
    {
      columnName: 'sprk_regardingworkassignment',
      navPropName: 'sprk_RegardingWorkAssignment',
      referencedEntity: 'sprk_workassignment',
    },
    { columnName: 'sprk_regardinginvoice', navPropName: 'sprk_RegardingInvoice', referencedEntity: 'sprk_invoice' },
    { columnName: 'sprk_regardingbudget', navPropName: 'sprk_RegardingBudget', referencedEntity: 'sprk_budget' },
    { columnName: 'sprk_regardinganalysis', navPropName: 'sprk_RegardingAnalysis', referencedEntity: 'sprk_analysis' },
    {
      columnName: 'sprk_regardingorganization',
      navPropName: 'sprk_RegardingOrganization',
      referencedEntity: 'sprk_organization',
    },
    { columnName: 'sprk_regardingcontact', navPropName: 'sprk_RegardingContact', referencedEntity: 'contact' },
    { columnName: 'sprk_regardingdocument', navPropName: 'sprk_RegardingDocument', referencedEntity: 'sprk_document' },
    {
      columnName: 'sprk_regardingrecordtype',
      navPropName: 'sprk_RegardingRecordType',
      referencedEntity: 'sprk_recordtype_ref',
    },
  ];
}

/**
 * Build a mock IPolymorphicWebApi that returns a canned sprk_recordtype_ref
 * row for any entity logical name asked about. Lets applyResolverFields
 * complete its 4-field population pass.
 */
function buildMockWebApi(): IPolymorphicWebApi {
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({
      entities: [
        {
          sprk_recordtype_refid: 'rt-test-guid-0001',
          sprk_recorddisplayname: 'Test Record Type',
        },
      ],
    }),
  };
}

// All 11 entity-specific @odata.bind keys (PascalCase nav-prop names).
const ALL_ELEVEN_BIND_KEYS = [
  'sprk_RegardingMatter@odata.bind',
  'sprk_RegardingProject@odata.bind',
  'sprk_RegardingEvent@odata.bind',
  'sprk_RegardingCommunication@odata.bind',
  'sprk_RegardingWorkAssignment@odata.bind',
  'sprk_RegardingInvoice@odata.bind',
  'sprk_RegardingBudget@odata.bind',
  'sprk_RegardingAnalysis@odata.bind',
  'sprk_RegardingOrganization@odata.bind',
  'sprk_RegardingContact@odata.bind',
  'sprk_RegardingDocument@odata.bind',
];

// ---------------------------------------------------------------------------
// TODO_REGARDING_CATALOG — shape verification
// ---------------------------------------------------------------------------

describe('TODO_REGARDING_CATALOG', () => {
  it('listsExactlyElevenTargets', () => {
    expect(TODO_REGARDING_CATALOG).toHaveLength(11);
  });

  it('listsTargetsInSpecOrder', () => {
    expect(TODO_REGARDING_CATALOG.map(t => t.entityType)).toEqual([
      'sprk_matter',
      'sprk_project',
      'sprk_event',
      'sprk_communication',
      'sprk_workassignment',
      'sprk_invoice',
      'sprk_budget',
      'sprk_analysis',
      'sprk_organization',
      'contact', // OOB
      'sprk_document',
    ]);
  });

  it('mapsEachEntityToCorrectEntitySet', () => {
    const byEntityType = Object.fromEntries(TODO_REGARDING_CATALOG.map(t => [t.entityType, t.entitySet]));
    expect(byEntityType['sprk_matter']).toBe('sprk_matters');
    expect(byEntityType['sprk_project']).toBe('sprk_projects');
    expect(byEntityType['sprk_event']).toBe('sprk_events');
    expect(byEntityType['sprk_communication']).toBe('sprk_communications');
    expect(byEntityType['sprk_workassignment']).toBe('sprk_workassignments');
    expect(byEntityType['sprk_invoice']).toBe('sprk_invoices');
    expect(byEntityType['sprk_budget']).toBe('sprk_budgets');
    // Irregular plural — `sprk_analysis` → `sprk_analyses`
    expect(byEntityType['sprk_analysis']).toBe('sprk_analyses');
    expect(byEntityType['sprk_organization']).toBe('sprk_organizations');
    // OOB
    expect(byEntityType['contact']).toBe('contacts');
    expect(byEntityType['sprk_document']).toBe('sprk_documents');
  });

  it('hasUniqueLookupAttributes', () => {
    const attrs = TODO_REGARDING_CATALOG.map(t => t.lookupAttribute);
    expect(new Set(attrs).size).toBe(attrs.length);
  });
});

// ---------------------------------------------------------------------------
// buildTodoRegardingUpdate — null → Matter
// ---------------------------------------------------------------------------

describe('buildTodoRegardingUpdate — null → Matter', () => {
  it('populatesAllFourResolverFields', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_matter' },
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Smith v. Jones'
    );

    // 4 resolver fields
    expect(payload['sprk_regardingrecordid']).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    expect(payload['sprk_regardingrecordname']).toBe('Smith v. Jones');
    expect(payload['sprk_regardingrecordurl']).toMatch(/etn=sprk_matter/);
    expect(payload['sprk_regardingrecordurl']).toMatch(/id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/);
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBe('/sprk_recordtype_refs(rt-test-guid-0001)');
  });

  it('bindsSelectedEntitySpecificLookup', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_matter' },
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Smith v. Jones'
    );

    expect(payload['sprk_RegardingMatter@odata.bind']).toBe('/sprk_matters(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)');
  });

  it('clearsAllOtherTenLookups', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_matter' },
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Smith v. Jones'
    );

    // The 10 NON-selected lookups must be explicitly null.
    const nonMatterBindKeys = ALL_ELEVEN_BIND_KEYS.filter(k => k !== 'sprk_RegardingMatter@odata.bind');
    for (const k of nonMatterBindKeys) {
      expect(payload[k]).toBeNull();
    }
  });

  it('normalizesBracedGuidToLowercaseBareGuid', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_matter' },
      '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}',
      'Smith v. Jones'
    );

    expect(payload['sprk_RegardingMatter@odata.bind']).toBe('/sprk_matters(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)');
    expect(payload['sprk_regardingrecordid']).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });
});

// ---------------------------------------------------------------------------
// buildTodoRegardingUpdate — Matter → Project (switch)
// ---------------------------------------------------------------------------

describe('buildTodoRegardingUpdate — Matter → Project', () => {
  it('clearsMatterAndSetsProject', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_project' },
      'ffffffff-0000-1111-2222-333333333333',
      'Project Phoenix'
    );

    // New project lookup is bound
    expect(payload['sprk_RegardingProject@odata.bind']).toBe('/sprk_projects(ffffffff-0000-1111-2222-333333333333)');

    // Previous matter lookup is cleared (null)
    expect(payload['sprk_RegardingMatter@odata.bind']).toBeNull();
  });

  it('refreshesAllFourResolverFieldsToProject', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_project' },
      'ffffffff-0000-1111-2222-333333333333',
      'Project Phoenix'
    );

    expect(payload['sprk_regardingrecordid']).toBe('ffffffff-0000-1111-2222-333333333333');
    expect(payload['sprk_regardingrecordname']).toBe('Project Phoenix');
    expect(payload['sprk_regardingrecordurl']).toMatch(/etn=sprk_project/);
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBe('/sprk_recordtype_refs(rt-test-guid-0001)');
  });

  it('clearsAllNineOtherLookupsTooNotJustTheImmediatePrior', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    const payload = await buildTodoRegardingUpdate(
      webApi,
      navProps,
      { entityType: 'sprk_project' },
      'ffffffff-0000-1111-2222-333333333333',
      'Project Phoenix'
    );

    // The 10 NON-project lookups (incl. matter) must all be null.
    const nonProjectBindKeys = ALL_ELEVEN_BIND_KEYS.filter(k => k !== 'sprk_RegardingProject@odata.bind');
    for (const k of nonProjectBindKeys) {
      expect(payload[k]).toBeNull();
    }
  });
});

// ---------------------------------------------------------------------------
// buildTodoRegardingClear — full clear (all 15 fields)
// ---------------------------------------------------------------------------

describe('buildTodoRegardingClear', () => {
  it('nullsAllElevenEntitySpecificLookups', () => {
    const payload = buildTodoRegardingClear(buildTodoNavProps());
    for (const k of ALL_ELEVEN_BIND_KEYS) {
      expect(payload[k]).toBeNull();
    }
  });

  it('nullsTheRecordTypeLookup', () => {
    const payload = buildTodoRegardingClear(buildTodoNavProps());
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBeNull();
  });

  it('nullsAllThreeResolverTextUrlFields', () => {
    const payload = buildTodoRegardingClear(buildTodoNavProps());
    expect(payload['sprk_regardingrecordid']).toBeNull();
    expect(payload['sprk_regardingrecordname']).toBeNull();
    expect(payload['sprk_regardingrecordurl']).toBeNull();
  });

  it('producesFifteenNullFieldsInTotal', () => {
    const payload = buildTodoRegardingClear(buildTodoNavProps());
    // 11 entity-specific binds + 1 record-type bind + 3 text/URL fields = 15
    const nulledKeys = Object.keys(payload).filter(k => payload[k] === null);
    expect(nulledKeys).toHaveLength(15);
  });

  it('worksWithEmptyNavProps_fallsBackToLookupAttributeNames', () => {
    // When navProps is empty (e.g., metadata endpoint failed), fall back to
    // the catalog's lookupAttribute names. Caller can still send the payload
    // — Dataverse accepts both navprop and column-name@odata.bind syntaxes
    // for clearing lookups.
    const payload = buildTodoRegardingClear([]);
    expect(payload['sprk_regardingmatter@odata.bind']).toBeNull();
    expect(payload['sprk_regardingproject@odata.bind']).toBeNull();
    expect(payload['sprk_regardingdocument@odata.bind']).toBeNull();
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBeNull();
    expect(payload['sprk_regardingrecordid']).toBeNull();
    expect(payload['sprk_regardingrecordname']).toBeNull();
    expect(payload['sprk_regardingrecordurl']).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// All 11 targets — smoke test
// ---------------------------------------------------------------------------

describe('buildTodoRegardingUpdate — all 11 targets', () => {
  it.each(TODO_REGARDING_CATALOG.map(t => [t.entityType, t.entitySet, t.lookupAttribute]))(
    'buildsCorrectPayloadFor_%s',
    async (entityType, entitySet, lookupAttribute) => {
      const webApi = buildMockWebApi();
      const navProps = buildTodoNavProps();
      const recordId = 'aaaaaaaa-1111-1111-1111-111111111111';

      const payload = await buildTodoRegardingUpdate(webApi, navProps, { entityType }, recordId, 'Display Name');

      // The nav-prop name for this catalog entry is the PascalCase form.
      const expectedNavProp = navProps.find(n => n.referencedEntity === entityType)!.navPropName;
      expect(payload[`${expectedNavProp}@odata.bind`]).toBe(`/${entitySet}(${recordId})`);
      expect(payload['sprk_regardingrecordid']).toBe(recordId);
      expect(payload['sprk_regardingrecordname']).toBe('Display Name');
      expect(payload['sprk_regardingrecordurl']).toMatch(new RegExp(`etn=${entityType}`));

      // Sanity check: lookupAttribute is referenced by the catalog (compile-time alignment)
      expect(lookupAttribute).toMatch(/^sprk_regarding/);
    }
  );
});

// ---------------------------------------------------------------------------
// Error handling
// ---------------------------------------------------------------------------

describe('buildTodoRegardingUpdate — error handling', () => {
  it('throwsForUnknownEntityType', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    await expect(
      buildTodoRegardingUpdate(
        webApi,
        navProps,
        { entityType: 'sprk_unknown_entity' },
        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        'X'
      )
    ).rejects.toThrow(/Unknown entity type/);
  });
});

// ---------------------------------------------------------------------------
// discoverTodoNavProps — fetch + cache behavior
// ---------------------------------------------------------------------------

describe('discoverTodoNavProps', () => {
  beforeEach(() => {
    _resetTodoNavPropCacheForTests();
  });

  it('callsTheCorrectMetadataEndpoint', async () => {
    const mockFetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => ({
        value: [
          {
            ReferencingAttribute: 'sprk_regardingmatter',
            ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
            ReferencedEntity: 'sprk_matter',
          },
        ],
      }),
    });

    const result = await discoverTodoNavProps(mockFetch as unknown as typeof fetch);
    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining("EntityDefinitions(LogicalName='sprk_todo')/ManyToOneRelationships"),
      expect.objectContaining({ credentials: 'include' })
    );
    expect(result).toHaveLength(1);
    expect(result[0]).toEqual({
      columnName: 'sprk_regardingmatter',
      navPropName: 'sprk_RegardingMatter',
      referencedEntity: 'sprk_matter',
    });
  });

  it('cachesResultBetweenCalls', async () => {
    const mockFetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ value: [] }),
    });

    await discoverTodoNavProps(mockFetch as unknown as typeof fetch);
    await discoverTodoNavProps(mockFetch as unknown as typeof fetch);
    // Second call should be cached — fetch called only once.
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('returnsEmptyArrayWhenFetchFails', async () => {
    const mockFetch = jest.fn().mockResolvedValue({ ok: false, status: 500 });
    const result = await discoverTodoNavProps(mockFetch as unknown as typeof fetch);
    expect(result).toEqual([]);
  });

  it('returnsEmptyArrayWhenFetchThrows', async () => {
    const mockFetch = jest.fn().mockRejectedValue(new Error('network'));
    const result = await discoverTodoNavProps(mockFetch as unknown as typeof fetch);
    expect(result).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Link-click navigation (FR-13 acceptance)
//
// Verifies that the URL produced by buildTodoRegardingUpdate is a Dataverse
// `/main.aspx?pagetype=entityrecord&...` URL that, when passed to a host's
// open-url handler, navigates to the correct regarding record. The actual
// Xrm.Navigation.openUrl call is exercised in the TodoDetailPanel host
// integration; here we verify the URL shape is correct.
// ---------------------------------------------------------------------------

describe('regardingrecordurl shape (FR-13 link target)', () => {
  it('producesValidMainAspxUrlForEachTarget', async () => {
    const webApi = buildMockWebApi();
    const navProps = buildTodoNavProps();

    for (const target of TODO_REGARDING_CATALOG) {
      const payload = await buildTodoRegardingUpdate(
        webApi,
        navProps,
        { entityType: target.entityType },
        'aaaaaaaa-1111-1111-1111-111111111111',
        'Test'
      );
      const url = payload['sprk_regardingrecordurl'] as string;
      expect(url).toMatch(/main\.aspx/);
      expect(url).toMatch(/pagetype=entityrecord/);
      expect(url).toMatch(new RegExp(`etn=${target.entityType}`));
      expect(url).toMatch(/id=aaaaaaaa-1111-1111-1111-111111111111/);
    }
  });
});
