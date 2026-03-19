/**
 * index.ts
 * Public barrel export for the SummarizeFilesWizard components.
 */

export { SummarizeFilesDialog } from './SummarizeFilesDialog';
export { SummaryResultsStep } from './SummaryResultsStep';
export { SummaryNextStepsStep } from './SummaryNextStepsStep';
export { SummarizeSendEmailStep } from './SummarizeSendEmailStep';
export { SummarizeCreateProjectStep } from './SummarizeCreateProjectStep';
export { SummarizeAnalysisStep } from './SummarizeAnalysisStep';
export { streamSummarize, runSummarize } from './summarizeService';
export {
  buildSummaryEmailSubject,
  buildSummaryEmailBody,
} from './SummarizeSendEmailStep';
export {
  FOLLOW_ON_STEP_ID_MAP,
  FOLLOW_ON_STEP_LABEL_MAP,
  FOLLOW_ON_CANONICAL_ORDER,
} from './SummaryNextStepsStep';

export type { ISummarizeFilesDialogProps } from './SummarizeFilesDialog';
export type { ISummaryResultsStepProps } from './SummaryResultsStep';
export type { ISummaryNextStepsStepProps, SummaryActionId } from './SummaryNextStepsStep';
export type { ISummarizeSendEmailStepProps } from './SummarizeSendEmailStep';
export type { ISummarizeCreateProjectStepProps } from './SummarizeCreateProjectStep';
export type { ISummarizeAnalysisStepProps } from './SummarizeAnalysisStep';
export type { AuthenticatedFetchFn, StreamSummarizeCallbacks } from './summarizeService';
export type {
  ISummarizeResult,
  ISummarizeResponse,
  IFileHighlight,
  IMentionedParty,
  SummarizeStatus,
} from './summarizeTypes';
