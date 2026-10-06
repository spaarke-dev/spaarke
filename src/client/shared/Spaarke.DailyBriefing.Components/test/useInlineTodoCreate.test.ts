/**
 * Unit tests for `useInlineTodoCreate` — the primary-contact wiring hook
 * (R7 W12 feedback item 7 / task 035 hardening, per notes/inbound-from-r7/03
 * item 2).
 *
 * `computeDueDate` (also exported from this module) already has coverage in
 * `useInlineTodoCreate.computeDueDate.test.ts`; this file covers the hook
 * itself — specifically the primary-contact resolve sequence
 * (retrieveMultipleRecords/retrieveRecord → sprk_AssignedTo@odata.bind) that
 * shipped untested.
 *
 * `@spaarke/ui-components/services` is routed via jest.config's
 * moduleNameMapper to `test/__mocks__/spaarke-ui-components-services.ts`,
 * which exports an EMPTY `TODO_REGARDING_CATALOG`. That means every test
 * item's `regardingEntityType` misses the catalog lookup and the hook takes
 * its documented "not in catalog — create without association" branch; the
 * multi-entity regarding resolution itself (ADR-024) is out of scope here.
 *
 * Covers:
 *   - Primary-contact resolve path: lookup finds a contact →
 *     sprk_AssignedTo@odata.bind is set on the created record.
 *   - No-record path: lookup succeeds but returns no contact → record is
 *     created WITHOUT sprk_AssignedTo@odata.bind (fail-soft).
 *   - No userId supplied: lookup is skipped entirely.
 *   - Primary-contact lookup rejects: fails soft, todo is still created.
 *   - Failure path: createRecord rejects → status becomes 'error', getError
 *     surfaces the message.
 *   - webApi null: createTodo is a no-op.
 */

import { renderHook, act } from '@testing-library/react';
import { useInlineTodoCreate } from '../src/hooks/useInlineTodoCreate';
import type { IWebApi, NotificationItem } from '../src/types/notifications';

/**
 * UAC-r2 task 147 r1: the hook now creates the To Do through the BFF (its default). These tests inject a creator that
 * delegates to the webApi stub, so the payload assertions below still read what the hook built.
 */
function viaWebApi(webApi: IWebApi) {
  return async (table: string, payload: Record<string, unknown>): Promise<string> =>
    ((await webApi.createRecord(table, payload)) as { id: string }).id;
}

function makeItem(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'n-1',
    title: 'Review motion to dismiss',
    body: 'Motion is overdue.',
    category: 'tasks-overdue',
    priority: 'high',
    actionUrl: '/main.aspx?etc=1&id=abc',
    regardingName: 'Acme Matter',
    regardingEntityType: 'sprk_matter',
    regardingId: '11111111-1111-1111-1111-111111111111',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate: null,
    ...overrides,
  };
}

function makeWebApi(overrides: Partial<IWebApi> = {}): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn().mockResolvedValue({ id: 'todo-1' }),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
    ...overrides,
  };
}

