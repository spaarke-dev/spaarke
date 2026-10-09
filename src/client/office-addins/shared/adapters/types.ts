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
  /**
   * What kind of attachment Outlook says this is (`Office.MailboxEnums.AttachmentType`): a `file`, an attached
   * Outlook `item` (a forwarded email or a meeting — its `name` has no extension), or a `cloud` link to a file kept
   * elsewhere (OneDrive/SharePoint), which has no file content in the email at all. Absent when the host did not say.
   * Task 116a: the email save reads attachment content in the add-in and must treat these three differently.
   */
  attachmentType?: 'file' | 'item' | 'cloud';
  /**
   * The form {@link content} came back in (`Office.MailboxEnums.AttachmentContentFormat`), set only by
   * `getAttachmentContent()`: `base64` (file attachments), `eml` / `icalendar` (an attached item, as TEXT — not
   * base64), or `url` (a cloud attachment: the content is a link, not the file).
   */
  contentFormat?: 'base64' | 'eml' | 'icalendar' | 'url';
  /** The content (populated when retrieved) — base64 unless {@link contentFormat} says otherwise */
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
  /**
   * Whether the host can read the client-side custom XML identity stamp task 014 (FR-02) writes into
   * every saved `.docx` — `Office.context.document.customXmlParts`, the Common API `CustomXmlParts`
   * requirement set (019 condition 1: NOT `Word.Document.customXmlParts`, which is WordApi 1.4 and
   * would drop Office 2019/2021 LTSC). spaarkeai-word-add-in-r1 task 051 (FR-02 client half).
   *
   * Word-only — no open document in Outlook, same reasoning as {@link canGetDocumentUrl}. UNLIKE
   * `canGetDocumentUrl`, this is NOT unconditionally true for Word: it is further gated at runtime on
   * `Office.context.requirements.isSetSupported('CustomXmlParts')` (019 condition 4) — a host can
   * satisfy this add-in's `WordApi` 1.3 floor and still lack the separate `CustomXmlParts` Common
   * requirement set. Views/services MUST gate the stamp read on this flag, never on `hostType`
   * (NFR-10) — when it is `false`, `readDocumentStamp()` is never called, and identity resolution
   * falls back to the URL-only path task 013 already shipped.
   */
  canReadDocumentStamp: boolean;
  /**
   * Whether the host can WRITE the identity stamp into the document open in Word after a save
   * (spaarkeai-word-add-in-r1 task 089, UAT-9) — `Office.context.document.customXmlParts.addAsync` /
   * `CustomXmlPart.deleteAsync`, the same Common API `CustomXmlParts` set as the read (019 conditions 1 and 4).
   *
   * Word-only and runtime-gated exactly like {@link canReadDocumentStamp}; `false` in Outlook, which has no open
   * document. Callers MUST gate `writeDocumentStamp()` on this flag, never on `hostType` (NFR-10). When it is
   * `false` no write is attempted and the save is reported exactly as before.
   */
  canWriteDocumentStamp: boolean;
  /**
   * Whether the pane can ask Spaarke whether the open EMAIL is already saved — i.e. whether the host can supply the
   * email's identity keys ({@link IHostAdapter.getEmailIdentityKeys}: the RFC Message-ID and the Exchange item id) for
   * `POST /api/documents/resolve-email-identity` (spaarkeai-word-add-in-r1 task 120, UAT round 12 O6).
   *
   * True for Outlook in READ mode (a received or sent message has a Message-ID; a draft being composed has none).
   * False for Word, which identifies its document by URL and stamp instead ({@link canGetDocumentUrl},
   * {@link canReadDocumentStamp}). Optional so existing fixtures stay valid: absent = `false`. Views/services MUST gate
   * the email lookup on this flag, never on `hostType` (NFR-10).
   */
  canResolveEmailIdentity?: boolean;
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
  /**
   * Whether the pane offers its own **Email tab** — an in-pane email form, built from the shared Spaarke compose
   * engine, that sends the open document as an ATTACHMENT from the user's own mailbox and records the email
   * against the document's related record (spaarkeai-word-add-in-r1 task 096, owner UAT round 4 items 6-8).
   *
   * True for Word: the pane's item is a document, which the form attaches by its `sprk_document` id. False for
   * Outlook, by the owner's 2026-10-04 decision ("Outlook unchanged"): the pane's item there is itself an email,
   * and Send Email keeps opening Outlook's native compose window ({@link canComposeEmail}). The tab table
   * (`TaskPaneNavigation.getAvailableTabs`) gates the Email tab on this flag, never on `hostType` (NFR-10).
   */
  canEmailFromPane: boolean;
  /**
   * Whether the host can show the linked-to-dos indicator banner (`LinkedTodosBanner`, count of
   * `sprk_todo` rows carrying `sprk_regardingcommunication` for the current item) —
   * spaarkeai-word-add-in-r1 task 040 / FR-19, formalizing smart-todo-decoupling-r3 FR-28 / A-1.
   *
   * Spec's parity boundary (spec.md Assumptions) lists "linked-todos" under Outlook-only, alongside
   * email/attachment save and triage. A Word document has no `sprk_communication` counterpart, so
   * there is nothing for the banner's query to key off. Views MUST gate the banner on this flag,
   * never on `hostType` (NFR-10) — when it is `false`, the banner never renders, however
   * `communicationId` was sourced.
   */
  canShowLinkedTodos: boolean;
  /**
   * Whether the host can fetch the Association Engine's ranked "Related to" auto-match candidates
   * for the Save tab's reconciliation-style cards (`communicationSuggestionsService.fetchRelatedCandidates`,
   * `GET /api/office/communications/by-message-id/{id}/suggestions`) — spaarkeai-word-add-in-r1 task
   * 040 / FR-19, formalizing the pre-existing "Outlook only" gate `SaveFlow.tsx` already carried.
   *
   * Spec's parity boundary (spec.md Assumptions) lists "triage" under Outlook-only. The engine keys
   * off the captured email's sender/recipients/thread signals (`internetMessageId`) — a Word document
   * has no equivalent captured-communication record to look up. Views MUST gate the auto-match fetch
   * on this flag, never on `hostType` (NFR-10).
   */
  canSuggestRelatedRecords: boolean;
  /**
   * Whether the host can supply the currently open item's OWN name as the FR-06 default for the
   * Save tab's "Document Name" field — i.e. whether that field should pre-populate from the item
   * and offer the pencil-edit affordance, rather than start as a plain empty input —
   * spaarkeai-word-add-in-r1 task 020 / FR-06 (converted to a capability per task 040's NFR-10
   * pattern, coordinator follow-up after task 020's initial `hostType === 'word'` gate).
   *
   * True for Word: the open document has its own name (`WordAdapter.getSubject()`, the Title
   * property Word falls back to "Untitled Document" for) independent of anything typed elsewhere.
   *
   * False for Outlook — **not** because Outlook cannot supply a name (`OutlookAdapter.getSubject()`
   * returns the email subject, which certainly qualifies as "the item's own name" in the abstract).
   * It is false because Outlook's Document Name box already has a DIFFERENT, pre-existing contract
   * (task 046 (b)): typing into it overrides the email's subject and sets
   * `email.isNameSystemDerived: false`, which decides whether the stored `.eml` file gets a
   * collision-avoiding unique suffix. Pre-populating/pencil-editing that box here would make an
   * untouched field register as "user typed it" and silently disable the suffix — the exact
   * collision this capability must not reintroduce (task 020's binding escalation-trigger boundary:
   * "do not adapt the Email or Attachment paths"). Views MUST gate the Document Name default/pencil
   * UI on this flag, never on `hostType` (NFR-10).
   */
  canProvideDocumentName: boolean;
  /**
   * Whether the host can report when the open document's CONTENT changes — `Word.Document`'s
   * `onParagraphAdded` / `onParagraphChanged` / `onParagraphDeleted` events, requirement set
   * **WordApi 1.6** (GA since Word on the web / Windows / Mac build 2308, well below the owner's
   * "desktop 2501+" floor — verified on Microsoft Learn 2026-10-04, spaarkeai-word-add-in-r1 task
   * 094). Drives the Save tab's "Re-enable on document edits" rule (owner, 2026-10-04): once a
   * document is saved, "Saved" turns back into an enabled "Save" on the next change event.
   *
   * Word-only — no open document in Outlook, same reasoning as {@link canGetDocumentUrl}. UNLIKE
   * `canGetDocumentUrl`, this is runtime-gated: a host can satisfy this adapter's `WordApi` 1.3 init
   * floor and still lack 1.6. Views MUST gate `registerDocumentChangeHandler` on this flag, never on
   * `hostType` (NFR-10) — **and MUST NOT gray the Save button when it is `false`**: the owner's
   * binding rule is "never block a save" — without detection, the button stays an enabled "Save".
   */
  canDetectDocumentChanges: boolean;
  /**
   * Whether this platform can be offered "Open in Word" — a synthetic anchor-click launch of the
   * `ms-word:` Office URI scheme (spaarkeai-word-add-in-r1 task 094, UAT round 4 trial; owner
   * 2026-10-04: *"if this is just something to test, then fine"*). **UNSUPPORTED mechanism**: no
   * documented Office.js call can launch a non-http(s) scheme from a task pane
   * (`Office.context.ui.openBrowserWindow` only accepts http/https — OfficeDev/office-js#2820,
   * closed "by design"); this relies on undocumented `<a href="ms-word:...">.click()` behavior
   * community evidence (OfficeDev/office-js#6926) showed working on Word desktop. Nothing can detect
   * a failed launch, so "Open in browser" (`canOpenBrowserWindow`/`window.open`) is always offered
   * alongside it as the real fallback, never a retry target.
   *
   * True only on `Office.PlatformType.PC` / `Mac` (desktop Word can register the `ms-word:` protocol
   * handler; the web and mobile hosts cannot). Decided by PLATFORM, never `hostType` (NFR-10) — the
   * collision prompt's "Open in Word" button is gated on this flag.
   */
  canOpenDesktopWord: boolean;
  /** Minimum required Office.js API version */
  minApiVersion: string;
  /** Currently supported requirement set */
  supportedRequirementSet: string;
}

/**
 * The open email's identity keys (spaarkeai-word-add-in-r1 task 120), sent as-is to
 * `POST /api/documents/resolve-email-identity`. Both are needed: every save stores the RFC Message-ID on the saved
 * `.eml` since task 121, but a task-pane save made before task 121 stored the Exchange item id. The Save tab also reads
 * the Message-ID from here for the save request (task 121).
 */
export interface EmailIdentityKeys {
  /** `Office.context.mailbox.item.internetMessageId` (RFC 5322 Message-ID). */
  internetMessageId: string;
  /** `Office.context.mailbox.item.itemId` (Exchange item id), or `null` when the host gave none. */
  exchangeItemId: string | null;
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
