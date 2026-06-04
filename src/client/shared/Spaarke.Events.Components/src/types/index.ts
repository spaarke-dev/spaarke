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

// IEventRecord retained pending task 033 (Calendar widget migration) —
// last consumer is `widgets/CalendarWorkspaceWidget`. See
// projects/spaarke-datagrid-framework-r1/notes/drafts/032-consumer-audit.md.
export type { IEventRecord } from '../components/GridSection/GridSection';

// IUserOption, IEventTypeOption, IStatusOption — RETIRED in task 032
// (2026-06-03) along with their component directories. No external
// consumers; CalendarWorkspaceWidget has its own inline IStatusOption.

export type { SavedView } from '../components/ViewSelectorDropdown/ViewSelectorDropdown';

export type { FetchXmlResult, ViewDefinition, LayoutColumn } from '../services/FetchXmlService';

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from '../context/EventsPageContext';
