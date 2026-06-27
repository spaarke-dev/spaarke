/**
 * CalendarSidePane - Root Application Component
 *
 * Renders the Calendar side pane with theme support and parent communication.
 * This component is designed to run inside a Dataverse side pane (Xrm.App.sidePanes).
 *
 * Features:
 * - Dark mode support via Fluent UI theme provider
 * - URL parameter parsing for initial filter state
 * - PostMessage communication with parent window for filter sync
 * - Event date indicators from parent data
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/096-calendar-sidepane-webresource.poml
 */

import * as React from "react";
import {
  FluentProvider,
  tokens,
  makeStyles,
} from "@fluentui/react-components";
import { sendSidePaneFilter } from "@spaarke/ui-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { parseCalendarParams, getInitialFilterState } from "./utils/parseParams";
import {
  sendCalendarReady,
  setupMessageListener,
  type IEventDateInfo,
} from "./utils/postMessage";
import {
  CalendarSection,
  type CalendarFilterOutput,
} from "@spaarke/events-components";

// Pane id MUST match the host EventsPage's sidePaneFilter.paneId
// (see src/solutions/EventsPage/src/config/eventConfig.ts → CALENDAR_PANE_ID).
const PANE_ID = "calendarPane";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// App Component
// ─────────────────────────────────────────────────────────────────────────────

export const App: React.FC = () => {
  const styles = useStyles();
  const [theme, setTheme] = React.useState(resolveTheme);

  // Parse initial parameters from URL
  const params = React.useMemo(() => parseCalendarParams(), []);
  const initialFilter = React.useMemo(() => getInitialFilterState(params), [params]);

  // Track current filter state (for ready message)
  const [currentFilter, setCurrentFilter] = React.useState<CalendarFilterOutput | null>(initialFilter);

  // Event dates from parent for calendar indicators
  const [eventDates, setEventDates] = React.useState<IEventDateInfo[]>([]);

  // Listen for theme changes
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  // Set up message listener for communication with parent
  React.useEffect(() => {
    const cleanup = setupMessageListener((dates) => {
      setEventDates(dates);
    });
    return cleanup;
  }, []);

  // Send ready message on mount
  React.useEffect(() => {
    // Slight delay to ensure parent is listening
    const timer = setTimeout(() => {
      sendCalendarReady(currentFilter);
    }, 100);
    return () => clearTimeout(timer);
  }, []); // Only on mount

  /**
   * Handle filter change from CalendarSection.
   *
   * **Task 035 hardening (2026-06-04)**: sends via the framework's
   * `sendSidePaneFilter` channel (paneId='calendarPane') instead of the legacy
   * `sendFilterChanged` wrapper. The payload is the inner CalendarFilterOutput
   * directly (no { filter: ... } wrap) — the framework's paneId multiplexing
   * replaces the per-message wrapping convention.
   */
  const handleFilterChange = React.useCallback((filter: CalendarFilterOutput | null) => {
    setCurrentFilter(filter);
    sendSidePaneFilter({ paneId: PANE_ID, payload: filter });
  }, []);

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        <CalendarSection
          eventDates={eventDates}
          onFilterChange={handleFilterChange}
          initialSelectedDate={params.selectedDate ?? undefined}
          initialRangeStart={params.rangeStart ?? undefined}
          initialRangeEnd={params.rangeEnd ?? undefined}
        />
      </div>
    </FluentProvider>
  );
};
