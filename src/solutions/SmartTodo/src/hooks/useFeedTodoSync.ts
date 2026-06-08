/**
 * useFeedTodoSync — No-op stub for the SmartTodo Code Page.
 *
 * In LegalWorkspace, this hook connects to FeedTodoSyncContext to synchronise
 * todo lifecycle events between the Updates Feed and the Smart To Do list.
 * The standalone SmartTodo Code Page does not have an Updates Feed, so this
 * stub provides a subscribe function that never fires.
 *
 * Per R3 FR-14 / task 023 (forthcoming), the `subscribe` callback delivers
 * `(todoId, isActive)` — todoId is a sprk_todoid; isActive=true when the todo
 * just became Open/In-Progress, false when it became Inactive (Completed or
 * Dismissed) or was deleted. The legacy `(eventId, flagged)` shape is gone.
 *
 * The legacy id-based methods (isFlagged / toggleFlag / isPending / getError)
 * are retained as no-ops for API compatibility with LegalWorkspace's
 * FeedTodoSyncContext until the formal payload update in task 023.
 */

export interface IFeedTodoSyncContextValue {
  isFlagged: (todoId: string) => boolean;
  toggleFlag: (todoId: string) => void;
  isPending: (todoId: string) => boolean;
  getError: (todoId: string) => string | null;
  subscribe: (
    callback: (todoId: string, isActive: boolean) => void
  ) => () => void;
}

/**
 * No-op stub: returns a subscribe function that does nothing.
 * The SmartTodo Code Page operates independently without the Updates Feed.
 */
export function useFeedTodoSync(): IFeedTodoSyncContextValue {
  return {
    isFlagged: () => false,
    toggleFlag: () => {},
    isPending: () => false,
    getError: () => null,
    subscribe: () => () => {},
  };
}
