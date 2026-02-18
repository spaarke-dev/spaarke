/**
 * FeedTodoSyncContext — shared flag state between the Updates Feed (Block 3)
 * and the Smart To Do list (Block 4).
 *
 * Architecture:
 *   - Maintains a Map<eventId, boolean> of flag states derived from Dataverse.
 *   - Provides optimistic toggle: UI updates immediately; Dataverse write happens
 *     asynchronously. On failure the optimistic update is rolled back.
 *   - Debounces rapid toggles (300 ms) so a quick double-click only issues one
 *     Dataverse write.
 *   - Exposes a subscribe() mechanism so Block 4 can react to flag changes from
 *     Block 3 without prop-drilling or a full re-render of the tree.
 *   - Uses useReducer for predictable state transitions on the flag map, pending
 *     writes set, and per-event error strings.
 *
 * NFR-08 compliance: Dataverse write is issued within the 300 ms debounce window,
 * so the write completes well within the 1-second requirement.
 *
 * Provider placement: mount FeedTodoSyncProvider at LegalWorkspaceApp level so
 * both Block 3 and Block 4 can share the same context instance.
 *
 * Usage:
 *   // Wrap at app level
 *   <FeedTodoSyncProvider webApi={webApi}>
 *     <WorkspaceGrid ... />
 *   </FeedTodoSyncProvider>
 *
 *   // Consume in child components via hook
 *   const { isFlagged, toggleFlag, isPending, getError } = useFeedTodoSync();
 */

import * as React from 'react';
import { DataverseService } from '../services/DataverseService';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Internal reducer state holding all flag-related data.
 *
 * flags:        eventId → current flag state (optimistically updated)
 * pendingWrites: set of eventIds currently being written to Dataverse
 * errors:        eventId → user-facing error string (cleared on next toggle)
 */
interface IFlagState {
  flags: Map<string, boolean>;
  pendingWrites: Set<string>;
  errors: Map<string, string>;
}

type FlagAction =
  | { type: 'TOGGLE_FLAG'; eventId: string; flagged: boolean }
  | { type: 'WRITE_PENDING'; eventId: string }
  | { type: 'WRITE_SUCCESS'; eventId: string }
  | { type: 'WRITE_FAILURE'; eventId: string; previousState: boolean; error: string }
  | { type: 'CLEAR_ERROR'; eventId: string }
  | { type: 'BULK_INIT'; flags: Map<string, boolean> };

/**
 * Public API exposed by the context.
 */
export interface IFeedTodoSyncContextValue {
  /**
   * Returns the current flag state for an event.
   * Falls back to false for unknown eventIds (safe default).
   *
   * NOTE: This function reads from React state (not a ref), so components
   * that call isFlagged() will re-render when the flags Map changes.
   * Use this in render paths; use the subscriber pattern for side-effects.
   */
  isFlagged: (eventId: string) => boolean;

  /**
   * Toggle the flag for an event.
   * Applies optimistic update immediately; issues Dataverse write after debounce.
   * Rolls back on failure and records an error message.
   * @returns Promise that resolves when the Dataverse write completes (or fails).
   */
  toggleFlag: (eventId: string) => Promise<void>;

  /**
   * Returns the count of currently flagged events.
   * Useful for Block 4's badge count.
   *
   * NOTE: Reads from React state — re-renders consumers when count changes.
   */
  getFlaggedCount: () => number;

  /**
   * Returns true if a Dataverse write is in-flight for the given eventId.
   * Consumers can show a loading indicator while pending.
   *
   * NOTE: Reads from React state — re-renders consumers when pending set changes.
   */
  isPending: (eventId: string) => boolean;

  /**
   * Returns the error string for an eventId, or undefined if no error.
   *
   * NOTE: Reads from React state — re-renders consumers when errors change.
   */
  getError: (eventId: string) => string | undefined;

  /**
   * Subscribe to flag change notifications.
   *
   * Listener fires after WRITE_SUCCESS (confirmed persist) or TOGGLE_FLAG
   * (optimistic) depending on consumer needs. Currently fires on optimistic update
   * so Block 4 refreshes immediately.
   *
   * Use this for side-effects (e.g. inserting a new item into a list).
   * For render updates, rely on isFlagged() re-rendering via React state.
   *
   * @param listener - Called with (eventId, newFlagState) on every change
   * @returns Unsubscribe function — call on component unmount
   */
  subscribe: (listener: (eventId: string, flagged: boolean) => void) => () => void;

