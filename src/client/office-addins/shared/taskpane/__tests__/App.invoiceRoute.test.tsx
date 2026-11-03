/**
 * Task 133: the Invoice view is the pane's Invoice tab (package 1.1.4, owner UAT 2026-11-03), offered on a received
 * email (`canGetAttachments`); a cached older manifest's ribbon `?action=submitInvoice` (App `initialAction`) still
 * opens it. The default pane opens on Save.
 *
 * Views are doubled to see which one App renders (harness mirrors `App.emailIdentity.test.tsx`).
 */
import { act, render, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import { getAvailableTabs } from '../components/TaskPaneNavigation';
import * as SaveViewModule from '../components/views/SaveView';
import * as InvoiceViewModule from '../components/views/InvoiceView';
import * as CreateTodoViewModule from '../components/views/CreateTodoView';
import { resolveEmailIdentity } from '../services/documentIdentityService';

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
// Records the shell's `showNavigation` on every render (the tab bar), to see when App hides it, and the capabilities
// App hands the tab bar (which tabs show).
const mockShellNavigation: Array<boolean | undefined> = [];
const mockShellTabCapabilities: Array<Record<string, unknown> | undefined> = [];
jest.mock('../components/TaskPaneShell', () => ({
  TaskPaneShell: ({
    children,
    showNavigation,
    tabCapabilities,
  }: {
    children: ReactNode;
    showNavigation?: boolean;
    tabCapabilities?: Record<string, unknown>;
  }) => {
    mockShellNavigation.push(showNavigation);
    mockShellTabCapabilities.push(tabCapabilities);
    return children;
  },
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

function outlookAdapter(over: { canGetAttachments?: boolean } = {}): IHostAdapter {
  return {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: async () => 'AAMkAD=',
    getSubject: async () => 'Invoice 4711',
    getItemType: () => 'email',
    getHostType: () => 'outlook',
    getCapabilities: () => ({
      canGetAttachments: over.canGetAttachments ?? true,
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

let fetchSpy: jest.Mock;
beforeEach(() => {
  jest.clearAllMocks();
  mockShellNavigation.length = 0;
  mockShellTabCapabilities.length = 0;
  fetchSpy = jest.fn(async () => ({ ok: true, status: 200, json: async () => ({}), text: async () => '{}' }));
  global.fetch = fetchSpy as unknown as typeof fetch;
});

describe('App — the Invoice view: the Invoice tab, and a cached ?action=submitInvoice (task 133)', () => {
  it('initialAction "submitInvoice" renders the Invoice view and not the Save tab', async () => {
    render(<App hostAdapter={outlookAdapter()} initialAction="submitInvoice" />);

    await waitFor(() => expect(InvoiceView).toHaveBeenCalled());
    expect(SaveView).not.toHaveBeenCalled();
    // A1: the submission and the firm list are wired (ebilling's from-email contract); corrections are on (ebilling
    // 097b/c/e live on dev) with their prefill.
    const props = InvoiceView.mock.calls[InvoiceView.mock.calls.length - 1]![0];
    expect(typeof props.onSubmit).toBe('function');
    expect(typeof props.loadFirms).toBe('function');
    expect(props.correctionAvailable).toBe(true);
    expect(typeof props.loadCorrectionPrefill).toBe('function');
    // Stable seams across re-renders: a new `loadFirms` would make the view reload the firms and drop the choice.
    const first = InvoiceView.mock.calls[0]![0];
    expect(props.loadFirms).toBe(first.loadFirms);
    expect(props.onSubmit).toBe(first.onSubmit);
  });

  it('passes the record the open email is filed to (the Save tab\'s "Filed to") to the Invoice view', async () => {
    const MATTER_ID = '15f7ff99-6f64-f111-ab0c-7ced8ddc4cc6';
    (resolveEmailIdentity as jest.Mock).mockResolvedValueOnce({
      kind: 'resolved',
      documentId: 'doc-1',
      relatedRecord: {
        entityType: 'sprk_matter',
        id: MATTER_ID,
        name: 'Patent',
        displayName: 'Patent',
        number: 'PAT-1',
      },
    });
    const base = outlookAdapter();
    const adapter = {
      ...base,
      getCapabilities: () => ({ ...base.getCapabilities(), canResolveEmailIdentity: true }),
      getEmailIdentityKeys: async () => ({ internetMessageId: '<m1@x>' }),
    } as unknown as IHostAdapter;
    render(<App hostAdapter={adapter} initialAction="submitInvoice" />);

    await waitFor(() =>
      expect(InvoiceView.mock.calls[InvoiceView.mock.calls.length - 1]![0].filedRecord).toEqual({
        id: MATTER_ID,
        entityType: 'Matter',
        logicalName: 'sprk_matter',
        name: 'Patent',
        displayInfo: 'PAT-1',
      })
    );
  });

  it('the default pane (no action) renders Save and no Invoice view', async () => {
    render(<App hostAdapter={outlookAdapter()} />);

    await waitFor(() => expect(SaveView).toHaveBeenCalled());
    expect(InvoiceView).not.toHaveBeenCalled();
    // Inert: nothing in the default pane reaches the from-email route.
    expect(fetchSpy).not.toHaveBeenCalledWith(expect.stringContaining('/api/finance/'), expect.anything());
  });

  it('initialAction "createTodo" is unchanged and renders no Invoice view', async () => {
    render(<App hostAdapter={outlookAdapter()} initialAction="createTodo" />);

    await waitFor(() => expect(CreateTodoView).toHaveBeenCalled());
    expect(InvoiceView).not.toHaveBeenCalled();
  });

  it('hides the tab bar while the Invoice view is busy (a submission running, or its result showing), then shows it again', async () => {
    render(<App hostAdapter={outlookAdapter()} initialAction="submitInvoice" />);
    await waitFor(() => expect(InvoiceView).toHaveBeenCalled());
    const lastNavigation = () => mockShellNavigation[mockShellNavigation.length - 1];
    const onBusyChange = InvoiceView.mock.calls[InvoiceView.mock.calls.length - 1]![0].onBusyChange as (
      busy: boolean
    ) => void;
    expect(typeof onBusyChange).toBe('function');
    expect(lastNavigation()).toBe(true);

    act(() => onBusyChange(true));
    expect(lastNavigation()).toBe(false);

    act(() => onBusyChange(false));
    expect(lastNavigation()).toBe(true);
  });

  it('must-not-fire: the default pane (no action) always shows the tab bar', async () => {
    render(<App hostAdapter={outlookAdapter()} />);
    await waitFor(() => expect(SaveView).toHaveBeenCalled());
    expect(InvoiceView).not.toHaveBeenCalled();
    expect(mockShellNavigation.length).toBeGreaterThan(0);
    expect(mockShellNavigation.every(shown => shown === true)).toBe(true);
  });

  it('App hands the tab bar canGetAttachments: a received email gets the Invoice tab', async () => {
    render(<App hostAdapter={outlookAdapter()} />);
    await waitFor(() => expect(SaveView).toHaveBeenCalled());
    const caps = mockShellTabCapabilities[mockShellTabCapabilities.length - 1];
    expect(caps).toMatchObject({ canGetAttachments: true });
    expect(getAvailableTabs('outlook', caps).map(t => t.value)).toContain('invoice');
  });

  it('must-not-fire: an item without attachments access (compose) gets no Invoice tab', async () => {
    render(<App hostAdapter={outlookAdapter({ canGetAttachments: false })} />);
    await waitFor(() => expect(SaveView).toHaveBeenCalled());
    const caps = mockShellTabCapabilities[mockShellTabCapabilities.length - 1];
    expect(caps).toMatchObject({ canGetAttachments: false });
    expect(getAvailableTabs('outlook', caps).map(t => t.value)).not.toContain('invoice');
  });

  it('must-not-fire: a stale ?action=submitInvoice where attachments cannot be read renders no Invoice view', async () => {
    render(<App hostAdapter={outlookAdapter({ canGetAttachments: false })} initialAction="submitInvoice" />);
    await waitFor(() => expect(mockShellNavigation.length).toBeGreaterThan(0));
    expect(InvoiceView).not.toHaveBeenCalled();
  });
});
