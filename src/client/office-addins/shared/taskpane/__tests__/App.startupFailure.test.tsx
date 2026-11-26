/**
 * Task 139 (spaarkeai-word-add-in-r1): pane start-up failures.
 *
 * Owner dev check 2026-11-25 (Outlook on the web, a just-arrived email): the pane showed the Welcome / Sign in screen
 * with "Failed to initialize", then recovered about two minutes later. Causes pinned here:
 *
 * - the host adapters throw PLAIN OBJECTS `{ code, message }` (`createHostAdapterError`), not `Error`s, so
 *   `instanceof Error` dropped every adapter failure's text;
 * - the sign-in state was read only AFTER the item, so a failed item read left a signed-in user "not authenticated"
 *   and showed them the Sign in screen.
 *
 * The real `App` and the real Sign in screen run; the tabs' views and the shell are doubled.
 * The pane's error text appears twice in the DOM (the visible MessageBar and an `sr-only` live region), so the
 * assertions read the visible element only.
 */
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import { authService } from '@shared/services';
import * as SaveViewModule from '../components/views/SaveView';
import * as IdentityService from '../services/documentIdentityService';

jest.mock('@shared/services', () => ({
  authService: {
    isAuthenticated: jest.fn(),
    getAccessToken: jest.fn(),
    getAccount: jest.fn(),
    signIn: jest.fn(),
    signOut: jest.fn(),
    clearCache: jest.fn(),
    initialize: jest.fn(),
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
jest.mock('../components/views/SaveView', () => ({
  SaveView: jest.fn(() => <div data-testid="save-view">Save view</div>),
}));
jest.mock('../components/views/FindView', () => ({ FindView: jest.fn(() => null) }));
jest.mock('../components/views/CreateTodoView', () => ({ CreateTodoView: jest.fn(() => null) }));
jest.mock('../services/documentIdentityService', () => ({
  ...jest.requireActual('../services/documentIdentityService'),
  resolveEmailIdentity: jest.fn(async () => null),
  identityOfSavedDocument: jest.fn(),
  resolveDocumentIdentity: jest.fn(),
  completeStampIdentity: jest.fn(async (outcome: unknown) => outcome),
}));

const isAuthenticated = authService.isAuthenticated as jest.Mock;
const SaveView = SaveViewModule.SaveView as unknown as jest.Mock;
const resolveEmailIdentity = IdentityService.resolveEmailIdentity as jest.Mock;

const CAPABILITIES = {
  canGetAttachments: true,
  canGetRecipients: true,
  canGetSender: true,
  canGetDocumentContent: false,
  canGetDocumentUrl: false,
  canReadDocumentStamp: false,
  canWriteDocumentStamp: false,
  canResolveEmailIdentity: false,
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
  minApiVersion: '1.5',
  supportedRequirementSet: 'Mailbox 1.8',
};

/**
 * What the item's mode lets the adapter do, as the real adapters report it (review F1): an Outlook pane with no item
 * is in mode 'unknown' and can resolve nothing; once an email is selected it is 'read' and can resolve the email's
 * identity. A Word pane resolves the open document's identity from its URL.
 */
type Mode = 'unknown' | 'read';
function capabilitiesFor(hostType: 'outlook' | 'word', mode: Mode) {
  return {
    ...CAPABILITIES,
    canResolveEmailIdentity: hostType === 'outlook' && mode === 'read',
    canGetDocumentUrl: hostType === 'word' && mode === 'read',
  };
}

/**
 * A host adapter whose capabilities FOLLOW `state.mode` (a test moves it, as the real adapter does when an item
 * appears). `overrides` win.
 */
function adapter(
  overrides: Record<string, unknown> = {},
  hostType: 'outlook' | 'word' = 'outlook',
  state: { mode: Mode } = { mode: 'read' }
): IHostAdapter {
  return {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: jest.fn(async () => 'item-1'),
    getSubject: jest.fn(async () => 'Subject'),
    getItemType: () => (hostType === 'outlook' ? 'email' : 'document'),
    getHostType: () => hostType,
    getCapabilities: () => capabilitiesFor(hostType, state.mode),
    getEmailIdentityKeys: jest.fn(async () => ({ internetMessageId: '<id@example.com>', itemId: 'item-1' })),
    getDocumentUrl: jest.fn(async () => 'https://contoso.sharepoint.com/a.docx'),
    readDocumentStamp: jest.fn(async () => null),
    ...overrides,
  } as unknown as IHostAdapter;
}

/**
 * Start-up that fails `failures.length` times in `initialize()` (the host adapter's own start), then works. Start-up
 * failures other than "no email selected" come from here: the item id / subject reads are best-effort (review F2).
 */
function initializeFailing(...failures: unknown[]) {
  let ready = false;
  let attempt = 0;
  return {
    isInitialized: () => ready,
    initialize: jest.fn(async () => {
      if (attempt < failures.length) {
        throw failures[attempt++];
      }
      ready = true;
    }),
  };
}

/** A `HostAdapterError` as `createHostAdapterError` builds it: a plain object, NOT an `Error`. */
const adapterError = (code: string, message: string) => ({ code, message, innerError: undefined });

/** The VISIBLE occurrences of `text` (the pane also mirrors errors into an `sr-only` live region). */
const visible = (text: string) => screen.queryAllByText(text).filter(el => !el.closest('.sr-only'));

const signInButton = () => screen.queryByRole('button', { name: /sign in/i });
const retryButton = () => screen.queryByRole('button', { name: 'Retry' });

beforeEach(() => {
  jest.clearAllMocks();
  isAuthenticated.mockImplementation(() => true);
  (authService.getAccessToken as jest.Mock).mockImplementation(async () => 'token');
  (authService.getAccount as jest.Mock).mockImplementation(() => ({ name: 'Guest User', username: 'g@example.com' }));
  SaveView.mockImplementation(({ documentIdentity }: { documentIdentity?: { kind?: string } }) => (
    <div data-testid="save-view">
      Save view
      {documentIdentity?.kind === 'resolved' ? <span>Saved to Spaarke</span> : null}
    </div>
  ));
  resolveEmailIdentity.mockImplementation(async () => null);
});

describe('start-up failure text', () => {
  it('shows an adapter { code, message } failure with its own message, not "Failed to initialize"', async () => {
    const host = adapter(initializeFailing(adapterError('INVALID_HOST', 'Expected Outlook host, but got: Word')));

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Expected Outlook host, but got: Word')).toHaveLength(1));
    expect(screen.queryByText('Failed to initialize')).not.toBeInTheDocument();
  });

  it('shows the message of a thrown Error', async () => {
    const host = adapter(initializeFailing(new Error('Office.js is not ready')));

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Office.js is not ready')).toHaveLength(1));
  });

  it.each([
    ['undefined', undefined],
    ['an empty object', {}],
    ['a blank message', { code: 'UNKNOWN_ERROR', message: '   ' }],
    ['a non-string message', { message: 42 }],
  ])('falls back to a generic sentence when the thrown value is %s', async (_label, thrown) => {
    const host = adapter(initializeFailing(thrown));

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Spaarke could not start. Try again.')).toHaveLength(1));
    expect(retryButton()).toBeInTheDocument();
  });
});

describe('no email selected', () => {
  it('says to select an email, with Retry; Retry then opens the pane normally', async () => {
    const getItemId = jest
      .fn()
      .mockRejectedValueOnce(adapterError('NO_ITEM_SELECTED', 'No email item is currently selected or available.'))
      .mockResolvedValue('item-1');
    const host = adapter({ getItemId });

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Select an email to use Spaarke.')).toHaveLength(1));
    // Plain text, not the adapter's technical sentence.
    expect(screen.queryByText(/No email item is currently selected/)).not.toBeInTheDocument();
    expect(signInButton()).not.toBeInTheDocument();
    expect(screen.queryByText('Welcome to Spaarke')).not.toBeInTheDocument();

    fireEvent.click(retryButton()!);

    await waitFor(() => expect(screen.getByTestId('save-view')).toBeInTheDocument());
    expect(getItemId).toHaveBeenCalledTimes(2);
    expect(visible('Select an email to use Spaarke.')).toHaveLength(0);
    expect(retryButton()).not.toBeInTheDocument();
  });

  it('keeps showing the failure (and Retry) when the retry fails again', async () => {
    const getItemId = jest.fn(async () => {
      throw adapterError('NO_ITEM_SELECTED', 'No email item is currently selected or available.');
    });

    render(<App hostAdapter={adapter({ getItemId })} title="Spaarke" />);
    await waitFor(() => expect(visible('Select an email to use Spaarke.')).toHaveLength(1));

    fireEvent.click(retryButton()!);

    await waitFor(() => expect(getItemId).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(visible('Select an email to use Spaarke.')).toHaveLength(1));
    expect(retryButton()).toBeInTheDocument();
  });
});

describe('Retry, not Sign in', () => {
  it('a SIGNED-IN user with a start-up failure gets Retry and never the Sign in screen', async () => {
    isAuthenticated.mockImplementation(() => true);
    const host = adapter(initializeFailing(adapterError('UNKNOWN_ERROR', 'Unable to determine item mode')));

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Unable to determine item mode')).toHaveLength(1));
    expect(retryButton()).toBeInTheDocument();
    expect(signInButton()).not.toBeInTheDocument();
    expect(screen.queryByText('Welcome to Spaarke')).not.toBeInTheDocument();
  });

  it('Retry reads the sign-in state again and opens the signed-in pane when start-up now works', async () => {
    render(
      <App
        hostAdapter={adapter(initializeFailing(adapterError('CONTENT_RETRIEVAL_FAILED', 'Failed to get item ID')))}
        title="Spaarke"
      />
    );
    await waitFor(() => expect(visible('Failed to get item ID')).toHaveLength(1));

    fireEvent.click(retryButton()!);

    await waitFor(() => expect(screen.getByTestId('save-view')).toBeInTheDocument());
    expect(signInButton()).not.toBeInTheDocument();
    expect(authService.signIn).not.toHaveBeenCalled();
  });

  it('an UNauthenticated user with no start-up failure still gets the Sign in screen', async () => {
    isAuthenticated.mockImplementation(() => false);

    render(<App hostAdapter={adapter()} title="Spaarke" />);

    await waitFor(() => expect(screen.getByText('Welcome to Spaarke')).toBeInTheDocument());
    expect(signInButton()).toBeInTheDocument();
    expect(retryButton()).not.toBeInTheDocument();
  });

  it('Word shows the same failure text and Retry (the shared start-up path)', async () => {
    const host = adapter(
      initializeFailing(adapterError('NOT_INITIALIZED', 'Adapter not initialized. Call initialize() first.')),
      'word'
    );

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Adapter not initialized. Call initialize() first.')).toHaveLength(1));
    expect(signInButton()).not.toBeInTheDocument();

    fireEvent.click(retryButton()!);
    await waitFor(() => expect(screen.getByTestId('save-view')).toBeInTheDocument());
  });
});

describe('item id / subject reads are best-effort at start-up (review F2)', () => {
  it.each([
    [
      'getItemId',
      {
        getItemId: jest.fn(async () => {
          throw adapterError('CONTENT_RETRIEVAL_FAILED', 'Failed to get item ID');
        }),
      },
    ],
    [
      'getSubject',
      {
        getSubject: jest.fn(async () => {
          throw adapterError('CONTENT_RETRIEVAL_FAILED', 'Failed to get subject');
        }),
      },
    ],
  ])('a failing %s does not stop the pane from starting', async (_label, overrides) => {
    render(<App hostAdapter={adapter(overrides)} title="Spaarke" />);

    await waitFor(() => expect(screen.getByTestId('save-view')).toBeInTheDocument());
    expect(retryButton()).not.toBeInTheDocument();
    expect(visible('Failed to get item ID')).toHaveLength(0);
    expect(visible('Failed to get subject')).toHaveLength(0);
  });

  it('NO_ITEM_SELECTED from the item reads still stops the start-up', async () => {
    const host = adapter({
      getSubject: jest.fn(async () => {
        throw adapterError('NO_ITEM_SELECTED', 'No email item is currently selected or available.');
      }),
    });

    render(<App hostAdapter={host} title="Spaarke" />);

    await waitFor(() => expect(visible('Select an email to use Spaarke.')).toHaveLength(1));
  });
});

describe('the saved-or-not lookup runs after a successful Retry (review F1)', () => {
  const savedOutcome = {
    kind: 'resolved',
    documentId: 'doc-1',
    documentName: 'Email.eml',
    fileName: 'Email.eml',
    relatedRecord: null,
  };

  it('Outlook: the adapter had no item (mode unknown) at start; after Retry the email is looked up and shown as saved', async () => {
    const state: { mode: Mode } = { mode: 'unknown' };
    resolveEmailIdentity.mockImplementation(async () => savedOutcome);
    const getItemId = jest.fn(async () => {
      if (state.mode === 'unknown') {
        throw adapterError('NO_ITEM_SELECTED', 'No email item is currently selected or available.');
      }
      return 'item-1';
    });
    const host = adapter({ getItemId }, 'outlook', state);

    render(<App hostAdapter={host} title="Spaarke" />);
    await waitFor(() => expect(visible('Select an email to use Spaarke.')).toHaveLength(1));
    expect(resolveEmailIdentity).not.toHaveBeenCalled();

    // The user selects an email; the adapter derives its mode when it is next asked.
    state.mode = 'read';
    fireEvent.click(retryButton()!);

    await waitFor(() => expect(screen.getByTestId('save-view')).toBeInTheDocument());
    await waitFor(() => expect(resolveEmailIdentity).toHaveBeenCalledTimes(1));
    expect(resolveEmailIdentity).toHaveBeenCalledWith({ internetMessageId: '<id@example.com>', itemId: 'item-1' });
    await waitFor(() => expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument());
  });

  it('Outlook: an email that is there from the start is looked up exactly once', async () => {
    resolveEmailIdentity.mockImplementation(async () => savedOutcome);

    render(<App hostAdapter={adapter()} title="Spaarke" />);

    await waitFor(() => expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument());
    expect(resolveEmailIdentity).toHaveBeenCalledTimes(1);
  });

  it('Word: a document identity failure at start-up, then Retry, resolves the open document', async () => {
    const state: { mode: Mode } = { mode: 'unknown' };
    (IdentityService.resolveDocumentIdentity as jest.Mock).mockImplementation(async () => savedOutcome);
    let failed = false;
    const host = adapter(
      {
        isInitialized: () => state.mode === 'read',
        initialize: jest.fn(async () => {
          if (!failed) {
            failed = true;
            throw adapterError('NOT_INITIALIZED', 'Adapter not initialized. Call initialize() first.');
          }
          state.mode = 'read';
        }),
      },
      'word',
      state
    );

    render(<App hostAdapter={host} title="Spaarke" />);
    await waitFor(() => expect(visible('Adapter not initialized. Call initialize() first.')).toHaveLength(1));
    expect(IdentityService.resolveDocumentIdentity).not.toHaveBeenCalled();

    fireEvent.click(retryButton()!);

    await waitFor(() => expect(IdentityService.resolveDocumentIdentity).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument());
  });
});
