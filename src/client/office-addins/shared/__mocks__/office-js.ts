/**
 * Office.js Mock Utilities for Unit Testing
 *
 * Provides comprehensive mocks for Office.js APIs used in the add-in.
 * These mocks support both Outlook and Word host scenarios.
 *
 * @see https://learn.microsoft.com/en-us/office/dev/add-ins/testing/testing-office-add-ins
 */

import type { AccountInfo, AuthenticationResult } from '@azure/msal-browser';

// ============================================
// Mock Types
// ============================================

export interface MockOfficeItem {
  itemId: string;
  subject: string;
  body: MockBody;
  from?: { emailAddress: string; displayName: string };
  to?: Array<{ emailAddress: string; displayName: string }>;
  cc?: Array<{ emailAddress: string; displayName: string }>;
  bcc?: Array<{ emailAddress: string; displayName: string }>;
  attachments?: MockAttachment[];
  internetMessageId?: string;
  conversationId?: string;
  importance?: Office.MailboxEnums.Importance;
  dateTimeCreated?: Date;
  dateTimeModified?: Date;
}

export interface MockAttachment {
  id: string;
  name: string;
  contentType: string;
  size: number;
  isInline: boolean;
  content?: string;
}

export interface MockBody {
  getAsync: jest.Mock;
  setSelectedDataAsync?: jest.Mock;
}

export interface MockDialog {
  close: jest.Mock;
  addEventHandler: jest.Mock;
  messageChild: jest.Mock;
}

// ============================================
// Mock Builders
// ============================================

/**
 * Create a mock Outlook read item (email).
 */
export function createMockReadItem(overrides: Partial<MockOfficeItem> = {}): MockOfficeItem {
  return {
    itemId: 'test-item-id-123',
    subject: 'Test Email Subject',
    body: createMockBody('html'),
    from: { emailAddress: 'sender@example.com', displayName: 'Test Sender' },
    to: [{ emailAddress: 'recipient@example.com', displayName: 'Test Recipient' }],
    cc: [],
    bcc: [],
    attachments: [],
    internetMessageId: '<test-message-id@example.com>',
    conversationId: 'conversation-123',
    importance: Office.MailboxEnums.Importance.Normal,
    dateTimeCreated: new Date('2026-01-15T10:00:00Z'),
    dateTimeModified: new Date('2026-01-15T10:00:00Z'),
    ...overrides,
  };
}

/**
 * Create a mock Outlook compose item.
 */
export function createMockComposeItem(overrides: Partial<MockOfficeItem> = {}): MockOfficeItem {
  const subject = {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<string>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: overrides.subject ?? 'Draft Subject',
        error: null,
      } as Office.AsyncResult<string>);
    }),
    setAsync: jest.fn(),
  };

  const composeItem: MockOfficeItem = {
    itemId: '',
    subject: subject as unknown as string,
    body: createMockBody('html', true),
    to: [],
    cc: [],
    bcc: [],
    attachments: [],
    ...overrides,
  };

  // Add getItemIdAsync for compose mode
  (composeItem as unknown as Record<string, unknown>).getItemIdAsync = jest.fn(
    (callback: (result: Office.AsyncResult<string>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: 'draft-item-id',
        error: null,
      } as Office.AsyncResult<string>);
    }
  );

  return composeItem;
}

/**
 * Create a mock body object.
 */
export function createMockBody(contentType: 'html' | 'text' = 'html', isCompose = false): MockBody {
  const content = contentType === 'html' ? '<p>Test email body content</p>' : 'Test email body content';

  const body: MockBody = {
    getAsync: jest.fn((coercionType: Office.CoercionType, callback: (result: Office.AsyncResult<string>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: content,
        error: null,
      } as Office.AsyncResult<string>);
    }),
  };

  if (isCompose) {
    body.setSelectedDataAsync = jest.fn(
      (data: string, options: unknown, callback: (result: Office.AsyncResult<void>) => void) => {
        callback({
          status: Office.AsyncResultStatus.Succeeded,
          value: undefined,
          error: null,
        } as Office.AsyncResult<void>);
      }
    );
  }

  return body;
}

/**
 * Create a mock attachment.
 */
export function createMockAttachment(overrides: Partial<MockAttachment> = {}): MockAttachment {
  return {
    id: `attachment-${Date.now()}`,
    name: 'test-document.pdf',
    contentType: 'application/pdf',
    size: 1024 * 100, // 100KB
    isInline: false,
    ...overrides,
  };
}

/**
 * Create mock attachment content result.
 */