  /**
   * Seed the context with initial flag states loaded from Dataverse.
   * Called by the ActivityFeed once events are fetched so the context
   * reflects persisted state before any user interaction.
   *
   * @param events - Array of objects with at minimum { sprk_eventid, sprk_todoflag }
   */
  initFlags: (events: Array<{ sprk_eventid: string; sprk_todoflag: boolean }>) => void;

  /**
   * The current flags Map exposed as React state for reactive consumers.
   *
   * Exposed so that components needing granular re-render control can subscribe
   * to changes via this value (Map identity changes on every state update).
   * Most consumers should use isFlagged() instead.
   *
   * @internal — primarily for testing and advanced consumers
   */
  _flagsSnapshot: ReadonlyMap<string, boolean>;
}

// ---------------------------------------------------------------------------
// Context
// ---------------------------------------------------------------------------

const FeedTodoSyncContext = React.createContext<IFeedTodoSyncContextValue | null>(null);
FeedTodoSyncContext.displayName = 'FeedTodoSyncContext';

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

function flagReducer(state: IFlagState, action: FlagAction): IFlagState {
  switch (action.type) {
    case 'BULK_INIT': {
      // Merge incoming flags — do not overwrite flags that are already pending
      const mergedFlags = new Map(state.flags);
      action.flags.forEach((value, key) => {
        if (!state.pendingWrites.has(key)) {
          mergedFlags.set(key, value);
        }
      });
      return { ...state, flags: mergedFlags };
    }

    case 'TOGGLE_FLAG': {
      const nextFlags = new Map(state.flags);
      nextFlags.set(action.eventId, action.flagged);
      // Clear any stale error when the user intentionally retries
      const nextErrors = new Map(state.errors);
      nextErrors.delete(action.eventId);
      return { ...state, flags: nextFlags, errors: nextErrors };
    }

    case 'WRITE_PENDING': {
      const nextPending = new Set(state.pendingWrites);
      nextPending.add(action.eventId);
      return { ...state, pendingWrites: nextPending };
    }

    case 'WRITE_SUCCESS': {
      const nextPending = new Set(state.pendingWrites);
      nextPending.delete(action.eventId);
      return { ...state, pendingWrites: nextPending };
    }

    case 'WRITE_FAILURE': {
      // Roll back the optimistic flag state
      const nextFlags = new Map(state.flags);
      nextFlags.set(action.eventId, action.previousState);
      const nextPending = new Set(state.pendingWrites);
      nextPending.delete(action.eventId);
      const nextErrors = new Map(state.errors);
      nextErrors.set(action.eventId, action.error);
      return { ...state, flags: nextFlags, pendingWrites: nextPending, errors: nextErrors };
    }

    case 'CLEAR_ERROR': {
      const nextErrors = new Map(state.errors);
      nextErrors.delete(action.eventId);
      return { ...state, errors: nextErrors };
    }

    default:
      return state;
  }
}

const INITIAL_STATE: IFlagState = {
  flags: new Map(),
  pendingWrites: new Set(),
  errors: new Map(),
};

// ---------------------------------------------------------------------------
// Debounce helper
// ---------------------------------------------------------------------------

/** Debounce timeout tracker: eventId → NodeJS.Timeout handle */
const _debounceTimers = new Map<string, ReturnType<typeof setTimeout>>();

/** Debounce delay in milliseconds — satisfies NFR-08 (< 1 second) */
const DEBOUNCE_DELAY_MS = 300;

// ---------------------------------------------------------------------------
// Provider
// ---------------------------------------------------------------------------

export interface IFeedTodoSyncProviderProps {
  /** Xrm.WebApi from PCF context — used to construct DataverseService */
  webApi: ComponentFramework.WebApi;
  children: React.ReactNode;
}

