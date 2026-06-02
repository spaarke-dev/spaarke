/**
 * useLazyLoad — FetchXML paging-cookie chain hook for the DataGrid framework.
 *
 * Manages the lazy-infinite-scroll lifecycle for a single FetchXML query:
 *   1. Initial fetch (page 1, no cookie) → returns `{ entities, moreRecords, pagingCookie }`.
 *   2. `fetchNextPage()` re-issues the FetchXML with `page="N+1" paging-cookie="..."`.
 *   3. Accumulates records across pages; consumer's `records` array grows.
 *   4. `reset()` discards accumulated state and re-fetches page 1.
 *
 * **IntersectionObserver setup is the CALLER's responsibility** — this hook only
 * exposes the imperative `fetchNextPage` + `hasMore` + `isLoading` knobs. The
 * DataGrid component wires an observer onto a sentinel `<div ref>` at the bottom
 * of its body and calls `fetchNextPage()` when the sentinel enters the viewport.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/design.md §11.5.1
 * **FR**: FR-DG-12 (lazy paging with IntersectionObserver), FR-DG-13 (Fluent v9 native)
 * **ADR**: ADR-022 (React-16-safe — uses only `useState`, `useEffect`, `useCallback`,
 *          `useRef`; NO `useId`, `useSyncExternalStore`, `useTransition`).
 */

import * as React from 'react';
import type {
  IDataverseClient,
  FetchMultipleResult,
} from '../../services/IDataverseClient';

/**
 * Caller-provided knobs for {@link useLazyLoad}.
 */
export interface UseLazyLoadOptions {
  /** The Dataverse client to query through. Cannot be null; pass a mock in tests. */
  dataverseClient: IDataverseClient;
  /** Logical name of the primary entity being queried. */
  entityName: string;
  /**
   * The base FetchXML query — without the framework's `page` / `count` / `paging-cookie`
   * attributes. This hook will mutate the top-level `<fetch>` element on each request
   * to inject the paging parameters.
   *
   * When this string changes (e.g., filter chip selection changed), the hook
   * automatically resets and re-fetches page 1.
   */
  fetchXml: string;
  /**
   * Records per page. Default `100` per FR-DG-12. Setting to `0` or negative
   * skips pagination injection entirely (single-page fetch).
   */
  pageSize?: number;
}

/**
 * Return shape of {@link useLazyLoad}.
 *
 * @template T - Row shape (defaults to a key/value bag).
 */
export interface UseLazyLoadResult<T = Record<string, unknown>> {
  /** Accumulated records across all loaded pages. */
  records: T[];
  /** `true` while a fetch is in flight (initial OR subsequent page). */
  isLoading: boolean;
  /** `true` if `dataverseClient.retrieveMultipleRecords` reported `moreRecords`. */
  hasMore: boolean;
  /** Last error from a fetch attempt, or `null`. */
  error: Error | null;
  /**
   * Trigger the next page. Idempotent if already loading or no more pages.
   * Safe to call from an IntersectionObserver callback.
   */
  fetchNextPage: () => void;
  /** Discard accumulated state and re-fetch page 1. Use after a filter change. */
  reset: () => void;
}

/**
 * Inject `page` / `count` / `paging-cookie` attributes into the top-level
 * `<fetch>` element. Returns the modified FetchXML string.
 *
 * Tolerant of malformed input: returns the original string on parse failure
 * (the downstream `retrieveMultipleRecords` call will surface the error properly).
 */
function applyPaging(
  fetchXml: string,
  page: number,
  pageSize: number,
  pagingCookie?: string,
): string {
  if (!fetchXml || pageSize <= 0) return fetchXml;
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(fetchXml, 'text/xml');
    if (doc.querySelector('parsererror')) return fetchXml;
    const fetchEl = doc.querySelector('fetch');
    if (!fetchEl) return fetchXml;
    fetchEl.setAttribute('page', String(page));
    fetchEl.setAttribute('count', String(pageSize));
    if (pagingCookie) {
      fetchEl.setAttribute('paging-cookie', pagingCookie);
    } else {
      fetchEl.removeAttribute('paging-cookie');
    }
    const serializer = new XMLSerializer();
    return serializer.serializeToString(doc);
  } catch {
    return fetchXml;
  }
}

