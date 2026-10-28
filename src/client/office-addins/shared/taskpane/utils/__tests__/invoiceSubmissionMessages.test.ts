/**
 * Task 133 A1: every code pinned in ebilling's from-email contract (095-from-email-contract.md @ fd70b8e1f6, §5, §6,
 * §9, and the §2 firm refusals) has its own sentence; unknown codes get the generic one. The lists below are copied
 * from the contract, not from the implementation, so a code dropped from the catalog fails here.
 */
import {
  describeInvoiceOutcome,
  describeSubmissionRefusal,
  GENERIC_FAILED_OUTCOME_MESSAGE,
  GENERIC_OUTCOME_MESSAGE,
  GENERIC_SUBMISSION_MESSAGE,
  HELD_FOR_REVIEW_MESSAGE,
  isRetryableSubmissionRefusal,
} from '../invoiceSubmissionMessages';

const PINNED_REFUSAL_CODES = [
  // §5
  'ebilling.submission.submission_already_ended',
  'ebilling.submission.document_already_submitted',
  'OFFICE_IDEMPOTENCY_CONFLICT',
  // §6
  'OFFICE_AUTH_001',
  'OFFICE_AUTH_002',
  'sdap.access.deny.internal_callers_only',
  'sdap.access.error.system_failure',
  'sdap.access.deny.insufficient_rights',
  'OFFICE_009',
  'OFFICE_015',
  'ebilling.disabled',
  'ebilling.submission.marks_invalid',
  'ebilling.submission.marked_file_missing',
  'ebilling.submission.email_unidentifiable',
  'ebilling.submission.email_unavailable',
  'ebilling.submission.record_unavailable',
  'ebilling.submission.failed',
  'ebilling.submission.document_core_record_mismatch',
  'ebilling.submission.correction_not_allowed',
  'sdap.access.deny.record_unavailable',
  'record_owner_no_default_team', // "RecordOwnerRefused reasonCode" (the record_owner_* family)
  'record_owner_no_source',
  'OFFICE_004',
  'OFFICE_005',
  'OFFICE_006',
  'OFFICE_020',
  'OFFICE_022',
  'OFFICE_012',
  'OFFICE_INTERNAL',
  'ebilling.submission.attachment_not_preserved', // added 2026-10-28 at cc7bb5eeee (422)
  'OFFICE_AUTH_ERROR', // added 2026-10-28 at cc7bb5eeee (500, the access probe faulted)
  // §2 firm fields
  'ebilling.submission.firm_invalid',
  'ebilling.submission.firm_not_linked',
  'ebilling.submission.firm_invoice_number_required',
];

const MARKS_INVALID_REASONS = [
  'invoice_count',
  'unknown_mark',
  'inline_attachment',
  'unmarked_attachment',
  'duplicate_mark',
  'ambiguous_attachment',
  'invoice_not_pdf', // added to the contract at fd70b8e1f6 (the entry marked Invoice is not a PDF)
];
const EMAIL_UNIDENTIFIABLE_REASONS = [
  'no_message_id',
  'email_not_found',
  'earlier_save_unlinked',
  'no_communication',
  'eml_ambiguous',
];
const CORRECTION_REASONS = ['not_available', 'invoice_status', 'different_firm', 'different_core_record'];

const PINNED_OUTCOME_CODES = [
  'invoice.read.no_invoice',
  'invoice.read.unreadable',
  'invoice.read.page_limit',
  'invoice.read.service_failed',
  'ebilling.submission.email_save_failed',
  'ebilling.submission.document_unavailable',
  'ebilling.submission.file_malicious',
  'ebilling.submission.file_unservable',
  'ebilling.submission.source_check_failed',
  'ebilling.submission.processing_failed',
  'ebilling.submission.file_scan_timeout', // added to the contract at f1ae28290e (task 092; Failed, re-attempt allowed)
  'ebilling.submission.blocked',
  'ebilling.submission.returned',
];

const refusal = (code: string, extra: Record<string, string> = {}) =>
  describeSubmissionRefusal({ kind: 'refused', status: 400, code, ...extra });

describe('invoice submission messages — document_already_submitted (EB-D-165)', () => {
  it('is neutral: the earlier submission may be by the caller or by another user', () => {
    const message = refusal('ebilling.submission.document_already_submitted');
    expect(message).not.toMatch(/different details/i);
    expect(message).toMatch(/already submitted/i);
  });
});

describe('invoice submission messages — every pinned code is mapped', () => {
  it.each(PINNED_REFUSAL_CODES)('%s has its own sentence', code => {
    const message = refusal(code);
    expect(message).not.toBe(GENERIC_SUBMISSION_MESSAGE);
    expect(message.length).toBeGreaterThan(10);
  });

  it.each(MARKS_INVALID_REASONS)('marks_invalid / %s has its own sentence', reason => {
    const fallback = refusal('ebilling.submission.marks_invalid');
    expect(refusal('ebilling.submission.marks_invalid', { reason })).not.toBe(fallback);
  });

  it.each(EMAIL_UNIDENTIFIABLE_REASONS)('email_unidentifiable / %s has its own sentence', reason => {
    const fallback = refusal('ebilling.submission.email_unidentifiable');
    expect(refusal('ebilling.submission.email_unidentifiable', { reason })).not.toBe(fallback);
  });

  it.each(CORRECTION_REASONS)('correction_not_allowed / %s has its own sentence', reason => {
    const fallback = refusal('ebilling.submission.correction_not_allowed');
    expect(refusal('ebilling.submission.correction_not_allowed', { reason })).not.toBe(fallback);
  });

  it.each(PINNED_OUTCOME_CODES)('outcome %s has its own sentence', code => {
    expect(describeInvoiceOutcome(code)).not.toBe(GENERIC_OUTCOME_MESSAGE);
  });
});

