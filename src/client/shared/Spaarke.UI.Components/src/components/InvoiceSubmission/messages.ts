/**
 * User-facing text for a refused invoice submission (ebilling-r1 task 051, ADR-002 / FR-17).
 *
 * The client never decides authorization: it shows what the server said. The server's ProblemDetails `detail` wins
 * when it wrote one (it can name the configured size limit, for example). The tables below are the fallback for a
 * refusal that carried no detail, keyed by the stable `errorCode` and then by HTTP status.
 */
import { InvoiceSubmissionError } from './types';

const BY_CODE: Readonly<Record<string, string>> = {
  'ebilling.submission.file_required': 'Choose a PDF file to submit.',
  'ebilling.submission.not_pdf': 'The file is not a PDF. Upload the invoice as a PDF.',
  'ebilling.submission.too_large': 'The file is larger than the allowed size. Upload a smaller PDF.',
  'ebilling.submission.firm_invoice_number_required': 'Enter the firm invoice number.',
  'ebilling.submission.firm_invoice_number_too_long': 'The firm invoice number is too long.',
  'ebilling.submission.po_number_too_long': 'The PO number is too long.',
  'ebilling.submission.firm_forbidden': 'Your account is not allowed to submit invoices for this firm.',
  'ebilling.submission.record_forbidden': 'You do not have access to submit an invoice against this record.',
  'ebilling.submission.firm_invalid': 'The firm for this submission could not be verified.',
  'ebilling.submission.firm_required':
    'Your account is linked to more than one firm and the firm could not be chosen automatically. Contact the team to complete this submission.',
  'ebilling.submission.firm_not_linked':
    'The firm you chose is not linked to this record, so an invoice cannot be submitted for it. Choose a record that is linked to the firm.',
  'ebilling.submission.use_internal_route':
    'Invoices are submitted from the internal workspace for staff accounts. Use the internal invoice submission instead.',
  'ebilling.submission.firm_unverifiable': 'Your firm could not be verified right now. Try again shortly.',
  'ebilling.submission.storage_not_configured': 'This record has no document storage configured yet.',
  'ebilling.submission.record_unavailable': 'This record is temporarily unavailable. Try again shortly.',
  'ebilling.submission.failed': 'Nothing was saved. Try again.',
  'ebilling.tracking.unavailable': 'A tracking number could not be issued right now. Try again shortly.',
  'ebilling.disabled': 'Invoice submission is not available.',
};

const BY_STATUS: Readonly<Record<number, string>> = {
  400: 'The submission was not accepted. Check the file and the details and try again.',
  401: 'Your sign-in is no longer valid. Reload the page to sign in again.',
  403: 'You do not have access to submit an invoice here.',
  404: 'That submission was not found, or you do not have access to it.',
  409: 'The submission could not be saved because of a conflict. Nothing was changed. Try again.',
  413: 'The file is larger than the allowed size. Upload a smaller PDF.',
  422: 'This record cannot take an invoice yet.',
};

/** What a network failure or a non-ProblemDetails error says: the request may or may not have reached the server. */
const UNCONFIRMED =
  'The submission could not be confirmed. Check your invoices list before submitting again, so the invoice is not sent twice.';

/** The message the screen shows for a refused (or failed) host callback. */
export function invoiceSubmissionErrorMessage(err: unknown): string {
  if (err instanceof InvoiceSubmissionError) {
    const detail = err.detail?.trim();
    if (detail) return detail;
    if (err.errorCode && BY_CODE[err.errorCode]) return BY_CODE[err.errorCode];
    if (err.statusCode !== undefined) {
      if (BY_STATUS[err.statusCode]) return BY_STATUS[err.statusCode];
      if (err.statusCode >= 500) return 'The service is unavailable right now. Try again shortly.';
    }
    return UNCONFIRMED;
  }
  return UNCONFIRMED;
}
