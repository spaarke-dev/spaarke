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

// AssignedToFilter, RecordTypeFilter, StatusFilter — RETIRED in task 032
// (2026-06-03). The new framework's auto-derived filter chips supersede the
// hand-rolled filter components, and the EventsPage host (rewritten in
// task 031) no longer mounts them. Re-exports removed; directories deleted.
// `useAssignedToFilter` + `useStatusFilter` hooks live in
// `context/EventsPageContext.tsx` and remain available via that barrel.
//
// GridSection — RETIRED in task 033b (2026-06-03). Its last consumer
// (`widgets/CalendarWorkspaceWidget`) migrated to `<DataGrid configId hostFilters/>`
// (the @spaarke/ui-components DataGrid framework). Directory deleted; barrel
// re-exports removed. See projects/spaarke-datagrid-framework-r1/notes/drafts/033b-deviations.md.

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
