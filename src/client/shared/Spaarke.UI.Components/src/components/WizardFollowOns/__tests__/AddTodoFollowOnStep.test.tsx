/**
 * AddTodoFollowOnStep — create path (createTodoRegardingChild).
 *
 * Behavioral test of the net-new Add-To-Do follow-on create handler (ADR-038):
 * it exercises the REAL create path — TodoService.createTodo → the shared
 * ADR-024 `applyResolverFields` primitive → IDataService.createRecord — and
 * asserts on the actual `createRecord` payload shape. It proves the created To
 * Do is regarding the JUST-CREATED CHILD record (not the host), with the
 * entity-specific `@odata.bind` lookup + resolver fields populated.
 *
 * Collaborators are injected: a jest.fn()-backed IDataService and a mocked
 * global.fetch for `sprk_todo` nav-prop discovery. No Mock<HttpMessageHandler>,
 * no DI-registration test, no ctor null-check test.
 *
 * @see steps/AddTodoFollowOnStep.tsx — createTodoRegardingChild
 * @see CreateTodoWizard/todoService.ts — createTodo
 * @see services/PolymorphicResolverService.ts — applyResolverFields
 */
import { createTodoRegardingChild } from '../steps/AddTodoFollowOnStep';
import { EMPTY_TODO_FORM } from '../../CreateTodoWizard/formTypes';
import type { ICreateTodoFormState } from '../../CreateTodoWizard/formTypes';
import { _resetTodoServiceNavPropCacheForTests } from '../../CreateTodoWizard/todoService';
import {
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';
import type { IDataService } from '../../../types/serviceInterfaces';

// sprk_todo ManyToOne nav-props for the child (invoice) + resolver record-type.
const TODO_NAV_PROPS = {
  value: [
    {
      ReferencingAttribute: 'sprk_regardinginvoice',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingInvoice',
      ReferencedEntity: 'sprk_invoice',
    },
    {
      ReferencingAttribute: 'sprk_regardingrecordtype',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
      ReferencedEntity: 'sprk_recordtype_ref',
    },
  ],
};

function buildDataService(): { dataService: IDataService; createRecord: jest.Mock; retrieveMultiple: jest.Mock } {
  const createRecord = jest.fn().mockResolvedValue('new-todo-guid');
  const retrieveMultiple = jest.fn().mockImplementation((entity: string) => {
    if (entity === 'sprk_recordtype_ref') {
      // resolveRecordType + record-number/display-name field lookups all query
      // sprk_recordtype_ref. Return the type id + null source-field mappings
      // (graceful-blank → display name falls back to parentRecordName; number
      // write skipped). Keeps the resolver path exercised without a target read.
      return Promise.resolve({
        entities: [
          {
            sprk_recordtype_refid: 'rt-invoice-guid',
            sprk_recorddisplayname: 'Invoice',
            sprk_regardingrecordnumberfield: null,
            sprk_recorddisplaynamefield: null,
          },
        ],
      });
    }
    return Promise.resolve({ entities: [] });
  });

  const dataService: IDataService = {
    createRecord,
    retrieveMultipleRecords: retrieveMultiple,
    retrieveRecord: jest.fn().mockResolvedValue({}),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
  return { dataService, createRecord, retrieveMultiple };
}

describe('createTodoRegardingChild (AddTodoFollowOnStep create path)', () => {
  beforeEach(() => {
    _resetTodoServiceNavPropCacheForTests();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    // Nav-prop discovery uses globalThis.fetch.
    (global as unknown as { fetch: jest.Mock }).fetch = jest.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => TODO_NAV_PROPS,
    });
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('creates a sprk_todo regarding the just-created child via applyResolverFields', async () => {
    const { dataService, createRecord } = buildDataService();

    const formValues: ICreateTodoFormState = {
      ...EMPTY_TODO_FORM,
      title: 'Follow-up review',
      notes: 'Check the invoice terms',
    };

    // The child is the record THIS wizard just created (a new invoice) — braces
    // + uppercase to prove the resolver normalizes the GUID.
    const child = {
      entityType: 'sprk_invoice',
      recordId: '{ABC-123-DEF}',
      recordName: 'INV-2026-0042',
    };

    const result = await createTodoRegardingChild(dataService, formValues, child);

    expect(result.success).toBe(true);
    expect(result.todoId).toBe('new-todo-guid');

    // Exactly one create, against sprk_todo (NEVER sprk_event).
    expect(createRecord).toHaveBeenCalledTimes(1);
    const [logicalName, payload] = createRecord.mock.calls[0];
    expect(logicalName).toBe('sprk_todo');

    // Core scalar fields.
    expect(payload.sprk_name).toBe('Follow-up review');
    expect(payload.sprk_notes).toBe('Check the invoice terms');

    // ADR-024 resolver fields — regarding the CHILD (normalized guid).
    expect(payload.sprk_regardingrecordid).toBe('abc-123-def');
    // Entity-specific lookup bind → the child invoice.
    expect(payload['sprk_RegardingInvoice@odata.bind']).toBe('/sprk_invoices(abc-123-def)');
    // Resolver record-type lookup bind.
    expect(payload['sprk_RegardingRecordType@odata.bind']).toBe('/sprk_recordtype_refs(rt-invoice-guid)');
    // Display name falls back to the picker-provided child name (graceful-blank).
    expect(payload.sprk_regardingrecordname).toBe('INV-2026-0042');
  });

  it('returns a failure result (no record created) for an unsupported regarding entity type', async () => {
    const { dataService, createRecord } = buildDataService();

    // sprk_kpiassessment is NOT in TODO_REGARDING_CATALOG — a To Do cannot be
    // regarding it. createTodo must fail gracefully, never throw, never create.
    const result = await createTodoRegardingChild(
      dataService,
      { ...EMPTY_TODO_FORM, title: 'Orphan todo' },
      { entityType: 'sprk_kpiassessment', recordId: 'kpi-1', recordName: 'KPI' }
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toMatch(/unsupported regarding entity type/i);
    expect(createRecord).not.toHaveBeenCalled();
  });
});
