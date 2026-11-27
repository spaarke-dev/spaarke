/**
 * The shared Smart To Do data layer (#2972): query builders, the preferences parser and hook, the service, and the
 * list hook. Ported from the SmartTodo code page's `queryHelpers.test.ts` and `useUserPreferences.test.ts` (whose
 * subjects moved here), plus the cases the consolidation fixed:
 *   - a threshold save keeps orientation, column order and unknown keys (the workspace copy dropped them);
 *   - two quick updates merge instead of overwriting each other;
 *   - createTodo goes through the injected create function and sets no owner.
 */

jest.mock('@spaarke/ui-components', () => ({
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
  ...jest.requireActual('../../Spaarke.UI.Components/src/components/TodoDetail/types'),
}));

import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { INQUIRY_TODO_CLOSE_MESSAGE } from '@spaarke/ui-components';
import { TodoDataverseService, type ITodoWebApi } from '../src/services';
import {
  buildActiveTodosQuery,
  buildTodoScopeClause,
  todoListScopeKey,
  type TodoListScope,
} from '../src/services/todoQueries';
import {
  DEFAULT_SMART_TODO_ORIENTATION,
  DEFAULT_SMART_TODO_VIEW_MODE,
  PREFERENCE_TYPE_TODO_KANBAN,
  parseTodoKanbanPreferences,
  useTodoKanbanPreferences,
  type IUseTodoKanbanPreferencesResult,
} from '../src/hooks/useTodoKanbanPreferences';
import { useTodoItems, type IUseTodoItemsResult } from '../src/hooks/useTodoItems';
import { DEFAULT_TODAY_THRESHOLD, DEFAULT_TOMORROW_THRESHOLD } from '../src/hooks/useKanbanColumns';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const CONTACT_ID = '11111111-1111-1111-1111-111111111111';
const USER_ID = '22222222-2222-2222-2222-222222222222';

// ---------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------

describe('buildActiveTodosQuery — assignedTo scope (SmartTodo code page)', () => {
  const scope: TodoListScope = { kind: 'assignedTo', contactId: CONTACT_ID };

  it('scopes to the assignee and Open / In Progress statuses', () => {
    const query = buildActiveTodosQuery(scope);
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
    expect(query).toContain('statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)');
    expect(query).not.toContain('statuscode eq 2');
    expect(query).not.toContain('_ownerid_value eq');
  });

  it('sorts by priority score desc then due date asc', () => {
    expect(decodeURIComponent(buildActiveTodosQuery(scope))).toContain(
      '$orderby=sprk_priorityscore desc,sprk_duedate asc'
    );
  });

  it('selects the union of both hosts’ fields (priority choice + regarding text)', () => {
    const query = buildActiveTodosQuery(scope);
    expect(query).toContain('sprk_priority,');
    expect(query).toContain('sprk_regardingrecordname');
    expect(query).toContain('sprk_regardingrecordnumber');
  });

  it('adds the entity-specific regarding clause for an openTodos launch', () => {
    const query = buildActiveTodosQuery({ ...scope, regardingFilter: { entityType: 'sprk_matter', recordId: 'm-1' } });
    expect(query).toContain('_sprk_regardingmatter_value eq m-1');
    expect(query).toContain(`_sprk_assignedto_value eq ${CONTACT_ID}`);
  });

  it('ignores an unsupported regarding entity type', () => {
    const query = buildActiveTodosQuery({
      ...scope,
      regardingFilter: { entityType: 'sprk_unknown_entity', recordId: 'x-1' },
    });
    expect(query).not.toContain('x-1');
  });
});

