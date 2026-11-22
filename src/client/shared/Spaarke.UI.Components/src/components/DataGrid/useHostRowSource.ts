/**
 * useHostRowSource — paging over a HOST-supplied row loader for the DataGrid (`rowSource` prop).
 *
 * The FetchXML path ({@link useLazyLoad}) reads rows through `IDataverseClient`. Some hosts already have an
 * authorised route that returns the rows (for example the external SPA's record documents list), so they give the
 * grid a page loader instead. The grid still resolves its columns, labels and renderers from the configuration;
 * only the rows come from here. Data fetching stays with the host (ADR-012): this hook only calls the callback.
 *
 * Contract:
 *  - Page 1 loads when `enabled` turns true, and again whenever `rowSourceKey` or `pageSize` changes (the host bumps
 *    the key to refresh, e.g. after an upload or delete). `reset()` does the same on demand (the grid's Refresh).
 *  - `fetchNextPage()` asks for page N+1 while `hasMore` is true; rows are appended (lazy scroll, ADR-051).
 *  - Each request gets an `AbortSignal`. The in-flight request is aborted on unmount, on a key / page-size change and
 *    when a reset starts; a late answer from an aborted or superseded request is ignored.
 *  - A page that returns zero rows ends paging even if it says `hasMore` (an empty page cannot grow the list).
 *  - A rejected page sets `error` and stops paging (`hasMore` false) so the sentinel does not retry in a loop; the
 *    rows already shown stay. `truncated: true` on any page is kept until the next page-1 load.
 *  - The latest `rowSource` is read on every request, so an inline (unstable) callback does not reload the grid;
 *    change `rowSourceKey` to reload.
 *
 * **ADR**: ADR-012 (callback, no fetching in the lib), ADR-022 (React-16-safe: useState / useEffect / useCallback /
 * useRef only), ADR-051 (paged lazy scroll).
 */

import * as React from 'react';

/** What the grid asks a host row source for. `page` is 1-based. */
export interface DataGridRowSourceRequest {
  page: number;
  pageSize: number;
  /** Aborted when the grid unmounts, the `rowSourceKey` changes or the grid reloads page 1. */
  signal: AbortSignal;
}

/** One page of rows from a host row source. */
export interface DataGridRowSourceResult {
  /** Rows in the shape of the configuration's columns (logical-name keys, optional `@formattedValues`). */
  rows: ReadonlyArray<Record<string, unknown>>;
  /** Whether another page exists. A page with zero rows stops paging even when this is true. */
  hasMore: boolean;
  /** The host knows the list is incomplete (for example a server row cap): the grid shows a notice. */
  truncated?: boolean;
}

/**
 * A host page loader for the DataGrid (`rowSource` prop).
 *
 * Host obligations:
 *  - **Reject with a message fit for users.** The grid's error banner shows the rejection's `Error.message` as-is
 *    (a non-Error rejection is shown as its string); keep status codes, stack text and internal detail out of it.
 *  - **Every row carries the configuration's primary id attribute** (for example `sprk_documentid`). A row without
 *    it gets a synthetic `__row_N` id, which is what `onRecordOpen` / `onRecordAction` then receive as the record id.
 */
export type DataGridRowSource = (request: DataGridRowSourceRequest) => Promise<DataGridRowSourceResult>;

/** Singular and plural noun for the grid's rows, used in the incomplete-list notice (`rowNoun` prop). */
export interface DataGridRowNoun {
  /** For a count of 1, e.g. "document". */
  one: string;
  /** For any other count, e.g. "documents". */
  other: string;
}

export interface UseHostRowSourceOptions {
  rowSource: DataGridRowSource | undefined;
  rowSourceKey?: string;
  pageSize: number;
  /** Load only when true (the grid turns it on once a row source is given and its configuration has resolved). */
  enabled: boolean;
}