export function createMockAttachmentContent(base64Content = 'dGVzdCBjb250ZW50'): Office.AttachmentContentAsync {
  return {
    content: base64Content,
    format: Office.MailboxEnums.AttachmentContentFormat.Base64,
  } as Office.AttachmentContentAsync;
}

/**
 * Create a mock dialog.
 */
export function createMockDialog(): MockDialog {
  return {
    close: jest.fn(),
    addEventHandler: jest.fn(),
    messageChild: jest.fn(),
  };
}

// ============================================
// Mock Office Context Setup
// ============================================

/**
 * Setup mock Outlook mailbox context.
 */
export function setupOutlookReadContext(item?: MockOfficeItem): void {
  const mockItem = item ?? createMockReadItem();

  // Add getAttachmentContentAsync method
  (mockItem as unknown as Record<string, unknown>).getAttachmentContentAsync = jest.fn(
    (attachmentId: string, callback: (result: Office.AsyncResult<Office.AttachmentContentAsync>) => void) => {
      const attachment = mockItem.attachments?.find(a => a.id === attachmentId);
      if (attachment?.content) {
        callback({
          status: Office.AsyncResultStatus.Succeeded,
          value: createMockAttachmentContent(attachment.content),
          error: null,
        } as Office.AsyncResult<Office.AttachmentContentAsync>);
      } else {
        callback({
          status: Office.AsyncResultStatus.Succeeded,
          value: createMockAttachmentContent(),
          error: null,
        } as Office.AsyncResult<Office.AttachmentContentAsync>);
      }
    }
  );

  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    mailbox: {
      item: mockItem,
      userProfile: {
        displayName: 'Test User',
        emailAddress: 'testuser@example.com',
        timeZone: 'UTC',
      },
    },
  };
}

/**
 * Setup mock Outlook compose context.
 */
export function setupOutlookComposeContext(item?: MockOfficeItem): void {
  const mockItem = item ?? createMockComposeItem();

  // Add Recipients objects with getAsync/setAsync methods
  const createRecipients = (existing: Array<{ emailAddress: string; displayName: string }> = []) => ({
    getAsync: jest.fn(
      (callback: (result: Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>) => void) => {
        callback({
          status: Office.AsyncResultStatus.Succeeded,
          value: existing,
          error: null,
        } as Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>);
      }
    ),
    setAsync: jest.fn(),
    addAsync: jest.fn(),
  });

  (mockItem as unknown as Record<string, unknown>).to = createRecipients(mockItem.to);
  (mockItem as unknown as Record<string, unknown>).cc = createRecipients(mockItem.cc);
  (mockItem as unknown as Record<string, unknown>).bcc = createRecipients(mockItem.bcc);

  // Add from as Office.From object
  (mockItem as unknown as Record<string, unknown>).from = {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Office.EmailAddressDetails>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: {
          emailAddress: 'testuser@example.com',
          displayName: 'Test User',
        },
        error: null,
      } as Office.AsyncResult<Office.EmailAddressDetails>);
    }),
  };

  // Add addFileAttachmentFromBase64Async for compose mode
  (mockItem as unknown as Record<string, unknown>).addFileAttachmentFromBase64Async = jest.fn(
    (
      base64Content: string,
      fileName: string,
      options: { isInline: boolean; contentType: string },
      callback: (result: Office.AsyncResult<string>) => void
    ) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: `attachment-${Date.now()}`,
        error: null,
      } as Office.AsyncResult<string>);
    }
  );

  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    mailbox: {
      item: mockItem,
      userProfile: {
        displayName: 'Test User',
        emailAddress: 'testuser@example.com',
        timeZone: 'UTC',
      },
    },
  };
}

/**
 * Setup mock Word context.
 */
export function setupWordContext(): void {
  // Mock Word.run for Word API calls
  (global as unknown as Record<string, unknown>).Word = {
    run: jest.fn(async (callback: (context: MockWordContext) => Promise<void>) => {
      const mockContext = createMockWordContext();
      await callback(mockContext);
    }),
    InsertLocation: {
      replace: 'Replace',
      start: 'Start',
      end: 'End',
      before: 'Before',
      after: 'After',
    },
  };

  // Update Office.context for Word
  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    document: {},
  };
}

interface MockWordContext {
  document: {
    body: MockWordBody;
    properties: MockWordProperties;
    getSelection: jest.Mock;
  };
  sync: jest.Mock;
}

interface MockWordBody {
  text: string;
  getHtml: jest.Mock;
  getOoxml: jest.Mock;
  load: jest.Mock;
}

interface MockWordProperties {
  title: string;
  author: string;
  creationDate: Date;
  load: jest.Mock;
}

