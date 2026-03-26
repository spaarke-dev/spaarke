/**
 * usePanelLayout Hook
 *
 * Manages three-panel layout state for the AnalysisWorkspace: Editor, Source,
 * and Chat panels. Handles visibility toggling, splitter drag operations,
 * minimum width enforcement, and sessionStorage persistence.
 *
 * The Editor panel is always visible. Source and Chat panels can be toggled
 * independently, with available space redistributed proportionally among
 * visible panels.
 *
 * Default ratios: ~45% Editor / ~30% Source / ~25% Chat
 *
 * @see ADR-021 - Fluent UI v9 design system
 */

import { useCallback, useEffect, useRef, useState } from 'react';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Minimum width for the Editor (left) panel in pixels */
const MIN_EDITOR_WIDTH_PX = 300;

/** Minimum width for the Source (center) panel in pixels */
const MIN_SOURCE_WIDTH_PX = 200;

/** Minimum width for the Chat (right) panel in pixels */
const MIN_CHAT_WIDTH_PX = 280;

/** Width of each splitter grip area in pixels */
const SPLITTER_WIDTH_PX = 4;

/** Default panel ratios — proportional share of available width */
const DEFAULT_EDITOR_RATIO = 0.45;
const DEFAULT_SOURCE_RATIO = 0.3;
const DEFAULT_CHAT_RATIO = 0.25;

/** Keyboard resize step in pixels */
const KEYBOARD_STEP_PX = 20;

/** sessionStorage keys for persisted state */
const STORAGE_KEY_RATIOS = 'aw-panel-layout-ratios';
const STORAGE_KEY_VISIBILITY = 'aw-panel-layout-visibility';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Internal representation of panel size ratios (always sum to 1.0) */
interface PanelRatios {
  editor: number;
  source: number;
  chat: number;
}

/** Visibility state for toggleable panels */
interface PanelVisibility {
  source: boolean;
  chat: boolean;
}

export interface UsePanelLayoutOptions {
  /** Override default Editor ratio (0-1) */
  defaultEditorRatio?: number;
  /** Override default Source ratio (0-1) */
  defaultSourceRatio?: number;
  /** Override default Chat ratio (0-1) */
  defaultChatRatio?: number;
  /** Override minimum Editor width in pixels */
  minEditorWidth?: number;
  /** Override minimum Source width in pixels */
  minSourceWidth?: number;
  /** Override minimum Chat width in pixels */
  minChatWidth?: number;
}

export interface PanelSizes {
  /** CSS width value for the Editor panel */
  editor: string;
  /** CSS width value for the Source panel */
  source: string;
  /** CSS width value for the Chat panel */
  chat: string;
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
  /** Computed CSS widths for each panel */
  panelSizes: PanelSizes;
  /** Whether the Source panel is currently visible */
  isSourceVisible: boolean;
  /** Whether the Chat panel is currently visible */
  isChatVisible: boolean;
  /** Toggle Source panel visibility */
  toggleSource: () => void;
  /** Toggle Chat panel visibility */
  toggleChat: () => void;
  /** Handlers for splitter 1 (between Editor and Source/Chat) */
  splitter1Handlers: SplitterHandlers;
  /** Handlers for splitter 2 (between Source and Chat) */
  splitter2Handlers: SplitterHandlers;
  /** Whether any splitter is actively being dragged */
  isDragging: boolean;
  /** Which splitter is being dragged (null if none) */
  activeSplitter: 1 | 2 | null;
  /** Reset all panels to default ratios and visibility */
  resetToDefaults: () => void;
  /** Ref to attach to the container element for measuring available width */
  containerRef: React.RefObject<HTMLDivElement | null>;
  /** Current ratios for visible panels (for ARIA values) */
  currentRatios: PanelRatios;
  /** Number of currently visible panels */
  visiblePanelCount: number;
}

// ---------------------------------------------------------------------------
// Helpers — sessionStorage
// ---------------------------------------------------------------------------

function loadPersistedRatios(defaults: PanelRatios): PanelRatios {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY_RATIOS);
    if (stored !== null) {
      const parsed = JSON.parse(stored) as Partial<PanelRatios>;
      if (
        typeof parsed.editor === 'number' &&
        typeof parsed.source === 'number' &&
        typeof parsed.chat === 'number' &&
        parsed.editor > 0 &&
        parsed.source > 0 &&
        parsed.chat > 0
      ) {
        // Normalize to ensure they sum to 1
        const sum = parsed.editor + parsed.source + parsed.chat;
        return {
          editor: parsed.editor / sum,
          source: parsed.source / sum,
          chat: parsed.chat / sum,
        };
      }
    }
  } catch {
    // sessionStorage may be unavailable in some contexts
  }
  return defaults;
}

