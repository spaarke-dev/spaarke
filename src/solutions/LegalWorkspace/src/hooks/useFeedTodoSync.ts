/**
 * useFeedTodoSync — convenient hook to access FeedTodoSyncContext.
 *
 * Returns a no-op fallback when consumed outside FeedTodoSyncProvider,
 * allowing SmartToDo (and other consumers) to work independently in any
 * context — workspace with ActivityFeed, without ActivityFeed, or
 * standalone Code Page.
 *
 * Usage:
 *   const { isFlagged, toggleFlag, isPending, getError } = useFeedTodoSync();
 *
 *   // In FeedItemCard — wire flag button:
 *   const flagged = isFlagged(event.sprk_eventid);
 *   const handleFlagClick = () => void toggleFlag(event.sprk_eventid);
 *
 *   // In SmartToDo (Block 4) — subscribe to cross-block changes:
 *   React.useEffect(() => {
 *     return subscribe((eventId, flagged) => {
 *       // Update local to-do list when feed flag changes
 *     });
 *   }, [subscribe]);
 */

import { useContext } from 'react';
import {
  FeedTodoSyncContext,
  IFeedTodoSyncContextValue,
} from '../contexts/FeedTodoSyncContext';

/**
 * No-op fallback returned when the hook is consumed outside of a
 * FeedTodoSyncProvider. Every method is a safe stub that does nothing,
 * so SmartToDo can render independently without crashing.
 */
const NOOP_SYNC: IFeedTodoSyncContextValue = {
  isFlagged: () => false,
  toggleFlag: async () => {},
  getFlaggedCount: () => 0,
  isPending: () => false,
  getError: () => undefined,
  subscribe: () => () => {},
  initFlags: () => {},
  _flagsSnapshot: new Map(),
};

/**
 * Access the FeedTodoSyncContext value.
 *
 * When a FeedTodoSyncProvider is present in the tree the real context is
 * returned. Otherwise a safe no-op fallback ({@link NOOP_SYNC}) is
 * returned so consumers do not need to guard against missing providers.
 *
 * @returns IFeedTodoSyncContextValue — the full context API (or no-op stubs)
 */
export function useFeedTodoSync(): IFeedTodoSyncContextValue {
  const ctx = useContext(FeedTodoSyncContext);
  return ctx ?? NOOP_SYNC;
}
