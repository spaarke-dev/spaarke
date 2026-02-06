/**
 * Components module exports
 *
 * Components for the Events Custom Page:
 * - CalendarSection: Date-based filtering with multi-month calendar (Task 061)
 * - GridSection: Events data grid with WebAPI fetching (Task 061)
 * - AssignedToFilter: User filter dropdown (Task 063)
 * - Filter toolbar components (Tasks 064-066)
 */

export { CalendarSection } from "./CalendarSection";
export type {
  CalendarSectionProps,
  CalendarFilterOutput,
  CalendarFilterSingle,
  CalendarFilterRange,
  CalendarFilterClear,
  CalendarFilterType,
  IEventDateInfo,
} from "./CalendarSection";

export { GridSection } from "./GridSection";
export type { GridSectionProps, IEventRecord } from "./GridSection";

export { AssignedToFilter } from "./AssignedToFilter";
export type { AssignedToFilterProps, IUserOption } from "./AssignedToFilter";

export { RecordTypeFilter } from "./RecordTypeFilter";
export type { RecordTypeFilterProps, IEventTypeOption } from "./RecordTypeFilter";

export { StatusFilter, getStatusOptions, getActionableStatuses } from "./StatusFilter";
export type { StatusFilterProps, IStatusOption } from "./StatusFilter";

// CalendarDrawer removed in Task 096 - Calendar is now in Xrm.App.sidePanes
// Use CalendarSidePane web resource instead

export {
  ViewSelectorDropdown,
  useViewSelection,
  EVENT_VIEWS,
  DEFAULT_VIEW_ID,
} from "./ViewSelectorDropdown";
export type {
  ViewSelectorDropdownProps,
  SavedView,
} from "./ViewSelectorDropdown";

export { ColumnFilterHeader } from "./ColumnFilterHeader";
export type {
  ColumnFilterHeaderProps,
  ColumnFilterType,
  ColumnFilterOption,
} from "./ColumnFilterHeader";

export { ColumnHeaderMenu } from "./ColumnHeaderMenu";
export type {
  ColumnHeaderMenuProps,
  ColumnFilterType as ColumnMenuFilterType,
  ColumnFilterOption as ColumnMenuFilterOption,
  SortDirection,
} from "./ColumnHeaderMenu";
