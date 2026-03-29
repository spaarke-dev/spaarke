/**
 * useTwoPanelLayout Hook
 *
 * Manages a two-panel layout: a primary (left) panel and a collapsible detail
 * (right) panel. Handles visibility toggling, splitter drag operations,
 * keyboard resize, minimum width enforcement, and localStorage persistence.
 *
 * Adapted from the three-panel usePanelLayout in AnalysisWorkspace, simplified
 * for the common two-panel pattern (e.g., kanban board + detail panel).
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */

import { useCallback, useEffect, useRef, useState } from 'react';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Width of the splitter grip area in pixels (matches PanelSplitter) */
const SPLITTER_WIDTH_PX = 4;

/** Keyboard resize step in pixels */
const KEYBOARD_STEP_PX = 20;

/** Default detail panel width in pixels */
const DEFAULT_DETAIL_WIDTH_PX = 400;

/** Default minimum primary panel width in pixels */
const DEFAULT_MIN_PRIMARY_WIDTH_PX = 300;

/** Default minimum detail panel width in pixels */
const DEFAULT_MIN_DETAIL_WIDTH_PX = 280;

/** Default localStorage key */
const DEFAULT_STORAGE_KEY = 'panel-layout';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseTwoPanelLayoutOptions {
  /** Default width for the detail panel in pixels (default: 400) */
  defaultDetailWidth?: number;
  /** Minimum width for the primary panel in pixels (default: 300) */
  minPrimaryWidth?: number;
  /** Minimum width for the detail panel in pixels (default: 280) */
  minDetailWidth?: number;
  /** localStorage key for persisting state (default: 'panel-layout') */
  storageKey?: string;
}

export interface SplitterHandlers {
  onMouseDown: (e: React.MouseEvent) => void;
  onKeyDown: (e: React.KeyboardEvent) => void;
  onDoubleClick: () => void;
}

export interface UseTwoPanelLayoutResult {
  /** CSS width value for the primary (left) panel */
  primaryWidth: string;
  /** CSS width value for the detail (right) panel, '0px' when hidden */
  detailWidth: string;
  /** Whether the detail panel is currently visible */
  isDetailVisible: boolean;
  /** Toggle detail panel visibility */
  toggleDetail: () => void;
  /** Show detail panel */
  showDetail: () => void;
  /** Hide detail panel */
  hideDetail: () => void;
  /** Handlers to pass to PanelSplitter component */
  splitterHandlers: SplitterHandlers;
  /** Whether the splitter is being dragged */
  isDragging: boolean;
  /** Ref to attach to the container element */
  containerRef: React.RefObject<HTMLDivElement | null>;
  /** Current ratio (0-1) of primary panel width — for PanelSplitter ARIA */
  currentRatio: number;
  /** Reset to default layout */
  resetToDefaults: () => void;
}

// ---------------------------------------------------------------------------
// Persisted state shape
// ---------------------------------------------------------------------------

interface PersistedState {
  detailWidth: number;
  isDetailVisible: boolean;
}

// ---------------------------------------------------------------------------
// Helpers — localStorage
// ---------------------------------------------------------------------------

function loadPersistedState(storageKey: string, defaults: PersistedState): PersistedState {
  try {
    const stored = localStorage.getItem(storageKey);
    if (stored !== null) {
      const parsed = JSON.parse(stored) as Partial<PersistedState>;
      return {
        detailWidth:
          typeof parsed.detailWidth === 'number' && parsed.detailWidth > 0
            ? parsed.detailWidth
            : defaults.detailWidth,
        isDetailVisible:
          typeof parsed.isDetailVisible === 'boolean'
            ? parsed.isDetailVisible
            : defaults.isDetailVisible,
      };
    }
  } catch {
    // localStorage may be unavailable in some contexts
  }
  return defaults;
}