/**
 * Lazy-load hook driving a FetchXML paging cookie chain.
 *
 * The hook **does not** own the IntersectionObserver — the caller wires that up
 * on a sentinel ref. Pattern:
 *
 * ```tsx
 * const { records, hasMore, isLoading, fetchNextPage } = useLazyLoad({
 *   dataverseClient, entityName, fetchXml, pageSize: 100,
 * });
 * const sentinelRef = React.useRef<HTMLDivElement>(null);
 * React.useEffect(() => {
 *   const sentinel = sentinelRef.current;
 *   if (!sentinel || !hasMore || isLoading) return;
 *   const obs = new IntersectionObserver(
 *     (entries) => entries[0].isIntersecting && fetchNextPage(),
 *     { rootMargin: '200px' },
 *   );
 *   obs.observe(sentinel);
 *   return () => obs.disconnect();
 * }, [hasMore, isLoading, fetchNextPage]);
 * ```
 *
 * **Cleanup**: the hook tracks an `isMountedRef` to drop fetch-result writes after
 * unmount (prevents the "setState on unmounted component" React 16 warning during
 * hot reload of Storybook stories).
 */
export function useLazyLoad<T = Record<string, unknown>>({
  dataverseClient,
  entityName,
  fetchXml,
  pageSize = 100,
}: UseLazyLoadOptions): UseLazyLoadResult<T> {
  const [records, setRecords] = React.useState<T[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [hasMore, setHasMore] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

  const pageRef = React.useRef<number>(1);
  const cookieRef = React.useRef<string | undefined>(undefined);
  const isMountedRef = React.useRef<boolean>(true);
  // Track in-flight request to suppress out-of-order results when fetchXml changes mid-flight.
  const requestIdRef = React.useRef<number>(0);

  React.useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  const loadPage = React.useCallback(
    (mode: 'reset' | 'next') => {
      if (!fetchXml || !entityName) return;

      const myRequestId = ++requestIdRef.current;
      const targetPage = mode === 'reset' ? 1 : pageRef.current + 1;
      const cookie = mode === 'reset' ? undefined : cookieRef.current;

      setIsLoading(true);
      setError(null);

      const pagedFetch = applyPaging(fetchXml, targetPage, pageSize, cookie);

      dataverseClient
        .retrieveMultipleRecords<T>(entityName, pagedFetch)
        .then((result: FetchMultipleResult<T>) => {
          if (!isMountedRef.current || requestIdRef.current !== myRequestId) {
            return;
          }
          pageRef.current = targetPage;
          cookieRef.current = result.pagingCookie;
          setRecords((prev) => (mode === 'reset' ? result.entities : prev.concat(result.entities)));
          setHasMore(result.moreRecords === true);
          setIsLoading(false);
        })
        .catch((err: unknown) => {
          if (!isMountedRef.current || requestIdRef.current !== myRequestId) {
            return;
          }
          setError(err instanceof Error ? err : new Error(String(err)));
          setIsLoading(false);
        });
    },
    [dataverseClient, entityName, fetchXml, pageSize],
  );

  // Auto-reset on fetchXml / entityName / pageSize change.
  React.useEffect(() => {
    pageRef.current = 0;
    cookieRef.current = undefined;
    setRecords([]);
    setHasMore(false);
    loadPage('reset');
    // loadPage is stable across the dep set (it changes IFF its deps change), so re-running here
    // is equivalent to re-running on the underlying dep change.
  }, [loadPage]);

  const fetchNextPage = React.useCallback(() => {
    if (isLoading || !hasMore) return;
    loadPage('next');
  }, [isLoading, hasMore, loadPage]);

  const reset = React.useCallback(() => {
    pageRef.current = 0;
    cookieRef.current = undefined;
    setRecords([]);
    setHasMore(false);
    loadPage('reset');
  }, [loadPage]);

  return { records, isLoading, hasMore, error, fetchNextPage, reset };
}
