/**
 * invoiceSubmissionSeam.ts — what the Invoice view (task 133) hands to the from-email submission, and the pure rules
 * that build it. NO I/O here.
 *
 * The client service behind {@link InvoiceSubmitHandler} is `invoiceSubmissionService.ts` (task 133 A1; contract
 * `projects/ebilling-r1/notes/095-from-email-contract.md`, `POST /api/finance/invoice-submissions/from-email`).
 * ⚠️ The contract matches a mark to an attachment by (fileName, size, contentType) AS SENT. Capture renames an
 * attached Outlook item to `<name>.eml` and sends it as `application/octet-stream` (`emailContentCapture.ts`
 * `toStoredAttachment`), so the service rewrites each mark from the captured attachment with the same `attachmentId`
 * — the values here are the ones Outlook listed, before capture.
 */

import type { AttachmentInfo } from '@shared/adapters/types';
import { cleanGuid } from '@spaarke/ui-components/guid';
import type { EntitySearchResult, EntityType } from '../hooks/useEntitySearch';
import type { DocumentIdentityState } from './documentIdentityService';
import {
  EMAIL_CONTENT_BUDGET_BYTES,
  MAX_ATTACHMENT_BYTES,
  base64Length,
  formatSize,
  type SkippedAttachment,
} from './emailContentCapture';
import type {
  InvoiceAttachmentRole,
  MarkableAttachment,
} from '@spaarke/communication-components/invoice-attachment-marking';

/** The core records an invoice is submitted against (owner decision 2026-10-21 item 5; the contract's targetEntity). */
export type InvoiceCoreRecordType = Extract<EntityType, 'Matter' | 'Project' | 'WorkAssignment'>;
export const INVOICE_CORE_RECORD_TYPES: InvoiceCoreRecordType[] = ['Matter', 'Project', 'WorkAssignment'];

/**
 * §6 (ebilling task 192): the one other core record a 409 `document_core_record_mismatch` says the email is filed under.
 * The server sends it only when the caller can Read that record and it is not secure.
 */
export interface InvoiceFiledUnder {
  entityType: InvoiceCoreRecordType;
  entityId: string;
  name: string;
}

const CORE_TYPE_BY_LOGICAL_NAME: Readonly<Record<string, InvoiceCoreRecordType>> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_workassignment: 'WorkAssignment',
};

/**
 * The core record the open email is filed to, from the pane's email identity (task 120): the same answer the Save tab's
 * "Filed to" card shows. Null when the email is not saved, its record is unknown, or it is filed to a record that is not
 * a Matter, Project or Work Assignment. The Invoice view starts on it (`InvoiceViewProps.filedRecord`).
 */
export function invoiceFiledRecord(identity: DocumentIdentityState | undefined): EntitySearchResult | null {
  if (identity === undefined || identity === 'checking' || identity.kind !== 'resolved') return null;
  const record = identity.relatedRecord;
  const entityType = record ? CORE_TYPE_BY_LOGICAL_NAME[record.entityType] : undefined;
  if (!record || !entityType) return null;
  const name = record.displayName || record.name || record.number || '';
  return {
    id: record.id,
    entityType,
    logicalName: record.entityType,
    name,
    ...(record.number && record.number !== name ? { displayInfo: record.number } : {}),
  };
}

const LOGICAL_NAME_BY_CORE_TYPE: Readonly<Record<InvoiceCoreRecordType, string>> = {
  Matter: 'sprk_matter',
  Project: 'sprk_project',
  WorkAssignment: 'sprk_workassignment',
};

/** The record a refusal's `filedUnder` names, as the core record picker holds it (the view's "Use {name}" switch). */
export function filedUnderRecord(filedUnder: InvoiceFiledUnder): EntitySearchResult {
  return {
    id: filedUnder.entityId,
    entityType: filedUnder.entityType,
    logicalName: LOGICAL_NAME_BY_CORE_TYPE[filedUnder.entityType],
    name: filedUnder.name,
  };
}

/** A mark as the contract spells it (exact case). */
export type InvoiceWireMark = 'Invoice' | 'Supporting' | 'DontSave';

const WIRE_MARK: Record<InvoiceAttachmentRole, InvoiceWireMark> = {
  invoice: 'Invoice',
  supporting: 'Supporting',
  skip: 'DontSave',
};

/** The contract's `targetEntity`. */
export interface InvoiceSubmissionTarget {
  entityType: InvoiceCoreRecordType;
  entityId: string;
}

