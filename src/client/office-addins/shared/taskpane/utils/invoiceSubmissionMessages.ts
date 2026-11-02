/**
 * invoiceSubmissionMessages.ts — user text for ebilling's from-email invoice submission (task 133 A1).
 *
 * Every code pinned in `projects/ebilling-r1/notes/095-from-email-contract.md` (pinned at fd70b8e1f6; the 2026-10-28
 * review round at cc7bb5eeee, task/ebill-095-from-email; task 094's rows at 97c793670b, merged into work/ebilling-r1 at
 * ba63082008) §5 (replays),
 * §6 (refusals, with the `marks_invalid`, `email_unidentifiable` and `correction_not_allowed` reasons) and §9 (final
 * outcomes) has its own sentence here, in the catalog pattern of `errorMessages.ts`. No server prose is pinned (§9:
 * "the add-in localises"), so the server's `detail` is never shown in place of these. A code this file does not know
 * gets the generic sentence — the contract says clients must accept unknown codes.
 */

/** §6: the permanent "this email can't be identified" refusal (the view then offers uploading in Spaarke). */
export const EMAIL_UNIDENTIFIABLE_CODE = 'ebilling.submission.email_unidentifiable';

/**
 * §6 refusals that cannot work from Outlook, so the view also suggests uploading in Spaarke: the email can't be
 * identified, or a marked file can't be kept byte for byte in the saved email (422, 2026-10-28; its `detail` names the
 * internal upload route). Task 094 (2026-10-28): the saved `.eml` is gone, empty or deleted, no longer carries a marked
 * file as marked (422s whose `detail` names the upload route), or belongs to another communication; or the caller may
 * not file the email under the record (an upload in Spaarke does not file the email).
 */
export const PERMANENT_SUBMISSION_CODES: ReadonlySet<string> = new Set([
  EMAIL_UNIDENTIFIABLE_CODE,
  'ebilling.submission.attachment_not_preserved',
  'ebilling.submission.email_save_failed',
  'ebilling.submission.document_unavailable',
  'ebilling.submission.document_not_found',
  'ebilling.submission.source_communication_mismatch',
  'COMMUNICATION_REFILE_FORBIDDEN',
]);

/**
 * §6 "After step A" (task 094): a refusal that comes after the submission filed the communication under the record
 * keeps that filing, and its `detail` ENDS with this pinned sentence (kept word for word; ebilling is adding a top-level
 * `filingKept: true` too, which the service reads first).
 */
export const FILING_KEPT_DETAIL = 'The communication was filed under the record first; that filing is kept.';

/** What the user is told when {@link FILING_KEPT_DETAIL} is present: the email is filed even though nothing was submitted. */
export const FILING_KEPT_MESSAGE = 'The email itself was filed under this record first, and it stays filed there.';

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
  /** §6 "After step A": the server filed the email under the record before it refused, and the filing is kept. */
  filingKept?: boolean;
  /** §6 (ebilling task 192): on `document_core_record_mismatch`, the record the email is filed under, when readable. */
  filedUnder?: { name: string };
}

type Facts = InvoiceRefusalLike & InvoiceMessageContext;
type Entry = string | ((facts: Facts) => string);

export const GENERIC_SUBMISSION_MESSAGE = "The invoice couldn't be submitted. Nothing was submitted.";
export const GENERIC_OUTCOME_MESSAGE = 'The invoice was not accepted.';
/** An unknown `Failed` code: the invoice was accepted for processing and then did not get through. */
export const GENERIC_FAILED_OUTCOME_MESSAGE = "Spaarke couldn't process the invoice.";
/**
 * An Accepted invoice can be HELD for review (contract §9: `outcomeReasonCode` "while an Accepted invoice is held").
 * The catalog's sentences describe a Failed or Rejected invoice, so none of them is used for an Accepted one.
 */
export const HELD_FOR_REVIEW_MESSAGE =
  'Spaarke accepted it and is holding it for review. You can follow it in Spaarke with the tracking number.';

/**
 * §3 / §9 (ebilling 097f, EB-D-180): an Accepted submission whose invoice a correction has superseded. It is NOT a hold;
 * the status read may also carry `replacedBy.trackingNumber` (absent when the caller can't read the correcting one).
 */
export const REPLACED_BY_CORRECTION_CODE = 'ebilling.submission.replaced_by_correction';

