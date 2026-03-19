/**
 * index.ts
 * Public barrel export for the SummarizeFilesWizard components.
 *
 * NOTE: FOLLOW_ON_STEP_ID_MAP / FOLLOW_ON_STEP_LABEL_MAP / FOLLOW_ON_CANONICAL_ORDER
 * are NOT re-exported here — they are already exported from CreateRecordWizard.
 * AuthenticatedFetchFn is NOT re-exported — it is already exported from services.
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

export type { ISummarizeFilesDialogProps } from './SummarizeFilesDialog';
export type { ISummaryResultsStepProps } from './SummaryResultsStep';
export type { ISummaryNextStepsStepProps, SummaryActionId } from './SummaryNextStepsStep';
export type { ISummarizeSendEmailStepProps } from './SummarizeSendEmailStep';
export type { ISummarizeCreateProjectStepProps } from './SummarizeCreateProjectStep';
export type { ISummarizeAnalysisStepProps } from './SummarizeAnalysisStep';
export type { StreamSummarizeCallbacks } from './summarizeService';
export type {
  ISummarizeResult,
  ISummarizeResponse,
  IFileHighlight,
  IMentionedParty,
  SummarizeStatus,
} from './summarizeTypes';
