/**
 * EmailComposer.types.ts
 *
 * Canonical type contract for the `<EmailComposer />` engine (task 020, FR-12).
 * This is THE single source of truth for email-send composition state — see
 * `projects/email-communication-solution-r4/reference/r3-send-side-design.md`
 * §5.4 (props contract), §5.6 (sub-components), §5.8 (validation contract).
 *
 * Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework` references. All
 * platform I/O is injected via props (`authenticatedFetch`, `onSearchRecipients`,
 * `sourceRecord`, `onSaveDraftRequest`).
 *
 * No `@spaarke/auth` import (ADR-028) — `authenticatedFetch` is injected and
 * forwarded into `sendCommunication()`.
 */
import type * as React from 'react';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type {
  ICommunicationAssociation,
  CommunicationSendMode,
  SendCommunicationError,
} from '../../services/communicationApi';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Mode / mount / format
// ---------------------------------------------------------------------------

/** The five composer modes (design §5.2). */
export type EmailComposerMode = 'compose' | 'view' | 'reply' | 'forward' | 'draft';

/** The three mount shapes (design §5.3). */
export type EmailComposerMount = 'inline' | 'dialog' | 'page';

/** Body authoring format. Matches the BFF `BodyFormat` enum spelling. */
export type EmailComposerBodyFormat = 'HTML' | 'PlainText';

/** Where an attachment originated. Drives which `AttachmentList` section it renders in. */
export type EmailAttachmentSourceKind = 'local' | 'spe' | 'related' | 'wizard';

// ---------------------------------------------------------------------------
// Attachment source configuration + items
// ---------------------------------------------------------------------------

/**
 * Declares one attachment source section for `AttachmentList` to render.
 * `config` is source-specific (e.g. `{ relatedEntityType, relatedEntityId }`
 * for `'related'`); the engine passes it through unopened to the source's
 * picker UI — it does not interpret `config` itself.
 */
export interface IComposerAttachmentSource {
  kind: EmailAttachmentSourceKind;
  config?: Record<string, unknown>;
}

/**
 * A single attachment tracked by engine state, regardless of source.
 *
 * `documentId` (a `sprk_document` GUID) is what `sendCommunication()`'s
 * `attachmentDocumentIds` field actually expects — email attachments are
 * always tracked Documents; the server resolves each to its backing SPE
 * File (see communicationApi.ts file-level note; R4 W0 owner decision,
 * 2026-07-14 — corrects the earlier R3 "these are raw driveItem ids"
 * misread). `driveItemId` is carried as supplementary SPE metadata (e.g.
 * useful for a future preview/download affordance) but is NOT what gets
 * sent. Items without a resolved `documentId` (e.g. a freshly-picked local
 * `File` awaiting upload-to-Document) are tracked for display/cap purposes
 * but excluded from the outbound send payload until resolved — see
 * `EmailComposer.tsx` `mapStateToSendRequest`.
 */
export interface IAttachmentItem {
  /** Stable client-side key (not necessarily a Dataverse id). */
  id: string;
  source: EmailAttachmentSourceKind;
  fileName: string;
  sizeBytes: number;
  mimeType?: string;
  /** `sprk_document` GUID — required for the item to be included in the send payload. */
  documentId?: string;
  /** Optional SPE driveItem id, when known (supplementary metadata only — not sent). */
  driveItemId?: string;
  /** Raw browser File — present only for freshly-picked 'local' items pre-upload. */
  file?: File;
  /**
   * Attach-file inclusion checkbox. `undefined`/`true` = included as an attached
   * FILE (its `documentId` flows into `SendCommunicationRequest.AttachmentDocumentIds`);
   * `false` = user deselected. Meaningful on `forward` AND `reply`/`replyAll`
   * (task 104): forward defaults these to `true`, replies default them to `false`.
   */
  selected?: boolean;
  /**
   * Body-link inclusion checkbox (task 104, D1/D2). `true` = insert a hyperlink to
   * this document into the outgoing message body at send time (see
   * `buildBodyWithAttachmentLinks`). Only offered when `linkUrl` is resolvable;
   * an item toggled on but missing `linkUrl` is skipped best-effort (never a hard
   * send failure).
   */
  linkSelected?: boolean;
  /**
   * Optional host-resolved URL to the document, used by the body-link path. The
   * engine is context-agnostic (ADR-012) and never builds this itself — the host
   * (e.g. the CommunicationActions PCF) supplies it (a Dataverse record deep-link
   * to the `sprk_document`). Absent → the "link" toggle is not offered for the item.
   */
  linkUrl?: string;
}

