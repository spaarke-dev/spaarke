import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient, ApiClientError } from '@shared/services';
import type { DocumentProfileOutcome } from './useDocumentProfile';

/**
 * useFindRecordMatches — the RECORDS half of the Find tab (spaarkeai-word-add-in-r1 task 077, gap (a); FR-16).
 *
 * **The bridge (discovery finding F-c).** No single endpoint returns similar documents AND records.
 * `GET /api/ai/visualization/related/{documentId}` is documents-only; `POST /api/ai/search/records` has
 * real per-row authorization but is query-TEXT driven and accepts no source document. This hook composes
 * the two client-side — no new BFF route (task 077's escalation trigger 1):
 *
 * | Half | Source | Authorization |
 * |---|---|---|
 * | Documents | visualization route (unchanged, fetched by `FindView`) | source-document auth + task 032's per-row trim |
 * | Records   | `POST /api/ai/search/records`, seeded by THIS document's AI profile | `RecordSearchAuthorizationFilter` + `AuthorizeRowsAsync` |
 *
 * Records MUST come from that route (task 077 constraint): it is the only record path with a per-row
 * check, and `RecordSearchEndpoints` refuses outright if its authorization filter is ever detached.
 *
 * **The seed — and why task 034's objection no longer applies.** Task 034 declined this bridge
 * (`notes/034-records-bridge-decision.md`) because every seed it considered was bad: the title is often
 * generic (`Document1.docx`) and there is no body-text extraction seam. It never considered the AI
 * profile. `sprk_filekeywords` is a purpose-built extraction produced by the profiling pipeline, which is
 * what a search query should be made of. Falls back to the TL;DR, then the summary, because keywords are
 * empty until profiling completes. See {@link deriveRecordSearchSeed}.
 *
 * **Honesty (034 §4 condition ii).** These are records matched on the document's SUBJECT MATTER by a text
 * query — not records vector-similar to its content. Nothing in the current index supports the latter.
 * The UI must say so; `FindResultsList` renders the source of the seed alongside the heading.
 *
 * **Paging — ADR-051, and a deliberate reading of its "page fullness" rule (034 §4 condition iii).**
 * Unlike the documents route (no cursor at all — the owner's 2026-09-15 reveal-only exception), this
 * route pages by `offset` (0–1000). So records load PROGRESSIVELY per ADR-051: an `IntersectionObserver`
 * on a bottom sentinel fetches the next page ~200px before it scrolls into view. No pager of any kind.
 *
 * ⚠️ ADR-051's literal rule is *full page ⇒ more, short page ⇒ end*. That premise does not hold here:
 * `AuthorizeRowsAsync` REMOVES rows the caller cannot read, so a page of 25 search hits with 3 unreadable
 * rows arrives as 22. The server's own remarks call such a page "indistinguishable from nothing matched".
 * Following the letter would stop after page 1 whenever page 1 held one unreadable record — which is the
 * exact "shows only the first page" failure ADR-051 exists to prevent. So this hook uses
 * **non-empty page ⇒ more, empty page ⇒ end**, and advances `offset` by the REQUESTED window (the
 * pre-filter search window), never by the number of rows that survived the trim — advancing by the
 * returned count would re-request rows already seen. Cost: one extra request at the true end.
 *
 * Known, accepted limitation: a page on which EVERY row is unreadable also arrives empty, so paging stops
 * there even if readable rows follow. That errs toward showing too little, never toward showing what the
 * caller cannot read. Recorded in `notes/077-find-gaps.md`.
 *
 * **Counts are never displayed.** The server rewrites `metadata.totalCount` to the count of rows permitted
 * in THIS page, deliberately, because the pre-filter total would reveal how many records match. It is
 * therefore not a total, and this hook does not read it.
 */

/** The record types the search route accepts (`RecordEntityType.ValidTypes`). */
export const RECORD_MATCH_TYPES = ['sprk_matter', 'sprk_project', 'sprk_invoice'] as const;

/** ADR-051: a sane incremental page (≈25–50). The route accepts 1–50. */
export const RECORD_PAGE_SIZE = 25;

/** The route's `[Range(0, 1000)]` on `options.offset` — requesting past it is a 400, not an empty page. */
export const MAX_RECORD_OFFSET = 1000;

/** The route's `[StringLength(1000)]` on `query`. */
const MAX_QUERY_LENGTH = 1000;

const SENTINEL_ROOT_MARGIN = '200px';

export type RecordSeedSource = 'keywords' | 'tldr' | 'summary';

export interface RecordSearchSeed {
  query: string;
  /** Which profile field supplied the query — rendered in the UI so the match basis is visible. */
  source: RecordSeedSource;
}

