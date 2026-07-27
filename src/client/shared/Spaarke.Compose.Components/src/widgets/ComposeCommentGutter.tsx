/**
 * ComposeCommentGutter.tsx — right-rail comment layout aligned to anchors
 * (ai-advanced-capabilities-nda-r1 task 032).
 *
 * Renders one card per comment thread in a RIGHT-SIDE column, absolutely positioned inside
 * `editorScrollWrap` (already `position: relative` — FIX #9), each vertically aligned to the CURRENT
 * ProseMirror position of its `commentAnchor` mark (spec FR-16: "comments on the RIGHT, aligned with
 * suggested edits"). Today wired to the NDA-REVIEW advisory-comment thread instance
 * ({@link ComposeEditorHandle.getAdvisoryCommentThreads}, task 031) — the session Comments panel
 * (`ComposeCommentThread.tsx`) is unaffected and keeps its own docked-panel presentation.
 *
 * LIVE POSITION (ADR-049 binding constraint — "do not trust stale stored positions"): a thread's
 * `anchorText` is a CREATION-TIME snapshot, never updated as the document is edited. This component
 * NEVER reads it for placement. Instead it resolves each thread's `commentAnchor` mark's CURRENT span
 * via {@link findCommentAnchorRange} (`ComposeCommentThread.types.ts`) — the SAME live-position
 * primitive `composeSessionCommentThreadsToAnchoredComments` (task 040) already uses for save-time
 * export — then reads `editor.view.coordsAtPos(span.from)` for the Y coordinate. A thread whose anchor
 * mark is no longer present (a later edit deleted the anchored text) is OMITTED, never guessed a
 * fallback position (mirrors {@link findCommentAnchorRange}'s own never-mis-map discipline).
 *
 * WIDGET-DECORATION PRECEDENT (binding design rationale, per the task's knowledge pointer): this
 * reuses the SAME coordsAtPos-driven positioning approach `TrackChangesExtension.ts` proved for its
 * live redline overlay — a VIEW-layer concern computed from the current doc, recomputed on every
 * transaction, never a content mutation. `TrackChangesExtension` renders ProseMirror `Decoration`s
 * (inside the editor's own DecorationSet); this component instead renders a SEPARATE React overlay
 * (absolutely positioned cards, not itself a decoration) because a comment card is real Fluent v9 UI
 * (badge, buttons, text) — decorations render raw DOM nodes with no React reconciliation, which would
 * make a rich interactive card unmaintainable. The POSITIONING technique (coordsAtPos, relative to the
 * scroll wrap, recomputed on transaction/scroll) is the part reused verbatim; the RENDERING technique
 * (React overlay vs. ProseMirror decoration) differs because the content differs.
 *
 * COLLISION / STACKING: raw Y positions are sorted ascending, then a greedy pass pushes any card whose
 * raw position would overlap the previous card's rendered bottom (+ a fixed gap) down to clear it — the
 * same "no two adjacent cards overlap" rule Google Docs' / Word's margin-comment rails use. Card
 * heights are read from measured DOM refs (falling back to a fixed estimate before first paint), so
 * the stacking math accounts for each card's REAL rendered height, not a guess.
 *
 * REFLOW: recomputed on doc change (`editor.on('transaction', …)`, mirroring the FIX #9 down-arrow
 * FAB's own transaction-listener convention in `ComposeEditor.tsx`), on scroll of the editor's
 * scrollable surface (rAF-throttled), and on window resize.
 *
 * ADR-021: Fluent v9 semantic tokens only (no hex); dark-mode compliant.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `ComposeCommentThread.tsx` is the docked SESSION comments panel (open/create/reply/
 *     resolve) — a different capability (thread MANAGEMENT UI) from this component (thread
 *     PLACEMENT/alignment). `TrackChangesExtension.ts` proves the positioning technique but renders
 *     raw decorations, not interactive Fluent cards — no overlap to extend.
 *   - Extension: folding this into `ComposeCommentThread.tsx` would blur that panel's SCOPE GUARD
 *     (view/create/reply/resolve, no positioning concern) with a live-position/collision-layout
 *     concern; folding it into `TrackChangesExtension.ts` would blur a pure-decoration overlay with
 *     interactive React UI. A new, focused component is the reuse-first move — it reuses the
 *     underlying primitives ({@link findCommentAnchorRange}, `coordsAtPos`, `riskBadgeColor`) rather
 *     than reinventing them.
 *   - Cost-of-doing-nothing: without it, NDA-REVIEW advisory comments have no vertically-aligned,
 *     at-a-glance right-rail presentation — only an in-document underline + the top-of-column
 *     `NdaReviewSummaryPanel` list (spec FR-16 unmet).
 *
 * @see ./ComposeCommentThread.types.ts — `findCommentAnchorRange` (live position), `ComposeCommentThreadModel`
 * @see ./marks/TrackChangesExtension.ts — the coordsAtPos/widget-decoration precedent this reuses
 * @see ./ComposeEditor.tsx — mount point (inside `editorScrollWrap`) + `advisoryComments.threads` source
 * @see ./NdaReviewSummaryPanel.tsx — `riskBadgeColor` (reused for the per-card risk badge)
 * @see projects/ai-advanced-capabilities-nda-r1/spec.md FR-16
 */
