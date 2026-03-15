/**
 * Hooks exports for Analysis Workspace PCF control
 */

export { useSseStream } from './useSseStream';
export type { ISseStreamOptions, ISseStreamState, ISseStreamActions } from './useSseStream';

export { useAuth } from './useAuth';
export type { UseAuthOptions, UseAuthResult } from './useAuth';

export { useDocumentResolution } from './useDocumentResolution';
export type { UseDocumentResolutionOptions, UseDocumentResolutionResult } from './useDocumentResolution';

export { useWorkingDocumentSave } from './useWorkingDocumentSave';
export type { UseWorkingDocumentSaveOptions, UseWorkingDocumentSaveResult } from './useWorkingDocumentSave';

export { useChatState } from './useChatState';
export type { UseChatStateOptions, UseChatStateResult } from './useChatState';

export { useAnalysisData, isWebApiAvailable } from './useAnalysisData';
export type { UseAnalysisDataOptions, UseAnalysisDataResult, PendingExecution } from './useAnalysisData';

export { useAnalysisExecution } from './useAnalysisExecution';
export type { UseAnalysisExecutionOptions, UseAnalysisExecutionResult } from './useAnalysisExecution';

export { usePanelResize } from './usePanelResize';
export type { UsePanelResizeOptions, UsePanelResizeResult } from './usePanelResize';
