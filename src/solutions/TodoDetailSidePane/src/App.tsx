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
import { loadTodoRecord, saveTodoFields } from "./services/todoService";
import type { ITodoFieldUpdates } from "./services/todoService";
import { sendTodoSaved } from "./utils/broadcastChannel";
import { TodoDetail } from "./components/TodoDetail";
import type { ITodoRecord } from "./types/TodoRecord";

// ---------------------------------------------------------------------------
// Theme resolution (matches EventDetailSidePane pattern)
// ---------------------------------------------------------------------------

function resolveTheme(): typeof webLightTheme {
  // 1. Check Power Apps navbar first (authoritative indicator of app theme)
  try {
    const navBar = window.parent?.document?.querySelector?.("[data-id='navbar']") as HTMLElement | null;
    if (navBar) {
      const bg = window.getComputedStyle(navBar).backgroundColor;
      if (bg && bg !== "rgb(255, 255, 255)" && bg !== "rgba(0, 0, 0, 0)") {
        const [r, g, b] = bg.match(/\d+/g)?.map(Number) ?? [255, 255, 255];
        if ((r + g + b) / 3 < 128) return webDarkTheme;
      }
      // Navbar found and is light — return light theme
      return webLightTheme;
    }
  } catch {
    // Cross-origin — fall through to system preference
  }
  // 2. Fallback: system preference (only when navbar is inaccessible)
  if (typeof window !== "undefined" && window.matchMedia?.("(prefers-color-scheme: dark)").matches) {
    return webDarkTheme;
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

  // Save fields handler — updates one or more fields on the event record
  const handleSaveFields = React.useCallback(
    async (evtId: string, fields: ITodoFieldUpdates) => {
      const result = await saveTodoFields(evtId, fields);
      if (result.success) {
        // Update local record with saved values
        setRecord((prev) => (prev ? { ...prev, ...fields } : prev));
        // Notify parent Kanban to refresh
        sendTodoSaved(evtId);
      } else {
        console.error("[App] Save failed:", result.error);
      }
      return result;
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
            onSaveFields={handleSaveFields}
          />
        </div>
      </div>
    </FluentProvider>
  );
}
