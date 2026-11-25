/**
 * GitHub #1571 (spaarkeai-word-add-in-r1 task 136): the pane's own two BFF calls — Create To Do
 * (`POST /api/office/todo`) and the Assigned To contact search (`GET /api/office/search/entities`) — retry once on
 * 401 through the auth service like every other call (clear the token cache, re-acquire, retry exactly once). A 401
 * that survives the retry shows the sign-in prompt, never a server error or a silently empty list. A 403 is a refusal
 * of the request itself and keeps the server's reason (owner decision on To Do refusals): no retry, no sign-in prompt.
 *
 * Review fix round 1:
 * - the auth double hands back the SAME token until the code under test clears the cache (`clearCache()`, which
 *   records the token as refused in the real stack), so the retry tests prove the retry asked again, not that the
 *   double happened to return a new token (F1);
 * - the sign-in prompt after a lasting 401 is a banner above the tabs: the To Do form stays mounted, so what the user
 *   typed survives (F1, #2607);
 * - the auth service's "sign-in required" signal shows the same banner (F2);
 * - a Sign in click that fails says so, and a second click while one runs does nothing (F2);
 * - a contact search the server refused (403) or failed (5xx) rejects with "Could not search contacts", never an empty
 *   list (F3).
 *
 * Review fix round 2: under NAA a forced refresh does NOT yield a new token (msal-browser 3.30.0 drops
 * `forceRefresh`; the host hands back the refused token, which the strategy then declines to use). The NAA case below
 * doubles that: after `clearCache()` there is no token at all, and the pane must show its prompt and make no sign-in
 * call of its own. The real stack is pinned in shared/services/__tests__/AuthService.naaRefusedToken.test.ts.
 *
 * Views are doubled to capture their props; `fetch` is the network boundary.
 */
import { render, waitFor, act, screen, fireEvent } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import type { HostCapabilities } from '@shared/adapters/types';
import type { DocumentIdentityOutcome } from '../services/documentIdentityService';
import * as identityService from '../services/documentIdentityService';
import * as CreateTodoViewModule from '../components/views/CreateTodoView';
import * as SignInViewModule from '../components/views/SignInView';
import { authService } from '@shared/services';

const mockAuth = { authenticated: true, listeners: new Set<() => void>() };
const mockTodoMounts = { mounted: 0, unmounted: 0 };