/**
 * Create a mock Word context.
 */
export function createMockWordContext(): MockWordContext {
  const mockBody: MockWordBody = {
    text: 'Test document content',
    getHtml: jest.fn().mockReturnValue({ value: '<p>Test document content</p>' }),
    getOoxml: jest.fn().mockReturnValue({ value: '<w:document>...</w:document>' }),
    load: jest.fn(),
  };

  const mockProperties: MockWordProperties = {
    title: 'Test Document',
    author: 'Test Author',
    creationDate: new Date('2026-01-15T10:00:00Z'),
    load: jest.fn(),
  };

  const mockSelection = {
    insertHtml: jest.fn(),
    insertText: jest.fn(),
  };

  return {
    document: {
      body: mockBody,
      properties: mockProperties,
      getSelection: jest.fn().mockReturnValue(mockSelection),
    },
    sync: jest.fn().mockResolvedValue(undefined),
  };
}

/**
 * Observations recorded by {@link setupWordCompressedFile} so a test can assert HOW the .docx was read,
 * not just what came back.
 */
export interface MockWordFileHandle {
  /** Slice indexes in the order `getSliceAsync` was actually called. */
  sliceOrder: number[];
  /** How many times `closeAsync` ran. Must be exactly 1 on both success and failure. */
  closeCount: number;
  /** The `(fileType, options)` pairs `getFileAsync` was invoked with. */
  getFileAsyncCalls: Array<{ fileType: unknown; options: unknown }>;
  /** Slice count the mocked handle advertises. */
  sliceCount: number;
}

/**
 * Mock `Office.context.document.getFileAsync(Office.FileType.Compressed, ...)` over a byte payload,
 * chunked exactly the way Office chunks a real file.
 *
 * Task 010 / FR-04: the Word save path extracts the real .docx binary through this Common API rather
 * than `body.getOoxml()` (which returned flat OOXML XML and broke SPE preview + AI extraction — UAT
 * 2026-09-03). Tests of that path need the slice protocol, not a `Word.run` mock.
 *
 * @param bytes - The payload the mocked document should yield.
 * @param options.sliceSize - Bytes per slice. Defaults to 65536, matching the adapter's request.
 * @param options.failSliceIndex - When set, that slice read reports `Failed` instead of succeeding.
 * @param options.failGetFile - When true, `getFileAsync` itself reports `Failed`.
 * @param options.dataAsNumberArray - Serve each slice as a plain `number[]` instead of a
 *   `Uint8Array`. Some Office hosts do exactly this, which is why the adapter carries a
 *   `data instanceof Uint8Array ? data : Uint8Array.from(data)` coercion. Without this option that
 *   branch is never exercised. (code-review S-1, 2026-09-09.)
 * @returns A handle recording slice order, close count, and the getFileAsync arguments.
 *
 * NOTE: this REPLACES `global.Office.context` wholesale and does not restore it. jest's
 * `clearMocks`/`restoreMocks` do not undo a replaced global object, so a suite that calls this should
 * snapshot and restore `Office.context` in `afterEach` (see `HostAdapterFactory.test.ts`) or call it in
 * every test that depends on the context. (adr-check W-7, 2026-09-09.)
 */
export function setupWordCompressedFile(
  bytes: Uint8Array,
  options: {
    sliceSize?: number;
    failSliceIndex?: number;
    failGetFile?: boolean;
    dataAsNumberArray?: boolean;
  } = {}
): MockWordFileHandle {
  const sliceSize = options.sliceSize ?? 65536;
  const sliceCount = Math.max(1, Math.ceil(bytes.length / sliceSize));

  const handle: MockWordFileHandle = {
    sliceOrder: [],
    closeCount: 0,
    getFileAsyncCalls: [],
    sliceCount,
  };

  const file = {
    size: bytes.length,
    sliceCount,
    getSliceAsync: (index: number, callback: (result: unknown) => void) => {
      handle.sliceOrder.push(index);
      if (options.failSliceIndex === index) {
        callback({
          status: Office.AsyncResultStatus.Failed,
          error: { message: `Mocked failure reading slice ${index}.` },
          value: undefined,
        });
        return;
      }
      const start = index * sliceSize;
      const data = bytes.slice(start, Math.min(start + sliceSize, bytes.length));
      const payload = options.dataAsNumberArray ? Array.from(data) : data;
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: { index, size: data.length, data: payload },
        error: null,
      });
    },
    closeAsync: (callback: () => void) => {
      handle.closeCount += 1;
      callback();
    },
  };

  (global.Office as unknown as Record<string, unknown>).context = {
    ...global.Office.context,
    requirements: { isSetSupported: jest.fn().mockReturnValue(true) },
    document: {
      getFileAsync: (fileType: unknown, opts: unknown, callback: (result: unknown) => void) => {
        handle.getFileAsyncCalls.push({ fileType, options: opts });
        if (options.failGetFile) {
          callback({
            status: Office.AsyncResultStatus.Failed,
            error: { message: 'Mocked getFileAsync failure.' },
            value: undefined,
          });
          return;
        }
        callback({ status: Office.AsyncResultStatus.Succeeded, value: file, error: null });
      },
    },
  };

  return handle;
}

