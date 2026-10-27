/**
 * Task 133 slice A must be INERT in the deployed pane: the Invoice view opens only from the ribbon's
 * `?action=submitInvoice` (App `initialAction`); the default pane and every tab lead nowhere near it.
 *
 * Views are doubled to see which one App renders (harness mirrors `App.emailIdentity.test.tsx`).
 */
import { render, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import { getAvailableTabs } from '../components/TaskPaneNavigation';
import * as SaveViewModule from '../components/views/SaveView';
import * as InvoiceViewModule from '../components/views/InvoiceView';
import * as CreateTodoViewModule from '../components/views/CreateTodoView';

jest.mock('@shared/services', () => ({
  authService: {
    isAuthenticated: () => true,
    getAccessToken: jest.fn(async () => 'token'),
    getAccount: () => ({ name: 'User', username: 'user@example.com' }),
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
jest.mock('../components/views/InvoiceView', () => ({ InvoiceView: jest.fn(() => null) }));
jest.mock('../services/documentIdentityService', () => ({
  ...jest.requireActual('../services/documentIdentityService'),
  resolveEmailIdentity: jest.fn(async () => null),
}));

const SaveView = SaveViewModule.SaveView as unknown as jest.Mock;
const InvoiceView = InvoiceViewModule.InvoiceView as unknown as jest.Mock;
const CreateTodoView = CreateTodoViewModule.CreateTodoView as unknown as jest.Mock;

function outlookAdapter(): IHostAdapter {
  return {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: async () => 'AAMkAD=',
    getSubject: async () => 'Invoice 4711',
    getItemType: () => 'email',
    getHostType: () => 'outlook',
    getCapabilities: () => ({
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
      canShowLinkedTodos: false,
      canSuggestRelatedRecords: false,
      canProvideDocumentName: false,
      canDetectDocumentChanges: false,
      canOpenDesktopWord: false,
      canResolveEmailIdentity: false,
      minApiVersion: '1.8',
      supportedRequirementSet: 'Mailbox 1.8',
    }),
  } as unknown as IHostAdapter;
}

beforeEach(() => jest.clearAllMocks());

describe('App — the Invoice view is reachable only through ?action=submitInvoice (task 133)', () => {
  it('initialAction "submitInvoice" renders the Invoice view and not the Save tab', async () => {
    render(<App hostAdapter={outlookAdapter()} initialAction="submitInvoice" />);

    await waitFor(() => expect(InvoiceView).toHaveBeenCalled());
    expect(SaveView).not.toHaveBeenCalled();
    // Slice A: the submission and the firm list are not wired; corrections stay off.
    const props = InvoiceView.mock.calls[InvoiceView.mock.calls.length - 1]![0];
    expect(props.onSubmit).toBeUndefined();
    expect(props.loadFirms).toBeUndefined();
    expect(props.correctionAvailable).toBeUndefined();
  });

  it('the default pane (no action) renders Save and no Invoice view', async () => {
    render(<App hostAdapter={outlookAdapter()} />);

    await waitFor(() => expect(SaveView).toHaveBeenCalled());
    expect(InvoiceView).not.toHaveBeenCalled();
  });

  it('initialAction "createTodo" is unchanged and renders no Invoice view', async () => {
    render(<App hostAdapter={outlookAdapter()} initialAction="createTodo" />);

    await waitFor(() => expect(CreateTodoView).toHaveBeenCalled());
    expect(InvoiceView).not.toHaveBeenCalled();
  });

  it('no tab offers the Invoice view, in either host', () => {
    for (const host of ['outlook', 'word'] as const) {
      const tabs = getAvailableTabs(host, { canEmailFromPane: true }).map(t => t.value);
      expect(tabs).not.toContain('invoice');
    }
  });
});
