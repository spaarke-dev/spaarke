/**
 * CalendarFilterPane — multi-control date filter builder for Dataverse
 * side panes on record forms. Promoted from
 * `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` per
 * R4 task 055 (B-6 Option B). Coexists intentionally with the workspace-
 * widget `CalendarSection` — they serve different user intents.
 */

export { CalendarFilterPane, toIsoDateString } from "./CalendarFilterPane";
export type {
  CalendarFilterPaneProps,
  CalendarFilterPaneOutput,
  CalendarFilterPaneSingle,
  CalendarFilterPaneRange,
  CalendarFilterPaneClear,
  CalendarFilterPaneFilterType,
  IEventDateInfo,
} from "./CalendarFilterPane";
