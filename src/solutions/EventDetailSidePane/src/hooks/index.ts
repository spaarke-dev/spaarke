/**
 * EventDetailSidePane Hooks
 *
 * Exports all custom hooks for the Event Detail Side Pane.
 */

export {
  useOptimisticUpdate,
  type GridUpdateCallback,
  type OptimisticErrorState,
  type UseOptimisticUpdateResult,
  type OptimisticGridProps,
} from "./useOptimisticUpdate";

export {
  useRecordAccess,
  isReadOnly,
  type RecordAccessResult,
} from "./useRecordAccess";

export {
  useFormConfig,
  type UseFormConfigResult,
} from "./useFormConfig";

export {
  useRelatedRecord,
  type UseRelatedRecordOptions,
  type UseRelatedRecordResult,
} from "./useRelatedRecord";
