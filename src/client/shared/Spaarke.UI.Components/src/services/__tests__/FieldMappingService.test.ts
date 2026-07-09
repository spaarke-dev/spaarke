/**
 * FieldMappingService — full engine unit-test suite (tasks 012/013/014/015)
 *
 * Closed-set coverage (task 015 — see the POML's CLOSED-SET COVERAGE
 * CHECKLIST; each item below maps to one or more `it()` blocks):
 *   1. Copy — scalar: assigns the source field's plain value to the target
 *      payload field.
 *   2. Copy — lookup (`@odata.bind`): resolves the source lookup's
 *      `_<field>_value` + `@Microsoft.Dynamics.CRM.lookuplogicalname`
 *      annotation, discovers the TARGET entity's nav-prop for that referent,
 *      and writes `${navProp}@odata.bind = /${entitySet}(${guid})` (the
 *      sprk_assignedattorney1 → contact worked example from the task POML).
 *   3. Default — writes the `defaultValue` literal to the target; an empty
 *      `defaultValue` warns and skips.
 *   4. Concat — resolves `{sprk_field}` placeholders in `rule.expression`
 *      against the parent record into a joined string.
 *   5. Template — the SAME placeholder resolver as Concat, exercised via a
 *      distinct expression/target to prove the Template dispatch branch
 *      independently invokes it (not just re-asserting Concat's case).
 *   6. Same-entity self-map (matter→matter, self-named rule) — applies as a
 *      real copy between two distinct records, never a same-name no-op.
 *   7. No `source === target` guard — a same-entity pair is not
 *      short-circuited: the BFF profile fetch and the apply/source-read path
 *      both still run.
 *   8. No-profile no-op — a BFF 404 returns
 *      `{ profileFound: false, fieldsMapped: [], warnings: [] }`, never
 *      throws.
 *   9. Missing-source-field warning — a Copy rule whose source field is
 *      absent from the parent record warns and skips (FR-09); other rules in
 *      the same profile still apply.
 *   10. Unresolved-placeholder warning — a Concat/Template token missing from
 *       the parent record is warned about and omitted (never left as the
 *       literal `"{sprk_field}"`, never thrown).
 *   11. Unresolvable-lookup warning — a Copy-lookup rule whose source lookup
 *       lacks the `lookuplogicalname` annotation (or the value itself) warns
 *       and skips.
 *   12. Never-throw — a failing rule (of any kind) warns but does not abort
 *       the invocation; every other rule in the same profile still applies,
 *       proving the payload is still creatable.
 *
 * Test doctrine: no `Mock<HttpMessageHandler>` for the BFF profile call — a
 * plain `jest.fn()` implementing `AuthenticatedFetchFn` stands in for
 * `authenticatedFetch` (ADR-038 targets HttpMessageHandler-style C# mocking;
 * the equivalent TS avoidance is not stubbing global `fetch` for BFF calls).
 * `global.fetch` IS stubbed for nav-prop discovery only, mirroring the
 * existing sibling pattern in
 * `CreateReportCardWizard/__tests__/reportCardService.resolver.test.ts`.
 *
 * @see ../FieldMappingService.ts
 * @see ../../types/FieldMappingTypes.ts
 * @see projects/set-regarding-and-field-mapping-resolver-r2/tasks/012-copy-engine-scalar-and-lookup.poml
 * @see projects/set-regarding-and-field-mapping-resolver-r2/tasks/013-default-concat-template-engines.poml
 * @see projects/set-regarding-and-field-mapping-resolver-r2/tasks/014-same-entity-support.poml
 * @see projects/set-regarding-and-field-mapping-resolver-r2/tasks/015-engine-unit-tests.poml
 */

import { applyFieldMappings } from '../FieldMappingService';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../EntityCreationService';
import { _resetNavPropCacheForTests } from '../PolymorphicResolverService';
import type { IFieldMappingRule } from '../../types/FieldMappingTypes';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SOURCE_ID = '11111111-1111-1111-1111-111111111111';
const ATTORNEY_CONTACT_ID = '{22222222-2222-2222-2222-222222222222}';
const ATTORNEY_CONTACT_ID_CLEAN = '22222222-2222-2222-2222-222222222222';