describe('buildTodoScopeClause — owner scope (LegalWorkspace)', () => {
  it('"my" (default) filters on the owner', () => {
    expect(buildTodoScopeClause({ kind: 'owner', userId: USER_ID })).toBe(`_ownerid_value eq ${USER_ID}`);
  });

  it('"all" with a business unit filters on the owning business unit', () => {
    expect(buildTodoScopeClause({ kind: 'owner', userId: USER_ID, scope: 'all', businessUnitId: 'bu-1' })).toBe(
      '_owningbusinessunit_value eq bu-1'
    );
  });

  it('"all" without a business unit falls back to the owner', () => {
    expect(buildTodoScopeClause({ kind: 'owner', userId: USER_ID, scope: 'all' })).toBe(`_ownerid_value eq ${USER_ID}`);
  });
});

describe('todoListScopeKey', () => {
  it('is equal for equal scopes and differs when the regarding record changes', () => {
    const a: TodoListScope = {
      kind: 'assignedTo',
      contactId: 'c',
      regardingFilter: { entityType: 'sprk_matter', recordId: '1' },
    };
    const b: TodoListScope = {
      kind: 'assignedTo',
      contactId: 'c',
      regardingFilter: { entityType: 'sprk_matter', recordId: '1' },
    };
    const c: TodoListScope = {
      kind: 'assignedTo',
      contactId: 'c',
      regardingFilter: { entityType: 'sprk_matter', recordId: '2' },
    };
    expect(todoListScopeKey(a)).toBe(todoListScopeKey(b));
    expect(todoListScopeKey(a)).not.toBe(todoListScopeKey(c));
    expect(todoListScopeKey(null)).toBe('');
  });
});

// ---------------------------------------------------------------------------
// Preferences parser (ported from the code page's useUserPreferences.test.ts)
// ---------------------------------------------------------------------------

describe('parseTodoKanbanPreferences', () => {
  it('defaults: horizontal orientation, card view, default thresholds, no column order', () => {
    const { preferences } = parseTodoKanbanPreferences('{}');
    expect(DEFAULT_SMART_TODO_ORIENTATION).toBe('horizontal');
    expect(preferences).toEqual({
      todayThreshold: DEFAULT_TODAY_THRESHOLD,
      tomorrowThreshold: DEFAULT_TOMORROW_THRESHOLD,
      viewMode: DEFAULT_SMART_TODO_VIEW_MODE,
      orientation: 'horizontal',
      columnOrders: {},
    });
  });

  it('restores every stored field together', () => {
    const json = JSON.stringify({
      todayThreshold: 75,
      tomorrowThreshold: 25,
      viewMode: 'list',
      orientation: 'vertical',
      columnOrders: { Today: ['a', 'b'] },
    });
    expect(parseTodoKanbanPreferences(json).preferences).toEqual({
      todayThreshold: 75,
      tomorrowThreshold: 25,
      viewMode: 'list',
      orientation: 'vertical',
      columnOrders: { Today: ['a', 'b'] },
    });
  });

  it('invalid fields fall back to defaults', () => {
    const { preferences } = parseTodoKanbanPreferences(
      JSON.stringify({ todayThreshold: '60', orientation: 'diagonal', viewMode: 'grid', columnOrders: { Today: [1] } })
    );
    expect(preferences.todayThreshold).toBe(DEFAULT_TODAY_THRESHOLD);
    expect(preferences.orientation).toBe('horizontal');
    expect(preferences.viewMode).toBe(DEFAULT_SMART_TODO_VIEW_MODE);
    expect(preferences.columnOrders).toEqual({});
  });

  it('a pre-orientation record (thresholds + viewMode only) keeps its fields', () => {
    const { preferences } = parseTodoKanbanPreferences(
      JSON.stringify({ todayThreshold: 60, tomorrowThreshold: 30, viewMode: 'list' })
    );
    expect(preferences.orientation).toBe('horizontal');
    expect(preferences.viewMode).toBe('list');
  });

  it('corrupt JSON, null, and a JSON array yield the defaults', () => {
    for (const json of ['this is not json {{{', null, '[1,2]']) {
      expect(parseTodoKanbanPreferences(json).preferences.orientation).toBe(DEFAULT_SMART_TODO_ORIENTATION);
      expect(parseTodoKanbanPreferences(json).extra).toEqual({});
    }
  });

  it('returns unknown keys in extra', () => {
    expect(parseTodoKanbanPreferences(JSON.stringify({ todayThreshold: 60, futureField: 1 })).extra).toEqual({
      futureField: 1,
    });
  });

  it('the preference type is the shared Kanban record (100000000)', () => {
    expect(PREFERENCE_TYPE_TODO_KANBAN).toBe(100000000);
  });
});

