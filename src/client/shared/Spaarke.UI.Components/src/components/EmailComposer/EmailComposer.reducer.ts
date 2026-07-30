/**
 * EmailComposer.reducer.ts
 *
 * Single `useReducer` state machine for the `<EmailComposer />` engine
 * (task 020 constraint: "State is a single useReducer source of truth; no
 * scattered useState for engine state"). Also hosts the pure mode-derivation
 * helpers (`deriveReplyState`/`deriveForwardState`/`deriveDraftState`) and the
 * canonical `validateState` implementing design §5.8.
 *
 * Everything in this file is pure — no I/O, no platform APIs (ADR-012).
 */
import type {
  EmailComposerAction,
  EmailComposerBodyFormat,
  EmailComposerState,
  IAttachmentItem,
  IEmailComposerProps,
  IRecipient,
  ISourceCommunicationRecord,
  IValidationResult,
  ValidationErrorCode,
} from './EmailComposer.types';
// Type-only import (keeps this module pure — no I/O): the send-request shape the
// engine maps state onto. `mapStateToSendRequest` lives here (not in the .tsx) so
// task 104's "selection → request payload" contract is unit-testable in isolation.
import type { SendCommunicationOptions } from '../../services/communicationApi';

// ---------------------------------------------------------------------------
// Attachment caps (project constraint — matches BFF enforcement + the
// CHAT-ATTACHMENT-POLICY.md analog referenced by task 020's POML)
// ---------------------------------------------------------------------------

export const ATTACHMENT_MAX_COUNT = 150;
export const ATTACHMENT_MAX_TOTAL_BYTES = 35 * 1024 * 1024; // 35 MB hard cap
export const ATTACHMENT_WARN_TOTAL_BYTES = 25 * 1024 * 1024; // 25 MB soft warning

/**
 * Per-file BINARY size cap for a locally-picked attachment (raw bytes,
 * pre-upload). Mirrors `docs/standards/CHAT-ATTACHMENT-POLICY.md` — 25 MB per
 * file, the established Spaarke binary-attachment standard (aligned with
 * DocumentUploadWizard, OfficeService, and SprkChat's `MAX_FILE_BYTES`). This
 * is the PER-FILE gate; `ATTACHMENT_MAX_TOTAL_BYTES` remains the aggregate gate.
 * Enforced CLIENT-SIDE ONLY (the policy's server gate operates on extracted
 * text, not binary — FR-20 / CHAT-ATTACHMENT-POLICY) and applies to
 * `source === 'local'` picks — governed Documents from spe/related/wizard have
 * already passed their own upload gates.
 */
export const ATTACHMENT_MAX_FILE_BYTES = 25 * 1024 * 1024;

/**
 * MIME allow-list for locally-picked attachments — the single-source-of-truth
 * set from `docs/standards/CHAT-ATTACHMENT-POLICY.md` (plain text, Markdown,
 * PDF, DOCX). Applies to `source === 'local'` picks only. A file whose `type`
 * is not in this set is rejected client-side with a VISIBLE error, never
 * silently accepted or sent (FR-20 negative acceptance criterion).
 */
export const ALLOWED_ATTACHMENT_MIME_TYPES: ReadonlySet<string> = new Set([
  'text/plain',
  'text/markdown',
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
]);

/** A rejection surfaced by {@link validateLocalAttachmentFile} — the caller MUST show `message`. */
export interface ILocalAttachmentRejection {
  code: 'ATTACHMENT_TOO_LARGE' | 'ATTACHMENT_BLOCKED_TYPE';
  message: string;
}

/**
 * Validate a locally-picked file against the CHAT-ATTACHMENT-POLICY binary cap
 * (25 MB) + MIME allow-list. Returns `null` when the file is acceptable, or a
 * rejection (code + user-visible message) the caller MUST surface. A rejected
 * file is never added to composer state and therefore never sent — this is the
 * pick-time gate behind FR-20's "rejected with a visible error, not silently
 * accepted" acceptance criterion. Pure (no I/O) so it is unit-testable and
 * reusable by the pick handler.
 */
