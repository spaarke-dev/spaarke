/**
 * FindSimilar barrel export.
 *
 * FindSimilarDialog is a local adapter that injects LegalWorkspace-specific
 * services into the shared @spaarke/ui-components FindSimilarDialog.
 * All other exports re-export from the shared library.
 */
export { FindSimilarDialog } from './FindSimilarDialog';
export type { IFindSimilarDialogProps } from './FindSimilarDialog';
export { FindSimilarResultsStep } from './FindSimilarResultsStep';
export type { IFindSimilarResultsStepProps } from '@spaarke/ui-components/components/FindSimilar/FindSimilarResultsStep';
export type {
  FindSimilarDomain,
  FindSimilarStatus,
  IDocumentResult,
  IRecordResult,
  IFindSimilarResults,
  IGridRecord,
  IGridColumn,
} from './findSimilarTypes';