/**
 * Pure: the text the record search is seeded with, or `null` when there is nothing honest to search on.
 *
 * Only a COMPLETED profile yields a seed. A pending/failed/absent profile has no keywords, TL;DR or
 * summary, and inventing a query from the title would reintroduce exactly the low-signal seed task 034
 * rejected — so the caller shows "records appear once the AI profile is complete" instead.
 */
export function deriveRecordSearchSeed(outcome: DocumentProfileOutcome): RecordSearchSeed | null {
  if (outcome.kind !== 'completed') {
    return null;
  }

  const candidates: Array<[RecordSeedSource, string]> = [
    ['keywords', outcome.keywords],
    ['tldr', outcome.tldr],
    ['summary', outcome.summary],
  ];

  for (const [source, raw] of candidates) {
    const text = (raw ?? '').replace(/\s+/g, ' ').trim();
    if (text) {
      return { query: truncateAtWordBoundary(text, MAX_QUERY_LENGTH), source };
    }
  }

  return null;
}

function truncateAtWordBoundary(text: string, max: number): string {
  if (text.length <= max) {
    return text;
  }
  const cut = text.slice(0, max);
  const lastSpace = cut.lastIndexOf(' ');
  return (lastSpace > 0 ? cut.slice(0, lastSpace) : cut).trim();
}

/**
 * Minimal mirror of the BFF's `RecordSearchResult` (`Models/Ai/RecordSearch/RecordSearchResult.cs`) —
 * only the fields the Find list renders.
 *
 * Task 092 (UAT-3): added `confidenceScore` + `matchReasons` so the Matching records section can show
 * WHY a record matched. `confidenceScore` mirrors the server's non-nullable `double` (`0.0`–`1.0`,
 * always present). `matchReasons` mirrors the server's `IReadOnlyList<string>?` (semantic captions and
 * "Name match: …" / "Description match: …" strings, up to 5, which MAY contain `<em>…</em>` highlight
 * markup) — rendered only via `renderMatchReason` in `FindResultsList.tsx`, never
 * `dangerouslySetInnerHTML`.
 */
export interface RecordMatch {
  recordId: string;
  recordType: string;
  recordName: string;
  recordDescription?: string | null;
  confidenceScore: number;
  matchReasons?: string[] | null;
  referenceNumbers?: string[] | null;
}

interface RecordSearchResponseShape {
  results?: RecordMatch[] | null;
}

export type RecordMatchesStatus =
  /** No seed — the profile is not complete (or has nothing to search on). No request is made. */
  | 'no-seed'
  /** The FIRST page is in flight. */
  | 'loading'
  /** At least one page has answered. `records` may legitimately be empty. */
  | 'ready'
  /** The first page failed. (A later page failing keeps what is already shown — see `loadMoreError`.) */
  | 'error';

export interface UseFindRecordMatchesResult {
  status: RecordMatchesStatus;
  records: RecordMatch[];
  /** Where the seed came from, for the UI's honesty line. `null` when there is no seed. */
  seedSource: RecordSeedSource | null;
  /** True while a page AFTER the first is in flight. */
  isLoadingMore: boolean;
  /** True while another page may exist (see the paging remarks above). */
  hasMore: boolean;
  /** First-page failure message (status `error`). */
  error: string | null;
  /** A later page failed; rows already shown are kept and paging stops. */
  loadMoreError: string | null;
  /**
   * Attach to a sentinel rendered immediately after the last record row. A CALLBACK ref, deliberately —
   * see the observer effect for why an object ref breaks paging here.
   */
  sentinelRef: (node: HTMLDivElement | null) => void;
}

function errorMessageOf(err: unknown, fallback: string): string {
  if (err instanceof ApiClientError) {
    return err.error.detail || err.error.title || fallback;
  }
  return err instanceof Error && err.message ? err.message : fallback;
}

