/**
 * index.ts
 * Public barrel export for the SummarizeFilesWizard components.
 *
 * NOTE: The follow-on card grid + Send Email step now live in the shared
 * WizardFollowOns module (FollowOnGrid + SendEmailFollowOnStep) — import those
 * from '@spaarke/ui-components' directly rather than from here.
 * AuthenticatedFetchFn is NOT re-exported — it is already exported from services.
 */

export { SummarizeFilesDialog } from './SummarizeFilesDialog';
export { SummaryResultsStep } from './SummaryResultsStep';
export { SummarizeCreateProjectStep } from './SummarizeCreateProjectStep';
export { SummarizeAnalysisStep } from './SummarizeAnalysisStep';
export { streamSummarize } from './summarizeService';
// Next Steps + Send Email are now the shared, config-driven WizardFollowOns
// module (FollowOnGrid + SendEmailFollowOnStep) — the local SummaryNextStepsStep
// + SummarizeSendEmailStep copies were deleted (design.md §5.9, task 024). The
// summary-specific email template builders + the follow-on id union moved onto
// the dialog module.
export { buildSummaryEmailSubject, buildSummaryEmailBody } from './SummarizeFilesDialog';

export type { ISummarizeFilesDialogProps } from './SummarizeFilesDialog';
export type { SummaryActionId } from './SummarizeFilesDialog';
export type { ISummaryResultsStepProps } from './SummaryResultsStep';
export type { ISummarizeCreateProjectStepProps } from './SummarizeCreateProjectStep';
export type { ISummarizeAnalysisStepProps, ICreateDocumentResult } from './SummarizeAnalysisStep';
export type { StreamSummarizeCallbacks } from './summarizeService';
export type {
  ISummarizeResult,
  ISummarizeResponse,
  IFileHighlight,
  IMentionedParty,
  SummarizeStatus,
} from './summarizeTypes';
