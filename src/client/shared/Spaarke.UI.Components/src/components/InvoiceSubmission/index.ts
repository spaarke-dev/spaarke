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
  InvoiceSubmissionError,
  EBILLING_DISABLED_ERROR_CODE,
  FIRM_REQUIRED_ERROR_CODE,
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
  InvoiceSubmissionAccepted,
  InvoiceSubmissionStatusName,
  InvoiceSubmissionStatusView,
  InvoiceSubmissionCallbacks,
} from './types';
