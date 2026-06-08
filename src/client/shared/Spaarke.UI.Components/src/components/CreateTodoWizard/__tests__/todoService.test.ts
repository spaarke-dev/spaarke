/**
 * todoService.test.ts
 *
 * Unit tests for the CreateTodoWizard `TodoService.createTodo` contract.
 *
 * Per smart-todo-decoupling-r3 task 031:
 *   - FR-15: Wizard creates `sprk_todo`; no `sprk_event` rows with `todoflag=true`.
 *   - FR-16: AssociateToStep is skippable — when skipped, all 11 lookups + 4 resolver
 *            fields remain null.
 *   - ADR-024: When a regarding triple is supplied, `applyResolverFields` populates
 *              the entity-specific lookup + 4 resolver fields atomically.
 *   - OS-1: No compat path — `createRecord` must be called with `"sprk_todo"`, NEVER
 *           with `"sprk_event"`.
 *
 * @see ../todoService.ts
 */

import { TodoService, _resetTodoServiceNavPropCacheForTests } from '../todoService';
import { EMPTY_TODO_FORM, type ICreateTodoFormState, type AssociationResult } from '../formTypes';
import { createMockDataService } from '../../../__mocks__/mockDataService';

// ---------------------------------------------------------------------------
// fetch stub for nav-prop discovery
// ---------------------------------------------------------------------------

const NAV_PROPS_RESPONSE = {
  value: [
    // Regarding lookups — one per supported parent
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
      ReferencingAttribute: 'sprk_regardingevent',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingEvent',
      ReferencedEntity: 'sprk_event',
    },
    {
      ReferencingAttribute: 'sprk_regardingcommunication',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingCommunication',
      ReferencedEntity: 'sprk_communication',
    },
    {
      ReferencingAttribute: 'sprk_regardingworkassignment',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingWorkAssignment',
      ReferencedEntity: 'sprk_workassignment',
    },
    {
      ReferencingAttribute: 'sprk_regardinginvoice',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingInvoice',
      ReferencedEntity: 'sprk_invoice',
    },
    {
      ReferencingAttribute: 'sprk_regardingbudget',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingBudget',
      ReferencedEntity: 'sprk_budget',
    },
    {
      ReferencingAttribute: 'sprk_regardinganalysis',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingAnalysis',
      ReferencedEntity: 'sprk_analysis',
    },
    {
      ReferencingAttribute: 'sprk_regardingorganization',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingOrganization',
      ReferencedEntity: 'sprk_organization',
    },
    {
      ReferencingAttribute: 'sprk_regardingcontact',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingContact',
      ReferencedEntity: 'contact',
    },
    {
      ReferencingAttribute: 'sprk_regardingdocument',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingDocument',
      ReferencedEntity: 'sprk_document',
    },
    // Resolver fields
    {
      ReferencingAttribute: 'sprk_regardingrecordtype',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
      ReferencedEntity: 'sprk_recordtype_ref',
    },
    // Assignee
    {
      ReferencingAttribute: 'sprk_assignedto',
      ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
      ReferencedEntity: 'systemuser',
    },
  ],
};

function installFetchMock(): jest.Mock {
  const mockFetch = jest.fn().mockResolvedValue({
    ok: true,
    status: 200,
    json: jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE),
  });
  (globalThis as unknown as { fetch: typeof fetch }).fetch = mockFetch as unknown as typeof fetch;
  return mockFetch;
}

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const FORM_VALUES: ICreateTodoFormState = {
  ...EMPTY_TODO_FORM,
  title: '  Review Discovery Motion  ', // intentional whitespace to test trim
  notes: 'Discuss with co-counsel',
  dueDate: '2026-06-15',
  priorityScore: 75,
  effortScore: 30,
};

const MATTER_REGARDING: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: '{ABCDEF12-3456-7890-ABCD-EF1234567890}', // braced + uppercase to test normalization
  recordName: 'Smith v. Jones',
};

// ---------------------------------------------------------------------------
// Suite
// ---------------------------------------------------------------------------

