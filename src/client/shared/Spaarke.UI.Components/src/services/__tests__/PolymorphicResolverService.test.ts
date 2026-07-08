/**
 * PolymorphicResolverService.applyResolverFields() — 5-field write tests.
 *
 * Covers the FR-A4-01 / FR-C1-01 extension (set-regarding-and-field-mapping-
 * resolver-r1): adds a data-driven `sprk_regardingrecordnumber` write sourced
 * from `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (with an optional
 * `options.sourceRecordNumberField` override for the caller-explicit path).
 *
 * Scenarios covered per task POML Step 6:
 *   (a) Happy path — 11 target entities including Matter → 5-field write.
 *   (b) Metadata-null case — Contact/Person + intentional-null → warn + skip
 *       (graceful-blank per NFR-06).
 *   (c) Target-record null-value case — metadata resolves but the target
 *       record's field value is null → warn + skip.
 *   (d) Backward-compat — caller without `options.sourceRecordNumberField`
 *       still gets the correct 4-or-5-field write depending on catalog data.
 *   (e) Per-target column names (D-13 handling) — Contact and Account use
 *       OOB source fields (`accountnumber` on Account; graceful-blank on
 *       Contact) sourced from the catalog, not from a hardcoded map.
 *
 * Test doctrine: no `Mock<HttpMessageHandler>` (ADR-038 compliance — TypeScript
 * equivalent = no direct `fetch`/XHR mocking). All I/O goes through the
 * injected `IPolymorphicWebApi` shim.
 *
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see .claude/adr/ADR-038-testing-strategy.md
 * @see projects/set-regarding-and-field-mapping-resolver-r1/spec.md §FR-A4-01
 * @see projects/set-regarding-and-field-mapping-resolver-r1/notes/wave-1-schema-additions.log — D-13
 */

