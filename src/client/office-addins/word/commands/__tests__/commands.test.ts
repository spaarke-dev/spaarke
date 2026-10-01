/**
 * Unit tests for word/commands/index.ts (spaarkeai-word-add-in-r1 task 037 / FR-17).
 *
 * Covers the task's hard acceptance criteria:
 *   - `event.completed()` fires on EVERY path for both commands — success, extraction/auth/network
 *     failure — asserted here, not by inspection (the task's own binding requirement).
 *   - `quickSave` bootstraps auth + apiClient + a WordAdapter (via the factory, explicit 'word' host),
 *     extracts bytes through `WordAdapter.getDocumentContent()`, computes the idempotency key via
 *     `computeQuickSaveIdempotencyKey`, and posts an unfiled CREATE request — never opening the pane.
 *   - `shareDocument` resolves the open document's identity, mints a share link, and reports outcome —
 *     with the ONE deliberate exception (no resolved identity -> open the pane, stated explicitly).
 *
 * All collaborators are module-mocked so this suite exercises ONLY this file's own orchestration
 * (NFR-10: commands are thin entry points over shared services, which have their own test suites).
 *
 * `word/commands/index.ts` caches its bootstrap state (`bootstrapped`, `wordAdapter`) in module-level
 * closures, matching `outlook/commands/index.ts`'s own convention — so each test re-`require`s a FRESH
 * module instance (`jest.resetModules()`) rather than relying on the ES import cached at file load;
 * otherwise a later test's "auth bootstrap fails" scenario would silently no-op against an
 * already-bootstrapped module and prove nothing.
 */

import type { IHostAdapter } from '@shared/adapters';

// ---------------------------------------------------------------------------------------------
// Module mocks — declared before importing the module under test. jest hoists `jest.mock()`
// calls above imports, but the mock FACTORIES below close over these `const` bindings, which is
// safe because the closures are only invoked when the mocked module is actually required.
// ---------------------------------------------------------------------------------------------

const mockRegisterAdapter = jest.fn();
const mockCreateAndInitialize = jest.fn();
jest.mock('@shared/adapters', () => ({
  HostAdapterFactory: {
    registerAdapter: (...args: unknown[]) => mockRegisterAdapter(...args),
    createAndInitialize: (...args: unknown[]) => mockCreateAndInitialize(...args),
  },
}));

jest.mock('@shared/adapters/WordAdapter', () => ({
  // Never actually constructed in these tests — HostAdapterFactory itself is mocked, so this only
  // needs to exist as SOME value `registerAdapter('word', WordAdapter)` can be called with.
  WordAdapter: class MockWordAdapter {},
}));

const mockAuthInitialize = jest.fn();
const mockApiConfigure = jest.fn();
const mockApiPost = jest.fn();
jest.mock('@shared/services', () => ({
  authService: { initialize: (...args: unknown[]) => mockAuthInitialize(...args) },
  apiClient: {
    configure: (...args: unknown[]) => mockApiConfigure(...args),
    post: (...args: unknown[]) => mockApiPost(...args),
  },
}));

const mockResolveDocumentIdentity = jest.fn();
const mockApplyStampPrecedence = jest.fn();
jest.mock('@shared/taskpane/services/documentIdentityService', () => ({
  resolveDocumentIdentity: (...args: unknown[]) => mockResolveDocumentIdentity(...args),
  applyStampPrecedence: (...args: unknown[]) => mockApplyStampPrecedence(...args),
}));

const mockBuildDocumentSaveRequest = jest.fn();
const mockComputeQuickSaveIdempotencyKey = jest.fn();
const mockArrayBufferToBase64 = jest.fn();
jest.mock('@shared/taskpane/services/quickSaveHelpers', () => ({
  buildDocumentSaveRequest: (...args: unknown[]) => mockBuildDocumentSaveRequest(...args),
  computeQuickSaveIdempotencyKey: (...args: unknown[]) => mockComputeQuickSaveIdempotencyKey(...args),
  arrayBufferToBase64: (...args: unknown[]) => mockArrayBufferToBase64(...args),
}));

const mockMintDocumentShareLink = jest.fn();
jest.mock('@shared/taskpane/services/shareLinkService', () => ({
  mintDocumentShareLink: (...args: unknown[]) => mockMintDocumentShareLink(...args),
}));

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
    getDocumentContent: jest.fn().mockResolvedValue(new ArrayBuffer(4)),
    getDocumentUrl: jest.fn().mockResolvedValue(null),
    readDocumentStamp: jest.fn().mockResolvedValue(null),
    getCapabilities: jest.fn().mockReturnValue({ canReadDocumentStamp: true }),
    initialize: jest.fn().mockResolvedValue(undefined),
    isInitialized: jest.fn().mockReturnValue(true),
    insertLink: jest.fn(),
    attachFile: jest.fn(),
    composeNewEmail: jest.fn(),
    ...overrides,
  } as unknown as IHostAdapter;
}

