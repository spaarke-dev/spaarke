/**
 * TodoContext — Shared state provider for the SmartTodo Code Page.
 *
 * Aggregates item data, selection state, and action callbacks into a single
 * React context consumed by both the KanbanBoard and TodoDetailPanel.
 *
 * Key behaviors:
 *   - selectItem toggles: clicking the same eventId deselects (sets null).
 *   - updateItem performs an optimistic partial merge into the items array
 *     without triggering a full refetch.
 *   - Action callbacks (handleStatusToggle, handleDismiss, handleRestore,
 *     handleAdd, handleRemove) manage optimistic state with rollback stubs.
 *   - Data fetching is placeholder — hooks from task 022 will be wired later.
 *
 * Constraints:
 *   - React 19 APIs (Code Page, not PCF)
 *   - No direct Xrm or Dataverse calls — accepts callbacks via provider props
 *   - Fluent v9 compatible (no styling in context)
 */

import * as React from "react";
import type { IEvent } from "../types/entities";

// ---------------------------------------------------------------------------
// Context value type
// ---------------------------------------------------------------------------

export interface TodoContextValue {
  /** Active to-do items. */
  items: IEvent[];
  /** Items that have been dismissed (hidden from the Kanban board). */
  dismissedItems: IEvent[];
  /** Whether the initial data load is in progress. */
  isLoading: boolean;
  /** Error message from the most recent fetch, or null. */
  error: string | null;

  /** Currently selected event ID, or null if nothing is selected. */
  selectedEventId: string | null;
  /**
   * Toggle selection: calling with the currently-selected ID deselects (null);
   * calling with a different ID (or null) sets that as the selection.
   */
  selectItem: (eventId: string | null) => void;

  /**
   * Optimistically merge partial fields into the matching item in the items
   * array. Does NOT trigger a refetch — the caller is responsible for
   * persisting the change to Dataverse.
   */
  updateItem: (eventId: string, partial: Partial<IEvent>) => void;

  /** Trigger a full data refetch. */
  refetch: () => void;
  /** Toggle an item between Open and Completed with optimistic update. */
  handleStatusToggle: (eventId: string) => void;
  /** Dismiss an item (move to dismissed list optimistically). */
  handleDismiss: (eventId: string) => void;
  /** Restore a dismissed item back to the active list. */
  handleRestore: (eventId: string) => void;
  /** Add a new item to the active list. */
  handleAdd: (newItem: IEvent) => void;
  /** Remove an item from the active list by eventId. */
  handleRemove: (eventId: string) => void;
}

// ---------------------------------------------------------------------------
// Context (default throws to catch missing provider)
// ---------------------------------------------------------------------------

const TodoContext = React.createContext<TodoContextValue | null>(null);
TodoContext.displayName = "TodoContext";

// ---------------------------------------------------------------------------
// Provider props
// ---------------------------------------------------------------------------

export interface TodoProviderProps {
  children: React.ReactNode;
  /**
   * Project (or matter) ID used to scope which events are fetched.
   * Placeholder — actual fetching will be wired in task 022.
   */
  projectId?: string;
  /** Optional initial items for testing or pre-loaded data. */
  initialItems?: IEvent[];
}

// ---------------------------------------------------------------------------
// Status constants (Dataverse choice values)
// ---------------------------------------------------------------------------

const TODO_STATUS_OPEN = 100000000;
const TODO_STATUS_COMPLETED = 100000001;

// ---------------------------------------------------------------------------
// Provider component
// ---------------------------------------------------------------------------

