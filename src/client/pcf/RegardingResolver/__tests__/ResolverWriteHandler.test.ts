/**
 * ResolverWriteHandler tests.
 *
 * Asserts the binding constraints:
 *   1. FR-21 / ADR-024 — `applyResolverFields` from `@spaarke/ui-components` is
 *      the SOLE write path. We mock it and verify it's called once per selection
 *      with the correct arguments.
 *   2. FR-22 — host entity is passed in. NO `sprk_todo` / `sprk_communication`
 *      literals in the handler. We verify by invoking the same handler with
 *      `hostEntity: 'sprk_todo'` AND `hostEntity: 'sprk_communication'` — both
 *      paths must succeed.
 *   3. FR-13 — clear-and-set: when entity X is selected, the OTHER 10
 *      entity-specific lookups must be present in the payload as
 *      `<navProp>@odata.bind = null`.
 *   4. New-record path: when `hostRecordId` is empty, the handler must NOT call
 *      `updateRecord` but must still return the payload for the caller to stage.
 */

import {
  applyRegardingSelection,
  clearRegarding,
  discoverHostNavProps,
  resolveAllowedCatalog,
  _resetNavPropCacheForTests,
} from '../RegardingResolver/handlers/ResolverWriteHandler';

// --- Mock @spaarke/ui-components ---
jest.mock('@spaarke/ui-components', () => {
  const TODO_REGARDING_CATALOG = [
    { entityType: 'sprk_matter', entitySet: 'sprk_matters', lookupAttribute: 'sprk_regardingmatter', navPropHint: 'matter' },
    { entityType: 'sprk_project', entitySet: 'sprk_projects', lookupAttribute: 'sprk_regardingproject', navPropHint: 'project' },
    { entityType: 'sprk_event', entitySet: 'sprk_events', lookupAttribute: 'sprk_regardingevent', navPropHint: 'event' },
    { entityType: 'sprk_communication', entitySet: 'sprk_communications', lookupAttribute: 'sprk_regardingcommunication', navPropHint: 'communication' },
    { entityType: 'sprk_workassignment', entitySet: 'sprk_workassignments', lookupAttribute: 'sprk_regardingworkassignment', navPropHint: 'workassignment' },
    { entityType: 'sprk_invoice', entitySet: 'sprk_invoices', lookupAttribute: 'sprk_regardinginvoice', navPropHint: 'invoice' },
    { entityType: 'sprk_budget', entitySet: 'sprk_budgets', lookupAttribute: 'sprk_regardingbudget', navPropHint: 'budget' },
    { entityType: 'sprk_analysis', entitySet: 'sprk_analyses', lookupAttribute: 'sprk_regardinganalysis', navPropHint: 'analysis' },
    { entityType: 'sprk_organization', entitySet: 'sprk_organizations', lookupAttribute: 'sprk_regardingorganization', navPropHint: 'organization' },
    { entityType: 'contact', entitySet: 'contacts', lookupAttribute: 'sprk_regardingcontact', navPropHint: 'contact' },
    { entityType: 'sprk_document', entitySet: 'sprk_documents', lookupAttribute: 'sprk_regardingdocument', navPropHint: 'document' },
  ];
  return {
    TODO_REGARDING_CATALOG,
    applyResolverFields: jest.fn(async (
      _webApi: unknown,
      entity: Record<string, unknown>,
      _navProps: unknown,
      parentEntityLogicalName: string,
      parentEntitySet: string,
      parentRecordId: string,
      parentRecordName: string,
      _entityLookupHint?: string
    ) => {
      // Mirror the real service's behavior at the payload level: set the
      // chosen entity-specific @odata.bind + 4 resolver fields.
      const cleanId = parentRecordId.replace(/[{}]/g, '').toLowerCase();
      entity[`mock_${parentEntityLogicalName}@odata.bind`] = `/${parentEntitySet}(${cleanId})`;
      entity['sprk_regardingrecordid'] = cleanId;
      entity['sprk_regardingrecordname'] = parentRecordName;
      entity['sprk_regardingrecordurl'] = `https://example.com/${parentEntityLogicalName}/${cleanId}`;
      entity['mock_recordtype@odata.bind'] = `/sprk_recordtype_refs(rt-${parentEntityLogicalName})`;
    }),
  };
});

