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
    sendMode: props.sendMode ?? 'sharedMailbox',
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
  const header =
    source.bodyFormat === 'HTML'
      ? `<p>---------- Forwarded message ----------</p><p>From: ${escapeHtml(source.from)}<br/>Sent: ${escapeHtml(sentAt)}<br/>Subject: ${escapeHtml(source.subject)}</p><hr/>`
      : `---------- Forwarded message ----------\nFrom: ${source.from}\nSent: ${sentAt}\nSubject: ${source.subject}\n\n`;
  return header + source.body;
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
export function mapStateToSendRequest(state: EmailComposerState): SendCommunicationOptions {
  return {
    to: state.to.map(r => r.email),
    cc: state.cc.length > 0 ? state.cc.map(r => r.email) : undefined,
    bcc: state.bcc.length > 0 ? state.bcc.map(r => r.email) : undefined,
    subject: state.subject,
    body: buildBodyWithAttachmentLinks(state.body, state.bodyFormat, state.attachments),
    bodyFormat: state.bodyFormat === 'HTML' ? 'html' : 'text',
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
      (state.mode === 'reply' || state.mode === 'forward') && state.communicationId
        ? state.communicationId
        : undefined,
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

  return { ok: errors.length === 0, errors };
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

    case 'REMOVE_ATTACHMENT':
      return { ...state, attachments: state.attachments.filter(a => a.id !== action.id), isDirty: true };

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