/**
 * One contract `marks[]` entry, plus `attachmentId` (NOT on the wire) so the A1 service can rewrite fileName / size /
 * contentType from the captured attachment — see the file header.
 */
export interface InvoiceSubmissionMark {
  attachmentId: string;
  fileName: string;
  size: number;
  contentType: string;
  mark: InvoiceWireMark;
}

/** The contract body without `email` (A1 captures and adds it). */
export interface InvoiceSubmissionInput {
  targetEntity: InvoiceSubmissionTarget;
  marks: InvoiceSubmissionMark[];
  /** The firm (contract §2). Never empty: the server refuses an invoice with no firm, so the pane never sends one. */
  firmId: string;
  firmInvoiceNumber: string;
  poNumber: string | null;
  mode: 'New' | 'Correction';
  invoiceId: string | null;
}

/** One user action. A Retry of the same action reuses `actionId` (the contract's `X-Idempotency-Key` rule). */
export interface InvoiceSubmitAttempt {
  actionId: string;
  /** Called once the server has accepted the submission (202), before its final status is known: "processing". */
  onAccepted?: (info: { trackingNumber?: string }) => void;
  /** Aborted when the view goes away (unmount): the handler stops reading the submission's status. */
  signal?: AbortSignal;
}

/**
 * What became of a submission, as the view shows it. Generic on purpose: the A1 service maps the contract's codes
 * onto these five; the view never reads a code.
 * - `success`: accepted (the contract's 202);
 * - `refused`: the server said no and a retry will not help — `message` says why. Nothing was submitted;
 * - `retryable`: a transient failure — the view offers Retry (same `actionId`);
 * - `permanent`: it cannot work from Outlook (e.g. the email can't be identified) — the view suggests uploading
 *   the invoice in Spaarke instead;
 * - `ended`: the server ACCEPTED the submission (it has a tracking number) and it then ended Rejected or Failed.
 *   Not "not submitted": the view keeps the tracking number so the user can follow it in Spaarke.
 *
 * `skipped` (any kind sent after the email was read): attachments the capture left out of the saved email, each with
 * the sentence that says why — the view shows them (`emailContentCapture.ts`: "the caller shows that").
 */
export type InvoiceSubmissionOutcome = (
  | { kind: 'success'; message?: string; trackingNumber?: string }
  /** `filedUnder`: the record the email is filed under (task 192), which the view offers to switch to. */
  | { kind: 'refused'; message: string; filedUnder?: InvoiceFiledUnder }
  | { kind: 'retryable'; message: string }
  | { kind: 'permanent'; message: string }
  | { kind: 'ended'; status: 'Rejected' | 'Failed'; message: string; trackingNumber?: string }
) & { skipped?: readonly SkippedAttachment[] };

export type InvoiceSubmitHandler = (
  input: InvoiceSubmissionInput,
  attempt: InvoiceSubmitAttempt
) => Promise<InvoiceSubmissionOutcome>;

/** One firm the invoice can be from (a `sprk_organization` linked to the core record). */
export interface InvoiceFirmOption {
  firmId: string;
  name: string;
}

/** The firms linked to a core record (contract §8; `invoiceSubmissionService.loadInvoiceFirms`). */
export type LoadInvoiceFirms = (target: InvoiceSubmissionTarget) => Promise<InvoiceFirmOption[]>;

/**
 * What a correction of an invoice must keep, and whether it can be made (contract §10, ebilling 097e). A correction is
 * sent with the invoice's own firm, core record and firm invoice number; `canCorrect` is the intake's own guard.
 */
export interface InvoiceCorrectionPrefill {
  invoiceId: string;
  /** The invoice's firm, or null when it was submitted without one (§10, EB-D-185); a correction then sends no firm. */
  firm: { id: string; name: string } | null;
  coreRecord: InvoiceFiledUnder;
  /** The lifecycle status name (`InReview`, `SentToAp`, `Superseded`, ...), or null when the server doesn't know it. */
  invoiceStatus: string | null;
  trackingNumber: string | null;
  firmInvoiceNumber: string;
  canCorrect: boolean;
  /** Why not, when `canCorrect` is false: `invoice_status`, `no_submission`, `submission_in_progress` (or a newer one). */
  reason: string | null;
  /** A Superseded invoice's replacement, when the caller can open it. */
  replacedBy: { invoiceId: string; name: string } | null;
}