// One auth double for the pane AND the fetch helper (which imports the AuthService module directly): the helper
// clears the cache before a retry and raises "sign-in required" after a lasting 401, on this same object.
jest.mock('@shared/services/AuthService', () => ({
  authService: {
    isAuthenticated: jest.fn(() => mockAuth.authenticated),
    getAccessToken: jest.fn(async () => 'token'),
    getAccount: () => ({ name: 'Guest User', username: 'guest@example.com' }),
    signIn: jest.fn(),
    signOut: jest.fn(),
    clearCache: jest.fn(),
    onSignInRequired: jest.fn((listener: () => void) => {
      mockAuth.listeners.add(listener);
      return () => mockAuth.listeners.delete(listener);
    }),
    notifySignInRequired: jest.fn(() => mockAuth.listeners.forEach(listener => listener())),
  },
}));
jest.mock('@shared/services', () => ({
  authService: jest.requireMock('@shared/services/AuthService').authService,
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
// The To Do view double counts mounts and unmounts: the sign-in prompt must not unmount it (it holds the user's input).
jest.mock('../components/views/CreateTodoView', () => ({
  CreateTodoView: jest.fn(() => {
    const { useEffect: useMountEffect } = jest.requireActual<typeof import('react')>('react');
    useMountEffect(() => {
      mockTodoMounts.mounted += 1;
      return () => {
        mockTodoMounts.unmounted += 1;
      };
    }, []);
    return null;
  }),
}));
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

const signIn = authService.signIn as jest.Mock;
const SIGN_IN_EXPIRED = /sign-in has expired/i;

beforeEach(() => {
  jest.clearAllMocks();
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  mockAuth.authenticated = true;
  mockAuth.listeners.clear();
  mockTodoMounts.mounted = 0;
  mockTodoMounts.unmounted = 0;
  // The standard client: the same token until `clearCache()` (the real AuthService then records it as refused and asks
  // again), so a retry gets 'fresh-token' only if the code under test cleared the cache. The NAA case overrides this.
  let forced = false;
  getAccessToken.mockImplementation(async () => (forced ? 'fresh-token' : 'token'));
  clearCache.mockImplementation(() => {
    forced = true;
  });
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

  it('a 401 that survives the retry shows the sign-in prompt above the To Do form, which stays mounted', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));

    let result: Awaited<ReturnType<typeof view.onCreateTodo>> | undefined;
    await act(async () => {
      result = await view.onCreateTodo(TODO_INPUT);
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result?.ok).toBe(false);
    expect(result?.error).toMatch(/sign in again/i);
    expect(await screen.findByText(SIGN_IN_EXPIRED)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeTruthy();
    // The user's half-filled To Do is still there (#2607): the form was never unmounted.
    expect(mockTodoMounts.unmounted).toBe(0);
    expect(SignInView).not.toHaveBeenCalled();
  });

  it('under NAA (no new token after clearCache): the retry does not resend the refused token; only the click signs in', async () => {
    getAccessToken.mockImplementation(async () => (clearCache.mock.calls.length > 0 ? null : 'token'));
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));

    let result: Awaited<ReturnType<typeof view.onCreateTodo>> | undefined;
    await act(async () => {
      result = await view.onCreateTodo(TODO_INPUT);
    });

    // The refused token is not resent; with no token silently the retry is not sent at all (fix round 3).
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result?.ok).toBe(false);
    expect(await screen.findByText(SIGN_IN_EXPIRED)).toBeTruthy();
    expect(signIn).not.toHaveBeenCalled();
    expect(mockTodoMounts.unmounted).toBe(0);

    signIn.mockResolvedValue(undefined);
    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }));
    });
    expect(signIn).toHaveBeenCalledTimes(1);
  });

  it('signing in from the prompt keeps the form and removes the prompt', async () => {
    await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));
    await act(async () => {
      await lastProps(CreateTodoView).onCreateTodo(TODO_INPUT);
    });
    signIn.mockResolvedValue(undefined);

    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }));
    });

    await waitFor(() => expect(screen.queryByText(SIGN_IN_EXPIRED)).toBeNull());
    expect(signIn).toHaveBeenCalledTimes(1);
    expect(mockTodoMounts.unmounted).toBe(0);
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

  it('a 401 that survives the retry shows the sign-in prompt, keeps the form, and the search says it failed', async () => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(401));

    let failure: unknown;
    await act(async () => {
      failure = await view.onSearchContacts('Jane').catch((err: unknown) => err);
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect((failure as Error).message).toMatch(/could not search contacts/i);
    expect(await screen.findByText(SIGN_IN_EXPIRED)).toBeTruthy();
    expect(mockTodoMounts.unmounted).toBe(0);
  });

  it.each([
    [403, 'You do not have permission to search contacts.'],
    [500, 'Search failed on the server.'],
  ])('a %i is "could not search contacts" with the reason, never an empty list', async (status, detail) => {
    const view = await openCreateTodo();
    fetchMock.mockResolvedValue(respond(status, { title: 'Error', status, detail }));

    let failure: unknown;
    await act(async () => {
      failure = await view.onSearchContacts('Jane').catch((err: unknown) => err);
    });

    expect(failure).toBeInstanceOf(Error);
    expect((failure as Error).message).toMatch(/could not search contacts/i);
    expect(screen.queryByText(SIGN_IN_EXPIRED)).toBeNull();
  });
});

describe('App — one sign-in route for every request (#1571 review F2)', () => {
  it('the auth service\'s "sign-in required" signal shows the prompt without unmounting the tabs', async () => {
    await openCreateTodo();
    expect(mockAuth.listeners.size).toBe(1);

    act(() => {
      mockAuth.listeners.forEach(listener => listener());
    });

    expect(await screen.findByText(SIGN_IN_EXPIRED)).toBeTruthy();
    expect(mockTodoMounts.unmounted).toBe(0);
  });

  it('a Sign in click that does not complete says why', async () => {
    await openCreateTodo();
    act(() => {
      mockAuth.listeners.forEach(listener => listener());
    });
    signIn.mockRejectedValue(
      new Error('Sign-in did not complete. If a pop-up was blocked, allow pop-ups for this site and try again.')
    );

    await act(async () => {
      fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }));
    });

    expect(await screen.findByText(/allow pop-ups for this site/i)).toBeTruthy();
    expect(screen.getByText(SIGN_IN_EXPIRED)).toBeTruthy();
  });

  it('the sign-in page: a second click while signing in does nothing, and the button shows it is busy', async () => {
    mockAuth.authenticated = false;
    let finish: () => void = () => undefined;
    signIn.mockImplementation(() => new Promise<void>(resolve => (finish = resolve)));
    render(<App hostAdapter={outlookAdapter()} initialTab="createTodo" />);
    await waitFor(() => expect(SignInView).toHaveBeenCalled());

    act(() => {
      void lastProps(SignInView).onSignIn();
      void lastProps(SignInView).onSignIn();
    });

    await waitFor(() => expect(lastProps(SignInView).isLoading).toBe(true));
    expect(signIn).toHaveBeenCalledTimes(1);
    mockAuth.authenticated = true;
    await act(async () => finish());
    expect(signIn).toHaveBeenCalledTimes(1);
  });
});
