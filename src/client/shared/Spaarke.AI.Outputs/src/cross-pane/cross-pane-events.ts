/**
 * @spaarke/ai-outputs — Cross-Pane Event Definitions
 *
 * Typed CustomEvent infrastructure for decoupled cross-pane communication.
 * The output pane dispatches events; the source pane subscribes via
 * document.addEventListener. No shared React context or Redux store required —
 * the two panes may live in completely separate React trees.
 *
 * @see CrossPaneLink.tsx — component that dispatches these events on click/keydown
 * @see useCrossPane.ts — React hooks wrapping dispatch and subscription
 *
 * NOT PCF-safe — requires DOM CustomEvent API (available in all modern browsers).
 */

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * The CustomEvent type string used for cross-pane link events.
 * Prefixed with "spaarke:" to avoid collision with browser or library events.
 *
 * @example
 * document.addEventListener(CROSS_PANE_LINK_EVENT, handler);
 */
export const CROSS_PANE_LINK_EVENT = "spaarke:cross-pane-link" as const;

// ---------------------------------------------------------------------------
// Event payload interface
// ---------------------------------------------------------------------------

/**
 * Payload carried in the `detail` property of a cross-pane link CustomEvent.
 *
 * Emitted when a user clicks (or activates via keyboard) a citation or
 * reference in the output pane. The source pane listens for this event and
 * navigates to / highlights the referenced range.
 */
export interface CrossPaneLinkEvent {
  /**
   * Identifier for the citation or reference that was activated.
   * Matches the `citationId` on the CrossPaneLink component.
   */
  citationId: string;

  /**
   * Identifier of the source widget that should handle this highlight request.
   * The source pane uses this to route the event to the correct widget instance.
   */
  sourceWidgetId: string;

  /**
   * Character or token offset where the highlight range starts in the source.
   * Interpretation is source-widget-specific (e.g., character offset in text).
   */
  highlightStart: number;

  /**
   * Character or token offset where the highlight range ends in the source.
   * The range is [highlightStart, highlightEnd) — exclusive end.
   */
  highlightEnd: number;
}

// ---------------------------------------------------------------------------
// Helper functions
// ---------------------------------------------------------------------------

/**
 * Dispatch a cross-pane link event on `document`.
 *
 * Call this when a citation or reference in the output pane is activated.
 * The source pane picks up the event via subscribeToCrossPaneLinks().
 *
 * @param event - The cross-pane link payload to broadcast.
 *
 * @example
 * dispatchCrossPaneLink({
 *   citationId: "ref-42",
 *   sourceWidgetId: "doc-viewer-1",
 *   highlightStart: 1024,
 *   highlightEnd: 1200,
 * });
 */
export function dispatchCrossPaneLink(event: CrossPaneLinkEvent): void {
  document.dispatchEvent(
    new CustomEvent<CrossPaneLinkEvent>(CROSS_PANE_LINK_EVENT, {
      detail: event,
      bubbles: false,
      cancelable: false,
    })
  );
}

/**
 * Subscribe to cross-pane link events dispatched on `document`.
 *
 * Returns an unsubscribe function. Always call the returned function on
 * cleanup (e.g., in a useEffect return or component unmount) to prevent
 * memory leaks.
 *
 * @param handler - Called with the CrossPaneLinkEvent payload whenever
 *                  a cross-pane link event is dispatched.
 * @returns A cleanup function that removes the event listener.
 *
 * @example
 * const unsubscribe = subscribeToCrossPaneLinks((e) => {
 *   highlightRange(e.highlightStart, e.highlightEnd);
 * });
 * // Later on unmount:
 * unsubscribe();
 */
export function subscribeToCrossPaneLinks(
  handler: (event: CrossPaneLinkEvent) => void
): () => void {
  const listener = (domEvent: Event): void => {
    const customEvent = domEvent as CustomEvent<CrossPaneLinkEvent>;
    handler(customEvent.detail);
  };

  document.addEventListener(CROSS_PANE_LINK_EVENT, listener);

  return (): void => {
    document.removeEventListener(CROSS_PANE_LINK_EVENT, listener);
  };
}