/**
 * The correction prefill of an invoice (`invoiceSubmissionService.loadCorrectionPrefill`). Any failure THROWS with a
 * sentence for the user, as {@link LoadInvoiceFirms} does.
 */
export type LoadCorrectionPrefill = (invoiceId: string) => Promise<InvoiceCorrectionPrefill>;

/**
 * The open email's latest invoice submission the caller can read (contract §11, ebilling 095a), as the view shows it at
 * the top: when, as what tracking number, by whom, and where it stands. `message` is already in words (the service maps
 * the outcome codes, so the view never reads one).
 */
export interface InvoicePreviousSubmission {
  submissionId: string;
  /** The §3 status URL, followed while the submission is still Received or Processing. */
  statusUrl: string;
  trackingNumber: string | null;
  status: 'Received' | 'Processing' | 'Accepted' | 'Rejected' | 'Failed';
  /** The sentence for a final status (a held or replaced Accepted, or why it Failed / was Rejected); else null. */
  message: string | null;
  /** The invoice the submission created, when it has one (offered for a correction once Accepted). */
  invoiceId: string | null;
  /** ISO 8601 (UTC), or null when the server sent none. */
  submittedOn: string | null;
  /** The submitter's name, or null when none is recorded. */
  submittedBy: string | null;
  isCorrection: boolean;
  /**
   * The firm the invoice was submitted for, when the server says (a `firm: { id, name }` on the §11 body; absent from
   * the server today, so absent is the common case). Absent or null: the banner does not name a firm.
   */
  firmName?: string | null;
}

/**
 * Reads the open email's latest submission (`invoiceSubmissionService.createPreviousSubmissionLoader`). Resolves null when
 * there is none to show (none, not readable, the email has no Message-ID, or eBilling is off); THROWS with a sentence
 * when Spaarke could not decide.
 */
export type LoadPreviousSubmission = (signal?: AbortSignal) => Promise<InvoicePreviousSubmission | null>;

/**
 * Follows a Received / Processing submission to its final status. Resolves with it updated (still Processing when the
 * answer is not known in time), or null when the view went away.
 */
export type FollowPreviousSubmission = (
  submission: InvoicePreviousSubmission,
  signal?: AbortSignal
) => Promise<InvoicePreviousSubmission | null>;

const PREVIOUS_TITLES: Record<InvoicePreviousSubmission['status'], string> = {
  Received: "This email's invoice is being processed",
  Processing: "This email's invoice is being processed",
  Accepted: "This email's invoice was already submitted",
  Rejected: "This email's invoice was rejected",
  Failed: "This email's invoice could not be processed",
};

/** The banner's title for an earlier submission. */
export function previousSubmissionTitle(submission: InvoicePreviousSubmission): string {
  return PREVIOUS_TITLES[submission.status];
}

/**
 * "Submitted on 3 Nov 2026, 10:04 as INV-012.2 for Ravenscroft and Vance LLP by Ana Diaz." — each part only when known;
 * the date in the user's own locale and time zone. `locale` is for tests.
 */
export function describeSubmittedLine(submission: InvoicePreviousSubmission, locale?: string): string {
  const when = submission.submittedOn ? new Date(submission.submittedOn) : null;
  const date =
    when && !Number.isNaN(when.getTime())
      ? when.toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short' })
      : null;
  return (
    `${submission.isCorrection ? 'A correction was submitted' : 'Submitted'}` +
    `${date ? ` on ${date}` : ''}` +
    `${submission.trackingNumber ? ` as ${submission.trackingNumber}` : ''}` +
    `${submission.firmName ? ` for ${submission.firmName}` : ''}` +
    `${submission.submittedBy ? ` by ${submission.submittedBy}` : ''}.`
  );
}

/** The contract's `firmInvoiceNumber` bound: trimmed, 1-100 characters. */
export const FIRM_INVOICE_NUMBER_MAX = 100;

/** The contract's `poNumber` bound (2026-10-28): optional, at most 100 characters after trimming. */
export const PO_NUMBER_MAX = 100;

/**
 * One listed attachment: the marking component's {@link MarkableAttachment} with the content type and size Outlook
 * gave (always present here; the wire marks carry them).
 */
export type InvoiceListedAttachment = MarkableAttachment & {
  contentType: string;
  sizeBytes: number;
  /** Set with a `disabledReason` that is only about the size budget (not a cloud link or a file over 20 MB). */
  budgetDisabled?: true;
};

const PDF_NAME = /\.pdf$/i;

