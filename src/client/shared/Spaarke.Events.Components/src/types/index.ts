/**
 * @spaarke/events-components — types barrel
 *
 * Re-exports of public types that have utility outside component files.
 * Component-local prop interfaces are exported from each component's barrel
 * (e.g. `./components/CalendarSection`).
 */

export type {
  CalendarFilterOutput,
  CalendarFilterSingle,
  CalendarFilterRange,
  CalendarFilterClear,
  CalendarFilterType,
  IEventDateInfo,
} from '../components/CalendarSection/CalendarSection';

// IUserOption, IEventTypeOption, IStatusOption — RETIRED in task 032
// (2026-06-03) along with their component directories. No external
// consumers; CalendarWorkspaceWidget has its own inline IStatusOption.
//
// IEventRecord — RETIRED in task 033b (2026-06-03) with the GridSection
// directory deletion. The DataGrid framework uses `Record<string, unknown>`
// for record rows; consumers that need typed event fields define them inline.
//
// SavedView (from ViewSelectorDropdown) and FetchXmlResult/ViewDefinition/
// LayoutColumn (from the Events-local FetchXmlService) — DELETED (task 087,
// C-24/C-25, 2026-10-03) along with their owning modules. Zero consumers
// outside this package's own barrels. See
// projects/spaarke-ontology-platform-r1/notes/reuse-verification-2026-10-02.md
// §8.8 X20/X21. Note: this is NOT `Spaarke.UI.Components/src/services/FetchXmlService.ts`,
// the different, live service cited by ADR-012.

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from '../context/EventsPageContext';
