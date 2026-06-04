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

// GridSection retained pending task 033 (Calendar widget migration) — its
// last consumer is `widgets/CalendarWorkspaceWidget` which task 033 will
// migrate to `<DataGrid />`. Deletion of GridSection happens at task 033
// closure. See projects/spaarke-datagrid-framework-r1/notes/drafts/032-consumer-audit.md.
export { GridSection } from './GridSection';
export type { GridSectionProps, IEventRecord } from './GridSection';

// AssignedToFilter, RecordTypeFilter, StatusFilter — RETIRED in task 032
// (2026-06-03). The new framework's auto-derived filter chips supersede the
// hand-rolled filter components, and the EventsPage host (rewritten in
// task 031) no longer mounts them. Re-exports removed; directories deleted.
// `useAssignedToFilter` + `useStatusFilter` hooks live in
// `context/EventsPageContext.tsx` and remain available via that barrel.

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
