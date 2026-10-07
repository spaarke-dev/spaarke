import { useLayoutEffect, useRef, useState, type RefObject } from 'react';

/**
 * useToolbarFit — task 107 (owner UAT round 9: in Word the Expand button sat ON TOP of the last tab).
 *
 * The toolbar's tabs container may shrink (`minWidth: 0`), so when the labelled tabs plus Send, Expand and "⋮" do
 * not fit, the tab strip overflowed visually under the buttons beside it. This hook measures that overflow and
 * steps the toolbar down until it fits:
 *
 *   0 — everything inline.
 *   1 — Expand/Collapse moves into the "⋮" menu (the pane-level tools live there already).
 *   2 — tabs (and Send) also become icon-only, each keeping its name as tooltip + accessible name.
 *
 * Going back up uses the header width each level NEEDED when it last overflowed, so the toolbar does not flicker
 * between levels: a level returns only once the header is at least that wide again.
 */
export type ToolbarFitLevel = 0 | 1 | 2;

const MAX_LEVEL: ToolbarFitLevel = 2;

/**
 * The pure step decision (one step per layout pass).
 *
 * @param level      the current level
 * @param overflow   how far the tab content overflows its container, in px (`scrollWidth - clientWidth`)
 * @param headerWidth the toolbar's current width, in px
 * @param needed     per level, the header width that level needed when it last overflowed (mutated on step-down)
 */
export function nextToolbarFitLevel(
  level: ToolbarFitLevel,
  overflow: number,
  headerWidth: number,
  needed: number[]
): ToolbarFitLevel {
  if (overflow > 1 && level < MAX_LEVEL) {
    needed[level] = headerWidth + overflow;
    return (level + 1) as ToolbarFitLevel;
  }
  if (level > 0 && overflow <= 1) {
    const previousNeeded = needed[level - 1];
    if (previousNeeded !== undefined && headerWidth >= previousNeeded + 1) {
      return (level - 1) as ToolbarFitLevel;
    }
  }
  return level;
}

/**
 * @param contentKey changes whenever the row's items change (which tabs, whether Send / Expand are offered), so the
 *                   row is re-measured then as well as on a resize.
 */
export function useToolbarFit(
  headerRef: RefObject<HTMLElement | null>,
  contentRef: RefObject<HTMLElement | null>,
  contentKey: string
): ToolbarFitLevel {
  const [level, setLevel] = useState<ToolbarFitLevel>(0);
  const [resizeTick, setResizeTick] = useState(0);
  const needed = useRef<number[]>([]);

  // Re-measure when the level, the row's items or the toolbar's size change; each step re-renders, so one pass
  // steps one level and the next pass checks again.
  useLayoutEffect(() => {
    const header = headerRef.current;
    const content = contentRef.current;
    if (!header || !content) return;
    const next = nextToolbarFitLevel(
      level,
      content.scrollWidth - content.clientWidth,
      header.clientWidth,
      needed.current
    );
    if (next !== level) setLevel(next);
  }, [headerRef, contentRef, level, contentKey, resizeTick]);

  useLayoutEffect(() => {
    const header = headerRef.current;
    if (!header || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver(() => setResizeTick(t => t + 1));
    observer.observe(header);
    return () => observer.disconnect();
  }, [headerRef]);

  return level;
}
