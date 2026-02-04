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