function makeRule(overrides: Partial<IFieldMappingRule>): IFieldMappingRule {
  return {
    id: overrides.id ?? 'rule-1',
    sourceField: overrides.sourceField ?? 'sprk_sourcefield',
    targetField: overrides.targetField ?? 'sprk_targetfield',
    sourceFieldType: overrides.sourceFieldType ?? 'Text',
    targetFieldType: overrides.targetFieldType ?? 'Text',
    priority: overrides.priority ?? 0,
    mappingType: overrides.mappingType ?? 'Copy',
    defaultValue: overrides.defaultValue ?? null,
    expression: overrides.expression ?? null,
    isRequired: overrides.isRequired ?? false,
    compatibilityMode: overrides.compatibilityMode ?? 'Strict',
  };
}

/**
 * Mock authenticatedFetch returning a fixed profile (one rule set per test).
 * Response stand-in mirrors the subset of the `Response` interface
 * `FieldMappingService.fetchProfile` touches (`ok`, `status`, `json()`) —
 * same pattern as `BffDataverseClient.test.ts`'s `makeResponse`.
 */
function makeAuthenticatedFetch(rules: IFieldMappingRule[]): AuthenticatedFetchFn {
  const body = {
    id: 'profile-1',
    name: 'Matter to Event (Attorney Matrix)',
    sourceEntity: 'sprk_matter',
    targetEntity: 'sprk_event',
    syncMode: 'OneTime',
    isActive: true,
    rules,
  };
  return jest.fn(async () => ({
    ok: true,
    status: 200,
    json: jest.fn().mockResolvedValue(body),
  })) as unknown as AuthenticatedFetchFn;
}

/**
 * Mock authenticatedFetch returning a fixed profile for an explicit
 * {sourceEntity, targetEntity} pair (task 014 — same-entity fixtures need the
 * profile body to actually say `sprk_matter`/`sprk_matter`, not the
 * cross-entity `sprk_matter`/`sprk_event` `makeAuthenticatedFetch` hardcodes).
 * Also usable directly as a `jest.fn()` so tests can assert on the call args
 * (URL, call count) to prove the fetch actually fired for the pair.
 */
function makeAuthenticatedFetchForPair(
  rules: IFieldMappingRule[],
  sourceEntity: string,
  targetEntity: string
): AuthenticatedFetchFn {
  const body = {
    id: 'profile-same-entity',
    name: `${sourceEntity} to ${targetEntity}`,
    sourceEntity,
    targetEntity,
    syncMode: 'OneTime',
    isActive: true,
    rules,
  };
  return jest.fn(async () => ({
    ok: true,
    status: 200,
    json: jest.fn().mockResolvedValue(body),
  })) as unknown as AuthenticatedFetchFn;
}

/**
 * Mock authenticatedFetch simulating "no profile configured" — a plain 404,
 * exactly as `fetchProfile` treats it (graceful no-op, no warning recorded).
 */
function makeAuthenticatedFetch404(): AuthenticatedFetchFn {
  return jest.fn(async () => ({
    ok: false,
    status: 404,
    json: jest.fn().mockResolvedValue(undefined),
    text: jest.fn().mockResolvedValue(''),
  })) as unknown as AuthenticatedFetchFn;
}

/** Mock IDataService whose retrieveRecord returns a fixed record + records call count/args. */
function makeDataService(
  record: Record<string, unknown>
): IDataService & { _retrieveRecordCalls: Array<{ entityName: string; id: string; options?: string }> } {
  const calls: Array<{ entityName: string; id: string; options?: string }> = [];
  return {
    _retrieveRecordCalls: calls,
    createRecord: jest.fn(async () => 'new-id'),
    retrieveRecord: jest.fn(async (entityName: string, id: string, options?: string) => {
      calls.push({ entityName, id, options });
      return record;
    }),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(async () => undefined),
    deleteRecord: jest.fn(async () => undefined),
  };
}