import * as React from 'react';
import { type Editor } from '@tiptap/react';
import { Badge, Button, Text, makeStyles, tokens } from '@fluentui/react-components';
import { findCommentAnchorRange, type ComposeCommentThreadModel } from './ComposeCommentThread.types';
import { riskBadgeColor } from './NdaReviewSummaryPanel';

/** Right-rail column width — cards clear of the document's own right margin. */
export const COMMENT_GUTTER_WIDTH_PX = 220;
/** Fixed vertical gap enforced between stacked cards (collision-avoidance pass). */
const CARD_GAP_PX = 8;
/** Fallback card height used ONLY before a card's real height has been measured (first paint). */
const DEFAULT_CARD_HEIGHT_PX = 96;
/**
 * Collapsed-card body character budget (UAT round-2 item #5). Advisory explanations frequently run
 * well past this in the narrow rail, so a collapsed card shows a preview + a "Show more" toggle; the
 * expanded card shows the full text. Kept short so the default (collapsed) rail stays scannable.
 */
const COLLAPSED_BODY_MAX_CHARS = 140;

const useStyles = makeStyles({
  rail: {
    position: 'absolute',
    top: 0,
    right: 0,
    bottom: 0,
    width: `${COMMENT_GUTTER_WIDTH_PX}px`,
    // The rail itself never intercepts clicks over the document beneath it — only individual cards
    // (which re-enable pointer events) are interactive.
    pointerEvents: 'none',
    zIndex: 1,
  },
  card: {
    position: 'absolute',
    right: tokens.spacingHorizontalS,
    left: tokens.spacingHorizontalS,
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow4,
    pointerEvents: 'auto',
    transition: 'top 120ms ease-out',
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalXS,
  },
  sectionRef: {
    color: tokens.colorNeutralForeground2,
  },
  body: {
    color: tokens.colorNeutralForeground1,
  },
  standardRef: {
    color: tokens.colorNeutralForeground3,
  },
  // "Show more"/"Show less" toggle — a compact, left-aligned link-style button (item #5).
  expandToggle: {
    alignSelf: 'flex-start',
    minWidth: 'auto',
    paddingLeft: 0,
    paddingRight: 0,
    height: 'auto',
    fontWeight: tokens.fontWeightRegular,
  },
});

/** Truncated, log-safe-length label (mirrors ComposeCommentThread's / NdaReviewSummaryPanel's helper). */
function truncate(text: string, max: number): string {
  const trimmed = text.trim();
  return trimmed.length > max ? `${trimmed.slice(0, max)}…` : trimmed;
}

export interface ComposeCommentGutterProps {
  /** The live TipTap editor. `null` while unmounted — the gutter renders nothing. */
  editor: Editor | null;
  /** Comment threads to place — today `ComposeEditorHandle.getAdvisoryCommentThreads()`'s source. */
  threads: readonly ComposeCommentThreadModel[];
  /**
   * The editor's SCROLLABLE surface (`editorScrollRef` in `ComposeEditor.tsx`) — its `scroll` events
   * drive reflow. A ref (not the element itself) so the gutter reads the CURRENT DOM node each time,
   * matching the mount-order contract `editorScrollWrap`'s children already rely on (siblings commit
   * together, so the ref is populated before this component's effects run).
   */
  scrollContainerRef: React.RefObject<HTMLDivElement | null>;
}

