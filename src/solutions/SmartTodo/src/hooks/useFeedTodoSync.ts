/**
 * useFeedTodoSync — No-op stub for the standalone SmartTodo Code Page.
 *
 * In LegalWorkspace this hook resolves a real FeedTodoSyncContext that acts as
 * a cross-block todo-lifecycle notification bus between the Updates Feed
 * (Block 3) and the Smart To Do block (Block 4). The standalone SmartTodo
 * Code Page is mounted independently with no parent providers, so this stub
 * returns a safe no-op shape that mirrors the LegalWorkspace contract.
 *
 * R3 contract (FR-14 / OS-1): both methods carry `todoId` (sprk_todoid) +
 * `isActive` boolean. `isActive=true` when the todo is Open / In Progress;
 * `isActive=false` when Completed / Dismissed / deleted. The legacy
 * `(eventId, flagged)` shape has no compat path here.
 */

/**
 * Listener callback for todo-lifecycle change notifications.
 *
 * @param todoId   - `sprk_todoid` GUID of the todo that changed.
 * @param isActive - true when the todo is currently active (Open / In Progress);
 *                   false when it became Completed, Dismissed, or was deleted.
 */
export type FeedTodoSyncListener = (todoId: string, isActive: boolean) => void;

/**
 * Public API exposed by the cross-block todo-lifecycle notification bus.
 *
 * Mirrors `IFeedTodoSyncContextValue` from LegalWorkspace so consumers can
 * use the same hook signature regardless of host.
 */
export interface IFeedTodoSyncContextValue {
  /** Broadcast that a `sprk_todo` record's active state changed. */
  notifyTodoChange: (todoId: string, isActive: boolean) => void;
  /** Subscribe to todo-lifecycle change notifications. */
  subscribe: (listener: FeedTodoSyncListener) => () => void;
}

/**
 * No-op stub: notifyTodoChange is a sink; subscribe registers a listener
 * that never fires. The standalone SmartTodo Code Page operates independently
 * without any Updates Feed counterpart.
 */
export function useFeedTodoSync(): IFeedTodoSyncContextValue {
  return {
    notifyTodoChange: () => {},
    subscribe: () => () => {},
  };
}
