/**
 * App — Root component for TodoDetailSidePane.
 *
 * Reads eventId from URL params, loads the record from Dataverse via
 * Xrm.WebApi, renders TodoDetail, and communicates saves back to the
 * parent Kanban board via BroadcastChannel.
 */

import * as React from "react";
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { parseSidePaneParams } from "./utils/parseParams";
import { loadTodoRecord, saveDescription } from "./services/todoService";
import { sendTodoSaved } from "./utils/broadcastChannel";
import { TodoDetail } from "./components/TodoDetail";
import type { ITodoRecord } from "./types/TodoRecord";

// ---------------------------------------------------------------------------
// Theme resolution (matches EventDetailSidePane pattern)
// ---------------------------------------------------------------------------

function resolveTheme(): typeof webLightTheme {
  // Check system preference
  if (typeof window !== "undefined" && window.matchMedia?.("(prefers-color-scheme: dark)").matches) {
    return webDarkTheme;
  }
  // Check Power Apps dark mode indicator (navbar bg)
  try {
    const navBar = window.parent?.document?.querySelector?.("[data-id='navbar']") as HTMLElement | null;
    if (navBar) {
      const bg = window.getComputedStyle(navBar).backgroundColor;
      if (bg && bg !== "rgb(255, 255, 255)" && bg !== "rgba(0, 0, 0, 0)") {
        const [r, g, b] = bg.match(/\d+/g)?.map(Number) ?? [255, 255, 255];
        if ((r + g + b) / 3 < 128) return webDarkTheme;
      }
    }
  } catch {
    // Cross-origin — ignore
  }
  return webLightTheme;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  headerTitle: {
    flex: "1 1 0",
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  body: {
    flex: "1 1 0",
    overflow: "hidden",
  },
});

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------

export function App() {
  const styles = useStyles();
  const theme = React.useMemo(() => resolveTheme(), []);
  const params = React.useMemo(() => parseSidePaneParams(), []);

  const [record, setRecord] = React.useState<ITodoRecord | null>(null);
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  // Listen for URL changes (pane reuse — navigate to different event)
  const [eventId, setEventId] = React.useState(params.eventId);

  React.useEffect(() => {
    // Listen for hashchange/popstate for pane navigation
    const handleNavigation = () => {
      const newParams = parseSidePaneParams();
      if (newParams.eventId && newParams.eventId !== eventId) {
        setEventId(newParams.eventId);
      }
    };
    window.addEventListener("hashchange", handleNavigation);
    window.addEventListener("popstate", handleNavigation);
    return () => {
      window.removeEventListener("hashchange", handleNavigation);
      window.removeEventListener("popstate", handleNavigation);
    };
  }, [eventId]);

  // Load record when eventId changes
  React.useEffect(() => {
    if (!eventId) {
      setIsLoading(false);
      setError("No event ID provided");
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    loadTodoRecord(eventId).then((result) => {
      if (cancelled) return;
      if (result.success && result.data) {
        setRecord(result.data);
      } else {
        setError(result.error ?? "Failed to load record");
      }
      setIsLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [eventId]);

  // Save description handler
  const handleSaveDescription = React.useCallback(
    async (evtId: string, description: string) => {
      const result = await saveDescription(evtId, description);
      if (result.success) {
        // Update local record
        setRecord((prev) =>
          prev ? { ...prev, sprk_description: description } : prev
        );
        // Notify parent Kanban to refresh
        sendTodoSaved(evtId);
      } else {
        console.error("[App] Save failed:", result.error);
      }
    },
    []
  );

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Header with event name */}
        <div className={styles.header}>
          <Text
            weight="semibold"
            size={400}
            className={styles.headerTitle}
          >
            {record?.sprk_eventname ?? "To Do Details"}
          </Text>
        </div>

        {/* Body */}
        <div className={styles.body}>
          <TodoDetail
            record={record}
            isLoading={isLoading}
            error={error}
            onSaveDescription={handleSaveDescription}
          />
        </div>
      </div>
    </FluentProvider>
  );
}
