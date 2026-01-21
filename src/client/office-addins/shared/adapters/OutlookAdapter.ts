/**
 * Outlook-specific host adapter implementation.
 *
 * Provides access to Outlook mailbox items (emails) for saving to Spaarke DMS.
 * Implements the IHostAdapter interface for read mode (email viewing) and
 * compose mode (email creation).
 *
 * Requirements:
 * - Mailbox 1.5+ for basic email access
 * - Mailbox 1.8+ for getAttachmentContentAsync (attachment binary retrieval)
 *
 * @example
 * ```typescript
 * const adapter = new OutlookAdapter();
 * await adapter.initialize();
 *
 * // Check current mode
 * const capabilities = adapter.getCapabilities();
 * if (capabilities.canGetAttachments) {
 *   const attachments = await adapter.getAttachments();
 *   for (const attachment of attachments) {
 *     const withContent = await adapter.getAttachmentContent(attachment.id);
 *     console.log(`${attachment.name}: ${withContent.content?.length} bytes`);
 *   }
 * }
 * ```
 */

import type { IHostAdapter } from './IHostAdapter';
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
  HostAdapterError,
  HostAdapterErrorCode,
} from './types';

/**
 * Represents the mode the Outlook item is in.
 */
type ItemMode = 'read' | 'compose' | 'unknown';

/**
 * Helper function to create HostAdapterError.
 */
function createHostAdapterError(
  code: HostAdapterErrorCode,
  message: string,
  innerError?: Error
): HostAdapterError {
  return { code, message, innerError };
}

/**
 * OutlookAdapter implements IHostAdapter for Outlook emails.
 *
 * Handles both read mode (viewing emails) and compose mode (creating emails).
 * Read mode supports: email metadata, body retrieval, attachment listing and content.
 * Compose mode supports: inserting links, attaching files.
 */
export class OutlookAdapter implements IHostAdapter {
  private _initialized = false;
  private _mailbox: Office.Mailbox | null = null;
  private _currentMode: ItemMode = 'unknown';

  /**
   * Check if Mailbox requirement set is supported.
   *
   * @param version - The minimum version required (e.g., '1.8')
   * @returns True if the requirement set is supported
   */
  private isMailboxSupported(version: string): boolean {
    try {
      return Office.context.requirements.isSetSupported('Mailbox', version);
    } catch {
      return false;
    }
  }

  /**
   * Get the current mailbox item.
   *
   * @throws When no item is available or not initialized
   */
  private getCurrentItem(): Office.Item {
    if (!this._initialized || !this._mailbox) {
      throw createHostAdapterError(
        'NOT_INITIALIZED',
        'Adapter not initialized. Call initialize() first.'
      );
    }

    const item = this._mailbox.item;
    if (!item) {
      throw createHostAdapterError(
        'NO_ITEM_SELECTED',
        'No email item is currently selected or available.'
      );
    }

    return item;
  }

  /**
   * Get the current item in read mode.
   *
   * @throws When not in read mode or no item is available
   */
  private getReadItem(): Office.MessageRead {
    const item = this.getCurrentItem();
    if (this._currentMode !== 'read') {
      throw createHostAdapterError(
        'CAPABILITY_NOT_SUPPORTED',
        'This operation requires read mode. Current mode: ' + this._currentMode
      );
    }
    return item as Office.MessageRead;
  }

  /**
   * Get the current item in compose mode.
   *
   * @throws When not in compose mode or no item is available
   */
  private getComposeItem(): Office.MessageCompose {
    const item = this.getCurrentItem();
    if (this._currentMode !== 'compose') {
      throw createHostAdapterError(
        'CAPABILITY_NOT_SUPPORTED',
        'This operation requires compose mode. Current mode: ' + this._currentMode
      );
    }
    return item as Office.MessageCompose;
  }