export interface UseHostRowSourceResult {
  records: Record<string, unknown>[];
  isLoading: boolean;
  hasMore: boolean;
  truncated: boolean;
  error: Error | null;
  fetchNextPage: () => void;
  reset: () => void;
}

export function useHostRowSource({
  rowSource,
  rowSourceKey,
  pageSize,
  enabled,
}: UseHostRowSourceOptions): UseHostRowSourceResult {
  const [records, setRecords] = React.useState<Record<string, unknown>[]>([]);
  const [isLoading, setIsLoading] = React.useState<boolean>(false);
  const [hasMore, setHasMore] = React.useState<boolean>(false);
  const [truncated, setTruncated] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

  // Latest callback, read per request (an inline host callback must not reload the grid every render).
  const sourceRef = React.useRef<DataGridRowSource | undefined>(rowSource);
  sourceRef.current = rowSource;

  const pageRef = React.useRef<number>(0);
  const controllerRef = React.useRef<AbortController | null>(null);
  const requestIdRef = React.useRef<number>(0);

  const loadPage = React.useCallback(
    (mode: 'reset' | 'next') => {
      const source = sourceRef.current;
      if (!enabled || !source) return;

      controllerRef.current?.abort();
      const controller = new AbortController();
      controllerRef.current = controller;
      const myRequestId = ++requestIdRef.current;
      const targetPage = mode === 'reset' ? 1 : pageRef.current + 1;

      setIsLoading(true);
      setError(null);

      let pending: Promise<DataGridRowSourceResult>;
      try {
        pending = Promise.resolve(source({ page: targetPage, pageSize, signal: controller.signal }));
      } catch (err) {
        pending = Promise.reject(err);
      }

      pending.then(
        result => {
          if (controller.signal.aborted || requestIdRef.current !== myRequestId) return;
          pageRef.current = targetPage;
          const rows = Array.isArray(result?.rows) ? (result.rows as Record<string, unknown>[]) : [];
          setRecords(prev => (mode === 'reset' ? rows.slice() : prev.concat(rows)));
          // A page with no rows ends paging even when the host says `hasMore`: an empty page never grows the list, so
          // the sentinel would otherwise request the next page again and again.
          setHasMore(result?.hasMore === true && rows.length > 0);
          const pageTruncated = result?.truncated === true;
          setTruncated(prev => (mode === 'reset' ? pageTruncated : prev || pageTruncated));
          setIsLoading(false);
        },
        (err: unknown) => {
          if (controller.signal.aborted || requestIdRef.current !== myRequestId) return;
          setError(err instanceof Error ? err : new Error(String(err)));
          // Stop paging: with hasMore left true the sentinel would re-request the failing page in a loop.
          setHasMore(false);
          setIsLoading(false);
        }
      );
    },
    [enabled, pageSize]
  );

  // Page 1 on enable / key change / page-size change. Cleanup aborts the in-flight request (key change, unmount).
  React.useEffect(() => {
    pageRef.current = 0;
    // Functional / same-value updates so a grid WITHOUT a row source (enabled false, nothing loaded) does not
    // re-render because of this hook.
    setRecords(prev => (prev.length === 0 ? prev : []));
    setHasMore(false);
    setTruncated(false);
    setError(null);
    if (enabled) {
      loadPage('reset');
    } else {
      setIsLoading(false);
    }
    return () => {
      controllerRef.current?.abort();
      controllerRef.current = null;
    };
  }, [loadPage, enabled, rowSourceKey]);

  const fetchNextPage = React.useCallback(() => {
    if (isLoading || !hasMore) return;
    loadPage('next');
  }, [isLoading, hasMore, loadPage]);

  const reset = React.useCallback(() => {
    pageRef.current = 0;
    setRecords(prev => (prev.length === 0 ? prev : []));
    setHasMore(false);
    setTruncated(false);
    loadPage('reset');
  }, [loadPage]);

  return { records, isLoading, hasMore, truncated, error, fetchNextPage, reset };
}
