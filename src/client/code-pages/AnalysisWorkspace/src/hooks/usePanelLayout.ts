/**
 * usePanelLayout Hook
 *
 * Manages the Analysis Workspace panel layout:
 * - Editor panel: flex:1, always fills remaining width
 * - Source panel: fixed pixel width (user-resizable via single splitter), collapsible
 * - Chat panel: fixed 360px, NOT user-resizable (no splitter on its left edge)
 *
 * One drag splitter exists: between Editor and Source (splitter1).
 * The Chat panel is always fixed-width and not resizable by the user.
 *
 * @see ADR-021 - Fluent UI v9 design system
 */

import { useCallback, useEffect, useRef, useState } from 'react';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Fixed width for the Chat (right) panel in pixels — not user-resizable */
export const CHAT_FIXED_WIDTH_PX = 360;

/** Default width for the Source (center) panel in pixels */
const DEFAULT_SOURCE_WIDTH_PX = 380;

/** Minimum width for the Source (center) panel in pixels */
const MIN_SOURCE_WIDTH_PX = 200;

/** Minimum width for the Editor (left) panel in pixels */
const MIN_EDITOR_WIDTH_PX = 300;

/** Width of the splitter grip area in pixels */
const SPLITTER_WIDTH_PX = 4;

/** Keyboard resize step in pixels */
const KEYBOARD_STEP_PX = 20;

/** sessionStorage keys for persisted state */
const STORAGE_KEY_SOURCE_WIDTH = 'aw-source-width-px';
const STORAGE_KEY_VISIBILITY = 'aw-panel-layout-visibility';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Visibility state for toggleable panels */
interface PanelVisibility {
  source: boolean;
  chat: boolean;
}

export interface SplitterHandlers {
  /** Mouse down handler to attach to the splitter element */
  onMouseDown: (e: React.MouseEvent) => void;
  /** Key down handler for keyboard resize */
  onKeyDown: (e: React.KeyboardEvent) => void;
  /** Double-click handler to reset to defaults */
  onDoubleClick: () => void;
}

export interface UsePanelLayoutResult {
  /** Current source panel width in pixels */
  sourceWidthPx: number;
  /** Fixed chat panel width — always CHAT_FIXED_WIDTH_PX */
  chatWidthPx: number;
  /** Whether the Source panel is currently visible */
  isSourceVisible: boolean;
  /** Whether the Chat panel is currently visible */
  isChatVisible: boolean;
  /** Toggle Source panel visibility */
  toggleSource: () => void;
  /** Toggle Chat panel visibility */
  toggleChat: () => void;
  /** Handlers for the splitter between Editor and Source */
  splitter1Handlers: SplitterHandlers;
  /** Whether the splitter is actively being dragged */
  isDragging: boolean;
  /** Reset all panels to default widths and visibility */
  resetToDefaults: () => void;
  /** Ref to attach to the content container for measuring available width */
  containerRef: React.RefObject<HTMLDivElement | null>;
}

// ---------------------------------------------------------------------------
// Helpers — sessionStorage
// ---------------------------------------------------------------------------

function loadPersistedSourceWidth(): number {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY_SOURCE_WIDTH);
    if (stored !== null) {
      const v = parseFloat(stored);
      if (Number.isFinite(v) && v >= MIN_SOURCE_WIDTH_PX) return v;
    }
  } catch {
    // sessionStorage may be unavailable
  }
  return DEFAULT_SOURCE_WIDTH_PX;
}

function persistSourceWidth(px: number): void {
  try {
    sessionStorage.setItem(STORAGE_KEY_SOURCE_WIDTH, String(Math.round(px)));
  } catch {
    // Silently ignore storage errors
  }
}

function loadPersistedVisibility(): PanelVisibility {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY_VISIBILITY);
    if (stored !== null) {
      const parsed = JSON.parse(stored) as Partial<PanelVisibility>;
      return {
        source: typeof parsed.source === 'boolean' ? parsed.source : true,
        chat: typeof parsed.chat === 'boolean' ? parsed.chat : true,
      };
    }
  } catch {
    // sessionStorage may be unavailable
  }
  return { source: true, chat: true };
}

