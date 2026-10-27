/**
 * invoiceSubmissionMessages.ts — user text for ebilling's from-email invoice submission (task 133 A1).
 *
 * Every code pinned in `projects/ebilling-r1/notes/095-from-email-contract.md` (agreed at 717cf238d9) §5 (replays),
 * §6 (refusals, with the `marks_invalid`, `email_unidentifiable` and `correction_not_allowed` reasons) and §9 (final
 * outcomes) has its own sentence here, in the catalog pattern of `errorMessages.ts`. No server prose is pinned (§9:
 * "the add-in localises"), so the server's `detail` is never shown in place of these. A code this file does not know
 * gets the generic sentence — the contract says clients must accept unknown codes.
 */

/** §6: the permanent "this email can't be identified" refusal (the view then offers uploading in Spaarke). */
export const EMAIL_UNIDENTIFIABLE_CODE = 'ebilling.submission.email_unidentifiable';

/** What a sentence may name. */
export interface InvoiceMessageContext {
  /** The file the user marked Invoice (from the client's own marks; §9 sends no file name). */
  invoiceFileName?: string;
}

/** A refusal or retryable failure as the service reports it (`code` = `errorCode ?? reasonCode`). */
export interface InvoiceRefusalLike {
  kind: 'refused' | 'retryable';
  status: number;
  code: string | undefined;
  reason?: string;
  fileName?: string;
  mark?: string;
  invoiceStatus?: string;
  retryAfterSeconds?: number;
}

type Facts = InvoiceRefusalLike & InvoiceMessageContext;
type Entry = string | ((facts: Facts) => string);

export const GENERIC_SUBMISSION_MESSAGE = "The invoice couldn't be submitted. Nothing was submitted.";
export const GENERIC_OUTCOME_MESSAGE = 'The invoice was not accepted.';

const NOTHING = 'Nothing was submitted.';

function tryAgain(facts: Facts): string {
  return facts.retryAfterSeconds !== undefined && facts.retryAfterSeconds > 0
    ? `Try again in ${facts.retryAfterSeconds} seconds.`
    : 'Try again in a moment.';
}

function quoted(fileName: string | undefined, fallback: string): string {
  return fileName ? `"${fileName}"` : fallback;
}

function markName(mark: string | undefined): string {
  return mark === 'Invoice' ? 'the invoice' : mark === 'Supporting' ? 'a supporting file' : 'to be saved';
}

/** §6 `marks_invalid` reasons. */
export const MARKS_INVALID_REASON_MESSAGES: Readonly<Record<string, Entry>> = {
  invoice_count: `Exactly one file must be marked as the invoice. ${NOTHING}`,
  unknown_mark: `A file has a mark Spaarke doesn't recognise. ${NOTHING}`,
  inline_attachment: f =>
    `${quoted(f.fileName, 'A picture in the email text')} is part of the email text and can't be marked. ${NOTHING}`,
  unmarked_attachment: f => `${quoted(f.fileName, 'An attachment')} has no mark. ${NOTHING}`,
  duplicate_mark: f => `${quoted(f.fileName, 'An attachment')} was marked twice. ${NOTHING}`,
  ambiguous_attachment: f =>
    `${quoted(f.fileName, 'Two attachments')} can't be told apart: two attachments have the same name, size and ` +
    `type. Submit the invoice in Spaarke instead. ${NOTHING}`,
};

/** §6 `email_unidentifiable` reasons (permanent; the view adds the upload-in-Spaarke alternative). */
export const EMAIL_UNIDENTIFIABLE_REASON_MESSAGES: Readonly<Record<string, Entry>> = {
  no_message_id:
    "This email has no message id Spaarke can use (it may not have been sent yet), so it can't be submitted from Outlook.",
  email_not_found: "Spaarke couldn't find this email in your mailbox, so it can't be submitted from Outlook.",
  earlier_save_unlinked:
    "This email was saved to Spaarke before in a way the invoice can't use, so it can't be submitted from Outlook.",
  no_communication: "Spaarke has no record of this email, so it can't be submitted from Outlook.",
  eml_ambiguous:
    "Spaarke found more than one saved copy of this email (or none), so it can't be submitted from Outlook.",
};
const EMAIL_UNIDENTIFIABLE_FALLBACK = "Spaarke couldn't identify this email, so it can't be submitted from Outlook.";

