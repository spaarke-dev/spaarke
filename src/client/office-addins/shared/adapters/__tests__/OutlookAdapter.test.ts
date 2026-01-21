/**
 * Unit tests for OutlookAdapter
 *
 * Tests the Outlook-specific host adapter implementation.
 */

import { OutlookAdapter } from '../OutlookAdapter';
import type { HostAdapterError, HostCapabilities } from '../types';

// Mock data
const mockReadItem = {
  itemId: 'test-item-123',
  subject: 'Test Email Subject',
  from: { emailAddress: 'sender@example.com', displayName: 'Test Sender' },
  to: [{ emailAddress: 'recipient@example.com', displayName: 'Test Recipient' }],
  cc: [{ emailAddress: 'cc@example.com', displayName: 'CC Recipient' }],
  bcc: [],
  attachments: [
    { id: 'att-1', name: 'document.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
    { id: 'att-2', name: 'image.png', contentType: 'image/png', size: 2048, isInline: true },
  ],
  internetMessageId: '<test-message-id@example.com>',
  conversationId: 'conversation-123',
  importance: Office.MailboxEnums.Importance.Normal,
  dateTimeCreated: new Date('2026-01-15T10:00:00Z'),
  dateTimeModified: new Date('2026-01-15T10:00:00Z'),
  body: {
    getAsync: jest.fn((coercionType: Office.CoercionType, callback: (result: Office.AsyncResult<string>) => void) => {
      const content = coercionType === Office.CoercionType.Html
        ? '<p>Test email body</p>'
        : 'Test email body';
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: content,
        error: null,
      } as Office.AsyncResult<string>);
    }),
  },
  getAttachmentContentAsync: jest.fn(
    (attachmentId: string, callback: (result: Office.AsyncResult<Office.AttachmentContentAsync>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: { content: 'dGVzdCBjb250ZW50', format: Office.MailboxEnums.AttachmentContentFormat.Base64 },
        error: null,
      } as Office.AsyncResult<Office.AttachmentContentAsync>);
    }
  ),
};

const mockComposeItem = {
  itemId: '',
  subject: {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<string>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: 'Draft Subject',
        error: null,
      } as Office.AsyncResult<string>);
    }),
    setAsync: jest.fn(),
  },
  from: {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Office.EmailAddressDetails>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: { emailAddress: 'user@example.com', displayName: 'Current User' },
        error: null,
      } as Office.AsyncResult<Office.EmailAddressDetails>);
    }),
  },
  to: {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: [{ emailAddress: 'to@example.com', displayName: 'To Recipient' }],
        error: null,
      } as Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>);
    }),
    setAsync: jest.fn(),
    addAsync: jest.fn(),
  },
  cc: {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: [],
        error: null,
      } as Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>);
    }),
    setAsync: jest.fn(),
    addAsync: jest.fn(),
  },
  bcc: {
    getAsync: jest.fn((callback: (result: Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: [],
        error: null,
      } as Office.AsyncResult<Array<{ emailAddress: string; displayName: string }>>);
    }),
    setAsync: jest.fn(),
    addAsync: jest.fn(),
  },
  body: {
    getAsync: jest.fn((coercionType: Office.CoercionType, callback: (result: Office.AsyncResult<string>) => void) => {
      callback({
        status: Office.AsyncResultStatus.Succeeded,
        value: '<p>Draft body</p>',
        error: null,
      } as Office.AsyncResult<string>);
    }),
    setSelectedDataAsync: jest.fn(
      (data: string, options: unknown, callback: (result: Office.AsyncResult<void>) => void) => {
        callback({
          status: Office.AsyncResultStatus.Succeeded,
          value: undefined,
          error: null,
        } as Office.AsyncResult<void>);
      }
    ),
  },
  attachments: [],
  getItemIdAsync: jest.fn((callback: (result: Office.AsyncResult<string>) => void) => {
    callback({
      status: Office.AsyncResultStatus.Succeeded,
      value: 'draft-item-id',
      error: null,
    } as Office.AsyncResult<string>);
  }),
  addFileAttachmentFromBase64Async: jest.fn(
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
  ),
};