/**
 * Build a deterministic payload that begins with the PK local-file-header signature
 * (0x50 0x4B 0x03 0x04) — the shape a real .docx has on the wire. Content past the signature is a
 * fixed LCG sequence so every byte position is distinguishable and an out-of-order assembly cannot
 * accidentally compare equal.
 */
export function createMockDocxBytes(length: number): Uint8Array {
  const out = new Uint8Array(length);
  out[0] = 0x50;
  out[1] = 0x4b;
  out[2] = 0x03;
  out[3] = 0x04;
  let x = 0x12345678;
  for (let i = 4; i < length; i++) {
    x = (x * 1103515245 + 12345) & 0x7fffffff;
    out[i] = (x >>> 16) & 0xff;
  }
  return out;
}

/**
 * Setup mock dialog display.
 */
export function setupMockDialog(onDisplayDialog?: (result: { value: MockDialog }) => void): MockDialog {
  const mockDialog = createMockDialog();

  global.Office.context.ui.displayDialogAsync = jest.fn(
    (url: string, options: unknown, callback: (result: Office.AsyncResult<Office.Dialog>) => void) => {
      const result = {
        status: Office.AsyncResultStatus.Succeeded,
        value: mockDialog as unknown as Office.Dialog,
        error: null,
      } as Office.AsyncResult<Office.Dialog>;

      callback(result);
      onDisplayDialog?.({ value: mockDialog });
    }
  );

  return mockDialog;
}

// ============================================
// MSAL Mock Helpers
// ============================================

/**
 * Create a mock MSAL account.
 */
export function createMockAccount(overrides: Partial<AccountInfo> = {}): AccountInfo {
  return {
    homeAccountId: 'test-home-id',
    environment: 'login.microsoftonline.com',
    tenantId: 'test-tenant-id',
    username: 'test@example.com',
    localAccountId: 'test-local-id',
    name: 'Test User',
    ...overrides,
  };
}

/**
 * Create a mock authentication result.
 */
export function createMockAuthResult(overrides: Partial<AuthenticationResult> = {}): AuthenticationResult {
  const account = createMockAccount();
  return {
    accessToken: 'mock-access-token-12345',
    account,
    expiresOn: new Date(Date.now() + 3600 * 1000), // 1 hour from now
    scopes: ['api://test-api-id/user_impersonation'],
    idToken: 'mock-id-token',
    idTokenClaims: {},
    tenantId: account.tenantId,
    uniqueId: 'test-unique-id',
    authority: 'https://login.microsoftonline.com/test-tenant-id',
    tokenType: 'Bearer',
    correlationId: 'test-correlation-id',
    fromCache: false,
    ...overrides,
  };
}

// ============================================
// Cleanup Utilities
// ============================================

/**
 * Reset Office context to defaults.
 */
export function resetOfficeContext(): void {
  (global.Office as unknown as Record<string, unknown>).context = {
    diagnostics: {
      platform: 'OfficeOnline',
      version: '16.0.0.0',
      host: 'Outlook',
    },
    requirements: {
      isSetSupported: jest.fn().mockReturnValue(true),
    },
    ui: {
      displayDialogAsync: jest.fn(),
      messageParent: jest.fn(),
    },
    mailbox: {
      item: null,
    },
    document: null,
  };
}

/**
 * Reset all mocks.
 */
export function resetAllMocks(): void {
  resetOfficeContext();
  jest.clearAllMocks();
}

// Export Office enums for convenience
export const OfficeEnums = {
  AsyncResultStatus: {
    Succeeded: 'succeeded' as const,
    Failed: 'failed' as const,
  },
  CoercionType: {
    Html: 'html' as const,
    Text: 'text' as const,
    Ooxml: 'ooxml' as const,
  },
  MailboxEnums: {
    Importance: {
      Low: 'Low' as const,
      Normal: 'Normal' as const,
      High: 'High' as const,
    },
    AttachmentContentFormat: {
      Base64: 'base64' as const,
      Url: 'url' as const,
    },
  },
};