export function validateLocalAttachmentFile(file: {
  size: number;
  type: string;
  name?: string;
}): ILocalAttachmentRejection | null {
  const label = file.name && file.name.trim() ? file.name : 'File';
  if (file.size > ATTACHMENT_MAX_FILE_BYTES) {
    return {
      code: 'ATTACHMENT_TOO_LARGE',
      message: `${label} exceeds the ${(ATTACHMENT_MAX_FILE_BYTES / (1024 * 1024)).toFixed(0)} MB per-file limit.`,
    };
  }
  if (!ALLOWED_ATTACHMENT_MIME_TYPES.has(file.type)) {
    return {
      code: 'ATTACHMENT_BLOCKED_TYPE',
      message: `${label} has an unsupported type${file.type ? ` (${file.type})` : ''}. Allowed types: TXT, Markdown, PDF, DOCX.`,
    };
  }
  return null;
}

/** Default recipient cap (matches BulkSend cap per design §5.4). */
export const DEFAULT_MAX_RECIPIENTS = 50;

/** Loose RFC 5322-adjacent email check — good enough for UI validation; the BFF is authoritative. */
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// ---------------------------------------------------------------------------
// initialState
// ---------------------------------------------------------------------------

function toRecipients(emails: string[] | undefined): IRecipient[] {
  return (emails ?? []).filter(e => e && e.trim().length > 0).map(e => ({ email: e.trim(), resolved: false }));
}

function wizardAttachments(props: IEmailComposerProps): IAttachmentItem[] {
  if (!props.wizardContext?.uploadedFiles?.length) return [];
  return props.wizardContext.uploadedFiles.map(f => ({
    id: `wizard:${f.driveItemId}`,
    source: 'wizard' as const,
    fileName: f.fileName,
    sizeBytes: f.sizeBytes,
    mimeType: f.mimeType,
    driveItemId: f.driveItemId,
    documentId: f.documentId,
    selected: true,
  }));
}

/**
 * Normalize the host-supplied `initialAttachments` (task 104) — the source
 * communication's attachments offered for inclusion on reply/replyAll/forward.
 * Applies the per-mode attach-file default when `selected` is left `undefined`:
 * forward → included (`true`), reply/replyAll → offered-but-off (`false`). The
 * body-link toggle always starts off. Hosts may still pass an explicit `selected`
 * to override the default.
 */
function initialAttachmentsFor(props: IEmailComposerProps): IAttachmentItem[] {
  const list = props.initialAttachments ?? [];
  if (list.length === 0) return [];
  const includeByDefault = props.mode === 'forward';
  return list.map(a => ({
    ...a,
    selected: a.selected ?? includeByDefault,
    linkSelected: a.linkSelected ?? false,
  }));
}

/**
 * Builds the engine's initial state from props. `compose` seeds from
 * `initial*` props (+ wizard-uploaded attachments); `view`/`reply`/`forward`/
 * `draft` derive from `props.sourceRecord` via the pure derive* helpers below.
 */
export function initialState(props: IEmailComposerProps): EmailComposerState {
  const base: EmailComposerState = {
    mode: props.mode,
    mount: props.mount,
    communicationId: props.communicationId ?? props.sourceRecord?.communicationId,
    to: toRecipients(props.initialTo),
    cc: toRecipients(props.initialCc),
    bcc: [],
    subject: props.initialSubject ?? '',
    body: props.initialBody ?? '',
    bodyFormat: props.initialBodyFormat ?? 'HTML',
    attachments: [...wizardAttachments(props), ...initialAttachmentsFor(props)],
    // `sendMode` (host-locked) wins; else `defaultSendMode` seeds an interactive default
    // (item 3 — email surface passes 'user'); else the historical 'sharedMailbox' default.
    sendMode: props.sendMode ?? props.defaultSendMode ?? 'sharedMailbox',
    fromMailbox: props.fromMailbox,
    archiveToSpe: props.archiveToSpe ?? true,
    associations: props.associations ?? [],
    isDirty: false,
    isSending: false,
    isSavingDraft: false,
    validation: { ok: true, errors: [] },
    readOnly: props.mode === 'view',
  };

  if (props.mode === 'compose' || !props.sourceRecord) {
    return base;
  }

  const patch =
    props.mode === 'reply'
      ? deriveReplyState(props.sourceRecord)
      : props.mode === 'forward'
        ? deriveForwardState(props.sourceRecord)
        : props.mode === 'draft'
          ? deriveDraftState(props.sourceRecord)
          : deriveViewState(props.sourceRecord); // 'view'

  return { ...base, ...patch, attachments: patch.attachments ?? base.attachments };
}

// ---------------------------------------------------------------------------
// Mode-derivation pure helpers (task 020 step 7)
// ---------------------------------------------------------------------------

/**
 * Prefixes `subject` with `prefix` (e.g. `'Re:'`/`'Fwd:'`) unless it is
 * already present (case-insensitive), so repeated replies don't accumulate
 * `Re: Re: Re: ...`.
 */
