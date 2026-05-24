/**
 * @spaarke/events-components — widgets barrel.
 *
 * Higher-level composition widgets that wire together multiple Events
 * components for a specific surface. Currently:
 *
 *  - `CalendarWorkspaceWidget` (task 115) — the 5th SpaarkeAi system
 *    workspace widget. Composes CalendarSection + GridSection +
 *    ViewSelectorDropdown + EventsPage toolbar inside an
 *    EventsPageProvider, with event-open routed to
 *    `Xrm.Navigation.navigateTo` modals instead of `Xrm.App.sidePanes`.
 */
export {
  CalendarWorkspaceWidget,
} from "./CalendarWorkspaceWidget";
export type { CalendarWorkspaceWidgetProps } from "./CalendarWorkspaceWidget";
