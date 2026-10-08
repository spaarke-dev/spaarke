/**
 * Task 120 (spaarkeai-word-add-in-r1, owner UAT round 12 O6 + task 119's web finding): the App wiring.
 *
 * - Outlook read mode (`canResolveEmailIdentity`): after sign-in the open email is looked up with BOTH its keys; a
 *   saved copy becomes the Save tab's `documentIdentity` (task 111's box) and the shared context's document id (Find,
 *   To Do); any other answer leaves NO identity (the ordinary form).
 * - After a pane save in Outlook, the identity becomes the saved `.eml`, read by its id.
 * - "Try again" / the return-from-Spaarke refresh re-runs the EMAIL lookup in Outlook.
 * - Word is unchanged: the email lookup is never attempted, and a save never re-reads the identity by id.
 * - Every tab's `canOpenRecord` comes from the one gate: ORG_URL + (openBrowserWindow OR window.open) — so it is true
 *   on Office on the web, where `canOpenBrowserWindow` is false.
 *
 * Views are doubled to capture their props; the identity service's network functions are doubled.
 */
import { render, waitFor, act } from '@testing-library/react';
import type { ReactNode } from 'react';
import { App } from '../App';
import type { IHostAdapter } from '@shared/adapters';
import type { HostCapabilities } from '@shared/adapters/types';
import type { DocumentIdentityOutcome } from '../services/documentIdentityService';
import * as identityService from '../services/documentIdentityService';
import * as SaveViewModule from '../components/views/SaveView';
import * as FindViewModule from '../components/views/FindView';
import * as CreateTodoViewModule from '../components/views/CreateTodoView';

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
jest.mock('../services/documentIdentityService', () => ({
  ...jest.requireActual('../services/documentIdentityService'),
  resolveEmailIdentity: jest.fn(),
  identityOfSavedDocument: jest.fn(),
  resolveDocumentIdentity: jest.fn(),
  completeStampIdentity: jest.fn(async (outcome: unknown) => outcome),
}));

const SaveView = SaveViewModule.SaveView as unknown as jest.Mock;
const FindView = FindViewModule.FindView as unknown as jest.Mock;
const CreateTodoView = CreateTodoViewModule.CreateTodoView as unknown as jest.Mock;

const resolveEmailIdentity = identityService.resolveEmailIdentity as jest.Mock;
const identityOfSavedDocument = identityService.identityOfSavedDocument as jest.Mock;
const resolveDocumentIdentity = identityService.resolveDocumentIdentity as jest.Mock;

const EML_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const SAVED_ID = '51ae4b62-c5c2-f111-a05c-3833c5e9614d';
const KEYS = { internetMessageId: '<CAF0a1b2c3@mail.example.com>', exchangeItemId: 'AAMkADAx/abc+def=' };
const ORG = 'https://contoso.crm.dynamics.com';
const originalOrgUrl = process.env.ORG_URL;

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

const BASE_CAPABILITIES: HostCapabilities = {
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
  minApiVersion: '1.5',
  supportedRequirementSet: 'Mailbox 1.8',
};

function outlookAdapter(overrides: Partial<HostCapabilities> = {}) {
  const getEmailIdentityKeys = jest.fn(async () => KEYS);
  const adapter = {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: async () => KEYS.exchangeItemId,
    getSubject: async () => 'RE: Discovery schedule',
    getItemType: () => 'email',
    getHostType: () => 'outlook',
    getCapabilities: () => ({ ...BASE_CAPABILITIES, canResolveEmailIdentity: true, ...overrides }),
    getEmailIdentityKeys,
    getDocumentUrl: jest.fn(),
    readDocumentStamp: jest.fn(),
  } as unknown as IHostAdapter;
  return { adapter, getEmailIdentityKeys };
}

function wordAdapter() {
  const getEmailIdentityKeys = jest.fn();
  const adapter = {
    isInitialized: () => true,
    initialize: jest.fn(),
    getItemId: async () => 'https://contoso.sharepoint.com/a.docx',
    getSubject: async () => 'a',
    getItemType: () => 'document',
    getHostType: () => 'word',
    getCapabilities: () => ({
      ...BASE_CAPABILITIES,
      canGetAttachments: false,
      canGetSender: false,
      canGetDocumentContent: true,
      canGetDocumentUrl: true,
      canResolveEmailIdentity: false,
      canOpenBrowserWindow: true,
    }),
    getEmailIdentityKeys,
    getDocumentUrl: jest.fn(async () => 'https://contoso.sharepoint.com/a.docx'),
    readDocumentStamp: jest.fn(),
  } as unknown as IHostAdapter;
  return { adapter, getEmailIdentityKeys };
}

const lastProps = (view: jest.Mock) => view.mock.calls[view.mock.calls.length - 1]![0];

