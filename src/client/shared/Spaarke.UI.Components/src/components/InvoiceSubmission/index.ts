/**
 * InvoiceSubmission module barrel (ebilling-r1 task 051). The shared "submit an invoice PDF" wizard and its
 * Processing / Result screen; consumed by the external SPA (051) and the internal entry point (052).
 */
export { InvoiceSubmissionWizard, DEFAULT_MAX_INVOICE_FILE_BYTES } from './InvoiceSubmissionWizard';
export type { InvoiceSubmissionWizardProps } from './InvoiceSubmissionWizard';
export { InvoiceSubmissionOutcome } from './InvoiceSubmissionOutcome';
export type { InvoiceSubmissionOutcomeProps } from './InvoiceSubmissionOutcome';
export { invoiceSubmissionErrorMessage } from './messages';
export {
  CORRECTION_NOTE_MAX_CHARS,
  correctionNoteCount,
  correctionNoteCounterText,
  correctionNoteProblem,
  correctionNoteToSend,
} from './correctionNote';
export {
  InvoiceSubmissionError,
  EBILLING_DISABLED_ERROR_CODE,
  FIRM_REQUIRED_ERROR_CODE,
  CORRECTION_NOTE_REQUIRED_ERROR_CODE,
  CORRECTION_NOT_ALLOWED_ERROR_CODE,
  CORRECTION_UNAVAILABLE_ERROR_CODE,
  isEbillingDisabledError,
  isTerminalStatus,
} from './types';
export type {
  InvoiceCoreRecordKind,
  InvoiceCoreRecordOption,
  InvoiceCoreRecordList,
  InvoiceRecordQuery,
  InvoiceFirmOption,
  InvoiceSubmissionInput,
  InvoiceSubmissionCorrectionInput,
  InvoiceCorrectionTarget,
  InvoiceSubmissionAccepted,
  InvoiceSubmissionStatusName,
  InvoiceSubmissionStatusView,
  InvoiceSubmissionWarning,
  InvoiceSubmissionCallbacks,
} from './types';
