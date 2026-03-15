/**
 * usePanelResize Hook
 *
 * Manages panel visibility, width state, drag-to-resize refs, and mouse
 * event handlers for the three-column Analysis Workspace layout.
 *
 * Encapsulates:
 * - Panel visibility toggles (conversation panel, document panel)
 * - Panel width state (left, center)
 * - 6 refs for drag tracking (container, left/center panels, drag state)
 * - Mouse event handlers for resize drag operations
 * - Cleanup effect for document-level event listeners
 *
 * Follows UseXxxOptions/UseXxxResult convention from shared library hooks.
 * React 16/17 compatible (ADR-022).
 *
 * @version 1.0.0
 */

import * as React from 'react';

/**
 * Options for the usePanelResize hook.
 * Currently empty — the hook is self-contained with no external dependencies.
 * Provided for consistency with the UseXxxOptions convention and future extensibility.
 */
export interface UsePanelResizeOptions {
  /** Initial visibility of the conversation (right) panel. Default: true */
  initialConversationVisible?: boolean;

  /** Initial visibility of the document (center) panel. Default: true */
  initialDocumentVisible?: boolean;
}

/**
 * Result returned by the usePanelResize hook
 */
export interface UsePanelResizeResult {
  /** Whether the conversation (right) panel is visible */
  isConversationPanelVisible: boolean;

  /** Whether the document (center) panel is visible */
  isDocumentPanelVisible: boolean;

  /** Override width for the left panel (null = use CSS flex default) */
  leftPanelWidth: number | null;

  /** Override width for the center panel (null = use CSS flex default) */
  centerPanelWidth: number | null;

  /** Ref for the outer container element */
  containerRef: React.RefObject<HTMLDivElement>;

  /** Ref for the left panel element */
  leftPanelRef: React.RefObject<HTMLDivElement>;

  /** Ref for the center panel element */
  centerPanelRef: React.RefObject<HTMLDivElement>;

  /** Toggle conversation panel visibility */
  setIsConversationPanelVisible: React.Dispatch<React.SetStateAction<boolean>>;

  /** Toggle document panel visibility */
  setIsDocumentPanelVisible: React.Dispatch<React.SetStateAction<boolean>>;

  /** Factory that returns a mousedown handler for a specific resize handle */
  handleResizeMouseDown: (
    handle: 'left-center' | 'center-right',
    panelElement: HTMLElement | null,
  ) => (e: React.MouseEvent) => void;
}

/**
 * usePanelResize Hook
 *
 * Encapsulates all panel resize logic for the three-column workspace layout.
 * Handles drag-to-resize between panels with minimum width constraints and
 * panel show/hide toggling.
 *
 * @example
 * ```tsx
 * const {
 *   isConversationPanelVisible,
 *   isDocumentPanelVisible,
 *   leftPanelWidth,
 *   centerPanelWidth,
 *   containerRef,
 *   leftPanelRef,
 *   centerPanelRef,
 *   setIsConversationPanelVisible,
 *   setIsDocumentPanelVisible,
 *   handleResizeMouseDown,
 * } = usePanelResize();
 * ```
 */
export const usePanelResize = (options?: UsePanelResizeOptions): UsePanelResizeResult => {
  const {
    initialConversationVisible = true,
    initialDocumentVisible = true,
  } = options || {};

  // Panel visibility state
  const [isConversationPanelVisible, setIsConversationPanelVisible] = React.useState(initialConversationVisible);
  const [isDocumentPanelVisible, setIsDocumentPanelVisible] = React.useState(initialDocumentVisible);

  // Panel width state (null = use CSS flex default)
  const [leftPanelWidth, setLeftPanelWidth] = React.useState<number | null>(null);
  const [centerPanelWidth, setCenterPanelWidth] = React.useState<number | null>(null);

  // DOM element refs
  const containerRef = React.useRef<HTMLDivElement>(null);
  const leftPanelRef = React.useRef<HTMLDivElement>(null);
  const centerPanelRef = React.useRef<HTMLDivElement>(null);

  // Drag tracking refs (use refs to avoid stale closure issues in document event listeners)
  const isDraggingRef = React.useRef<'left-center' | 'center-right' | null>(null);
  const dragStartXRef = React.useRef<number>(0);
  const dragStartWidthRef = React.useRef<number>(0);

  // Mouse move handler — updates panel width based on drag delta.
  // Defined as a stable function reference since it's added/removed on document.
  const handleResizeMouseMove = React.useCallback((e: MouseEvent) => {
    if (!isDraggingRef.current || !containerRef.current) return;

    const containerWidth = containerRef.current.getBoundingClientRect().width;
    const delta = e.clientX - dragStartXRef.current;
    const rawWidth = dragStartWidthRef.current + delta;

    // Constants for minimum widths
    const MIN_LEFT = 250;
    const MIN_CENTER = 200;
    const MIN_RIGHT = 300;
    const HANDLE_WIDTH = 8; // 2 handles x 4px each

    if (isDraggingRef.current === 'left-center') {
      // Resize left panel - must leave room for center + right + handles
      const maxLeft = containerWidth - MIN_CENTER - MIN_RIGHT - HANDLE_WIDTH;
      const newWidth = Math.max(MIN_LEFT, Math.min(rawWidth, maxLeft));
      setLeftPanelWidth(newWidth);
    } else if (isDraggingRef.current === 'center-right') {
      // Resize center panel - must leave room for right panel + handle
      const currentLeftWidth = leftPanelRef.current?.getBoundingClientRect().width || MIN_LEFT;
      const maxCenter = containerWidth - currentLeftWidth - MIN_RIGHT - HANDLE_WIDTH;
      const newWidth = Math.max(MIN_CENTER, Math.min(rawWidth, maxCenter));
      setCenterPanelWidth(newWidth);
    }
  }, []);

  // Mouse up handler — cleans up drag state and document listeners.
  const handleResizeMouseUp = React.useCallback(() => {
    isDraggingRef.current = null;
    document.removeEventListener('mousemove', handleResizeMouseMove);
    document.removeEventListener('mouseup', handleResizeMouseUp);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }, [handleResizeMouseMove]);

  // Mouse down factory — returns a handler for a specific resize handle.
  // Uses refs to track initial position/width to avoid stale closure issues.
  const handleResizeMouseDown = React.useCallback(
    (handle: 'left-center' | 'center-right', panelElement: HTMLElement | null) =>
      (e: React.MouseEvent) => {
        e.preventDefault();
        if (!panelElement) return;

        isDraggingRef.current = handle;
        dragStartXRef.current = e.clientX;
        dragStartWidthRef.current = panelElement.getBoundingClientRect().width;

        document.addEventListener('mousemove', handleResizeMouseMove);
        document.addEventListener('mouseup', handleResizeMouseUp);
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
      },
    [handleResizeMouseMove, handleResizeMouseUp],
  );

  // Cleanup resize listeners on unmount
  React.useEffect(() => {
    return () => {
      document.removeEventListener('mousemove', handleResizeMouseMove);
      document.removeEventListener('mouseup', handleResizeMouseUp);
    };
  }, [handleResizeMouseMove, handleResizeMouseUp]);

  return {
    isConversationPanelVisible,
    isDocumentPanelVisible,
    leftPanelWidth,
    centerPanelWidth,
    containerRef,
    leftPanelRef,
    centerPanelRef,
    setIsConversationPanelVisible,
    setIsDocumentPanelVisible,
    handleResizeMouseDown,
  };
};

export default usePanelResize;