// ---------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------

function fakeWebApi(overrides: Partial<ITodoWebApi> = {}): jest.Mocked<ITodoWebApi> {
  return {
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    createRecord: jest.fn(async () => ({ id: 'new-pref' })),
    updateRecord: jest.fn(async () => undefined),
    ...overrides,
  } as jest.Mocked<ITodoWebApi>;
}

describe('TodoDataverseService', () => {
  it('createTodo sends an Open, owner-less record through the injected create function', async () => {
    const createTodoRecord = jest.fn(async () => 'todo-1');
    const webApi = fakeWebApi();
    const service = new TodoDataverseService(webApi, createTodoRecord, jest.fn());
    const result = await service.createTodo('  Draft reply  ');
    expect(result).toEqual({ success: true, data: 'todo-1' });
    expect(createTodoRecord).toHaveBeenCalledWith({ sprk_name: 'Draft reply', statecode: 0, statuscode: 1 });
    expect(webApi.createRecord).not.toHaveBeenCalled();
  });

  it('createTodo rejects a blank title without calling the create function', async () => {
    const createTodoRecord = jest.fn();
    const result = await new TodoDataverseService(fakeWebApi(), createTodoRecord, jest.fn()).createTodo('   ');
    expect(result.success).toBe(false);
    expect(createTodoRecord).not.toHaveBeenCalled();
  });

  it('createTodo returns a failure (does not throw) when the BFF call rejects', async () => {
    const service = new TodoDataverseService(
      fakeWebApi(),
      jest.fn(async () => Promise.reject(new Error('403'))),
      jest.fn()
    );
    const result = await service.createTodo('x');
    expect(result.success).toBe(false);
  });

  it('dismissTodo and restore set the right status/state codes', async () => {
    const webApi = fakeWebApi();
    const updateTodoRecord = jest.fn(async () => undefined);
    const service = new TodoDataverseService(webApi, jest.fn(), updateTodoRecord);
    await service.dismissTodo('t1');
    await service.updateTodoStatus('t1', 'Open');
    expect(updateTodoRecord).toHaveBeenNthCalledWith(1, 't1', { statecode: 1, statuscode: 659490002 });
    expect(updateTodoRecord).toHaveBeenNthCalledWith(2, 't1', { statecode: 0, statuscode: 1 });
  });

  // Ontology task 234 (#3279, D-233): every status change goes through the BFF update (PATCH
  // /api/v1/child-records/sprk_todo/{id}), which refuses closing an open inquiry's outcome To Do. Never Xrm.WebApi.
  describe('status changes go through the BFF update, never Xrm.WebApi (task 234)', () => {
    it.each([
      ['dismiss', (s: TodoDataverseService) => s.dismissTodo('t1'), { statecode: 1, statuscode: 659490002 }],
      ['complete', (s: TodoDataverseService) => s.updateTodoStatus('t1', 'Completed'), { statecode: 1, statuscode: 2 }],
      ['restore', (s: TodoDataverseService) => s.updateTodoStatus('t1', 'Open'), { statecode: 0, statuscode: 1 }],
    ])(
      '%s of an ordinary To Do writes the same columns as before, through the BFF (must-not-fire)',
      async (_name, act, columns) => {
        const webApi = fakeWebApi();
        const updateTodoRecord = jest.fn(async () => undefined);
        const result = await act(new TodoDataverseService(webApi, jest.fn(), updateTodoRecord));
        expect(result).toEqual({ success: true, data: undefined });
        expect(updateTodoRecord).toHaveBeenCalledTimes(1);
        expect(updateTodoRecord).toHaveBeenCalledWith('t1', columns);
        expect(webApi.updateRecord).not.toHaveBeenCalled();
      }
    );

    // What a real host's BFF update rejects with (fix round 1, F2-1): `@spaarke/auth`'s authenticatedFetch THROWS an ApiError
    // for a 409, its reasonCode inside problemDetails; `updateChildRecordViaBff` turns that into a ChildRecordWriteError
    // (top-level reasonCode). The service reads both shapes.
    const apiErrorRefusal = (): Error =>
      Object.assign(new Error('This to-do is completed by recording how the budget inquiry turned out.'), {
        name: 'ApiError',
        status: 409,
        problemDetails: { status: 409, detail: 'server words', reasonCode: 'inquiry.disposition_required' },
      });
    const childRecordWriteErrorRefusal = (): Error =>
      Object.assign(new Error('server words'), {
        name: 'ChildRecordWriteError',
        status: 409,
        reasonCode: 'inquiry.disposition_required',
      });

    it.each([
      ['dismiss', 'ApiError', (s: TodoDataverseService) => s.dismissTodo('t1'), apiErrorRefusal],
      ['complete', 'ApiError', (s: TodoDataverseService) => s.updateTodoStatus('t1', 'Completed'), apiErrorRefusal],
      [
        'dismiss',
        'ChildRecordWriteError',
        (s: TodoDataverseService) => s.dismissTodo('t1'),
        childRecordWriteErrorRefusal,
      ],
      [
        'complete',
        'ChildRecordWriteError',
        (s: TodoDataverseService) => s.updateTodoStatus('t1', 'Completed'),
        childRecordWriteErrorRefusal,
      ],
    ])("%s of an open inquiry's outcome To Do is refused in plain words (%s)", async (_name, _shape, act, refusal) => {
      const webApi = fakeWebApi();
      const result = await act(
        new TodoDataverseService(
          webApi,
          jest.fn(),
          jest.fn(async () => Promise.reject(refusal()))
        )
      );
      expect(result.success).toBe(false);
      expect(!result.success && result.error.code).toBe('TODO_INQUIRY_DISPOSITION_REQUIRED');
      expect(!result.success && result.error.message).toBe(INQUIRY_TODO_CLOSE_MESSAGE);
      expect(INQUIRY_TODO_CLOSE_MESSAGE).toMatch(/^Record the outcome on the inquiry first\./);
      expect(webApi.updateRecord).not.toHaveBeenCalled();
    });

    it('any other failed update keeps its own message and code, and never falls back to Xrm.WebApi', async () => {
      const webApi = fakeWebApi();
      const result = await new TodoDataverseService(
        webApi,
        jest.fn(),
        jest.fn(async () => Promise.reject(new Error('Network down')))
      ).dismissTodo('t1');
      expect(result).toMatchObject({
        success: false,
        error: { code: 'TODO_STATUS_UPDATE_ERROR', message: 'Network down' },
      });
      expect(webApi.updateRecord).not.toHaveBeenCalled();
    });
  });

  it('getActiveTodos maps formatted values onto plain fields', async () => {
    const webApi = fakeWebApi({
      retrieveMultipleRecords: jest.fn(async () => ({
        entities: [
          {
            sprk_todoid: 't1',
            '_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue': 'Pat Doe',
            'sprk_duedate@OData.Community.Display.V1.FormattedValue': '12/1/2026',
          },
        ],
      })),
    });
    const result = await new TodoDataverseService(webApi, jest.fn(), jest.fn()).getActiveTodos({
      kind: 'assignedTo',
      contactId: 'c',
    });
    expect(result.success && result.data[0]).toMatchObject({
      assignedToName: 'Pat Doe',
      dueDateFormatted: '12/1/2026',
    });
  });

  it('saveUserPreferences updates the existing record, or creates one bound to the user', async () => {
    const webApi = fakeWebApi();
    const service = new TodoDataverseService(webApi, jest.fn(), jest.fn());
    expect(await service.saveUserPreferences(USER_ID, 100000000, '{}', 'pref-1')).toEqual({
      success: true,
      data: 'pref-1',
    });
    expect(webApi.updateRecord).toHaveBeenCalledWith('sprk_userpreference', 'pref-1', { sprk_preferencevalue: '{}' });

    expect(await service.saveUserPreferences(USER_ID, 100000000, '{"a":1}')).toEqual({
      success: true,
      data: 'new-pref',
    });
    expect(webApi.createRecord).toHaveBeenCalledWith('sprk_userpreference', {
      sprk_preferencetype: 100000000,
      sprk_preferencevalue: '{"a":1}',
      'sprk_User@odata.bind': `/systemusers(${USER_ID})`,
    });
  });
});

