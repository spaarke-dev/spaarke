import { useLayoutEffect, useRef, useState, type RefObject } from 'react';

/**
 * useToolbarFit — whether the toolbar's tabs show their text labels.
 *
 * Task 108 (owner UAT round 10, item 2: "in word the tool icons show only icon image no word label when the side
 * pane width is normal, and then shows labels when expanded ... change the outlook to match"): ONE rule for both
 * hosts — icons only at a normal pane width, labels once the pane is wide ({@link TOOLBAR_LABELS_MIN_WIDTH}, i.e.
 * expanded). Every icon-only tab keeps its name as tooltip + accessible name.
 *
 * Safety net (task 107): even when wide, if the labelled tabs would overflow their row (more tabs, a narrow host),
 * the row stays icon-only, and labels return only once the toolbar is wider than the width they needed — so the
 * row never overlaps and never flickers.
 */

/**
 * The toolbar width at and above which tabs show labels. Default panes are below it (Word desktop 395, Word on the
 * web 405, Outlook ~340-400); an expanded pane is above it (Word desktop 640, task 108).
 */
export const TOOLBAR_LABELS_MIN_WIDTH = 480;

/**
 * The pure decision (one per layout pass).
 *
 * @param showLabels  whether labels are shown now
 * @param overflow    how far the tab row overflows its container, in px (`scrollWidth - clientWidth`)
 * @param headerWidth the toolbar's current width, in px
 * @param needed      the toolbar width the labels needed when they last overflowed (0 = never), mutated
 */
export function nextShowLabels(
  showLabels: boolean,
  overflow: number,
  headerWidth: number,
  needed: { width: number }
): boolean {
  if (headerWidth < TOOLBAR_LABELS_MIN_WIDTH) return false;
  if (showLabels) {
    if (overflow > 1) {
      needed.width = headerWidth + overflow;
      return false;
    }
    return true;
  }
  return headerWidth >= needed.width + 1;
}

/**
 * @param contentKey changes whenever the row's items change (which tabs, whether Send is offered), so the row is
 *                   re-measured then as well as on a resize.
 */
export function useToolbarFit(
  headerRef: RefObject<HTMLElement | null>,
  contentRef: RefObject<HTMLElement | null>,
  contentKey: string
): { showLabels: boolean } {
  const [showLabels, setShowLabels] = useState(false);
  const [resizeTick, setResizeTick] = useState(0);
  const needed = useRef({ width: 0 });

  // Re-measure when the mode, the row's items or the toolbar's size change.
  useLayoutEffect(() => {
    const header = headerRef.current;
    const content = contentRef.current;
    if (!header || !content) return;
    const next = nextShowLabels(
      showLabels,
      content.scrollWidth - content.clientWidth,
      header.clientWidth,
      needed.current
    );
    if (next !== showLabels) setShowLabels(next);
  }, [headerRef, contentRef, showLabels, contentKey, resizeTick]);

  useLayoutEffect(() => {
    const header = headerRef.current;
    if (!header || typeof ResizeObserver === 'undefined') return undefined;
    const observer = new ResizeObserver(() => setResizeTick(t => t + 1));
    observer.observe(header);
    return () => observer.disconnect();
  }, [headerRef]);

  return { showLabels };
}
