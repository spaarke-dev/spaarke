import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';

/**
 * useLazyResults — progressive DOM reveal over an ALREADY-FETCHED, bounded array
 * (spaarkeai-word-add-in-r1 task 034, Path 2).
 *
 * **This is not a data-fetching hook and it issues NO network requests.** It exists because
 * `GET /api/ai/visualization/related/{documentId}` — the only source for the Find tab's results,
 * hardened by task 032 — has no `skip`/`offset`/`cursor` of any kind (confirmed by reading
 * `VisualizationQueryParameters`, `IVisualizationService`, and `VisualizationService`; see
 * `projects/spaarkeai-word-add-in-r1/notes/034-route-paging-escalation.md`). It returns ONE complete,
 * authorization-trimmed, bounded response (at most 50 rows) per call — there is no "page 2" to ask
 * for. The owner decided (2026-09-15) that this list gets a documented, Find-only exception to
 * ADR-051's literal "fetch progressively" MUST: treat the single response as complete, and use
 * `IntersectionObserver` only to reveal more of what is ALREADY in hand as the user scrolls, never to
 * trigger another request.
 *
 * `hasMore` therefore means ONLY "`items` holds rows beyond what has been revealed to the DOM so far"
 * — never "the server might have more". It becomes `false` once every item has been revealed, and
 * scrolling further does nothing (there is nothing left to reveal and no route to ask for more).
 *
 * A NEW `items` array (a newly loaded document's results) resets the reveal window back to the first
 * chunk — callers should pass a stable array reference for the SAME result set and a genuinely new
 * one only when the underlying data changes.
 */
export interface UseLazyResultsResult<T> {
  /** The prefix of `items` revealed to the DOM so far. */
  visibleItems: T[];
  /** True only while `items` holds rows beyond `visibleItems`. False once everything is shown. */
  hasMore: boolean;
  /** Reveals the next chunk of `items`. Never issues a network call. */
  revealNext: () => void;
  /** Attach to a sentinel element rendered immediately after the last revealed row. */
  sentinelRef: RefObject<HTMLDivElement | null>;
}

const DEFAULT_CHUNK_SIZE = 20;
const SENTINEL_ROOT_MARGIN = '200px';

export function useLazyResults<T>(items: T[], chunkSize: number = DEFAULT_CHUNK_SIZE): UseLazyResultsResult<T> {
  const [revealedCount, setRevealedCount] = useState(() => Math.min(chunkSize, items.length));

  // A new `items` array reference (a newly loaded document's results) resets the reveal window.
  // Intentionally keyed on `items` itself, not its length — a same-length-but-different-content array
  // (e.g. a different source document) must also reset to the first chunk, not keep an unrelated count.
  useEffect(() => {
    setRevealedCount(Math.min(chunkSize, items.length));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items]);

  const hasMore = revealedCount < items.length;

  const revealNext = useCallback(() => {
    setRevealedCount(current => Math.min(current + chunkSize, items.length));
  }, [chunkSize, items.length]);

  const sentinelRef = useRef<HTMLDivElement>(null);

  // Sentinel-driven reveal (`.claude/patterns/ui/infinite-scroll-list.md`'s mechanic, minus the
  // fetch): an IntersectionObserver on the bottom sentinel reveals the next chunk ~200px before the
  // sentinel enters the viewport. Re-created only when `hasMore` flips (nothing to observe once
  // everything is revealed) or `revealNext` changes identity (a new `items`/`chunkSize`) — NOT on
  // every `revealedCount` change, since the same sentinel DOM node stays mounted as the list grows.
  useEffect(() => {
    if (!hasMore) {
      return;
    }

    const node = sentinelRef.current;
    if (!node || typeof IntersectionObserver === 'undefined') {
      return;
    }

    const observer = new IntersectionObserver(
      entries => {
        if (entries.some(entry => entry.isIntersecting)) {
          revealNext();
        }
      },
      { rootMargin: SENTINEL_ROOT_MARGIN, threshold: 0 }
    );

    observer.observe(node);
    return () => observer.disconnect();
  }, [hasMore, revealNext]);

  return {
    visibleItems: items.slice(0, revealedCount),
    hasMore,
    revealNext,
    sentinelRef,
  };
}