describe('useInlineTodoCreate', () => {
  beforeEach(() => {
    // Silence the hook's expected info/warn/error logging noise for the
    // fail-soft / failure-path tests below.
    jest.spyOn(console, 'error').mockImplementation(() => {});
    jest.spyOn(console, 'info').mockImplementation(() => {});
    jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('primary-contact resolve path: binds sprk_AssignedTo@odata.bind when the lookup finds a contact', async () => {
    const webApi = makeWebApi({
      retrieveRecord: jest.fn().mockResolvedValue({ _sprk_primarycontact_value: 'contact-123' }),
    });
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1', viaWebApi(webApi)));
    const item = makeItem();

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(webApi.retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-1', '?$select=_sprk_primarycontact_value');
    expect(webApi.createRecord).toHaveBeenCalledWith(
      'sprk_todo',
      expect.objectContaining({ 'sprk_AssignedTo@odata.bind': '/contacts(contact-123)' })
    );
    expect(result.current.isCreated(item.id)).toBe(true);
    expect(result.current.getCreatedId(item.id)).toBe('todo-1');
  });

  it('no-record path: lookup succeeds but returns no contact — record is created WITHOUT sprk_AssignedTo@odata.bind', async () => {
    const webApi = makeWebApi({
      retrieveRecord: jest.fn().mockResolvedValue({}), // no _sprk_primarycontact_value field
    });
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1', viaWebApi(webApi)));
    const item = makeItem({ id: 'n-2' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    const [, record] = (webApi.createRecord as jest.Mock).mock.calls[0];
    expect(record).not.toHaveProperty('sprk_AssignedTo@odata.bind');
    expect(result.current.isCreated(item.id)).toBe(true);
  });

  it('no userId supplied: skips the primary-contact lookup entirely and creates without assignment', async () => {
    const webApi = makeWebApi();
    const { result } = renderHook(() => useInlineTodoCreate(webApi, undefined, viaWebApi(webApi)));
    const item = makeItem({ id: 'n-3' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(webApi.retrieveRecord).not.toHaveBeenCalled();
    const [, record] = (webApi.createRecord as jest.Mock).mock.calls[0];
    expect(record).not.toHaveProperty('sprk_AssignedTo@odata.bind');
  });

  it('primary-contact lookup rejects: fails soft — sprk_AssignedTo left unset, todo is still created', async () => {
    const webApi = makeWebApi({
      retrieveRecord: jest.fn().mockRejectedValue(new Error('lookup failed')),
    });
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1', viaWebApi(webApi)));
    const item = makeItem({ id: 'n-4' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    const [, record] = (webApi.createRecord as jest.Mock).mock.calls[0];
    expect(record).not.toHaveProperty('sprk_AssignedTo@odata.bind');
    expect(result.current.isCreated(item.id)).toBe(true);
  });

  it('failure path: createRecord rejects — status becomes error and getError surfaces the message', async () => {
    const webApi = makeWebApi({
      createRecord: jest.fn().mockRejectedValue(new Error('Dataverse rejected create')),
    });
    const { result } = renderHook(() => useInlineTodoCreate(webApi, undefined, viaWebApi(webApi)));
    const item = makeItem({ id: 'n-5' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(result.current.isCreated(item.id)).toBe(false);
    expect(result.current.isPending(item.id)).toBe(false);
    expect(result.current.getError(item.id)).toBe('Dataverse rejected create');
    expect(result.current.getCreatedId(item.id)).toBeUndefined();
  });

  it('UAC-r2 task 147 r1: by default the To Do is created through the BFF (G5), never through webApi.createRecord', async () => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const services = require('@spaarke/ui-components/services') as { createChildRecordViaBff: jest.Mock };
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const auth = require('@spaarke/auth') as { authenticatedFetch: jest.Mock };
    services.createChildRecordViaBff.mockClear();
    const webApi = makeWebApi();
    const { result } = renderHook(() => useInlineTodoCreate(webApi));
    const item = makeItem({ id: 'n-7' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(webApi.createRecord).not.toHaveBeenCalled();
    expect(services.createChildRecordViaBff).toHaveBeenCalledTimes(1);
    const [fetchFn, base, table, record] = services.createChildRecordViaBff.mock.calls[0];
    expect(fetchFn).toBe(auth.authenticatedFetch);
    expect(base).toBe('');
    expect(table).toBe('sprk_todo');
    expect(record).not.toHaveProperty('ownerid@odata.bind');
    expect(result.current.getCreatedId(item.id)).toBe('bff-todo-1');
  });

  it('UAC-r2 task 147 r1: a BFF refusal surfaces the server message on the item', async () => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const services = require('@spaarke/ui-components/services') as { createChildRecordViaBff: jest.Mock };
    services.createChildRecordViaBff.mockRejectedValueOnce(
      new Error('A record this to-do is filed under was not found. The to-do was not saved.')
    );
    const { result } = renderHook(() => useInlineTodoCreate(makeWebApi()));
    const item = makeItem({ id: 'n-8' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(result.current.isCreated(item.id)).toBe(false);
    expect(result.current.getError(item.id)).toBe(
      'A record this to-do is filed under was not found. The to-do was not saved.'
    );
  });

  it('webApi is null: createTodo is a no-op and no state is mutated', async () => {
    const { result } = renderHook(() => useInlineTodoCreate(null));
    const item = makeItem({ id: 'n-6' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(result.current.isCreated(item.id)).toBe(false);
    expect(result.current.isPending(item.id)).toBe(false);
    expect(result.current.getError(item.id)).toBeUndefined();
  });
});
