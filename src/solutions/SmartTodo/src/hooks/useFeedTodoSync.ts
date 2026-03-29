/**
 * useFeedTodoSync — No-op stub for the SmartTodo Code Page.
 *
 * In the LegalWorkspace, this hook connects to FeedTodoSyncContext to
 * synchronise flag toggles between the Updates Feed and the Smart To Do
 * list. The standalone SmartTodo Code Page does not have an Updates Feed,
 * so this stub provides a subscribe function that never fires.
 */

export interface IFeedTodoSyncContextValue {
  isFlagged: (eventId: string) => boolean;
  toggleFlag: (eventId: string) => void;
  isPending: (eventId: string) => boolean;
  getError: (eventId: string) => string | null;
  subscribe: (
    callback: (eventId: string, flagged: boolean) => void
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
