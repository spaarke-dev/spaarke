/**
 * useRelatedCount — filter-agnostic $count query hook.
 *
 * FR-06 (record-header-and-notepad-r1): primitive count hook consumed by
 * record-header toolbar actions to badge sparkle / checkmark / annotation
 * icons with related-record counts (e.g. sprk_todo count, sprk_memo count).
 *
 * Signature: `useRelatedCount(relatedEntity, filter)` — the caller supplies
 * the entity logical name AND the fully-formed OData `$filter` expression.
 * The hook is agnostic to the filter shape:
 *
 *  - `sprk_todo` count uses the standard polymorphic pattern:
 *      `_regardingobjectid_value eq {guid}`
 *  - `sprk_memo` count uses the ADR-024 dual-field entity-specific lookup:
 *      `_sprk_regardingmatter_value eq {guid}` (for Matter), etc.
 *      Consumers use `buildMemoFilterForParent()` from
 *      `./toolbarLaunchDefaults` to construct this.
 *
 * When `filter === null` the hook treats the surface as unsupported: no fetch
 * is issued, `count = 0`, `loading = false`, `error = null`. This is the
 * "memos not applicable to this parent entity" resting state.
 *
 * Refresh model per spec FR-11: mount + `window` focus event only. Zero
 * polling. Focus listener is registered exactly once per mount and cleaned
 * up on unmount.
 *
 * Boundary constraints:
 *  - Host-context surface. Uses `Xrm.WebApi` only — no `@spaarke/auth`, no
 *    BFF (spec NFR-05, NFR-07; ADR-028 host-context boundary).
 *  - Reaches `Xrm` through the shared `getXrm()` cross-frame walker so the
 *    hook works from PCF controls, Code Page iframes, or nested iframes.
 *  - React 16/17 compatible (uses `React.useState` / `React.useEffect` /
 *    `React.useCallback` / `React.useRef` only). No `use()`,
 *    `useSyncExternalStore`, or other React 18-exclusive APIs (ADR-022 /
 *    spec NFR-06).
 *  - No retry — errors surfaced verbatim on `error` for caller-directed UX.
 *
 * @see FR-06 / FR-11 in projects/record-header-and-notepad-r1/spec.md
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see .claude/adr/ADR-028-spaarke-auth-architecture.md
 * @see .claude/patterns/pcf/dataverse-queries.md
 * @see ./toolbarLaunchDefaults.buildMemoFilterForParent
 */

import * as React from 'react';
import { getXrm } from '../utils/xrmContext';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result shape of `useRelatedCount`.
 */
export interface UseRelatedCountResult {
  /**
   * The related-record count read from the OData `@odata.count` annotation.
   * `0` while loading, when `filter` is `null` (unsupported), when Xrm is
   * unavailable, or after an error. Populated once the query resolves.
   */
  count: number;
  /**
   * `true` between fetch start and completion. `false` when there is no
   * request in flight (idle, filter=null, completed, errored, or no Xrm).
   */
  loading: boolean;
  /**
   * The error surfaced by `Xrm.WebApi.retrieveMultipleRecords`, OR a synthetic
   * `Error("Xrm.WebApi not available")` when the hook runs outside a Power
   * Platform host (unit tests, Storybook, etc.). `null` otherwise.
   */
  error: Error | null;
}

/**
 * OData response shape for a `$count=true&$top=0` query. `@odata.count`
 * carries the total; `entities` is empty because `$top=0`.
 */
