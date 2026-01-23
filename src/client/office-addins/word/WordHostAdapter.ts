import type {
  IHostAdapter,
  IHostContext,
  IContentData,
  HostFeature,
} from '@shared/adapters';
import type {
  HostType,
  ItemType,
  BodyContent,
  AttachmentInfo,
  Recipient,
  HostCapabilities,
  InsertLinkResult,
  AttachFileResult,
  GetDocumentContentOptions,
} from '@shared/adapters/types';

/**
 * Word-specific host adapter implementation.
 *
 * Provides access to Word documents for saving to Spaarke DMS.
 * Requires Office.js WordApi requirement set 1.3 or higher.
 */
export class WordHostAdapter implements IHostAdapter {
  private _isInitialized = false;
  private _documentTitle = 'Untitled Document';

  /**
   * Get the Office host type.
   */
  getHostType(): HostType {
    return 'word';
  }

  /**
   * Get the unique identifier of the current document.
   * For Word documents, we use the document URL or a generated identifier.
   */
  async getItemId(): Promise<string> {
    return Word.run(async (context) => {
      const document = context.document;
      const properties = document.properties;
      properties.load(['title']);
      await context.sync();

      // Word doesn't have a unique ID like Outlook's itemId
      // Use title + timestamp as a fallback identifier
      return `word-doc-${properties.title || 'untitled'}-${Date.now()}`;
    });
  }

  /**
   * Get the type of the current item.
   */
  getItemType(): ItemType {
    return 'document';
  }

  /**
   * Get the document title.
   */
  async getSubject(): Promise<string> {
    return Word.run(async (context) => {
      const document = context.document;
      const properties = document.properties;
      properties.load(['title']);
      await context.sync();

      this._documentTitle = properties.title || 'Untitled Document';
      return this._documentTitle;
    });
  }

  /**
   * Get the body content of the Word document.
   */
  async getBody(preferredType: 'html' | 'text' = 'html'): Promise<BodyContent> {
    return Word.run(async (context) => {
      const body = context.document.body;

      if (preferredType === 'html') {
        const html = body.getHtml();
        await context.sync();
        return {
          content: html.value,
          type: 'html',
        };
      } else {
        body.load('text');
        await context.sync();
        return {
          content: body.text,
          type: 'text',
        };
      }
    });
  }

  /**
   * Get attachments - Word documents don't have attachments like emails.
   */
  async getAttachments(): Promise<AttachmentInfo[]> {
    // Word documents don't have attachments in the same sense as emails
    return [];
  }

  /**
   * Get attachment content - not applicable for Word.
   */
  async getAttachmentContent(_attachmentId: string): Promise<AttachmentInfo> {
    throw new Error('Word documents do not support attachments');
  }

  /**
   * Get sender email - not applicable for Word.
   */
  async getSenderEmail(): Promise<string> {
    return '';
  }

  /**
   * Get recipients - not applicable for Word.
   */
  async getRecipients(): Promise<Recipient[]> {
    return [];
  }

  /**
   * Get the document content as an ArrayBuffer.
   */
  async getDocumentContent(options?: GetDocumentContentOptions): Promise<ArrayBuffer> {
    const format = options?.format || 'ooxml';

    return Word.run(async (context) => {
      const body = context.document.body;

      if (format === 'ooxml') {
        // Get as OOXML and convert to ArrayBuffer
        const ooxml = body.getOoxml();
        await context.sync();

        // Convert OOXML string to ArrayBuffer
        const encoder = new TextEncoder();
        return encoder.encode(ooxml.value).buffer as ArrayBuffer;
      } else if (format === 'html') {
        const html = body.getHtml();
        await context.sync();

        const encoder = new TextEncoder();
        return encoder.encode(html.value).buffer as ArrayBuffer;
      } else if (format === 'text') {
        body.load('text');
        await context.sync();

        const encoder = new TextEncoder();
        return encoder.encode(body.text).buffer as ArrayBuffer;
      }

      throw new Error(`Unsupported format: ${format}`);
    });
  }