import {
  applyResolverFields,
  resolveRecordNumberFieldName,
  resolveRecordDisplayNameFieldName,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../PolymorphicResolverService';
import type { INavPropEntry, IPolymorphicWebApi } from '../PolymorphicResolverService';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

/**
 * Simulated `sprk_recordtype_ref` catalog rows (as-of SRFR-052 2026-07-06).
 * `recordNumberField` null = intentional graceful-blank (Contact/Person +
 * host-only rows that don't have a natural business-key text field).
 * `recordDisplayNameField` names the DISPLAY-NAME column on each target
 * entity (SRFR-052 — mirrors the deployed schema). Owner UAT surfaced that
 * `Xrm.Utility.lookupObjects` returns the Primary Name column, which for
 * sprk_matter is `sprk_matternumber` (not `sprk_mattername`); the resolver
 * now queries the catalog-nominated display-name column.
 */
const CATALOG: Record<
  string,
  {
    recordTypeId: string;
    recordDisplayName: string;
    recordNumberField: string | null;
    recordDisplayNameField: string | null;
  }
> = {
  sprk_matter: {
    recordTypeId: 'rt-matter',
    recordDisplayName: 'Matter',
    recordNumberField: 'sprk_matternumber',
    recordDisplayNameField: 'sprk_mattername',
  },
  sprk_project: {
    recordTypeId: 'rt-project',
    recordDisplayName: 'Project',
    recordNumberField: 'sprk_projectnumber',
    recordDisplayNameField: 'sprk_projectname',
  },
  sprk_event: {
    recordTypeId: 'rt-event',
    recordDisplayName: 'Event',
    recordNumberField: 'sprk_eventnumber',
    recordDisplayNameField: 'sprk_eventname',
  },
  sprk_workassignment: {
    recordTypeId: 'rt-wa',
    recordDisplayName: 'Work Assignment',
    recordNumberField: 'sprk_workassignmentnumber',
    recordDisplayNameField: 'sprk_workassignmentname',
  },
  sprk_invoice: {
    recordTypeId: 'rt-invoice',
    recordDisplayName: 'Invoice',
    recordNumberField: 'sprk_invoicenumber',
    recordDisplayNameField: 'sprk_invoicename',
  },
  sprk_budget: {
    recordTypeId: 'rt-budget',
    recordDisplayName: 'Budget',
    recordNumberField: 'sprk_budgetnumber',
    recordDisplayNameField: 'sprk_budgetname',
  },
  sprk_analysis: {
    recordTypeId: 'rt-analysis',
    recordDisplayName: 'Analysis',
    recordNumberField: 'sprk_analysisnumber',
    recordDisplayNameField: 'sprk_analysisname',
  },
  sprk_organization: {
    recordTypeId: 'rt-org',
    recordDisplayName: 'Organization',
    recordNumberField: 'sprk_organizationnumber',
    recordDisplayNameField: 'sprk_organizationname',
  },
  sprk_document: {
    recordTypeId: 'rt-doc',
    recordDisplayName: 'Document',
    recordNumberField: 'sprk_documentnumber',
    recordDisplayNameField: 'sprk_documentname',
  },
  account: {
    recordTypeId: 'rt-account',
    recordDisplayName: 'Account',
    recordNumberField: 'accountnumber',
    recordDisplayNameField: 'name',
  },
  // Contact: intentional-null recordNumberField per Q-06 / A-07 (no natural
  // business-key on OOB contact). Display-name uses OOB `fullname` (SRFR-052).
  contact: {
    recordTypeId: 'rt-contact',
    recordDisplayName: 'Person',
    recordNumberField: null,
    recordDisplayNameField: 'fullname',
  },
};

/**
 * Build a host nav-props array covering all 11 target lookups + the
 * `sprk_regardingrecordtype` reference lookup. Names follow Dataverse
 * conventions.
 */
function buildHostNavProps(): INavPropEntry[] {
  return [
    { columnName: 'sprk_regardingmatter', navPropName: 'sprk_RegardingMatter', referencedEntity: 'sprk_matter' },
    { columnName: 'sprk_regardingproject', navPropName: 'sprk_RegardingProject', referencedEntity: 'sprk_project' },
    { columnName: 'sprk_regardingevent', navPropName: 'sprk_RegardingEvent', referencedEntity: 'sprk_event' },
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
    { columnName: 'sprk_regardingdocument', navPropName: 'sprk_RegardingDocument', referencedEntity: 'sprk_document' },
    { columnName: 'sprk_regardingaccount', navPropName: 'sprk_RegardingAccount', referencedEntity: 'account' },
    { columnName: 'sprk_regardingcontact', navPropName: 'sprk_RegardingContact', referencedEntity: 'contact' },
    {
      columnName: 'sprk_regardingrecordtype',
      navPropName: 'sprk_RegardingRecordType',
      referencedEntity: 'sprk_recordtype_ref',
    },
  ];
}

/**
 * Build a mock `IPolymorphicWebApi` that answers three families of queries:
 *   1. `sprk_recordtype_ref` catalog lookups (by logical name) — used by
 *      `resolveRecordType`, `resolveRecordNumberFieldName`, and (SRFR-052)
 *      `resolveRecordDisplayNameFieldName`. Returns ALL three catalog columns
 *      regardless of `$select` since the mock is $select-agnostic.
 *   2. Target-record lookups (by `${entity}id eq …`) — used by the resolver's
 *      combined number + display-name read path. Supports multi-field
 *      `$select=fieldA,fieldB` (SRFR-052 consolidated one round-trip).
 *   3. Anything else → empty entities array.
 *
 * `targetRecordValues` maps target entity logical name → per-field values.
 * Supports two shapes:
 *   - `{ sprk_matter: 'M-00042' }` → treated as `{ [numberField]: 'M-00042' }`
 *     for backward compat with the FR-A4-01 tests.
 *   - `{ sprk_matter: { sprk_matternumber: 'M-00042', sprk_mattername: 'Smith v Jones' } }`
 *     → per-field values for the SRFR-052 display-name tests.
 * Missing entry / null value = target record has no value for that field.
 */
type TargetRecordValueSpec = string | null | Record<string, string | null>;

function buildMockWebApi(targetRecordValues: Record<string, TargetRecordValueSpec>): IPolymorphicWebApi {
  return {
    retrieveMultipleRecords: jest.fn(async (entityLogicalName: string, query: string, _maxPageSize?: number) => {
      if (entityLogicalName === 'sprk_recordtype_ref') {
        const filterMatch = query.match(/sprk_recordlogicalname eq '([^']+)'/);
        const logicalName = filterMatch ? filterMatch[1] : null;
        if (logicalName && CATALOG[logicalName]) {
          const row = CATALOG[logicalName];
          return {
            entities: [
              {
                sprk_recordtype_refid: row.recordTypeId,
                sprk_recorddisplayname: row.recordDisplayName,
                sprk_regardingrecordnumberfield: row.recordNumberField,
                sprk_recorddisplaynamefield: row.recordDisplayNameField,
              },
            ],
          };
        }
        return { entities: [] };
      }

      // Target-record lookup (any entity except sprk_recordtype_ref).
      // Support multi-field $select per SRFR-052 (resolver now reads both
      // number and display-name in one round trip).
      const selectMatch = query.match(/\$select=([^&]+)/);
      const selectedFields = selectMatch ? selectMatch[1].split(',') : [];
      const spec = targetRecordValues[entityLogicalName];
      if (selectedFields.length === 0 || spec === undefined) {
        return { entities: [] };
      }

      const record: Record<string, string | null> = {};
      if (typeof spec === 'string' || spec === null) {
        // Legacy single-value shape — assumed to be the record-number field.
        // Populate whichever of the requested fields matches the catalog's
        // number-field for this entity; leave others absent (=> null in the
        // resolver's read).
        const numberField = CATALOG[entityLogicalName]?.recordNumberField;
        for (const field of selectedFields) {
          if (field === numberField) {
            record[field] = spec;
          }
        }
      } else {
        // Per-field object — return the requested subset.
        for (const field of selectedFields) {
          if (field in spec) {
            record[field] = spec[field];
          }
        }
      }

      return { entities: [record] };
    }),
  };
}

