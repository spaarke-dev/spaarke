/**
 * App — Root component for TodoDetailSidePane.
 *
 * Reads eventId from URL params, loads data from TWO Dataverse entities:
 *   - sprk_event: core event fields
 *   - sprk_eventtodo: to-do extension fields (notes, completed, statuscode)
 *
 * Communicates saves back to the parent Kanban board via BroadcastChannel.
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { resolveCodePageTheme as resolveTheme } from "@spaarke/ui-components";
import { parseSidePaneParams } from "./utils/parseParams";
import {
  loadTodoRecord,
  loadTodoExtension,
  saveTodoFields,
  saveTodoExtensionFields,
  deactivateTodoExtension,
  searchContacts,
} from "./services/todoService";
import { sendTodoSaved } from "./utils/broadcastChannel";
import { getXrm } from "./utils/xrmAccess";
import { TodoDetail } from "@spaarke/ui-components";
import type {
  ITodoRecord,
  ITodoExtension,
  IEventFieldUpdates,
  ITodoExtensionUpdates,
} from "@spaarke/ui-components";

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
  const [todoExt, setTodoExt] = React.useState<ITodoExtension | null>(null);
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

  // Load both entities in parallel when eventId changes
  React.useEffect(() => {
    if (!eventId) {
      setIsLoading(false);
      setError("No event ID provided");
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    Promise.all([
      loadTodoRecord(eventId),
      loadTodoExtension(eventId),
    ]).then(([eventResult, extResult]) => {
      if (cancelled) return;
      if (eventResult.success && eventResult.data) {
        setRecord(eventResult.data);
      } else {
        setError(eventResult.error ?? "Failed to load record");
      }
      // Extension is optional — may not exist for every event
      if (extResult.success && extResult.data) {
        setTodoExt(extResult.data);
      } else {
        setTodoExt(null);
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

  // Save event fields (sprk_event)
  const handleSaveEventFields = React.useCallback(
    async (evtId: string, fields: IEventFieldUpdates) => {
      const result = await saveTodoFields(evtId, fields);
      if (result.success) {
        setRecord((prev) => (prev ? { ...prev, ...fields } : prev));
        sendTodoSaved(evtId);
      } else {
        console.error("[App] Event save failed:", result.error);
      }
      return result;
    },
    []
  );

  // Save todo extension fields (sprk_eventtodo)
  const handleSaveTodoExtFields = React.useCallback(
    async (todoId: string, fields: ITodoExtensionUpdates) => {
      const result = await saveTodoExtensionFields(todoId, fields);
      if (result.success) {
        setTodoExt((prev) => (prev ? { ...prev, ...fields } : prev));
        if (eventId) sendTodoSaved(eventId);
      } else {
        console.error("[App] Todo extension save failed:", result.error);
      }
      return result;
    },
    [eventId]
  );

  // Deactivate sprk_eventtodo (via direct REST API — Xrm.WebApi ignores statecode)
  const handleDeactivateTodoExt = React.useCallback(
    async (todoId: string) => {
      const result = await deactivateTodoExtension(todoId);
      if (result.success) {
        setTodoExt((prev) =>
          prev ? { ...prev, statecode: 1, statuscode: 2 } : prev
        );
        if (eventId) sendTodoSaved(eventId);
      } else {
        console.error("[App] Deactivate todo extension failed:", result.error);
      }
      return result;
    },
    [eventId]
  );

  // Search contacts for the Assigned To picker
  const handleSearchContacts = React.useCallback(
    async (query: string) => searchContacts(query),
    []
  );

  // Open regarding record in a new browser tab via Xrm.Navigation
  const handleOpenRegardingRecord = React.useCallback(
    (entityName: string, recordId: string) => {
      const xrm = getXrm();
      if (xrm?.Navigation) {
        xrm.Navigation.navigateTo(
          {
            pageType: "entityrecord",
            entityName,
            entityId: recordId,
          },
          { target: 1 } // 1 = new window/tab
        ).catch(() => {
          // Fallback: silent — Xrm navigation failed
        });
      }
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
            todoExtension={todoExt}
            isLoading={isLoading}
            error={error}
            onSaveEventFields={handleSaveEventFields}
            onSaveTodoExtFields={handleSaveTodoExtFields}
            onDeactivateTodoExt={handleDeactivateTodoExt}
            onRemoveTodo={handleRemoveTodo}
            onClose={handleClose}
            onSearchContacts={handleSearchContacts}
            onOpenRegardingRecord={handleOpenRegardingRecord}
          />
        </div>
      </div>
    </FluentProvider>
  );
}