import * as sharedLib from '@spaarke/ui-components';

const applyResolverFieldsMock = (sharedLib as unknown as {
  applyResolverFields: jest.Mock;
}).applyResolverFields;

describe('ResolverWriteHandler', () => {
  let mockUpdateRecord: jest.Mock;
  let mockRetrieveMultipleRecords: jest.Mock;
  let mockFetch: jest.Mock;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    applyResolverFieldsMock.mockClear();

    mockUpdateRecord = jest.fn().mockResolvedValue({ id: 'ok' });
    mockRetrieveMultipleRecords = jest.fn().mockResolvedValue({ entities: [] });
    mockFetch = jest.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ value: [] }),
    });
  });

  // -------------------------------------------------------------------------
  // FR-21 / ADR-024 — sole write path
  // -------------------------------------------------------------------------

  test('FR-21 — applyResolverFields is called exactly once per selection', async () => {
    const result = await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      {
        entityType: 'sprk_matter',
        recordId: '33333333-3333-3333-3333-333333333333',
        recordName: 'Smith v. Jones',
      },
      undefined,
      mockFetch as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(applyResolverFieldsMock).toHaveBeenCalledTimes(1);
    expect(applyResolverFieldsMock).toHaveBeenCalledWith(
      expect.anything(),
      expect.any(Object),
      expect.any(Array),
      'sprk_matter',
      'sprk_matters',
      '33333333-3333-3333-3333-333333333333',
      'Smith v. Jones',
      'matter'
    );
  });

  test('updateRecord is called with the full payload for existing records', async () => {
    await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      { entityType: 'sprk_matter', recordId: '33333333-3333-3333-3333-333333333333', recordName: 'X' },
      undefined,
      mockFetch as unknown as typeof fetch
    );

    expect(mockUpdateRecord).toHaveBeenCalledTimes(1);
    const [entity, recordId, payload] = mockUpdateRecord.mock.calls[0];
    expect(entity).toBe('sprk_todo');
    expect(recordId).toBe('22222222-2222-2222-2222-222222222222');
    expect(payload).toEqual(expect.objectContaining({
      'sprk_regardingrecordid': '33333333-3333-3333-3333-333333333333',
      'sprk_regardingrecordname': 'X',
    }));
  });

  test('rejects an unknown entity type', async () => {
    const result = await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      { entityType: 'unknown_entity', recordId: 'x', recordName: 'y' },
      undefined,
      mockFetch as unknown as typeof fetch
    );

    expect(result.success).toBe(false);
    expect(result.error).toMatch(/Unknown entity type/);
    expect(applyResolverFieldsMock).not.toHaveBeenCalled();
    expect(mockUpdateRecord).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // FR-13 — clear-and-set (10 other lookups nulled)
  // -------------------------------------------------------------------------

  test('FR-13 — payload nulls the 10 OTHER entity-specific lookups', async () => {
    await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      { entityType: 'sprk_matter', recordId: '33333333-3333-3333-3333-333333333333', recordName: 'X' },
      undefined,
      mockFetch as unknown as typeof fetch
    );

    const [, , payload] = mockUpdateRecord.mock.calls[0];
    // Each "other" entity should have its lookup attribute @odata.bind = null.
    const cleared = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    // 10 OTHER entity-specific lookups should be nulled (catalog has 11 entries; chose 1, so 10 are cleared).
    expect(cleared.length).toBeGreaterThanOrEqual(10);
  });

  // -------------------------------------------------------------------------
  // FR-22 — entity is a prop, no entity-specific branching
  // -------------------------------------------------------------------------

  test('FR-22 — same handler works for sprk_todo AND sprk_communication', async () => {
    // Same selection, two different host entities.
    const selection = {
      entityType: 'sprk_matter',
      recordId: '33333333-3333-3333-3333-333333333333',
      recordName: 'Same Matter',
    };

    const r1 = await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      selection,
      undefined,
      mockFetch as unknown as typeof fetch
    );
    expect(r1.success).toBe(true);

    const r2 = await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_communication',
        hostRecordId: '44444444-4444-4444-4444-444444444444',
      },
      selection,
      undefined,
      mockFetch as unknown as typeof fetch
    );
    expect(r2.success).toBe(true);

    expect(applyResolverFieldsMock).toHaveBeenCalledTimes(2);
    // updateRecord called with each host entity in turn.
    expect(mockUpdateRecord).toHaveBeenNthCalledWith(
      1,
      'sprk_todo',
      '22222222-2222-2222-2222-222222222222',
      expect.any(Object)
    );
    expect(mockUpdateRecord).toHaveBeenNthCalledWith(
      2,
      'sprk_communication',
      '44444444-4444-4444-4444-444444444444',
      expect.any(Object)
    );
  });

  // -------------------------------------------------------------------------
  // New-record (no hostRecordId) — staged for pre-save handler
  // -------------------------------------------------------------------------

  test('new-record path: returns payload but does NOT call updateRecord', async () => {
    const result = await applyRegardingSelection(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: undefined,
      },
      { entityType: 'sprk_matter', recordId: '33333333-3333-3333-3333-333333333333', recordName: 'X' },
      undefined,
      mockFetch as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(result.payload).toBeDefined();
    expect(applyResolverFieldsMock).toHaveBeenCalledTimes(1);
    expect(mockUpdateRecord).not.toHaveBeenCalled();
  });

  // -------------------------------------------------------------------------
  // resolveAllowedCatalog
  // -------------------------------------------------------------------------

  test('resolveAllowedCatalog returns full catalog when input is empty', () => {
    expect(resolveAllowedCatalog(undefined)).toHaveLength(11);
    expect(resolveAllowedCatalog('')).toHaveLength(11);
    expect(resolveAllowedCatalog(null)).toHaveLength(11);
  });

  test('resolveAllowedCatalog filters to the comma-separated list', () => {
    const filtered = resolveAllowedCatalog('sprk_matter,sprk_project, contact');
    expect(filtered.map(c => c.entityType).sort()).toEqual(['contact', 'sprk_matter', 'sprk_project']);
  });

  // -------------------------------------------------------------------------
  // clearRegarding
  // -------------------------------------------------------------------------

  test('clearRegarding produces a payload nulling all 14 nullable fields', async () => {
    const result = await clearRegarding(
      {
        webApi: { retrieveMultipleRecords: mockRetrieveMultipleRecords, updateRecord: mockUpdateRecord },
        hostEntity: 'sprk_todo',
        hostRecordId: '22222222-2222-2222-2222-222222222222',
      },
      mockFetch as unknown as typeof fetch
    );

    expect(result.success).toBe(true);
    expect(result.payload).toBeDefined();
    const payload = result.payload as Record<string, unknown>;

    // 11 entity-specific lookups + 1 record-type lookup, all @odata.bind = null
    const nulledBinds = Object.entries(payload).filter(
      ([k, v]) => k.endsWith('@odata.bind') && v === null
    );
    expect(nulledBinds.length).toBeGreaterThanOrEqual(12);

    // 3 text/URL fields explicitly null
    expect(payload['sprk_regardingrecordid']).toBeNull();
    expect(payload['sprk_regardingrecordname']).toBeNull();
    expect(payload['sprk_regardingrecordurl']).toBeNull();

    // updateRecord called
    expect(mockUpdateRecord).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // discoverHostNavProps cache
  // -------------------------------------------------------------------------

  test('discoverHostNavProps caches per host entity', async () => {
    const fetchSpy = jest.fn().mockResolvedValue({
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

    const a = await discoverHostNavProps('sprk_todo', fetchSpy as unknown as typeof fetch);
    const b = await discoverHostNavProps('sprk_todo', fetchSpy as unknown as typeof fetch);

    expect(a).toBe(b); // cached reference
    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  test('discoverHostNavProps returns empty array on HTTP error (graceful)', async () => {
    _resetNavPropCacheForTests();
    const fetchSpy = jest.fn().mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    });

    const result = await discoverHostNavProps('sprk_communication', fetchSpy as unknown as typeof fetch);
    expect(result).toEqual([]);
  });
});