export function dedupSubjectPrefix(subject: string, prefix: 'Re:' | 'Fwd:'): string {
  const trimmed = (subject ?? '').trim();
  const re = new RegExp(`^${prefix.replace(':', '\\:')}\\s*`, 'i');
  return re.test(trimmed) ? trimmed : `${prefix} ${trimmed}`.trim();
}

function wrapForwardedBody(source: ISourceCommunicationRecord): string {
  const sentAt = source.sentAt ? new Date(source.sentAt).toLocaleString() : '';
  // Owner UAT 2026-07-30 (item 13): leave vertical room ABOVE the quoted thread so the
  // author can type their message before the forwarded block. HTML → two empty paragraphs
  // (the caret lands in the first); PlainText → two leading newlines.
  const lead = source.bodyFormat === 'HTML' ? '<p></p><p></p>' : '\n\n';
  const header =
    source.bodyFormat === 'HTML'
      ? `<p>---------- Forwarded message ----------</p><p>From: ${escapeHtml(source.from)}<br/>Sent: ${escapeHtml(sentAt)}<br/>Subject: ${escapeHtml(source.subject)}</p><hr/>`
      : `---------- Forwarded message ----------\nFrom: ${source.from}\nSent: ${sentAt}\nSubject: ${source.subject}\n\n`;
  return lead + header + source.body;
}

function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch] as string
  );
}

export function deriveViewState(source: ISourceCommunicationRecord): Partial<EmailComposerState> {
  return {
    to: toRecipients(source.to),
    cc: toRecipients(source.cc),
    subject: source.subject,
    body: source.body,
    bodyFormat: source.bodyFormat,
    attachments: source.attachments ?? [],
    associations: source.associations ?? [],
    readOnly: true,
  };
}

export function deriveReplyState(source: ISourceCommunicationRecord): Partial<EmailComposerState> {
  return {
    to: toRecipients([source.from]),
    cc: [],
    bcc: [],
    subject: dedupSubjectPrefix(source.subject, 'Re:'),
    body: '',
    bodyFormat: source.bodyFormat,
    // Reply OFFERS the source attachments but defaults them OFF (task 104, D1/D2) —
    // the user opts in per item (attach-file and/or body-link) via AttachmentList.
    attachments: (source.attachments ?? []).map(a => ({ ...a, selected: false, linkSelected: false })),
    associations: source.associations ?? [],
    readOnly: false,
  };
}

export function deriveForwardState(source: ISourceCommunicationRecord): Partial<EmailComposerState> {
  return {
    to: [],
    cc: [],
    bcc: [],
    subject: dedupSubjectPrefix(source.subject, 'Fwd:'),
    body: wrapForwardedBody(source),
    bodyFormat: source.bodyFormat,
    // Forward DEFAULTS to including the source attachments as files (task 104).
    attachments: (source.attachments ?? []).map(a => ({ ...a, selected: true, linkSelected: a.linkSelected ?? false })),
    associations: source.associations ?? [],
    readOnly: false,
  };
}

export function deriveDraftState(source: ISourceCommunicationRecord): Partial<EmailComposerState> {
  return {
    to: toRecipients(source.to),
    cc: toRecipients(source.cc),
    subject: source.subject,
    body: source.body,
    bodyFormat: source.bodyFormat,
    attachments: source.attachments ?? [],
    associations: source.associations ?? [],
    readOnly: false,
  };
}

/**
 * Maps engine state onto a partial `sprk_communication` update payload for a
 * future draft-persistence endpoint. NOTE: no such BFF endpoint exists yet
 * (`CommunicationEndpoints.cs` has `/send` / `/send-bulk` / `/{id}/status`
 * only — no draft PATCH). This helper shapes the payload a host-provided
 * `onSaveDraftRequest` callback can forward; `EmailComposer.tsx`'s
 * `saveDraft()` throws when no such callback is injected. Kept here (rather
 * than inline) so task 023 can unit-test the mapping shape independent of
 * the missing transport.
 */
export function mapStateToDraftUpdate(state: EmailComposerState): Record<string, unknown> {
  return {
    sprk_to: state.to.map(r => r.email).join(';'),
    sprk_cc: state.cc.map(r => r.email).join(';'),
    sprk_subject: state.subject,
    sprk_body: state.body,
    sprk_bodyformat: state.bodyFormat,
    sprk_communicationtype: 'Email',
  };
}

