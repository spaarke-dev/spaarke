/**
 * Unit tests for WordAdapter
 *
 * Tests the Word-specific host adapter implementation.
 */

import { WordAdapter } from '../WordAdapter';

// Mock Word.run
const mockWordBody = {
  text: 'Test document content',
  getHtml: jest.fn().mockReturnValue({ value: '<p>Test document content</p>' }),
  getOoxml: jest.fn().mockReturnValue({ value: '<?xml version="1.0"?><w:document></w:document>' }),
  load: jest.fn(),
};

const mockWordProperties = {
  title: 'Test Document',
  author: 'Test Author',
  creationDate: new Date('2026-01-15T10:00:00Z'),
  load: jest.fn(),
};

const mockWordSelection = {
  insertHtml: jest.fn(),
  insertText: jest.fn(),
};

const mockWordContext = {
  document: {
    body: mockWordBody,
    properties: mockWordProperties,
    getSelection: jest.fn().mockReturnValue(mockWordSelection),
  },
  sync: jest.fn().mockResolvedValue(undefined),
};

// Set up global Word mock
(global as unknown as Record<string, unknown>).Word = {
  run: jest.fn(async (callback: (context: typeof mockWordContext) => Promise<void>) => {
    await callback(mockWordContext);
  }),
  InsertLocation: {
    replace: 'Replace',
    start: 'Start',
    end: 'End',
    before: 'Before',
    after: 'After',
  },
};

