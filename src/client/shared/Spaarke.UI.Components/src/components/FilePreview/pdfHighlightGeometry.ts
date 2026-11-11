/**
 * pdfHighlightGeometry — pure box and scroll maths for PdfHighlightViewer.
 *
 * Contract (ebilling-r1 FR-31, prototype F-4): every box is `{x, y, w, h}` normalised 0-1 from the
 * TOP-LEFT of its page. Extraction can return slightly out-of-range values (a box that spills over the page
 * edge, a NaN from a failed read), so everything here clamps before it reaches CSS.
 *
 * No React, no DOM: unit-testable in isolation.
 */

/** A page-relative box, normalised 0-1 from the top-left. */
export interface IPdfNormalisedBox {
  /**
   * 1-based page number as returned by extraction. Informational: the viewer places a box on
   * `IPdfHighlight.page`, so an extraction box can be passed straight through.
   */
  page?: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

/** A box as CSS percentage strings, ready for `left/top/width/height`. */
export interface IPdfCssBox {
  left: string;
  top: string;
  width: string;
  height: string;
}

/** Where the scroller lands relative to its viewport: 30 % down, so the row has context above it. */
export const PDF_HIGHLIGHT_SCROLL_VIEWPORT_FRACTION = 0.3;

/** Clamp to [0, 1]. Non-finite input (NaN, +/-Infinity) becomes 0 so a bad read never produces a stray overlay. */
export function clamp01(n: number): number {
  if (!Number.isFinite(n)) return 0;
  if (n < 0) return 0;
  if (n > 1) return 1;
  return n;
}

/**
 * Clamp a box so it lies fully inside the page: x/y into [0,1], then w/h so that x+w and y+h do not pass 1.
 * Returns null when the box is unusable (null/undefined input, or no area left after clamping).
 */
export function clampBox(box: IPdfNormalisedBox | null | undefined): IPdfNormalisedBox | null {
  if (!box) return null;
  const x = clamp01(box.x);
  const y = clamp01(box.y);
  const w = Math.min(clamp01(box.w), 1 - x);
  const h = Math.min(clamp01(box.h), 1 - y);
  if (w <= 0 || h <= 0) return null;
  return { x, y, w, h };
}

const toPercent = (n: number): string => `${n * 100}%`;

/** Convert a normalised box to CSS percentages (after clamping). Null when the box is unusable. */
export function boxToCss(box: IPdfNormalisedBox | null | undefined): IPdfCssBox | null {
  const c = clampBox(box);
  if (!c) return null;
  return { left: toPercent(c.x), top: toPercent(c.y), width: toPercent(c.w), height: toPercent(c.h) };
}

/**
 * True when `page` is a whole 1-based page number inside the document. A highlight on a page that does not
 * exist is dropped, not moved to the nearest page: boxing the wrong page would point a reviewer at the wrong evidence.
 */
export function isValidPage(page: number, pageCount: number): boolean {
  return Number.isInteger(page) && Number.isInteger(pageCount) && page >= 1 && page <= pageCount;
}

/**
 * Scroll target for a highlighted row (prototype F-4):
 *
 *   page.offsetTop + y x page.offsetHeight - 30 % of the viewport, never below 0
 *
 * `pageOffsetTop` is measured relative to the scroller's content (the caller subtracts the scroller's own
 * offset when the two share an offsetParent).
 */
export function computeScrollTop(
  pageOffsetTop: number,
  pageHeight: number,
  rowY: number,
  viewportHeight: number
): number {
  const target = pageOffsetTop + clamp01(rowY) * pageHeight - viewportHeight * PDF_HIGHLIGHT_SCROLL_VIEWPORT_FRACTION;
  return Math.max(0, target);
}

/**
 * The 1-based pages to keep rendered: every page in `visible` plus `neighbours` pages either side
 * (NFR-02: visible page and its neighbours), clamped to [1, pageCount]. Ascending and de-duplicated.
 */
export function pagesToRender(visible: Iterable<number>, pageCount: number, neighbours = 1): number[] {
  const out = new Set<number>();
  for (const p of Array.from(visible)) {
    if (!Number.isFinite(p)) continue;
    for (let q = p - neighbours; q <= p + neighbours; q += 1) {
      if (q >= 1 && q <= pageCount) out.add(q);
    }
  }
  return Array.from(out).sort((a, b) => a - b);
}
