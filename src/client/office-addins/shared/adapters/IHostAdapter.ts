/**
 * Host adapter interface for Office add-in host-specific operations.
 *
 * Implementations provide host-specific (Outlook/Word) functionality
 * while the shared taskpane uses a common interface. This pattern allows
 * the UI code to be host-agnostic while still accessing host-specific features.
 *
 * @example
 * ```typescript
 * const adapter = HostAdapterFactory.create();
 * await adapter.initialize();
 *
 * const hostType = adapter.getHostType();
 * const subject = await adapter.getSubject();
 * const body = await adapter.getBody();
 *
 * if (adapter.getCapabilities().canGetAttachments) {
 *   const attachments = await adapter.getAttachments();
 * }
 * ```
 */

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
} from './types';

/**
 * Interface for host-specific operations in Office add-ins.
 *
 * The adapter pattern abstracts the differences between Office hosts (Outlook, Word)
 * so the shared task pane UI can work with a consistent API regardless of the host.
 */
export interface IHostAdapter {
  /**
   * Get the Office host type.
   * @returns The host type ('outlook' or 'word')
   */
  getHostType(): HostType;

  /**
   * Get the unique identifier of the current item.
   *
   * - For Outlook emails: Returns the item ID
   * - For Word documents: Returns the document URL or a generated identifier
   *
   * @returns Promise resolving to the item ID
   * @throws When no item is selected or available
   */
  getItemId(): Promise<string>;

  /**
   * Get the type of the current item.
   * @returns The item type ('email' for Outlook, 'document' for Word)
   */
  getItemType(): ItemType;

  /**
   * Get the subject or title of the current item.
   *
   * - For Outlook: Returns the email subject
   * - For Word: Returns the document title
   *
   * @returns Promise resolving to the subject/title string
   */
  getSubject(): Promise<string>;

  /**
   * Get the body content of the current item.
   *
   * - For Outlook: Returns the email body (HTML or text)
   * - For Word: Returns the document body content (HTML or text)
   *
   * @param preferredType - Preferred content type ('html' or 'text'). Defaults to 'html'.
   * @returns Promise resolving to the body content with type indicator
   */
  getBody(preferredType?: 'html' | 'text'): Promise<BodyContent>;

  /**
   * Get the attachments from the current item.
   *
   * This method is primarily for Outlook emails. Word documents do not have
   * attachments in the same sense.
   *
   * @returns Promise resolving to array of attachment information.
   *          Returns empty array for Word or when no attachments exist.
   * @throws When attachment retrieval fails
   */
  getAttachments(): Promise<AttachmentInfo[]>;

  /**
   * Get the content of a specific attachment.
   *
   * Requires Outlook Mailbox API 1.8 or higher.
   *
   * @param attachmentId - The ID of the attachment to retrieve
   * @returns Promise resolving to the attachment info with content populated
   * @throws When attachment is not found or retrieval fails
   */
  getAttachmentContent(attachmentId: string): Promise<AttachmentInfo>;

  /**
   * Get the sender's email address.
   *
   * This method is only applicable for Outlook emails.
   *
   * @returns Promise resolving to the sender's email address.
   *          Returns empty string for Word documents.
   */
  getSenderEmail(): Promise<string>;

  /**
   * Get the recipients of the current email.
   *
   * This method is only applicable for Outlook emails.
   *
   * @returns Promise resolving to array of recipients (To, CC, BCC).
   *          Returns empty array for Word documents.
   */
  getRecipients(): Promise<Recipient[]>;

  /**
   * Get the document content as an ArrayBuffer.
   *
   * This method is only applicable for Word documents.
   *
   * @param options - Options for content retrieval (format, etc.)
   * @returns Promise resolving to the document content as ArrayBuffer.
   *          Returns empty ArrayBuffer for Outlook emails.
   */
  getDocumentContent(options?: GetDocumentContentOptions): Promise<ArrayBuffer>;

  /**
   * Get the capabilities of this host adapter.
   *
   * Use this to determine what features are available before calling
   * host-specific methods.
   *
   * @returns The capabilities object describing what this adapter supports
   */
  getCapabilities(): HostCapabilities;

  /**
   * Initialize the adapter.
   *
   * Must be called after Office.js is ready. Sets up the adapter
   * and validates that we're running in the expected host.
   *
   * @throws When not running in the expected Office host
   */
  initialize(): Promise<void>;

  /**
   * Check if the adapter has been initialized.
   * @returns True if initialize() has been called successfully
   */
  isInitialized(): boolean;

  /**
   * Insert a link into the current item.
   *
   * - For Outlook (compose mode): Inserts the link into the email body
   * - For Word: Inserts the link at the current cursor position
   *
   * @param url - The URL to insert
   * @param displayText - Optional display text for the link
   * @returns Promise resolving to the result of the insertion
   */
  insertLink(url: string, displayText?: string): Promise<InsertLinkResult>;

  /**
   * Attach a file to the current item.
   *
   * This method is primarily for Outlook compose mode.
   *
   * @param content - Base64-encoded file content
   * @param fileName - Name of the file to attach
   * @param contentType - MIME type of the file
   * @returns Promise resolving to the result of the attachment
   */
  attachFile(
    content: string,
    fileName: string,
    contentType: string
  ): Promise<AttachFileResult>;
}

/**
 * Context information about the current item or document.
 * @deprecated Use IHostAdapter methods directly instead.
 */
export interface IHostContext {
  /** Unique identifier for the item */
  itemId?: string;
  /** Type of item (email, document, etc.) */
  itemType: 'email' | 'document' | 'unknown';
  /** Display name or subject */
  displayName: string;
  /** Additional metadata depending on host */
  metadata: Record<string, unknown>;
}

/**
 * Content data to be saved to Spaarke DMS.
 * @deprecated Use IHostAdapter.getBody() and related methods instead.
 */
export interface IContentData {
  /** Content format (html, pdf, eml, docx, etc.) */
  format: ContentFormat;
  /** The actual content (base64 encoded for binary formats) */
  content: string;
  /** MIME type of the content */
  mimeType: string;
  /** File name for the content */
  fileName: string;
  /** Additional metadata */
  metadata: IContentMetadata;
}

/**
 * Content format types supported by the add-ins.
 */
export type ContentFormat = 'html' | 'text' | 'pdf' | 'eml' | 'docx' | 'binary';

/**
 * Metadata associated with content.
 */
export interface IContentMetadata {
  /** Original file name or subject */
  originalName: string;
  /** Size in bytes */
  sizeBytes?: number | undefined;
  /** Created date */
  createdDate?: Date | undefined;
  /** Modified date */
  modifiedDate?: Date | undefined;
  /** Author or sender */
  author?: string | undefined;
  /** Host-specific metadata */
  hostMetadata: Record<string, unknown>;
}

/**
 * Features that may or may not be supported by a host.
 * @deprecated Use IHostAdapter.getCapabilities() instead.
 */
export type HostFeature =
  | 'save-as-pdf'
  | 'save-as-eml'
  | 'attachments'
  | 'quick-create'
  | 'entity-association'
  | 'share-links';
