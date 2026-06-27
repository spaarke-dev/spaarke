/**
 * useFeedTodoSync — convenient hook to access FeedTodoSyncContext.
 *
 * Returns a no-op fallback when consumed outside FeedTodoSyncProvider so any
 * consumer (ActivityFeed, SmartToDo widget, TodoDetail panel, standalone
 * surfaces) can render independently without crashing or branching on
 * provider presence.
 *
 * R3 contract (FR-14 / OS-1):
 *   - Notifications carry `todoId` (sprk_todoid) + `isActive` boolean.
 *   - The legacy `(eventId, flagged)` shape backed by `Map<eventId, boolean>`
 *     is removed in R3 — no compat shim.
 *
 * Usage:
 *   // Producer (after a successful Dataverse mutation):
 *   const { notifyTodoChange } = useFeedTodoSync();
 *   await dataverse.dismissTodo(todoId);
 *   notifyTodoChange(todoId, false);
 *
 *   // Consumer (subscribe to cross-block changes):
 *   const { subscribe } = useFeedTodoSync();
 *   React.useEffect(
 *     () => subscribe((todoId, isActive) => {
 *       // Refresh / insert / remove based on isActive
 *     }),
 *     [subscribe]
 *   );
 */

import { useContext } from 'react';
import {
  FeedTodoSyncContext,
  IFeedTodoSyncContextValue,
} from '../contexts/FeedTodoSyncContext';

/**
 * No-op fallback returned when the hook is consumed outside of a
 * FeedTodoSyncProvider. Calls to `notifyTodoChange` are silently dropped and
 * `subscribe` registers a listener that never fires — so consumers can be
 * mounted in surfaces that don't host the provider (e.g. the standalone
 * SmartTodo Code Page) without needing null checks.
 */
const NOOP_SYNC: IFeedTodoSyncContextValue = {
  notifyTodoChange: () => {},
  subscribe: () => () => {},
};

/**
 * Access the FeedTodoSyncContext value.
 *
 * When a FeedTodoSyncProvider is present in the tree the real context is
 * returned. Otherwise a safe no-op fallback ({@link NOOP_SYNC}) is returned.
 *
 * @returns IFeedTodoSyncContextValue — the full notification-bus API.
 */
export function useFeedTodoSync(): IFeedTodoSyncContextValue {
  const ctx = useContext(FeedTodoSyncContext);
  return ctx ?? NOOP_SYNC;
}
