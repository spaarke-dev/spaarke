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
  AccessRights,
  type RecordAccessResult,
} from "./useRecordAccess";

export {
  useEventTypeConfig,
  DEFAULT_SECTION_STATES,
  ALL_SECTION_NAMES,
  type SectionCollapseState,
  type SectionName,
  type ISectionDefaults,
  type IEventTypeFieldConfig,
  type IComputedFieldState,
  type IComputedSectionState,
  type IComputedFieldStates,
  type UseEventTypeConfigResult,
} from "./useEventTypeConfig";