function persistRatios(ratios: PanelRatios): void {
  try {
    sessionStorage.setItem(
      STORAGE_KEY_RATIOS,
      JSON.stringify({
        editor: parseFloat(ratios.editor.toFixed(4)),
        source: parseFloat(ratios.source.toFixed(4)),
        chat: parseFloat(ratios.chat.toFixed(4)),
      })
    );
  } catch {
    // Silently ignore storage errors
  }
}

function loadPersistedVisibility(defaults: PanelVisibility): PanelVisibility {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY_VISIBILITY);
    if (stored !== null) {
      const parsed = JSON.parse(stored) as Partial<PanelVisibility>;
      return {
        source: typeof parsed.source === 'boolean' ? parsed.source : defaults.source,
        chat: typeof parsed.chat === 'boolean' ? parsed.chat : defaults.chat,
      };
    }
  } catch {
    // sessionStorage may be unavailable
  }
  return defaults;
}

function persistVisibility(visibility: PanelVisibility): void {
  try {
    sessionStorage.setItem(STORAGE_KEY_VISIBILITY, JSON.stringify(visibility));
  } catch {
    // Silently ignore storage errors
  }
}

// ---------------------------------------------------------------------------
// Helpers — ratio computation
// ---------------------------------------------------------------------------

/**
 * Compute the effective ratios for visible panels only.
 * Hidden panels get ratio 0; visible panels share proportionally.
 */
function computeEffectiveRatios(baseRatios: PanelRatios, visibility: PanelVisibility): PanelRatios {
  const visibleSum =
    baseRatios.editor + (visibility.source ? baseRatios.source : 0) + (visibility.chat ? baseRatios.chat : 0);

  if (visibleSum <= 0) {
    // Fallback: editor always visible
    return { editor: 1, source: 0, chat: 0 };
  }

  return {
    editor: baseRatios.editor / visibleSum,
    source: visibility.source ? baseRatios.source / visibleSum : 0,
    chat: visibility.chat ? baseRatios.chat / visibleSum : 0,
  };
}

/**
 * Count how many splitters are visible given current panel visibility.
 */
