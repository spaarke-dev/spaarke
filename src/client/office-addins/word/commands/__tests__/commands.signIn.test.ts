/**
 * GitHub #1571 review F1 (spaarkeai-word-add-in-r1 task 136, fix round 1): the Word ribbon's Quick Save signs in from
 * the click. A ribbon click IS a user gesture, so when the silent acquire found no token it calls `signIn()` ONCE; when
 * there is still no token it saves nothing and says "Open the Spaarke pane and sign in, then try again." — never
 * "Check your connection". A 401 that survives the request's retry says the same.
 *
 * Same harness as commands.test.ts, with the auth double's token and sign-in programmable.
 */

import type { IHostAdapter } from '@shared/adapters';

const mockRegisterAdapter = jest.fn();
const mockCreateAndInitialize = jest.fn();
jest.mock('@shared/adapters', () => ({
  HostAdapterFactory: {
    registerAdapter: (...args: unknown[]) => mockRegisterAdapter(...args),
    createAndInitialize: (...args: unknown[]) => mockCreateAndInitialize(...args),
  },
}));

jest.mock('@shared/adapters/WordAdapter', () => ({
  // Never constructed — HostAdapterFactory itself is mocked.
  WordAdapter: class MockWordAdapter {},
}));

const mockAuthInitialize = jest.fn();
const mockGetAccessToken = jest.fn();
const mockSignIn = jest.fn();
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
const mockApiGet = jest.fn();
jest.mock('@shared/services', () => ({
  authService: {
    initialize: (...args: unknown[]) => mockAuthInitialize(...args),
    // #1571 review F1: Quick Save checks for a token (and signs in once from the click when there is none).
    getAccessToken: (...args: unknown[]) => mockGetAccessToken(...args),
    signIn: (...args: unknown[]) => mockSignIn(...args),
  },
  apiClient: {
    configure: (...args: unknown[]) => mockApiConfigure(...args),
    post: (...args: unknown[]) => mockApiPost(...args),
    get: (...args: unknown[]) => mockApiGet(...args),
  },
}));

const mockResolveDocumentIdentity = jest.fn();
const mockWriteIdentityStampAfterSave = jest.fn();
jest.mock('@shared/taskpane/services/documentIdentityService', () => {
  const actual = jest.requireActual('@shared/taskpane/services/documentIdentityService');
  return {
    // The precedence rule is pure — run it for real.
    applyStampPrecedence: actual.applyStampPrecedence,
    resolveDocumentIdentity: (...args: unknown[]) => mockResolveDocumentIdentity(...args),
    writeIdentityStampAfterSave: (...args: unknown[]) => mockWriteIdentityStampAfterSave(...args),
  };
});

const mockComputeQuickSaveIdempotencyKey = jest.fn();
jest.mock('@shared/taskpane/services/quickSaveHelpers', () => ({
  ...jest.requireActual('@shared/taskpane/services/quickSaveHelpers'),
  computeQuickSaveIdempotencyKey: (...args: unknown[]) => mockComputeQuickSaveIdempotencyKey(...args),
}));

const CREATED_ID = '1eea00c5-0000-4000-8000-000000000002';

function createMockEvent(): Office.AddinCommands.Event {
  return { completed: jest.fn() } as unknown as Office.AddinCommands.Event;
}

function createMockAdapter(overrides: Partial<Record<keyof IHostAdapter, unknown>> = {}): IHostAdapter {
  return {
    getHostType: jest.fn().mockReturnValue('word'),
    getItemType: jest.fn().mockReturnValue('document'),
    getItemId: jest.fn(),
    getSubject: jest.fn().mockResolvedValue('My Document'),
    getBody: jest.fn(),
    getAttachments: jest.fn().mockResolvedValue([]),
    getAttachmentContent: jest.fn(),
    getSenderEmail: jest.fn().mockResolvedValue(''),
    getRecipients: jest.fn().mockResolvedValue([]),
    getDocumentContent: jest.fn().mockResolvedValue(new Uint8Array([0x50, 0x4b, 0x03, 0x04]).buffer),
    getDocumentUrl: jest.fn().mockResolvedValue(null),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    writeDocumentStamp: jest.fn().mockResolvedValue('written'),
    getCapabilities: jest.fn().mockReturnValue({ canReadDocumentStamp: true, canWriteDocumentStamp: true }),
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: jest.fn().mockReturnValue(true),
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    ...overrides,
  } as unknown as IHostAdapter;
}

/** A fake Office dialog handle: records its handlers so a test can play the page / the user. */
interface FakeDialog {
  close: jest.Mock;
  handlers: Record<string, (arg: unknown) => void>;
  addEventHandler: jest.Mock;
}

function createFakeDialog(): FakeDialog {
  const handlers: Record<string, (arg: unknown) => void> = {};
  const close = jest.fn();
  const addEventHandler = jest.fn((type: string, handler: (arg: unknown) => void) => {
    handlers[type] = handler;
  });
  return { close, handlers, addEventHandler };
}

function lastNotifyCall(displayDialogAsync: jest.Mock): unknown[] | undefined {
  return displayDialogAsync.mock.calls[displayDialogAsync.mock.calls.length - 1];
}

/** Reads the `message` query param off the URL `displayDialogAsync` was called with. */
function lastNotifyMessage(displayDialogAsync: jest.Mock): string | null {
  const call = lastNotifyCall(displayDialogAsync);
  return call ? new URL(call[0] as string).searchParams.get('message') : null;
}

