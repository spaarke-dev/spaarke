/**
 * index.ts
 * Public barrel export for the SummarizeFiles wizard components.
 */

export { SummarizeFilesDialog } from './SummarizeFilesDialog';
export { SummaryResultsStep } from './SummaryResultsStep';
export { SummaryNextStepsStep } from './SummaryNextStepsStep';
export { runSummarize } from './summarizeService';

export type { ISummarizeFilesDialogProps } from './SummarizeFilesDialog';
export type { ISummaryResultsStepProps } from './SummaryResultsStep';
export type { ISummaryNextStepsStepProps, SummaryActionId } from './SummaryNextStepsStep';
export type {
  ISummarizeResult,
  ISummarizeResponse,
  IFileHighlight,
  IMentionedParty,
  SummarizeStatus,
} from './summarizeTypes';
