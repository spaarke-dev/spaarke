export * from './types';
export * from './useDatasetMode';
export * from './useHeadlessMode';
export * from './useVirtualization';
export * from './useKeyboardShortcuts';
export * from './useEntityTypeConfig';
export * from './useDirtyFields';
export * from './useOptimisticSave';
export * from './useWriteMode';
export { useSseStream, parseSseEvent, parsePaneEvent, readSseStream } from './useSseStream';
export type { ReadSseStreamOptions } from './useSseStream';
export * from './useAiSummary';
// task 045 (FR-P3-06, NFR-08): the useLinearRunProgress HOOK was deleted (dead
// hook body — zero runtime call sites; duplicated the canonical readSseStream
// loop). Only its event/state TYPES remain at the same module path.
export type { LinearRunEvent, LinearRunEventKind, LinearRunState } from './useLinearRunProgress';
export * from './useAiPrefill';
export * from './useForceSimulation';
export * from './useInlineAiToolbar';
export * from './useInlineAiActions';
export * from './useSlashCommands';
export * from './useTwoPanelLayout';
export * from './useTheme';
export * from './useDocumentMultiSelect';

// DataGrid framework (task 003)
export { DataGridContextProvider, useDataGridContext, useDataGridContextOptional } from './useDataGridContext';
export type { DataGridContextValue, DataGridContextProviderProps, DataGridParentContext } from './useDataGridContext';

// Record-header primitives (record-header-and-notepad-r1 Phase 1, tasks 009–012).
// Xrm.WebApi + Xrm.Navigation host-context surface only — no @spaarke/auth, no BFF
// (spec NFR-05, NFR-07; ADR-028 boundary).

// FR-05 — read a Dataverse record's specified fields via Xrm.WebApi.retrieveRecord.
export { useRecordFieldValues } from './useRecordFieldValues';
export type { UseRecordFieldValuesResult } from './useRecordFieldValues';

// FR-06 — filter-agnostic $count query (mount + focus, no polling).
export { useRelatedCount } from './useRelatedCount';
export type { UseRelatedCountResult } from './useRelatedCount';

// FR-07/08/08a/09/10/11 — LINCHPIN toolbar-actions hook composing sparkle / checkmark
// / annotation slots. Explicit-named export to keep `__testables` OUT of the public
// consumer surface (per file-header directive in useRecordHeaderToolbarActions.ts).
export { useRecordHeaderToolbarActions } from './useRecordHeaderToolbarActions';
export type {
  IUseRecordHeaderToolbarActionsOptions,
  IUseRecordHeaderToolbarActionsEnabled,
  IUseRecordHeaderToolbarActionsResult,
} from './useRecordHeaderToolbarActions';

// FR-09/10/11 — modal size + entity mapping constants + memo-filter builder.
// Consumed by useRecordHeaderToolbarActions AND by any consumer that needs to
// build memo filters or launch the Notepad / SmartTodo modals directly.
export {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
  RECORDSUMMARY_FIELD,
  SUPPORTED_MEMO_PARENTS,
  SUPPORTED_TODO_PARENTS,
  buildMemoFilterForParent,
  buildTodoFilterForParent,
} from './toolbarLaunchDefaults';