function persistState(storageKey: string, state: PersistedState): void {
  try {
    localStorage.setItem(
      storageKey,
      JSON.stringify({
        detailWidth: Math.round(state.detailWidth),
        isDetailVisible: state.isDetailVisible,
      })
    );
  } catch {
    // Silently ignore storage errors
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useTwoPanelLayout(
  options: UseTwoPanelLayoutOptions = {}
): UseTwoPanelLayoutResult {
  const {
    defaultDetailWidth = DEFAULT_DETAIL_WIDTH_PX,
    minPrimaryWidth = DEFAULT_MIN_PRIMARY_WIDTH_PX,
    minDetailWidth = DEFAULT_MIN_DETAIL_WIDTH_PX,
    storageKey = DEFAULT_STORAGE_KEY,
  } = options;

  const defaultState: PersistedState = {
    detailWidth: defaultDetailWidth,
    isDetailVisible: true,
  };

  // -----------------------------------------------------------------------
  // Refs
  // -----------------------------------------------------------------------

  const containerRef = useRef<HTMLDivElement | null>(null);
  const isDraggingRef = useRef(false);
  const dragStartXRef = useRef(0);
  const dragStartDetailWidthRef = useRef(defaultDetailWidth);

  // -----------------------------------------------------------------------
  // State
  // -----------------------------------------------------------------------

  const [detailWidthPx, setDetailWidthPx] = useState<number>(() => {
    const persisted = loadPersistedState(storageKey, defaultState);
    return persisted.detailWidth;
  });

  const [isDetailVisible, setIsDetailVisible] = useState<boolean>(() => {
    const persisted = loadPersistedState(storageKey, defaultState);
    return persisted.isDetailVisible;
  });

  const [isDragging, setIsDragging] = useState(false);

  // -----------------------------------------------------------------------
  // Clamping
  // -----------------------------------------------------------------------

  /**
   * Clamp the detail width so both panels respect their minimum widths
   * given the current container size.
   */
  const clampDetailWidth = useCallback(
    (width: number): number => {
      const container = containerRef.current;
      if (!container) return Math.max(width, minDetailWidth);

      const containerWidth = container.getBoundingClientRect().width;
      const availableWidth = containerWidth - SPLITTER_WIDTH_PX;
      if (availableWidth <= 0) return width;

      // Detail must be at least minDetailWidth
      let clamped = Math.max(width, minDetailWidth);
      // Primary must be at least minPrimaryWidth
      const maxDetailWidth = availableWidth - minPrimaryWidth;
      clamped = Math.min(clamped, maxDetailWidth);
      // Ensure we don't go below minimum after both constraints
      clamped = Math.max(clamped, minDetailWidth);

      return clamped;
    },
    [minPrimaryWidth, minDetailWidth]
  );

  // -----------------------------------------------------------------------
  // Persist helpers
  // -----------------------------------------------------------------------

  const persistCurrentState = useCallback(
    (width: number, visible: boolean) => {
      persistState(storageKey, { detailWidth: width, isDetailVisible: visible });
    },
    [storageKey]
  );

  // -----------------------------------------------------------------------
  // Update detail width (clamp + persist)
  // -----------------------------------------------------------------------

  const updateDetailWidth = useCallback(
    (newWidth: number) => {
      const clamped = clampDetailWidth(newWidth);
      setDetailWidthPx(clamped);
      persistCurrentState(clamped, isDetailVisible);
    },
    [clampDetailWidth, persistCurrentState, isDetailVisible]
  );

  // -----------------------------------------------------------------------
  // Visibility toggles
  // -----------------------------------------------------------------------

  const toggleDetail = useCallback(() => {
    setIsDetailVisible(prev => {
      const next = !prev;
      persistCurrentState(detailWidthPx, next);
      return next;
    });
  }, [detailWidthPx, persistCurrentState]);

  const showDetail = useCallback(() => {
    if (!isDetailVisible) {
      setIsDetailVisible(true);
      persistCurrentState(detailWidthPx, true);
    }
  }, [isDetailVisible, detailWidthPx, persistCurrentState]);

  const hideDetail = useCallback(() => {
    if (isDetailVisible) {
      setIsDetailVisible(false);
      persistCurrentState(detailWidthPx, false);
    }
  }, [isDetailVisible, detailWidthPx, persistCurrentState]);

  // -----------------------------------------------------------------------
  // Mouse drag handlers
  // -----------------------------------------------------------------------

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isDraggingRef.current || !containerRef.current) return;

      // Dragging the splitter left increases detail width; right decreases it.
      // deltaX > 0 means mouse moved right → detail shrinks.
      const deltaX = e.clientX - dragStartXRef.current;
      const newDetailWidth = dragStartDetailWidthRef.current - deltaX;

      const clamped = clampDetailWidth(newDetailWidth);
      setDetailWidthPx(clamped);
      persistCurrentState(clamped, true);
    },
    [clampDetailWidth, persistCurrentState]
  );

  const handleMouseUp = useCallback(() => {
    isDraggingRef.current = false;
    setIsDragging(false);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }, []);

  // Attach/detach document-level listeners when dragging
  useEffect(() => {
    if (isDragging) {
      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);
    }
    return () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging, handleMouseMove, handleMouseUp]);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      dragStartXRef.current = e.clientX;
      dragStartDetailWidthRef.current = detailWidthPx;
      setIsDragging(true);
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    },
    [detailWidthPx]
  );

  // -----------------------------------------------------------------------
  // Keyboard resize
  // -----------------------------------------------------------------------

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      const container = containerRef.current;
      if (!container) return;

      const containerWidth = container.getBoundingClientRect().width;
      const availableWidth = containerWidth - SPLITTER_WIDTH_PX;
      if (availableWidth <= 0) return;

      let delta = 0;
      if (e.key === 'ArrowLeft') {
        // ArrowLeft expands detail (splitter moves left)
        delta = KEYBOARD_STEP_PX;
      } else if (e.key === 'ArrowRight') {
        // ArrowRight shrinks detail (splitter moves right)
        delta = -KEYBOARD_STEP_PX;
      } else if (e.key === 'Home') {
        // Home: maximize detail (push splitter to far left)
        delta = availableWidth;
      } else if (e.key === 'End') {
        // End: minimize detail (push splitter to far right)
        delta = -availableWidth;
      } else {
        return;
      }

      e.preventDefault();
      updateDetailWidth(detailWidthPx + delta);
    },
    [detailWidthPx, updateDetailWidth]
  );

  // -----------------------------------------------------------------------
  // Double-click reset
  // -----------------------------------------------------------------------

  const handleDoubleClick = useCallback(() => {
    const clamped = clampDetailWidth(defaultDetailWidth);
    setDetailWidthPx(clamped);
    persistCurrentState(clamped, isDetailVisible);
  }, [defaultDetailWidth, clampDetailWidth, persistCurrentState, isDetailVisible]);

  // -----------------------------------------------------------------------
  // Reset to defaults
  // -----------------------------------------------------------------------

  const resetToDefaults = useCallback(() => {
    const clamped = clampDetailWidth(defaultDetailWidth);
    setDetailWidthPx(clamped);
    setIsDetailVisible(true);
    persistCurrentState(clamped, true);
  }, [defaultDetailWidth, clampDetailWidth, persistCurrentState]);

  // -----------------------------------------------------------------------
  // Computed CSS values
  // -----------------------------------------------------------------------

  let primaryWidth: string;
  let detailWidth: string;

  if (!isDetailVisible) {
    primaryWidth = '100%';
    detailWidth = '0px';
  } else {
    primaryWidth = `calc(100% - ${detailWidthPx}px - ${SPLITTER_WIDTH_PX}px)`;
    detailWidth = `${detailWidthPx}px`;
  }

  // -----------------------------------------------------------------------
  // Current ratio (for ARIA valueNow on the splitter)
  // -----------------------------------------------------------------------

  let currentRatio = 0.5;
  const container = containerRef.current;
  if (container && isDetailVisible) {
    const containerWidth = container.getBoundingClientRect().width;
    const availableWidth = containerWidth - SPLITTER_WIDTH_PX;
    if (availableWidth > 0) {
      currentRatio = (availableWidth - detailWidthPx) / availableWidth;
    }
  }

  // -----------------------------------------------------------------------
  // Splitter handlers
  // -----------------------------------------------------------------------

  const splitterHandlers: SplitterHandlers = {
    onMouseDown: handleMouseDown,
    onKeyDown: handleKeyDown,
    onDoubleClick: handleDoubleClick,
  };

  return {
    primaryWidth,
    detailWidth,
    isDetailVisible,
    toggleDetail,
    showDetail,
    hideDetail,
    splitterHandlers,
    isDragging,
    containerRef,
    currentRatio,
    resetToDefaults,
  };
}
