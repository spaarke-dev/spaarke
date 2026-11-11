/**
 * index.ts
 * Public barrel export for the CreateInvoiceWizard shared library component.
 *
 * The wizard is the shared InvoiceSubmission flow mounted on the internal submission routes (ebilling-r1 task 052). It creates no
 * Dataverse record itself.
 *
 * Consumer usage:
 *   import { CreateInvoiceWizard } from './components/CreateInvoiceWizard';
 */

// Primary entry point -- the wizard component
export { CreateInvoiceWizard, default } from './CreateInvoiceWizard';
export type { ICreateInvoiceWizardProps } from './CreateInvoiceWizard';

// Findings summary and the Override / Return to firm decision of a held submission
export { BlockedSubmissionPanel } from './BlockedSubmissionPanel';
export type { BlockedSubmissionPanelProps } from './BlockedSubmissionPanel';

// Adapter onto the internal submission and blocked-submission routes
export {
  createInternalInvoiceSubmission,
  resolveInitialInvoiceRecord,
  isBlockedStatus,
  blockedDecisionErrorMessage,
  BLOCKED_REASON_CODE,
  RETURNED_REASON_CODE,
  OVERRIDDEN_REASON_CODE,
  NO_FIRM_LINKED_ERROR_CODE,
  MAX_DECISION_REASON_LENGTH,
} from './internalInvoiceSubmission';
export type {
  InternalInvoiceSubmission,
  InternalInvoiceSubmissionOptions,
  IInitialInvoiceAssociation,
  BlockedSubmissionView,
  BlockedSubmissionState,
  BlockedFinding,
  BlockedDecisionVerb,
  BlockedDecisionResponse,
  BlockedDecisionStamp,
} from './internalInvoiceSubmission';
