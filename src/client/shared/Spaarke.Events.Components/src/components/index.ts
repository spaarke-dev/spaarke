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
//
// GridSection — RETIRED in task 033b (2026-06-03). Its last consumer
// (`widgets/CalendarWorkspaceWidget`) migrated to `<DataGrid configId hostFilters/>`
// (the @spaarke/ui-components DataGrid framework). Directory deleted; barrel
// re-exports removed. See projects/spaarke-datagrid-framework-r1/notes/drafts/033b-deviations.md.
//
// ColumnFilterHeader, ColumnHeaderMenu, ViewSelectorDropdown — DELETED (task 087,
// C-2/C-24/C-25, 2026-10-03). Pre-DataGrid-framework forks that collided by name
// with the live `@spaarke/ui-components` DataGrid column-header primitives of the
// same names; EventsPage was rewritten onto that framework (task 031) and stopped
// importing this package's forks, leaving them orphaned and reachable only by
// name-collision autocomplete into a non-functional header. The ColumnHeaderMenu
// fork additionally lacked the live version's dark-mode portal fix (NFR-03/ADR-021).
// See projects/spaarke-ontology-platform-r1/notes/reuse-verification-2026-10-02.md
// §8.2 D1 / §8.8 X20/X21. `useAssignedToFilter` + `useStatusFilter` selector hooks
// (also dead, same migration) were removed from `context/EventsPageContext.tsx`.