// ---------------------------------------------------------------------------
// Attachment inclusion → send payload (task 104, D1/D2)
// ---------------------------------------------------------------------------

/**
 * Append a "Linked documents" block to `body` for every attachment the user
 * toggled ON for the body-link path (`linkSelected === true`) that has a
 * resolvable `linkUrl`. Best-effort: an item toggled on but missing `linkUrl`
 * is silently skipped (never a hard send failure — task 104 constraint). Pure —
 * returns a new body string; the caller decides where the result goes.
 */
export function buildBodyWithAttachmentLinks(
  body: string,
  bodyFormat: EmailComposerBodyFormat,
  attachments: readonly IAttachmentItem[]
): string {
  const linked = attachments.filter(a => a.linkSelected === true && !!a.linkUrl);
  if (linked.length === 0) return body;

  if (bodyFormat === 'HTML') {
    const items = linked
      .map(a => `<li><a href="${escapeHtml(a.linkUrl as string)}">${escapeHtml(a.fileName)}</a></li>`)
      .join('');
    return `${body}<p>Linked documents:</p><ul>${items}</ul>`;
  }
  const lines = linked.map(a => `- ${a.fileName}: ${a.linkUrl}`).join('\n');
  return `${body}\n\nLinked documents:\n${lines}`;
}

/**
 * Map engine state onto the `sendCommunication()` request (task 104 moved this
 * here from EmailComposer.tsx so the payload contract is unit-testable):
 *   - attach-file path → `attachmentDocumentIds` carries the `sprk_document`
 *     GUID of every attachment that is BOTH kept (`selected !== false`) AND
 *     resolved (`documentId`). Locally-picked files without a Document yet, and
 *     reply/forward items the user deselected, are excluded.
 *   - body-link path → `buildBodyWithAttachmentLinks` appends chosen links.
 *
 * Reuses the existing `SendCommunicationRequest.AttachmentDocumentIds` field —
 * NO new send-contract attachment field is introduced (task 104 / §11).
 */
export function mapStateToSendRequest(state: EmailComposerState, threadId?: string): SendCommunicationOptions {
  return {
    to: state.to.map(r => r.email),
    cc: state.cc.length > 0 ? state.cc.map(r => r.email) : undefined,
    bcc: state.bcc.length > 0 ? state.bcc.map(r => r.email) : undefined,
    subject: state.subject,
    body: buildBodyWithAttachmentLinks(state.body, state.bodyFormat, state.attachments),
    bodyFormat: state.bodyFormat === 'HTML' ? 'html' : 'text',
    // R3 task 020 (FR-07/FR-19): pin the sent email to the active conversation
    // thread when opened from a conversation. Omitted (undefined) for every
    // existing caller → server find-or-create, unchanged. Same send path
    // (ADR-045) — not a new branch.
    threadId,
    attachmentDocumentIds: state.attachments
      .filter(a => a.selected !== false && a.documentId)
      .map(a => a.documentId as string),
    archiveToSpe: state.archiveToSpe,
    associations: state.associations,
    sendMode: state.sendMode,
    fromMailbox: state.fromMailbox,
    // Reply / Reply All / Forward inherit the parent communication's regarding associations
    // (UAT R4 D12-1 / task 124). The new draft is composed from a source communication whose id is
    // `state.communicationId`; the BFF copies that source's populated sprk_regarding* lookups onto the
    // new record at create time (true inheritance — a direct copy, NOT an engine re-derivation). Only
    // reply/forward set it (Reply All maps to the 'reply' mode); compose/draft/view do not inherit.
    inheritRegardingFromCommunicationId:
      (state.mode === 'reply' || state.mode === 'forward') && state.communicationId ? state.communicationId : undefined,
  };
}

// ---------------------------------------------------------------------------
// validateState — canonical validation contract (design §5.8)
// ---------------------------------------------------------------------------

export interface IValidateOptions {
  /**
   * `true` when validating for an actual `send()` (required-field errors
   * apply). `false` for `saveDraft()`/live-typing checks, where a draft may
   * be intentionally incomplete — only structural caps (attachment count /
   * size) still apply.
   */
  forSend: boolean;
  allowEmptyBody?: boolean;
  maxRecipients?: number;
}

