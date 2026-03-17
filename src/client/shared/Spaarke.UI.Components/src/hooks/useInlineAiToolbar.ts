/**
 * useInlineAiToolbar - Selection-tracking hook for the InlineAiToolbar
 *
 * Monitors the document for text selection changes and computes the
 * position and visibility state for the InlineAiToolbar floating container.
 * Debounces at 200ms to avoid flooding during drag-select operations.
 *
 * Usage:
 * ```tsx
 * const editorContainerRef = useRef<HTMLDivElement>(null);
 * const { visible, position, selectedText, actions } = useInlineAiToolbar({
 *   editorContainerRef,
 * });
 *
 * return (
 *   <>
 *     <div ref={editorContainerRef}>...</div>
 *     <InlineAiToolbar
 *       visible={visible}
 *       position={position}
 *       actions={actions}
 *       onAction={handleAction}
 *     />
 *   </>
 * );
 * ```
 *
 * Constraints:
 * - MUST NOT import Xrm or ComponentFramework (ADR-012)
 * - Selection detection uses only the browser Selection API and React refs
 * - Debounce MUST be ≤ 200ms (spec-NFR-01)
 *
 * @see InlineAiToolbar component
 * @see useInlineAiActions — companion hook for dispatching actions
 * @see ADR-012 - Shared Component Library
 * @see ADR-021 - Fluent UI v9
 */

import { useState, useEffect, useRef, useCallback } from 'react';
import {
  DEFAULT_INLINE_ACTIONS,
  type InlineAiAction,
} from '../components/InlineAiToolbar/inlineAiToolbar.types';

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

/** Debounce delay for selection change events (spec-NFR-01: ≤200ms) */
const SELECTION_DEBOUNCE_MS = 200;

/** Vertical offset (px) above the selection bounding rect to position the toolbar */
const TOOLBAR_VERTICAL_OFFSET_PX = 40;

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface UseInlineAiToolbarOptions {
  /**
   * Ref to the container element that hosts the editable content.
   * The toolbar will only appear when the selection is within this element.
   */
  editorContainerRef: React.RefObject<HTMLElement | null>;

  /**
   * Override the default set of inline actions shown in the toolbar.
   * Defaults to DEFAULT_INLINE_ACTIONS from inlineAiToolbar.types.
   */
  actions?: InlineAiAction[];
}

export interface UseInlineAiToolbarResult {
  /** Whether the toolbar should be visible */
  visible: boolean;

  /** Absolute pixel position for the floating toolbar container */
  position: { top: number; left: number };

  /** The text that was selected when the toolbar became visible */
  selectedText: string;

  /** The set of actions to render in the toolbar */
  actions: InlineAiAction[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Tracks text selection within an editor container and computes toolbar
 * visibility and positioning.
 *
 * Returns toolbar state: visible, position (top/left), selectedText, and the
 * action list. The toolbar is positioned above the selection bounding rect at
 * a fixed vertical offset.
 *
 * @param options - Configuration options
 * @returns Toolbar display state
 */
export function useInlineAiToolbar(options: UseInlineAiToolbarOptions): UseInlineAiToolbarResult {
  const { editorContainerRef, actions = DEFAULT_INLINE_ACTIONS } = options;

  const [visible, setVisible] = useState(false);
  const [position, setPosition] = useState<{ top: number; left: number }>({ top: 0, left: 0 });
  const [selectedText, setSelectedText] = useState('');

  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  /**
   * Read the current selection and update toolbar state.
   * Called after the debounce delay.
   */
  const evaluateSelection = useCallback(() => {
    const selection = window.getSelection();

    // No selection or collapsed selection (cursor only) → hide toolbar
    if (!selection || selection.rangeCount === 0 || selection.isCollapsed) {
      setVisible(false);
      return;
    }

    const text = selection.toString();

    // Empty text after trim → hide toolbar
    if (!text.trim()) {
      setVisible(false);
      return;
    }

    // Check that the selection is within the editor container.
    // Walk up from the range's common ancestor to see if it's inside the ref.
    const range = selection.getRangeAt(0);
    const container = editorContainerRef.current;

    if (!container) {
      setVisible(false);
      return;
    }

    if (!container.contains(range.commonAncestorContainer)) {
      // Selection is outside the editor container — do not show toolbar
      setVisible(false);
      return;
    }

    // Compute toolbar position from the selection bounding rect.
    // top: above the selection by TOOLBAR_VERTICAL_OFFSET_PX
    // left: aligned to the left edge of the selection
    const rect = range.getBoundingClientRect();
    const containerRect = container.getBoundingClientRect();

    // Position relative to the container so that absolute positioning works
    // when the toolbar is a child of the container, or use page-relative
    // coordinates (top/left in page space) for portalled toolbars.
    const top = rect.top - containerRect.top - TOOLBAR_VERTICAL_OFFSET_PX;
    const left = rect.left - containerRect.left;

    setPosition({ top, left });
    setSelectedText(text);
    setVisible(true);
  }, [editorContainerRef]);

  /**
   * Debounced wrapper around evaluateSelection.
   * Clears previous timer on every call to ensure the delay resets on each event.
   */
  const handleSelectionChange = useCallback(() => {
    if (debounceTimerRef.current !== null) {
      clearTimeout(debounceTimerRef.current);
    }

    debounceTimerRef.current = setTimeout(() => {
      debounceTimerRef.current = null;
      evaluateSelection();
    }, SELECTION_DEBOUNCE_MS);
  }, [evaluateSelection]);

  // ───────────────────────────────────────────────────────────────────────────
  // Event listener lifecycle
  // ───────────────────────────────────────────────────────────────────────────

  useEffect(() => {
    document.addEventListener('selectionchange', handleSelectionChange);

    return () => {
      document.removeEventListener('selectionchange', handleSelectionChange);

      // Clear any pending debounce timer on unmount
      if (debounceTimerRef.current !== null) {
        clearTimeout(debounceTimerRef.current);
        debounceTimerRef.current = null;
      }

      // Reset toolbar state on unmount
      setVisible(false);
    };
  }, [handleSelectionChange]);

  return {
    visible,
    position,
    selectedText,
    actions,
  };
}
