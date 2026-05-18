/**
 * @spaarke/ai-widgets — TextSelectionListener
 *
 * Declarative wrapper component that listens for user text selection inside
 * workspace widgets and dispatches SelectionChangedEvents via PaneEventBus
 * for the cross-pane "Refine this?" feature (AIPU2-101).
 *
 * Wrap any workspace widget content with this component:
 *
 *   <TextSelectionListener widgetId={tabId} widgetType="clause-list" contextLabel="Clause list">
 *     <ClauseListContent />
 *   </TextSelectionListener>
 *
 * Behaviour:
 *   - Listens for `mouseup` and `selectionchange` events on the container div.
 *   - Reads `window.getSelection()` to get the current selection string.
 *   - Debounces 300 ms — only dispatches after the selection is stable.
 *   - Ignores selections shorter than MIN_SELECTION_LENGTH (10 chars) to
 *     prevent spurious dispatches from accidental single-word clicks.
 *   - Dispatches `{ type: 'selection_changed', selectedText, … }` on the
 *     `workspace` PaneEventBus channel when a valid selection is stable.
 *   - On `mousedown` clears the previous selection signal immediately (does not
 *     wait for debounce) so the chip disappears as soon as the user starts a
 *     new drag, preventing stale chip state.
 *   - When the resolved selection is below MIN_SELECTION_LENGTH the clear signal
 *     is also dispatched so the chip is hidden.
 *
 * This component uses useTextSelection internally. Widget authors who need
 * imperative control (e.g. virtual-scroll canvases) can call useTextSelection
 * directly instead.
 *
 * ADR-021: no hard-coded colors; this component contains no visual output —
 * it is a transparent wrapper div that delegates rendering to its children.
 *
 * React 19, NOT PCF-safe.
 *
 * @see useTextSelection — underlying hook (imperative API)
 * @see ConversationPane — subscriber that shows the "Refine this?" chip
 * @see PaneEventTypes — WorkspacePaneEvent.selection_changed definition
 */

import React, { useCallback, useEffect, useRef } from 'react';
import { useTextSelection } from './useTextSelection';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * Minimum number of characters a selection must contain before a
 * `selection_changed` event is dispatched. Prevents spurious events from
 * accidental single-word clicks or drag-select starts.
 */
const MIN_SELECTION_LENGTH = 10;

/**
 * Debounce delay in milliseconds. The `selection_changed` dispatch fires only
 * after the selection has been stable for this long, preventing rapid repeated
 * events during drag-select operations.
 *
 * Acceptance criterion: dispatch within 350 ms (300 ms debounce + 50 ms max
 * dispatch overhead).
 */
const DEBOUNCE_MS = 300;

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface TextSelectionListenerProps {
  /**
   * Unique identifier for this widget instance (e.g. the workspace tab id).
   * Included in the bus event payload for routing and diagnostics.
   */
  widgetId: string;

  /**
   * Registered widget type string (e.g. `"document-summary"`, `"clause-list"`).
   * Passed through to the bus event and used as the default `contextLabel`.
   */
  widgetType: string;

  /**
   * Human-readable label describing the selection origin.
   * Shown as the chip preview label prefix in ConversationPane.
   * Defaults to `widgetType` when omitted.
   *
   * Example: `"Document viewer"`, `"Clause list"`, `"Search results"`.
   */
  contextLabel?: string;

  /** Widget content. Selection events are captured within this subtree. */
  children: React.ReactNode;

  /**
   * Optional className forwarded to the wrapper div.
   * The wrapper is `display: contents` by default so it does not affect layout,
   * but a className may be supplied to override this for layout debugging.
   */
  className?: string;
}

// ---------------------------------------------------------------------------
// TextSelectionListener
// ---------------------------------------------------------------------------

/**
 * Transparent wrapper that captures text selection events within its subtree
 * and dispatches SelectionChangedEvents via PaneEventBus.
 *
 * Renders a single `<div>` container. The wrapper uses `display: contents` so
 * it does not introduce an extra layout box that could affect flex/grid children.
 */
