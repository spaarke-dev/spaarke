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
export function createMockBody(
  contentType: 'html' | 'text' = 'html',
  isCompose = false
): MockBody {
  const content = contentType === 'html'
    ? '<p>Test email body content</p>'
    : 'Test email body content';

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
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: existing,
        error: null,
      } as Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>);
    }),
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
        value: { emailAddress: 'testuser@example.com', displayName: 'Test User' },
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
 * Setup mock dialog display.
 */
export function setupMockDialog(
  onDisplayDialog?: (result: { value: MockDialog }) => void
): MockDialog {
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