  /**
   * Determine the current item mode (read or compose).
   */
  private determineMode(item: Office.Item | null): ItemMode {
    if (!item) {
      return 'unknown';
    }

    // Check for properties that are unique to read mode
    // In read mode, 'from' is a property (EmailAddressDetails)
    // In compose mode, 'from' is an Office.From object with getAsync
    if ('itemType' in item) {
      // MessageRead has 'from' as EmailAddressDetails
      // MessageCompose has 'from' as Office.From (has getAsync method)
      const fromProperty = (item as unknown as Record<string, unknown>).from;
      if (fromProperty) {
        // In compose mode, from has getAsync method
        if (typeof fromProperty === 'object' && fromProperty !== null && 'getAsync' in fromProperty) {
          return 'compose';
        }
        // In read mode, from is EmailAddressDetails (has emailAddress property directly)
        if (typeof fromProperty === 'object' && fromProperty !== null && 'emailAddress' in fromProperty) {
          return 'read';
        }
      }

      // Alternative check: read mode has internetMessageId as a string
      // compose mode does not have internetMessageId
      if ('internetMessageId' in item) {
        return 'read';
      }

      // Another check: compose mode has body.setAsync, read mode only has body.getAsync
      // Actually both have getAsync, but compose mode allows setting
      if ('subject' in item) {
        const subject = (item as unknown as Record<string, unknown>).subject;
        // In compose mode, subject is a Subject object with getAsync/setAsync
        if (typeof subject === 'object' && subject !== null && 'setAsync' in subject) {
          return 'compose';
        }
        // In read mode, subject is a string
        if (typeof subject === 'string') {
          return 'read';
        }
      }
    }

    return 'unknown';
  }

  // ============================================================
  // IHostAdapter Implementation
  // ============================================================

  /**
   * @inheritdoc
   */
  getHostType(): HostType {
    return 'outlook';
  }

  /**
   * @inheritdoc
   */
  async getItemId(): Promise<string> {
    const item = this.getCurrentItem();

    if (this._currentMode === 'read') {
      const readItem = item as Office.MessageRead;
      return readItem.itemId;
    }

    if (this._currentMode === 'compose') {
      // In compose mode, we need to get the item ID asynchronously
      const composeItem = item as Office.MessageCompose;
      return new Promise((resolve, reject) => {
        composeItem.getItemIdAsync((result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            // ItemId may be null for unsaved drafts
            resolve(result.value ?? '');
          } else {
            reject(
              createHostAdapterError(
                'CONTENT_RETRIEVAL_FAILED',
                result.error?.message ?? 'Failed to get item ID'
              )
            );
          }
        });
      });
    }

