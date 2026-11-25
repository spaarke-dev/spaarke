/**
 * useSmartTodoBridge — the code page's wiring into the shared Smart To Do data layer (#2907 / #2972).
 *
 * The package hooks are mocked at the module boundary so these tests pin only what this page decides:
 *   - the list is scoped to the user's CONTACT (sprk_assignedto is a contact lookup), not the systemuser id;
 *   - before the contact resolves (or when there is none) the scope matches no to-dos;
 *   - an `openTodos` launch narrows the list to the parent record; other launches do not;
 *   - creates go through the BFF helper (`createTodoThroughBff`), so the server sets the owner.
 */

import * as React from 'react';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';

const mockUseTodoItems = jest.fn((_options: unknown) => ({ items: [], isLoading: false, error: null, refetch: jest.fn() }));
const mockUseCurrentContactId = jest.fn((_options: unknown) => ({ contactId: 'contact-1' as string | null, contactName: null }));
const mockServiceCtor = jest.fn();

jest.mock('@spaarke/smart-todo-components', () => ({
  TodoDataverseService: class {
    constructor(...args: unknown[]) {
      mockServiceCtor(...args);
    }
    createTodo = jest.fn(async () => ({ success: true, data: 'new-id' }));
    dismissTodo = jest.fn(async () => ({ success: true }));
    updateTodoStatus = jest.fn(async () => ({ success: true }));
  },
  useCurrentContactId: (options: unknown) => mockUseCurrentContactId(options),
  useTodoItems: (options: unknown) => mockUseTodoItems(options),
  useTodoKanbanPreferences: () => ({
    preferences: { todayThreshold: 60, tomorrowThreshold: 30, viewMode: 'card', orientation: 'horizontal', columnOrders: {} },
    updatePreferences: jest.fn(async () => undefined),
    isLoading: false,
  }),
}));

const mockLaunchContext = jest.fn((): unknown => undefined);
jest.mock('../useLaunchContext', () => ({
  LAUNCH_ACTION_OPEN_TODOS: 'openTodos',
  useLaunchContext: () => mockLaunchContext(),
}));

const mockCreateTodoThroughBff = jest.fn();
jest.mock('../../services/childRecordWrites', () => ({
  createTodoThroughBff: (...args: unknown[]) => mockCreateTodoThroughBff(...args),
}));

import { useSmartTodoBridge, type ISmartTodoBridgeResult } from '../useSmartTodoBridge';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const USER_ID = 'user-1';
const WEB_API = { retrieveMultipleRecords: jest.fn() };

let container: HTMLDivElement;
let root: Root;
const latest: { current?: ISmartTodoBridgeResult } = {};

function Probe(): null {
  latest.current = useSmartTodoBridge({ webApi: WEB_API, userId: USER_ID });
  return null;
}

function renderBridge(): void {
  act(() => root.render(<Probe />));
}

function lastScope(): unknown {
  const calls = mockUseTodoItems.mock.calls;
  return (calls[calls.length - 1][0] as { scope: unknown }).scope;
}

beforeEach(() => {
  jest.clearAllMocks();
  mockLaunchContext.mockReturnValue(undefined);
  mockUseCurrentContactId.mockReturnValue({ contactId: 'contact-1', contactName: null });
  container = document.createElement('div');
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
});

describe('useSmartTodoBridge', () => {
  it('scopes the list to the current user’s contact, not the systemuser id', () => {
    renderBridge();
    expect(mockUseCurrentContactId).toHaveBeenCalledWith(expect.objectContaining({ userId: USER_ID }));
    expect(lastScope()).toEqual({ kind: 'assignedTo', contactId: 'contact-1', regardingFilter: undefined });
  });

  it('matches no to-dos while the contact is unresolved', () => {
    mockUseCurrentContactId.mockReturnValue({ contactId: null, contactName: null });
    renderBridge();
    expect(lastScope()).toEqual({
      kind: 'assignedTo',
      contactId: '00000000-0000-0000-0000-000000000000',
      regardingFilter: undefined,
    });
  });

  it('an openTodos launch narrows the list to the parent record', () => {
    mockLaunchContext.mockReturnValue({
      action: 'openTodos',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'matter-9' },
    });
    renderBridge();
    expect(lastScope()).toEqual({
      kind: 'assignedTo',
      contactId: 'contact-1',
      regardingFilter: { entityType: 'sprk_matter', recordId: 'matter-9' },
    });
  });

  it('other launches (createTodo, openTodo) keep the unfiltered list', () => {
    mockLaunchContext.mockReturnValue({ action: 'openTodo', todoId: 't-1' });
    renderBridge();
    expect(lastScope()).toEqual({ kind: 'assignedTo', contactId: 'contact-1', regardingFilter: undefined });
  });

  it('builds the service over Xrm.WebApi with the BFF create helper', async () => {
    renderBridge();
    expect(mockServiceCtor).toHaveBeenCalledTimes(1);
    const [webApi, createFn] = mockServiceCtor.mock.calls[0] as [unknown, (p: unknown) => unknown];
    expect(webApi).toBe(WEB_API);
    createFn({ sprk_name: 'x' });
    expect(mockCreateTodoThroughBff).toHaveBeenCalledWith({ sprk_name: 'x' });
  });

  it('adapts the service results to the board’s mutation results', async () => {
    renderBridge();
    await expect(latest.current!.onCreateTodo('Call client')).resolves.toEqual({ success: true, id: 'new-id' });
    await expect(latest.current!.onDismissTodo('t-1')).resolves.toEqual({ success: true, id: undefined });
  });
});
