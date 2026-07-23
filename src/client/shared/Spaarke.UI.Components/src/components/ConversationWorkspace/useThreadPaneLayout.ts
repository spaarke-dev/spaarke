/**
 * useThreadPaneLayout — LEFT-oriented resizable + collapsible pane layout for
 * `<ConversationWorkspace />` (R3 UAT 2026-07-23 items 1/2/3).
 *
 * This is the left-pane mirror of the shared `useTwoPanelLayout` hook, which is
 * built for a RIGHT-side collapsible "detail" pane (its drag direction is
 * inverted for a left pane, and it renders the collapsible pane on the right).
 * The conversation workspace needs the opposite: a fixed-width, collapsible
 * THREAD pane on the LEFT and a flexible conversation pane on the right — so we
 * reuse the presentational `PanelSplitter` grip (same as SpaarkeAi) but own the
 * left-oriented drag/collapse/persist logic here.
 *
 * PCF-safe (ADR-022): uses only React-16-compatible hooks (useState / useRef /
 * useEffect / useCallback), so `ConversationWorkspace` keeps working inside the
 * React-16 PCF host as well as the React-18/19 code-page + widget hosts.
 *
 * Persistence: localStorage under `${storageKey}` → `{ widthPx, collapsed }`
 * (sticky preference, mirroring SpaarkeAi's collapse persistence).
 */
import { useCallback, useEffect, useRef, useState } from 'react';

const KEYBOARD_STEP_PX = 20;
const SPLITTER_WIDTH_PX = 4;

export interface UseThreadPaneLayoutOptions {
  /** localStorage key for width + collapsed persistence. */
  storageKey?: string;
  /** Default thread-pane width as a fraction of the container on first mount (item 1 = 0.2). */
  defaultFrac?: number;
  /** Minimum thread-pane width in px. */
  minThreadPx?: number;
  /** Minimum conversation-pane width in px (the flexible side). */
  minConversationPx?: number;
}

export interface SplitterHandlers {
  onMouseDown: (e: React.MouseEvent) => void;
  onKeyDown: (e: React.KeyboardEvent) => void;
  onDoubleClick: () => void;
}

export interface UseThreadPaneLayoutResult {
  /** Current thread-pane width in px. */
  threadWidthPx: number;
  /** Whether the thread pane is collapsed. */
  collapsed: boolean;
  /** Toggle the collapsed state. */
  toggleCollapse: () => void;
  /** Handlers for the `<PanelSplitter />` grip. */
  splitterHandlers: SplitterHandlers;
  /** True while the splitter is being dragged. */
  isDragging: boolean;
  /** Ref to attach to the flex container that holds both panes. */
  containerRef: React.RefObject<HTMLDivElement | null>;
  /** Thread-pane proportion (0-1) for the splitter's ARIA value. */
  currentRatio: number;
}

interface PersistedState {
  widthPx: number;
  collapsed: boolean;
}

function loadPersisted(storageKey: string): Partial<PersistedState> | null {
  try {
    const raw = localStorage.getItem(storageKey);
    if (raw !== null) return JSON.parse(raw) as Partial<PersistedState>;
  } catch {
    // localStorage may be unavailable — degrade to defaults.
  }
  return null;
}

function persist(storageKey: string, state: PersistedState): void {
  try {
    localStorage.setItem(storageKey, JSON.stringify({ widthPx: Math.round(state.widthPx), collapsed: state.collapsed }));
  } catch {
    // Silently ignore storage errors.
  }
}

