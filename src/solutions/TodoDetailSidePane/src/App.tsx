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

/**
 * Theme resolution — matches EventDetailSidePane ThemeProvider pattern.
 * Priority: localStorage > navbar detection > default light.
 *
 * NOTE: System preference (prefers-color-scheme) is intentionally NOT used
 * as a fallback because side pane iframes cannot detect the Power Apps theme.
 * OS dark mode + Power Apps light mode would show dark incorrectly.
 */
function resolveTheme(): typeof webLightTheme {
  // 1. Check localStorage (shared across all Spaarke web resources)
  try {
    const pref = localStorage.getItem("spaarke-theme");
    if (pref === "dark") return webDarkTheme;
    if (pref === "light") return webLightTheme;
  } catch {
    // localStorage unavailable
  }

  // 2. Try to detect Power Apps navbar (works when same-origin)
  const framesToCheck: Array<Window | null> = [];
  try { framesToCheck.push(window.top); } catch { /* cross-origin */ }
  try { framesToCheck.push(window.parent); } catch { /* cross-origin */ }
  for (const frame of framesToCheck) {
    if (!frame || frame === window) continue;
    try {
      const navBar = frame.document?.querySelector?.(
        '[data-id="navbar-container"]'
      ) as HTMLElement | null;
      if (navBar) {
        const bg = window.getComputedStyle(navBar).backgroundColor;
        if (bg === "rgb(10, 10, 10)") return webDarkTheme;
        return webLightTheme;
      }
    } catch {
      // Cross-origin — try next frame
    }
  }

  // 3. Default to light theme (safe default for Power Apps)
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

  // Close handler — closes the Xrm side pane
  const handleClose = React.useCallback(() => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any =
        (window as any)?.Xrm ??
        (window.parent as any)?.Xrm ??
        (window.top as any)?.Xrm;
      const pane = xrm?.App?.sidePanes?.getPane("todoDetailPane");
      if (pane) {
        pane.close();
        return;
      }
    } catch {
      // Xrm unavailable
    }
    // Fallback: try closing window
    try { window.close(); } catch { /* ignore */ }
  }, []);

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

  // Remove from To Do — sets sprk_todoflag = false, notifies Kanban, closes pane
  const handleRemoveTodo = React.useCallback(
    async (evtId: string) => {
      const result = await saveTodoFields(evtId, { sprk_todoflag: false });
      if (result.success) {
        sendTodoSaved(evtId);
        handleClose();
      } else {
        throw new Error(result.error ?? "Failed to remove from To Do");
      }
    },
    [handleClose]
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
            onRemoveTodo={handleRemoveTodo}
            onClose={handleClose}
          />
        </div>
      </div>
    </FluentProvider>
  );
}
