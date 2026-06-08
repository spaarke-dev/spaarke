/**
 * TodoDetailPanel — Wrapper that loads a single `sprk_todo` record and wires
 * save / dismiss callbacks to the shared TodoDetail component via TodoContext.
 *
 * Single-entity model (R3 FR-09 / task 011): the legacy two-entity load
 * (sprk_event + sprk_eventtodo) is removed (OS-1). The new `TodoDetail` API
 * exposes `onSaveTodo(todoId, ITodoFieldUpdates)` + `onDismissTodo(todoId)`
 * — see `projects/smart-todo-decoupling-r3/notes/tododetail-consumer-breakage-task011.md`.
 *
 * Responsibilities:
 *   - Consume the selected todo ID from TodoContext (`selectedEventId` —
 *     property name retained; value is a sprk_todoid).
 *   - Load one ITodoRecord from Dataverse.
 *   - Wrap shared TodoDetail with save / dismiss callbacks that write to
 *     Dataverse AND call TodoContext.updateItem for optimistic Kanban refresh.
 *   - Render header bar with todo name + close (X) button.
 *
 * Per ADR-006: no BroadcastChannel — all communication goes through TodoContext.
 * Per ADR-012: TodoDetail comes from @spaarke/ui-components (context-agnostic).
 * Per ADR-021: Fluent UI v9 design system (makeStyles + tokens only).
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
import { TodoDetail } from "@spaarke/ui-components/TodoDetail";
import type {
  ITodoRecord,
  ITodoFieldUpdates,
} from "@spaarke/ui-components/TodoDetail";
import { useTodoContext } from "../context/TodoContext";
import {
  loadTodoRecord,
  saveTodoFields,
  dismissTodo,
  searchContacts,
} from "../services/todoDetailService";
import { getXrm } from "../services/xrmProvider";

// ---------------------------------------------------------------------------
// statuscode constants (sprk_todo per task 009)
// ---------------------------------------------------------------------------

const STATUSCODE_DISMISSED = 659490002;
const STATECODE_INACTIVE = 1;

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
    overflow: "auto",
    minHeight: 0,
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
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Load record when selectedEventId (sprk_todoid) changes
  // ---------------------------------------------------------------------------
  React.useEffect(() => {
    if (!selectedEventId) {
      setRecord(null);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    loadTodoRecord(selectedEventId).then((result) => {
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
  }, [selectedEventId]);

  // ---------------------------------------------------------------------------
  // Close handler — deselects the item
  // ---------------------------------------------------------------------------
  const handleClose = React.useCallback(() => {
    selectItem(null);
  }, [selectItem]);

  // ---------------------------------------------------------------------------
  // Save sprk_todo fields + optimistic Kanban update
  // ---------------------------------------------------------------------------
  const handleSaveTodo = React.useCallback(
    async (todoId: string, fields: ITodoFieldUpdates) => {
      // Capture previous item state for rollback
      const previousItem = items.find((i) => i.sprk_todoid === todoId);

      // Optimistic update — Kanban card re-renders immediately (NFR-02)
      updateItem(todoId, fields as Partial<Record<string, unknown>>);
      setRecord((prev: ITodoRecord | null) =>
        prev ? { ...prev, ...fields } : prev,
      );

      // Persist to Dataverse in background
      const result = await saveTodoFields(todoId, fields);
      if (!result.success) {
        // Rollback optimistic update on failure
        if (previousItem) {
          updateItem(todoId, previousItem);
          setRecord((prev: ITodoRecord | null) =>
            prev ? { ...prev, ...previousItem } : prev,
          );
        }
        console.error(
          "[TodoDetailPanel] Todo save failed, reverted:",
          result.error,
        );
      }
      return result;
    },
    [updateItem, items],
  );

  // ---------------------------------------------------------------------------
  // Dismiss handler — set statecode=1 / statuscode=Dismissed + remove from
  // Kanban + close panel. Preserves the record so it can be recovered from
  // the DismissedSection (per task 011 follow-up note default behaviour).
  // ---------------------------------------------------------------------------
  const handleDismissTodo = React.useCallback(
    async (todoId: string) => {
      // Optimistic: drop from Kanban + close panel immediately
      const previousItem = items.find((i) => i.sprk_todoid === todoId);

      const result = await dismissTodo(todoId);
      if (result.success) {
        handleRemove(todoId);
        selectItem(null);
      } else if (previousItem) {
        // No rollback needed for the item itself (we hadn't removed it yet),
        // but log the failure for the host so it can surface an error.
        console.error(
          "[TodoDetailPanel] Dismiss failed:",
          result.error,
        );
      }
      return result;
    },
    [handleRemove, selectItem, items],
  );

  // ---------------------------------------------------------------------------
  // Search systemusers for the Assigned To picker
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
  // Suppress unused-warning for STATUSCODE_DISMISSED / STATECODE_INACTIVE —
  // they're declared at module scope for documentation of the semantic chosen
  // by `dismissTodo` (statecode=1 / statuscode=659490002).
  void STATUSCODE_DISMISSED;
  void STATECODE_INACTIVE;

  return (
    <div
      className={styles.root}
      role="complementary"
      aria-label="To-do detail panel"
    >
      {/* Header with todo name and close button */}
      <div className={styles.header}>
        <Text weight="semibold" size={400} className={styles.headerTitle}>
          {record?.sprk_name ?? "To Do Details"}
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
          isLoading={false}
          error={null}
          onSaveTodo={handleSaveTodo}
          onDismissTodo={handleDismissTodo}
          onClose={handleClose}
          onSearchContacts={handleSearchContacts}
          onOpenRegardingRecord={handleOpenRegardingRecord}
        />
      </div>
    </div>
  );
}