/**
 * One entity target in the composer's record-lookup menu (owner UAT round 5) — the
 * RegardingResolver pattern: a search icon opens a menu of these, and choosing one runs
 * the host's `onLookupRecord`.
 */
export interface IRecordLookupTarget {
  /** Entity logical name, e.g. `sprk_matter`, `sprk_document`, `contact`. */
  logicalName: string;
  /** User-facing label shown in the lookup menu. */
  displayName: string;
}

/**
 * A record the user picked via the host's `onLookupRecord` (RegardingResolver-style
 * `Xrm.Utility.lookupObjects`). A `sprk_document` pick is added to the attachments
 * (Attach/Link per row); any OTHER record type is inserted as a link in the body.
 */
export interface IPickedRecord {
  entityType: string;
  id: string;
  name: string;
  /** Host-built record URL (deep-link) — used for the body link / the document Link option. */
  url?: string;
  /** File size in bytes when the pick is a document (optional). */
  sizeBytes?: number;
}

/** Files a hosting wizard has already uploaded, offered as a pre-checked attachment source. */
export interface IWizardContext {
  uploadedFiles: {
    documentId: string;
    driveItemId: string;
    fileName: string;
    mimeType: string;
    sizeBytes: number;
  }[];
}

// ---------------------------------------------------------------------------
// Recipients
// ---------------------------------------------------------------------------

/**
 * A single To/Cc/Bcc recipient. `resolved: true` means the entry came from
 * `onSearchRecipients` (directory match); `false`/`undefined` means free-text
 * (validated by regex on commit) — see `RecipientField` (§5.6.1).
 */
export interface IRecipient {
  email: string;
  displayName?: string;
  resolved?: boolean;
  /**
   * Originating `ILookupItem.id` when `resolved === true`. Together with
   * {@link entityType}, this identifies the resolved `contact`/`systemuser`
   * record (task 060, FR-10) — previously "audit/debug only"; now also
   * consumed by callers (e.g. `TimelineComposeBox`) that want to know a
   * recipient resolved to a typed Dataverse identity rather than free text.
   */
  sourceId?: string;
  /**
   * Dataverse logical name of the resolved record (`contact`/`systemuser`),
   * mirrored from `ILookupItem.entityType` when `resolved === true` (task 060,
   * FR-10). Undefined for free-text recipients. Additive/optional — existing
   * callers that never set it are unaffected.
   */
  entityType?: 'contact' | 'systemuser';
}

// ---------------------------------------------------------------------------
// Validation contract (design §5.8)
// ---------------------------------------------------------------------------

/**
 * Canonical validation codes — the ONE set used everywhere. Never duplicate
 * these as ad-hoc strings per caller (constraint: "project" in task 020 POML).
 *
 * `FROM_REQUIRED` / `FROM_NOT_APPROVED` are BFF-authoritative: the client has
 * no reliable signal to raise them from `validate()` (sender identity /
 * approved-sender allow-list are server-side concepts), so these two codes
 * are reserved for mapping `SendCommunicationError.code` onto the same
 * canonical taxonomy rather than being raised locally. See `EmailComposer.reducer.ts`.
 */
export type ValidationErrorCode =
  | 'TO_REQUIRED'
  | 'TO_INVALID_EMAIL'
  | 'TO_TOO_MANY'
  | 'SUBJECT_REQUIRED'
  | 'BODY_REQUIRED'
  | 'ATTACHMENT_TOO_LARGE'
  | 'ATTACHMENTS_TOO_MANY'
  | 'ATTACHMENT_BLOCKED_TYPE'
  | 'FROM_REQUIRED'
  | 'FROM_NOT_APPROVED';

export interface IValidationError {
  field: 'to' | 'subject' | 'body' | 'attachments' | 'from';
  code: ValidationErrorCode;
  message: string;
}

export interface IValidationResult {
  ok: boolean;
  errors: IValidationError[];
}

// ---------------------------------------------------------------------------
// Source record (view / reply / forward / draft pre-fill)
// ---------------------------------------------------------------------------

/**
 * The existing `sprk_communication` record backing `view`/`reply`/`forward`/
 * `draft` modes. Context-agnostic per ADR-012: the ENGINE never fetches this
 * itself (no `Xrm.WebApi` / `IDataService` in its prop surface) — the host
 * (Code Page, task 041) reads the record and passes it in. `deriveReplyState`
 * / `deriveForwardState` / `deriveDraftState` (EmailComposer.reducer.ts) are
 * pure functions over this shape.
 */
export interface ISourceCommunicationRecord {
  communicationId: string;
  from: string;
  to: string[];
  cc?: string[];
  subject: string;
  body: string;
  bodyFormat: EmailComposerBodyFormat;
  sentAt?: string;
  attachments?: IAttachmentItem[];
  associations?: ICommunicationAssociation[];
}

