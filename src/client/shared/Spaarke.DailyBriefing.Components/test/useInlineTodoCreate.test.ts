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
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1'));
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
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1'));
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
    const { result } = renderHook(() => useInlineTodoCreate(webApi));
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
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1'));
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
    const { result } = renderHook(() => useInlineTodoCreate(webApi));
    const item = makeItem({ id: 'n-5' });

    await act(async () => {
      await result.current.createTodo(item);
    });

    expect(result.current.isCreated(item.id)).toBe(false);
    expect(result.current.isPending(item.id)).toBe(false);
    expect(result.current.getError(item.id)).toBe('Dataverse rejected create');
    expect(result.current.getCreatedId(item.id)).toBeUndefined();
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
