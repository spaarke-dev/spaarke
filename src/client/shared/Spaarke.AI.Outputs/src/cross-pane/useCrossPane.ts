/**
 * @spaarke/ai-outputs — Cross-Pane React Hooks
 *
 * React hooks that wrap the cross-pane DOM event infrastructure for use inside
 * React components. The output pane uses useDispatchCrossPaneLink to fire
 * link events; the source pane uses useCrossPaneSubscription to receive them.
 *
 * Both hooks are thin wrappers over dispatchCrossPaneLink /
 * subscribeToCrossPaneLinks so that the raw helpers remain usable outside
 * React trees (e.g., in vanilla-JS source widgets).
 *
 * NOT PCF-safe — requires React 19 and DOM CustomEvent API.
 */

import { useCallback, useEffect, useRef } from "react";
import {
  dispatchCrossPaneLink,
  subscribeToCrossPaneLinks,
} from "./cross-pane-events";
import type { CrossPaneLinkEvent } from "./cross-pane-events";

// ---------------------------------------------------------------------------
// useDispatchCrossPaneLink
// ---------------------------------------------------------------------------

/**
 * Returns a stable callback that dispatches a cross-pane link event.
 *
 * Use this in output pane widgets / CrossPaneLink to fire highlight requests
 * without rebuilding the callback reference on every render.
 *
 * @returns A memoised function `(event: CrossPaneLinkEvent) => void`.
 *
 * @example
 * const dispatch = useDispatchCrossPaneLink();
 * dispatch({ citationId: "ref-1", sourceWidgetId: "doc-1", highlightStart: 0, highlightEnd: 50 });
 */
export function useDispatchCrossPaneLink(): (
  event: CrossPaneLinkEvent
) => void {
  // useCallback with empty deps — dispatchCrossPaneLink is a stable module-level
  // function, so this callback is guaranteed stable across renders.
  return useCallback((event: CrossPaneLinkEvent): void => {
    dispatchCrossPaneLink(event);
  }, []);
}

// ---------------------------------------------------------------------------
// useCrossPaneSubscription
// ---------------------------------------------------------------------------

/**
 * Subscribes to cross-pane link events for the lifetime of the component.
 *
 * Sets up a document-level event listener in a useEffect and removes it
 * on unmount (or when the handler reference changes). This is the recommended
 * way for source pane widgets to react to citation clicks in the output pane.
 *
 * The handler is stabilised internally with a ref so that consumers can pass
 * an inline function without triggering unnecessary re-subscriptions.
 *
 * @param handler - Called with the CrossPaneLinkEvent payload whenever a
 *                  cross-pane link is activated. The reference may change each
 *                  render — the hook will always call the latest version.
 *
 * @example
 * useCrossPaneSubscription((event) => {
 *   if (event.sourceWidgetId === myWidgetId) {
 *     highlightRange(event.highlightStart, event.highlightEnd);
 *   }
 * });
 */
export function useCrossPaneSubscription(
  handler: (event: CrossPaneLinkEvent) => void
): void {
  // Store the handler in a ref so that the effect's listener closure always
  // calls the latest version without needing to resubscribe on every render.
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect((): (() => void) => {
    const unsubscribe = subscribeToCrossPaneLinks(
      (event: CrossPaneLinkEvent): void => {
        handlerRef.current(event);
      }
    );

    // Cleanup: remove the event listener when the component unmounts.
    return unsubscribe;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // subscribe once; handler changes are handled via handlerRef
}
