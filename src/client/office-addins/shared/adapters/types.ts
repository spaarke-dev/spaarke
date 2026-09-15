/**
 * Types for the host adapter pattern in Office add-ins.
 *
 * These types define the contracts for host-specific data structures
 * that are used across Outlook and Word adapters.
 */

/**
 * Represents the host type for Office add-ins.
 */
export type HostType = 'outlook' | 'word';

/**
 * Represents the type of item being processed.
 */
export type ItemType = 'email' | 'document';

/**
 * Represents the body content format.
 */
export type BodyType = 'html' | 'text';

/**
 * Information about an email attachment.
 * Used by Outlook adapter to represent attachments.
 */
export interface AttachmentInfo {
  /** Unique identifier for the attachment */
  id: string;
  /** Display name of the attachment */
  name: string;
  /** MIME content type (e.g., 'application/pdf') */
  contentType: string;
  /** Size in bytes */
  size: number;
  /** Whether the attachment is inline (embedded in body) */
  isInline: boolean;
  /** Base64-encoded content (populated when retrieved) */
  content?: string;
}

/**
 * Represents an email recipient.
 * Used by Outlook adapter for To, CC, BCC recipients.
 */
export interface Recipient {
  /** Email address of the recipient */
  email: string;
  /** Display name of the recipient (if available) */
  displayName?: string;
  /** Recipient type */
  type: 'to' | 'cc' | 'bcc';
}

/**
 * Represents the body content of an item.
 */
export interface BodyContent {
  /** The actual content (HTML or plain text) */
  content: string;
  /** The format of the content */
  type: BodyType;
}

/**
 * Describes the capabilities of a host adapter.
 * Used to determine what features are available in the current host.
 */
export interface HostCapabilities {
  /** Whether attachments can be retrieved (Outlook only) */
  canGetAttachments: boolean;
  /** Whether recipients can be retrieved (Outlook only) */
  canGetRecipients: boolean;
  /** Whether sender email can be retrieved (Outlook only) */
  canGetSender: boolean;
  /** Whether document content can be retrieved as ArrayBuffer (Word only) */
  canGetDocumentContent: boolean;
  /** Whether the open document's URL can be retrieved (Word only) */
  canGetDocumentUrl: boolean;
  /** Whether document can be saved as PDF */
  canSaveAsPdf: boolean;
  /** Whether item can be saved as EML (Outlook emails) */
  canSaveAsEml: boolean;
  /** Whether links can be inserted into the document/email */
  canInsertLink: boolean;
  /** Whether files can be attached (Outlook compose) */
  canAttachFile: boolean;
  /**
   * Whether the host can open a plain browser tab/window (`OpenBrowserWindowApi` 1.1).
   * spaarkeai-word-add-in-r1 task 027 / FR-10 — Spike-2's chosen mechanism (Option 3: read-only
   * detail in-pane + a browser-tab escape hatch) for opening a Dataverse record from the pane.
   * Decided at runtime via `Office.context.requirements.isSetSupported('OpenBrowserWindowApi',
   * '1.1')` — NOT declared as a manifest requirement (that would stop the add-in loading on hosts
   * without it). Views MUST gate the open-record affordance on this flag, never on `hostType`
   * (NFR-10) — when it is `false`, the related-record card and Document-record affordance render
   * without their open action.
   */
  canOpenBrowserWindow: boolean;
  /**
   * Whether the host can open a native new-message compose window pre-populated with a subject and
   * HTML body (`Office.context.mailbox.displayNewMessageForm`, `Mailbox` requirement set 1.6).
   * spaarkeai-word-add-in-r1 task 036 / FR-15 — Send Email via Outlook.
   *
   * `Office.context.mailbox` does not exist in Word, so this is Outlook-only. Within Outlook it is
   * further gated on the pane running in **read** mode — `displayNewMessageForm`'s documented
   * applicable mode is Message Read; calling it from a compose surface is unsupported by the host
   * API itself, not a Spaarke-side restriction. Views MUST gate the Send Email affordance on this
   * flag, never on `hostType` (NFR-10) — when it is `false`, the affordance is hidden entirely.
   */
  canComposeEmail: boolean;
  /** Minimum required Office.js API version */
  minApiVersion: string;
  /** Currently supported requirement set */
  supportedRequirementSet: string;
}