  /**
   * Get the capabilities of this host adapter.
   */
  getCapabilities(): HostCapabilities {
    return {
      canGetAttachments: false,
      canGetRecipients: false,
      canGetSender: false,
      canGetDocumentContent: true,
      canSaveAsPdf: true,
      canSaveAsEml: false,
      canInsertLink: true,
      canAttachFile: false,
      minApiVersion: '1.3',
      supportedRequirementSet: 'WordApi 1.3',
    };
  }

  /**
   * Initialize the adapter.
   */
  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady((info) => {
        if (info.host === Office.HostType.Word) {
          this._isInitialized = true;
          resolve();
        } else {
          reject(new Error('Not running in Word'));
        }
      });
    });
  }

  /**
   * Check if the adapter has been initialized.
   */
  isInitialized(): boolean {
    return this._isInitialized;
  }

  /**
   * Insert a link at the current cursor position.
   */
  async insertLink(url: string, displayText?: string): Promise<InsertLinkResult> {
    return Word.run(async (context) => {
      const selection = context.document.getSelection();
      const text = displayText || url;

      // Insert text with hyperlink
      const range = selection.insertText(text, Word.InsertLocation.replace);
      range.hyperlink = url;

      await context.sync();

      return {
        success: true,
      };
    }).catch((error) => {
      return {
        success: false,
        errorMessage: error.message,
      };
    });
  }

  /**
   * Attach a file - not applicable for Word documents.
   */
  async attachFile(
    _content: string,
    _fileName: string,
    _contentType: string
  ): Promise<AttachFileResult> {
    return {
      success: false,
      errorMessage: 'Word documents do not support file attachments',
    };
  }

  // Legacy methods for backward compatibility

  async getCurrentContext(): Promise<IHostContext> {
    return Word.run(async (context) => {
      const document = context.document;
      const properties = document.properties;
      properties.load(['title', 'author', 'creationDate', 'lastSaveTime']);

      await context.sync();

      const title = properties.title || 'Untitled Document';

      return {
        itemType: 'document',
        displayName: title,
        metadata: {
          title: properties.title,
          author: properties.author,
          creationDate: properties.creationDate,
          lastSaveTime: properties.lastSaveTime,
          hostType: 'word',
        },
      };
    });
  }

  async getContentToSave(): Promise<IContentData> {
    const context = await this.getCurrentContext();

    // Get document as base64-encoded OOXML
    const content = await this.getDocumentContentAsString('Ooxml');

    return {
      format: 'docx',
      content,
      mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      fileName: `${this.sanitizeFileName(context.displayName)}.docx`,
      metadata: {
        originalName: context.displayName,
        author: (context.metadata.author as string) || undefined,
        createdDate: context.metadata.creationDate as Date | undefined,
        modifiedDate: context.metadata.lastSaveTime as Date | undefined,
        hostMetadata: context.metadata,
      },
    };
  }

  supportsFeature(feature: HostFeature): boolean {
    switch (feature) {
      case 'save-as-pdf':
        return true; // Conversion happens server-side
      case 'save-as-eml':
        return false; // Not applicable for Word
      case 'attachments':
        return false; // Not applicable for Word
      case 'quick-create':
        return true;
      case 'entity-association':
        return true;
      case 'share-links':
        return true;
      default:
        return false;
    }
  }

  // Private helper methods

  private async getDocumentContentAsString(
    format: 'Ooxml' | 'Text' | 'Html'
  ): Promise<string> {
    return Word.run(async (context) => {
      const body = context.document.body;

      switch (format) {
        case 'Ooxml': {
          const ooxml = body.getOoxml();
          await context.sync();
          return ooxml.value;
        }
        case 'Html': {
          const html = body.getHtml();
          await context.sync();
          return html.value;
        }
        case 'Text':
        default:
          body.load('text');
          await context.sync();
          return body.text;
      }
    });
  }

  private sanitizeFileName(name: string): string {
    return name
      .replace(/[<>:"/\\|?*]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .substring(0, 200);
  }
}