/**
 * The sentence for an Accepted submission: none without a code, the replaced-by-correction sentence for
 * {@link REPLACED_BY_CORRECTION_CODE}, and {@link HELD_FOR_REVIEW_MESSAGE} for any other code.
 */
export function describeAcceptedOutcome(
  code: string | null | undefined,
  replacedByTrackingNumber?: string
): string | undefined {
  if (!code) return undefined;
  if (code === REPLACED_BY_CORRECTION_CODE) {
    return replacedByTrackingNumber
      ? `This invoice was replaced by a correction (tracking ${replacedByTrackingNumber}).`
      : 'This invoice was replaced by a correction.';
  }
  return HELD_FOR_REVIEW_MESSAGE;
}

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
  // The entry marked Invoice is not a PDF (the view stops this earlier; the server's own check is the backstop).
  invoice_not_pdf: f =>
    `${quoted(f.fileName ?? f.invoiceFileName, 'The file marked as the invoice')} isn't a PDF. The invoice must be a PDF. ${NOTHING}`,
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

/**
 * An invoice status as the contract spells it (§6 `invoiceStatus`, §10 `invoiceStatus`), in words. A status not listed
 * here is not named: the sentence says "in its current state". `Superseded` has its own sentence.
 */
export const INVOICE_STATUS_WORDS: Readonly<Record<string, string>> = {
  InReview: 'In Review',
  Approved: 'Approved',
  SentToAp: 'Sent to AP',
  Paid: 'Paid',
  Rejected: 'Rejected',
  ReturnedToFirm: 'Returned to Firm',
  Cancelled: 'Cancelled',
};

/** A Superseded invoice (§10, agreed 2026-10-31): the correction belongs on the newer invoice. */
export const SUPERSEDED_INVOICE_MESSAGE =
  'This invoice has already been replaced by a correction. Correct the newer invoice instead.';

/** Why an invoice can't be corrected because of its status (§6 reason `invoice_status`, §10 `reason`). */
export function describeUncorrectableStatus(invoiceStatus: string | null | undefined): string {
  if (invoiceStatus === 'Superseded') return SUPERSEDED_INVOICE_MESSAGE;
  const words = invoiceStatus ? INVOICE_STATUS_WORDS[invoiceStatus] : undefined;
  return `This invoice can't be corrected ${words ? `while it is ${words}` : 'in its current state'}.`;
}

/**
 * §6 `correction_not_allowed` reasons (097b, 2026-10-30). `not_available` (OP-7) is no longer sent; an unknown reason
 * gets the fallback.
 */