// ---------------------------------------------------------------------------
// Hooks (rendered through a tiny probe component)
// ---------------------------------------------------------------------------

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
});

const flush = async (): Promise<void> => {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
  });
};

describe('useTodoKanbanPreferences', () => {
  function setup(storedValue: string | null) {
    const getUserPreferences = jest.fn(async () => ({
      success: true as const,
      data:
        storedValue === null ? [] : [{ sprk_userpreferenceid: 'pref-1', sprk_preferencevalue: storedValue } as never],
    }));
    const saveUserPreferences = jest.fn(async () => ({ success: true as const, data: 'pref-1' }));
    const service = { getUserPreferences, saveUserPreferences };
    const latest: { current?: IUseTodoKanbanPreferencesResult } = {};
    function Probe(): null {
      latest.current = useTodoKanbanPreferences({ service, userId: USER_ID });
      return null;
    }
    act(() => root.render(<Probe />));
    return { latest, saveUserPreferences };
  }

  it('a threshold save keeps orientation, column order and unknown keys (the workspace copy dropped them)', async () => {
    const { latest, saveUserPreferences } = setup(
      JSON.stringify({
        todayThreshold: 60,
        tomorrowThreshold: 30,
        orientation: 'vertical',
        columnOrders: { Today: ['a'] },
        futureField: 'x',
      })
    );
    await flush();
    expect(latest.current!.preferences.orientation).toBe('vertical');

    await act(async () => {
      await latest.current!.updatePreferences({ todayThreshold: 70, tomorrowThreshold: 20 });
    });
    const saved = JSON.parse((saveUserPreferences.mock.calls[0] as unknown[])[2] as string);
    expect(saved).toMatchObject({
      todayThreshold: 70,
      tomorrowThreshold: 20,
      orientation: 'vertical',
      columnOrders: { Today: ['a'] },
      futureField: 'x',
    });
    expect((saveUserPreferences.mock.calls[0] as unknown[])[3]).toBe('pref-1');
  });

  it('two quick updates merge instead of overwriting each other', async () => {
    const { latest, saveUserPreferences } = setup(null);
    await flush();
    await act(async () => {
      const update = latest.current!.updatePreferences;
      void update({ columnOrders: { Future: ['b', 'a'] } });
      await update({ orientation: 'vertical' });
    });
    const last = JSON.parse((saveUserPreferences.mock.calls.at(-1) as unknown[])[2] as string);
    expect(last).toMatchObject({ orientation: 'vertical', columnOrders: { Future: ['b', 'a'] } });
    expect(latest.current!.preferences).toMatchObject({
      orientation: 'vertical',
      columnOrders: { Future: ['b', 'a'] },
    });
  });
});