/** Reads the `status` query param off the URL `displayDialogAsync` was called with. */
function lastNotifyStatus(displayDialogAsync: jest.Mock): string | null {
  const call = lastNotifyCall(displayDialogAsync);
  return call ? new URL(call[0] as string).searchParams.get('status') : null;
}

/** The job read after a save: Completed, naming the document it landed on and where it is stored. */
function completedJob(documentId: string, fileName: string) {
  return {
    status: 'Completed',
    result: {
      artifact: {
        id: documentId,
        webUrl: `https://contoso.sharepoint.com/:w:r/contentstorage/CSP_1/_layouts/15/doc2.aspx?sourcedoc=%7Bx%7D&file=${encodeURIComponent(fileName)}&action=default`,
      },
    },
  };
}

/** A thrown ApiClientError's shape: the server's ProblemDetails on `.error`. */
function serverRefusal(problem: Record<string, unknown>): Error {
  return Object.assign(new Error(String(problem.detail ?? problem.title)), { name: 'ApiClientError', error: problem });
}

describe('word/commands/index.ts — Quick Save signs in from the ribbon click (#1571 review F1)', () => {
  let mockAdapter: IHostAdapter;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;
  let displayDialogAsync: jest.Mock;
  let dialogs: FakeDialog[];
  let showAsTaskpane: jest.Mock;

  /**
   * Runs Quick Save to its end, then plays the user closing the (latest) dialog — the point at which the command's
   * `event.completed()` is due (task 110). A run that never opened a dialog has nothing to close.
   */
  async function runQuickSave(event: Office.AddinCommands.Event): Promise<void> {
    await commands.quickSave(event);
    const latest = dialogs[dialogs.length - 1];
    latest?.handlers['dialogEventReceived']?.({ error: 12006 });
  }

  beforeEach(() => {
    jest.resetModules();
    jest.clearAllMocks();

    mockAdapter = createMockAdapter();
    mockAuthInitialize.mockResolvedValue(undefined);
    mockGetAccessToken.mockResolvedValue('token');
    mockSignIn.mockResolvedValue(undefined);
    mockApiConfigure.mockReturnValue(undefined);
    mockCreateAndInitialize.mockResolvedValue(mockAdapter);
    mockComputeQuickSaveIdempotencyKey.mockResolvedValue('idem-key-1');
    mockResolveDocumentIdentity.mockResolvedValue({ kind: 'new', reason: 'not_cloud_document' });
    mockWriteIdentityStampAfterSave.mockResolvedValue('written');
    mockApiPost.mockResolvedValue({
      success: true,
      duplicate: false,
      jobId: 'job-1',
      statusUrl: '/api/office/jobs/job-1',
    });
    mockApiGet.mockResolvedValue(completedJob(CREATED_ID, 'My Document.docx'));

    dialogs = [];
    displayDialogAsync = jest.fn((_url: string, _options: unknown, callback?: (result: unknown) => void) => {
      const dialog = createFakeDialog();
      dialogs.push(dialog);
      callback?.({ status: 'succeeded', value: dialog });
    });
    global.Office.context.ui.displayDialogAsync =
      displayDialogAsync as unknown as typeof Office.context.ui.displayDialogAsync;

    showAsTaskpane = jest.fn().mockResolvedValue(undefined);
    (global as unknown as { Office: Record<string, unknown> }).Office.addin = { showAsTaskpane };

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  const SIGN_IN_MESSAGE = 'Open the Spaarke pane and sign in, then try again.';

  it('with a silent token: no sign-in, the save runs', async () => {
    await runQuickSave(createMockEvent());

    expect(mockSignIn).not.toHaveBeenCalled();
    expect(mockApiPost).toHaveBeenCalledTimes(1);
  });

  it('with no silent token: signs in ONCE from the click, then saves', async () => {
    mockGetAccessToken.mockResolvedValueOnce(null).mockResolvedValue('token');

    await runQuickSave(createMockEvent());

    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockApiPost).toHaveBeenCalledTimes(1);
    expect(lastNotifyStatus(displayDialogAsync)).toBe('success');
  });

  it('a sign-in that does not complete saves nothing and says to sign in from the pane', async () => {
    mockGetAccessToken.mockResolvedValue(null);
    mockSignIn.mockRejectedValue(new Error('Sign-in did not complete.'));
    const event = createMockEvent();

    await runQuickSave(event);

    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockApiPost).not.toHaveBeenCalled();
    expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
    expect(lastNotifyMessage(displayDialogAsync)).toBe(SIGN_IN_MESSAGE);
    expect(event.completed).toHaveBeenCalledTimes(1);
  });

  it('a 401 that survived the retry says to sign in from the pane, not "check your connection"', async () => {
    mockApiPost.mockRejectedValue(
      serverRefusal({
        title: 'Unauthorized',
        status: 401,
        detail: 'Authentication required',
        errorCode: 'OFFICE_AUTH_001',
      })
    );

    await runQuickSave(createMockEvent());

    expect(lastNotifyMessage(displayDialogAsync)).toBe(SIGN_IN_MESSAGE);
  });

  it('NEGATIVE: a network failure still says Spaarke could not be reached', async () => {
    mockApiPost.mockRejectedValue(new TypeError('Failed to fetch'));

    await runQuickSave(createMockEvent());

    expect(lastNotifyMessage(displayDialogAsync)).toMatch(/Check your connection/);
  });
});
