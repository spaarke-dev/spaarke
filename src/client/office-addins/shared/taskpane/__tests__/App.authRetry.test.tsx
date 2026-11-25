/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136): the pane's own two BFF calls — Create To Do
 * (`POST /api/office/todo`) and the Assigned To contact search (`GET /api/office/search/entities`) — retry once on
 * 401 through the auth service like every other call (clear the token cache, re-acquire, retry exactly once). A 401
 * that survives the retry shows the sign-in prompt, never a server error or a silently empty list. A 403 is a refusal
 * of the request itself and keeps the server's reason (owner decision on To Do refusals): no retry, no sign-in prompt.
 *
 * Views are doubled to capture their props; `fetch` is the network boundary.
 */
import { render, waitFor, act } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import type { HostCapabilities } from '@shared/adapters/types';
import type { DocumentIdentityOutcome } from '../services/documentIdentityService';
import * as identityService from '../services/documentIdentityService';
import * as CreateTodoViewModule from '../components/views/CreateTodoView';
import * as SignInViewModule from '../components/views/SignInView';
import { authService } from '@shared/services';

jest.mock('@shared/services', () => ({
  authService: {
    isAuthenticated: () => true,
    getAccessToken: jest.fn(async () => 'token'),
    getAccount: () => ({ name: 'Guest User', username: 'guest@example.com' }),
    signIn: jest.fn(),
    signOut: jest.fn(),
    clearCache: jest.fn(),
  },
  apiClient: { get: jest.fn(), post: jest.fn() },
  ApiClientError: class ApiClientError extends Error {},
}));
jest.mock('../hooks/useTheme', () => ({
  useTheme: () => ({
    theme: jest.requireActual('@fluentui/react-components').webLightTheme,
    preference: 'system',
    setPreference: jest.fn(),
  }),
}));
jest.mock('../hooks', () => ({
  ...jest.requireActual('../hooks'),
  useLinkedTodosForCommunication: () => ({ count: 0, isLoading: false, error: null }),
}));
jest.mock('../components/TaskPaneShell', () => ({
  TaskPaneShell: ({ children }: { children: ReactNode }) => children,
}));
jest.mock('../components/SaveFlow', () => ({
  DEFAULT_SAVED_DOCUMENT_PANE_STATE: { savedDocument: null, profileRefreshSignal: 0, contentChangedSinceSave: false },
}));
jest.mock('../components/views/SaveView', () => ({ SaveView: jest.fn(() => null) }));
jest.mock('../components/views/FindView', () => ({ FindView: jest.fn(() => null) }));
jest.mock('../components/views/CreateTodoView', () => ({ CreateTodoView: jest.fn(() => null) }));
jest.mock('../components/views/SignInView', () => ({ SignInView: jest.fn(() => null) }));
jest.mock('../services/documentIdentityService', () => ({
  ...jest.requireActual('../services/documentIdentityService'),
  resolveEmailIdentity: jest.fn(),
  identityOfSavedDocument: jest.fn(),
  resolveDocumentIdentity: jest.fn(),
  completeStampIdentity: jest.fn(async (outcome: unknown) => outcome),
}));

const CreateTodoView = CreateTodoViewModule.CreateTodoView as unknown as jest.Mock;
const SignInView = SignInViewModule.SignInView as unknown as jest.Mock;
const resolveEmailIdentity = identityService.resolveEmailIdentity as jest.Mock;
const getAccessToken = authService.getAccessToken as jest.Mock;
const clearCache = authService.clearCache as jest.Mock;

const EML_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const KEYS = { internetMessageId: '<CAF0a1b2c3@mail.example.com>', exchangeItemId: 'AAMkADAx/abc+def=' };

const SAVED_EMAIL: DocumentIdentityOutcome = {
  kind: 'resolved',
  documentId: EML_ID,
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord: {
    entityType: 'sprk_matter',
    id: '22222222-2222-2222-2222-222222222222',
    name: 'MAT-1',
    displayName: 'Gamma',
    number: 'MAT-1',
  },
};

const CAPABILITIES: HostCapabilities = {
  canGetAttachments: true,
  canGetRecipients: true,
  canGetSender: true,
  canGetDocumentContent: false,
  canGetDocumentUrl: false,
  canReadDocumentStamp: false,
  canWriteDocumentStamp: false,
  canSaveAsPdf: true,
  canSaveAsEml: true,
  canInsertLink: false,
  canAttachFile: false,
  canOpenBrowserWindow: false,
  canComposeEmail: false,
  canEmailFromPane: false,
  canShowLinkedTodos: true,
  canSuggestRelatedRecords: true,
  canProvideDocumentName: false,
  canDetectDocumentChanges: false,
  canOpenDesktopWord: false,
  canResolveEmailIdentity: true,
  minApiVersion: '1.5',
  supportedRequirementSet: 'Mailbox 1.8',
};

