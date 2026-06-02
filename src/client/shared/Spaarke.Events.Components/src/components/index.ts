/**
 * @spaarke/events-components — components barrel
 *
 * Components for Events + Tasks surfaces (standalone EventsPage + Calendar
 * workspace widget). Each component lives in its own folder so the package
 * remains tree-shake-friendly.
 */

export { CalendarSection, CalendarDrawer } from './CalendarSection';
export type {
  CalendarSectionProps,
  CalendarFilterOutput,
  CalendarFilterSingle,
  CalendarFilterRange,
  CalendarFilterClear,
  CalendarFilterType,
  IEventDateInfo,
  CalendarDrawerProps,
} from './CalendarSection';

// CalendarFilterPane — record-form side pane filter builder (R4 task 055 B-6
// Option B). Intentionally coexists with CalendarSection (workspace widget);
// different user intents. See CalendarFilterPane.tsx header for details.
export { CalendarFilterPane, toIsoDateString } from './CalendarFilterPane';
export type {
  CalendarFilterPaneProps,
  CalendarFilterPaneOutput,
  CalendarFilterPaneSingle,
  CalendarFilterPaneRange,
  CalendarFilterPaneClear,
  CalendarFilterPaneFilterType,
} from './CalendarFilterPane';

export { GridSection } from './GridSection';
export type { GridSectionProps, IEventRecord } from './GridSection';

export { AssignedToFilter } from './AssignedToFilter';
export type { AssignedToFilterProps, IUserOption } from './AssignedToFilter';

export { RecordTypeFilter } from './RecordTypeFilter';
export type { RecordTypeFilterProps, IEventTypeOption } from './RecordTypeFilter';

export { StatusFilter, getStatusOptions, getActionableStatuses } from './StatusFilter';
export type { StatusFilterProps, IStatusOption } from './StatusFilter';

export { ColumnFilterHeader } from './ColumnFilterHeader';
export type { ColumnFilterHeaderProps, ColumnFilterType, ColumnFilterOption } from './ColumnFilterHeader';

export { ColumnHeaderMenu } from './ColumnHeaderMenu';
export type {
  ColumnHeaderMenuProps,
  ColumnMenuFilterType,
  ColumnMenuFilterOption,
  SortDirection,
} from './ColumnHeaderMenu';

export { ViewSelectorDropdown, useViewSelection, EVENT_VIEWS, DEFAULT_VIEW_ID } from './ViewSelectorDropdown';
export type { ViewSelectorDropdownProps, SavedView } from './ViewSelectorDropdown';
