/**
 * useInlineTodoCreate — primary-contact promise-cache tests (R5 task 037 / FR-C8).
 *
 * Guards the fix for notes/inbound-from-r7/03 item 4: primaryContactRef used to cache the
 * RESOLVED value, so two createTodo calls that race before the first lookup resolved BOTH saw
 * `undefined` and each issued a duplicate `retrieveRecord`. The fix caches the in-flight
 * Promise instead, so concurrent callers await the same lookup.
 *
 * `@spaarke/ui-components/services` is routed via jest.config moduleNameMapper to an empty
 * TODO_REGARDING_CATALOG mock (same as useInlineTodoCreate.test.ts), so the regarding step is
 * skipped and these tests focus purely on the primary-contact resolution.
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

describe('useInlineTodoCreate — primary-contact promise cache (R5 task 037 / FR-C8)', () => {
  beforeEach(() => {
    jest.spyOn(console, 'error').mockImplementation(() => {});
    jest.spyOn(console, 'info').mockImplementation(() => {});
    jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('resolves the primary-contact lookup exactly once under two concurrent createTodo calls', async () => {
    // Deferred retrieveRecord: stays pending until we release it, so the second createTodo
    // starts BEFORE the first has resolved the lookup — the exact race item 4 describes. A
    // resolved-value cache would fire two lookups here; the promise cache fires one.
    let releaseLookup: (v: Record<string, unknown>) => void = () => {};
    const lookupPromise = new Promise<Record<string, unknown>>(resolve => {
      releaseLookup = resolve;
    });
    const retrieveRecord = jest.fn().mockReturnValue(lookupPromise);
    const webApi = makeWebApi({ retrieveRecord });

    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1'));

    await act(async () => {
      // Kick off two creates concurrently — neither awaited before the other starts.
      const p1 = result.current.createTodo(makeItem({ id: 'n-1' }));
      const p2 = result.current.createTodo(makeItem({ id: 'n-2' }));
      // Release the single in-flight lookup; both creates proceed off the same promise.
      releaseLookup({ _sprk_primarycontact_value: 'contact-123' });
      await Promise.all([p1, p2]);
    });

    // The lookup was issued exactly once despite two concurrent creates.
    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    expect(retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-1', '?$select=_sprk_primarycontact_value');

    // Both todos were created, and both bound the same resolved contact.
    expect(webApi.createRecord).toHaveBeenCalledTimes(2);
    const boundContacts = (webApi.createRecord as jest.Mock).mock.calls.map(
      ([, record]) => (record as Record<string, unknown>)['sprk_AssignedTo@odata.bind']
    );
    expect(boundContacts).toEqual(['/contacts(contact-123)', '/contacts(contact-123)']);
  });

  it('caches the resolved contact across sequential creates — second create issues no new lookup', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({ _sprk_primarycontact_value: 'contact-abc' });
    const webApi = makeWebApi({ retrieveRecord });
    const { result } = renderHook(() => useInlineTodoCreate(webApi, 'user-1'));

    await act(async () => {
      await result.current.createTodo(makeItem({ id: 'n-1' }));
    });
    await act(async () => {
      await result.current.createTodo(makeItem({ id: 'n-2' }));
    });

    // One lookup total; the second create reuses the cached (already-resolved) promise.
    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    expect(webApi.createRecord).toHaveBeenCalledTimes(2);
  });
});