describe('OutlookAdapter', () => {
  let adapter: OutlookAdapter;

  beforeEach(() => {
    adapter = new OutlookAdapter();

    // Reset Office context mock
    (global.Office as unknown as Record<string, unknown>).context = {
      ...global.Office.context,
      mailbox: {
        item: null,
        userProfile: {
          displayName: 'Test User',
          emailAddress: 'testuser@example.com',
          timeZone: 'UTC',
        },
      },
      requirements: {
        isSetSupported: jest.fn().mockReturnValue(true),
      },
    };

    // Mock Office.onReady for Outlook
    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Outlook, platform: Office.PlatformType.OfficeOnline });
      return Promise.resolve({ host: Office.HostType.Outlook, platform: Office.PlatformType.OfficeOnline });
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('getHostType', () => {
    it('should return "outlook"', () => {
      expect(adapter.getHostType()).toBe('outlook');
    });
  });

  describe('getItemType', () => {
    it('should return "email"', () => {
      expect(adapter.getItemType()).toBe('email');
    });
  });

  describe('initialize', () => {
    it('should initialize successfully in Outlook', async () => {
      await adapter.initialize();

      expect(adapter.isInitialized()).toBe(true);
    });

    it('should reject if not running in Outlook', async () => {
      global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
        callback({ host: Office.HostType.Word, platform: Office.PlatformType.OfficeOnline });
      });

      await expect(adapter.initialize()).rejects.toMatchObject({
        code: 'INVALID_HOST',
      });
    });
  });

  describe('read mode operations', () => {
    beforeEach(async () => {
      // Set up read mode context
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();
    });

    describe('getItemId', () => {
      it('should return the item ID', async () => {
        const itemId = await adapter.getItemId();

        expect(itemId).toBe('test-item-123');
      });
    });

    describe('getSubject', () => {
      it('should return the subject', async () => {
        const subject = await adapter.getSubject();

        expect(subject).toBe('Test Email Subject');
      });
    });

    describe('getBody', () => {
      it('should return HTML body by default', async () => {
        const body = await adapter.getBody();

        expect(body.content).toBe('<p>Test email body</p>');
        expect(body.type).toBe('html');
      });

      it('should return text body when requested', async () => {
        const body = await adapter.getBody('text');

        expect(body.content).toBe('Test email body');
        expect(body.type).toBe('text');
      });
    });

    describe('getAttachments', () => {
      it('should return attachment list', async () => {
        const attachments = await adapter.getAttachments();

        expect(attachments).toHaveLength(2);
        expect(attachments[0].name).toBe('document.pdf');
        expect(attachments[1].isInline).toBe(true);
      });

      it('should return empty array when no attachments', async () => {
        (global.Office.context.mailbox as unknown as { item: { attachments: never[] } }).item = {
          ...mockReadItem,
          attachments: [],
        };

        const attachments = await adapter.getAttachments();

        expect(attachments).toHaveLength(0);
      });
    });

    describe('getAttachmentContent', () => {
      it('should return attachment with content', async () => {
        const attachment = await adapter.getAttachmentContent('att-1');

        expect(attachment.id).toBe('att-1');
        expect(attachment.content).toBe('dGVzdCBjb250ZW50');
      });

      it('should throw when attachment not found', async () => {
        await expect(adapter.getAttachmentContent('non-existent')).rejects.toMatchObject({
          code: 'ATTACHMENT_NOT_FOUND',
        });
      });
    });

    describe('getSenderEmail', () => {
      it('should return sender email', async () => {
        const email = await adapter.getSenderEmail();

        expect(email).toBe('sender@example.com');
      });
    });

    describe('getRecipients', () => {
      it('should return all recipients', async () => {
        const recipients = await adapter.getRecipients();

        expect(recipients).toHaveLength(2);
        expect(recipients[0].type).toBe('to');
        expect(recipients[0].email).toBe('recipient@example.com');
        expect(recipients[1].type).toBe('cc');
      });
    });

    describe('getCapabilities', () => {
      it('should return correct capabilities for read mode with Mailbox 1.8', () => {
        const capabilities = adapter.getCapabilities();

        expect(capabilities.canGetAttachments).toBe(true);
        expect(capabilities.canGetRecipients).toBe(true);
        expect(capabilities.canGetSender).toBe(true);
        expect(capabilities.canGetDocumentContent).toBe(false);
        expect(capabilities.canSaveAsPdf).toBe(true);
        expect(capabilities.canInsertLink).toBe(false); // Read mode
        expect(capabilities.canAttachFile).toBe(false); // Read mode
      });
    });

    describe('getInternetMessageId', () => {
      it('should return the internet message ID', () => {
        const messageId = adapter.getInternetMessageId();

        expect(messageId).toBe('<test-message-id@example.com>');
      });
    });

    describe('getConversationId', () => {
      it('should return the conversation ID', () => {
        const conversationId = adapter.getConversationId();

        expect(conversationId).toBe('conversation-123');
      });
    });

    describe('getImportance', () => {
      it('should return normal importance', () => {
        const importance = adapter.getImportance();

        expect(importance).toBe('normal');
      });
    });
  });

  describe('compose mode operations', () => {
    beforeEach(async () => {
      // Set up compose mode context
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockComposeItem;
      await adapter.initialize();
    });

    describe('getItemId', () => {
      it('should return item ID asynchronously', async () => {
        const itemId = await adapter.getItemId();

        expect(itemId).toBe('draft-item-id');
      });
    });

    describe('getSubject', () => {
      it('should return subject asynchronously', async () => {
        const subject = await adapter.getSubject();

        expect(subject).toBe('Draft Subject');
      });
    });

    describe('getRecipients', () => {
      it('should return recipients asynchronously', async () => {
        const recipients = await adapter.getRecipients();

        expect(recipients).toHaveLength(1);
        expect(recipients[0].type).toBe('to');
        expect(recipients[0].email).toBe('to@example.com');
      });
    });

    describe('insertLink', () => {
      it('should insert link successfully', async () => {
        const result = await adapter.insertLink('https://example.com', 'Example Link');

        expect(result.success).toBe(true);
        expect(mockComposeItem.body.setSelectedDataAsync).toHaveBeenCalled();
      });

      it('should escape HTML in URL and display text', async () => {
        await adapter.insertLink('https://example.com?a=1&b=2', 'Test <script>');

        const callArgs = mockComposeItem.body.setSelectedDataAsync.mock.calls[0];
        const htmlContent = callArgs[0];

        expect(htmlContent).toContain('&amp;');
        expect(htmlContent).toContain('&lt;script&gt;');
      });
    });

    describe('attachFile', () => {
      it('should attach file successfully', async () => {
        const result = await adapter.attachFile('dGVzdA==', 'test.txt', 'text/plain');

        expect(result.success).toBe(true);
        expect(result.attachmentId).toBeDefined();
        expect(mockComposeItem.addFileAttachmentFromBase64Async).toHaveBeenCalled();
      });

      it('should fail when Mailbox 1.8 not supported', async () => {
        global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(false);

        // Reinitialize to pick up the new mock
        adapter = new OutlookAdapter();
        (global.Office.context.mailbox as unknown as { item: unknown }).item = mockComposeItem;
        await adapter.initialize();

        const result = await adapter.attachFile('dGVzdA==', 'test.txt', 'text/plain');

        expect(result.success).toBe(false);
        expect(result.errorMessage).toContain('Mailbox API 1.8');
      });
    });

    describe('getCapabilities', () => {
      it('should return correct capabilities for compose mode', () => {
        const capabilities = adapter.getCapabilities();

        expect(capabilities.canInsertLink).toBe(true);
        expect(capabilities.canAttachFile).toBe(true);
        expect(capabilities.canGetAttachments).toBe(false); // Compose mode
      });
    });
  });

  describe('error handling', () => {
    it('should throw when not initialized', async () => {
      const uninitializedAdapter = new OutlookAdapter();

      await expect(uninitializedAdapter.getItemId()).rejects.toMatchObject({
        code: 'NOT_INITIALIZED',
      });
    });

    it('should throw when no item is selected', async () => {
      // Initialize with no item
      (global.Office.context.mailbox as unknown as { item: null }).item = null;
      await adapter.initialize();

      await expect(adapter.getItemId()).rejects.toMatchObject({
        code: 'NO_ITEM_SELECTED',
      });
    });
  });

  describe('getDocumentContent', () => {
    it('should return empty ArrayBuffer for emails', async () => {
      (global.Office.context.mailbox as unknown as { item: unknown }).item = mockReadItem;
      await adapter.initialize();

      const content = await adapter.getDocumentContent();

      expect(content.byteLength).toBe(0);
    });
  });
});
