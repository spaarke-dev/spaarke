/**
 * FeedTodoSyncContext — Cross-block todo-lifecycle notification bus.
 *
 * Purpose (R3 FR-14):
 *   Provides a lightweight pub/sub channel that the LegalWorkspace ActivityFeed
 *   block, the SmartToDo block, and the embedded TodoDetail panel use to stay
 *   in sync when a `sprk_todo` record changes. When a todo is created,
 *   updated, dismissed, completed, restored, or hard-deleted in one block, the
 *   producer calls `notifyTodoChange(todoId, isActive)` and every subscriber
 *   reacts within the next render cycle.
 *
 * Semantics:
 *   - `todoId`     — a `sprk_todoid` GUID (NOT a `sprk_eventid`).
 *   - `isActive`   — true when the todo is currently Open / In Progress
 *                    (statecode = Active), false when the todo became
 *                    Completed / Dismissed / deleted.
 *
 * R3 background (OS-1 — no event-id back-compat):
 *   This file replaces the pre-R3 implementation that managed
 *   `Map<eventId, boolean>` for `sprk_event.sprk_todoflag` toggles. In R3 the
 *   flag column on `sprk_event` is being deleted and "To Do" is its own
 *   first-class entity, so the cross-block contract changed to a simple
 *   notification bus. There is no event-id compat path.
 *
 * Architectural decisions:
 *   - Stateless context. No `Map<id, state>` is held in React state —
 *     producers own persistence (Dataverse / Web API). The context only
 *     forwards notifications.
 *   - No debounce, no rollback. Persistence concerns live in the producer
 *     (e.g. TodoDetailPanel optimistic save, SmartToDo dismiss handler).
 *   - Synchronous fan-out via a stable subscriber Set. Listeners run inside
 *     the producer's effect chain so consumers receive the event in the
 *     same React render commit (FR-14 acceptance: one-render-cycle update).
 *   - Context-agnostic per ADR-012: zero Xrm / PCF coupling here. Producers
 *     supply the IDs; consumers decide how to refetch / refresh.
 *
 * Provider placement: mount FeedTodoSyncProvider at LegalWorkspaceApp level
 * so ActivityFeed (Block 3), SmartToDo widget (Block 4), and TodoDetailPane
 * all share the same context instance.
 *
 * Usage:
 *   // Wrap at app level (no `webApi` needed — context owns no persistence)
 *   <FeedTodoSyncProvider>
 *     <WorkspaceGrid ... />
 *   </FeedTodoSyncProvider>
 *
 *   // Producer (e.g. SmartToDo handleDismiss)
 *   const { notifyTodoChange } = useFeedTodoSync();
 *   await dataverse.dismissTodo(todoId);
 *   notifyTodoChange(todoId, false); // became inactive
 *
 *   // Consumer (e.g. useTodoItems subscription)
 *   const { subscribe } = useFeedTodoSync();
 *   React.useEffect(() => subscribe((todoId, isActive) => {
 *     // Refresh local state for this todoId based on isActive
 *   }), [subscribe]);
 */

import * as React from 'react';

// ---------------------------------------------------------------------------
// Public API contract (R3 FR-14)
// ---------------------------------------------------------------------------

/**
 * Listener callback for todo-lifecycle change notifications.
 *
 * @param todoId   - `sprk_todoid` GUID of the todo that changed.
 * @param isActive - true when the todo is currently active (Open / In Progress);
 *                   false when it became Completed, Dismissed, or was deleted.
 */
export type FeedTodoSyncListener = (todoId: string, isActive: boolean) => void;

/**
 * Public API exposed by the context.
 *
 * The shape is intentionally minimal — a notification bus only. Persistence
 * (Dataverse writes) is the producer's responsibility; this context just
 * forwards lifecycle events to subscribers.
 */
export interface IFeedTodoSyncContextValue {
  /**
   * Broadcast that a `sprk_todo` record's active state changed.
   *
   * Producers call this AFTER the corresponding Dataverse write succeeds
   * (or optimistically — at the producer's discretion). All currently
   * registered subscribers fire synchronously in registration order.
   *
   * @param todoId   - `sprk_todoid` GUID that changed.
   * @param isActive - true if the todo is now Open / In Progress; false if
   *                   Completed / Dismissed / deleted.
   */
  notifyTodoChange: (todoId: string, isActive: boolean) => void;

  /**
   * Subscribe to todo-lifecycle change notifications.
   *
   * Listeners are invoked synchronously from `notifyTodoChange`, so any
   * `setState` calls inside the listener are batched into the producer's
   * render cycle — satisfying FR-14's one-render-cycle propagation rule.
   *
   * @param listener - Called with `(todoId, isActive)` on every change.
   * @returns Unsubscribe function — call on component unmount.
   */
  subscribe: (listener: FeedTodoSyncListener) => () => void;
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const FeedTodoSyncContext = React.createContext<IFeedTodoSyncContextValue | null>(null);
FeedTodoSyncContext.displayName = 'FeedTodoSyncContext';

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export interface IFeedTodoSyncProviderProps {
  children: React.ReactNode;
}

/**
 * FeedTodoSyncProvider — Mount once at LegalWorkspaceApp level.
 *
 * Owns the subscriber registry. Does not own any todo state — producers
 * persist via their own Dataverse callers and notify via `notifyTodoChange`.
 */
export const FeedTodoSyncProvider: React.FC<IFeedTodoSyncProviderProps> = ({
  children,
}) => {
  // Subscriber registry — stable across renders via useRef.
  // A Set guarantees O(1) add/remove and uniqueness per listener identity.
  const subscribersRef = React.useRef<Set<FeedTodoSyncListener>>(new Set());

  const notifyTodoChange = React.useCallback(
    (todoId: string, isActive: boolean): void => {
      // Synchronous fan-out. Listeners that call setState run inside the
      // current React render commit (FR-14: one-render-cycle propagation).
      subscribersRef.current.forEach((listener) => {
        try {
          listener(todoId, isActive);
        } catch (err) {
          // A misbehaving subscriber must not break the broadcast loop —
          // log and continue.
          // eslint-disable-next-line no-console
          console.error('[FeedTodoSyncContext] subscriber threw:', err);
        }
      });
    },
    []
  );

  const subscribe = React.useCallback(
    (listener: FeedTodoSyncListener): (() => void) => {
      subscribersRef.current.add(listener);
      return () => {
        subscribersRef.current.delete(listener);
      };
    },
    []
  );

  // Stable context value — both callbacks are useRef-backed and never change
  // identity, so `useMemo` here only re-creates the wrapper object on first
  // mount.
  const contextValue = React.useMemo<IFeedTodoSyncContextValue>(
    () => ({
      notifyTodoChange,
      subscribe,
    }),
    [notifyTodoChange, subscribe]
  );

  return (
    <FeedTodoSyncContext.Provider value={contextValue}>
      {children}
    </FeedTodoSyncContext.Provider>
  );
};

// ---------------------------------------------------------------------------
// Raw context export for useFeedTodoSync hook
// ---------------------------------------------------------------------------

export { FeedTodoSyncContext };