/** The marking component's PDF rule (an Invoice must be a PDF): the content type says PDF, else the name ends .pdf. */
export function isPdfAttachment(a: { fileName: string; contentType?: string }): boolean {
  return /pdf/i.test(a.contentType ?? '') || PDF_NAME.test(a.fileName.trim());
}

function doesNotFitReason(budget: number): string {
  return (
    `It does not fit with the other marked files: one submission can carry about ${formatSize(Math.floor((budget * 3) / 4))} ` +
    "of attachments together. Set another file to Don't save to make room."
  );
}

/** The marking: one role per listed attachment id (the component's `value`; it holds every non-inline id). */
export type InvoiceMarkingValue = Readonly<Record<string, InvoiceAttachmentRole>>;

/**
 * The marking list for an email: every NON-inline attachment (the contract marks those and only those), each with a
 * `disabledReason` when the add-in cannot send it, using the save's own limits (`emailContentCapture.ts`). No
 * `suggestedInvoice`: Outlook gives no hint beyond the PDF rule, which the marking component applies itself.
 * Limits:
 * - a cloud link (no file in the email);
 * - larger than {@link MAX_ATTACHMENT_BYTES} (20 MB);
 * - past the {@link EMAIL_CONTENT_BUDGET_BYTES} budget once the listed attachments before it are counted. This is the
 *   STARTING list, for the view's initial marking (every sendable attachment marked, in the email's order); the
 *   budget reason is then recomputed from the live marking by {@link applyMarkingBudget}. Inline pictures are never
 *   marked, so the capture sends them last and they are NOT counted: a PDF the capture would send is never disabled
 *   because of a picture in the email text. The email's text is not counted here; the capture at submit re-checks
 *   with it.
 */
export function toInvoiceMarkingAttachments(
  attachments: readonly AttachmentInfo[],
  budget: number = EMAIL_CONTENT_BUDGET_BYTES
): InvoiceListedAttachment[] {
  const result: InvoiceListedAttachment[] = [];
  let used = 0;
  for (const attachment of attachments) {
    if (attachment.isInline) {
      continue;
    }
    let disabledReason: string | undefined;
    let budgetDisabled = false;
    if (attachment.attachmentType === 'cloud') {
      disabledReason = 'It is a link to a file stored in the cloud (for example OneDrive), not a file in the email.';
    } else if (attachment.size > MAX_ATTACHMENT_BYTES) {
      disabledReason = `It is larger than ${formatSize(MAX_ATTACHMENT_BYTES)}, the most one submission can carry.`;
    } else if (used + base64Length(attachment.size) > budget) {
      disabledReason = doesNotFitReason(budget);
      budgetDisabled = true;
    } else {
      used += base64Length(attachment.size);
    }
    result.push({
      id: attachment.id,
      fileName: attachment.name,
      contentType: attachment.contentType,
      sizeBytes: attachment.size,
      ...(disabledReason ? { disabledReason } : {}),
      ...(budgetDisabled ? { budgetDisabled: true as const } : {}),
    });
  }
  return result;
}

/** One attachment's cost against the request budget: its base64 length. */
function costOf(attachment: InvoiceListedAttachment): number {
  return base64Length(attachment.sizeBytes);
}

/** The ids the capture would leave out: files in this order, each kept only if it fits in what is left. */
function overflowOf(ordered: readonly InvoiceListedAttachment[], budget: number): Set<string> {
  const over = new Set<string>();
  let used = 0;
  for (const attachment of ordered) {
    if (used + costOf(attachment) > budget) over.add(attachment.id);
    else used += costOf(attachment);
  }
  return over;
}

/**
 * The capture's order for a marking. `captureEmailContent` spends the budget on the ticked files (here: every file
 * marked Invoice or Supporting, from `toSubmissionMarks`) in the EMAIL's order, Invoice and Supporting alike; a file
 * marked Don't save is unticked, so it is sent after these and takes no part in the budget here.
 */
function captureOrder(
  sendable: readonly InvoiceListedAttachment[],
  marking: InvoiceMarkingValue
): InvoiceListedAttachment[] {
  return sendable.filter(a => (marking[a.id] ?? 'skip') !== 'skip');
}