/** One thread's resolved raw (pre-collision) Y position, `top` relative to the rail's own top edge. */
interface RawCardPosition {
  id: string;
  top: number;
}

/**
 * Pure collision/stacking layout: given raw (unresolved-collision) Y positions and each card's
 * rendered (or fallback-estimated) height, returns the FINAL top offset per thread id such that no
 * two cards overlap — a card is pushed down (never up) just far enough to clear the previous one.
 * Exported for direct unit testing (no editor/DOM dependency).
 */
export function layoutCommentGutterCards(
  raw: readonly RawCardPosition[],
  heights: ReadonlyMap<string, number>
): Record<string, number> {
  const sorted = [...raw].sort((a, b) => a.top - b.top);
  const result: Record<string, number> = {};
  let cursor = Number.NEGATIVE_INFINITY;
  for (const { id, top } of sorted) {
    const height = heights.get(id) ?? DEFAULT_CARD_HEIGHT_PX;
    const placed = cursor === Number.NEGATIVE_INFINITY ? top : Math.max(top, cursor);
    result[id] = placed;
    cursor = placed + height + CARD_GAP_PX;
  }
  return result;
}

export function ComposeCommentGutter(props: ComposeCommentGutterProps): React.JSX.Element | null {
  const { editor, threads, scrollContainerRef } = props;
  const styles = useStyles();
  const railRef = React.useRef<HTMLDivElement | null>(null);
  const cardElementsRef = React.useRef<Map<string, HTMLDivElement>>(new Map());
  const [cardTops, setCardTops] = React.useState<Record<string, number>>({});
  // Per-card expand/collapse (item #5). A Set of thread ids whose full text is shown.
  const [expandedIds, setExpandedIds] = React.useState<ReadonlySet<string>>(() => new Set());

  const toggleExpanded = React.useCallback((id: string): void => {
    setExpandedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const recompute = React.useCallback((): void => {
    if (!editor || !railRef.current) return;
    const railRect = railRef.current.getBoundingClientRect();
    const raw: RawCardPosition[] = [];
    for (const thread of threads) {
      // Live position (ADR-049) — resolve the mark's CURRENT span; never trust `thread.anchorText`.
      const span = findCommentAnchorRange(editor.state.doc, thread.id);
      if (!span) continue; // anchor deleted by a later edit — omit, never guess a fallback position
      try {
        const coords = editor.view.coordsAtPos(span.from);
        raw.push({ id: thread.id, top: coords.top - railRect.top });
      } catch {
        // `coordsAtPos` measures real DOM layout (`Range.getClientRects`) and can throw in an
        // environment without full layout support (e.g. a detached/not-yet-painted view). Skip this
        // thread's placement for this pass rather than crash the whole editor — the next transaction
        // or scroll/resize recompute retries it.
        continue;
      }
    }
    const heights = new Map<string, number>();
    let allMeasured = true;
    for (const { id } of raw) {
      const el = cardElementsRef.current.get(id);
      if (el) heights.set(id, el.offsetHeight);
      else allMeasured = false; // not yet mounted — this pass falls back to DEFAULT_CARD_HEIGHT_PX for it
    }
    const next = layoutCommentGutterCards(raw, heights);

    setCardTops(prev => {
      const prevKeys = Object.keys(prev);
      const nextKeys = Object.keys(next);
      const unchanged =
        prevKeys.length === nextKeys.length &&
        nextKeys.every(id => Math.abs((prev[id] ?? Number.NaN) - next[id]) < 0.5);
      return unchanged ? prev : next;
    });

    // A card that just got its FIRST position (this pass) wasn't in `cardElementsRef` yet (it only
    // mounts once `cardTops` includes it), so this pass used the DEFAULT_CARD_HEIGHT_PX estimate for
    // it — not its real rendered height. Nothing else automatically re-triggers `recompute` once that
    // card mounts (`cardTops` isn't a dependency of the effects below, by design, to avoid a
    // recompute-loop), so without this, the estimate-based stacking would persist until the next
    // scroll/edit. Schedule exactly one follow-up pass after paint — by then the card is mounted and
    // `cardElementsRef` has its real height, so `allMeasured` is true and this doesn't reschedule again.
    if (!allMeasured) {
      requestAnimationFrame(() => recompute());
    }
  }, [editor, threads]);

  // Reflow on doc change — mirrors the FIX #9 down-arrow FAB's `editor.on('transaction', …)` pattern.
  React.useEffect(() => {
    if (!editor) return;
    recompute();
    editor.on('transaction', recompute);
    return () => {
      editor.off('transaction', recompute);
    };
  }, [editor, recompute]);

  // Reflow on scroll of the editor's scrollable surface + window resize (rAF-throttled).
  React.useEffect(() => {
    const el = scrollContainerRef.current;
    if (!el) return;
    let rafId = 0;
    const onScrollOrResize = (): void => {
      cancelAnimationFrame(rafId);
      rafId = requestAnimationFrame(recompute);
    };
    el.addEventListener('scroll', onScrollOrResize, { passive: true });
    window.addEventListener('resize', onScrollOrResize, { passive: true });
    return () => {
      el.removeEventListener('scroll', onScrollOrResize);
      window.removeEventListener('resize', onScrollOrResize);
      cancelAnimationFrame(rafId);
    };
  }, [scrollContainerRef, recompute]);

  // Re-measure after cards render (real heights may differ from DEFAULT_CARD_HEIGHT_PX, e.g. a long
  // explanation wraps to more lines) — a second layout pass corrects the initial estimate-based math.
  React.useLayoutEffect(() => {
    recompute();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-run whenever the thread SET changes
  }, [threads.length, recompute]);

  // Expanding/collapsing a card changes its measured height — re-run the collision layout so cards
  // below reflow to clear (or reclaim) the space (item #5). Runs after the new text has committed to
  // the DOM (useLayoutEffect) so `offsetHeight` reflects the expanded/collapsed size.
  React.useLayoutEffect(() => {
    recompute();
  }, [expandedIds, recompute]);

  if (!editor || threads.length === 0) return null;

  return (
    <div ref={railRef} className={styles.rail} data-testid="compose-comment-gutter">
      {threads.map(thread => {
        const top = cardTops[thread.id];
        if (top === undefined) return null; // anchor unresolved (deleted) — omitted, never guessed
        const fullText = thread.text.trim();
        const isExpanded = expandedIds.has(thread.id);
        const isTruncatable = fullText.length > COLLAPSED_BODY_MAX_CHARS;
        const bodyText = isExpanded || !isTruncatable ? fullText : truncate(fullText, COLLAPSED_BODY_MAX_CHARS);
        return (
          <div
            key={thread.id}
            ref={el => {
              if (el) cardElementsRef.current.set(thread.id, el);
              else cardElementsRef.current.delete(thread.id);
            }}
            className={styles.card}
            style={{ top: `${top}px` }}
            role="complementary"
            aria-label={`Comment${thread.sectionRef ? `: ${thread.sectionRef}` : ''}`}
            data-testid={`compose-comment-gutter-card-${thread.id}`}
          >
            <div className={styles.cardHeader}>
              <Text weight="semibold" size={200} className={styles.sectionRef}>
                {thread.sectionRef ?? 'Comment'}
              </Text>
              {thread.riskLevel ? (
                <Badge
                  appearance="tint"
                  size="small"
                  color={riskBadgeColor(thread.riskLevel)}
                  data-testid={`compose-comment-gutter-risk-${thread.id}`}
                >
                  {thread.riskLevel}
                </Badge>
              ) : null}
            </div>
            <Text size={200} className={styles.body} data-testid={`compose-comment-gutter-body-${thread.id}`}>
              {bodyText}
            </Text>
            {isTruncatable ? (
              <Button
                appearance="transparent"
                size="small"
                className={styles.expandToggle}
                onClick={() => toggleExpanded(thread.id)}
                aria-expanded={isExpanded}
                data-testid={`compose-comment-gutter-expand-${thread.id}`}
              >
                {isExpanded ? 'Show less' : 'Show more'}
              </Button>
            ) : null}
            {thread.standardRef ? (
              <Text size={100} className={styles.standardRef}>
                Standard: {thread.standardRef}
              </Text>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}

ComposeCommentGutter.displayName = 'ComposeCommentGutter';

export default ComposeCommentGutter;
