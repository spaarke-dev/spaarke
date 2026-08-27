/**
 * ResizablePane — drag-to-resize support for the docked bottom detail panes.
 *
 * Added 2026-08-26 (UAT round 6). The detail panes were fixed at 45% of the page: enough for the
 * Settings tab, too little for a permissions grid, and too much when the operator only wanted to
 * scan the list. A fixed split always fits one task and fights the others.
 *
 * Deliberately hand-rolled rather than pulled from a library: this is ~40 lines of pointer maths
 * against one axis, and the alternative was a new dependency in a code page whose bundle already
 * ships at 2.3 MB (CLAUDE.md §11 — extend before adding).
 *
 * ADR-021: Fluent design tokens only, no hard-coded colours.
 */

import * as React from "react";
import { makeStyles, tokens } from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export interface UseResizablePaneOptions {
  /** Starting height in pixels. */
  defaultHeight?: number;
  /** Floor — below this the pane's own header and tab strip stop fitting. */
  minHeight?: number;
  /**
   * Ceiling as a fraction of the viewport. Capped rather than unbounded so a drag cannot push the
   * list it belongs to off the screen entirely — the pane is a detail view, never the whole page.
   */
  maxViewportFraction?: number;
}

export interface ResizablePaneApi {
  /** Current pane height in pixels — pass to the pane's `style.height`. */
  height: number;
  /** Attach to the splitter handle. */
  onPointerDown: (e: React.PointerEvent<HTMLDivElement>) => void;
  /** Keyboard resize, for the handle's `onKeyDown`. */
  onKeyDown: (e: React.KeyboardEvent<HTMLDivElement>) => void;
  /** True while a drag is in flight — used to suppress text selection. */
  isDragging: boolean;
}

/**
 * Tracks the height of a bottom-docked pane under pointer drag.
 *
 * Uses pointer events with `setPointerCapture` rather than document-level mousemove listeners:
 * capture keeps the drag alive when the pointer crosses an iframe or leaves the window, which a
 * document listener in a Dataverse-hosted web resource does not reliably do.
 */
export function useResizablePane(
  options: UseResizablePaneOptions = {}
): ResizablePaneApi {
  const {
    defaultHeight = 340,
    minHeight = 160,
    maxViewportFraction = 0.8,
  } = options;

  const [height, setHeight] = React.useState(defaultHeight);
  const [isDragging, setIsDragging] = React.useState(false);

  /** Drag origin — pointer Y and pane height at mousedown. */
  const origin = React.useRef<{ y: number; height: number } | null>(null);

  const clamp = React.useCallback(
    (next: number) => {
      const max = Math.max(minHeight, window.innerHeight * maxViewportFraction);
      return Math.min(max, Math.max(minHeight, next));
    },
    [minHeight, maxViewportFraction]
  );

  const onPointerDown = React.useCallback(
    (e: React.PointerEvent<HTMLDivElement>) => {
      // Left button only — a right-click on the handle should open the context menu, not resize.
      if (e.button !== 0) return;
      e.preventDefault();
      origin.current = { y: e.clientY, height };
      setIsDragging(true);
      e.currentTarget.setPointerCapture(e.pointerId);
    },
    [height]
  );

  // Move/up are bound on the captured element for the life of the drag.
  React.useEffect(() => {
    if (!isDragging) return;

    const handleMove = (e: PointerEvent) => {
      const start = origin.current;
      if (!start) return;
      // Dragging UP (smaller clientY) makes the bottom pane TALLER — hence the inversion.
      setHeight(clamp(start.height - (e.clientY - start.y)));
    };

    const end = () => {
      origin.current = null;
      setIsDragging(false);
    };

    window.addEventListener("pointermove", handleMove);
    window.addEventListener("pointerup", end);
    window.addEventListener("pointercancel", end);
    return () => {
      window.removeEventListener("pointermove", handleMove);
      window.removeEventListener("pointerup", end);
      window.removeEventListener("pointercancel", end);
    };
  }, [isDragging, clamp]);

  /** Arrow keys resize in 24px steps; Home/End jump to the floor and ceiling. */
  const onKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      const STEP = 24;
      if (e.key === "ArrowUp") {
        e.preventDefault();
        setHeight((h) => clamp(h + STEP));
      } else if (e.key === "ArrowDown") {
        e.preventDefault();
        setHeight((h) => clamp(h - STEP));
      } else if (e.key === "Home") {
        e.preventDefault();
        setHeight(clamp(Number.MAX_SAFE_INTEGER));
      } else if (e.key === "End") {
        e.preventDefault();
        setHeight(clamp(0));
      }
    },
    [clamp]
  );

  return { height, onPointerDown, onKeyDown, isDragging };
}

// ─────────────────────────────────────────────────────────────────────────────
// Handle
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * The grab strip. Six pixels tall with a wider invisible hit area would be ideal, but a plain
   * 6px band is enough at this size and avoids overlaying the rows above it.
   */
  handle: {
    flex: "0 0 auto",
    height: "6px",
    cursor: "row-resize",
    backgroundColor: tokens.colorNeutralBackground3,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
    position: "relative",
    // Suppress the text selection that otherwise sweeps the grid during a drag.
    userSelect: "none",
    touchAction: "none",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground3Hover,
    },
    ":focus-visible": {
      outlineWidth: "2px",
      outlineStyle: "solid",
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: "-2px",
    },
  },

  handleDragging: {
    backgroundColor: tokens.colorNeutralBackground3Pressed,
  },

  /** The centred grip marks, so the strip reads as draggable rather than as a border. */
  grip: {
    position: "absolute",
    top: "50%",
    left: "50%",
    transform: "translate(-50%, -50%)",
    width: "28px",
    height: "2px",
    borderRadius: tokens.borderRadiusSmall,
    backgroundColor: tokens.colorNeutralForeground4,
    pointerEvents: "none",
  },
});

export interface PaneSplitterProps extends Pick<
  ResizablePaneApi,
  "onPointerDown" | "onKeyDown" | "isDragging"
> {
  /** Names what is being resized, e.g. "container details". */
  label: string;
  /** Current height, surfaced to assistive tech via aria-valuenow. */
  height: number;
}

/**
 * The draggable divider. `role="separator"` with `aria-orientation="horizontal"` is the ARIA
 * window-splitter pattern, so the arrow-key resize is discoverable rather than mouse-only.
 */
export const PaneSplitter: React.FC<PaneSplitterProps> = ({
  onPointerDown,
  onKeyDown,
  isDragging,
  label,
  height,
}) => {
  const styles = useStyles();
  return (
    <div
      className={`${styles.handle}${isDragging ? ` ${styles.handleDragging}` : ""}`}
      onPointerDown={onPointerDown}
      onKeyDown={onKeyDown}
      role="separator"
      aria-orientation="horizontal"
      aria-label={`Resize ${label}`}
      aria-valuenow={Math.round(height)}
      tabIndex={0}
    >
      <div className={styles.grip} />
    </div>
  );
};