export const CORRECTION_REASON_MESSAGES: Readonly<Record<string, Entry>> = {
  invoice_status: f => `${describeUncorrectableStatus(f.invoiceStatus)} ${NOTHING}`,
  different_firm: `A correction must come from the same firm as the original invoice. ${NOTHING}`,
  different_core_record: `A correction must be for the same matter, project or work assignment as the original invoice. ${NOTHING}`,
  different_invoice_number: `A correction must keep the original invoice's number. ${NOTHING}`,
  no_submission: `This invoice has no submission in Spaarke to correct. ${NOTHING}`,
  submission_in_progress: `The original invoice is still being processed. ${NOTHING} Wait until it has finished, then submit the correction again.`,
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
  // EB-D-160: the server answers this only when the earlier submission was Rejected (after a Failed one the same
  // request is a new attempt), so it never tells the user to change the details and submit again.
  'ebilling.submission.submission_already_ended':
    'This email was already submitted with these same details, and the invoice was rejected. You can follow it in ' +
    `Spaarke. ${NOTHING}`,
  // Also another user's submission, or (task 094) other files already staged under this attempt: the caller may have
  // nothing to follow, so the sentence does not promise it.
  'ebilling.submission.document_already_submitted': `This email was already submitted as an invoice for this record. ${NOTHING}`,
  OFFICE_IDEMPOTENCY_CONFLICT: f => `This submission is still being sent. ${tryAgain(f)}`,
  // §6 access and filters
  OFFICE_AUTH_001: `Your sign-in has expired. Sign in again, then submit. ${NOTHING}`,
  OFFICE_AUTH_002: `Your sign-in has expired. Sign in again, then submit. ${NOTHING}`,
  'sdap.access.deny.internal_callers_only': `Only internal Spaarke users can submit invoices. ${NOTHING}`,
  'sdap.access.error.system_failure': f => `Spaarke couldn't check your access right now. ${NOTHING} ${tryAgain(f)}`,
  // 2026-10-28: the access probe faulted (500, retryable, carries a correlationId).
  OFFICE_AUTH_ERROR: f => `Spaarke couldn't check your access right now. ${NOTHING} ${tryAgain(f)}`,
  // 2026-10-28: also a firm id that doesn't exist (the filter's check on the firm runs before firm_invalid).
  'sdap.access.deny.insufficient_rights': `You don't have permission to submit invoices to this record or for this firm. ${NOTHING}`,
  OFFICE_009: `You don't have permission to submit invoices to this record. ${NOTHING}`,
  OFFICE_015: f => `Too many saves in a short time. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.disabled': `Invoice submission is turned off in Spaarke. ${NOTHING}`,
  // §2 firm fields
  'ebilling.submission.firm_invalid': `The chosen firm isn't an active firm in Spaarke. ${NOTHING}`,
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
  'ebilling.submission.attachment_not_preserved': `One of the marked files couldn't be saved exactly as attached. ${NOTHING}`,
  // Filed under another core record, or (task 094 step A) unfiled but held under a restricted record. Task 192: the
  // body names the record (`filedUnder`) only when it is the one readable, non-secure record the email is filed under;
  // without it the sentence is true for both cases and names no record to submit to.
  'ebilling.submission.document_core_record_mismatch': ({ filedUnder }) =>
    filedUnder
      ? `This email is filed under ${filedUnder.name}, so its invoice can only be submitted to that record. ${NOTHING}`
      : `This email belongs to a different matter, project or work assignment, so it can't be submitted to this record. ${NOTHING}`,
  'ebilling.submission.correction_not_allowed': fromReasons(CORRECTION_REASON_MESSAGES, CORRECTION_FALLBACK),
  // Correction: the uniform 404 for an invoice the caller can't Write or that doesn't exist; a transient fault of the
  // rights check gives the same answer (§6), so the sentence suggests trying again.
  'sdap.access.deny.record_unavailable': `The invoice isn't available to you: it may not exist, or you can't change it. ${NOTHING} If you can open this invoice in Spaarke, try again.`,
  // §6 task 094 (2026-10-28): step 1 reads the saved .eml and its communication, step A files an unfiled communication
  'ebilling.submission.source_forbidden': `You can't open this email's saved copy in Spaarke, so it can't be submitted. ${NOTHING}`,
  'ebilling.submission.document_not_found': `This email's saved copy is no longer in Spaarke. ${NOTHING}`,
  'ebilling.submission.source_communication_mismatch':
    "This email's saved copy in Spaarke doesn't belong to its email record, so it can't be submitted from Outlook.",
  COMMUNICATION_REFILE_FORBIDDEN: `This email isn't filed yet, and you don't have permission to file it under this record. ${NOTHING}`,
  'ebilling.submission.storage_not_configured': `Invoice file storage isn't set up in Spaarke. ${NOTHING} Ask an administrator.`,
  // ebilling task 191 (2026-11-01): checked before anything is written or filed, after the storage check.
  'ebilling.submission.reader_not_configured': `Invoice reading isn't set up in Spaarke yet. ${NOTHING} Ask an administrator.`,
  'ebilling.tracking.unavailable': f =>
    `Spaarke couldn't give the invoice a tracking number right now. ${NOTHING} ${tryAgain(f)}`,
  'ebilling.submission.email_save_failed':
    "This email's saved copy in Spaarke is missing or empty, so the invoice can't be submitted from Outlook.",
  'ebilling.submission.document_unavailable':
    "A marked file isn't in this email's saved copy as it was marked, so the invoice can't be submitted from Outlook.",
  'ebilling.submission.earlier_attempt_being_removed': f =>
    `An earlier attempt to submit this email failed and Spaarke is still clearing it away. ${NOTHING} ${tryAgain(f)}`,
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
  'ebilling.submission.file_scan_timeout':
    "The malware scan of a marked file didn't finish in time, so the invoice was not processed. Submit it again.",
  // Task 094 step 3 (2026-10-28): the saved .eml changed state before the commit.
  'ebilling.submission.document_already_invoiced':
    "This email's saved copy was linked to another invoice while this one was being processed, so it was not processed.",
  'ebilling.submission.document_rejected':
    "This email's saved copy was marked as not an invoice while it was being processed, so it was not processed.",
  'ebilling.submission.document_core_record_mismatch':
    'This email was filed under, or is held by, a different record by the time the invoice was processed, so it was not processed.',
  'ebilling.submission.document_not_found':
    "This email's saved copy was deleted from Spaarke while the invoice was being processed, so it was not processed.",
  // Task 094 (2026-10-28): a re-attempt is allowed (EB-D-160).
  'ebilling.submission.source_changed':
    "The email's saved copy in Spaarke changed while the invoice was being processed, so it was not processed. Submit it again.",
  // ebilling task 191 (2026-11-01): the AI reading and line coding. The first two are permanent; the job retries the
  // other three and stores the code on its last attempt, so a later re-submission can work.
  'ai.invoice.read.not_configured':
    "Invoice reading isn't set up in Spaarke yet, so the invoice was not processed. Ask an administrator.",
  'ai.invoice.disabled':
    'Invoice reading is turned off in Spaarke, so the invoice was not processed. Ask an administrator.',
  'ai.invoice.binding_missing':
    "Spaarke's invoice line coding isn't fully set up, so the invoice was not processed. Ask an administrator.",
  'ai.invoice.completion_failed':
    "Spaarke's AI service failed while coding the invoice lines, so the invoice was not processed. Submit it again later.",
  'ai.invoice.output_invalid':
    "Spaarke couldn't code the invoice lines, so the invoice was not processed. Submit it again later.",
  // 097c (2026-10-30): the job re-checks a correction before it replaces the original invoice. A refusal there ends the
  // submission Failed with one of these; the original invoice is unchanged, and the same request is a new attempt.
  'ebilling.submission.correction_not_allowed.invoice_status':
    "The original invoice's status changed before the correction was processed, so the correction was not processed.",
  'ebilling.submission.correction_not_allowed.different_firm':
    "The original invoice's firm changed before the correction was processed, so the correction was not processed.",
  'ebilling.submission.correction_not_allowed.different_core_record':
    "The original invoice's matter, project or work assignment changed before the correction was processed, so the " +
    'correction was not processed.',
  'ebilling.submission.correction_not_allowed.different_invoice_number':
    "The original invoice's number changed before the correction was processed, so the correction was not processed.",
  'ebilling.submission.correction_not_allowed.submission_in_progress':
    'The original invoice was still being processed, so the correction was not processed. Submit the correction again ' +
    'once it has finished.',
  'ebilling.submission.correction_target_unavailable':
    'The original invoice was no longer available when the correction was processed, so the correction was not processed.',
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
    code === 'OFFICE_AUTH_ERROR' ||
    code === 'OFFICE_IDEMPOTENCY_CONFLICT' ||
    code === 'OFFICE_012' ||
    code === 'OFFICE_015'
  ) {
    return true;
  }
  return status === 503 || status === 429 || ((status === 502 || status === 504) && code === undefined);
}

