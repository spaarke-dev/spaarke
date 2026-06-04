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

export type { IEventRecord } from '../components/GridSection/GridSection';

export type { IUserOption } from '../components/AssignedToFilter/AssignedToFilter';

export type { IEventTypeOption } from '../components/RecordTypeFilter/RecordTypeFilter';

export type { IStatusOption } from '../components/StatusFilter/StatusFilter';

export type { SavedView } from '../components/ViewSelectorDropdown/ViewSelectorDropdown';

export type { FetchXmlResult, ViewDefinition, LayoutColumn } from '../services/FetchXmlService';

export type {
  EventsPageFilters,
  EventsPageState,
  EventsPageActions,
  EventsPageContextValue,
  EventsPageProviderProps,
} from '../context/EventsPageContext';
