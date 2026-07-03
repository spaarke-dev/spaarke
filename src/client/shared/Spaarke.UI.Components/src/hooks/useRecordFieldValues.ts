/**
 * useRecordFieldValues — read a single Dataverse record's specified fields via
 * `Xrm.WebApi.retrieveRecord`. Returns `{ values, loading, error }`.
 *
 * FR-05 (record-header-and-notepad-r1): primitive read hook consumed by
 * RecordHeader PCFs to fetch a small field payload (e.g. Matter 5-field header
 * + `sprk_recordsummary`) with one Xrm call per mount / dep-change.
 *
 * Design notes:
 *  - Host-context surface. Uses `Xrm.WebApi` only — no `@spaarke/auth`, no BFF
 *    (spec NFR-05, NFR-07; ADR-028 boundary).
 *  - Reaches `Xrm` through the shared `getXrm()` cross-frame walker so the hook
 *    works from PCF controls, Code Page iframes, or nested iframes.
 *  - React 16/17 compatible (uses `React.useState` / `React.useEffect` /
 *    `React.useCallback` only). No `use()`, `useSyncExternalStore`, or other
 *    React 18-exclusive APIs (ADR-022 / spec NFR-06).
 *  - Dep key includes `JSON.stringify(fields)` so a NEW array reference with
 *    the same contents does NOT trigger a refetch loop. If the field set does
 *    change, `useEffect` re-runs and we issue a fresh Xrm call.
 *  - No retry — errors are surfaced verbatim on `error`. Spec / POML notes
 *    both require surfacing failure to the caller (they decide UX).
 *
 * @see FR-05 in projects/record-header-and-notepad-r1/spec.md
 * @see .claude/adr/ADR-028-spaarke-auth-architecture.md (host-context surface)
 * @see docs/standards/DATA-ACCESS-DECISION-CRITERIA.md (Xrm.WebApi vs BFF)
 */

import * as React from 'react';
import { getXrm } from '../utils/xrmContext';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result shape of `useRecordFieldValues`.
 */
