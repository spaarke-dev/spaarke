/**
 * useFeedTodoSync — convenient hook to access FeedTodoSyncContext.
 *
 * Throws a descriptive error if consumed outside FeedTodoSyncProvider,
 * making misconfiguration immediately visible during development.
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
 * Access the FeedTodoSyncContext value.
 *
 * @returns IFeedTodoSyncContextValue — the full context API
 * @throws  Error if called outside of FeedTodoSyncProvider
 */
export function useFeedTodoSync(): IFeedTodoSyncContextValue {
  const ctx = useContext(FeedTodoSyncContext);

  if (ctx === null) {
    throw new Error(
      'useFeedTodoSync must be used within a <FeedTodoSyncProvider>. ' +
        'Ensure FeedTodoSyncProvider wraps both Block 3 (ActivityFeed) and ' +
        'Block 4 (SmartToDo) in the component tree, typically at LegalWorkspaceApp level.'
    );
  }

  return ctx;
}