// ---------------------------------------------------------------------------
// Setup
// ---------------------------------------------------------------------------

let consoleWarnSpy: jest.SpyInstance;

beforeEach(() => {
  _resetRecordNumberFieldCacheForTests();
  _resetDisplayNameFieldCacheForTests();
  consoleWarnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {
    /* silence for expected-warn assertions */
  });
});

afterEach(() => {
  consoleWarnSpy.mockRestore();
});

// ---------------------------------------------------------------------------
// resolveRecordNumberFieldName — the metadata-resolution helper
// ---------------------------------------------------------------------------

describe('resolveRecordNumberFieldName', () => {
  it('returns the source-field name when catalog has a non-null value', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    expect(result).toBe('sprk_matternumber');
  });

  it('returns null when catalog value is null (intentional graceful-blank)', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordNumberFieldName(webApi, 'contact');
    expect(result).toBeNull();
  });

  it('returns null when no catalog row matches (unknown entity type)', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordNumberFieldName(webApi, 'sprk_nonexistent');
    expect(result).toBeNull();
  });

  it('caches on subsequent calls (single query per entity per page)', async () => {
    const webApi = buildMockWebApi({});
    await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });

  it('does NOT cache errors (allows retry on next call)', async () => {
    const webApi: IPolymorphicWebApi = {
      retrieveMultipleRecords: jest
        .fn()
        .mockRejectedValueOnce(new Error('boom'))
        .mockResolvedValueOnce({
          entities: [
            {
              sprk_recordtype_refid: 'rt-matter',
              sprk_recorddisplayname: 'Matter',
              sprk_regardingrecordnumberfield: 'sprk_matternumber',
            },
          ],
        }),
    };

    const first = await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    expect(first).toBeNull();
    const second = await resolveRecordNumberFieldName(webApi, 'sprk_matter');
    expect(second).toBe('sprk_matternumber');
    expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(2);
  });
});

// ---------------------------------------------------------------------------
// applyResolverFields — 5-field write (happy path)
// ---------------------------------------------------------------------------