export interface UseRecordFieldValuesResult {
  /**
   * The `retrieveRecord` payload keyed by field logical name. `null` while
   * loading, when `recordId` is empty, or after an error.
   */
  values: Record<string, unknown> | null;
  /**
   * `true` between fetch start and completion. `false` when there is no
   * request in flight (idle, empty `recordId`, completed, errored, or the
   * host has no `Xrm.WebApi`).
   */
  loading: boolean;
  /**
   * The error surfaced by `Xrm.WebApi.retrieveRecord`, OR a synthetic
   * `Error("Xrm.WebApi not available")` when the hook runs outside a Power
   * Platform host (unit tests, Storybook, etc.). `null` otherwise.
   */
  error: Error | null;
  /**
   * Force a re-fetch with the current entity / recordId / fields.
   *
   * v1.0.2: added so inline-edit consumers can pull the persisted row after
   * a successful `Xrm.WebApi.updateRecord`.
   *
   * v1.0.6: accepts `{ silent: true }` to skip the loading state — the
   * refetch runs in the background and `values` swaps in place when the new
   * row arrives. This prevents the "whole PCF refreshes" flash that users
   * see when a lookup save triggers a full skeleton re-render.
   */
  refresh: (options?: { silent?: boolean }) => Promise<void>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Read the specified fields of a Dataverse record via `Xrm.WebApi.retrieveRecord`.
 *
 * Behavior:
 *  - When `recordId` is `null`, empty, or whitespace, NO Xrm call is made and
 *    the hook returns `{ values: null, loading: false, error: null }`. This is
 *    the "no record selected" resting state.
 *  - When `Xrm.WebApi` is not accessible (unit-test / non-MDA host), the hook
 *    returns `{ values: null, loading: false, error: Error("Xrm.WebApi not available") }`
 *    on the first non-empty `recordId`.
 *  - On success, `values` is the raw record object as returned by
 *    `Xrm.WebApi.retrieveRecord` — callers read fields by logical name
 *    (e.g. `values.sprk_matternumber`) or lookup fields via the
 *    `_lookupfield_value` convention (per pattern
 *    `.claude/patterns/pcf/dataverse-queries.md`).
 *  - Refetches when `entity`, `recordId`, or the CONTENTS of `fields` change
 *    (stable dep key `JSON.stringify(fields)` — new-reference-same-contents
 *    does NOT trigger a fetch).
 *  - No retry on failure — the caller decides how to present the error.
 *
 * @param entity   Dataverse entity logical name (e.g. `"sprk_matter"`).
 * @param recordId Record GUID; `null`/empty means "no fetch."
 * @param fields   Array of field logical names to `$select`.
 * @returns `{ values, loading, error }` — see {@link UseRecordFieldValuesResult}.
 *
 * @example
 * ```tsx
 * const { values, loading, error } = useRecordFieldValues(
 *   'sprk_matter',
 *   recordId,
 *   ['sprk_matternumber', 'sprk_mattername', 'sprk_recordsummary'],
 * );
 * ```
 */
export function useRecordFieldValues(
  entity: string,
  recordId: string | null,
  fields: string[]
): UseRecordFieldValuesResult {
  const [values, setValues] = React.useState<Record<string, unknown> | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<Error | null>(null);
  // Manual refresh nonce — bumping this state variable triggers the effect
  // below without the caller having to re-set entity / recordId / fields.
  const [refreshNonce, setRefreshNonce] = React.useState<number>(0);

  // Stable dep key: same-contents / new-reference → same key → no refetch loop.
  // Callers may pass a fresh array literal on every render; that is fine.
  const fieldsKey = JSON.stringify(fields);

  // Build the `$select` OData clause. `useCallback` ensures the effect body
  // sees a stable function identity for the duration of a given fields set.
  const buildQuery = React.useCallback((): string => {
    // Empty fields → let Xrm return the full record (rare in Spaarke, but
    // defined behavior). A `$select=` with no fields is invalid OData, so
    // we skip the query string entirely in that case.
    if (!fields || fields.length === 0) {
      return '';
    }
    return `?$select=${fields.join(',')}`;
    // Depend on the stable key, not the array identity (per POML step 2).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fieldsKey]);

  React.useEffect(() => {
    // No record selected → resting state, no fetch.
    if (recordId === null || recordId === undefined || recordId.trim() === '') {
      setValues(null);
      setLoading(false);
      setError(null);
      return;
    }

    // Host-context check. If Xrm.WebApi is not available (unit tests,
    // Storybook, non-MDA hosts), surface a synthetic error immediately.
    const xrm = getXrm();
    if (!xrm || !xrm.WebApi) {
      setValues(null);
      setLoading(false);
      setError(new Error('Xrm.WebApi not available'));
      return;
    }

    // Guard against setState-after-unmount / stale-response overwrite when
    // `recordId` or fields change mid-flight.
    let cancelled = false;

    setLoading(true);
    setError(null);

    const query = buildQuery();

    xrm.WebApi.retrieveRecord(entity, recordId, query).then(
      (record: Record<string, unknown>) => {
        if (cancelled) return;
        setValues(record);
        setLoading(false);
      },
      (err: unknown) => {
        if (cancelled) return;
        // Preserve the original error object where possible; otherwise wrap.
        const wrapped = err instanceof Error ? err : new Error(typeof err === 'string' ? err : 'retrieveRecord failed');
        setValues(null);
        setError(wrapped);
        setLoading(false);
      }
    );

    return () => {
      cancelled = true;
    };
    // buildQuery is memoized on fieldsKey, so listing it here is safe; we also
    // include the raw fieldsKey to make the dep dependency explicit to reviewers.
    // `refreshNonce` is listed so `refresh()` triggers a re-run.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entity, recordId, fieldsKey, refreshNonce]);

  // Refresh helper.
  //
  // Default: bumps the nonce so the effect re-runs — sets loading=true
  // transitionally which is fine for user-triggered manual refreshes.
  //
  // `{ silent: true }` mode: refetches in the background WITHOUT touching
  // loading/error state, then swaps `values` in place on success. Prevents
  // the "whole PCF flash" that consumers observe when the RecordHeaderShell
  // renders its skeleton in response to a transient loading=true. This is
  // the correct mode after an inline field save.
  const refresh = React.useCallback(
    async (options?: { silent?: boolean }): Promise<void> => {
      if (!options?.silent) {
        setRefreshNonce(prev => prev + 1);
        return;
      }
      // Silent path: replicate the effect body without setLoading / setError.
      if (recordId === null || recordId === undefined || recordId.trim() === '') return;
      const xrm = getXrm();
      if (!xrm || !xrm.WebApi) return;
      try {
        const record = await xrm.WebApi.retrieveRecord(entity, recordId, buildQuery());
        setValues(record);
      } catch {
        // Swallow — silent refresh should not disturb the visible error
        // state on save. If the row genuinely became inaccessible, a
        // subsequent user-triggered refresh will surface the error.
      }
    },
    [entity, recordId, buildQuery]
  );

  return { values, loading, error, refresh };
}