function outlookAdapter(): IHostAdapter {
  return {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: async () => KEYS.exchangeItemId,
    getSubject: async () => 'RE: Discovery schedule',
    getItemType: () => 'email',
    getHostType: () => 'outlook',
    getCapabilities: () => CAPABILITIES,
    getEmailIdentityKeys: jest.fn(async () => KEYS),
    getDocumentUrl: jest.fn(),
    readDocumentStamp: jest.fn(),
  } as unknown as IHostAdapter;
}

const lastProps = (view: jest.Mock) => view.mock.calls[view.mock.calls.length - 1]![0];

function respond(status: number, body: unknown = {}): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

const fetchMock = jest.fn();
const originalFetch = global.fetch;

const TODO_INPUT = { name: 'Call the client', description: '', assignedToContactId: undefined, dueDate: undefined };

/** Render the To Do tab with a filed email (so Create To Do has a record to go to) and return its view's props. */
async function openCreateTodo() {
  resolveEmailIdentity.mockResolvedValue(SAVED_EMAIL);
  render(<App hostAdapter={outlookAdapter()} initialTab="createTodo" />);
  await waitFor(() => expect(lastProps(CreateTodoView).savedContext?.regardingRecordId).toBeTruthy());
  return lastProps(CreateTodoView) as {
    onCreateTodo: (input: unknown) => Promise<{ ok: boolean; error?: string; todoId?: string }>;
    onSearchContacts: (q: string) => Promise<{ id: string; name: string }[]>;
  };
}

function authorizationOf(call: unknown[]): string | undefined {
  const init = call[1] as RequestInit;
  return (init.headers as Record<string, string>).Authorization;
}

beforeEach(() => {
  jest.clearAllMocks();
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  getAccessToken.mockResolvedValueOnce('token').mockResolvedValue('fresh-token');
});

afterAll(() => {
  global.fetch = originalFetch;
});

describe('App — Create To Do retries once on 401 (#1571)', () => {
  it('a 401 then a 201: clears the token cache, retries once with the fresh token, and the To Do is created', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValueOnce(respond(401)).mockResolvedValueOnce(respond(201, { todoId: 'todo-1' }));

    let result: Awaited<ReturnType<typeof view.onCreateTodo>> | undefined;
    await act(async () => {
      result = await view.onCreateTodo(TODO_INPUT);
    });

    expect(result).toEqual({ ok: true, todoId: 'todo-1' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(clearCache).toHaveBeenCalledTimes(1);
    expect(authorizationOf(fetchMock.mock.calls[0]!)).toBe('Bearer token');
    expect(authorizationOf(fetchMock.mock.calls[1]!)).toBe('Bearer fresh-token');
    expect(SignInView).not.toHaveBeenCalled();
  });

  it('a 401 that survives the retry shows the sign-in prompt, not a server error', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));

    let result: Awaited<ReturnType<typeof view.onCreateTodo>> | undefined;
    await act(async () => {
      result = await view.onCreateTodo(TODO_INPUT);
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result?.ok).toBe(false);
    expect(result?.error).toMatch(/sign in again/i);
    await waitFor(() => expect(SignInView).toHaveBeenCalled());
    expect(lastProps(SignInView).error).toMatch(/sign in again/i);
  });

  it('negative: a 403 refusal is not retried and keeps the server reason; no sign-in prompt', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(
      respond(403, {
        type: 'https://spaarke.com/errors/office/forbidden',
        title: 'Forbidden',
        status: 403,
        detail: 'You cannot assign this To Do to that person, or choose a different person to assign it to.',
        errorCode: 'OFFICE_009',
      })
    );

    let result: Awaited<ReturnType<typeof view.onCreateTodo>> | undefined;
    await act(async () => {
      result = await view.onCreateTodo(TODO_INPUT);
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(clearCache).not.toHaveBeenCalled();
    expect(result?.ok).toBe(false);
    expect(result?.error).not.toMatch(/sign in again/i);
    expect(SignInView).not.toHaveBeenCalled();
  });
});

describe('App — Assigned To contact search retries once on 401 (#1571)', () => {
  it('a 401 then a 200: retries once with the fresh token and returns the contacts', async () => {
    const view = await openCreateTodo();
    fetchMock
      .mockResolvedValueOnce(respond(401))
      .mockResolvedValueOnce(respond(200, { results: [{ id: 'c1', name: 'Jane Cooper' }] }));

    let contacts: { id: string; name: string }[] = [];
    await act(async () => {
      contacts = await view.onSearchContacts('Jane');
    });

    expect(contacts).toEqual([{ id: 'c1', name: 'Jane Cooper' }]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(clearCache).toHaveBeenCalledTimes(1);
    expect(authorizationOf(fetchMock.mock.calls[1]!)).toBe('Bearer fresh-token');
  });

  it('a 401 that survives the retry shows the sign-in prompt instead of a silently empty list', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));

    await act(async () => {
      await view.onSearchContacts('Jane');
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(SignInView).toHaveBeenCalled());
    expect(lastProps(SignInView).error).toMatch(/sign in again/i);
  });
});