export function useThreadPaneLayout(options: UseThreadPaneLayoutOptions = {}): UseThreadPaneLayoutResult {
  const {
    storageKey = 'sprk-comm-thread-pane',
    defaultFrac = 0.33,
    minThreadPx = 160,
    minConversationPx = 320,
  } = options;

  const containerRef = useRef<HTMLDivElement | null>(null);
  const isDraggingRef = useRef(false);
  const dragStartXRef = useRef(0);
  const dragStartWidthRef = useRef(0);

  const persistedRef = useRef(loadPersisted(storageKey));
  const hasPersistedWidthRef = useRef(typeof persistedRef.current?.widthPx === 'number');
  const hasSizedRef = useRef(false);

  const [threadWidthPx, setThreadWidthPx] = useState<number>(() => {
    const p = persistedRef.current;
    if (p && typeof p.widthPx === 'number') return p.widthPx;
    const w = typeof window !== 'undefined' ? window.innerWidth : 1200;
    return Math.round(w * defaultFrac);
  });
  const [collapsed, setCollapsed] = useState<boolean>(() => persistedRef.current?.collapsed === true);
  const [isDragging, setIsDragging] = useState(false);

  const clamp = useCallback(
    (width: number): number => {
      const c = containerRef.current;
      const cw = c ? c.getBoundingClientRect().width : 0;
      const upper = cw > 0 ? cw - minConversationPx - SPLITTER_WIDTH_PX : Number.POSITIVE_INFINITY;
      return Math.max(minThreadPx, Math.min(width, Math.max(minThreadPx, upper)));
    },
    [minThreadPx, minConversationPx]
  );

  // Observe the container so the pane sizes correctly once it has a real width
  // (e.g. a modal that animates in — the width is 0 on first paint) AND re-clamps
  // when the container/window resizes so the panes always follow the container
  // (R3 UAT round 3 item 16). First real width sizes to `defaultFrac` (unless a
  // persisted width exists); later resizes only re-clamp the current width.
  useEffect(() => {
    const c = containerRef.current;
    if (!c) return;

    const sizeToContainer = () => {
      const cw = c.getBoundingClientRect().width;
      if (cw <= 0) return;
      setThreadWidthPx(prev => {
        if (!hasSizedRef.current) {
          hasSizedRef.current = true;
          return clamp(hasPersistedWidthRef.current ? prev : Math.round(cw * defaultFrac));
        }
        return clamp(prev);
      });
    };

    sizeToContainer();

    if (typeof ResizeObserver === 'undefined') return;
    const ro = new ResizeObserver(sizeToContainer);
    ro.observe(c);
    return () => ro.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clamp, defaultFrac]);

  // Persist on change.
  useEffect(() => {
    persist(storageKey, { widthPx: threadWidthPx, collapsed });
  }, [storageKey, threadWidthPx, collapsed]);

  // Left-oriented drag: moving the splitter RIGHT (deltaX > 0) GROWS the thread pane.
  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!isDraggingRef.current) return;
      const deltaX = e.clientX - dragStartXRef.current;
      setThreadWidthPx(clamp(dragStartWidthRef.current + deltaX));
    },
    [clamp]
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

  const onMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      isDraggingRef.current = true;
      dragStartXRef.current = e.clientX;
      dragStartWidthRef.current = threadWidthPx;
      setIsDragging(true);
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
    },
    [threadWidthPx]
  );

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      // ArrowRight grows the (left) thread pane; ArrowLeft shrinks it.
      let delta = 0;
      if (e.key === 'ArrowRight') delta = KEYBOARD_STEP_PX;
      else if (e.key === 'ArrowLeft') delta = -KEYBOARD_STEP_PX;
      else return;
      e.preventDefault();
      setThreadWidthPx(prev => clamp(prev + delta));
    },
    [clamp]
  );

  const onDoubleClick = useCallback(() => {
    const c = containerRef.current;
    const cw = c ? c.getBoundingClientRect().width : 0;
    setThreadWidthPx(clamp(cw > 0 ? Math.round(cw * defaultFrac) : threadWidthPx));
  }, [clamp, defaultFrac, threadWidthPx]);

  const toggleCollapse = useCallback(() => setCollapsed(prev => !prev), []);

  let currentRatio = defaultFrac;
  const c = containerRef.current;
  if (c) {
    const cw = c.getBoundingClientRect().width;
    if (cw > 0) currentRatio = threadWidthPx / cw;
  }

  return {
    threadWidthPx,
    collapsed,
    toggleCollapse,
    splitterHandlers: { onMouseDown, onKeyDown, onDoubleClick },
    isDragging,
    containerRef,
    currentRatio,
  };
}