/**
 * Content for a new-message compose window (spaarkeai-word-add-in-r1 task 036 / FR-15).
 * Mirrors the subset of `Office.context.mailbox.displayNewMessageForm`'s parameters this add-in
 * uses — recipients are deliberately NOT included; the user addresses the message themselves.
 */
export interface EmailComposeContent {
  /** The message subject. */
  subject: string;
  /** The message body as HTML. */
  htmlBody: string;
}

/**
 * Result of a {@link IHostAdapter.composeNewEmail} call — always a defined result, mirroring the
 * `InsertLinkResult` / `AttachFileResult` convention (never throws for an expected "not supported"
 * outcome; callers still SHOULD gate on {@link HostCapabilities.canComposeEmail} first).
 */
export interface ComposeEmailResult {
  /** Whether the compose window was actually opened. */
  success: boolean;
  /** Error message if opening the compose window failed or is not supported. */
  errorMessage?: string;
}

/**
 * Error thrown by host adapter operations.
 */
export interface HostAdapterError {
  /** Error code for programmatic handling */
  code: HostAdapterErrorCode;
  /** Human-readable error message */
  message: string;
  /** Original error if wrapping another error */
  innerError?: Error | undefined;
}

/**
 * Error codes for host adapter operations.
 */
export type HostAdapterErrorCode =
  | 'NOT_INITIALIZED'
  | 'NO_ITEM_SELECTED'
  | 'INVALID_HOST'
  | 'CAPABILITY_NOT_SUPPORTED'
  | 'ATTACHMENT_NOT_FOUND'
  | 'CONTENT_RETRIEVAL_FAILED'
  | 'API_NOT_AVAILABLE'
  | 'UNKNOWN_ERROR';

/**
 * Result of a link insertion operation.
 */
export interface InsertLinkResult {
  /** Whether the insertion was successful */
  success: boolean;
  /** Error message if insertion failed */
  errorMessage?: string;
}

/**
 * Result of a file attachment operation.
 */
export interface AttachFileResult {
  /** Whether the attachment was successful */
  success: boolean;
  /** The ID assigned to the attachment by the host */
  attachmentId?: string;
  /** Error message if attachment failed */
  errorMessage?: string;
}

/**
 * Options for getting document content.
 */
export interface GetDocumentContentOptions {
  /** The format to retrieve the document in */
  format: 'ooxml' | 'html' | 'text' | 'pdf';
}

/**
 * Metadata about the current item context.
 */
export interface ItemMetadata {
  /** When the item was created */
  createdDate?: Date;
  /** When the item was last modified */
  modifiedDate?: Date;
  /** Author or sender of the item */
  author?: string;
  /** Additional host-specific metadata */
  [key: string]: unknown;
}

/**
 * Email-specific metadata.
 */
export interface EmailMetadata extends ItemMetadata {
  /** Internet message ID (RFC 2822) */
  internetMessageId?: string;
  /** Conversation/thread ID */
  conversationId?: string;
  /** Email importance level */
  importance?: 'low' | 'normal' | 'high';
  /** Whether the email has attachments */
  hasAttachments?: boolean;
  /** Received date for the email */
  receivedDate?: Date;
  /** Sent date for the email */
  sentDate?: Date;
}

/**
 * Document-specific metadata.
 */
export interface DocumentMetadata extends ItemMetadata {
  /** Document title */
  title?: string;
  /** Document file path (if available) */
  filePath?: string;
  /** Document word count */
  wordCount?: number;
  /** Last printed date */
  lastPrintedDate?: Date;
}
