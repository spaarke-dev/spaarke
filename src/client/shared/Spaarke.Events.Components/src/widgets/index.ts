/**
 * @spaarke/events-components — widgets barrel.
 *
 * Higher-level composition widgets that wire together multiple Events
 * components for a specific surface. Currently:
 *
 *  - `CalendarWorkspaceWidget` (task 115) — the 5th SpaarkeAi system
 *    workspace widget. Composes CalendarSection + the `@spaarke/ui-components`
 *    DataGrid framework inside an EventsPageProvider, with event-open routed
 *    to `Xrm.Navigation.navigateTo` modals instead of `Xrm.App.sidePanes`.
 *    (Corrected 2026-10-03, task 087: the prior "+ GridSection + ViewSelectorDropdown"
 *    description was stale — both were retired/deleted and the widget never
 *    actually imported them. See
 *    projects/spaarke-ontology-platform-r1/notes/reuse-verification-2026-10-02.md §8.8 X21.)
 */
export { CalendarWorkspaceWidget } from './CalendarWorkspaceWidget';
export type { CalendarWorkspaceWidgetProps } from './CalendarWorkspaceWidget';