export function TextSelectionListener({
  widgetId,
  widgetType,
  contextLabel,
  children,
  className,
}: TextSelectionListenerProps): React.JSX.Element {
  const containerRef = useRef<HTMLDivElement>(null);

  // Stable PaneEventBus dispatch callbacks from the hook layer.
  const { dispatchSelection, dispatchClear } = useTextSelection(
    widgetId,
    widgetType,
    contextLabel
  );

  // ── Debounce timer ref ───────────────────────────────────────────────────
  //
  // The timer id is stored in a ref (not state) so updating it does not
  // trigger a re-render, and so the cleanup function in useEffect can access
  // the latest timer without a stale closure.
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  /** Clear the pending debounce timer without firing it. */
  const cancelDebounce = useCallback((): void => {
    if (debounceTimerRef.current !== null) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
  }, []);

  // ── Resolved selection helper ────────────────────────────────────────────

  /**
   * Read the current browser selection string.
   * Returns null if nothing is selected or the selection is outside the
   * container element.
   */
  const resolveSelection = useCallback((): string | null => {
    const container = containerRef.current;
    if (container === null) return null;

    const sel = window.getSelection();
    if (sel === null || sel.rangeCount === 0) return null;

    const text = sel.toString();
    if (text.length === 0) return null;

    // Verify the selection is contained within (or intersects) our container.
    const range = sel.getRangeAt(0);
    if (!container.contains(range.commonAncestorContainer)) return null;

    return text;
  }, []);

  // ── mousedown handler — immediate clear ──────────────────────────────────

  const handleMouseDown = useCallback((): void => {
    // Cancel any pending debounce from the previous selection.
    cancelDebounce();
    // Immediately signal "selection cleared" so the chip disappears as the
    // user begins a new drag-select, preventing stale chip state.
    dispatchClear();
  }, [cancelDebounce, dispatchClear]);

  // ── mouseup / selectionchange handler — debounced dispatch ───────────────

  const handleSelectionEvent = useCallback((): void => {
    // Always cancel the previous debounce timer before scheduling a new one.
    cancelDebounce();

    debounceTimerRef.current = setTimeout((): void => {
      debounceTimerRef.current = null;

      const text = resolveSelection();

      if (text === null || text.length < MIN_SELECTION_LENGTH) {
        // Selection too short or gone — hide the chip.
        dispatchClear();
        return;
      }

      // Valid selection — show the chip.
      dispatchSelection(text);
    }, DEBOUNCE_MS);
  }, [cancelDebounce, resolveSelection, dispatchSelection, dispatchClear]);

  // ── DOM event subscriptions ──────────────────────────────────────────────

  useEffect((): (() => void) => {
    const container = containerRef.current;
    if (container === null) return () => undefined;

    container.addEventListener('mousedown', handleMouseDown);
    container.addEventListener('mouseup', handleSelectionEvent);

    // `selectionchange` fires on `document`, not on the container — it covers
    // keyboard-driven selection (Shift+Arrow, Ctrl+A) and touch selection.
    // We attach it to `document` but resolveSelection() confirms the selection
    // is inside our container before dispatching.
    document.addEventListener('selectionchange', handleSelectionEvent);

    return (): void => {
      container.removeEventListener('mousedown', handleMouseDown);
      container.removeEventListener('mouseup', handleSelectionEvent);
      document.removeEventListener('selectionchange', handleSelectionEvent);
      // Ensure no dangling timer fires after unmount.
      cancelDebounce();
    };
  }, [handleMouseDown, handleSelectionEvent, cancelDebounce]);

  // ── Render ───────────────────────────────────────────────────────────────
  //
  // The wrapper uses `display: contents` so it is invisible to the layout
  // engine — children retain their natural layout roles. A `className` prop
  // can override this for special cases (e.g. adding a debug border).

  return (
    <div
      ref={containerRef}
      className={className}
      style={className ? undefined : { display: 'contents' }}
      data-testid="text-selection-listener"
    >
      {children}
    </div>
  );
}