describe('invoice submission messages — specifics', () => {
  it("unknown codes get the generic sentence (refusal and outcome); unknown reasons the code's own", () => {
    expect(refusal('ebilling.submission.something_new')).toBe(GENERIC_SUBMISSION_MESSAGE);
    expect(describeInvoiceOutcome('invoice.read.something_new')).toBe(GENERIC_OUTCOME_MESSAGE);
    expect(describeInvoiceOutcome(null)).toBe(GENERIC_OUTCOME_MESSAGE);
    expect(refusal('ebilling.submission.marks_invalid', { reason: 'new_reason' })).toBe(
      refusal('ebilling.submission.marks_invalid')
    );
  });

  it('invoice.read.no_invoice names the file the client marked Invoice', () => {
    expect(describeInvoiceOutcome('invoice.read.no_invoice', { invoiceFileName: 'INV-4711.pdf' })).toMatch(
      /^"INV-4711\.pdf" isn't an invoice\./
    );
    expect(describeInvoiceOutcome('invoice.read.no_invoice')).toMatch(/^The file marked as the invoice isn't/);
  });

  it('refusal extras appear: file name, mark, invoice status, Retry-After', () => {
    expect(refusal('ebilling.submission.marked_file_missing', { fileName: 'a.pdf', mark: 'Invoice' })).toMatch(
      /"a\.pdf" is marked as the invoice/
    );
    expect(
      refusal('ebilling.submission.correction_not_allowed', { reason: 'invoice_status', invoiceStatus: 'Paid' })
    ).toMatch(/while it is Paid/);
    expect(
      describeSubmissionRefusal({ kind: 'retryable', status: 429, code: 'OFFICE_015', retryAfterSeconds: 30 })
    ).toMatch(/Try again in 30 seconds\./);
  });

  it('marks_invalid / invoice_not_pdf names the file and says the invoice must be a PDF', () => {
    expect(refusal('ebilling.submission.marks_invalid', { reason: 'invoice_not_pdf', fileName: 'scan.docx' })).toMatch(
      /^"scan.docx" isn't a PDF. The invoice must be a PDF./
    );
    expect(refusal('ebilling.submission.marks_invalid', { reason: 'invoice_not_pdf' })).toMatch(
      /^The file marked as the invoice isn't a PDF./
    );
  });

  it('submission_already_ended (EB-D-160: Rejected only) says rejected and never tells the user to change and resubmit', () => {
    const message = refusal('ebilling.submission.submission_already_ended');
    expect(message).toMatch(/rejected/i);
    expect(message).toMatch(/follow it in Spaarke/i);
    expect(message).not.toMatch(/change the details|submit again|try again/i);
  });

  it('an unknown Failed code does not say "not accepted"; an unknown Rejected code does; the held sentence is neutral', () => {
    expect(describeInvoiceOutcome('invoice.read.something_new', {}, 'Failed')).toBe(GENERIC_FAILED_OUTCOME_MESSAGE);
    expect(describeInvoiceOutcome(null, {}, 'Failed')).toBe(GENERIC_FAILED_OUTCOME_MESSAGE);
    expect(describeInvoiceOutcome('x.y', {}, 'Rejected')).toBe(GENERIC_OUTCOME_MESSAGE);
    // A known code keeps its own sentence whatever the status.
    expect(describeInvoiceOutcome('ebilling.submission.blocked', {}, 'Rejected')).toBe(
      'The invoice was rejected by a billing rule.'
    );
    expect(HELD_FOR_REVIEW_MESSAGE).toMatch(/holding it for review/);
    expect(HELD_FOR_REVIEW_MESSAGE).not.toMatch(/not accepted|rejected|refused/i);
  });

  it('retry classes follow §6', () => {
    expect(isRetryableSubmissionRefusal(503, 'ebilling.submission.email_unavailable')).toBe(true);
    expect(isRetryableSubmissionRefusal(429, 'OFFICE_015')).toBe(true);
    expect(isRetryableSubmissionRefusal(403, 'sdap.access.error.system_failure')).toBe(true);
    expect(isRetryableSubmissionRefusal(500, 'OFFICE_AUTH_ERROR')).toBe(true);
    expect(isRetryableSubmissionRefusal(422, 'ebilling.submission.attachment_not_preserved')).toBe(false);
    expect(isRetryableSubmissionRefusal(409, 'OFFICE_IDEMPOTENCY_CONFLICT')).toBe(true);
    expect(isRetryableSubmissionRefusal(503, 'ebilling.disabled')).toBe(false);
    expect(isRetryableSubmissionRefusal(409, 'ebilling.submission.submission_already_ended')).toBe(false);
    expect(isRetryableSubmissionRefusal(400, 'ebilling.submission.marks_invalid')).toBe(false);
  });
});
