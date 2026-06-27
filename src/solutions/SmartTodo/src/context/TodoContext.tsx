/**
 * TodoContext — Shared state provider for the SmartTodo Code Page.
 *
 * Aggregates item data, selection state, and action callbacks into a single
 * React context consumed by the KanbanBoard (and List view). Per R4 task 042 /
 * FR-18 the R3 `TodoDetailPanel` side-pane is retired; the hybrid
 * `<SmartTodoModal>` replaces it (R4 task 040 — see `../components/Modal/`).
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
import type { ITodo } from "../types/entities";

// ---------------------------------------------------------------------------
// Context value type
// ---------------------------------------------------------------------------

export interface TodoContextValue {
  /** Active to-do items. */
  items: ITodo[];
  /** Items that have been dismissed (hidden from the Kanban board). */
  dismissedItems: ITodo[];
  /** Whether the initial data load is in progress. */
  isLoading: boolean;
  /** Error message from the most recent fetch, or null. */
  error: string | null;

  /** Currently selected to-do ID (sprk_todoid), or null if nothing is selected. */
  selectedEventId: string | null;
  /**
   * Toggle selection: calling with the currently-selected ID deselects (null);
   * calling with a different ID (or null) sets that as the selection.
   */
  selectItem: (todoId: string | null) => void;

  /**
   * Optimistically merge partial fields into the matching item in the items
   * array. Does NOT trigger a refetch — the caller is responsible for
   * persisting the change to Dataverse.
   */
  updateItem: (todoId: string, partial: Partial<ITodo>) => void;

  /** Trigger a full data refetch. */
  refetch: () => void;
  /** Toggle an item between Open and Completed with optimistic update. */
  handleStatusToggle: (todoId: string) => void;
  /** Dismiss an item (move to dismissed list optimistically). */
  handleDismiss: (todoId: string) => void;
  /** Restore a dismissed item back to the active list. */
  handleRestore: (todoId: string) => void;
  /** Add a new item to the active list. */
  handleAdd: (newItem: ITodo) => void;
  /** Remove an item from the active list by todoId. */
  handleRemove: (todoId: string) => void;
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
   * Project (or matter) ID used to scope which todos are fetched.
   * Placeholder — actual fetching will be wired in task 022.
   */
  projectId?: string;
  /** Optional initial items for testing or pre-loaded data. */
  initialItems?: ITodo[];
}

// ---------------------------------------------------------------------------
// statuscode constants (sprk_todo per task 009 — see entity-schema.md)
// ---------------------------------------------------------------------------

const TODO_STATUSCODE_OPEN = 1;
const TODO_STATUSCODE_COMPLETED = 2;
const STATECODE_ACTIVE = 0;
const STATECODE_INACTIVE = 1;

// ---------------------------------------------------------------------------
// Provider component
// ---------------------------------------------------------------------------

export function TodoProvider({
  children,
  initialItems = [],
}: TodoProviderProps) {
  // ── Core data state ──────────────────────────────────────────────────────
  const [items, setItems] = React.useState<ITodo[]>(initialItems);
  const [dismissedItems, setDismissedItems] = React.useState<ITodo[]>([]);
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Sync items when initialItems prop changes (referential identity).
  // Skip the default empty array to avoid infinite re-render loops.
  const initialItemsRef = React.useRef(initialItems);
  React.useEffect(() => {
    if (initialItems !== initialItemsRef.current && initialItems.length > 0) {
      initialItemsRef.current = initialItems;
      setItems(initialItems);
    }
  }, [initialItems]);

  // ── Selection state ──────────────────────────────────────────────────────
  const [selectedEventId, setSelectedEventId] = React.useState<string | null>(
    null,
  );

  const selectItem = React.useCallback((todoId: string | null) => {
    setSelectedEventId((prev) => {
      // Toggle: clicking the same ID deselects
      if (todoId !== null && prev === todoId) return null;
      return todoId;
    });
  }, []);

  // ── Optimistic single-item merge ─────────────────────────────────────────
  const updateItem = React.useCallback(
    (todoId: string, partial: Partial<ITodo>) => {
      setItems((prev) =>
        prev.map((item) =>
          item.sprk_todoid === todoId ? { ...item, ...partial } : item,
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
  const handleStatusToggle = React.useCallback((todoId: string) => {
    setItems((prev) => {
      const target = prev.find((i) => i.sprk_todoid === todoId);
      if (!target) return prev;

      const isCompleted = target.statuscode === TODO_STATUSCODE_COMPLETED;
      const newStatuscode = isCompleted ? TODO_STATUSCODE_OPEN : TODO_STATUSCODE_COMPLETED;
      const newStatecode = isCompleted ? STATECODE_ACTIVE : STATECODE_INACTIVE;

      return prev.map((item) =>
        item.sprk_todoid === todoId
          ? { ...item, statuscode: newStatuscode, statecode: newStatecode }
          : item,
      );
    });
    // Persistence and rollback will be wired in task 022 via the data hooks.
  }, []);

  // ── Dismiss (move item from active to dismissed) ─────────────────────────
  const handleDismiss = React.useCallback((todoId: string) => {
    setItems((prev) => {
      const target = prev.find((i) => i.sprk_todoid === todoId);
      if (!target) return prev;

      // Optimistically move to dismissed list
      setDismissedItems((dismissed) => [target, ...dismissed]);
      return prev.filter((i) => i.sprk_todoid !== todoId);
    });
    // Deselect if the dismissed item was selected
    setSelectedEventId((prev) => (prev === todoId ? null : prev));
  }, []);

  // ── Restore (move item from dismissed back to active) ────────────────────
  const handleRestore = React.useCallback((todoId: string) => {
    setDismissedItems((prev) => {
      const target = prev.find((i) => i.sprk_todoid === todoId);
      if (!target) return prev;

      // Restore to active list with Open status
      setItems((items) => [
        ...items,
        { ...target, statuscode: TODO_STATUSCODE_OPEN, statecode: STATECODE_ACTIVE },
      ]);
      return prev.filter((i) => i.sprk_todoid !== todoId);
    });
  }, []);

  // ── Add new item ─────────────────────────────────────────────────────────
  const handleAdd = React.useCallback((newItem: ITodo) => {
    setItems((prev) => [...prev, newItem]);
  }, []);

  // ── Remove item ──────────────────────────────────────────────────────────
  const handleRemove = React.useCallback((todoId: string) => {
    setItems((prev) => prev.filter((i) => i.sprk_todoid !== todoId));
    setSelectedEventId((prev) => (prev === todoId ? null : prev));
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

/**
 * Access the TodoContext optionally. Returns null if no provider is present.
 * Use this in components that may render both inside and outside a TodoProvider
 * (e.g. SmartToDo used standalone vs. inside SmartTodoApp Code Page).
 */
export function useOptionalTodoContext(): TodoContextValue | null {
  return React.useContext(TodoContext);
}