/**
 * The listing for the CURRENT marking: which files can be marked, and which marked files no longer fit.
 *
 * - A cloud link or a file over 20 MB stays disabled whatever the marking.
 * - A file marked Don't save is disabled for the budget only if it would not fit when marked, with the other marks as
 *   they are now, in the capture's order (the marked files in the email's order). So a file disabled only for the budget becomes markable as soon as the user
 *   sets other files to Don't save.
 * - A file that is already marked is never disabled or unmarked here. When a mark change leaves it past the budget
 *   it is returned in `overBudget`: the view says why and blocks Submit, so the user decides what to drop.
 */
export function applyMarkingBudget(
  listed: readonly InvoiceListedAttachment[],
  marking: InvoiceMarkingValue,
  budget: number = EMAIL_CONTENT_BUDGET_BYTES
): { attachments: InvoiceListedAttachment[]; overBudget: InvoiceListedAttachment[] } {
  const base = listed.map(a => {
    if (!a.budgetDisabled) return a;
    const copy = { ...a };
    delete copy.disabledReason;
    delete copy.budgetDisabled;
    return copy;
  });
  const sendable = base.filter(a => !a.disabledReason);

  const current = overflowOf(captureOrder(sendable, marking), budget);
  const overBudget = sendable.filter(a => current.has(a.id));

  const attachments = base.map(a => {
    if (a.disabledReason || (marking[a.id] ?? 'skip') !== 'skip') return a;
    // Marked as either role the file takes the same place in the capture's order, so one check covers both.
    const fits = !overflowOf(captureOrder(sendable, { ...marking, [a.id]: 'supporting' }), budget).has(a.id);
    return fits ? a : { ...a, disabledReason: doesNotFitReason(budget), budgetDisabled: true as const };
  });
  return { attachments, overBudget };
}

/** The sentence for a marked file the budget no longer holds. */
export function describeOverBudget(
  attachment: InvoiceListedAttachment,
  budget: number = EMAIL_CONTENT_BUDGET_BYTES
): string {
  return (
    `"${attachment.fileName}" doesn't fit with the other marked files: one submission can carry about ` +
    `${formatSize(Math.floor((budget * 3) / 4))} of attachments together. Set a file to Don't save to make room.`
  );
}

/** The contract's `marks[]`: one per listed (non-inline) attachment; a disabled or unmarked one is `DontSave`. */
export function toSubmissionMarks(
  attachments: readonly InvoiceListedAttachment[],
  marking: InvoiceMarkingValue
): InvoiceSubmissionMark[] {
  return attachments.map(attachment => ({
    attachmentId: attachment.id,
    fileName: attachment.fileName,
    size: attachment.sizeBytes,
    contentType: attachment.contentType,
    mark: attachment.disabledReason ? 'DontSave' : WIRE_MARK[marking[attachment.id] ?? 'skip'],
  }));
}

/** The trimmed firm invoice number, or null when it breaks the contract's 1-100 rule. */
export function normalizeFirmInvoiceNumber(raw: string): string | null {
  const trimmed = raw.trim();
  return trimmed.length >= 1 && trimmed.length <= FIRM_INVOICE_NUMBER_MAX ? trimmed : null;
}

/** Why {@link buildInvoiceSubmissionInput} refuses a blank firm (never shown: the view keeps Submit disabled first). */
export const NO_FIRM_REFUSAL = 'An invoice cannot be submitted without a firm.';

/**
 * Builds the seam input. GUIDs are canonicalized (ADR-044); a blank PO number is null. A blank firm is REFUSED (throws
 * {@link NO_FIRM_REFUSAL}): an invoice is never submitted without a firm, so the input never carries a null or empty
 * `firmId`.
 */
export function buildInvoiceSubmissionInput(args: {
  target: InvoiceSubmissionTarget;
  attachments: readonly InvoiceListedAttachment[];
  marking: InvoiceMarkingValue;
  firmId: string;
  firmInvoiceNumber: string;
  poNumber: string;
  mode: 'New' | 'Correction';
  invoiceId?: string | null;
}): InvoiceSubmissionInput {
  const po = args.poNumber.trim();
  const firmId = cleanGuid(args.firmId);
  if (!firmId) throw new Error(NO_FIRM_REFUSAL);
  return {
    targetEntity: { entityType: args.target.entityType, entityId: cleanGuid(args.target.entityId) },
    marks: toSubmissionMarks(args.attachments, args.marking),
    firmId,
    firmInvoiceNumber: args.firmInvoiceNumber.trim(),
    poNumber: po.length > 0 ? po : null,
    mode: args.mode,
    invoiceId: args.mode === 'Correction' && args.invoiceId ? cleanGuid(args.invoiceId) : null,
  };
}