/** The sentence for a refusal or a retryable failure (§5, §6), saying when the email stays filed (§6 "After step A"). */
export function describeSubmissionRefusal(refusal: InvoiceRefusalLike, context: InvoiceMessageContext = {}): string {
  const sentence = refusalSentence(refusal, context);
  return refusal.filingKept ? `${sentence} ${FILING_KEPT_MESSAGE}` : sentence;
}

function refusalSentence(refusal: InvoiceRefusalLike, context: InvoiceMessageContext): string {
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

/**
 * The sentence for a Failed or Rejected outcome code (§9); unknown or absent codes get a generic sentence (for a
 * Failed invoice, one that does not call it "not accepted"). An ACCEPTED invoice that carries a
 * code is described by {@link HELD_FOR_REVIEW_MESSAGE}, never by this catalog.
 */
export function describeInvoiceOutcome(
  code: string | null | undefined,
  context: InvoiceMessageContext = {},
  status?: 'Rejected' | 'Failed'
): string {
  const entry = code ? INVOICE_OUTCOME_MESSAGES[code] : undefined;
  if (entry === undefined) return status === 'Failed' ? GENERIC_FAILED_OUTCOME_MESSAGE : GENERIC_OUTCOME_MESSAGE;
  return render(entry, { kind: 'refused', status: 0, code: code ?? undefined, ...context });
}
