/**
 * @spaarke/ai-widgets — useTextSelection
 *
 * Hook for workspace widgets to participate in the cross-pane text-refinement
 * feature (AIPU2-101).
 *
 * When a user selects text inside a workspace widget the hook dispatches a
 * `selection_changed` event on the `workspace` PaneEventBus channel. The
 * ConversationPane subscribes to this event and shows a "Refine this?"
 * suggestion chip above the SprkChat input bar.
 *
 * Usage:
 *   function MyWidget({ widgetType }: WorkspaceWidgetProps) {
 *     const containerRef = useRef<HTMLDivElement>(null);
 *     useTextSelection(containerRef, widgetType, 'My widget');
 *     return <div ref={containerRef}>…</div>;
 *   }
 *
 * The hook is a thin coordinator. It attaches `mouseup` and `selectionchange`
 * event listeners through the {@link TextSelectionListener} component (which
 * handles debouncing and minimum-length guards). Widget authors who prefer a
 * declarative API should use TextSelectionListener directly.
 *
 * This hook is used internally by TextSelectionListener to bridge DOM events
 * into the typed PaneEventBus.
 *
 * @see TextSelectionListener — declarative React wrapper (preferred for most widgets)
 * @see PaneEventTypes — WorkspacePaneEvent.selection_changed
 * @see ConversationPane — subscriber that renders the "Refine this?" chip
 *
 * React 19, NOT PCF-safe.
 */

import { useCallback } from 'react';
import { useDispatchPaneEvent } from '../events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Result object returned by useTextSelection.
 *
 * Provides stable callbacks that TextSelectionListener (and advanced widget
 * authors) call in response to resolved DOM selection state.
 */
export interface UseTextSelectionResult {
  /**
   * Dispatch a non-null text selection to the `workspace` PaneEventBus channel.
   *
   * Called by TextSelectionListener after debouncing and minimum-length checks
   * pass. Dispatches `{ type: 'selection_changed', selectedText, widgetType,
   * contextLabel }` so ConversationPane can show the "Refine this?" chip.
   *
   * @param selectedText - The user-selected text (already validated: non-empty,
   *                       length >= minimum). Must not be null.
   */
  dispatchSelection: (selectedText: string) => void;

  /**
   * Dispatch a selection-cleared signal to the `workspace` PaneEventBus channel.
   *
   * Called by TextSelectionListener on `mousedown` (new selection begins) or
   * when the resolved selection falls below the minimum length threshold.
   * Dispatches `{ type: 'selection_changed', selectedText: null }` so
   * ConversationPane hides the chip without disrupting in-progress user input.
   */
  dispatchClear: () => void;
}

/**
 * Hook that produces stable dispatch callbacks for the text-selection
 * cross-pane integration.
 *
 * Consumes the PaneEventBus from context and memoises the two dispatch
 * callbacks so that TextSelectionListener can safely include them in its
 * own dependency arrays without causing infinite re-render cycles.
 *
 * @param widgetId   - Unique instance identifier for the widget (e.g. the tab
 *                     id or a stable React key). Included in the bus event for
 *                     diagnostic purposes.
 * @param widgetType - Registered type string of the widget (e.g.
 *                     `"document-summary"`, `"clause-list"`). Included in the
 *                     bus event for routing and display.
 * @param contextLabel - Human-readable description of the selection origin
 *                       shown as the chip preview label in ConversationPane.
 *                       Defaults to `widgetType` when omitted.
 *
 * @returns An object with `dispatchSelection` and `dispatchClear` callbacks.
 *
 * @example
 * const { dispatchSelection, dispatchClear } = useTextSelection(
 *   tabId,
 *   'clause-list',
 *   'Clause list',
 * );
 */
export function useTextSelection(widgetId: string, widgetType: string, contextLabel?: string): UseTextSelectionResult {
  const dispatch = useDispatchPaneEvent();

  const label = contextLabel ?? widgetType;

  // Stable callback — dispatches a non-null selection to the workspace channel.
  const dispatchSelection = useCallback(
    (selectedText: string): void => {
      dispatch('workspace', {
        type: 'selection_changed',
        widgetType,
        targetWidgetId: widgetId,
        selectedText,
        contextLabel: label,
      });
    },
    // widgetId, widgetType, label, and dispatch are all stable for the lifetime
    // of the hook invocation (dispatch is memoised by useDispatchPaneEvent).
    [dispatch, widgetId, widgetType, label]
  );

  // Stable callback — dispatches the cleared signal to the workspace channel.
  const dispatchClear = useCallback((): void => {
    dispatch('workspace', {
      type: 'selection_changed',
      widgetType,
      targetWidgetId: widgetId,
      selectedText: null,
    });
  }, [dispatch, widgetId, widgetType]);

  return { dispatchSelection, dispatchClear };
}