describe('WordAdapter', () => {
  let adapter: WordAdapter;

  beforeEach(() => {
    adapter = new WordAdapter();

    // Reset mocks
    jest.clearAllMocks();

    // Reset Office context for Word
    (global.Office as unknown as Record<string, unknown>).context = {
      ...global.Office.context,
      document: {},
      requirements: {
        isSetSupported: jest.fn().mockReturnValue(true),
      },
    };

    // Mock Office.onReady for Word
    global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
      callback({ host: Office.HostType.Word, platform: Office.PlatformType.OfficeOnline });
      return Promise.resolve({ host: Office.HostType.Word, platform: Office.PlatformType.OfficeOnline });
    });
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  describe('getHostType', () => {
    it('should return "word"', () => {
      expect(adapter.getHostType()).toBe('word');
    });
  });

  describe('getItemType', () => {
    it('should return "document"', () => {
      expect(adapter.getItemType()).toBe('document');
    });
  });

  describe('initialize', () => {
    it('should initialize successfully in Word', async () => {
      await adapter.initialize();

      expect(adapter.isInitialized()).toBe(true);
    });

    it('should reject if not running in Word', async () => {
      global.Office.onReady = jest.fn().mockImplementation((callback: (info: { host: Office.HostType }) => void) => {
        callback({ host: Office.HostType.Outlook, platform: Office.PlatformType.OfficeOnline });
      });

      await expect(adapter.initialize()).rejects.toMatchObject({
        code: 'INVALID_HOST',
      });
    });

    it('should reject if WordApi 1.3 is not supported', async () => {
      global.Office.context.requirements.isSetSupported = jest.fn().mockReturnValue(false);

      await expect(adapter.initialize()).rejects.toMatchObject({
        code: 'API_NOT_AVAILABLE',
      });
    });
  });

  describe('initialized operations', () => {
    beforeEach(async () => {
      await adapter.initialize();
    });

    describe('getItemId', () => {
      it('should return a document identifier', async () => {
        const itemId = await adapter.getItemId();

        expect(itemId).toContain('word-doc-');
        expect(typeof itemId).toBe('string');
      });

      it('should return cached ID on subsequent calls', async () => {
        const id1 = await adapter.getItemId();
        const id2 = await adapter.getItemId();

        expect(id1).toBe(id2);
      });
    });

    describe('getSubject', () => {
      it('should return document title', async () => {
        const subject = await adapter.getSubject();

        expect(subject).toBe('Test Document');
      });

      it('should return "Untitled Document" when no title is set', async () => {
        mockWordProperties.title = '';

        const subject = await adapter.getSubject();

        expect(subject).toBe('Untitled Document');

        // Reset
        mockWordProperties.title = 'Test Document';
      });
    });

    describe('getBody', () => {
      it('should return HTML body by default', async () => {
        const body = await adapter.getBody();

        expect(body.content).toBe('<p>Test document content</p>');
        expect(body.type).toBe('html');
      });

      it('should return text body when requested', async () => {
        const body = await adapter.getBody('text');

        expect(body.content).toBe('Test document content');
        expect(body.type).toBe('text');
      });
    });

    describe('getDocumentContent', () => {
      it('should return OOXML by default', async () => {
        const content = await adapter.getDocumentContent();

        expect(content.byteLength).toBeGreaterThan(0);
        expect(mockWordBody.getOoxml).toHaveBeenCalled();
      });

      it('should return HTML when requested', async () => {
        const content = await adapter.getDocumentContent({ format: 'html' });

        expect(content.byteLength).toBeGreaterThan(0);
        expect(mockWordBody.getHtml).toHaveBeenCalled();
      });

      it('should return text when requested', async () => {
        const content = await adapter.getDocumentContent({ format: 'text' });

        expect(content.byteLength).toBeGreaterThan(0);
        expect(mockWordBody.load).toHaveBeenCalledWith('text');
      });

      it('should throw for unsupported PDF format', async () => {
        await expect(adapter.getDocumentContent({ format: 'pdf' })).rejects.toMatchObject({
          code: 'CAPABILITY_NOT_SUPPORTED',
        });
      });
    });

    describe('insertLink', () => {
      it('should insert link at cursor position', async () => {
        const result = await adapter.insertLink('https://example.com', 'Example');

        expect(result.success).toBe(true);
        expect(mockWordContext.document.getSelection).toHaveBeenCalled();
        expect(mockWordSelection.insertHtml).toHaveBeenCalledWith(
          expect.stringContaining('href="https://example.com"'),
          'Replace'
        );
      });

      it('should escape HTML in URL and display text', async () => {
        await adapter.insertLink('https://example.com?a=1&b=2', 'Test <script>');

        const callArgs = mockWordSelection.insertHtml.mock.calls[0];
        const htmlContent = callArgs[0];

        expect(htmlContent).toContain('&amp;');
        expect(htmlContent).toContain('&#39;');
      });

      it('should use URL as display text when not provided', async () => {
        await adapter.insertLink('https://example.com');

        const callArgs = mockWordSelection.insertHtml.mock.calls[0];
        const htmlContent = callArgs[0];

        expect(htmlContent).toContain('>https://example.com</a>');
      });

      it('should return error on failure', async () => {
        mockWordSelection.insertHtml.mockImplementationOnce(() => {
          throw new Error('Insert failed');
        });

        const result = await adapter.insertLink('https://example.com');

        expect(result.success).toBe(false);
        expect(result.errorMessage).toContain('Insert failed');
      });
    });

    describe('getAttachments', () => {
      it('should return empty array (not applicable for Word)', async () => {
        const attachments = await adapter.getAttachments();

        expect(attachments).toEqual([]);
      });
    });

    describe('getAttachmentContent', () => {
      it('should throw CAPABILITY_NOT_SUPPORTED error', async () => {
        await expect(adapter.getAttachmentContent('any-id')).rejects.toMatchObject({
          code: 'CAPABILITY_NOT_SUPPORTED',
        });
      });
    });

    describe('getSenderEmail', () => {
      it('should return empty string (not applicable for Word)', async () => {
        const email = await adapter.getSenderEmail();

        expect(email).toBe('');
      });
    });

    describe('getRecipients', () => {
      it('should return empty array (not applicable for Word)', async () => {
        const recipients = await adapter.getRecipients();

        expect(recipients).toEqual([]);
      });
    });

    describe('attachFile', () => {
      it('should return error (not supported in Word)', async () => {
        const result = await adapter.attachFile('content', 'file.txt', 'text/plain');

        expect(result.success).toBe(false);
        expect(result.errorMessage).toContain('not supported in Word');
      });
    });

    describe('getCapabilities', () => {
      it('should return correct capabilities for Word', () => {
        const capabilities = adapter.getCapabilities();

        expect(capabilities.canGetAttachments).toBe(false);
        expect(capabilities.canGetRecipients).toBe(false);
        expect(capabilities.canGetSender).toBe(false);
        expect(capabilities.canGetDocumentContent).toBe(true);
        expect(capabilities.canSaveAsPdf).toBe(true);
        expect(capabilities.canSaveAsEml).toBe(false);
        expect(capabilities.canInsertLink).toBe(true);
        expect(capabilities.canAttachFile).toBe(false);
        expect(capabilities.minApiVersion).toBe('1.3');
        expect(capabilities.supportedRequirementSet).toBe('WordApi 1.3');
      });
    });
  });

  describe('error handling', () => {
    it('should throw when not initialized', async () => {
      const uninitializedAdapter = new WordAdapter();

      await expect(uninitializedAdapter.getItemId()).rejects.toMatchObject({
        code: 'NOT_INITIALIZED',
      });
    });

    it('should throw for ensureInitialized when not initialized', () => {
      const uninitializedAdapter = new WordAdapter();

      expect(() => uninitializedAdapter.getCapabilities()).not.toThrow();
      // getCapabilities doesn't require initialization
    });
  });

  describe('isInitialized', () => {
    it('should return false before initialization', () => {
      expect(adapter.isInitialized()).toBe(false);
    });

    it('should return true after initialization', async () => {
      await adapter.initialize();

      expect(adapter.isInitialized()).toBe(true);
    });
  });
});