describe('useTodoKanbanPreferences — save ordering and early updates', () => {
  it('a first-time user’s two quick updates create one record, then update it', async () => {
    let created = 0;
    const saveUserPreferences = jest.fn(async (_u: string, _t: number, _v: string, existingId?: string) => {
      // Simulates the service: no id → look up (none yet) → create a new record.
      await Promise.resolve();
      if (existingId) return { success: true as const, data: existingId };
      created++;
      return { success: true as const, data: `pref-${created}` };
    });
    const service = {
      getUserPreferences: jest.fn(async () => ({ success: true as const, data: [] })),
      saveUserPreferences,
    };
    const latest: { current?: IUseTodoKanbanPreferencesResult } = {};
    function Probe(): null {
      latest.current = useTodoKanbanPreferences({ service, userId: USER_ID });
      return null;
    }
    act(() => root.render(<Probe />));
    await flush();
    await act(async () => {
      const first = latest.current!.updatePreferences({ columnOrders: { Today: ['x', 'y'] } });
      const second = latest.current!.updatePreferences({ orientation: 'vertical' });
      await Promise.all([first, second]);
    });
    expect(created).toBe(1);
    expect(saveUserPreferences).toHaveBeenCalledTimes(2);
    expect((saveUserPreferences.mock.calls[1] as unknown[])[3]).toBe('pref-1');
  });

  it('an update made while the record loads is saved on top of the stored values, not over them', async () => {
    let resolveLoad: (v: unknown) => void = () => undefined;
    const getUserPreferences = jest.fn(
      () =>
        new Promise(resolve => {
          resolveLoad = resolve;
        })
    );
    const saveUserPreferences = jest.fn(async () => ({ success: true as const, data: 'pref-1' }));
    const service = { getUserPreferences, saveUserPreferences } as never;
    const latest: { current?: IUseTodoKanbanPreferencesResult } = {};
    function Probe(): null {
      latest.current = useTodoKanbanPreferences({ service, userId: USER_ID });
      return null;
    }
    act(() => root.render(<Probe />));

    let pending: Promise<void> = Promise.resolve();
    act(() => {
      pending = latest.current!.updatePreferences({ orientation: 'vertical' });
    });
    await flush();
    expect(saveUserPreferences).not.toHaveBeenCalled();

    await act(async () => {
      resolveLoad({
        success: true,
        data: [
          {
            sprk_userpreferenceid: 'pref-1',
            sprk_preferencevalue: JSON.stringify({
              todayThreshold: 80,
              tomorrowThreshold: 40,
              columnOrders: { Today: ['a'] },
            }),
          },
        ],
      });
      await pending;
    });
    const saved = JSON.parse((saveUserPreferences.mock.calls[0] as unknown[])[2] as string);
    expect(saved).toMatchObject({
      todayThreshold: 80,
      tomorrowThreshold: 40,
      columnOrders: { Today: ['a'] },
      orientation: 'vertical',
    });
    expect(latest.current!.preferences).toMatchObject({ todayThreshold: 80, orientation: 'vertical' });
  });
});

