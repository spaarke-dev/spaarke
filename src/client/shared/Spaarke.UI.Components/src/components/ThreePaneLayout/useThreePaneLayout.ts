/**
 * useThreePaneLayout Hook
 *
 * Generalized panel layout hook for three-pane layouts with draggable splitters.
 *
 * Extracted and generalized from AnalysisWorkspace/src/hooks/usePanelLayout.ts.
 *
 * Features:
 * - Left pane: fixed pixel width, user-resizable via left splitter, collapsible
 * - Center pane: flex:1, always fills remaining space
 * - Right pane: fixed pixel width, user-resizable via right splitter, collapsible
 * - Keyboard resize: Arrow keys (step), Home/End (expand/collapse to min/max)
 * - sessionStorage persistence with configurable key prefix
 * - Double-click on splitter resets that pane to default width
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared component library
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { UseThreePaneLayoutResult } from './ThreePaneLayout.types';
import { SplitterHandlers } from '../../hooks/useTwoPanelLayout';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Width of the splitter grip area in pixels */
const SPLITTER_WIDTH_PX = 4;

/** Keyboard resize step in pixels */
const KEYBOARD_STEP_PX = 20;

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

export interface UseThreePaneLayoutOptions {
  /** Default left pane width in pixels */
  defaultLeftWidthPx?: number;
  /** Default right pane width in pixels */
  defaultRightWidthPx?: number;
  /** Minimum left pane width in pixels */
  minLeftWidthPx?: number;
  /** Minimum right pane width in pixels */
  minRightWidthPx?: number;
  /** Minimum center pane width in pixels */
  minCenterWidthPx?: number;
  /** sessionStorage key prefix for persistence */
  storageKey?: string;
  /** Initial left visibility (overridden by persisted state) */
  defaultLeftVisible?: boolean;
  /** Initial right visibility (overridden by persisted state) */
  defaultRightVisible?: boolean;
  /**
   * (Task 117) Optional percentage-of-viewport initial width for the LEFT
   * pane (e.g. 0.25 for 25%). Used only on first mount when sessionStorage
   * has no persisted pixel width. See ThreePaneLayoutProps.defaultLeftWidthFrac
   * for full precedence documentation.
   */
  defaultLeftWidthFrac?: number;
  /**
   * (Task 117) Optional percentage-of-viewport initial width for the RIGHT
   * pane. See `defaultLeftWidthFrac`.
   */
  defaultRightWidthFrac?: number;
}

// ---------------------------------------------------------------------------
// Helpers — sessionStorage
// ---------------------------------------------------------------------------