/** §6 / OP-7 `correction_not_allowed` reasons. */
export const CORRECTION_REASON_MESSAGES: Readonly<Record<string, Entry>> = {
  not_available: `Corrections from Outlook are not available yet. ${NOTHING}`,
  invoice_status: f =>
    `This invoice can't be corrected${f.invoiceStatus ? ` while it is ${f.invoiceStatus}` : ' in its current state'}. ${NOTHING}`,
  different_firm: `A correction must come from the same firm as the original invoice. ${NOTHING}`,
  different_core_record: `A correction must be for the same matter, project or work assignment as the original invoice. ${NOTHING}`,
};
const CORRECTION_FALLBACK = `This invoice can't be corrected. ${NOTHING}`;

function fromReasons(table: Readonly<Record<string, Entry>>, fallback: string): Entry {
  return f => {
    const entry = f.reason ? table[f.reason] : undefined;
    if (entry === undefined) return fallback;
    return typeof entry === 'function' ? entry(f) : entry;
  };
}

/** §2, §5 and §6 codes. */
export const SUBMISSION_ERROR_MESSAGES: Readonly<Record<string, Entry>> = {
  // §5 replays
  'ebilling.submission.submission_already_ended':
    'This email was already submitted with these same details, and that submission has ended. Change the details ' +
    '(for example which file is the invoice) to submit again, or follow the earlier submission in Spaarke.',
  'ebilling.submission.document_already_submitted': `This invoice was already submitted from this email with different details. ${NOTHING}`,
  OFFICE_IDEMPOTENCY_CONFLICT: f => `This submission is still being sent. ${tryAgain(f)}`,
  // §6 access and filters
  OFFICE_AUTH_001: `Your sign-in has expired. Sign in again, then submit. ${NOTHING}`,
  OFFICE_AUTH_002: `Your sign-in has expired. Sign in again, then submit. ${NOTHING}`,
  'sdap.access.deny.internal_callers_only': `Only internal Spaarke users can submit invoices. ${NOTHING}`,
  'sdap.access.error.system_failure': f => `Spaarke couldn't check your access right now. ${NOTHING} ${tryAgain(f)}`,
  'sdap.access.deny.insufficient_rights': `You don't have permission to submit invoices to this record. ${NOTHING}`,
  OFFICE_009: `You don't have permission to submit invoices to this record. ${NOTHING}`,
  OFFICE_015: f => `Too many saves in a short time. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.disabled': `Invoice submission is turned off in Spaarke. ${NOTHING}`,
  // §2 firm fields
  'ebilling.submission.firm_invalid': `The chosen firm isn't a firm in Spaarke. ${NOTHING}`,
  'ebilling.submission.firm_not_linked': `The chosen firm isn't linked to this record. ${NOTHING}`,
  'ebilling.submission.firm_invoice_number_required': `Enter the firm's invoice number. ${NOTHING}`,
  // §6 handler refusals
  'ebilling.submission.marks_invalid': fromReasons(
    MARKS_INVALID_REASON_MESSAGES,
    `The marked files weren't accepted. ${NOTHING}`
  ),
  'ebilling.submission.marked_file_missing': f =>
    `${quoted(f.fileName, 'A marked file')} is marked as ${markName(f.mark)} but couldn't be sent with the email. ${NOTHING}`,
  [EMAIL_UNIDENTIFIABLE_CODE]: fromReasons(EMAIL_UNIDENTIFIABLE_REASON_MESSAGES, EMAIL_UNIDENTIFIABLE_FALLBACK),
  'ebilling.submission.email_unavailable': f =>
    `Spaarke couldn't read this email from your mailbox right now. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.submission.record_unavailable': f =>
    `Spaarke couldn't reach the record right now. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.submission.failed': f => `The submission couldn't be queued. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.submission.document_core_record_mismatch': `This email is filed under a different matter, project or work assignment. Submit the invoice to that record. ${NOTHING}`,
  'ebilling.submission.correction_not_allowed': fromReasons(CORRECTION_REASON_MESSAGES, CORRECTION_FALLBACK),
  'sdap.access.deny.record_unavailable': `The invoice isn't available to you: it may not exist, or you can't change it. ${NOTHING}`,
  // §6: passed through from the in-process save
  OFFICE_004: `An attachment is too large to save. ${NOTHING}`,
  OFFICE_005: `The attachments together are too large to save. ${NOTHING}`,
  OFFICE_006: `An attachment's file type isn't allowed. ${NOTHING}`,
  OFFICE_020: `A document with the same name already exists on this record. ${NOTHING}`,
  OFFICE_022: `No owner could be assigned to what this would create. ${NOTHING} Ask an administrator to check your business unit.`,
  OFFICE_012: f => `The email couldn't be uploaded to Spaarke. ${NOTHING} ${tryAgain(f)}`,
  OFFICE_INTERNAL: `Spaarke had a problem saving the email. ${NOTHING}`,
};

