/**
 * `useInlineTodoCreate` uses the SHARED nav-prop discovery (Pattern D, issues #2803 P-3 / #2804 P-4).
 *
 * The hook used to keep a private `_discoverNavProps` + cache for `sprk_todo`, so a page that also ran any other
 * `sprk_todo` writer queried Dataverse metadata twice. It now calls `discoverNavProps` from `@spaarke/ui-components`,
 * which owns the one cache. This suite overrides the package's services stub with the REAL discovery and catalog (from
 * source) to prove it end to end:
 *   - a `sprk_todo` already discovered through the shared function is not fetched again by the hook;
 *   - two creates through the hook cost ONE metadata request, for the lowercase entity.
 */

jest.mock('@spaarke/ui-components/services', () => {
  const resolver = jest.requireActual('../../Spaarke.UI.Components/src/services/PolymorphicResolverService');
  const builder = jest.requireActual('../../Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder');
  return {
    __esModule: true,
    discoverNavProps: resolver.discoverNavProps,
    applyResolverFields: resolver.applyResolverFields,
    _resetNavPropCacheForTests: resolver._resetNavPropCacheForTests,
    TODO_REGARDING_CATALOG: builder.TODO_REGARDING_CATALOG,
    createChildRecordViaBff: jest.fn(),
  };
});

import { renderHook, act } from '@testing-library/react';
import { discoverNavProps, _resetNavPropCacheForTests } from '@spaarke/ui-components/services';
import { useInlineTodoCreate } from '../src/hooks/useInlineTodoCreate';
import type { IWebApi, NotificationItem } from '../src/types/notifications';

const NAV_PROPS_BODY = {
  value: [
    {
      ReferencingAttribute: 'sprk_regardingmatter',
      ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
      ReferencedEntity: 'sprk_matter',
    },
  ],
};

function metadataFetch(): jest.Mock {
  return jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => NAV_PROPS_BODY });
}

function makeItem(id: string): NotificationItem {
  return {
    id,
    title: 'Review motion',
    body: '',
    category: 'tasks-overdue',
    priority: 'high',
    actionUrl: '',
    regardingName: 'Acme Matter',
    regardingEntityType: 'sprk_matter',
    regardingId: '11111111-1111-1111-1111-111111111111',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate: null,
  };
}

function makeWebApi(): IWebApi {
  return {
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    retrieveRecord: jest.fn(),
    createRecord: jest.fn().mockResolvedValue({ id: 'todo-1' }),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

describe('useInlineTodoCreate — shared nav-prop discovery', () => {
  const realFetch = global.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    global.fetch = realFetch;
    jest.restoreAllMocks();
  });

  it('does not re-query a sprk_todo already discovered through the shared function', async () => {
    await discoverNavProps('sprk_todo', metadataFetch() as unknown as typeof fetch);

    const hookFetch = metadataFetch();
    global.fetch = hookFetch as unknown as typeof fetch;
    const webApi = makeWebApi();
    const createChild = jest.fn().mockResolvedValue('todo-1');
    const { result } = renderHook(() => useInlineTodoCreate(webApi, undefined, createChild));

    await act(async () => {
      await result.current.createTodo(makeItem('n-1'));
    });

    expect(result.current.isCreated('n-1')).toBe(true);
    expect(hookFetch).not.toHaveBeenCalled();
  });

  it('issues ONE lowercase metadata request for two creates', async () => {
    const hookFetch = metadataFetch();
    global.fetch = hookFetch as unknown as typeof fetch;
    const webApi = makeWebApi();
    const createChild = jest.fn().mockResolvedValue('todo-1');
    const { result } = renderHook(() => useInlineTodoCreate(webApi, undefined, createChild));

    await act(async () => {
      await result.current.createTodo(makeItem('n-1'));
      await result.current.createTodo(makeItem('n-2'));
    });

    expect(result.current.isCreated('n-1')).toBe(true);
    expect(result.current.isCreated('n-2')).toBe(true);
    expect(hookFetch).toHaveBeenCalledTimes(1);
    expect(String(hookFetch.mock.calls[0][0])).toContain(
      "EntityDefinitions(LogicalName='sprk_todo')/ManyToOneRelationships"
    );
    // The shared resolver bound the regarding lookup from the discovered nav-prop.
    const [, record] = createChild.mock.calls[0];
    expect(Object.keys(record)).toContain('sprk_RegardingMatter@odata.bind');
  });
});