export function useFindRecordMatches(seed: RecordSearchSeed | null): UseFindRecordMatchesResult {
  // Keyed on the query TEXT, not the seed object: the caller derives a fresh object every render.
  const query = seed?.query ?? null;
  const seedSource = seed?.source ?? null;

  const [status, setStatus] = useState<RecordMatchesStatus>(query ? 'loading' : 'no-seed');
  const [records, setRecords] = useState<RecordMatch[]>([]);
  const [nextOffset, setNextOffset] = useState(0);
  const [hasMore, setHasMore] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null);

  // A response is applied only if it belongs to the CURRENT query — a stale page from a previous seed
  // must never be appended to the new one's results.
  const generationRef = useRef(0);
  // Synchronous guard against a second sentinel callback firing before the first request's state lands.
  const inFlightRef = useRef(false);

  const fetchPage = useCallback(
    async (offset: number, generation: number) => {
      if (!query) return;
      inFlightRef.current = true;
      const isFirstPage = offset === 0;
      if (!isFirstPage) setIsLoadingMore(true);

      try {
        const response = await apiClient.post<RecordSearchResponseShape>('/api/ai/search/records', {
          query,
          recordTypes: [...RECORD_MATCH_TYPES],
          options: { limit: RECORD_PAGE_SIZE, offset },
        });
        if (generation !== generationRef.current) return;

        const page = response?.results ?? [];
        setRecords(previous => {
          // De-duplicate across pages: the index can shift between requests, and a row seen twice
          // would render twice with the same React key.
          const seen = new Set(previous.map(r => `${r.recordType}:${r.recordId}`));
          const fresh = page.filter(r => !seen.has(`${r.recordType}:${r.recordId}`));
          return fresh.length > 0 ? [...previous, ...fresh] : previous;
        });

        const advanced = offset + RECORD_PAGE_SIZE;
        setNextOffset(advanced);
        // Non-empty ⇒ more; empty ⇒ end. NOT page fullness — see the remarks: the authorization trim
        // makes a short page ambiguous.
        setHasMore(page.length > 0 && advanced <= MAX_RECORD_OFFSET);
        setStatus('ready');
      } catch (err) {
        if (generation !== generationRef.current) return;
        if (isFirstPage) {
          setError(errorMessageOf(err, 'Failed to load matching records.'));
          setStatus('error');
        } else {
          // Keep what is already shown; stop paging rather than retry-looping on the sentinel.
          setLoadMoreError(errorMessageOf(err, 'Failed to load more matching records.'));
        }
        setHasMore(false);
      } finally {
        if (generation === generationRef.current) {
          inFlightRef.current = false;
          setIsLoadingMore(false);
        }
      }
    },
    [query]
  );

  // A new query resets everything and loads the first page.
  useEffect(() => {
    generationRef.current += 1;
    const generation = generationRef.current;
    inFlightRef.current = false;
    setRecords([]);
    setNextOffset(0);
    setHasMore(false);
    setIsLoadingMore(false);
    setError(null);
    setLoadMoreError(null);

    if (!query) {
      setStatus('no-seed');
      return;
    }

    setStatus('loading');
    void fetchPage(0, generation);

    // Invalidate this query's in-flight pages on a query change AND on unmount, so a late response never
    // lands in state that no longer belongs to it.
    return () => {
      generationRef.current += 1;
    };
  }, [query, fetchPage]);

  // ⚠️ A CALLBACK ref stored in state — NOT `useRef`. Code review of task 077 found a real race with an
  // object ref: this hook is called in `FindView`, but the sentinel is rendered by `FindResultsList`, which
  // mounts only once the DOCUMENTS request has loaded. Both requests fire together; if records answer
  // first, the observer effect below runs while the sentinel does not exist yet and bails — and with an
  // object ref nothing re-runs it when the sentinel later mounts, so records stop at page 1 forever (the
  // exact failure ADR-051 exists to prevent). Holding the node in state makes its ARRIVAL a dependency.
  // (`useLazyResults` does not need this: it is called in the same component that renders its sentinel.)
  const [sentinelNode, setSentinelNode] = useState<HTMLDivElement | null>(null);
  const sentinelRef = useCallback((node: HTMLDivElement | null) => setSentinelNode(node), []);

  // Sentinel-driven progressive fetch (ADR-051 / `.claude/patterns/ui/infinite-scroll-list.md`).
  // Re-created after every page (`nextOffset` in deps): an IntersectionObserver reports the CURRENT
  // intersection when it starts observing, so if the sentinel is still on screen after a short page it
  // fetches again rather than waiting for a scroll that will never come.
  useEffect(() => {
    if (status !== 'ready' || !hasMore) return;

    const node = sentinelNode;
    if (!node || typeof IntersectionObserver === 'undefined') return;

    const generation = generationRef.current;
    const observer = new IntersectionObserver(
      entries => {
        if (inFlightRef.current) return;
        if (entries.some(entry => entry.isIntersecting)) {
          void fetchPage(nextOffset, generation);
        }
      },
      { rootMargin: SENTINEL_ROOT_MARGIN, threshold: 0 }
    );

    observer.observe(node);
    return () => observer.disconnect();
  }, [status, hasMore, nextOffset, fetchPage, sentinelNode]);

  return { status, records, seedSource, isLoadingMore, hasMore, error, loadMoreError, sentinelRef };
}
