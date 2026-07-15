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
   * Forward-mode inclusion checkbox. `undefined`/`true` = included;
   * `false` = user deselected. Only meaningful when `mode === 'forward'`.
   */
  selected?: boolean;
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
  /** Originating `ILookupItem.id` when `resolved === true` (audit/debug only). */
  sourceId?: string;
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
  | { type: 'TOGGLE_ATTACHMENT_SELECTED'; id: string }
  | { type: 'SET_VALIDATION_ERRORS'; result: IValidationResult }
  | { type: 'BEGIN_SEND' }
  | { type: 'END_SEND' }
  | { type: 'BEGIN_SAVE_DRAFT' }
  | { type: 'END_SAVE_DRAFT' }
  | { type: 'RESET'; state: EmailComposerState };

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

  // — Attachment sources —
  /** Default `['wizard','related','local','spe']` when `wizardContext` present, else `['local','related','spe']`. */
  attachmentSources?: IComposerAttachmentSource[];
  wizardContext?: IWizardContext;

  // — Recipient directory lookup (RecipientField) —
  /** Mirrors `searchUsersAndContacts(dataService, query)` shape, pre-bound by the host. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;

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