    throw createHostAdapterError(
      'UNKNOWN_ERROR',
      'Unable to determine item mode'
    );
  }

  /**
   * @inheritdoc
   */
  getItemType(): ItemType {
    return 'email';
  }

  /**
   * @inheritdoc
   */
  async getSubject(): Promise<string> {
    const item = this.getCurrentItem();

    if (this._currentMode === 'read') {
      const readItem = item as Office.MessageRead;
      return readItem.subject ?? '';
    }

    if (this._currentMode === 'compose') {
      const composeItem = item as Office.MessageCompose;
      return new Promise((resolve, reject) => {
        composeItem.subject.getAsync((result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            resolve(result.value ?? '');
          } else {
            reject(
              createHostAdapterError(
                'CONTENT_RETRIEVAL_FAILED',
                result.error?.message ?? 'Failed to get subject'
              )
            );
          }
        });
      });
    }

    return '';
  }

  /**
   * @inheritdoc
   */
  async getBody(preferredType: 'html' | 'text' = 'html'): Promise<BodyContent> {
    const item = this.getCurrentItem();

    const coercionType =
      preferredType === 'html' ? Office.CoercionType.Html : Office.CoercionType.Text;

    return new Promise((resolve, reject) => {
      item.body.getAsync(coercionType, (result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded) {
          resolve({
            content: result.value ?? '',
            type: preferredType,
          });
        } else {
          reject(
            createHostAdapterError(
              'CONTENT_RETRIEVAL_FAILED',
              result.error?.message ?? 'Failed to get body'
            )
          );
        }
      });
    });
  }

  /**
   * @inheritdoc
   */
  async getAttachments(): Promise<AttachmentInfo[]> {
    if (this._currentMode !== 'read') {
      // In compose mode, attachments is different
      // For now, return empty array as compose mode attachment listing is complex
      return [];
    }

    const item = this.getReadItem();
    const attachments = item.attachments;

    if (!attachments || attachments.length === 0) {
      return [];
    }

    return attachments.map(
      (attachment): AttachmentInfo => ({
        id: attachment.id,
        name: attachment.name,
        contentType: attachment.contentType,
        size: attachment.size,
        isInline: attachment.isInline,
        // Content is not populated here - use getAttachmentContent() to retrieve
      })
    );
  }

  /**
   * @inheritdoc
   */
  async getAttachmentContent(attachmentId: string): Promise<AttachmentInfo> {
    // Check if Mailbox 1.8 is supported (required for getAttachmentContentAsync)
    if (!this.isMailboxSupported('1.8')) {
      throw createHostAdapterError(
        'API_NOT_AVAILABLE',
        'Attachment content retrieval requires Mailbox API 1.8 or higher. ' +
        'The current client does not support this requirement set.'
      );
    }

    if (this._currentMode !== 'read') {
      throw createHostAdapterError(
        'CAPABILITY_NOT_SUPPORTED',
        'Attachment content retrieval is only available in read mode.'
      );
    }

    const item = this.getReadItem();

    // Find the attachment metadata first
    const attachments = item.attachments;
    const attachmentMeta = attachments?.find((a) => a.id === attachmentId);

    if (!attachmentMeta) {
      throw createHostAdapterError(
        'ATTACHMENT_NOT_FOUND',
        `Attachment with ID '${attachmentId}' not found.`
      );
    }

    return new Promise((resolve, reject) => {
      item.getAttachmentContentAsync(attachmentId, (result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded) {
          const content = result.value;
          resolve({
            id: attachmentMeta.id,
            name: attachmentMeta.name,
            contentType: attachmentMeta.contentType,
            size: attachmentMeta.size,
            isInline: attachmentMeta.isInline,
            // content.content is base64-encoded for file attachments
            // content.format indicates the format (Base64, Url, etc.)
            content: content.content,
          });
        } else {
          reject(
            createHostAdapterError(
              'CONTENT_RETRIEVAL_FAILED',
              result.error?.message ?? 'Failed to get attachment content'
            )
          );
        }
      });
    });
  }

  /**
   * @inheritdoc
   */
  async getSenderEmail(): Promise<string> {
    if (this._currentMode !== 'read') {
      // In compose mode, sender is the current user
      // Could potentially get from Office.context.mailbox.userProfile
      return this._mailbox?.userProfile?.emailAddress ?? '';
    }

    const item = this.getReadItem();
    return item.from?.emailAddress ?? '';
  }

  /**
   * Get the sender's display name.
   *
   * @returns The sender's display name or empty string
   */
  async getSenderDisplayName(): Promise<string> {
    if (this._currentMode !== 'read') {
      return this._mailbox?.userProfile?.displayName ?? '';
    }

    const item = this.getReadItem();
    return item.from?.displayName ?? '';
  }

  /**
   * @inheritdoc
   */
  async getRecipients(): Promise<Recipient[]> {
    const recipients: Recipient[] = [];

    if (this._currentMode === 'read') {
      const item = this.getReadItem();

      // To recipients
      if (item.to) {
        for (const to of item.to) {
          recipients.push({
            email: to.emailAddress,
            displayName: to.displayName,
            type: 'to',
          });
        }
      }

      // CC recipients
      if (item.cc) {
        for (const cc of item.cc) {
          recipients.push({
            email: cc.emailAddress,
            displayName: cc.displayName,
            type: 'cc',
          });
        }
      }

      // BCC recipients (if available - may not be for received emails)
      if (item.bcc) {
        for (const bcc of item.bcc) {
          recipients.push({
            email: bcc.emailAddress,
            displayName: bcc.displayName,
            type: 'bcc',
          });
        }
      }
    } else if (this._currentMode === 'compose') {
      const item = this.getComposeItem();

      // Get To recipients
      const toRecipients = await this.getRecipientsAsync(item.to, 'to');
      recipients.push(...toRecipients);

      // Get CC recipients
      const ccRecipients = await this.getRecipientsAsync(item.cc, 'cc');
      recipients.push(...ccRecipients);

      // Get BCC recipients
      const bccRecipients = await this.getRecipientsAsync(item.bcc, 'bcc');
      recipients.push(...bccRecipients);
    }

    return recipients;
  }

  /**
   * Helper to get recipients asynchronously (for compose mode).
   */
  private async getRecipientsAsync(
    recipientField: Office.Recipients,
    type: 'to' | 'cc' | 'bcc'
  ): Promise<Recipient[]> {
    return new Promise((resolve) => {
      recipientField.getAsync((result) => {
        if (result.status === Office.AsyncResultStatus.Succeeded && result.value) {
          const recipients = result.value.map((r): Recipient => ({
            email: r.emailAddress,
            displayName: r.displayName,
            type,
          }));
          resolve(recipients);
        } else {
          // If we can't get recipients, return empty array
          resolve([]);
        }
      });
    });
  }

  /**
   * @inheritdoc
   */
  async getDocumentContent(_options?: GetDocumentContentOptions): Promise<ArrayBuffer> {
    // Outlook emails are not documents - return empty ArrayBuffer
    return new ArrayBuffer(0);
  }

  /**
   * @inheritdoc
   */
  getCapabilities(): HostCapabilities {
    const hasMailbox18 = this.isMailboxSupported('1.8');
    const isReadMode = this._currentMode === 'read';
    const isComposeMode = this._currentMode === 'compose';

    return {
      // Attachment operations require read mode and Mailbox 1.8
      canGetAttachments: isReadMode && hasMailbox18,
      // Recipient retrieval is available in both modes
      canGetRecipients: true,
      // Sender retrieval is available in both modes
      canGetSender: true,
      // Document content is not available for emails
      canGetDocumentContent: false,
      // PDF conversion is server-side, so we can indicate support
      canSaveAsPdf: true,
      // EML saving requires Mailbox 1.8 for full attachment support
      canSaveAsEml: hasMailbox18,
      // Link insertion is available in compose mode
      canInsertLink: isComposeMode,
      // File attachment is available in compose mode
      canAttachFile: isComposeMode,
      // Minimum API version for basic functionality
      minApiVersion: '1.5',
      // Actual supported version
      supportedRequirementSet: hasMailbox18 ? 'Mailbox 1.8' : 'Mailbox 1.5',
    };
  }

  /**
   * @inheritdoc
   */
  async initialize(): Promise<void> {
    return new Promise((resolve, reject) => {
      Office.onReady((info) => {
        if (info.host !== Office.HostType.Outlook) {
          reject(
            createHostAdapterError(
              'INVALID_HOST',
              `Expected Outlook host, but got: ${info.host}`
            )
          );
          return;
        }

        this._mailbox = Office.context.mailbox;
        this._currentMode = this.determineMode(this._mailbox?.item ?? null);
        this._initialized = true;

        resolve();
      });
    });
  }

  /**
   * @inheritdoc
   */
  isInitialized(): boolean {
    return this._initialized;
  }

  /**
   * @inheritdoc
   */
  async insertLink(url: string, displayText?: string): Promise<InsertLinkResult> {
    if (this._currentMode !== 'compose') {
      return {
        success: false,
        errorMessage: 'Link insertion is only available in compose mode.',
      };
    }

    const item = this.getComposeItem();
    const linkHtml = displayText
      ? `<a href="${this.escapeHtml(url)}">${this.escapeHtml(displayText)}</a>`
      : `<a href="${this.escapeHtml(url)}">${this.escapeHtml(url)}</a>`;

    return new Promise((resolve) => {
      item.body.setSelectedDataAsync(
        linkHtml,
        { coercionType: Office.CoercionType.Html },
        (result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            resolve({ success: true });
          } else {
            resolve({
              success: false,
              errorMessage: result.error?.message ?? 'Failed to insert link',
            });
          }
        }
      );
    });
  }

  /**
   * @inheritdoc
   */
  async attachFile(
    content: string,
    fileName: string,
    contentType: string
  ): Promise<AttachFileResult> {
    if (this._currentMode !== 'compose') {
      return {
        success: false,
        errorMessage: 'File attachment is only available in compose mode.',
      };
    }

    // Check for addFileAttachmentFromBase64Async support (Mailbox 1.8)
    if (!this.isMailboxSupported('1.8')) {
      return {
        success: false,
        errorMessage:
          'File attachment requires Mailbox API 1.8 or higher. ' +
          'The current client does not support this feature.',
      };
    }

    const item = this.getComposeItem();

    return new Promise((resolve) => {
      item.addFileAttachmentFromBase64Async(
        content,
        fileName,
        { isInline: false, contentType },
        (result) => {
          if (result.status === Office.AsyncResultStatus.Succeeded) {
            resolve({
              success: true,
              attachmentId: result.value,
            });
          } else {
            resolve({
              success: false,
              errorMessage: result.error?.message ?? 'Failed to attach file',
            });
          }
        }
      );
    });
  }

  // ============================================================
  // Additional Outlook-Specific Methods
  // ============================================================

  /**
   * Get the internet message ID (RFC 2822 Message-ID).
   *
   * This is used for duplicate detection and email threading.
   * Only available in read mode.
   *
   * @returns The internet message ID or empty string
   */
  getInternetMessageId(): string {
    if (this._currentMode !== 'read') {
      return '';
    }

    try {
      const item = this.getReadItem();
      return item.internetMessageId ?? '';
    } catch {
      return '';
    }
  }

  /**
   * Get the conversation ID for email threading.
   *
   * Only available in read mode.
   *
   * @returns The conversation ID or empty string
   */
  getConversationId(): string {
    if (this._currentMode !== 'read') {
      return '';
    }

    try {
      const item = this.getReadItem();
      return item.conversationId ?? '';
    } catch {
      return '';
    }
  }

  /**
   * Get internet headers for deduplication.
   *
   * Retrieves specific headers that can be used for duplicate detection:
   * - Message-ID: Unique message identifier
   * - In-Reply-To: Parent message ID (for threading)
   * - References: Thread references
   *
   * Requires Mailbox 1.8+.
   *
   * @returns Object with header names and values
   */
  async getInternetHeaders(): Promise<Record<string, string>> {
    if (!this.isMailboxSupported('1.8')) {
      // Fall back to just the internetMessageId if available
      const messageId = this.getInternetMessageId();
      return messageId ? { 'Message-ID': messageId } : {};
    }

    if (this._currentMode !== 'read') {
      return {};
    }

    const item = this.getReadItem();

    // Note: getAllInternetHeadersAsync is available in Mailbox 1.8
    // but getInternetHeadersAsync (specific headers) is available in 1.10
    // We'll use internetMessageId directly for 1.8 compatibility
    const headers: Record<string, string> = {};

    // Get the internet message ID (always available in read mode)
    const messageId = item.internetMessageId;
    if (messageId) {
      headers['Message-ID'] = messageId;
    }

    // Get the conversation ID
    const conversationId = item.conversationId;
    if (conversationId) {
      headers['X-Conversation-ID'] = conversationId;
    }

    return headers;
  }

  /**
   * Get the email importance level.
   *
   * @returns The importance level ('low', 'normal', 'high')
   */
  getImportance(): 'low' | 'normal' | 'high' {
    if (this._currentMode !== 'read') {
      return 'normal';
    }

    try {
      const item = this.getReadItem();
      const importance = item.importance;

      // Office.MailboxEnums.Importance maps to string values
      if (importance === Office.MailboxEnums.Importance.Low) {
        return 'low';
      }
      if (importance === Office.MailboxEnums.Importance.High) {
        return 'high';
      }
      return 'normal';
    } catch {
      return 'normal';
    }
  }

  /**
   * Get the received date of the email.
   *
   * @returns The received date or undefined
   */
  getReceivedDate(): Date | undefined {
    if (this._currentMode !== 'read') {
      return undefined;
    }

    try {
      const item = this.getReadItem();
      return item.dateTimeCreated ?? undefined;
    } catch {
      return undefined;
    }
  }

  /**
   * Get the sent date of the email.
   *
   * @returns The sent date or undefined
   */
  getSentDate(): Date | undefined {
    if (this._currentMode !== 'read') {
      return undefined;
    }

    try {
      const item = this.getReadItem();
      return item.dateTimeModified ?? undefined;
    } catch {
      return undefined;
    }
  }

  /**
   * Get the current mode (read or compose).
   *
   * @returns The current item mode
   */
  getCurrentMode(): ItemMode {
    return this._currentMode;
  }

  // ============================================================
  // Private Helper Methods
  // ============================================================

  /**
   * Escape HTML special characters for safe insertion.
   */
  private escapeHtml(text: string): string {
    const map: Record<string, string> = {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#039;',
    };
    return text.replace(/[&<>"']/g, (char) => map[char] ?? char);
  }
}

// Export the adapter class
export default OutlookAdapter;
