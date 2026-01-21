import type {
  IHostAdapter,
  IHostContext,
  IContentData,
  HostFeature,
} from '@shared/adapters';

/**
 * Outlook-specific host adapter implementation.
 *
 * Provides access to Outlook mailbox items (emails) for saving to Spaarke DMS.
 * Requires Office.js Mailbox requirement set 1.8 or higher.
 */
export class OutlookHostAdapter implements IHostAdapter {
  readonly hostType = 'outlook' as const;
  private mailbox: Office.Mailbox | null = null;
  private currentItem: Office.MessageRead | Office.AppointmentRead | null = null;

  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady((info) => {
        if (info.host === Office.HostType.Outlook) {
          this.mailbox = Office.context.mailbox;
          this.currentItem = this.mailbox.item as Office.MessageRead | Office.AppointmentRead | null;
          resolve();
        } else {
          reject(new Error('Not running in Outlook'));
        }
      });
    });
  }

  async getCurrentContext(): Promise<IHostContext> {
    if (!this.currentItem) {
      return {
        itemType: 'unknown',
        displayName: 'No item selected',
        metadata: {},
      };
    }

    // For emails (MessageRead)
    if ('subject' in this.currentItem) {
      const subject = this.getSubject();
      const sender = await this.getSender();
      const internetMessageId = this.getInternetMessageId();

      return {
        itemId: this.currentItem.itemId,
        itemType: 'email',
        displayName: subject || 'No Subject',
        metadata: {
          subject,
          sender,
          internetMessageId,
          itemType: this.currentItem.itemType,
        },
      };
    }

    return {
      itemType: 'unknown',
      displayName: 'Unknown item type',
      metadata: {},
    };
  }

  async getContentToSave(): Promise<IContentData> {
    if (!this.currentItem) {
      throw new Error('No item selected');
    }

    const context = await this.getCurrentContext();
    const body = await this.getBody('html');
    const subject = context.displayName;

    return {
      format: 'html',
      content: body,
      mimeType: 'text/html',
      fileName: `${this.sanitizeFileName(subject)}.html`,
      metadata: {
        originalName: subject,
        author: (context.metadata.sender as string) || undefined,
        hostMetadata: context.metadata,
      },
    };
  }

  supportsFeature(feature: HostFeature): boolean {
    switch (feature) {
      case 'save-as-pdf':
        return true; // Conversion happens server-side
      case 'save-as-eml':
        return this.checkRequirementSet('Mailbox', '1.8');
      case 'attachments':
        return this.checkRequirementSet('Mailbox', '1.8');
      case 'quick-create':
        return true;
      case 'entity-association':
        return true;
      case 'share-links':
        return false; // Not applicable for emails
      default:
        return false;
    }
  }

  // Helper methods

  private getSubject(): string {
    if (!this.currentItem || !('subject' in this.currentItem)) {
      return '';
    }
    // In read mode, subject is a string property
    return this.currentItem.subject || '';
  }

  private async getSender(): Promise<string> {
    return new Promise((resolve) => {
      if (!this.currentItem || !('from' in this.currentItem)) {
        resolve('');
        return;
      }

      const from = this.currentItem.from;
      resolve(from?.emailAddress || '');
    });
  }

  private getInternetMessageId(): string {
    if (!this.currentItem || !('internetMessageId' in this.currentItem)) {
      return '';
    }
    // In read mode, internetMessageId is a string property
    return this.currentItem.internetMessageId || '';
  }

  private async getBody(format: 'html' | 'text'): Promise<string> {
    return new Promise((resolve, reject) => {
      if (!this.currentItem || !('body' in this.currentItem)) {
        resolve('');
        return;
      }

      const coercionType =
        format === 'html' ? Office.CoercionType.Html : Office.CoercionType.Text;

      this.currentItem.body.getAsync(coercionType, (result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded) {
          resolve(result.value);
        } else {
          reject(new Error(result.error.message));
        }
      });
    });
  }

  private checkRequirementSet(set: string, version: string): boolean {
    try {
      return Office.context.requirements.isSetSupported(set, version);
    } catch {
      return false;
    }
  }

  private sanitizeFileName(name: string): string {
    // Remove or replace characters that are invalid in file names
    return name
      .replace(/[<>:"/\\|?*]/g, '_')
      .replace(/\s+/g, ' ')
      .trim()
      .substring(0, 200); // Limit length
  }
}