export function TodoProvider({
  children,
  initialItems = [],
}: TodoProviderProps) {
  // ── Core data state ──────────────────────────────────────────────────────
  const [items, setItems] = React.useState<IEvent[]>(initialItems);
  const [dismissedItems, setDismissedItems] = React.useState<IEvent[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Sync items when initialItems changes (e.g. after hook wiring in task 022)
  React.useEffect(() => {
    setItems(initialItems);
  }, [initialItems]);

  // ── Selection state ──────────────────────────────────────────────────────
  const [selectedEventId, setSelectedEventId] = React.useState<string | null>(
    null,
  );

  const selectItem = React.useCallback((eventId: string | null) => {
    setSelectedEventId((prev) => {
      // Toggle: clicking the same ID deselects
      if (eventId !== null && prev === eventId) return null;
      return eventId;
    });
  }, []);

  // ── Optimistic single-item merge ─────────────────────────────────────────
  const updateItem = React.useCallback(
    (eventId: string, partial: Partial<IEvent>) => {
      setItems((prev) =>
        prev.map((item) =>
          item.sprk_eventid === eventId ? { ...item, ...partial } : item,
        ),
      );
    },
    [],
  );

  // ── Refetch placeholder ──────────────────────────────────────────────────
  // Will be replaced with actual data fetching when hooks are wired (task 022).
  const refetch = React.useCallback(() => {
    // Placeholder: in production this will call the data-fetching hook's
    // refetch function. For now, no-op.
    setIsLoading(true);
    // Simulate async completion
    queueMicrotask(() => setIsLoading(false));
  }, []);

  // ── Status toggle (Open <-> Completed) ───────────────────────────────────
  const handleStatusToggle = React.useCallback((eventId: string) => {
    setItems((prev) => {
      const target = prev.find((i) => i.sprk_eventid === eventId);
      if (!target) return prev;

      const newStatus =
        target.sprk_todostatus === TODO_STATUS_COMPLETED
          ? TODO_STATUS_OPEN
          : TODO_STATUS_COMPLETED;

      return prev.map((item) =>
        item.sprk_eventid === eventId
          ? { ...item, sprk_todostatus: newStatus }
          : item,
      );
    });
    // Persistence and rollback will be wired in task 022 via the data hooks.
  }, []);

  // ── Dismiss (move item from active to dismissed) ─────────────────────────
  const handleDismiss = React.useCallback((eventId: string) => {
    setItems((prev) => {
      const target = prev.find((i) => i.sprk_eventid === eventId);
      if (!target) return prev;

      // Optimistically move to dismissed list
      setDismissedItems((dismissed) => [target, ...dismissed]);
      return prev.filter((i) => i.sprk_eventid !== eventId);
    });
    // Deselect if the dismissed item was selected
    setSelectedEventId((prev) => (prev === eventId ? null : prev));
  }, []);

  // ── Restore (move item from dismissed back to active) ────────────────────
  const handleRestore = React.useCallback((eventId: string) => {
    setDismissedItems((prev) => {
      const target = prev.find((i) => i.sprk_eventid === eventId);
      if (!target) return prev;

      // Restore to active list with Open status
      setItems((items) => [
        ...items,
        { ...target, sprk_todostatus: TODO_STATUS_OPEN },
      ]);
      return prev.filter((i) => i.sprk_eventid !== eventId);
    });
  }, []);

  // ── Add new item ─────────────────────────────────────────────────────────
  const handleAdd = React.useCallback((newItem: IEvent) => {
    setItems((prev) => [...prev, newItem]);
  }, []);

  // ── Remove item ──────────────────────────────────────────────────────────
  const handleRemove = React.useCallback((eventId: string) => {
    setItems((prev) => prev.filter((i) => i.sprk_eventid !== eventId));
    setSelectedEventId((prev) => (prev === eventId ? null : prev));
  }, []);

  // ── Context value (stable reference via useMemo) ─────────────────────────
  const value = React.useMemo<TodoContextValue>(
    () => ({
      items,
      dismissedItems,
      isLoading,
      error,
      selectedEventId,
      selectItem,
      updateItem,
      refetch,
      handleStatusToggle,
      handleDismiss,
      handleRestore,
      handleAdd,
      handleRemove,
    }),
    [
      items,
      dismissedItems,
      isLoading,
      error,
      selectedEventId,
      selectItem,
      updateItem,
      refetch,
      handleStatusToggle,
      handleDismiss,
      handleRestore,
      handleAdd,
      handleRemove,
    ],
  );

  return <TodoContext.Provider value={value}>{children}</TodoContext.Provider>;
}

// ---------------------------------------------------------------------------
// Consumer hook
// ---------------------------------------------------------------------------

/**
 * Access the TodoContext. Must be called within a `<TodoProvider>`.
 * Throws if the provider is missing — catches wiring errors early.
 */
export function useTodoContext(): TodoContextValue {
  const ctx = React.useContext(TodoContext);
  if (!ctx) {
    throw new Error(
      "useTodoContext must be used within a <TodoProvider>. " +
        "Wrap your component tree with <TodoProvider>.",
    );
  }
  return ctx;
}