describe('applyResolverFields — 5-field write (happy path)', () => {
  const HAPPY_PATH_ENTITIES: ReadonlyArray<{
    entity: string;
    entitySet: string;
    hint: string;
    expectedSourceField: string;
    expectedNumberValue: string;
    navPropName: string;
  }> = [
    {
      entity: 'sprk_matter',
      entitySet: 'sprk_matters',
      hint: 'matter',
      expectedSourceField: 'sprk_matternumber',
      expectedNumberValue: 'M-00042',
      navPropName: 'sprk_RegardingMatter',
    },
    {
      entity: 'sprk_project',
      entitySet: 'sprk_projects',
      hint: 'project',
      expectedSourceField: 'sprk_projectnumber',
      expectedNumberValue: 'P-00007',
      navPropName: 'sprk_RegardingProject',
    },
    {
      entity: 'sprk_event',
      entitySet: 'sprk_events',
      hint: 'event',
      expectedSourceField: 'sprk_eventnumber',
      expectedNumberValue: 'E-2026-01',
      navPropName: 'sprk_RegardingEvent',
    },
    {
      entity: 'sprk_workassignment',
      entitySet: 'sprk_workassignments',
      hint: 'workassignment',
      expectedSourceField: 'sprk_workassignmentnumber',
      expectedNumberValue: 'WA-201',
      navPropName: 'sprk_RegardingWorkAssignment',
    },
    {
      entity: 'sprk_invoice',
      entitySet: 'sprk_invoices',
      hint: 'invoice',
      expectedSourceField: 'sprk_invoicenumber',
      expectedNumberValue: 'INV-9001',
      navPropName: 'sprk_RegardingInvoice',
    },
    {
      entity: 'sprk_budget',
      entitySet: 'sprk_budgets',
      hint: 'budget',
      expectedSourceField: 'sprk_budgetnumber',
      expectedNumberValue: 'B-101',
      navPropName: 'sprk_RegardingBudget',
    },
    {
      entity: 'sprk_analysis',
      entitySet: 'sprk_analyses',
      hint: 'analysis',
      expectedSourceField: 'sprk_analysisnumber',
      expectedNumberValue: 'A-33',
      navPropName: 'sprk_RegardingAnalysis',
    },
    {
      entity: 'sprk_organization',
      entitySet: 'sprk_organizations',
      hint: 'organization',
      expectedSourceField: 'sprk_organizationnumber',
      expectedNumberValue: 'ORG-77',
      navPropName: 'sprk_RegardingOrganization',
    },
    {
      entity: 'sprk_document',
      entitySet: 'sprk_documents',
      hint: 'document',
      expectedSourceField: 'sprk_documentnumber',
      expectedNumberValue: 'DOC-9',
      navPropName: 'sprk_RegardingDocument',
    },
    {
      entity: 'account',
      entitySet: 'accounts',
      hint: 'account',
      expectedSourceField: 'accountnumber',
      expectedNumberValue: 'ACCT-001',
      navPropName: 'sprk_RegardingAccount',
    },
  ];

  it.each(HAPPY_PATH_ENTITIES)(
    'writes the 5th field sprk_regardingrecordnumber for $entity (source=$expectedSourceField, value=$expectedNumberValue)',
    async ({ entity, entitySet, hint, expectedSourceField, expectedNumberValue, navPropName }) => {
      const targetId = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
      const webApi = buildMockWebApi({ [entity]: expectedNumberValue });
      const payload: Record<string, unknown> = {};

      const result = await applyResolverFields(
        webApi,
        payload,
        buildHostNavProps(),
        entity,
        entitySet,
        targetId,
        `Test ${entity}`,
        hint
      );

      // All 5 fields present.
      expect(payload[`${navPropName}@odata.bind`]).toBe(`/${entitySet}(${targetId})`);
      expect(payload['sprk_regardingrecordid']).toBe(targetId);
      expect(payload['sprk_regardingrecordname']).toBe(`Test ${entity}`);
      expect(payload['sprk_regardingrecordurl']).toEqual(expect.stringContaining(entity));
      expect(payload['sprk_RegardingRecordType@odata.bind']).toEqual(expect.stringContaining('sprk_recordtype_refs('));
      expect(payload['sprk_regardingrecordnumber']).toBe(expectedNumberValue);

      // Return value carries the resolved data.
      expect(result.recordNumber).toBe(expectedNumberValue);
      expect(result.recordNumberSourceField).toBe(expectedSourceField);
    }
  );

  it('normalizes braced GUIDs before both @odata.bind and target-record queries', async () => {
    const webApi = buildMockWebApi({ sprk_matter: 'M-00042' });
    const payload: Record<string, unknown> = {};
    const braced = '{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}';

    await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      braced,
      'Test Matter',
      'matter'
    );

    // The @odata.bind value uses the cleaned GUID (no braces, lowercase).
    expect(payload['sprk_RegardingMatter@odata.bind']).toBe('/sprk_matters(aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee)');
    expect(payload['sprk_regardingrecordid']).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');

    // The target-record query used the cleaned GUID too (verify via the mock call).
    const calls = (webApi.retrieveMultipleRecords as jest.Mock).mock.calls;
    const targetCall = calls.find(c => c[0] === 'sprk_matter');
    expect(targetCall).toBeDefined();
    expect(targetCall?.[1]).toContain('sprk_matterid eq aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });
});

// ---------------------------------------------------------------------------
// applyResolverFields — NFR-06 graceful-blank scenarios
// ---------------------------------------------------------------------------

describe('applyResolverFields — NFR-06 graceful-blank', () => {
  it('warns and skips sprk_regardingrecordnumber when catalog value is null (Contact/Person)', async () => {
    const webApi = buildMockWebApi({ contact: 'ignored' });
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'contact',
      'contacts',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Jane Doe',
      'contact'
    );

    // 4-field write preserved.
    expect(payload['sprk_regardingrecordid']).toBeDefined();
    expect(payload['sprk_regardingrecordname']).toBe('Jane Doe');
    expect(payload['sprk_regardingrecordurl']).toBeDefined();
    expect(payload['sprk_RegardingContact@odata.bind']).toBeDefined();

    // 5th field NOT written.
    expect(payload['sprk_regardingrecordnumber']).toBeUndefined();

    // Warn logged; no throw.
    expect(consoleWarnSpy).toHaveBeenCalledWith(
      expect.stringContaining('No sprk_regardingrecordnumberfield mapping for "contact"')
    );

    // Return value signals graceful-blank.
    expect(result.recordNumber).toBeNull();
    expect(result.recordNumberSourceField).toBeNull();
  });

  it('warns and skips when target record has null value in the source field', async () => {
    // Catalog resolves to `sprk_matternumber`, but the target record's value is null.
    const webApi = buildMockWebApi({ sprk_matter: null });
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Empty Matter',
      'matter'
    );

    expect(payload['sprk_regardingrecordnumber']).toBeUndefined();
    expect(consoleWarnSpy).toHaveBeenCalledWith(expect.stringContaining('has null/empty "sprk_matternumber"'));
    expect(result.recordNumber).toBeNull();
    expect(result.recordNumberSourceField).toBe('sprk_matternumber');
  });

  it('warns and skips when target-record query fails (does NOT throw)', async () => {
    const webApi: IPolymorphicWebApi = {
      retrieveMultipleRecords: jest.fn(async (entityLogicalName: string, query: string) => {
        if (entityLogicalName === 'sprk_recordtype_ref') {
          return {
            entities: [
              {
                sprk_recordtype_refid: 'rt-matter',
                sprk_recorddisplayname: 'Matter',
                sprk_regardingrecordnumberfield: 'sprk_matternumber',
              },
            ],
          };
        }
        // Target-record query fails.
        throw new Error('target read failed');
      }),
    };
    const payload: Record<string, unknown> = {};

    await expect(
      applyResolverFields(
        webApi,
        payload,
        buildHostNavProps(),
        'sprk_matter',
        'sprk_matters',
        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
        'Matter',
        'matter'
      )
    ).resolves.toMatchObject({ recordNumber: null, recordNumberSourceField: 'sprk_matternumber' });

    expect(payload['sprk_regardingrecordnumber']).toBeUndefined();
    // Warn message now includes both source fields because SRFR-052 consolidated
    // the number + display-name reads into a single $select. Catalog fixture in
    // this test only returns `sprk_regardingrecordnumberfield` (display-name
    // catalog value defaults to null → excluded from $select), so the failed
    // read's $select contains just `sprk_matternumber` — old assertion still holds.
    expect(consoleWarnSpy).toHaveBeenCalledWith(
      expect.stringContaining('Failed to read [sprk_matternumber]'),
      expect.any(Error)
    );
  });

  it('warns and skips when catalog row does NOT exist for the target entity', async () => {
    const webApi = buildMockWebApi({});
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_nonexistent',
      'sprk_nonexistents',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Ghost',
      'nonexistent'
    );

    expect(payload['sprk_regardingrecordnumber']).toBeUndefined();
    expect(result.recordNumber).toBeNull();
    expect(result.recordNumberSourceField).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// applyResolverFields — backward compat + explicit override
// ---------------------------------------------------------------------------

describe('applyResolverFields — backward compat + explicit override', () => {
  it('preserves 4-field behavior when caller supplies NO options (matter catalog null override)', async () => {
    // Simulate a fresh environment where sprk_recordtype_ref rows have not yet
    // been populated (Wave 0 pre-fix state). The 5th field silently skips.
    const webApi: IPolymorphicWebApi = {
      retrieveMultipleRecords: jest.fn(async (entityLogicalName: string, _query: string) => {
        if (entityLogicalName === 'sprk_recordtype_ref') {
          return {
            entities: [
              {
                sprk_recordtype_refid: 'rt-matter',
                sprk_recorddisplayname: 'Matter',
                sprk_regardingrecordnumberfield: null,
              },
            ],
          };
        }
        return { entities: [] };
      }),
    };
    const payload: Record<string, unknown> = {};

    await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Legacy Matter',
      'matter'
    );

    // The 4 historical fields are all still present.
    expect(payload['sprk_RegardingMatter@odata.bind']).toBeDefined();
    expect(payload['sprk_regardingrecordid']).toBeDefined();
    expect(payload['sprk_regardingrecordname']).toBe('Legacy Matter');
    expect(payload['sprk_regardingrecordurl']).toBeDefined();
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBeDefined();
    // 5th field silently omitted → no regression.
    expect(payload['sprk_regardingrecordnumber']).toBeUndefined();
  });

  it('honors the explicit sourceRecordNumberField override (bypasses catalog lookup)', async () => {
    // Catalog is empty for this entity, but the caller passes an explicit
    // override. The service should use that override without hitting the catalog.
    const webApi: IPolymorphicWebApi = {
      retrieveMultipleRecords: jest.fn(async (entityLogicalName: string, query: string) => {
        if (entityLogicalName === 'sprk_recordtype_ref') {
          return { entities: [] };
        }
        // Return a stub with the caller-specified field name.
        const selectMatch = query.match(/\$select=([^&]+)/);
        const selectedField = selectMatch ? selectMatch[1] : null;
        if (selectedField === 'sprk_customnumber') {
          return { entities: [{ sprk_customnumber: 'CUST-99' }] };
        }
        return { entities: [] };
      }),
    };
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Custom Matter',
      'matter',
      { sourceRecordNumberField: 'sprk_customnumber' }
    );

    expect(payload['sprk_regardingrecordnumber']).toBe('CUST-99');
    expect(result.recordNumber).toBe('CUST-99');
    expect(result.recordNumberSourceField).toBe('sprk_customnumber');
  });

  it('ignores an empty-string override and falls back to catalog resolution', async () => {
    const webApi = buildMockWebApi({ sprk_matter: 'M-00042' });
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Matter',
      'matter',
      { sourceRecordNumberField: '   ' }
    );

    // Fell back to catalog → resolved sprk_matternumber → target value M-00042.
    expect(payload['sprk_regardingrecordnumber']).toBe('M-00042');
    expect(result.recordNumberSourceField).toBe('sprk_matternumber');
  });
});