beforeEach(() => {
  jest.clearAllMocks();
  delete process.env.ORG_URL;
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

describe('App — Outlook email identity (task 120)', () => {
  it('looks the open email up with both keys after sign-in; a saved copy becomes the Save tab identity', async () => {
    resolveEmailIdentity.mockResolvedValue(SAVED_EMAIL);
    const { adapter, getEmailIdentityKeys } = outlookAdapter();

    render(<App hostAdapter={adapter} />);

    await waitFor(() => expect(lastProps(SaveView).documentIdentity).toEqual(SAVED_EMAIL));
    expect(getEmailIdentityKeys).toHaveBeenCalledTimes(1);
    expect(resolveEmailIdentity).toHaveBeenCalledWith(KEYS);
    expect(lastProps(SaveView).resolvedDocumentId).toBe(EML_ID);
    expect(resolveDocumentIdentity).not.toHaveBeenCalled();
  });

  it('any non-saved answer leaves NO identity — the ordinary save form', async () => {
    resolveEmailIdentity.mockResolvedValue(null);
    const { adapter } = outlookAdapter();

    render(<App hostAdapter={adapter} />);

    await waitFor(() => expect(resolveEmailIdentity).toHaveBeenCalled());
    await waitFor(() => expect(lastProps(SaveView).documentIdentity).toBeUndefined());
    expect(lastProps(SaveView).resolvedDocumentId).toBeUndefined();
  });

  it('a failure reading the keys also leaves no identity, and never throws', async () => {
    const { adapter, getEmailIdentityKeys } = outlookAdapter();
    getEmailIdentityKeys.mockRejectedValue(new Error('not read mode'));
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      render(<App hostAdapter={adapter} />);

      await waitFor(() => expect(getEmailIdentityKeys).toHaveBeenCalled());
      await waitFor(() => expect(lastProps(SaveView).documentIdentity).toBeUndefined());
      expect(resolveEmailIdentity).not.toHaveBeenCalled();
    } finally {
      warn.mockRestore();
    }
  });

  it('after a pane save, the identity becomes the saved .eml (read by its id)', async () => {
    resolveEmailIdentity.mockResolvedValue(null);
    const adopted: DocumentIdentityOutcome = { ...SAVED_EMAIL, documentId: SAVED_ID };
    identityOfSavedDocument.mockResolvedValue(adopted);
    const { adapter } = outlookAdapter();

    render(<App hostAdapter={adapter} />);
    await waitFor(() => expect(lastProps(SaveView).documentIdentity).toBeUndefined());

    await act(async () => {
      lastProps(SaveView).onComplete(`{${SAVED_ID.toUpperCase()}}`, 'https://aka.ms/spe-openfilelocation');
    });

    await waitFor(() => expect(lastProps(SaveView).documentIdentity).toEqual(adopted));
    expect(identityOfSavedDocument).toHaveBeenCalledWith(`{${SAVED_ID.toUpperCase()}}`);
    expect(lastProps(SaveView).resolvedDocumentId).toBe(SAVED_ID);
  });

  it('"Try again" re-runs the EMAIL lookup in Outlook', async () => {
    resolveEmailIdentity.mockResolvedValue(null);
    const { adapter } = outlookAdapter();

    render(<App hostAdapter={adapter} />);
    await waitFor(() => expect(resolveEmailIdentity).toHaveBeenCalledTimes(1));

    resolveEmailIdentity.mockResolvedValue(SAVED_EMAIL);
    await act(async () => {
      lastProps(SaveView).onRetryDocumentIdentity();
    });

    await waitFor(() => expect(resolveEmailIdentity).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(lastProps(SaveView).documentIdentity).toEqual(SAVED_EMAIL));
  });

  it('Find and To Do get the saved .eml as the document', async () => {
    resolveEmailIdentity.mockResolvedValue(SAVED_EMAIL);
    const { adapter } = outlookAdapter();

    const view = render(<App hostAdapter={adapter} initialTab="find" />);
    await waitFor(() => expect(lastProps(FindView).documentIdentity).toEqual(SAVED_EMAIL));
    expect(lastProps(FindView).savedDocumentId).toBe(EML_ID);
    view.unmount();

    render(<App hostAdapter={adapter} initialTab="createTodo" />);
    await waitFor(() => expect(lastProps(CreateTodoView).savedContext?.documentId).toBe(EML_ID));
    expect(lastProps(CreateTodoView).savedContext?.regardingRecordId).toBe('22222222-2222-2222-2222-222222222222');
  });
});

describe('App — Word unchanged (task 120)', () => {
  it('never attempts the email lookup, and a save does not re-read the identity by id', async () => {
    resolveDocumentIdentity.mockResolvedValue({ kind: 'new', reason: 'not_spaarke_document' });
    const { adapter, getEmailIdentityKeys } = wordAdapter();

    render(<App hostAdapter={adapter} />);
    await waitFor(() => expect(resolveDocumentIdentity).toHaveBeenCalled());

    await act(async () => {
      lastProps(SaveView).onComplete(SAVED_ID, 'https://contoso.sharepoint.com/a.docx');
    });

    expect(getEmailIdentityKeys).not.toHaveBeenCalled();
    expect(resolveEmailIdentity).not.toHaveBeenCalled();
    expect(identityOfSavedDocument).not.toHaveBeenCalled();
  });
});

describe('App — one open gate for every tab (task 120)', () => {
  it('on the web (no openBrowserWindow) the tabs may open records when ORG_URL is set', async () => {
    process.env.ORG_URL = ORG;
    resolveEmailIdentity.mockResolvedValue(null);
    const { adapter } = outlookAdapter({ canOpenBrowserWindow: false });

    const view = render(<App hostAdapter={adapter} initialTab="find" />);
    await waitFor(() => expect(FindView).toHaveBeenCalled());
    expect(lastProps(FindView).canOpenRecord).toBe(true);
    view.unmount();

    render(<App hostAdapter={adapter} initialTab="createTodo" />);
    await waitFor(() => expect(CreateTodoView).toHaveBeenCalled());
    expect(lastProps(CreateTodoView).canOpenRecord).toBe(true);
  });

  it('without ORG_URL nothing is offered, whatever the host can open', async () => {
    resolveEmailIdentity.mockResolvedValue(null);
    const { adapter } = outlookAdapter({ canOpenBrowserWindow: true });

    render(<App hostAdapter={adapter} initialTab="find" />);
    await waitFor(() => expect(FindView).toHaveBeenCalled());
    expect(lastProps(FindView).canOpenRecord).toBe(false);
  });
});