/** Stub global fetch for nav-prop discovery on the TARGET entity (sprk_event). */
function stubFetchNavProps() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => {
    if (url.includes("LogicalName='sprk_event'")) {
      return {
        ok: true,
        status: 200,
        json: async () => ({
          value: [
            {
              ReferencingAttribute: 'sprk_assignedattorney1',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney1',
              ReferencedEntity: 'contact',
            },
            {
              ReferencingAttribute: 'sprk_assignedattorney2',
              ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney2',
              ReferencedEntity: 'contact',
            },
          ],
        }),
      };
    }
    return { ok: false, status: 404, json: async () => ({ value: [] }) };
  });
}

beforeEach(() => {
  _resetNavPropCacheForTests();
  stubFetchNavProps();
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('FieldMappingService — Copy engine (task 012)', () => {
  it('scalar Copy assigns the source value to the target payload field', async () => {
    const rule = makeRule({
      sourceField: 'sprk_description',
      targetField: 'sprk_description',
      sourceFieldType: 'Memo',
      targetFieldType: 'Memo',
      mappingType: 'Copy',
    });
    const dataService = makeDataService({ sprk_description: 'Contract dispute matter' });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_description']).toBe('Contract dispute matter');
    expect(result.fieldsMapped).toEqual(['sprk_description']);
    expect(result.warnings).toEqual([]);
    expect(result.profileFound).toBe(true);

    // Fetch-once: exactly one retrieveRecord call, $select-ing the plain field name.
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
    expect(dataService._retrieveRecordCalls[0].options).toBe('?$select=sprk_description');
  });

  it('lookup Copy resolves the referent entity and writes navProp@odata.bind (sprk_assignedattorney1 -> contact)', async () => {
    const rule = makeRule({
      sourceField: 'sprk_assignedattorney1',
      targetField: 'sprk_assignedattorney1',
      sourceFieldType: 'Lookup',
      targetFieldType: 'Lookup',
      mappingType: 'Copy',
    });
    const dataService = makeDataService({
      '_sprk_assignedattorney1_value': ATTORNEY_CONTACT_ID,
      '_sprk_assignedattorney1_value@Microsoft.Dynamics.CRM.lookuplogicalname': 'contact',
    });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_AssignedAttorney1@odata.bind']).toBe(`/contacts(${ATTORNEY_CONTACT_ID_CLEAN})`);
    expect(result.fieldsMapped).toEqual(['sprk_assignedattorney1']);
    expect(result.warnings).toEqual([]);

    // Fetch-once: exactly one retrieveRecord call, $select-ing the OData `_value` form.
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
    expect(dataService._retrieveRecordCalls[0].options).toBe('?$select=_sprk_assignedattorney1_value');
  });

  it('unresolvable lookup (missing lookuplogicalname annotation) warns and skips — never throws', async () => {
    const rule = makeRule({
      sourceField: 'sprk_assignedattorney1',
      targetField: 'sprk_assignedattorney1',
      sourceFieldType: 'Lookup',
      targetFieldType: 'Lookup',
      mappingType: 'Copy',
    });
    // Record has the GUID but NO lookuplogicalname annotation (e.g. a Web API
    // Prefer header that only includes FormattedValue, not lookuplogicalname —
    // the exact BFF-adapter gap documented in the source read).
    const dataService = makeDataService({
      '_sprk_assignedattorney1_value': ATTORNEY_CONTACT_ID,
    });
    const payload: Record<string, unknown> = {};

    let thrown: unknown = null;
    let result;
    try {
      result = await applyFieldMappings({
        sourceEntity: 'sprk_matter',
        sourceId: SOURCE_ID,
        targetEntity: 'sprk_event',
        payload,
        dataService,
        authenticatedFetch: makeAuthenticatedFetch([rule]),
        bffBaseUrl: 'https://bff.example.com',
      });
    } catch (err) {
      thrown = err;
    }

    expect(thrown).toBeNull();
    expect(result!.fieldsMapped).toEqual([]);
    expect(result!.warnings).toHaveLength(1);
    expect(result!.warnings[0]).toMatch(/could not resolve the referent entity/i);
    expect('sprk_AssignedAttorney1@odata.bind' in payload).toBe(false);
  });

  it('multiple Copy rules (scalar + lookup) share ONE retrieveRecord call', async () => {
    const scalarRule = makeRule({
      id: 'rule-scalar',
      sourceField: 'sprk_description',
      targetField: 'sprk_description',
      sourceFieldType: 'Memo',
      targetFieldType: 'Memo',
      mappingType: 'Copy',
      priority: 1,
    });
    const lookupRule = makeRule({
      id: 'rule-lookup',
      sourceField: 'sprk_assignedattorney1',
      targetField: 'sprk_assignedattorney1',
      sourceFieldType: 'Lookup',
      targetFieldType: 'Lookup',
      mappingType: 'Copy',
      priority: 2,
    });
    const dataService = makeDataService({
      sprk_description: 'Contract dispute matter',
      '_sprk_assignedattorney1_value': ATTORNEY_CONTACT_ID,
      '_sprk_assignedattorney1_value@Microsoft.Dynamics.CRM.lookuplogicalname': 'contact',
    });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([scalarRule, lookupRule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(result.fieldsMapped.sort()).toEqual(['sprk_assignedattorney1', 'sprk_description']);
    expect(payload['sprk_description']).toBe('Contract dispute matter');
    expect(payload['sprk_AssignedAttorney1@odata.bind']).toBe(`/contacts(${ATTORNEY_CONTACT_ID_CLEAN})`);

    // The critical "fetch once" assertion: ONE retrieveRecord call total, with
    // a combined $select covering both rules.
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
    const select = dataService._retrieveRecordCalls[0].options ?? '';
    expect(select).toContain('sprk_description');
    expect(select).toContain('_sprk_assignedattorney1_value');
  });
});

describe('FieldMappingService — Default/Concat/Template engines (task 013)', () => {
  it('Default rule writes the defaultValue literal to the target; no source fetch is made', async () => {
    const rule = makeRule({
      sourceField: 'sprk_unused',
      targetField: 'sprk_status',
      mappingType: 'Default',
      defaultValue: 'Draft',
    });
    const dataService = makeDataService({});
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_status']).toBe('Draft');
    expect(result.fieldsMapped).toEqual(['sprk_status']);
    expect(result.warnings).toEqual([]);

    // Default needs no source read at all.
    expect(dataService._retrieveRecordCalls).toHaveLength(0);
  });

  it('a Default rule with an empty defaultValue warns and skips', async () => {
    const rule = makeRule({
      targetField: 'sprk_status',
      mappingType: 'Default',
      defaultValue: null,
    });
    const dataService = makeDataService({});
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect('sprk_status' in payload).toBe(false);
    expect(result.fieldsMapped).toEqual([]);
    expect(result.warnings).toHaveLength(1);
    expect(result.warnings[0]).toMatch(/defaultValue is empty/i);
  });

  it('Concat/Template rule resolves "{sprk_matternumber} - {sprk_mattername}" to the joined parent values', async () => {
    const rule = makeRule({
      targetField: 'sprk_description',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Concat',
      expression: '{sprk_matternumber} - {sprk_mattername}',
    });
    const dataService = makeDataService({
      sprk_matternumber: 'M-1001',
      sprk_mattername: 'Acme v. Widget Co.',
    });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_description']).toBe('M-1001 - Acme v. Widget Co.');
    expect(result.fieldsMapped).toEqual(['sprk_description']);
    expect(result.warnings).toEqual([]);

    // Placeholder fields were parsed up front and included in the single $select.
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
    const select = dataService._retrieveRecordCalls[0].options ?? '';
    expect(select).toContain('sprk_matternumber');
    expect(select).toContain('sprk_mattername');
  });

  it('a Template rule behaves identically to Concat (shared resolveExpression)', async () => {
    const rule = makeRule({
      targetField: 'sprk_summary',
      sourceFieldType: 'Text',
      targetFieldType: 'Memo',
      mappingType: 'Template',
      expression: 'Matter {sprk_matternumber}',
    });
    const dataService = makeDataService({ sprk_matternumber: 'M-2002' });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_summary']).toBe('Matter M-2002');
    expect(result.warnings).toEqual([]);
  });

  it('an unresolved placeholder warns and is omitted from the output — never thrown, never left literal', async () => {
    const rule = makeRule({
      targetField: 'sprk_description',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Concat',
      expression: '{sprk_matternumber} - {sprk_missingfield}',
    });
    // Parent record has matternumber but NOT missingfield.
    const dataService = makeDataService({ sprk_matternumber: 'M-1001' });
    const payload: Record<string, unknown> = {};

    let thrown: unknown = null;
    let result;
    try {
      result = await applyFieldMappings({
        sourceEntity: 'sprk_matter',
        sourceId: SOURCE_ID,
        targetEntity: 'sprk_event',
        payload,
        dataService,
        authenticatedFetch: makeAuthenticatedFetch([rule]),
        bffBaseUrl: 'https://bff.example.com',
      });
    } catch (err) {
      thrown = err;
    }

    expect(thrown).toBeNull();
    // The unresolved token is OMITTED (empty string substitution), not left as "{sprk_missingfield}".
    expect(payload['sprk_description']).toBe('M-1001 - ');
    expect((payload['sprk_description'] as string)).not.toContain('{sprk_missingfield}');
    expect(result!.fieldsMapped).toEqual(['sprk_description']);
    expect(result!.warnings).toHaveLength(1);
    expect(result!.warnings[0]).toMatch(/sprk_missingfield.*could not be resolved/i);
  });

  it('a Concat/Template rule targeting a Lookup warns and skips (a format string cannot bind a lookup)', async () => {
    const rule = makeRule({
      targetField: 'sprk_assignedattorney1',
      sourceFieldType: 'Text',
      targetFieldType: 'Lookup',
      mappingType: 'Concat',
      expression: '{sprk_matternumber}',
    });
    const dataService = makeDataService({ sprk_matternumber: 'M-1001' });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([rule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect('sprk_assignedattorney1' in payload).toBe(false);
    expect(result.fieldsMapped).toEqual([]);
    expect(result.warnings).toHaveLength(1);
    expect(result.warnings[0]).toMatch(/cannot bind a Lookup target/i);
  });

  it('Copy + Concat rules in the same profile share ONE combined source fetch', async () => {
    const copyRule = makeRule({
      id: 'rule-copy',
      sourceField: 'sprk_description',
      targetField: 'sprk_description',
      sourceFieldType: 'Memo',
      targetFieldType: 'Memo',
      mappingType: 'Copy',
      priority: 1,
    });
    const concatRule = makeRule({
      id: 'rule-concat',
      targetField: 'sprk_summary',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Concat',
      expression: '{sprk_matternumber} - {sprk_mattername}',
      priority: 2,
    });
    const dataService = makeDataService({
      sprk_description: 'Contract dispute matter',
      sprk_matternumber: 'M-1001',
      sprk_mattername: 'Acme v. Widget Co.',
    });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([copyRule, concatRule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_description']).toBe('Contract dispute matter');
    expect(payload['sprk_summary']).toBe('M-1001 - Acme v. Widget Co.');
    expect(result.fieldsMapped.sort()).toEqual(['sprk_description', 'sprk_summary']);
    expect(result.warnings).toEqual([]);

    // The critical cross-task coordination assertion: still exactly ONE
    // retrieveRecord call, its $select spanning both the Copy field and the
    // Concat rule's placeholder fields.
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
    const select = dataService._retrieveRecordCalls[0].options ?? '';
    expect(select).toContain('sprk_description');
    expect(select).toContain('sprk_matternumber');
    expect(select).toContain('sprk_mattername');
  });

  it('when the shared batch fetch fails, a Concat/Template rule skips silently (no per-placeholder warning spam)', async () => {
    const rule = makeRule({
      targetField: 'sprk_description',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Concat',
      expression: '{sprk_matternumber} - {sprk_mattername}',
    });
    const dataService: IDataService & { _retrieveRecordCalls: unknown[] } = {
      _retrieveRecordCalls: [],
      createRecord: jest.fn(async () => 'new-id'),
      retrieveRecord: jest.fn(async () => {
        throw new Error('Dataverse unavailable');
      }),
      retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
      updateRecord: jest.fn(async () => undefined),
      deleteRecord: jest.fn(async () => undefined),
    };
    const payload: Record<string, unknown> = {};

    let thrown: unknown = null;
    let result;
    try {
      result = await applyFieldMappings({
        sourceEntity: 'sprk_matter',
        sourceId: SOURCE_ID,
        targetEntity: 'sprk_event',
        payload,
        dataService,
        authenticatedFetch: makeAuthenticatedFetch([rule]),
        bffBaseUrl: 'https://bff.example.com',
      });
    } catch (err) {
      thrown = err;
    }

    expect(thrown).toBeNull();
    expect('sprk_description' in payload).toBe(false);
    expect(result!.fieldsMapped).toEqual([]);
    // Exactly ONE warning (the shared fetch-failure root cause) — NOT one
    // root-cause warning plus one per unresolved placeholder.
    expect(result!.warnings).toHaveLength(1);
    expect(result!.warnings[0]).toMatch(/Failed to fetch source record/i);
  });
});

describe('FieldMappingService — same-entity support (task 014)', () => {
  it('a same-entity (matter -> matter) self-named Copy rule applies the parent value to the target payload — not a no-op', async () => {
    // sourceEntity === targetEntity === 'sprk_matter', AND sourceField ===
    // targetField === 'sprk_practicearea'. The source record and the target
    // (create) payload are two DIFFERENT records, so this must be a real
    // copy — never skipped as a same-name "no-op".
    const rule = makeRule({
      sourceField: 'sprk_practicearea',
      targetField: 'sprk_practicearea',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Copy',
    });
    const dataService = makeDataService({ sprk_practicearea: 'Litigation' });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_matter',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetchForPair([rule], 'sprk_matter', 'sprk_matter'),
      bffBaseUrl: 'https://bff.example.com',
    });

    expect(payload['sprk_practicearea']).toBe('Litigation');
    expect(result.fieldsMapped).toEqual(['sprk_practicearea']);
    expect(result.warnings).toEqual([]);
    expect(result.profileFound).toBe(true);
  });

  it('a same-entity pair (sourceEntity === targetEntity) is NOT short-circuited: the BFF profile fetch still fires and the apply path still runs', async () => {
    const rule = makeRule({
      sourceField: 'sprk_practicearea',
      targetField: 'sprk_practicearea',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Copy',
    });
    const dataService = makeDataService({ sprk_practicearea: 'Litigation' });
    const payload: Record<string, unknown> = {};
    const fetchMock = makeAuthenticatedFetchForPair([rule], 'sprk_matter', 'sprk_matter');

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_matter',
      payload,
      dataService,
      authenticatedFetch: fetchMock,
      bffBaseUrl: 'https://bff.example.com',
    });

    // The BFF profile fetch DID fire for this same-entity pair — proving
    // there is no `sourceEntity === targetEntity` guard short-circuiting
    // before the fetch. The URL carries the same entity logical name on both
    // path segments (`.../profiles/sprk_matter/sprk_matter`).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const calledUrl = (fetchMock as unknown as jest.Mock).mock.calls[0][0] as string;
    expect(calledUrl).toContain('/profiles/sprk_matter/sprk_matter');

    // The apply path ran to completion (not skipped): a real rule was
    // mapped and the source record was actually read via the engine's
    // normal single-fetch path — nothing about same-entity short-circuits it.
    expect(result.profileFound).toBe(true);
    expect(result.fieldsMapped.length).toBeGreaterThan(0);
    expect(dataService._retrieveRecordCalls).toHaveLength(1);
  });
});

describe('FieldMappingService — graceful degradation (task 015)', () => {
  it('no profile configured (BFF 404) is a graceful no-op: profileFound false, empty arrays, never throws', async () => {
    const dataService = makeDataService({});
    const payload: Record<string, unknown> = {};
    const fetchMock = makeAuthenticatedFetch404();

    let thrown: unknown = null;
    let result;
    try {
      result = await applyFieldMappings({
        sourceEntity: 'sprk_matter',
        sourceId: SOURCE_ID,
        targetEntity: 'sprk_invoice',
        payload,
        dataService,
        authenticatedFetch: fetchMock,
        bffBaseUrl: 'https://bff.example.com',
      });
    } catch (err) {
      thrown = err;
    }

    expect(thrown).toBeNull();
    expect(result).toEqual({ profileFound: false, fieldsMapped: [], warnings: [] });
    // No profile is the expected "not configured" signal, not an error condition
    // — no diagnostic warning is recorded (mirrors applyResolverFields NFR-06).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    // No source record was read — the engine never got past the profile fetch.
    expect(dataService._retrieveRecordCalls).toHaveLength(0);
  });

  it('a Copy rule whose source field is absent from the parent record warns and skips (FR-09) — other rules still apply', async () => {
    const missingFieldRule = makeRule({
      id: 'rule-missing',
      sourceField: 'sprk_doesnotexist',
      targetField: 'sprk_target1',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Copy',
      priority: 1,
    });
    const okRule = makeRule({
      id: 'rule-ok',
      sourceField: 'sprk_description',
      targetField: 'sprk_target2',
      sourceFieldType: 'Memo',
      targetFieldType: 'Memo',
      mappingType: 'Copy',
      priority: 2,
    });
    // The mocked parent record genuinely has no "sprk_doesnotexist" key (e.g. a
    // stale/renamed field reference in the rule config), but DOES have
    // "sprk_description" for the sibling rule.
    const dataService = makeDataService({ sprk_description: 'Contract dispute matter' });
    const payload: Record<string, unknown> = {};

    const result = await applyFieldMappings({
      sourceEntity: 'sprk_matter',
      sourceId: SOURCE_ID,
      targetEntity: 'sprk_event',
      payload,
      dataService,
      authenticatedFetch: makeAuthenticatedFetch([missingFieldRule, okRule]),
      bffBaseUrl: 'https://bff.example.com',
    });

    // The missing-field rule is skipped, not written as `undefined`.
    expect('sprk_target1' in payload).toBe(false);
    expect(result.warnings).toHaveLength(1);
    expect(result.warnings[0]).toMatch(/sprk_doesnotexist.*missing from the parent record/i);

    // The sibling rule still applied — the missing-field failure did not abort
    // the invocation.
    expect(payload['sprk_target2']).toBe('Contract dispute matter');
    expect(result.fieldsMapped).toEqual(['sprk_target2']);
  });

  it('never-throw: a profile mixing a failing Copy-lookup rule, a failing Copy-scalar rule, and a succeeding Default rule completes without throwing and still applies the succeeding rule', async () => {
    const unresolvableLookupRule = makeRule({
      id: 'rule-bad-lookup',
      sourceField: 'sprk_assignedattorney1',
      targetField: 'sprk_assignedattorney1',
      sourceFieldType: 'Lookup',
      targetFieldType: 'Lookup',
      mappingType: 'Copy',
      priority: 1,
    });
    const missingFieldRule = makeRule({
      id: 'rule-bad-scalar',
      sourceField: 'sprk_doesnotexist',
      targetField: 'sprk_target1',
      sourceFieldType: 'Text',
      targetFieldType: 'Text',
      mappingType: 'Copy',
      priority: 2,
    });
    const defaultRule = makeRule({
      id: 'rule-default',
      targetField: 'sprk_status',
      mappingType: 'Default',
      defaultValue: 'Draft',
      priority: 3,
    });
    // Record has the lookup GUID but no lookuplogicalname annotation (unresolvable
    // lookup) and no "sprk_doesnotexist" key (missing scalar source field).
    const dataService = makeDataService({
      '_sprk_assignedattorney1_value': ATTORNEY_CONTACT_ID,
    });
    const payload: Record<string, unknown> = {};

    let thrown: unknown = null;
    let result;
    try {
      result = await applyFieldMappings({
        sourceEntity: 'sprk_matter',
        sourceId: SOURCE_ID,
        targetEntity: 'sprk_event',
        payload,
        dataService,
        authenticatedFetch: makeAuthenticatedFetch([unresolvableLookupRule, missingFieldRule, defaultRule]),
        bffBaseUrl: 'https://bff.example.com',
      });
    } catch (err) {
      thrown = err;
    }

    expect(thrown).toBeNull();
    // Both failing rules recorded a warning and were skipped...
    expect(result!.warnings).toHaveLength(2);
    expect('sprk_AssignedAttorney1@odata.bind' in payload).toBe(false);
    expect('sprk_target1' in payload).toBe(false);
    // ...but the Default rule still applied — the payload is still creatable.
    expect(payload['sprk_status']).toBe('Draft');
    expect(result!.fieldsMapped).toEqual(['sprk_status']);
    expect(result!.profileFound).toBe(true);
  });
});
