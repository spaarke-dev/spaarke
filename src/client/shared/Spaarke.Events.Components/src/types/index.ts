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

export type { SavedView } from '../components/ViewSelectorDropdown/ViewSelectorDropdown';

export type { FetchXmlResult, ViewDefinition, LayoutColumn } from '../services/FetchXmlService';

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from '../context/EventsPageContext';