// ---------------------------------------------------------------------------
// Engine state + actions
// ---------------------------------------------------------------------------

export interface EmailComposerState {
  mode: EmailComposerMode;
  mount: EmailComposerMount;
  communicationId?: string;
  to: IRecipient[];
  cc: IRecipient[];
  bcc: IRecipient[];
  subject: string;
  body: string;
  bodyFormat: EmailComposerBodyFormat;
  attachments: IAttachmentItem[];
  sendMode: CommunicationSendMode;
  fromMailbox?: string;
  archiveToSpe: boolean;
  associations: ICommunicationAssociation[];
  isDirty: boolean;
  isSending: boolean;
  isSavingDraft: boolean;
  validation: IValidationResult;
  /** True for `mode === 'view'` — sub-components render read-only. */
  readOnly: boolean;
}

export type EmailComposerAction =
  | { type: 'SET_FIELD'; field: 'subject' | 'body' | 'fromMailbox'; value: string }
  | { type: 'SET_BODY_FORMAT'; value: EmailComposerBodyFormat }
  | { type: 'SET_RECIPIENTS'; field: 'to' | 'cc' | 'bcc'; value: IRecipient[] }
  | { type: 'SET_SEND_MODE'; value: CommunicationSendMode }
  | { type: 'SET_ARCHIVE_TO_SPE'; value: boolean }
  | { type: 'SET_MODE'; mode: EmailComposerMode; patch?: Partial<EmailComposerState> }
  | { type: 'ADD_ATTACHMENT'; item: IAttachmentItem }
  | { type: 'REMOVE_ATTACHMENT'; id: string }
  | { type: 'RESOLVE_ATTACHMENT_DOCUMENT'; id: string; documentId: string; driveItemId?: string }
  | { type: 'TOGGLE_ATTACHMENT_SELECTED'; id: string }
  | { type: 'TOGGLE_ATTACHMENT_LINK'; id: string }
  | { type: 'SET_VALIDATION_ERRORS'; result: IValidationResult }
  | { type: 'BEGIN_SEND' }
  | { type: 'END_SEND' }
  | { type: 'BEGIN_SAVE_DRAFT' }
  | { type: 'END_SAVE_DRAFT' }
  | { type: 'RESET'; state: EmailComposerState };

// ---------------------------------------------------------------------------
// Conversation context (R3 task 020, FR-07) — additive, optional
// ---------------------------------------------------------------------------

/** A lightweight, clickable record reference rendered in the composer (FR-07). */
export interface IComposerRecordLink {
  /** Absolute or app-relative URL to the record. */
  url: string;
  /** Human label (e.g. "Matter: Smith v. Jones"). */
  label: string;
}

// ---------------------------------------------------------------------------
// Props contract (design §5.4)
// ---------------------------------------------------------------------------

export interface IEmailComposerProps {
  // — Mode & mount —
  mode: EmailComposerMode;
  mount: EmailComposerMount;

  // — Auth (injected per shared-lib decoupling rule, ADR-028) —
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded to `sendCommunication()`'s `bffBaseUrl`. */
  bffBaseUrl?: string;

  // — Source data (required for view/reply/forward/draft; see ISourceCommunicationRecord) —
  communicationId?: string;
  sourceRecord?: ISourceCommunicationRecord;

  // — Pre-fill (compose mode) —
  initialTo?: string[];
  initialCc?: string[];
  initialSubject?: string;
  initialBody?: string;
  initialBodyFormat?: EmailComposerBodyFormat;

  // — Associations —
  associations?: ICommunicationAssociation[];
  /** Default `true` — renders `associations[]` as read-only Fluent Tags via `AssociationChips`. */
  showAssociations?: boolean;

  // — Conversation context (R3 task 020, FR-07/FR-19) — all optional/additive —
  /**
   * Active conversation thread id. When set, it is carried into the send
   * payload (`SendCommunicationOptions.threadId`) so the backend pins the sent
   * email to this thread (FR-19) — via the EXISTING send path, NOT a new send
   * branch (ADR-045). Undefined = today's behavior (server find-or-create).
   */
  threadId?: string;
  /**
   * Optional record-link affordance rendered below the linked-record chips
   * (FR-07) — a lightweight "this email is about <record>" link. Purely
   * presentational; auto-association + thread pinning are handled by
   * `associations` (ADR-024 mechanism) + `threadId`, not by this link.
   * Unsafe-scheme urls (`javascript:`/`data:`/`vbscript:`) degrade to a
   * non-clickable label.
   */
  recordLink?: IComposerRecordLink;