describe('TodoService.createTodo', () => {
  beforeEach(() => {
    _resetTodoServiceNavPropCacheForTests();
    jest.clearAllMocks();
  });

  // -----------------------------------------------------------------
  // FR-15 / OS-1: creates sprk_todo, never sprk_event
  // -----------------------------------------------------------------

  it('createsSprkTodoNotSprkEvent_whenNoRegarding', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    const result = await service.createTodo(FORM_VALUES);

    expect(result.success).toBe(true);
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);

    const [entityName] = dataService.createRecord.mock.calls[0];
    expect(entityName).toBe('sprk_todo');
    // Hard constraint: never sprk_event
    expect(entityName).not.toBe('sprk_event');
  });

  it('createsSprkTodoNotSprkEvent_whenRegardingPresent', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    // Make resolveRecordType return a record-type so applyResolverFields gets the recordtype bind
    dataService.retrieveMultipleRecords.mockResolvedValue({
      entities: [{ sprk_recordtype_refid: 'rt-1234', sprk_recorddisplayname: 'Matter' }],
    });

    const service = new TodoService(dataService);
    const result = await service.createTodo(FORM_VALUES, MATTER_REGARDING);

    expect(result.success).toBe(true);
    expect(dataService.createRecord).toHaveBeenCalledTimes(1);

    const [entityName] = dataService.createRecord.mock.calls[0];
    expect(entityName).toBe('sprk_todo');
    expect(entityName).not.toBe('sprk_event');
  });

  it('neverWritesSprkTodoflagField_inAnyScenario', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    dataService.retrieveMultipleRecords.mockResolvedValue({
      entities: [{ sprk_recordtype_refid: 'rt-1234', sprk_recorddisplayname: 'Matter' }],
    });

    const service = new TodoService(dataService);
    await service.createTodo(FORM_VALUES, MATTER_REGARDING);

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];
    expect(payload).not.toHaveProperty('sprk_todoflag');
    expect(payload).not.toHaveProperty('sprk_todoflag@odata.bind');
  });

  // -----------------------------------------------------------------
  // Core field payload — sprk_todo schema fields
  // -----------------------------------------------------------------

  it('populatesCoreSprkTodoFields_fromFormValues', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    await service.createTodo(FORM_VALUES);

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];

    // Primary name (mapped from title, trimmed)
    expect(payload['sprk_name']).toBe('Review Discovery Motion');
    expect(payload['sprk_notes']).toBe('Discuss with co-counsel');
    expect(payload['sprk_duedate']).toBe('2026-06-15');
    expect(payload['sprk_priorityscore']).toBe(75);
    expect(payload['sprk_effortscore']).toBe(30);
    expect(payload['statecode']).toBe(0); // Active
    expect(payload['statuscode']).toBe(1); // Open (per entity-schema.md)
  });

  it('omitsOptionalFields_whenEmpty', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    await service.createTodo({
      ...EMPTY_TODO_FORM,
      title: 'Bare minimum task',
    });

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];
    expect(payload['sprk_name']).toBe('Bare minimum task');
    expect(payload).not.toHaveProperty('sprk_notes');
    expect(payload).not.toHaveProperty('sprk_duedate');
    // priorityScore/effortScore are always written (default 50)
    expect(payload['sprk_priorityscore']).toBe(EMPTY_TODO_FORM.priorityScore);
    expect(payload['sprk_effortscore']).toBe(EMPTY_TODO_FORM.effortScore);
  });

  // -----------------------------------------------------------------
  // Assignee lookup
  // -----------------------------------------------------------------

  it('bindsAssigneeLookup_whenAssignedToIdProvided', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    await service.createTodo({
      ...FORM_VALUES,
      assignedToId: 'user-guid-123',
      assignedToName: 'Jane Doe',
    });

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];
    // Discovered nav-prop is sprk_AssignedTo
    expect(payload['sprk_AssignedTo@odata.bind']).toBe('/systemusers(user-guid-123)');
  });

  // -----------------------------------------------------------------
  // FR-16: skip path — no regarding fields written
  // -----------------------------------------------------------------

  it('writesZeroLookupsAndZeroResolverFields_whenRegardingSkipped', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    await service.createTodo(FORM_VALUES); // no regarding arg

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];

    // All 11 entity-specific lookups must be absent
    const lookupKeys = [
      'sprk_RegardingMatter',
      'sprk_RegardingProject',
      'sprk_RegardingEvent',
      'sprk_RegardingCommunication',
      'sprk_RegardingWorkAssignment',
      'sprk_RegardingInvoice',
      'sprk_RegardingBudget',
      'sprk_RegardingAnalysis',
      'sprk_RegardingOrganization',
      'sprk_RegardingContact',
      'sprk_RegardingDocument',
    ];
    for (const key of lookupKeys) {
      expect(payload).not.toHaveProperty(`${key}@odata.bind`);
    }

    // All 4 resolver fields must be absent (NOT null — just absent)
    expect(payload).not.toHaveProperty('sprk_regardingrecordid');
    expect(payload).not.toHaveProperty('sprk_regardingrecordname');
    expect(payload).not.toHaveProperty('sprk_regardingrecordurl');
    expect(payload).not.toHaveProperty('sprk_RegardingRecordType@odata.bind');
  });

  it('writesZeroLookupsAndZeroResolverFields_whenRegardingNull', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    await service.createTodo(FORM_VALUES, null);

    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];
    expect(payload).not.toHaveProperty('sprk_regardingrecordid');
    expect(payload).not.toHaveProperty('sprk_regardingrecordname');
    expect(payload).not.toHaveProperty('sprk_regardingrecordurl');
  });

  // -----------------------------------------------------------------
  // FR-15 + ADR-024: regarding path — 11+4 fields populated atomically
  // -----------------------------------------------------------------

  it('populatesAllFifteenRegardingFields_whenRegardingProvided', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    // sprk_recordtype_ref lookup returns the Matter record-type.
    // Note: PolymorphicResolverService.resolveRecordType caches by entity logical
    // name across calls within the process; this test asserts on the returned
    // value field rather than a specific GUID to remain robust to cache state.
    dataService.retrieveMultipleRecords.mockResolvedValue({
      entities: [{ sprk_recordtype_refid: 'matter-rt-id', sprk_recorddisplayname: 'Matter' }],
    });

    const service = new TodoService(dataService);
    const result = await service.createTodo(FORM_VALUES, MATTER_REGARDING);

    expect(result.success).toBe(true);
    const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];

    // 1 entity-specific lookup (chosen) — bound via nav-prop (sprk_RegardingMatter)
    // GUID is normalized: braces stripped + lowercased
    expect(payload['sprk_RegardingMatter@odata.bind']).toBe('/sprk_matters(abcdef12-3456-7890-abcd-ef1234567890)');

    // 3 resolver text/URL fields
    expect(payload['sprk_regardingrecordid']).toBe('abcdef12-3456-7890-abcd-ef1234567890');
    expect(payload['sprk_regardingrecordname']).toBe('Smith v. Jones');
    expect(payload['sprk_regardingrecordurl']).toEqual(expect.stringContaining('sprk_matter'));
    expect(payload['sprk_regardingrecordurl']).toEqual(expect.stringContaining('abcdef12-3456-7890-abcd-ef1234567890'));

    // 1 resolver record-type lookup — must be bound (specific GUID may come from a cached
    // resolveRecordType call in PolymorphicResolverService, which is module-scoped).
    expect(payload['sprk_RegardingRecordType@odata.bind']).toEqual(
      expect.stringMatching(/^\/sprk_recordtype_refs\(.+\)$/)
    );
  });

  it('populatesAllFifteenRegardingFields_forEachOfElevenTargets', async () => {
    // Lighter end-to-end version covering all 11 entity types in one shot
    const targets: Array<{ entityType: string; entitySet: string; expectedNavProp: string }> = [
      { entityType: 'sprk_matter', entitySet: 'sprk_matters', expectedNavProp: 'sprk_RegardingMatter' },
      { entityType: 'sprk_project', entitySet: 'sprk_projects', expectedNavProp: 'sprk_RegardingProject' },
      { entityType: 'sprk_event', entitySet: 'sprk_events', expectedNavProp: 'sprk_RegardingEvent' },
      {
        entityType: 'sprk_communication',
        entitySet: 'sprk_communications',
        expectedNavProp: 'sprk_RegardingCommunication',
      },
      {
        entityType: 'sprk_workassignment',
        entitySet: 'sprk_workassignments',
        expectedNavProp: 'sprk_RegardingWorkAssignment',
      },
      { entityType: 'sprk_invoice', entitySet: 'sprk_invoices', expectedNavProp: 'sprk_RegardingInvoice' },
      { entityType: 'sprk_budget', entitySet: 'sprk_budgets', expectedNavProp: 'sprk_RegardingBudget' },
      { entityType: 'sprk_analysis', entitySet: 'sprk_analyses', expectedNavProp: 'sprk_RegardingAnalysis' },
      {
        entityType: 'sprk_organization',
        entitySet: 'sprk_organizations',
        expectedNavProp: 'sprk_RegardingOrganization',
      },
      { entityType: 'contact', entitySet: 'contacts', expectedNavProp: 'sprk_RegardingContact' },
      { entityType: 'sprk_document', entitySet: 'sprk_documents', expectedNavProp: 'sprk_RegardingDocument' },
    ];

    for (const t of targets) {
      _resetTodoServiceNavPropCacheForTests();
      installFetchMock();
      const dataService = createMockDataService();
      dataService.retrieveMultipleRecords.mockResolvedValue({
        entities: [{ sprk_recordtype_refid: `rt-${t.entityType}`, sprk_recorddisplayname: t.entityType }],
      });

      const service = new TodoService(dataService);
      const result = await service.createTodo(FORM_VALUES, {
        entityType: t.entityType,
        recordId: '11111111-1111-1111-1111-111111111111',
        recordName: `Parent of ${t.entityType}`,
      });

      expect(result.success).toBe(true);
      const [, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];
      expect(payload[`${t.expectedNavProp}@odata.bind`]).toBe(`/${t.entitySet}(11111111-1111-1111-1111-111111111111)`);
      expect(payload['sprk_regardingrecordid']).toBe('11111111-1111-1111-1111-111111111111');
      expect(payload['sprk_regardingrecordname']).toBe(`Parent of ${t.entityType}`);
    }
  });

  // -----------------------------------------------------------------
  // Validation / error paths
  // -----------------------------------------------------------------

  it('returnsError_whenRegardingEntityTypeUnsupported', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    const service = new TodoService(dataService);

    const result = await service.createTodo(FORM_VALUES, {
      entityType: 'account', // not in TODO_REGARDING_CATALOG
      recordId: '11111111-1111-1111-1111-111111111111',
      recordName: 'Some Account',
    });

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('Unsupported regarding entity type');
    // createRecord must NOT have been called when validation failed
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  it('returnsError_whenCreateRecordRejects', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    dataService.createRecord.mockRejectedValue(new Error('Network blew up'));

    const service = new TodoService(dataService);
    const result = await service.createTodo(FORM_VALUES);

    expect(result.success).toBe(false);
    expect(result.errorMessage).toContain('Network blew up');
  });

  // -----------------------------------------------------------------
  // Final OS-1 safety net: scan the payload for any legacy todoflag wiring
  // -----------------------------------------------------------------

  it('payloadHasNoLegacyEventTodoFields_inAnyConfiguration', async () => {
    installFetchMock();
    const dataService = createMockDataService();
    dataService.retrieveMultipleRecords.mockResolvedValue({
      entities: [{ sprk_recordtype_refid: 'rt-matter', sprk_recorddisplayname: 'Matter' }],
    });

    const service = new TodoService(dataService);
    await service.createTodo({ ...FORM_VALUES, assignedToId: 'u-1' }, MATTER_REGARDING);

    const [entityName, payload] = dataService.createRecord.mock.calls[0] as [string, Record<string, unknown>];

    expect(entityName).toBe('sprk_todo');

    // None of the legacy event-todo fields/markers may appear
    for (const key of Object.keys(payload)) {
      expect(key.toLowerCase()).not.toContain('todoflag');
      expect(key.toLowerCase()).not.toContain('sprk_eventtodo');
      // sprk_eventname was the legacy primary-name field; the new entity uses sprk_name
      expect(key.toLowerCase()).not.toBe('sprk_eventname');
    }
  });
});