function safeGetItem(key: string): string | null {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeSetItem(key: string, value: string): void {
  try {
    sessionStorage.setItem(key, value);
  } catch {
    // Silently ignore storage errors
  }
}

/**
 * (Task 117) Resolve the initial pane width with the three-tier precedence
 * documented on ThreePaneLayoutProps.defaultLeftWidthFrac:
 *   1. sessionStorage stored pixel width (user-dragged value)
 *   2. `frac` × `window.innerWidth` when `frac` is provided
 *   3. `defaultPx` (legacy pixel default)
 *
 * The computed value is clamped to at least `min`. `window.innerWidth` is
 * used as the viewport reference because the layout's own bounding rect is
 * not yet measurable on first mount.
 */
function resolveInitialWidth(
  key: string,
  defaultPx: number,
  min: number,
  frac: number | undefined
): number {
  // 1. Stored pixel width always wins (user-dragged value persists).
  const stored = safeGetItem(key);
  if (stored !== null) {
    const v = parseFloat(stored);
    if (Number.isFinite(v) && v >= min) return v;
  }

  // 2. Percentage default — applied on cold mount when no stored value.
  if (
    frac !== undefined &&
    Number.isFinite(frac) &&
    frac > 0 &&
    typeof window !== 'undefined' &&
    Number.isFinite(window.innerWidth) &&
    window.innerWidth > 0
  ) {
    const pxFromFrac = Math.round(window.innerWidth * frac);
    return Math.max(min, pxFromFrac);
  }

  // 3. Legacy pixel default.
  return Math.max(min, defaultPx);
}

function persistWidth(key: string, px: number): void {
  safeSetItem(key, String(Math.round(px)));
}

interface PanelVisibility {
  left: boolean;
  right: boolean;
}

function loadPersistedVisibility(key: string, defaults: PanelVisibility): PanelVisibility {
  const stored = safeGetItem(key);
  if (stored !== null) {
    try {
      const parsed = JSON.parse(stored) as Partial<PanelVisibility>;
      return {
        left: typeof parsed.left === 'boolean' ? parsed.left : defaults.left,
        right: typeof parsed.right === 'boolean' ? parsed.right : defaults.right,
      };
    } catch {
      // Fall through to defaults
    }
  }
  return defaults;
}

function persistVisibility(key: string, v: PanelVisibility): void {
  safeSetItem(key, JSON.stringify(v));
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useThreePaneLayout(options: UseThreePaneLayoutOptions = {}): UseThreePaneLayoutResult {
  const {
    defaultLeftWidthPx = 280,
    defaultRightWidthPx = 360,
    minLeftWidthPx = 180,
    minRightWidthPx = 200,
    minCenterWidthPx = 300,
    storageKey = 'three-pane',
    defaultLeftVisible = true,
    defaultRightVisible = true,
    defaultLeftWidthFrac,
    defaultRightWidthFrac,
  } = options;

  // Storage keys derived from prefix
  const leftWidthKey = `${storageKey}-left-width-px`;
  const rightWidthKey = `${storageKey}-right-width-px`;
  const visibilityKey = `${storageKey}-visibility`;

  // Refs for drag state (not React state — no re-renders during drag)
  const containerRef = useRef<HTMLDivElement | null>(null);
  const isDraggingRef = useRef(false);
  const activeSplitterRef = useRef<'left' | 'right' | null>(null);
  const dragStartXRef = useRef(0);
  const dragStartWidthRef = useRef(0);

  // React state
  // (Task 117) Initial widths resolved with three-tier precedence:
  //   stored sessionStorage value > frac × window.innerWidth > legacy default.
  // See `resolveInitialWidth` for details. When `defaultLeftWidthFrac` /
  // `defaultRightWidthFrac` are undefined the behavior is identical to the
  // pre-task-117 `loadPersistedWidth` path (full backwards compatibility).
  const [leftWidthPx, setLeftWidthPx] = useState<number>(() =>
    resolveInitialWidth(leftWidthKey, defaultLeftWidthPx, minLeftWidthPx, defaultLeftWidthFrac)
  );
  const [rightWidthPx, setRightWidthPx] = useState<number>(() =>
    resolveInitialWidth(rightWidthKey, defaultRightWidthPx, minRightWidthPx, defaultRightWidthFrac)
  );
  const [visibility, setVisibility] = useState<PanelVisibility>(() =>
    loadPersistedVisibility(visibilityKey, { left: defaultLeftVisible, right: defaultRightVisible })
  );
  const [isDragging, setIsDragging] = useState(false);

  // ------------------------------------------------------------------
  // Clamping — prevents squeezing panes below their minimums
  // ------------------------------------------------------------------

  const clampLeftWidth = useCallback(
    (width: number): number => {
      const container = containerRef.current;
      if (!container) return Math.max(minLeftWidthPx, width);
      const containerWidth = container.getBoundingClientRect().width;
      const rightOccupied = visibility.right ? rightWidthPx + SPLITTER_WIDTH_PX : 0;
      const maxLeft = containerWidth - rightOccupied - SPLITTER_WIDTH_PX - minCenterWidthPx;
      return Math.max(minLeftWidthPx, Math.min(maxLeft, width));
    },
    [visibility.right, rightWidthPx, minLeftWidthPx, minCenterWidthPx]
  );

  const clampRightWidth = useCallback(
    (width: number): number => {
      const container = containerRef.current;
      if (!container) return Math.max(minRightWidthPx, width);
      const containerWidth = container.getBoundingClientRect().width;
      const leftOccupied = visibility.left ? leftWidthPx + SPLITTER_WIDTH_PX : 0;
      const maxRight = containerWidth - leftOccupied - SPLITTER_WIDTH_PX - minCenterWidthPx;
      return Math.max(minRightWidthPx, Math.min(maxRight, width));
    },
    [visibility.left, leftWidthPx, minRightWidthPx, minCenterWidthPx]
  );

  const updateLeftWidth = useCallback(
    (width: number) => {
      const clamped = clampLeftWidth(width);
      setLeftWidthPx(clamped);
      persistWidth(leftWidthKey, clamped);
    },
    [clampLeftWidth, leftWidthKey]
  );

  const updateRightWidth = useCallback(
    (width: number) => {
      const clamped = clampRightWidth(width);
      setRightWidthPx(clamped);
      persistWidth(rightWidthKey, clamped);
    },
    [clampRightWidth, rightWidthKey]
  );

  // ------------------------------------------------------------------
  // Visibility toggles
  // ------------------------------------------------------------------

  const toggleLeft = useCallback(() => {
    setVisibility(prev => {
      const next = { ...prev, left: !prev.left };
      persistVisibility(visibilityKey, next);
      return next;
    });
  }, [visibilityKey]);

  const toggleRight = useCallback(() => {
    setVisibility(prev => {
      const next = { ...prev, right: !prev.right };
      persistVisibility(visibilityKey, next);
      return next;
    });
  }, [visibilityKey]);

  // ------------------------------------------------------------------
  // Mouse drag handlers (global listeners during drag)
  // ------------------------------------------------------------------

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isDraggingRef.current) return;
      const deltaX = e.clientX - dragStartXRef.current;

      if (activeSplitterRef.current === 'left') {
        // Left splitter: between left pane and center
        // Drag right (+delta) → left pane grows
        // Drag left (-delta) → left pane shrinks
        updateLeftWidth(dragStartWidthRef.current + deltaX);
      } else if (activeSplitterRef.current === 'right') {
        // Right splitter: between center and right pane
        // Drag right (+delta) → right pane shrinks
        // Drag left (-delta) → right pane grows
        updateRightWidth(dragStartWidthRef.current - deltaX);
      }
    },
    [updateLeftWidth, updateRightWidth]
  );

  const handleMouseUp = useCallback(() => {
    isDraggingRef.current = false;
    activeSplitterRef.current = null;
    setIsDragging(false);
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  }, []);

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

  // ------------------------------------------------------------------
  // Splitter mouse-down factory
  // ------------------------------------------------------------------

  const makeSplitterMouseDown = useCallback(
    (splitter: 'left' | 'right', currentWidth: number) => (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      activeSplitterRef.current = splitter;
      dragStartXRef.current = e.clientX;
      dragStartWidthRef.current = currentWidth;
      setIsDragging(true);
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    },
    []
  );

  // ------------------------------------------------------------------
  // Keyboard resize
  // ------------------------------------------------------------------

  const makeKeyDown = useCallback(
    (splitter: 'left' | 'right', currentWidth: number, update: (w: number) => void) => (e: React.KeyboardEvent) => {
      let delta = 0;
      if (splitter === 'left') {
        // Left splitter: ArrowRight grows left pane, ArrowLeft shrinks
        if (e.key === 'ArrowRight') delta = KEYBOARD_STEP_PX;
        else if (e.key === 'ArrowLeft') delta = -KEYBOARD_STEP_PX;
        else if (e.key === 'End')
          delta = 9999; // expand left to max
        else if (e.key === 'Home')
          delta = -9999; // collapse left to min
        else return;
      } else {
        // Right splitter: ArrowLeft grows right pane, ArrowRight shrinks
        if (e.key === 'ArrowLeft') delta = KEYBOARD_STEP_PX;
        else if (e.key === 'ArrowRight') delta = -KEYBOARD_STEP_PX;
        else if (e.key === 'Home')
          delta = 9999; // expand right to max
        else if (e.key === 'End')
          delta = -9999; // collapse right to min
        else return;
      }
      e.preventDefault();
      update(currentWidth + delta);
    },
    []
  );

  // ------------------------------------------------------------------
  // Reset to defaults
  // ------------------------------------------------------------------

  const resetToDefaults = useCallback(() => {
    const newVisibility: PanelVisibility = { left: defaultLeftVisible, right: defaultRightVisible };
    setLeftWidthPx(defaultLeftWidthPx);
    setRightWidthPx(defaultRightWidthPx);
    setVisibility(newVisibility);
    persistWidth(leftWidthKey, defaultLeftWidthPx);
    persistWidth(rightWidthKey, defaultRightWidthPx);
    persistVisibility(visibilityKey, newVisibility);
  }, [
    defaultLeftWidthPx,
    defaultRightWidthPx,
    defaultLeftVisible,
    defaultRightVisible,
    leftWidthKey,
    rightWidthKey,
    visibilityKey,
  ]);

  // ------------------------------------------------------------------
  // (Task 119) Reset to FRAC defaults — forces left/right widths back to
  // `defaultLeftWidthFrac` / `defaultRightWidthFrac` × `window.innerWidth`,
  // discarding any user-dragged pixel widths in sessionStorage. Used by the
  // ThreePaneLayout all-panes-collapsed empty-state Open button so the layout
  // returns to a clean 25/50/25 (or whatever the consumer's frac defaults are)
  // even if the user had dragged the splitters earlier in the session.
  //
  // Falls back to the legacy pixel defaults when `frac` is not provided.
  // Does NOT touch visibility (callers handle uncollapse separately via the
  // existing onToggle* callbacks they already wire).
  // ------------------------------------------------------------------

  const resetToFracDefaults = useCallback(() => {
    const computeFromFrac = (
      frac: number | undefined,
      defaultPx: number,
      min: number
    ): number => {
      if (
        frac !== undefined &&
        Number.isFinite(frac) &&
        frac > 0 &&
        typeof window !== 'undefined' &&
        Number.isFinite(window.innerWidth) &&
        window.innerWidth > 0
      ) {
        return Math.max(min, Math.round(window.innerWidth * frac));
      }
      return Math.max(min, defaultPx);
    };

    const newLeft = computeFromFrac(defaultLeftWidthFrac, defaultLeftWidthPx, minLeftWidthPx);
    const newRight = computeFromFrac(defaultRightWidthFrac, defaultRightWidthPx, minRightWidthPx);

    setLeftWidthPx(newLeft);
    setRightWidthPx(newRight);
    persistWidth(leftWidthKey, newLeft);
    persistWidth(rightWidthKey, newRight);
  }, [
    defaultLeftWidthFrac,
    defaultRightWidthFrac,
    defaultLeftWidthPx,
    defaultRightWidthPx,
    minLeftWidthPx,
    minRightWidthPx,
    leftWidthKey,
    rightWidthKey,
  ]);

  // ------------------------------------------------------------------
  // Splitter handler objects
  // ------------------------------------------------------------------

  const leftSplitterHandlers: SplitterHandlers = {
    onMouseDown: makeSplitterMouseDown('left', leftWidthPx),
    onKeyDown: makeKeyDown('left', leftWidthPx, updateLeftWidth),
    onDoubleClick: () => updateLeftWidth(defaultLeftWidthPx),
  };

  const rightSplitterHandlers: SplitterHandlers = {
    onMouseDown: makeSplitterMouseDown('right', rightWidthPx),
    onKeyDown: makeKeyDown('right', rightWidthPx, updateRightWidth),
    onDoubleClick: () => updateRightWidth(defaultRightWidthPx),
  };

  return {
    leftWidthPx,
    rightWidthPx,
    isLeftVisible: visibility.left,
    isRightVisible: visibility.right,
    toggleLeft,
    toggleRight,
    leftSplitterHandlers,
    rightSplitterHandlers,
    isDragging,
    resetToDefaults,
    resetToFracDefaults,
    containerRef,
  };
}
