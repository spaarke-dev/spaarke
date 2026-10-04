/**
 * Unit tests for word/commands/index.ts (spaarkeai-word-add-in-r1 task 037 / FR-17; reworked by task 089 for UAT
 * round 3 — UAT-9 Quick Save, UAT-10 Share → Open Spaarke).
 *
 * Covers:
 *   - `event.completed()` fires on EVERY path of every command — success, extraction/auth/network failure — asserted
 *     here, not by inspection (omitting it on any path leaves the Word ribbon spinner stuck forever).
 *   - Quick Save resolves the open document's identity FIRST: a resolved document is a VERSION save
 *     (`existingDocumentId` + `isNewVersion`); a new one is a CREATE with the pane's name rule and `allowRename`; any
 *     other outcome saves nothing and says why. After a success the open document is marked with the saved id.
 *   - Every notification says what happened in words; a refusal shows the SERVER's message, never the old fixed
 *     "Failed to save" text.
 *   - Open Spaarke opens the Console URL built from the build settings, through the capability-chosen opener.
 *
 * Collaborators with side effects (auth, the API client, the adapter factory, identity resolution and the stamp
 * write) are module-mocked; the PURE request/message builders in `quickSaveHelpers` run for real so the tests pin
 * the actual request body and notification text.
 *
 * `word/commands/index.ts` caches its bootstrap state in module-level closures, so each test re-`require`s a FRESH
 * module instance (`jest.resetModules()`).
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
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
const mockApiGet = jest.fn();
jest.mock('@shared/services', () => ({
  authService: { initialize: (...args: unknown[]) => mockAuthInitialize(...args) },
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

const RESOLVED_ID = '2bcfc5d2-0000-4000-8000-000000000001';
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

describe('word/commands/index.ts', () => {
  let mockAdapter: IHostAdapter;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;
  let displayDialogAsync: jest.Mock;
  let showAsTaskpane: jest.Mock;

  beforeEach(() => {
    jest.resetModules();
    jest.clearAllMocks();

    mockAdapter = createMockAdapter();
    mockAuthInitialize.mockResolvedValue(undefined);
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

    displayDialogAsync = jest.fn((_url: string, _options: unknown, callback?: (result: unknown) => void) => {
      callback?.({ status: 'succeeded', value: {} });
    });
    global.Office.context.ui.displayDialogAsync =
      displayDialogAsync as unknown as typeof Office.context.ui.displayDialogAsync;

    showAsTaskpane = jest.fn().mockResolvedValue(undefined);
    (global as unknown as { Office: Record<string, unknown> }).Office.addin = { showAsTaskpane };

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  describe('showTaskPane', () => {
    it('completes immediately', () => {
      const event = createMockEvent();
      commands.showTaskPane(event);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });

  // -----------------------------------------------------------------------------------------
  // quickSave
  // -----------------------------------------------------------------------------------------
  describe('quickSave — an UNRESOLVED document is a keep-both CREATE', () => {
    it("bootstraps (explicit 'word' host), creates with the pane's file-name rule + allowRename, stamps the created id, names the file, and completes", async () => {
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockAuthInitialize).toHaveBeenCalledTimes(1);
      expect(mockApiConfigure).toHaveBeenCalledTimes(1);
      expect(mockRegisterAdapter).toHaveBeenCalledWith('word', expect.anything());
      expect(mockCreateAndInitialize).toHaveBeenCalledWith('word');

      // Identity was resolved (URL, then the stamp) before anything was sent.
      expect(mockAdapter.getDocumentUrl).toHaveBeenCalledTimes(1);
      expect(mockAdapter.readDocumentStamp).toHaveBeenCalledTimes(1);

      expect(mockApiPost).toHaveBeenCalledTimes(1);
      const [route, body] = mockApiPost.mock.calls[0]!;
      expect(route).toBe('/api/office/save');
      expect(body.contentType).toBe('Document');
      expect(body.idempotencyKey).toBe('idem-key-1');
      expect(body.document).toMatchObject({ fileName: 'My Document.docx', title: 'My Document', allowRename: true });
      expect(body.document).not.toHaveProperty('existingDocumentId');
      expect(body.document).not.toHaveProperty('isNewVersion');
      expect(body).not.toHaveProperty('targetEntity');

      // The job names the created document; the open document is marked with it.
      expect(mockApiGet).toHaveBeenCalledWith('/api/office/jobs/job-1');
      expect(mockWriteIdentityStampAfterSave).toHaveBeenCalledWith(mockAdapter, CREATED_ID);

      expect(lastNotifyStatus(displayDialogAsync)).toBe('info');
      expect(lastNotifyMessage(displayDialogAsync)).toBe("Saved to Spaarke as 'My Document.docx'.");
      expect(showAsTaskpane).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('when the server KEPT BOTH under another name, the notification names the file actually created', async () => {
      (mockAdapter.getSubject as jest.Mock).mockResolvedValue('Untitled Document');
      mockApiGet.mockResolvedValue(completedJob(CREATED_ID, 'Untitled Document 1.docx'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost.mock.calls[0]![1].document.fileName).toBe('Untitled Document.docx');
      const message = lastNotifyMessage(displayDialogAsync)!;
      expect(message).toContain("'Untitled Document 1.docx'");
      expect(message).toMatch(/both were kept/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('a name collision on an unresolved document is never turned into a version save (#1005: a name is not identity)', async () => {
      // Even when the file name matches an existing Spaarke document, an unresolved identity creates.
      (mockAdapter.getSubject as jest.Mock).mockResolvedValue('The Newfound Importance of Knowledge Management');
      await commands.quickSave(createMockEvent());

      expect(mockApiPost.mock.calls[0]![1].document).not.toHaveProperty('existingDocumentId');
      expect(mockApiPost.mock.calls[0]![1].document.allowRename).toBe(true);
    });
  });

  describe('quickSave — a RESOLVED document is a VERSION save', () => {
    it('resolved by its identity stamp (a local file): sends existingDocumentId + isNewVersion and says "Saved a new version"', async () => {
      (mockAdapter.readDocumentStamp as jest.Mock).mockResolvedValue(RESOLVED_ID);
      mockApiGet.mockResolvedValue(completedJob(RESOLVED_ID, 'My Document.docx'));
      const event = createMockEvent();

      await commands.quickSave(event);

      const body = mockApiPost.mock.calls[0]![1];
      expect(body.document).toMatchObject({ existingDocumentId: RESOLVED_ID, isNewVersion: true });
      expect(body.document).not.toHaveProperty('allowRename');
      // The version key names the document, so a version and a create of the same bytes never share a key.
      expect(mockComputeQuickSaveIdempotencyKey).toHaveBeenCalledWith(
        expect.objectContaining({ kind: 'document', existingDocumentId: RESOLVED_ID })
      );

      expect(mockWriteIdentityStampAfterSave).toHaveBeenCalledWith(mockAdapter, RESOLVED_ID);
      expect(lastNotifyStatus(displayDialogAsync)).toBe('info');
      expect(lastNotifyMessage(displayDialogAsync)).toBe("Saved a new version of 'My Document.docx'.");
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it("resolved by its cloud URL: versions that document and names it by the resolver's document name", async () => {
      (mockAdapter.getDocumentUrl as jest.Mock).mockResolvedValue('https://contoso.sharepoint.com/Brief.docx');
      mockResolveDocumentIdentity.mockResolvedValue({
        kind: 'resolved',
        documentId: RESOLVED_ID,
        documentName: 'Engagement Brief',
        fileName: 'Brief.docx',
        relatedRecord: null,
      });
      mockApiGet.mockResolvedValue(completedJob(RESOLVED_ID, 'Brief.docx'));

      await commands.quickSave(createMockEvent());

      expect(mockResolveDocumentIdentity).toHaveBeenCalledWith('https://contoso.sharepoint.com/Brief.docx');
      expect(mockApiPost.mock.calls[0]![1].document.existingDocumentId).toBe(RESOLVED_ID);
      expect(lastNotifyMessage(displayDialogAsync)).toBe("Saved a new version of 'Engagement Brief'.");
    });

    it('a version save whose job cannot be read still stamps the known document and reports the version', async () => {
      (mockAdapter.readDocumentStamp as jest.Mock).mockResolvedValue(RESOLVED_ID);
      mockApiGet.mockRejectedValue(new Error('job read failed'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockWriteIdentityStampAfterSave).toHaveBeenCalledWith(mockAdapter, RESOLVED_ID);
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/^Saved a new version of/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });

  describe('quickSave — identity it must not guess about saves NOTHING and says why', () => {
    it.each([
      [{ kind: 'conflict' }, /conflicting records/],
      [{ kind: 'indeterminate', reason: 'unavailable' }, /could not check/],
      [{ kind: 'denied' }, /can't add a version/],
      [{ kind: 'error', message: 'boom' }, /boom/],
    ])('%o → no POST, an error notification with the reason', async (outcome, reason) => {
      mockResolveDocumentIdentity.mockResolvedValue(outcome);
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(mockWriteIdentityStampAfterSave).not.toHaveBeenCalled();
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(reason);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('identity resolution that THROWS is reported, not turned into a create', async () => {
      (mockAdapter.getDocumentUrl as jest.Mock).mockRejectedValue(new Error('url read failed'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/url read failed/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });

  describe('quickSave — failures say what failed, in words', () => {
    it("NEGATIVE: a refusal shows the SERVER's message (403), never the fixed 'Failed to save' text, and stamps nothing", async () => {
      mockApiPost.mockRejectedValue(
        serverRefusal({ status: 403, title: 'Forbidden', detail: 'You do not have write access to this document.' })
      );
      const event = createMockEvent();

      await commands.quickSave(event);

      const message = lastNotifyMessage(displayDialogAsync)!;
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(message).toContain('You do not have write access to this document.');
      expect(message).not.toMatch(/Failed to save/);
      expect(mockWriteIdentityStampAfterSave).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('a rate-limited save (429) shows the server reason too', async () => {
      mockApiPost.mockRejectedValue(serverRefusal({ status: 429, title: 'Too Many Requests' }));
      await commands.quickSave(createMockEvent());
      expect(lastNotifyMessage(displayDialogAsync)).toBe('Spaarke did not save this document: Too Many Requests (429)');
    });

    it('a document that cannot be read from Word says so, and posts nothing', async () => {
      (mockAdapter.getDocumentContent as jest.Mock).mockRejectedValue({
        code: 'CONTENT_RETRIEVAL_FAILED',
        message: 'getFileAsync failed',
      });
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toBe(
        "Couldn't read this document from Word, so nothing was saved (getFileAsync failed)."
      );
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('a network failure (no server reason) says Spaarke could not be reached', async () => {
      mockApiPost.mockRejectedValue(new TypeError('Failed to fetch'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/^Couldn't reach Spaarke.*Failed to fetch/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('an auth bootstrap failure posts nothing, says it could not connect, and completes', async () => {
      mockAuthInitialize.mockRejectedValue(new Error('auth failed'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/^Couldn't connect to Spaarke.*auth failed/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it("a job that ended Failed after the save was accepted reports the job's reason, not a success", async () => {
      mockApiGet.mockResolvedValue({ status: 'Failed', error: { message: 'Upload finalization failed' } });
      await commands.quickSave(createMockEvent());

      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/Upload finalization failed/);
      expect(mockWriteIdentityStampAfterSave).not.toHaveBeenCalled();
    });

    it('a stamp-write failure never turns a successful save into an error (the helper is non-fatal)', async () => {
      mockWriteIdentityStampAfterSave.mockResolvedValue('failed');
      await commands.quickSave(createMockEvent());
      expect(lastNotifyStatus(displayDialogAsync)).toBe('info');
    });

    it('a very long server reason is bounded before it goes into the dialog URL', async () => {
      mockApiPost.mockRejectedValue(serverRefusal({ status: 400, title: 'Bad', detail: 'x'.repeat(5000) }));
      await commands.quickSave(createMockEvent());
      const message = lastNotifyMessage(displayDialogAsync)!;
      expect(message.length).toBeLessThanOrEqual(400);
      expect(message.endsWith('…')).toBe(true);
    });

    it('error notifications get a larger window than a success', async () => {
      mockApiPost.mockRejectedValue(serverRefusal({ status: 403, title: 'Forbidden', detail: 'No.' }));
      await commands.quickSave(createMockEvent());
      expect(lastNotifyCall(displayDialogAsync)![1]).toMatchObject({ height: 30, width: 35 });
    });

    it('never opens the task pane on any path', async () => {
      mockApiPost.mockRejectedValue(new Error('network down'));
      await commands.quickSave(createMockEvent());
      expect(showAsTaskpane).not.toHaveBeenCalled();
    });
  });

  // -----------------------------------------------------------------------------------------
  // openSpaarke (replaces shareDocument — UAT-10)
  // -----------------------------------------------------------------------------------------
  describe('openSpaarke', () => {
    const originalEnv = { ORG_URL: process.env.ORG_URL, SPAARKE_APP_NAME: process.env.SPAARKE_APP_NAME };
    let openBrowserWindow: jest.Mock;
    let isSetSupported: jest.Mock;

    beforeEach(() => {
      process.env.ORG_URL = 'https://spaarkedev1.crm.dynamics.com';
      process.env.SPAARKE_APP_NAME = 'sprk_MatterManagement';
      openBrowserWindow = jest.fn();
      isSetSupported = jest.fn().mockReturnValue(true);
      global.Office.context.ui.openBrowserWindow =
        openBrowserWindow as unknown as typeof Office.context.ui.openBrowserWindow;
      (global.Office.context as unknown as { requirements: unknown }).requirements = { isSetSupported };
    });

    afterEach(() => {
      process.env.ORG_URL = originalEnv.ORG_URL;
      process.env.SPAARKE_APP_NAME = originalEnv.SPAARKE_APP_NAME;
    });

    it('opens Matter Management on the Workspace (the Console) via openBrowserWindow, with no sign-in or API call, and completes', () => {
      const event = createMockEvent();

      commands.openSpaarke(event);

      expect(openBrowserWindow).toHaveBeenCalledWith(
        'https://spaarkedev1.crm.dynamics.com/main.aspx?appname=sprk_MatterManagement&pagetype=webresource&webresourceName=sprk_spaarkeai'
      );
      expect(isSetSupported).toHaveBeenCalledWith('OpenBrowserWindowApi', '1.1');
      expect(mockAuthInitialize).not.toHaveBeenCalled();
      expect(mockApiPost).not.toHaveBeenCalled();
      expect(mockApiGet).not.toHaveBeenCalled();
      expect(displayDialogAsync).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('falls back to window.open where openBrowserWindow is not supported (Office on the web)', () => {
      isSetSupported.mockReturnValue(false);
      const fakeWindow = { opener: 'pane' } as unknown as Window;
      const windowOpen = jest.spyOn(window, 'open').mockReturnValue(fakeWindow);
      const event = createMockEvent();

      try {
        commands.openSpaarke(event);
        expect(openBrowserWindow).not.toHaveBeenCalled();
        expect(windowOpen).toHaveBeenCalledWith(expect.stringContaining('webresourceName=sprk_spaarkeai'), '_blank');
        expect((fakeWindow as unknown as { opener: unknown }).opener).toBeNull();
        expect(event.completed).toHaveBeenCalledTimes(1);
      } finally {
        windowOpen.mockRestore();
      }
    });

    it('a blocked window is reported, not swallowed', () => {
      isSetSupported.mockReturnValue(false);
      const windowOpen = jest.spyOn(window, 'open').mockReturnValue(null);
      try {
        commands.openSpaarke(createMockEvent());
        expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
        expect(lastNotifyMessage(displayDialogAsync)).toMatch(/blocked/);
      } finally {
        windowOpen.mockRestore();
      }
    });

    it.each([
      ['ORG_URL', { ORG_URL: '' }],
      ['the app name', { SPAARKE_APP_NAME: '' }],
    ])('without %s it opens nothing, says it is not set up, and completes', (_label, env) => {
      Object.assign(process.env, env);
      const event = createMockEvent();

      commands.openSpaarke(event);

      expect(openBrowserWindow).not.toHaveBeenCalled();
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/isn't set up/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when the opener throws', () => {
      openBrowserWindow.mockImplementation(() => {
        throw new Error('host refused');
      });
      const event = createMockEvent();

      commands.openSpaarke(event);

      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/host refused/);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });

  // -----------------------------------------------------------------------------------------
  // Notification helper resilience — a broken notification surface must never block completion.
  // -----------------------------------------------------------------------------------------
  describe('notify() resilience', () => {
    it('quickSave still completes when Office.context.ui is unavailable', async () => {
      const original = global.Office.context.ui.displayDialogAsync;
      // @ts-expect-error — simulating the function being entirely absent on this host.
      delete global.Office.context.ui.displayDialogAsync;
      const event = createMockEvent();

      try {
        await commands.quickSave(event);
        expect(event.completed).toHaveBeenCalledTimes(1);
      } finally {
        global.Office.context.ui.displayDialogAsync = original;
      }
    });

    it('quickSave still completes when displayDialogAsync throws synchronously', async () => {
      global.Office.context.ui.displayDialogAsync = jest.fn(() => {
        throw new Error('displayDialogAsync failed');
      }) as unknown as typeof Office.context.ui.displayDialogAsync;
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });
});