// ---------------------------------------------------------------------------
// SRFR-052 (2026-07-06) — display-name resolution via
// `sprk_recordtype_ref.sprk_recorddisplaynamefield`.
//
// Root cause fixed: `Xrm.Utility.lookupObjects` returns the target record's
// Primary Name. For sprk_matter, the Primary Name column is
// `sprk_matternumber` (NOT `sprk_mattername`), so the picker was returning
// the number as the display name. Fix: catalog nominates the true display-
// name column per entity; resolver queries the target for that column's
// value and writes it to `sprk_regardingrecordname`. Falls back to the
// picker-provided `parentRecordName` on null/missing (NFR-06 graceful-blank).
// ---------------------------------------------------------------------------

describe('resolveRecordDisplayNameFieldName', () => {
  it('returns sprk_mattername for sprk_matter', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordDisplayNameFieldName(webApi, 'sprk_matter');
    expect(result).toBe('sprk_mattername');
  });

  it('returns name for account (OOB display-name column)', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordDisplayNameFieldName(webApi, 'account');
    expect(result).toBe('name');
  });

  it('returns fullname for contact (OOB display-name column)', async () => {
    const webApi = buildMockWebApi({});
    const result = await resolveRecordDisplayNameFieldName(webApi, 'contact');
    expect(result).toBe('fullname');
  });

  it('caches on subsequent calls (single query per entity per page)', async () => {
    const webApi = buildMockWebApi({});
    await resolveRecordDisplayNameFieldName(webApi, 'sprk_matter');
    await resolveRecordDisplayNameFieldName(webApi, 'sprk_matter');
    await resolveRecordDisplayNameFieldName(webApi, 'sprk_matter');
    expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });
});

