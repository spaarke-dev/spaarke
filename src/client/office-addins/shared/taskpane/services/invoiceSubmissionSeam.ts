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
import type { EntityType } from '../hooks/useEntitySearch';
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
  | { kind: 'refused'; message: string }
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

/** The contract's `firmInvoiceNumber` bound: trimmed, 1-100 characters. */
export const FIRM_INVOICE_NUMBER_MAX = 100;

/**
 * One listed attachment: the marking component's {@link MarkableAttachment} with the content type and size Outlook
 * gave (always present here; the wire marks carry them).
 */
export type InvoiceListedAttachment = MarkableAttachment & { contentType: string; sizeBytes: number };

/** The marking: one role per listed attachment id (the component's `value`; it holds every non-inline id). */
export type InvoiceMarkingValue = Readonly<Record<string, InvoiceAttachmentRole>>;

/**
 * The marking list for an email: every NON-inline attachment (the contract marks those and only those), each with a
 * `disabledReason` when the add-in cannot send it, using the save's own limits (`emailContentCapture.ts`). No
 * `suggestedInvoice`: Outlook gives no hint beyond the PDF rule, which the marking component applies itself.
 * Limits:
 * - a cloud link (no file in the email);
 * - larger than {@link MAX_ATTACHMENT_BYTES} (20 MB);
 * - past the {@link EMAIL_CONTENT_BUDGET_BYTES} budget once the listed attachments before it are counted. That is
 *   the order the capture spends the budget in: the marked files first, in the email's order (the view starts with
 *   every sendable attachment marked), and only then the rest. Inline pictures are never marked, so the capture
 *   sends them last and they are NOT counted here: a PDF the capture would send is never disabled because of a
 *   picture in the email text. The email's text is not counted here; the capture at submit re-checks with it.
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
    if (attachment.attachmentType === 'cloud') {
      disabledReason = 'It is a link to a file stored in the cloud (for example OneDrive), not a file in the email.';
    } else if (attachment.size > MAX_ATTACHMENT_BYTES) {
      disabledReason = `It is larger than ${formatSize(MAX_ATTACHMENT_BYTES)}, the most one submission can carry.`;
    } else if (used + base64Length(attachment.size) > budget) {
      disabledReason =
        `It does not fit: one submission can carry about ${formatSize(Math.floor((budget * 3) / 4))} of ` +
        'attachments together.';
    } else {
      used += base64Length(attachment.size);
    }
    result.push({
      id: attachment.id,
      fileName: attachment.name,
      contentType: attachment.contentType,
      sizeBytes: attachment.size,
      ...(disabledReason ? { disabledReason } : {}),
    });
  }
  return result;
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

/** Builds the seam input. GUIDs are canonicalized (ADR-044); a blank PO number is null. */
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
  return {
    targetEntity: { entityType: args.target.entityType, entityId: cleanGuid(args.target.entityId) },
    marks: toSubmissionMarks(args.attachments, args.marking),
    firmId: cleanGuid(args.firmId),
    firmInvoiceNumber: args.firmInvoiceNumber.trim(),
    poNumber: po.length > 0 ? po : null,
    mode: args.mode,
    invoiceId: args.mode === 'Correction' && args.invoiceId ? cleanGuid(args.invoiceId) : null,
  };
}
