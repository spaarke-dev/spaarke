/**
 * TodoDetailPanel — Wrapper that loads entity data and wires save callbacks
 * to the shared TodoDetail component via TodoContext.
 *
 * Responsibilities:
 *   - Consumes selectedEventId from TodoContext
 *   - Loads ITodoRecord and ITodoExtension from Dataverse in parallel
 *   - Wraps the shared TodoDetail component with save callbacks that write to
 *     Dataverse AND call TodoContext.updateItem for optimistic Kanban updates
 *   - Header bar with event name and close (X) button
 *
 * No BroadcastChannel — all communication goes through TodoContext (ADR-006).
 * TodoDetail imported from @spaarke/ui-components (ADR-012).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + tokens only)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { TodoDetail } from "@spaarke/ui-components";
import type {
  ITodoRecord,
  ITodoExtension,
  IEventFieldUpdates,
  ITodoExtensionUpdates,
} from "@spaarke/ui-components";
import { useTodoContext } from "../context/TodoContext";
import {
  loadTodoRecord,
  loadTodoExtension,
  saveTodoFields,
  saveTodoExtensionFields,
  deactivateTodoExtension,
  searchContacts,
  removeTodoFlag,
} from "../services/todoDetailService";
import { getXrm } from "../services/xrmProvider";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  header: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
    gap: tokens.spacingHorizontalS,
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
  emptyState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorNeutralForeground3,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  loadingState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
  },
  errorState: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    color: tokens.colorPaletteRedForeground1,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function TodoDetailPanel(): React.ReactElement {
  const styles = useStyles();
  const { selectedEventId, selectItem, updateItem, handleRemove, items } =
    useTodoContext();

  // ---------------------------------------------------------------------------
  // Data state
  // ---------------------------------------------------------------------------
  const [record, setRecord] = React.useState<ITodoRecord | null>(null);
  const [todoExt, setTodoExt] = React.useState<ITodoExtension | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Load record + extension in parallel when selectedEventId changes
  // ---------------------------------------------------------------------------
  React.useEffect(() => {
    if (!selectedEventId) {
      setRecord(null);
      setTodoExt(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    Promise.all([
      loadTodoRecord(selectedEventId),
      loadTodoExtension(selectedEventId),
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
  }, [selectedEventId]);

  // ---------------------------------------------------------------------------
  // Close handler — deselects the item
  // ---------------------------------------------------------------------------
  const handleClose = React.useCallback(() => {
    selectItem(null);
  }, [selectItem]);

  // ---------------------------------------------------------------------------
  // Save event fields (sprk_event) + optimistic Kanban update
  // ---------------------------------------------------------------------------
  const handleSaveEventFields = React.useCallback(
    async (evtId: string, fields: IEventFieldUpdates) => {
      // Capture previous item state for rollback
      const previousItem = items.find((i) => i.sprk_eventid === evtId);

      // Optimistic update — Kanban card re-renders immediately (NFR-02)
      updateItem(evtId, fields as Partial<Record<string, unknown>>);
      setRecord((prev) => (prev ? { ...prev, ...fields } : prev));

      // Persist to Dataverse in background
      const result = await saveTodoFields(evtId, fields);
      if (!result.success) {
        // Rollback optimistic update on failure
        if (previousItem) {
          updateItem(evtId, previousItem);
          setRecord((prev) => (prev ? { ...prev, ...previousItem } : prev));
        }
        console.error(
          "[TodoDetailPanel] Event save failed, reverted:",
          result.error,
        );
      }
      return result;
    },
    [updateItem, items],
  );

  // ---------------------------------------------------------------------------
  // Save todo extension fields (sprk_eventtodo)
  // ---------------------------------------------------------------------------
  const handleSaveTodoExtFields = React.useCallback(
    async (todoId: string, fields: ITodoExtensionUpdates) => {
      // Capture previous extension state for rollback
      const previousExt = todoExt ? { ...todoExt } : null;

      // Optimistic update
      setTodoExt((prev) => (prev ? { ...prev, ...fields } : prev));

      // Persist to Dataverse
      const result = await saveTodoExtensionFields(todoId, fields);
      if (!result.success) {
        // Rollback on failure
        setTodoExt(previousExt);
        console.error(
          "[TodoDetailPanel] Todo extension save failed, reverted:",
          result.error,
        );
      }
      return result;
    },
    [todoExt],
  );

  // ---------------------------------------------------------------------------
  // Deactivate sprk_eventtodo + optimistic Kanban update (mark completed)
  // ---------------------------------------------------------------------------
  const handleDeactivateTodoExt = React.useCallback(
    async (todoId: string) => {
      // Capture previous states for rollback
      const previousExt = todoExt ? { ...todoExt } : null;
      const previousItem = selectedEventId
        ? items.find((i) => i.sprk_eventid === selectedEventId)
        : undefined;

      // Optimistic update — immediately reflect completed status
      setTodoExt((prev) =>
        prev ? { ...prev, statecode: 1, statuscode: 2 } : prev,
      );
      if (selectedEventId) {
        updateItem(selectedEventId, {
          sprk_todostatus: 100000001, // Completed
        });
      }

      // Persist to Dataverse
      const result = await deactivateTodoExtension(todoId);
      if (!result.success) {
        // Rollback on failure
        setTodoExt(previousExt);
        if (selectedEventId && previousItem) {
          updateItem(selectedEventId, previousItem);
        }
        console.error(
          "[TodoDetailPanel] Deactivate todo extension failed, reverted:",
          result.error,
        );
      }
      return result;
    },
    [selectedEventId, updateItem, todoExt, items],
  );

  // ---------------------------------------------------------------------------
  // Search contacts for the Assigned To picker
  // ---------------------------------------------------------------------------
  const handleSearchContacts = React.useCallback(
    async (query: string) => searchContacts(query),
    [],
  );

  // ---------------------------------------------------------------------------
  // Open regarding record via Xrm.Navigation
  // ---------------------------------------------------------------------------
  const handleOpenRegardingRecord = React.useCallback(
    (entityName: string, recordId: string) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = getXrm() as any;
      if (xrm?.Navigation?.navigateTo) {
        xrm.Navigation.navigateTo(
          {
            pageType: "entityrecord",
            entityName,
            entityId: recordId,
          },
          { target: 1 }, // 1 = new window/tab
        ).catch(() => {
          // Fallback: silent — Xrm navigation failed
        });
      }
    },
    [],
  );

  // ---------------------------------------------------------------------------
  // Remove from To Do — clear flag, remove from Kanban, close panel
  // ---------------------------------------------------------------------------
  const handleRemoveTodo = React.useCallback(
    async (evtId: string) => {
      const result = await removeTodoFlag(evtId);
      if (result.success) {
        handleRemove(evtId);
        selectItem(null);
      } else {
        throw new Error(result.error ?? "Failed to remove from To Do");
      }
    },
    [handleRemove, selectItem],
  );

  // ---------------------------------------------------------------------------
  // Render: empty state (no selection)
  // ---------------------------------------------------------------------------
  if (!selectedEventId) {
    return (
      <div
        className={styles.emptyState}
        role="complementary"
        aria-label="To-do detail panel"
      >
        <Text size={200}>Select a to-do item to view details</Text>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: loading
  // ---------------------------------------------------------------------------
  if (isLoading) {
    return (
      <div className={styles.root} role="complementary" aria-label="To-do detail panel">
        <div className={styles.header}>
          <Text weight="semibold" size={400} className={styles.headerTitle}>
            Loading...
          </Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label="Close detail panel"
            onClick={handleClose}
          />
        </div>
        <div className={styles.loadingState}>
          <Spinner size="medium" label="Loading..." />
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: error
  // ---------------------------------------------------------------------------
  if (error) {
    return (
      <div className={styles.root} role="complementary" aria-label="To-do detail panel">
        <div className={styles.header}>
          <Text weight="semibold" size={400} className={styles.headerTitle}>
            Error
          </Text>
          <Button
            appearance="subtle"
            icon={<DismissRegular />}
            aria-label="Close detail panel"
            onClick={handleClose}
          />
        </div>
        <div className={styles.errorState}>
          <Text>{error}</Text>
        </div>
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render: full detail panel
  // ---------------------------------------------------------------------------
  return (
    <div
      className={styles.root}
      role="complementary"
      aria-label="To-do detail panel"
    >
      {/* Header with event name and close button */}
      <div className={styles.header}>
        <Text weight="semibold" size={400} className={styles.headerTitle}>
          {record?.sprk_eventname ?? "To Do Details"}
        </Text>
        <Button
          appearance="subtle"
          icon={<DismissRegular />}
          aria-label="Close detail panel"
          onClick={handleClose}
        />
      </div>

      {/* TodoDetail body */}
      <div className={styles.body}>
        <TodoDetail
          record={record}
          todoExtension={todoExt}
          isLoading={false}
          error={null}
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
  );
}