  // — Attachment sources —
  /** Default `['wizard','related','local','spe']` when `wizardContext` present, else `['local','related','spe']`. */
  attachmentSources?: IComposerAttachmentSource[];
  wizardContext?: IWizardContext;
  /**
   * Source-communication attachments offered for inclusion on reply/replyAll/forward
   * (task 104, D1/D2). Used by hosts that seed via the `initial*` props rather than
   * `sourceRecord` (e.g. the CommunicationActions PCF, which derives Re:/Fwd: fields
   * itself). Each item's attach-file default is applied per mode when `selected` is
   * left `undefined`: forward → included, reply/replyAll → offered-but-off. Ignored
   * when `sourceRecord` is supplied (that path derives attachments from the record).
   */
  initialAttachments?: IAttachmentItem[];

  /**
   * Resolve a locally-picked file to a governed `sprk_document` (task 042 /
   * FR-20 attach-on-compose). The engine is context-agnostic (ADR-012) and
   * owns no upload transport — the HOST injects this, wired to its EXISTING
   * Document-upload surface (e.g. `POST /api/documents`); it is NOT a new send
   * or upload path in the send engine (ADR-045). When provided, a local pick
   * that passes the CHAT-ATTACHMENT-POLICY 25 MB + MIME gate is uploaded and
   * the returned `documentId` is patched onto the attachment so it flows into
   * the existing `sendCommunication()` `attachmentDocumentIds`. When OMITTED,
   * local picks remain display-only (the pre-task-042 behavior — back-compat)
   * and are excluded from the send payload until resolved. Rejection (throw)
   * removes the item and surfaces an inline error; it never blocks the send of
   * the already-resolved attachments.
   */
  onUploadLocalAttachment?: (file: File) => Promise<{ documentId: string; driveItemId?: string }>;

  // — Recipient directory lookup (RecipientField) —
  /** Mirrors `searchUsersAndContacts(dataService, query)` shape, pre-bound by the host. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;

  /**
   * Record-lookup targets for the attachments "look up a record" tool (owner UAT round 5).
   * When supplied with {@link onLookupRecord}, a search icon opens a menu of these entity
   * types (RegardingResolver pattern). A `sprk_document` pick attaches (Attach/Link); any
   * other type inserts a link to the record in the message body.
   */
  recordLookupCatalog?: IRecordLookupTarget[];
  /**
   * Runs the host lookup for the chosen entity type (RegardingResolver-style
   * `Xrm.Utility.lookupObjects`) and returns the picked record (or null if cancelled).
   * Context-agnostic (ADR-012): the host owns the Xrm bridge + the record URL.
   */
  onLookupRecord?: (entityType: string) => Promise<IPickedRecord | null>;

  // — Send-side behavior —
  sendMode?: CommunicationSendMode;
  fromMailbox?: string;
  /** Archive sent `.eml` to SPE. Default `true`. */
  archiveToSpe?: boolean;

  // — Validation & feature gates —
  allowEmptyBody?: boolean;
  /** Default 50 (matches BulkSend cap). */
  maxRecipients?: number;

  // — Draft persistence (no BFF draft endpoint exists yet — see EmailComposer.tsx `saveDraft`) —
  onSaveDraftRequest?: (state: EmailComposerState) => Promise<{ communicationId: string }>;

  // — Callbacks (inline mount) —
  onStateChange?: (state: EmailComposerState) => void;

  // — Callbacks (dialog/page mount) —
  onSent?: (result: { communicationId: string }) => void;
  onCancel?: () => void;
  onError?: (err: SendCommunicationError) => void;
  onSaveDraft?: (result: { communicationId: string }) => void;

  // — View-mode navigation callbacks (host handles the actual mode switch —
  //   e.g. Code Page re-navigates with `?mode=reply&id=...` per design §7.5;
  //   the engine only surfaces the button clicks) —
  onEdit?: () => void;
  onReply?: () => void;
  onForward?: () => void;
  /** View mode only: whether the underlying record's statuscode is Draft (enables the Edit button). */
  isDraftRecord?: boolean;

  /** Optional className applied to the root layout container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Imperative handle
// ---------------------------------------------------------------------------

export interface IEmailComposerHandle {
  /** Returns errors; does not throw. */
  validate(): IValidationResult;
  send(): Promise<{ communicationId: string }>;
  saveDraft(): Promise<{ communicationId: string }>;
  getState(): EmailComposerState;
}

/** Ref type alias used by wrapper components (task 021). */
export type EmailComposerRef = React.Ref<IEmailComposerHandle>;