interface CountResponse {
  '@odata.count'?: number;
  entities: unknown[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Read the count of records in `relatedEntity` matching `filter` via
 * `Xrm.WebApi.retrieveMultipleRecords` with `$count=true&$top=0`.
 *
 * Behavior:
 *  - When `filter === null`, no Xrm call is made and the hook returns
 *    `{ count: 0, loading: false, error: null }` — the "unsupported surface"
 *    resting state (e.g. sprk_memo when the parent entity is not one of the
 *    six supported ADR-024 lookups).
 *  - When `Xrm.WebApi` is not accessible (unit-test / non-MDA host), the
 *    hook returns `{ count: 0, loading: false, error: Error("Xrm.WebApi not available") }`.
 *  - On success, `count` is populated from the OData `@odata.count`
 *    annotation on the response.
 *  - Refetches when `relatedEntity` or `filter` change, and when the browser
 *    `window` receives a focus event (spec FR-11: mount + focus, no polling).
 *  - Cancellation flag guards against setState-after-unmount and stale
 *    responses when `filter` changes mid-flight.
 *
 * @param relatedEntity Dataverse entity logical name to count (e.g. `"sprk_memo"`).
 * @param filter        Fully-formed OData `$filter` expression, or `null` to
 *                      skip the fetch (unsupported surface).
 * @returns `{ count, loading, error }` — see {@link UseRelatedCountResult}.
 *
 * @example Memo count for a Matter parent (ADR-024 dual-field lookup):
 * ```tsx
 * const filter = buildMemoFilterForParent('sprk_matter', matterId);
 * const { count, loading, error } = useRelatedCount('sprk_memo', filter);
 * ```
 *
 * @example Todo count for any regarding parent (standard polymorphic lookup):
 * ```tsx
 * const { count } = useRelatedCount(
 *   'sprk_todo',
 *   `_regardingobjectid_value eq ${regardingId}`,
 * );
 * ```
 */
export function useRelatedCount(relatedEntity: string, filter: string | null): UseRelatedCountResult {
  const [count, setCount] = React.useState<number>(0);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);

  // Track the LATEST fetch call so a stale in-flight response (from a
  // superseded `filter`) does not overwrite fresher state. We also use it to
  // guard against setState-after-unmount.
  const runIdRef = React.useRef<number>(0);
  const mountedRef = React.useRef<boolean>(true);

  // Core fetch function. Stable identity is important so the focus listener
  // (registered once per mount below) always reads the current `relatedEntity`
  // + `filter`. We use refs for those inputs so the callback is truly stable.
  const relatedEntityRef = React.useRef<string>(relatedEntity);
  const filterRef = React.useRef<string | null>(filter);
  relatedEntityRef.current = relatedEntity;
  filterRef.current = filter;

  const fetchCount = React.useCallback((): void => {
    const currentEntity = relatedEntityRef.current;
    const currentFilter = filterRef.current;

    // Unsupported surface → resting state, no fetch.
    if (currentFilter === null) {
      setCount(0);
      setLoading(false);
      setError(null);
      return;
    }

    // Host-context check. If Xrm.WebApi is not available (unit tests, non-MDA
    // hosts), surface a synthetic error immediately.
    const xrm = getXrm();
    if (!xrm || !xrm.WebApi) {
      setCount(0);
      setLoading(false);
      setError(new Error('Xrm.WebApi not available'));
      return;
    }

    // Bump the run id BEFORE dispatching. When the promise settles we compare
    // the captured id to the ref; if a newer fetch has started, we drop this
    // result (stale-response guard).
    runIdRef.current += 1;
    const thisRun = runIdRef.current;

    setLoading(true);
    setError(null);

    const query = `?$filter=${currentFilter}&$count=true&$top=0`;

    xrm.WebApi.retrieveMultipleRecords(currentEntity, query).then(
      (result: CountResponse) => {
        if (!mountedRef.current || thisRun !== runIdRef.current) return;
        const raw = result['@odata.count'];
        const value = typeof raw === 'number' ? raw : 0;
        setCount(value);
        setLoading(false);
      },
      (err: unknown) => {
        if (!mountedRef.current || thisRun !== runIdRef.current) return;
        const wrapped =
          err instanceof Error ? err : new Error(typeof err === 'string' ? err : 'retrieveMultipleRecords failed');
        setCount(0);
        setError(wrapped);
        setLoading(false);
      }
    );
    // No deps — the callback reads its inputs from refs so it stays stable
    // across renders. Effects below own the "when to call" logic.
  }, []);

  // Fetch on mount + whenever `relatedEntity` or `filter` change.
  React.useEffect(() => {
    fetchCount();
    // fetchCount is stable (see comment above); relatedEntity + filter drive
    // the effect. Refs are updated synchronously before this effect runs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [relatedEntity, filter]);

  // Register the focus listener ONCE per mount. Spec FR-11: refresh on
  // window-focus, never poll.
  React.useEffect(() => {
    mountedRef.current = true;

    // Guard for non-browser hosts (SSR, some unit-test setups).
    if (typeof window === 'undefined') {
      return () => {
        mountedRef.current = false;
      };
    }

    const handler = (): void => {
      fetchCount();
    };
    window.addEventListener('focus', handler);

    return () => {
      mountedRef.current = false;
      window.removeEventListener('focus', handler);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return { count, loading, error };
}