export const FeedTodoSyncProvider: React.FC<IFeedTodoSyncProviderProps> = ({
  webApi,
  children,
}) => {
  const [state, dispatch] = React.useReducer(flagReducer, INITIAL_STATE);

  // Stable DataverseService reference — recreated only when webApi changes
  const serviceRef = React.useRef<DataverseService>(new DataverseService(webApi));
  React.useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // Subscriber registry — listeners receive (eventId, flagged) on optimistic toggle
  const subscribersRef = React.useRef<Set<(eventId: string, flagged: boolean) => void>>(
    new Set()
  );

  // Keep a ref to the current state so async closures always see latest flags
  const stateRef = React.useRef(state);
  React.useEffect(() => {
    stateRef.current = state;
  }, [state]);

  // -------------------------------------------------------------------------
  // initFlags — seed context from loaded event list
  // -------------------------------------------------------------------------

  const initFlags = React.useCallback(
    (events: Array<{ sprk_eventid: string; sprk_todoflag: boolean }>) => {
      const flagMap = new Map<string, boolean>();
      events.forEach((e) => {
        flagMap.set(e.sprk_eventid, e.sprk_todoflag);
      });
      dispatch({ type: 'BULK_INIT', flags: flagMap });
    },
    []
  );

  // -------------------------------------------------------------------------
  // toggleFlag — optimistic update + debounced Dataverse write
  // -------------------------------------------------------------------------

  const toggleFlag = React.useCallback(
    async (eventId: string): Promise<void> => {
      const currentFlagged = stateRef.current.flags.get(eventId) ?? false;
      const newFlagged = !currentFlagged;

      // Optimistic update — renders immediately
      dispatch({ type: 'TOGGLE_FLAG', eventId, flagged: newFlagged });

      // Notify subscribers synchronously after optimistic update
      subscribersRef.current.forEach((listener) => listener(eventId, newFlagged));

      // Cancel any pending debounce for this eventId (rapid toggle protection)
      const existingTimer = _debounceTimers.get(eventId);
      if (existingTimer !== undefined) {
        clearTimeout(existingTimer);
      }

      // Issue Dataverse write after debounce window
      return new Promise<void>((resolve) => {
        const timer = setTimeout(async () => {
          _debounceTimers.delete(eventId);

          // Read the FINAL desired state at write time (user may have toggled again)
          const finalFlagged = stateRef.current.flags.get(eventId) ?? newFlagged;

          dispatch({ type: 'WRITE_PENDING', eventId });

          const result = await serviceRef.current.toggleTodoFlag(eventId, finalFlagged);

          if (result.success) {
            dispatch({ type: 'WRITE_SUCCESS', eventId });
          } else {
            // Roll back to the state BEFORE the last optimistic update
            const previousState = !finalFlagged;
            const errorMessage =
              result.error?.message ?? 'Failed to update flag. Please try again.';
            dispatch({ type: 'WRITE_FAILURE', eventId, previousState, error: errorMessage });
            // Notify subscribers of the rollback
            subscribersRef.current.forEach((listener) => listener(eventId, previousState));
          }

          resolve();
        }, DEBOUNCE_DELAY_MS);

        _debounceTimers.set(eventId, timer);
      });
    },
    [] // intentionally empty — uses refs for current state and service
  );

  // -------------------------------------------------------------------------
  // Public API helpers
  //
  // IMPORTANT: isFlagged, getFlaggedCount, isPending, and getError read from
  // the React `state` object (not the ref) so that components calling them
  // inside their render path will be scheduled for a re-render whenever the
  // relevant slice of state changes.
  //
  // The `stateRef` is still used inside async closures (toggleFlag debounce
  // callback) where we need the latest value without a re-render cycle.
  // -------------------------------------------------------------------------

  const isFlagged = React.useCallback(
    (eventId: string): boolean => state.flags.get(eventId) ?? false,
    // Re-creates when state.flags Map identity changes — happens on every
    // TOGGLE_FLAG / BULK_INIT / WRITE_FAILURE reducer action.
    [state.flags]
  );

  const getFlaggedCount = React.useCallback(
    (): number => {
      let count = 0;
      state.flags.forEach((flagged) => {
        if (flagged) count++;
      });
      return count;
    },
    [state.flags]
  );

  const isPending = React.useCallback(
    (eventId: string): boolean => state.pendingWrites.has(eventId),
    [state.pendingWrites]
  );

  const getError = React.useCallback(
    (eventId: string): string | undefined => state.errors.get(eventId),
    [state.errors]
  );

  const subscribe = React.useCallback(
    (listener: (eventId: string, flagged: boolean) => void): (() => void) => {
      subscribersRef.current.add(listener);
      return () => {
        subscribersRef.current.delete(listener);
      };
    },
    []
  );

  // -------------------------------------------------------------------------
  // Context value — stable reference via useMemo
  //
  // The value object is re-created when any of the state-derived callbacks
  // change their identity (i.e. when flags / pendingWrites / errors change).
  // This propagates reactive updates to all consumers of the context.
  // -------------------------------------------------------------------------

  const contextValue = React.useMemo<IFeedTodoSyncContextValue>(
    () => ({
      isFlagged,
      toggleFlag,
      getFlaggedCount,
      isPending,
      getError,
      subscribe,
      initFlags,
      _flagsSnapshot: state.flags,
    }),
    [isFlagged, toggleFlag, getFlaggedCount, isPending, getError, subscribe, initFlags, state.flags]
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