/** Reads the `message` query param off the URL `displayDialogAsync` was called with. */
function lastNotifyMessage(displayDialogAsync: jest.Mock): string | null {
  const call = displayDialogAsync.mock.calls[displayDialogAsync.mock.calls.length - 1];
  if (!call) return null;
  const url = new URL(call[0] as string);
  return url.searchParams.get('message');
}

/** Reads the `status` query param off the URL `displayDialogAsync` was called with. */
function lastNotifyStatus(displayDialogAsync: jest.Mock): string | null {
  const call = displayDialogAsync.mock.calls[displayDialogAsync.mock.calls.length - 1];
  if (!call) return null;
  const url = new URL(call[0] as string);
  return url.searchParams.get('status');
}

describe('word/commands/index.ts', () => {
  let mockAdapter: IHostAdapter;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let commands: any;
  let displayDialogAsync: jest.Mock;
  let showAsTaskpane: jest.Mock;

  beforeEach(() => {
    jest.resetModules();

    mockAdapter = createMockAdapter();
    mockAuthInitialize.mockResolvedValue(undefined);
    mockApiConfigure.mockReturnValue(undefined);
    mockCreateAndInitialize.mockResolvedValue(mockAdapter);
    mockArrayBufferToBase64.mockReturnValue('BASE64BYTES');
    mockComputeQuickSaveIdempotencyKey.mockResolvedValue('idem-key-1');
    mockBuildDocumentSaveRequest.mockReturnValue({ contentType: 'Document', document: { fileName: 'x.docx' } });
    mockResolveDocumentIdentity.mockResolvedValue({ kind: 'new', reason: 'not_cloud_document' });
    mockApplyStampPrecedence.mockImplementation((urlOutcome: unknown) => urlOutcome);
    mockMintDocumentShareLink.mockResolvedValue({ ok: true, url: 'https://spaarke.app/doc/abc' });

    displayDialogAsync = jest.fn((_url: string, _options: unknown, callback?: (result: unknown) => void) => {
      callback?.({ status: 'succeeded', value: {} });
    });
    global.Office.context.ui.displayDialogAsync =
      displayDialogAsync as unknown as typeof Office.context.ui.displayDialogAsync;

    showAsTaskpane = jest.fn().mockResolvedValue(undefined);
    (global as unknown as { Office: Record<string, unknown> }).Office.addin = { showAsTaskpane };

    Object.defineProperty(global.navigator, 'clipboard', {
      value: { writeText: jest.fn().mockResolvedValue(undefined) },
      configurable: true,
    });

    // eslint-disable-next-line @typescript-eslint/no-var-requires
    commands = require('../index');
  });

  // -----------------------------------------------------------------------------------------
  // showTaskPane — unchanged behavior, sanity-checked alongside the two rewritten commands.
  // -----------------------------------------------------------------------------------------
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
  describe('quickSave', () => {
    it('bootstraps auth + apiClient + WordAdapter (explicit "word" host), saves an unfiled document, notifies success, and completes', async () => {
      mockApiPost.mockResolvedValue({ jobId: 'job-1' });
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockAuthInitialize).toHaveBeenCalledTimes(1);
      expect(mockApiConfigure).toHaveBeenCalledTimes(1);
      expect(mockRegisterAdapter).toHaveBeenCalledWith('word', expect.anything());
      // Explicit 'word' literal, NOT the no-arg form (see the file's own bootstrap doc comment).
      expect(mockCreateAndInitialize).toHaveBeenCalledWith('word');

      expect(mockAdapter.getSubject).toHaveBeenCalledTimes(1);
      expect(mockAdapter.getDocumentContent).toHaveBeenCalledWith({ format: 'ooxml' });
      expect(mockArrayBufferToBase64).toHaveBeenCalled();
      expect(mockComputeQuickSaveIdempotencyKey).toHaveBeenCalledWith({
        kind: 'document',
        title: 'My Document',
        contentBase64: 'BASE64BYTES',
      });
      expect(mockBuildDocumentSaveRequest).toHaveBeenCalledWith({ title: 'My Document' }, 'BASE64BYTES', 'idem-key-1');
      expect(mockApiPost).toHaveBeenCalledWith('/api/office/save', {
        contentType: 'Document',
        document: { fileName: 'x.docx' },
      });

      expect(displayDialogAsync).toHaveBeenCalledTimes(1);
      expect(lastNotifyStatus(displayDialogAsync)).toBe('info');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/saved/i);

      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when document content extraction fails, without posting, and notifies failure', async () => {
      (mockAdapter.getDocumentContent as jest.Mock).mockRejectedValue(new Error('getFileAsync failed'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when auth bootstrap fails, without posting', async () => {
      mockAuthInitialize.mockRejectedValue(new Error('auth failed'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(mockApiPost).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when the network POST fails', async () => {
      mockApiPost.mockRejectedValue(new Error('network down'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('never opens the task pane on any path (success or failure)', async () => {
      mockApiPost.mockRejectedValue(new Error('network down'));
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(showAsTaskpane).not.toHaveBeenCalled();
    });
  });

  // -----------------------------------------------------------------------------------------
  // shareDocument
  // -----------------------------------------------------------------------------------------
  describe('shareDocument', () => {
    it('mints a share link for a resolved document, copies it to the clipboard, notifies success, and completes without opening the pane', async () => {
      mockResolveDocumentIdentity.mockResolvedValue({
        kind: 'resolved',
        documentId: '11111111-1111-1111-1111-111111111111',
        documentName: 'Contract',
        fileName: 'Contract.docx',
        relatedRecord: null,
      });
      const event = createMockEvent();

      await commands.shareDocument(event);

      expect(mockMintDocumentShareLink).toHaveBeenCalledWith('11111111-1111-1111-1111-111111111111');
      const clipboard = (global.navigator as unknown as { clipboard: { writeText: jest.Mock } }).clipboard;
      expect(clipboard.writeText).toHaveBeenCalledWith('https://spaarke.app/doc/abc');

      expect(lastNotifyStatus(displayDialogAsync)).toBe('info');
      expect(lastNotifyMessage(displayDialogAsync)).toMatch(/copied/i);

      expect(showAsTaskpane).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('DELIBERATE EXCEPTION: opens the pane when the document has no resolved Spaarke identity yet, and does not attempt to mint', async () => {
      mockResolveDocumentIdentity.mockResolvedValue({ kind: 'new', reason: 'not_cloud_document' });
      const event = createMockEvent();

      await commands.shareDocument(event);

      expect(mockMintDocumentShareLink).not.toHaveBeenCalled();
      expect(showAsTaskpane).toHaveBeenCalledTimes(1);
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('a link-mint failure notifies failure, completes, and does not open the pane or touch the clipboard', async () => {
      mockResolveDocumentIdentity.mockResolvedValue({
        kind: 'resolved',
        documentId: '11111111-1111-1111-1111-111111111111',
        documentName: 'Contract',
        fileName: 'Contract.docx',
        relatedRecord: null,
      });
      mockMintDocumentShareLink.mockResolvedValue({ ok: false, message: 'nope' });
      const event = createMockEvent();

      await commands.shareDocument(event);

      const clipboard = (global.navigator as unknown as { clipboard: { writeText: jest.Mock } }).clipboard;
      expect(clipboard.writeText).not.toHaveBeenCalled();
      expect(lastNotifyStatus(displayDialogAsync)).toBe('error');
      expect(lastNotifyMessage(displayDialogAsync)).toBe('nope');
      expect(showAsTaskpane).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when identity resolution throws unexpectedly', async () => {
      mockResolveDocumentIdentity.mockRejectedValue(new Error('boom'));
      const event = createMockEvent();

      await commands.shareDocument(event);

      expect(mockMintDocumentShareLink).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('completes even when auth bootstrap fails', async () => {
      mockAuthInitialize.mockRejectedValue(new Error('auth failed'));
      const event = createMockEvent();

      await commands.shareDocument(event);

      expect(mockResolveDocumentIdentity).not.toHaveBeenCalled();
      expect(event.completed).toHaveBeenCalledTimes(1);
    });

    it('a clipboard write failure never blocks completion (best effort only)', async () => {
      mockResolveDocumentIdentity.mockResolvedValue({
        kind: 'resolved',
        documentId: '11111111-1111-1111-1111-111111111111',
        documentName: 'Contract',
        fileName: 'Contract.docx',
        relatedRecord: null,
      });
      Object.defineProperty(global.navigator, 'clipboard', {
        value: { writeText: jest.fn().mockRejectedValue(new Error('denied')) },
        configurable: true,
      });
      const event = createMockEvent();

      await commands.shareDocument(event);

      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });

  // -----------------------------------------------------------------------------------------
  // Notification helper resilience — a broken notification surface must never block completion.
  // -----------------------------------------------------------------------------------------
  describe('notify() resilience', () => {
    it('quickSave still completes when Office.context.ui is unavailable', async () => {
      // Simulate a host without the Dialog API / Office.context.ui at all. Deletes only
      // `displayDialogAsync` (not the whole shared `Office.context.ui` object that jest.setup.js
      // creates once for the file) so this test cannot corrupt state for tests that run after it.
      const original = global.Office.context.ui.displayDialogAsync;
      // @ts-expect-error — simulating the function being entirely absent on this host.
      delete global.Office.context.ui.displayDialogAsync;
      mockApiPost.mockResolvedValue({ jobId: 'job-1' });
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
      mockApiPost.mockResolvedValue({ jobId: 'job-1' });
      const event = createMockEvent();

      await commands.quickSave(event);

      expect(event.completed).toHaveBeenCalledTimes(1);
    });
  });
});