export function validateState(state: EmailComposerState, options: IValidateOptions): IValidationResult {
  const errors: {
    field: 'to' | 'subject' | 'body' | 'attachments' | 'from';
    code: ValidationErrorCode;
    message: string;
  }[] = [];
  const maxRecipients = options.maxRecipients ?? DEFAULT_MAX_RECIPIENTS;

  if (state.mode !== 'view') {
    if (options.forSend) {
      if (state.to.length === 0) {
        errors.push({ field: 'to', code: 'TO_REQUIRED', message: 'At least one recipient is required.' });
      }
      if (!state.subject.trim()) {
        errors.push({ field: 'subject', code: 'SUBJECT_REQUIRED', message: 'Subject is required.' });
      }
      if (!options.allowEmptyBody && !state.body.trim()) {
        errors.push({ field: 'body', code: 'BODY_REQUIRED', message: 'Message body is required.' });
      }
    }

    const allRecipients = [...state.to, ...state.cc, ...state.bcc];
    const invalid = allRecipients.filter(r => !EMAIL_RE.test(r.email));
    if (invalid.length > 0) {
      errors.push({
        field: 'to',
        code: 'TO_INVALID_EMAIL',
        message: `Invalid email address${invalid.length > 1 ? 'es' : ''}: ${invalid.map(r => r.email).join(', ')}`,
      });
    }
    if (state.to.length > maxRecipients) {
      errors.push({ field: 'to', code: 'TO_TOO_MANY', message: `Too many recipients (max ${maxRecipients}).` });
    }
  }

  // Structural attachment caps apply regardless of forSend — they protect
  // the BFF payload size limit even for an in-progress draft.
  const includedAttachments = state.attachments.filter(a => a.selected !== false);
  if (includedAttachments.length > ATTACHMENT_MAX_COUNT) {
    errors.push({
      field: 'attachments',
      code: 'ATTACHMENTS_TOO_MANY',
      message: `Too many attachments (max ${ATTACHMENT_MAX_COUNT}).`,
    });
  }
  const totalBytes = includedAttachments.reduce((sum, a) => sum + a.sizeBytes, 0);
  if (totalBytes > ATTACHMENT_MAX_TOTAL_BYTES) {
    errors.push({
      field: 'attachments',
      code: 'ATTACHMENT_TOO_LARGE',
      message: `Total attachment size exceeds ${(ATTACHMENT_MAX_TOTAL_BYTES / (1024 * 1024)).toFixed(0)} MB.`,
    });
  }

  // Per-file binary cap + MIME allow-list for LOCALLY-picked files
  // (`source === 'local'`), per CHAT-ATTACHMENT-POLICY.md. Defense-in-depth:
  // the pick handler (AttachmentList) rejects an oversize/disallowed file at
  // pick time with a visible error, and this send-time gate guarantees such a
  // file can never be sent even if it reached state another way. Governed
  // Documents (spe/related/wizard) are exempt — they passed their own upload
  // gates and may legitimately be any type/size within the aggregate cap.
  for (const a of includedAttachments) {
    if (a.source !== 'local') continue;
    if (a.sizeBytes > ATTACHMENT_MAX_FILE_BYTES) {
      errors.push({
        field: 'attachments',
        code: 'ATTACHMENT_TOO_LARGE',
        message: `${a.fileName} exceeds the ${(ATTACHMENT_MAX_FILE_BYTES / (1024 * 1024)).toFixed(0)} MB per-file limit.`,
      });
    }
    if (a.mimeType === undefined || !ALLOWED_ATTACHMENT_MIME_TYPES.has(a.mimeType)) {
      errors.push({
        field: 'attachments',
        code: 'ATTACHMENT_BLOCKED_TYPE',
        message: `${a.fileName} has an unsupported type${a.mimeType ? ` (${a.mimeType})` : ''}.`,
      });
    }
  }

  return { ok: errors.length === 0, errors };
}

/**
 * Canonical dedup key for an association — `entityType:entityId`, lowercased,
 * with `{}` braces stripped (Dataverse GUIDs are normally brace-less; this is a
 * defensive superset of the historical ADD_ASSOCIATION dedup so a braced pick
 * and its brace-less twin collapse to one). Used by `SET_PRIMARY_ASSOCIATION`.
 */
function associationKey(a: { entityType: string; entityId: string }): string {
  return `${a.entityType}:${a.entityId.replace(/[{}]/g, '')}`.toLowerCase();
}

// ---------------------------------------------------------------------------
// emailComposerReducer
// ---------------------------------------------------------------------------