function persistVisibility(v: PanelVisibility): void {
  try {
    sessionStorage.setItem(STORAGE_KEY_VISIBILITY, JSON.stringify(v));
  } catch {
    // Silently ignore storage errors
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function usePanelLayout(): UsePanelLayoutResult {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const isDraggingRef = useRef(false);
  const dragStartXRef = useRef(0);
  const dragStartSourceWidthRef = useRef(DEFAULT_SOURCE_WIDTH_PX);

  const [sourceWidthPx, setSourceWidthPx] = useState<number>(loadPersistedSourceWidth);
  const [visibility, setVisibility] = useState<PanelVisibility>(loadPersistedVisibility);
  const [isDragging, setIsDragging] = useState(false);

  // ------------------------------------------------------------------
  // Clamping
  // ------------------------------------------------------------------

  const clampSourceWidth = useCallback(
    (width: number): number => {
      const container = containerRef.current;
      if (!container) return Math.max(MIN_SOURCE_WIDTH_PX, width);
      const containerWidth = container.getBoundingClientRect().width;
      const chatWidth = visibility.chat ? CHAT_FIXED_WIDTH_PX : 0;
      const maxSourceWidth =
        containerWidth - chatWidth - SPLITTER_WIDTH_PX - MIN_EDITOR_WIDTH_PX;
      return Math.max(MIN_SOURCE_WIDTH_PX, Math.min(maxSourceWidth, width));
    },
    [visibility.chat]
  );

  const updateSourceWidth = useCallback(
    (width: number) => {
      const clamped = clampSourceWidth(width);
      setSourceWidthPx(clamped);
      persistSourceWidth(clamped);
    },
    [clampSourceWidth]
  );

  // ------------------------------------------------------------------
  // Toggle handlers
  // ------------------------------------------------------------------

  const toggleSource = useCallback(() => {
    setVisibility(prev => {
      const next = { ...prev, source: !prev.source };
      persistVisibility(next);
      return next;
    });
  }, []);

  const toggleChat = useCallback(() => {
    setVisibility(prev => {
      const next = { ...prev, chat: !prev.chat };
      persistVisibility(next);
      return next;
    });
  }, []);

  // ------------------------------------------------------------------
  // Mouse drag handlers
  // ------------------------------------------------------------------

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isDraggingRef.current) return;
      const deltaX = e.clientX - dragStartXRef.current;
      // Splitter is between Editor (left, flex:1) and Source (right, fixed width).
      // Drag right (+delta) → editor grows, source shrinks.
      // Drag left (-delta) → editor shrinks, source grows.
      updateSourceWidth(dragStartSourceWidthRef.current - deltaX);
    },
    [updateSourceWidth]
  );

  const handleMouseUp = useCallback(() => {
    isDraggingRef.current = false;
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

  const onSplitterMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      dragStartXRef.current = e.clientX;
      dragStartSourceWidthRef.current = sourceWidthPx;
      setIsDragging(true);
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    },
    [sourceWidthPx]
  );

  // ------------------------------------------------------------------
  // Keyboard resize
  // ------------------------------------------------------------------

  const onSplitterKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      let delta = 0;
      if (e.key === 'ArrowLeft') delta = KEYBOARD_STEP_PX;       // source grows
      else if (e.key === 'ArrowRight') delta = -KEYBOARD_STEP_PX; // source shrinks
      else if (e.key === 'Home') delta = 9999;                    // expand source to max
      else if (e.key === 'End') delta = -9999;                    // collapse source to min
      else return;
      e.preventDefault();
      updateSourceWidth(sourceWidthPx + delta);
    },
    [sourceWidthPx, updateSourceWidth]
  );

  // ------------------------------------------------------------------
  // Reset to defaults
  // ------------------------------------------------------------------

  const resetToDefaults = useCallback(() => {
    const newVisibility: PanelVisibility = { source: true, chat: true };
    setSourceWidthPx(DEFAULT_SOURCE_WIDTH_PX);
    setVisibility(newVisibility);
    persistSourceWidth(DEFAULT_SOURCE_WIDTH_PX);
    persistVisibility(newVisibility);
  }, []);

  // ------------------------------------------------------------------
  // Splitter handlers
  // ------------------------------------------------------------------

  const splitter1Handlers: SplitterHandlers = {
    onMouseDown: onSplitterMouseDown,
    onKeyDown: onSplitterKeyDown,
    onDoubleClick: resetToDefaults,
  };

  return {
    sourceWidthPx,
    chatWidthPx: CHAT_FIXED_WIDTH_PX,
    isSourceVisible: visibility.source,
    isChatVisible: visibility.chat,
    toggleSource,
    toggleChat,
    splitter1Handlers,
    isDragging,
    resetToDefaults,
    containerRef,
  };
}
