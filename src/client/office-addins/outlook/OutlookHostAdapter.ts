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
 * Outlook-specific host adapter implementation.
 *
 * Provides access to Outlook mailbox items (emails) for saving to Spaarke DMS.
 * Requires Office.js Mailbox requirement set 1.8 or higher.
 */
export class OutlookHostAdapter implements IHostAdapter {
  private _isInitialized = false;
  private mailbox: Office.Mailbox | null = null;
  private currentItem: Office.MessageRead | Office.AppointmentRead | null = null;

  /**
   * Get the Office host type.
   */
  getHostType(): HostType {
    return 'outlook';
  }

  /**
   * Get the unique identifier of the current email.
   */
  async getItemId(): Promise<string> {
    if (!this.currentItem) {
      throw new Error('No item selected');
    }
    return this.currentItem.itemId || '';
  }

  /**
   * Get the type of the current item.
   */
  getItemType(): ItemType {
    return 'email';
  }

  /**
   * Get the subject of the current email.
   */
  async getSubject(): Promise<string> {
    if (!this.currentItem || !('subject' in this.currentItem)) {
      return '';
    }
    // In read mode, subject is a string property
    return this.currentItem.subject || '';
  }

  /**
   * Get the body content of the email.
   */
  async getBody(preferredType: 'html' | 'text' = 'html'): Promise<BodyContent> {
    return new Promise((resolve, reject) => {
      if (!this.currentItem || !('body' in this.currentItem)) {
        resolve({ content: '', type: preferredType });
        return;
      }

      const coercionType =
        preferredType === 'html' ? Office.CoercionType.Html : Office.CoercionType.Text;

      this.currentItem.body.getAsync(coercionType, (result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded) {
          resolve({
            content: result.value,
            type: preferredType,
          });
        } else {
          reject(new Error(result.error.message));
        }
      });
    });
  }

  /**
   * Get the attachments from the current email.
   */
  async getAttachments(): Promise<AttachmentInfo[]> {
    if (!this.currentItem || !('attachments' in this.currentItem)) {
      return [];
    }

    const attachments = this.currentItem.attachments;
    return attachments.map((att) => ({
      id: att.id,
      name: att.name,
      contentType: att.contentType,
      size: att.size,
      isInline: att.isInline,
    }));
  }

  /**
   * Get the content of a specific attachment.
   * Requires Mailbox API 1.8 or higher.
   */
  async getAttachmentContent(attachmentId: string): Promise<AttachmentInfo> {
    if (!this.currentItem) {
      throw new Error('No item selected');
    }

    if (!this.checkRequirementSet('Mailbox', '1.8')) {
      throw new Error('Attachment content requires Mailbox API 1.8');
    }

    return new Promise((resolve, reject) => {
      this.currentItem!.getAttachmentContentAsync(attachmentId, (result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded) {
          const content = result.value;
          // Find the attachment info
          const attachments = (this.currentItem as Office.MessageRead).attachments || [];
          const attachment = attachments.find((a) => a.id === attachmentId);

          if (!attachment) {
            reject(new Error(`Attachment ${attachmentId} not found`));
            return;
          }

          resolve({
            id: attachmentId,
            name: attachment.name,
            contentType: attachment.contentType,
            size: attachment.size,
            isInline: attachment.isInline,
            content: content.content,
          });
        } else {
          reject(new Error(result.error.message));
        }
      });
    });
  }

  /**
   * Get the sender's email address.
   */
  async getSenderEmail(): Promise<string> {
    if (!this.currentItem || !('from' in this.currentItem)) {
      return '';
    }

    const from = this.currentItem.from;
    return from?.emailAddress || '';
  }

  /**
   * Get the recipients of the current email.
   */
  async getRecipients(): Promise<Recipient[]> {
    const recipients: Recipient[] = [];

    if (!this.currentItem) {
      return recipients;
    }

    // Get To recipients
    if ('to' in this.currentItem && this.currentItem.to) {
      for (const r of this.currentItem.to) {
        recipients.push({
          email: r.emailAddress,
          displayName: r.displayName,
          type: 'to',
        });
      }
    }

    // Get CC recipients
    if ('cc' in this.currentItem && this.currentItem.cc) {
      for (const r of this.currentItem.cc) {
        recipients.push({
          email: r.emailAddress,
          displayName: r.displayName,
          type: 'cc',
        });
      }
    }

    return recipients;
  }

  /**
   * Get document content - not applicable for Outlook emails.
   */
  async getDocumentContent(_options?: GetDocumentContentOptions): Promise<ArrayBuffer> {
    // Outlook emails don't have document content in the same sense as Word
    return new ArrayBuffer(0);
  }

  /**
   * Get the capabilities of this host adapter.
   */
  getCapabilities(): HostCapabilities {
    const hasMailbox18 = this.checkRequirementSet('Mailbox', '1.8');

    return {
      canGetAttachments: hasMailbox18,
      canGetRecipients: true,
      canGetSender: true,
      canGetDocumentContent: false,
      canSaveAsPdf: true,
      canSaveAsEml: hasMailbox18,
      canInsertLink: this.isComposeMode(),
      canAttachFile: this.isComposeMode(),
      minApiVersion: '1.3',
      supportedRequirementSet: hasMailbox18 ? 'Mailbox 1.8' : 'Mailbox 1.3',
    };
  }

  /**
   * Initialize the adapter.
   */
  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady((info) => {
        if (info.host === Office.HostType.Outlook) {
          this.mailbox = Office.context.mailbox;
          this.currentItem = this.mailbox.item as Office.MessageRead | Office.AppointmentRead | null;
          this._isInitialized = true;
          resolve();
        } else {
          reject(new Error('Not running in Outlook'));
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
   * Insert a link into the email body (compose mode only).
   */
  async insertLink(url: string, displayText?: string): Promise<InsertLinkResult> {
    if (!this.isComposeMode()) {
      return {
        success: false,
        errorMessage: 'Link insertion is only available in compose mode',
      };
    }

    return new Promise((resolve) => {
      const composeItem = this.currentItem as Office.MessageCompose;
      const linkHtml = `<a href="${url}">${displayText || url}</a>`;

      composeItem.body.setSelectedDataAsync(
        linkHtml,
        { coercionType: Office.CoercionType.Html },
        (result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            resolve({ success: true });
          } else {
            resolve({ success: false, errorMessage: result.error.message });
          }
        }
      );
    });
  }

  /**
   * Attach a file to the email (compose mode only).
   */
  async attachFile(
    content: string,
    fileName: string,
    contentType: string
  ): Promise<AttachFileResult> {
    if (!this.isComposeMode()) {
      return {
        success: false,
        errorMessage: 'File attachment is only available in compose mode',
      };
    }

    return new Promise((resolve) => {
      const composeItem = this.currentItem as Office.MessageCompose;

      composeItem.addFileAttachmentFromBase64Async(
        content,
        fileName,
        { asyncContext: { contentType } },
        (result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            resolve({ success: true, attachmentId: result.value });
          } else {
            resolve({ success: false, errorMessage: result.error.message });
          }
        }
      );
    });
  }

  // Legacy methods for backward compatibility

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
      const subject = await this.getSubject();
      const sender = await this.getSenderEmail();
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
          hostType: 'outlook',
        },
      };
    }

    return {
      itemType: 'unknown',
      displayName: 'Unknown item type',
      metadata: { hostType: 'outlook' },
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
      content: body.content,
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

  // Private helper methods

  private getInternetMessageId(): string {
    if (!this.currentItem || !('internetMessageId' in this.currentItem)) {
      return '';
    }
    // In read mode, internetMessageId is a string property
    return this.currentItem.internetMessageId || '';
  }

  private checkRequirementSet(set: string, version: string): boolean {
    try {
      return Office.context.requirements.isSetSupported(set, version);
    } catch {
      return false;
    }
  }

  private isComposeMode(): boolean {
    return this.currentItem !== null && 'body' in this.currentItem &&
           typeof (this.currentItem as Office.MessageCompose).body?.setSelectedDataAsync === 'function';
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