export function emailComposerReducer(state: EmailComposerState, action: EmailComposerAction): EmailComposerState {
  switch (action.type) {
    case 'SET_FIELD':
      return { ...state, [action.field]: action.value, isDirty: true };

    case 'SET_BODY_FORMAT':
      return { ...state, bodyFormat: action.value, isDirty: true };

    case 'SET_RECIPIENTS':
      return { ...state, [action.field]: action.value, isDirty: true };

    case 'SET_SEND_MODE':
      return { ...state, sendMode: action.value, isDirty: true };

    case 'SET_ARCHIVE_TO_SPE':
      return { ...state, archiveToSpe: action.value, isDirty: true };

    case 'SET_MODE':
      return { ...state, mode: action.mode, readOnly: action.mode === 'view', ...action.patch };

    case 'ADD_ATTACHMENT':
      return { ...state, attachments: [...state.attachments, action.item], isDirty: true };

    case 'ADD_ASSOCIATION': {
      // Dedup by entityType+entityId — the connector may re-pick an already-linked record.
      const key = `${action.association.entityType}:${action.association.entityId}`.toLowerCase();
      if (state.associations.some(a => `${a.entityType}:${a.entityId}`.toLowerCase() === key)) return state;
      return { ...state, associations: [...state.associations, action.association], isDirty: true };
    }

    case 'SET_PRIMARY_ASSOCIATION': {
      // Owner UAT 2026-07-30 (item 8): single-primary "Related to" model. The picked record
      // becomes index 0 — the PRIMARY regarding (the BFF's MapAssociationFields writes
      // associations[0] onto sprk_regardingrecord* at send time; this is a CLIENT-ONLY ordering
      // decision — no new send field). The OLD index-0 primary is REPLACED (dropped); any
      // SECONDARY associations (index 1+) are preserved with the picked record deduped out.
      // Key match mirrors the ADD_ASSOCIATION dedup (entityType:entityId, case-insensitive) with
      // braces stripped defensively — GUIDs are normally brace-less, so this matches identically.
      const key = associationKey(action.association);
      const secondaries = state.associations.slice(1).filter(a => associationKey(a) !== key);
      return { ...state, associations: [action.association, ...secondaries], isDirty: true };
    }

    case 'REMOVE_ASSOCIATION': {
      // Owner UAT 2026-07-30 (item 10): a parent-inherited (or manually-linked) association
      // may not actually belong on this email — the user can delete it from "Related to".
      const key = `${action.entityType}:${action.entityId}`.toLowerCase();
      return {
        ...state,
        associations: state.associations.filter(a => `${a.entityType}:${a.entityId}`.toLowerCase() !== key),
        isDirty: true,
      };
    }

    case 'REMOVE_ATTACHMENT':
      return { ...state, attachments: state.attachments.filter(a => a.id !== action.id), isDirty: true };

    case 'RESOLVE_ATTACHMENT_DOCUMENT':
      // A locally-picked file finished uploading to a governed sprk_document
      // (task 042 / FR-20). Patch the item with its resolved `documentId` (+
      // optional `driveItemId`) so it flows into the EXISTING send payload
      // (`mapStateToSendRequest` → `attachmentDocumentIds`). No new send path
      // (ADR-045) — the send engine is unchanged; this only makes the local
      // pick send-eligible.
      return {
        ...state,
        attachments: state.attachments.map(a =>
          a.id === action.id
            ? {
                ...a,
                documentId: action.documentId,
                driveItemId: action.driveItemId ?? a.driveItemId,
                // linkUrl lights up the per-row Link toggle (item 9b). Only set when the
                // host resolved one; never clobber an existing value with undefined.
                linkUrl: action.linkUrl ?? a.linkUrl,
              }
            : a
        ),
      };

    case 'TOGGLE_ATTACHMENT_SELECTED':
      return {
        ...state,
        attachments: state.attachments.map(a => (a.id === action.id ? { ...a, selected: a.selected === false } : a)),
        isDirty: true,
      };

    case 'TOGGLE_ATTACHMENT_LINK':
      return {
        ...state,
        attachments: state.attachments.map(a => (a.id === action.id ? { ...a, linkSelected: !a.linkSelected } : a)),
        isDirty: true,
      };

    case 'SET_VALIDATION_ERRORS':
      return { ...state, validation: action.result };

    case 'BEGIN_SEND':
      return { ...state, isSending: true };

    case 'END_SEND':
      return { ...state, isSending: false };

    case 'BEGIN_SAVE_DRAFT':
      return { ...state, isSavingDraft: true };

    case 'END_SAVE_DRAFT':
      return { ...state, isSavingDraft: false, isDirty: false };

    case 'RESET':
      return action.state;

    default:
      return state;
  }
}

export type { EmailComposerBodyFormat };