describe('applyResolverFields — SRFR-052 display-name resolution', () => {
  it("writes target's sprk_mattername value to sprk_regardingrecordname when metadata is present", async () => {
    // Simulate the owner UAT bug scenario: picker hands back the Matter's
    // Primary Name (sprk_matternumber → "M-00042") as parentRecordName; the
    // resolver should REPLACE that with the true display name from
    // sprk_mattername ("Smith v Jones") sourced from the catalog.
    const webApi = buildMockWebApi({
      sprk_matter: {
        sprk_matternumber: 'M-00042',
        sprk_mattername: 'Smith v Jones',
      },
    });
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      // Simulate the picker returning the Primary Name (= sprk_matternumber
      // value) as the display name — this is the UAT bug. The resolver
      // should override this with the true display name.
      'M-00042',
      'matter'
    );

    // The resolved display name from the catalog wins over the picker fallback.
    expect(payload['sprk_regardingrecordname']).toBe('Smith v Jones');
    expect(result.displayName).toBe('Smith v Jones');
    // 5th field still populated as normal.
    expect(payload['sprk_regardingrecordnumber']).toBe('M-00042');
    expect(result.recordNumber).toBe('M-00042');
    expect(result.recordNumberSourceField).toBe('sprk_matternumber');
  });

  it('falls back to parentRecordName when metadata field is null (NFR-06 graceful)', async () => {
    // Simulate a fresh environment where sprk_recorddisplaynamefield is NOT
    // populated on the catalog row. The resolver should preserve the historical
    // behavior (write parentRecordName) rather than skipping the field entirely.
    const webApi: IPolymorphicWebApi = {
      retrieveMultipleRecords: jest.fn(async (entityLogicalName: string, query: string) => {
        if (entityLogicalName === 'sprk_recordtype_ref') {
          return {
            entities: [
              {
                sprk_recordtype_refid: 'rt-matter',
                sprk_recorddisplayname: 'Matter',
                sprk_regardingrecordnumberfield: 'sprk_matternumber',
                sprk_recorddisplaynamefield: null, // NOT populated yet
              },
            ],
          };
        }
        // Target-record query — number field only (display-name field excluded
        // from $select because its catalog value is null).
        const selectMatch = query.match(/\$select=([^&]+)/);
        const selectedFields = selectMatch ? selectMatch[1].split(',') : [];
        const record: Record<string, string | null> = {};
        for (const field of selectedFields) {
          if (field === 'sprk_matternumber') record[field] = 'M-00042';
        }
        return { entities: [record] };
      }),
    };
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Fallback Matter Name',
      'matter'
    );

    // Fell back to the picker-provided name (never left blank per NFR-06).
    expect(payload['sprk_regardingrecordname']).toBe('Fallback Matter Name');
    expect(result.displayName).toBe('Fallback Matter Name');
    // Number path still works independently.
    expect(payload['sprk_regardingrecordnumber']).toBe('M-00042');
    // Warn logged for diagnostics.
    expect(consoleWarnSpy).toHaveBeenCalledWith(
      expect.stringContaining('No sprk_recorddisplaynamefield mapping for "sprk_matter"')
    );
  });

  it("falls back to parentRecordName when target record's display-name value is null (NFR-06 graceful)", async () => {
    // Catalog resolves display-name field to `sprk_mattername`, but the target
    // record's value is null (e.g., freshly created Matter without a name).
    // Preserve the picker-provided fallback.
    const webApi = buildMockWebApi({
      sprk_matter: {
        sprk_matternumber: 'M-00042',
        sprk_mattername: null,
      },
    });
    const payload: Record<string, unknown> = {};

    const result = await applyResolverFields(
      webApi,
      payload,
      buildHostNavProps(),
      'sprk_matter',
      'sprk_matters',
      'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      'Picker Fallback',
      'matter'
    );

    expect(payload['sprk_regardingrecordname']).toBe('Picker Fallback');
    expect(result.displayName).toBe('Picker Fallback');
    expect(consoleWarnSpy).toHaveBeenCalledWith(expect.stringContaining('has null/empty "sprk_mattername"'));
  });
});