describe('useTodoItems', () => {
  const row = (id: string, score: number) =>
    ({
      sprk_todoid: id,
      sprk_name: id,
      sprk_priorityscore: score,
      sprk_effortscore: 10,
      statecode: 0,
      statuscode: 1,
    }) as never;

  it('feed sync: an inactive todo is removed, a newly active one triggers a refetch', async () => {
    let listener: ((todoId: string, active: boolean) => void) | undefined;
    const feedSync = {
      subscribe: jest.fn((fn: (todoId: string, active: boolean) => void) => {
        listener = fn;
        return () => undefined;
      }),
    };
    const getActiveTodos = jest.fn(async () => ({ success: true as const, data: [row('a', 90), row('b', 10)] }));
    const latest: { current?: IUseTodoItemsResult } = {};
    const scope: TodoListScope = { kind: 'owner', userId: USER_ID };
    function Probe(): null {
      latest.current = useTodoItems({ service: { getActiveTodos } as never, scope, feedSync: feedSync as never });
      return null;
    }
    act(() => root.render(<Probe />));
    await flush();
    expect(latest.current!.items.map(i => i.sprk_todoid)).toEqual(['a', 'b']);

    // The listener is async: await each call inside act.
    await act(async () => {
      await listener!('a', false);
    });
    expect(latest.current!.items.map(i => i.sprk_todoid)).toEqual(['b']);

    await act(async () => {
      await listener!('c', true);
    });
    expect(getActiveTodos).toHaveBeenCalledTimes(2);
  });

  it('a failed fetch reports an error; refetch retries', async () => {
    const getActiveTodos = jest
      .fn()
      .mockResolvedValueOnce({ success: false, error: { code: 'X', message: 'boom' } })
      .mockResolvedValueOnce({ success: true, data: [row('a', 50)] });
    const latest: { current?: IUseTodoItemsResult } = {};
    const scope: TodoListScope = { kind: 'owner', userId: USER_ID };
    function Probe(): null {
      latest.current = useTodoItems({ service: { getActiveTodos } as never, scope });
      return null;
    }
    act(() => root.render(<Probe />));
    await flush();
    expect(latest.current!.error).not.toBeNull();
    expect(latest.current!.items).toEqual([]);

    act(() => latest.current!.refetch());
    await flush();
    expect(latest.current!.error).toBeNull();
    expect(latest.current!.items.map(i => i.sprk_todoid)).toEqual(['a']);
  });

  it('mockItems bypass the service', async () => {
    const getActiveTodos = jest.fn();
    const latest: { current?: IUseTodoItemsResult } = {};
    const mockItems = [row('m', 50)];
    function Probe(): null {
      latest.current = useTodoItems({
        service: { getActiveTodos } as never,
        scope: { kind: 'owner', userId: USER_ID },
        mockItems,
      });
      return null;
    }
    act(() => root.render(<Probe />));
    await flush();
    expect(getActiveTodos).not.toHaveBeenCalled();
    expect(latest.current!.items.map(i => i.sprk_todoid)).toEqual(['m']);
  });

  it('fetches the scope, sorts, and refetches when the scope changes', async () => {
    const getActiveTodos = jest.fn(async (scope: TodoListScope) => ({
      success: true as const,
      data: [
        {
          sprk_todoid: `${todoListScopeKey(scope)}-low`,
          sprk_name: 'low',
          sprk_priorityscore: 10,
          sprk_effortscore: 10,
          statecode: 0,
          statuscode: 1,
        },
        {
          sprk_todoid: `${todoListScopeKey(scope)}-high`,
          sprk_name: 'high',
          sprk_priorityscore: 90,
          sprk_effortscore: 10,
          statecode: 0,
          statuscode: 1,
        },
      ] as never[],
    }));
    const service = { getActiveTodos };
    const latest: { current?: IUseTodoItemsResult } = {};
    function Probe({ recordId }: { recordId: string }): null {
      latest.current = useTodoItems({
        service: service as never,
        // A new object every render — the hook must key on its contents, not identity.
        scope: { kind: 'assignedTo', contactId: 'c', regardingFilter: { entityType: 'sprk_matter', recordId } },
      });
      return null;
    }
    act(() => root.render(<Probe recordId="1" />));
    await flush();
    expect(getActiveTodos).toHaveBeenCalledTimes(1);
    expect(latest.current!.items.map(i => i.sprk_name)).toEqual(['high', 'low']);

    act(() => root.render(<Probe recordId="1" />));
    await flush();
    expect(getActiveTodos).toHaveBeenCalledTimes(1);

    act(() => root.render(<Probe recordId="2" />));
    await flush();
    expect(getActiveTodos).toHaveBeenCalledTimes(2);
  });

  it('a null scope fetches nothing', async () => {
    const getActiveTodos = jest.fn();
    function Probe(): null {
      useTodoItems({ service: { getActiveTodos } as never, scope: null });
      return null;
    }
    act(() => root.render(<Probe />));
    await flush();
    expect(getActiveTodos).not.toHaveBeenCalled();
  });
});
