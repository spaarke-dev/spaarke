import type {
  IHostAdapter,
  IHostContext,
  IContentData,
  HostFeature,
} from '@shared/adapters';

/**
 * Word-specific host adapter implementation.
 *
 * Provides access to Word documents for saving to Spaarke DMS.
 * Requires Office.js WordApi requirement set 1.3 or higher.
 */
export class WordHostAdapter implements IHostAdapter {
  readonly hostType = 'word' as const;

  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady((info) => {
        if (info.host === Office.HostType.Word) {
          resolve();
        } else {
          reject(new Error('Not running in Word'));
        }
      });
    });
  }

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
        },
      };
    });
  }

  async getContentToSave(): Promise<IContentData> {
    const context = await this.getCurrentContext();

    // Get document as base64-encoded OOXML
    const content = await this.getDocumentContent('Ooxml');

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

  // Helper methods

  private async getDocumentContent(
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
