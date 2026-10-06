import React, { useCallback, useEffect, useRef, useState } from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

/**
 * FindSplitPane — two stacked regions (top / bottom) separated by a draggable, keyboard-operable divider
 * (spaarkeai-word-add-in-r1 task 102, UAT round 6 item 4).
 *
 * Built locally rather than reusing a shared splitter: the add-in does not depend on
 * `@spaarke/ui-components` beyond exact-path aliases (ADR-012 Path-A exception), and no shared
 * vertical splitter exists to alias (searched `Spaarke.UI.Components/src` for Splitter / Resizable /
 * `role="separator"` — only horizontal column resizers inside DataGrid, not a reusable component).
 *
 * **Sizing** is a RATIO (the top region's share, default 0.5 = an even split), applied as `flex-grow`
 * with `flex-basis: 0` so the regions always sum to the available height whatever the pane size.
 * `min-height` on each region keeps either from disappearing; the ratio is additionally clamped so the
 * divider can never be dragged past the minimum.
 *
 * **Divider** is an ARIA window splitter: `role="separator"`, `aria-orientation="horizontal"`,
 * `aria-valuenow` = the top region's share in percent, focusable, ArrowUp/ArrowDown move it 5 %,
 * Home/End jump to the limits. Pointer dragging uses pointer capture, so it works for mouse, pen and
 * touch and keeps tracking when the pointer leaves the thin handle.
 *
 * The chosen ratio is remembered per viewer in localStorage (wrapped in try/catch — it can throw or
 * be empty in a private window; the pane then simply starts even).
 */

export const SPLIT_MIN_REGION_PX = 96;
export const SPLIT_DEFAULT_RATIO = 0.5;
export const SPLIT_KEY_STEP = 0.05;
export const SPLIT_STORAGE_KEY = 'sprk.office-addin.find.splitRatio';
/** Fallback clamp when the container has no measurable height (first paint, jsdom). */
const FALLBACK_MIN_RATIO = 0.1;
const DIVIDER_HEIGHT_PX = 10;

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
  },
  region: {
    display: 'flex',
    flexDirection: 'column',
    flexShrink: 1,
    flexBasis: 0,
    minHeight: `${SPLIT_MIN_REGION_PX}px`,
    overflow: 'hidden',
  },
  divider: {
    position: 'relative',
    flexShrink: 0,
    flexGrow: 0,
    height: `${DIVIDER_HEIGHT_PX}px`,
    cursor: 'row-resize',
    touchAction: 'none',
    outlineStyle: 'none',
    '::after': {
      content: '""',
      position: 'absolute',
      top: '50%',
      left: '50%',
      width: '40px',
      height: '3px',
      transform: 'translate(-50%, -50%)',
      borderRadius: tokens.borderRadiusMedium,
      backgroundColor: tokens.colorNeutralStroke1,
    },
    ':hover::after': { backgroundColor: tokens.colorNeutralStroke1Hover },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
      borderRadius: tokens.borderRadiusMedium,
    },
  },
  dividerActive: {
    '::after': { backgroundColor: tokens.colorBrandStroke1 },
  },
});

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

function readStoredRatio(): number {
  try {
    const raw = window.localStorage.getItem(SPLIT_STORAGE_KEY);
    const parsed = raw === null ? NaN : Number(raw);
    return Number.isFinite(parsed) ? clamp(parsed, FALLBACK_MIN_RATIO, 1 - FALLBACK_MIN_RATIO) : SPLIT_DEFAULT_RATIO;
  } catch {
    return SPLIT_DEFAULT_RATIO;
  }
}

function storeRatio(ratio: number): void {
  try {
    window.localStorage.setItem(SPLIT_STORAGE_KEY, String(ratio));
  } catch {
    /* storage unavailable — the split simply is not remembered */
  }
}

export interface FindSplitPaneProps {
  top: React.ReactNode;
  bottom: React.ReactNode;
  /** Accessible name for the divider. */
  dividerLabel?: string;
}

export const FindSplitPane: React.FC<FindSplitPaneProps> = ({
  top,
  bottom,
  dividerLabel = 'Resize similar documents and matching records',
}) => {
  const styles = useStyles();
  const rootRef = useRef<HTMLDivElement>(null);
  const [ratio, setRatio] = useState<number>(readStoredRatio);
  const [dragging, setDragging] = useState(false);

  /** The ratio range the divider may occupy — honors the pixel minimum when the height is known. */
  const limits = useCallback((): { min: number; max: number } => {
    const height = rootRef.current?.getBoundingClientRect().height ?? 0;
    const usable = height - DIVIDER_HEIGHT_PX;
    if (usable <= 0) {
      return { min: FALLBACK_MIN_RATIO, max: 1 - FALLBACK_MIN_RATIO };
    }
    const min = Math.min(0.5, SPLIT_MIN_REGION_PX / usable);
    return { min, max: 1 - min };
  }, []);

  const commit = useCallback(
    (next: number) => {
      const { min, max } = limits();
      const bounded = clamp(next, min, max);
      setRatio(bounded);
      storeRatio(bounded);
    },
    [limits]
  );

  const onPointerDown = (event: React.PointerEvent<HTMLDivElement>) => {
    if (event.button !== undefined && event.button !== 0) return;
    event.currentTarget.setPointerCapture?.(event.pointerId);
    setDragging(true);
  };

  const onPointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging || !rootRef.current) return;
    const rect = rootRef.current.getBoundingClientRect();
    const usable = rect.height - DIVIDER_HEIGHT_PX;
    if (usable <= 0) return;
    commit((event.clientY - rect.top - DIVIDER_HEIGHT_PX / 2) / usable);
  };

  const endDrag = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!dragging) return;
    event.currentTarget.releasePointerCapture?.(event.pointerId);
    setDragging(false);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const { min, max } = limits();
    switch (event.key) {
      case 'ArrowUp':
        commit(ratio - SPLIT_KEY_STEP);
        break;
      case 'ArrowDown':
        commit(ratio + SPLIT_KEY_STEP);
        break;
      case 'Home':
        commit(min);
        break;
      case 'End':
        commit(max);
        break;
      default:
        return;
    }
    event.preventDefault();
  };

  // Re-clamp when the pane is resized (e.g. task 103's width toggle, window resize) so a stored ratio
  // can never leave a region below its minimum.
  useEffect(() => {
    const node = rootRef.current;
    if (!node || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => {
      const { min, max } = limits();
      setRatio(current => clamp(current, min, max));
    });
    observer.observe(node);
    return () => observer.disconnect();
  }, [limits]);

  const { min, max } = limits();

  return (
    <div className={styles.root} ref={rootRef} data-testid="find-split-pane">
      <div className={styles.region} style={{ flexGrow: ratio }} data-testid="find-split-top">
        {top}
      </div>
      <div
        role="separator"
        aria-orientation="horizontal"
        aria-label={dividerLabel}
        aria-valuenow={Math.round(ratio * 100)}
        aria-valuemin={Math.round(min * 100)}
        aria-valuemax={Math.round(max * 100)}
        tabIndex={0}
        className={mergeClasses(styles.divider, dragging && styles.dividerActive)}
        data-testid="find-split-divider"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onKeyDown={onKeyDown}
      />
      <div className={styles.region} style={{ flexGrow: 1 - ratio }} data-testid="find-split-bottom">
        {bottom}
      </div>
    </div>
  );
};

export default FindSplitPane;