function countVisibleSplitters(visibility: PanelVisibility): number {
  let count = 0;
  if (visibility.source) count++;
  if (visibility.chat) count++;
  return count;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function usePanelLayout(options: UsePanelLayoutOptions = {}): UsePanelLayoutResult {
  const {
    defaultEditorRatio = DEFAULT_EDITOR_RATIO,
    defaultSourceRatio = DEFAULT_SOURCE_RATIO,
    defaultChatRatio = DEFAULT_CHAT_RATIO,
    minEditorWidth = MIN_EDITOR_WIDTH_PX,
    minSourceWidth = MIN_SOURCE_WIDTH_PX,
    minChatWidth = MIN_CHAT_WIDTH_PX,
  } = options;

  const defaultRatios: PanelRatios = {
    editor: defaultEditorRatio,
    source: defaultSourceRatio,
    chat: defaultChatRatio,
  };

  const containerRef = useRef<HTMLDivElement | null>(null);
  const isDraggingRef = useRef(false);
  const activeSplitterRef = useRef<1 | 2 | null>(null);
  const dragStartXRef = useRef(0);
  const dragStartRatiosRef = useRef<PanelRatios>(defaultRatios);

  const [ratios, setRatios] = useState<PanelRatios>(() => loadPersistedRatios(defaultRatios));
  const [visibility, setVisibility] = useState<PanelVisibility>(() =>
    loadPersistedVisibility({ source: true, chat: true })
  );
  const [isDragging, setIsDragging] = useState(false);
  const [activeSplitter, setActiveSplitter] = useState<1 | 2 | null>(null);

  // ------------------------------------------------------------------
  // Derived values
  // ------------------------------------------------------------------

  const effectiveRatios = computeEffectiveRatios(ratios, visibility);
  const splitterCount = countVisibleSplitters(visibility);
  const visiblePanelCount = 1 + (visibility.source ? 1 : 0) + (visibility.chat ? 1 : 0);

  // ------------------------------------------------------------------
  // Clamping
  // ------------------------------------------------------------------

  /**
   * Clamp ratios so all visible panels respect minimum widths.
   * Returns adjusted ratios that satisfy min-width constraints.
   */
  const clampRatios = useCallback(
    (newRatios: PanelRatios, vis: PanelVisibility): PanelRatios => {
      const container = containerRef.current;
      if (!container) return newRatios;

      const totalSplitterWidth = countVisibleSplitters(vis) * SPLITTER_WIDTH_PX;
      const availableWidth = container.getBoundingClientRect().width - totalSplitterWidth;
      if (availableWidth <= 0) return newRatios;

      // Compute effective ratios for visible panels
      const effective = computeEffectiveRatios(newRatios, vis);

      // Convert to pixels
      let editorPx = effective.editor * availableWidth;
      let sourcePx = effective.source * availableWidth;
      let chatPx = effective.chat * availableWidth;

      // Enforce minimums for visible panels
      if (editorPx < minEditorWidth) editorPx = minEditorWidth;
      if (vis.source && sourcePx < minSourceWidth) sourcePx = minSourceWidth;
      if (vis.chat && chatPx < minChatWidth) chatPx = minChatWidth;

      // Normalize back to ratios (as base ratios, not effective)
      const totalPx = editorPx + sourcePx + chatPx;
      if (totalPx <= 0) return newRatios;

      return {
        editor: editorPx / totalPx,
        source: vis.source ? sourcePx / totalPx : newRatios.source,
        chat: vis.chat ? chatPx / totalPx : newRatios.chat,
      };
    },
    [minEditorWidth, minSourceWidth, minChatWidth]
  );

  // ------------------------------------------------------------------
  // Update and persist
  // ------------------------------------------------------------------

  const updateRatios = useCallback(
    (newRatios: PanelRatios) => {
      const clamped = clampRatios(newRatios, visibility);
      setRatios(clamped);
      persistRatios(clamped);
    },
    [clampRatios, visibility]
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
      if (!isDraggingRef.current || !containerRef.current || !activeSplitterRef.current) return;

      const containerRect = containerRef.current.getBoundingClientRect();
      const totalSplitterWidth = countVisibleSplitters(visibility) * SPLITTER_WIDTH_PX;
      const availableWidth = containerRect.width - totalSplitterWidth;
      if (availableWidth <= 0) return;

      const deltaX = e.clientX - dragStartXRef.current;
      const deltaRatio = deltaX / availableWidth;
      const startRatios = dragStartRatiosRef.current;

      let newRatios: PanelRatios;

      if (activeSplitterRef.current === 1) {
        // Splitter 1: between Editor and the next visible panel
        if (visibility.source) {
          // Dragging transfers space between Editor and Source
          newRatios = {
            editor: startRatios.editor + deltaRatio,
            source: startRatios.source - deltaRatio,
            chat: startRatios.chat,
          };
        } else {
          // Source hidden — splitter 1 is between Editor and Chat
          newRatios = {
            editor: startRatios.editor + deltaRatio,
            source: startRatios.source,
            chat: startRatios.chat - deltaRatio,
          };
        }
      } else {
        // Splitter 2: between Source and Chat
        newRatios = {
          editor: startRatios.editor,
          source: startRatios.source + deltaRatio,
          chat: startRatios.chat - deltaRatio,
        };
      }

      // Prevent any ratio from going negative before clamping
      if (newRatios.editor <= 0 || newRatios.source < 0 || newRatios.chat < 0) return;
      if (visibility.source && newRatios.source <= 0) return;
      if (visibility.chat && newRatios.chat <= 0) return;

      updateRatios(newRatios);
    },
    [updateRatios, visibility]
  );

  const handleMouseUp = useCallback(() => {
    isDraggingRef.current = false;
    activeSplitterRef.current = null;
    setIsDragging(false);
    setActiveSplitter(null);
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

  const createMouseDownHandler = useCallback(
    (splitterIndex: 1 | 2) => (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      activeSplitterRef.current = splitterIndex;
      dragStartXRef.current = e.clientX;
      dragStartRatiosRef.current = { ...ratios };
      setIsDragging(true);
      setActiveSplitter(splitterIndex);
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    },
    [ratios]
  );

  // ------------------------------------------------------------------
  // Keyboard resize
  // ------------------------------------------------------------------

  const createKeyDownHandler = useCallback(
    (splitterIndex: 1 | 2) => (e: React.KeyboardEvent) => {
      const container = containerRef.current;
      if (!container) return;

      const totalSplitterWidth = countVisibleSplitters(visibility) * SPLITTER_WIDTH_PX;
      const availableWidth = container.getBoundingClientRect().width - totalSplitterWidth;
      if (availableWidth <= 0) return;

      let delta = 0;
      if (e.key === 'ArrowLeft') {
        delta = -KEYBOARD_STEP_PX;
      } else if (e.key === 'ArrowRight') {
        delta = KEYBOARD_STEP_PX;
      } else if (e.key === 'Home') {
        delta = -availableWidth;
      } else if (e.key === 'End') {
        delta = availableWidth;
      } else {
        return;
      }

      e.preventDefault();
      const deltaRatio = delta / availableWidth;

      let newRatios: PanelRatios;

      if (splitterIndex === 1) {
        if (visibility.source) {
          newRatios = {
            editor: ratios.editor + deltaRatio,
            source: ratios.source - deltaRatio,
            chat: ratios.chat,
          };
        } else {
          newRatios = {
            editor: ratios.editor + deltaRatio,
            source: ratios.source,
            chat: ratios.chat - deltaRatio,
          };
        }
      } else {
        newRatios = {
          editor: ratios.editor,
          source: ratios.source + deltaRatio,
          chat: ratios.chat - deltaRatio,
        };
      }

      // Prevent negatives
      if (newRatios.editor > 0 && newRatios.source >= 0 && newRatios.chat >= 0) {
        updateRatios(newRatios);
      }
    },
    [ratios, updateRatios, visibility]
  );

  // ------------------------------------------------------------------
  // Reset to defaults
  // ------------------------------------------------------------------

  const resetToDefaults = useCallback(() => {
    const newRatios = { ...defaultRatios };
    const newVisibility: PanelVisibility = { source: true, chat: true };
    setRatios(newRatios);
    setVisibility(newVisibility);
    persistRatios(newRatios);
    persistVisibility(newVisibility);
  }, [defaultRatios]);

  // ------------------------------------------------------------------
  // Computed CSS values
  // ------------------------------------------------------------------

  const panelSizes: PanelSizes = (() => {
    const totalSplitterWidth = splitterCount * SPLITTER_WIDTH_PX;

    if (visiblePanelCount === 1) {
      // Only editor visible
      return {
        editor: '100%',
        source: '0px',
        chat: '0px',
      };
    }

    if (visiblePanelCount === 2) {
      // Two panels visible — distribute using effective ratios
      const editorPct = (effectiveRatios.editor * 100).toFixed(2);

      if (visibility.source && !visibility.chat) {
        const sourcePct = (effectiveRatios.source * 100).toFixed(2);
        return {
          editor: `calc(${editorPct}% - ${totalSplitterWidth / 2}px)`,
          source: `calc(${sourcePct}% - ${totalSplitterWidth / 2}px)`,
          chat: '0px',
        };
      }

      // Editor + Chat
      const chatPct = (effectiveRatios.chat * 100).toFixed(2);
      return {
        editor: `calc(${editorPct}% - ${totalSplitterWidth / 2}px)`,
        source: '0px',
        chat: `calc(${chatPct}% - ${totalSplitterWidth / 2}px)`,
      };
    }

    // All three visible
    const editorPct = (effectiveRatios.editor * 100).toFixed(2);
    const sourcePct = (effectiveRatios.source * 100).toFixed(2);
    const chatPct = (effectiveRatios.chat * 100).toFixed(2);

    // Each panel absorbs its share of total splitter width
    // With 2 splitters, each panel absorbs 2/3 of a splitter width on average
    // Simpler: distribute evenly — total deduction = totalSplitterWidth
    const splitterSharePx = (totalSplitterWidth / 3).toFixed(1);

    return {
      editor: `calc(${editorPct}% - ${splitterSharePx}px)`,
      source: `calc(${sourcePct}% - ${splitterSharePx}px)`,
      chat: `calc(${chatPct}% - ${splitterSharePx}px)`,
    };
  })();

  // ------------------------------------------------------------------
  // Splitter handler objects
  // ------------------------------------------------------------------

  const splitter1Handlers: SplitterHandlers = {
    onMouseDown: createMouseDownHandler(1),
    onKeyDown: createKeyDownHandler(1),
    onDoubleClick: resetToDefaults,
  };

  const splitter2Handlers: SplitterHandlers = {
    onMouseDown: createMouseDownHandler(2),
    onKeyDown: createKeyDownHandler(2),
    onDoubleClick: resetToDefaults,
  };

  return {
    panelSizes,
    isSourceVisible: visibility.source,
    isChatVisible: visibility.chat,
    toggleSource,
    toggleChat,
    splitter1Handlers,
    splitter2Handlers,
    isDragging,
    activeSplitter,
    resetToDefaults,
    containerRef,
    currentRatios: effectiveRatios,
    visiblePanelCount,
  };
}
