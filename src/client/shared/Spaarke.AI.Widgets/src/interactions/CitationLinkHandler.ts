/**
 * @spaarke/ai-widgets — CitationLinkHandler
 *
 * Utility that workspace widgets call when a user clicks a citation reference
 * (e.g. "[1]", "[ref-42]") in the AI-generated output. Dispatches a
 * `context_highlight` event to the `context` PaneEventBus channel so the
 * ContextPaneController can scroll to and highlight the source passage in
 * whichever context widget is currently active (CitationWidget or
 * SearchResultsWidget path).
 *
 * Design notes:
 * - This module is intentionally side-effect-free: it accepts an already-
 *   resolved `dispatch` function rather than calling hooks directly. This
 *   makes it trivially testable in isolation (no React render needed) and
 *   allows workspace widgets to call it from any non-hook context (event
 *   handlers, callback props, imperative code).
 * - The companion `useCitationLink` hook provides the React-idiomatic wrapper
 *   that binds a dispatch function from `useDispatchPaneEvent`.
 * - Event dispatch is synchronous; the acceptance criterion of <50 ms from
 *   click to dispatch is trivially satisfied because no async work occurs here.
 *
 * @see useCitationLink — React hook that wires this handler into a component
 * @see ContextPaneController — handles context_highlight and calls onHighlight
 * @see ContextWidgetAdapter — exposes onHighlight on registered context widgets
 *
 * Task: AIPU2-100
 */

import type { DispatchPaneEvent } from '../events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// CitationClickPayload
// ---------------------------------------------------------------------------

/**
 * Metadata carried by a citation click inside a workspace widget.
 *
 * Workspace widgets produce this payload when the user clicks a bracketed
 * citation reference. The payload is forwarded verbatim to the PaneEventBus
 * `context_highlight` event so the context pane can route it to the correct
 * source widget.
 */
export interface CitationClickPayload {
  /**
   * Stable citation identifier — typically the bracketed number shown in the
   * AI output (e.g. `"1"` for `[1]`), but may also be a slug for named
   * citations (e.g. `"smith-v-jones-2022"`).
   *
   * Must match the `data-citation-id` attribute on the corresponding source
   * element in the active context widget.
   */
  citationId: string;

  /**
   * Optional sub-range reference within the source document.
   *
   * Format is context-widget-specific (e.g. `"char:1024-1200"`,
   * `"section:3.2"`, `"page:7"`). Passed through to the widget's
   * `onHighlight()` call unchanged. May be undefined when the citation
   * targets a whole source item rather than a specific passage.
   */
  selectionRef?: string;
}

// ---------------------------------------------------------------------------
// handleCitationClick
// ---------------------------------------------------------------------------

/**
 * Dispatch a `context_highlight` event to the `context` PaneEventBus channel
 * in response to a citation link click in a workspace widget.
 *
 * This is a pure utility function — it requires a pre-resolved `dispatch`
 * function (from `useDispatchPaneEvent`) so it can be used in any JS context
 * without React hook rules applying at the call site.
 *
 * The ContextPaneController already subscribes to `context_highlight` events
 * and forwards them to the active widget's `onHighlight()` imperative handle.
 * No additional wiring is required in the controller for this feature.
 *
 * @param dispatch   - Typed dispatch function from `useDispatchPaneEvent`.
 * @param payload    - Citation metadata from the clicked link.
 *
 * @example
 * // Inside a workspace widget's click handler:
 * const dispatch = useDispatchPaneEvent();
 *
 * function onCitationAnchorClick(citationId: string) {
 *   handleCitationClick(dispatch, { citationId });
 * }
 *
 * @example
 * // With a sub-range selectionRef:
 * handleCitationClick(dispatch, {
 *   citationId: '3',
 *   selectionRef: 'char:1024-1200',
 * });
 */
export function handleCitationClick(
  dispatch: DispatchPaneEvent,
  payload: CitationClickPayload,
): void {
  dispatch('context', {
    type: 'context_highlight',
    citationId: payload.citationId,
    selectionRef: payload.selectionRef,
  });
}