/** §6 "RecordOwnerRefused reasonCode": the `record_owner_*` family (ProblemDetailsHelper.RecordOwnerRefused). */
const RECORD_OWNER_PREFIX = 'record_owner_';
const RECORD_OWNER_MESSAGE = `No owner could be assigned to the new invoice records. ${NOTHING} Ask an administrator to check the record's business unit.`;

/** §9 `outcomeReasonCode` values. */
export const INVOICE_OUTCOME_MESSAGES: Readonly<Record<string, Entry>> = {
  'invoice.read.no_invoice': f =>
    `${quoted(f.invoiceFileName, 'The file marked as the invoice')} isn't an invoice. Mark the file that is the ` +
    'invoice and submit again.',
  'invoice.read.unreadable': f =>
    `${quoted(f.invoiceFileName, 'The invoice file')} couldn't be read: it may be damaged or password-protected.`,
  'invoice.read.page_limit': f => `${quoted(f.invoiceFileName, 'The invoice file')} has too many pages to be read.`,
  'invoice.read.service_failed': "Spaarke couldn't read the invoice. Try again later.",
  'ebilling.submission.email_save_failed': "The email couldn't be saved to Spaarke, so the invoice wasn't processed.",
  'ebilling.submission.document_unavailable': "The marked files couldn't be read from the saved email.",
  'ebilling.submission.file_malicious': 'A marked file failed the malware scan, so the invoice was not processed.',
  'ebilling.submission.file_unservable': "A marked file couldn't be stored, so the invoice was not processed.",
  'ebilling.submission.source_check_failed':
    "The email's filing couldn't be confirmed, so the invoice was not processed.",
  'ebilling.submission.processing_failed': "The invoice couldn't be processed.",
  'ebilling.submission.blocked': 'The invoice was rejected by a billing rule.',
  'ebilling.submission.returned': 'The invoice was returned to the firm.',
};

function render(entry: Entry, facts: Facts): string {
  return typeof entry === 'function' ? entry(facts) : entry;
}

/** Whether a refusal is transient: retry the SAME action (same key). §6 "Retry" column. */
export function isRetryableSubmissionRefusal(status: number, code: string | undefined): boolean {
  if (code === 'ebilling.disabled') return false;
  if (
    code === 'sdap.access.error.system_failure' ||
    code === 'OFFICE_IDEMPOTENCY_CONFLICT' ||
    code === 'OFFICE_012' ||
    code === 'OFFICE_015'
  ) {
    return true;
  }
  return status === 503 || status === 429 || ((status === 502 || status === 504) && code === undefined);
}

/** The sentence for a refusal or a retryable failure (§5, §6). */
export function describeSubmissionRefusal(refusal: InvoiceRefusalLike, context: InvoiceMessageContext = {}): string {
  const facts: Facts = { ...refusal, ...context };
  const entry = refusal.code ? SUBMISSION_ERROR_MESSAGES[refusal.code] : undefined;
  if (entry !== undefined) return render(entry, facts);
  if (refusal.code?.startsWith(RECORD_OWNER_PREFIX)) return RECORD_OWNER_MESSAGE;
  if (refusal.code === undefined) {
    if (refusal.status === 0) {
      return refusal.kind === 'retryable'
        ? `Couldn't reach Spaarke. ${NOTHING} Check your connection and try again.`
        : GENERIC_SUBMISSION_MESSAGE;
    }
    if (refusal.status === 401) return `Your sign-in has expired. Sign in again, then submit. ${NOTHING}`;
    if (refusal.status === 400) return `Spaarke didn't accept the submission's details. ${NOTHING}`;
    if (refusal.status === 413) return `This email and its attachments are too large to submit together. ${NOTHING}`;
  }
  return refusal.kind === 'retryable'
    ? `Spaarke couldn't take the submission right now. ${NOTHING} ${tryAgain(facts)}`
    : GENERIC_SUBMISSION_MESSAGE;
}

/** The sentence for a final outcome code (§9); unknown or absent codes get the generic sentence. */
export function describeInvoiceOutcome(code: string | null | undefined, context: InvoiceMessageContext = {}): string {
  const entry = code ? INVOICE_OUTCOME_MESSAGES[code] : undefined;
  return entry === undefined
    ? GENERIC_OUTCOME_MESSAGE
    : render(entry, { kind: 'refused', status: 0, code: code ?? undefined, ...context });
}
