/**
 * useSprkMemoRepository — Notepad's `sprk_memo` CRUD hook (list + create +
 * update) via `Xrm.WebApi` and `PolymorphicResolverService`.
 *
 * Contract (spec FR-14 / FR-15 / FR-17 — REVISED post task 001)
 * ─────────────────────────────────────────────────────────────
 *   List (FR-14):
 *     - Filters memos by the ADR-024 entity-specific lookup for the parent
 *       type (e.g. `_sprk_regardingmatter_value eq {guid}` for Matter).
 *     - $select is fixed: sprk_memoid, sprk_name, sprk_memobody,
 *       sprk_regardingrecordid, createdon, modifiedon + $expand(createdby).
 *     - $orderby createdon desc.
 *
 *   Create (FR-15):
 *     - MUST use `PolymorphicResolverService.applyResolverFields()` to populate
 *       the entity-specific lookup + all 4 resolver text/URL/lookup fields in
 *       one call (ADR-024). Nav-props for `sprk_memo` are discovered via a
 *       Web-API metadata query and cached (`discoverMemoNavProps`).
 *     - `sprk_name` is NOT NULL on the schema; default is `"Untitled"`.
 *     - `sprk_memobody` is initialised empty.
 *     - After create, list is refetched and `currentMemo` is set to the new
 *       record (matches typical "user just added a memo, focus it" flow).
 *
 *   Update (FR-17):
 *     - `updateBody(body)` debounces 1000ms so a burst of keystrokes coalesces
 *       into a single Web-API write. Passing `{ immediate: true }` cancels the
 *       pending timer and writes now — used by Ctrl+Enter and blur handlers.
 *     - `updateName(name)` writes immediately (no debounce). Title changes
 *       are user-visible edits, not typing bursts.
 *     - The debounce timer is stored in a ref and cleared on unmount so a
 *       page navigation right after typing does NOT leak a stale write.
 *
 *   Unsupported parent (spec FR-19, closed schema list):
 *     - If `regardingEntity` is not a key of `SUPPORTED_MEMO_PARENTS`
 *       (Matter, Project, Event, Invoice, Budget, WorkAssignment), the hook
 *       returns `unsupported: true` and skips ALL queries + mutations. The
 *       caller renders the FR-13 MessageBar.
 *
 *   No `Xrm` available (test env / broken host):
 *     - Returns `error` state populated + safe defaults. No throws — a broken
 *       host must not crash React.
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-14, FR-15, FR-17
 * @see projects/record-header-and-notepad-r1/notes/sprk-memo-schema.md
 * @see projects/record-header-and-notepad-r1/notes/design-alignment-corrections.md
 * @see .claude/adr/ADR-024-polymorphic-resolver-pattern.md
 * @see src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts
 */

import * as React from 'react';

import { applyResolverFields } from '@spaarke/ui-components/services';
import type { IPolymorphicWebApi } from '@spaarke/ui-components/services';
import { SUPPORTED_MEMO_PARENTS } from '@spaarke/ui-components';

import type { Memo, MemoRaw } from '../types/memo';
import { discoverMemoNavProps } from './discoverMemoNavProps';

// ---------------------------------------------------------------------------
// Debounce duration
// ---------------------------------------------------------------------------

/**
 * Idle-typing debounce for `updateBody`. Per spec FR-17: "on 1s idle typing
 * (debounced)". Exported so tests can advance fake timers to the exact
 * threshold without magic-number drift.
 */
export const MEMO_BODY_DEBOUNCE_MS = 1000;

// ---------------------------------------------------------------------------
// Parent-entity primary-name field map
// ---------------------------------------------------------------------------

/**
 * Primary-name field per supported parent entity. Used to fetch the display
 * name for `applyResolverFields(..., parentRecordName, ...)`.
 *
 * Values verified via Dataverse MCP `describe('tables/sprk_matter')` etc. in
 * task 001. See `notes/design-alignment-corrections.md` §3 for the Matter
 * correction (`sprk_mattername` NOT `sprk_name`).
 *
 * A hardcoded map for R1 is deliberate: the 6 entities are schema-fixed and
 * the alternative (querying `EntityDefinitions(...)/PrimaryNameAttribute`)
 * costs an extra Web-API round trip on every memo create with no incremental
 * value while all six are known and stable.
 */
export const PARENT_PRIMARY_NAME_FIELD: Record<string, string> = {
  sprk_matter: 'sprk_mattername',
  sprk_project: 'sprk_projectname',
  sprk_event: 'sprk_eventname',
  sprk_invoice: 'sprk_name',
  sprk_budget: 'sprk_name',
  sprk_workassignment: 'sprk_name',
};

// ---------------------------------------------------------------------------
// Public result type
// ---------------------------------------------------------------------------

export interface UseSprkMemoRepositoryResult {
  /** All memos for this regarding record, sorted `createdon desc`. */
  memos: Memo[];
  /** True while the initial list load OR a refetch is in flight. */
  loading: boolean;
  /** Last error surface from list / create / update; consumers may render it. */
  error: Error | null;
  /**
   * True when `regardingEntity` is NOT one of `SUPPORTED_MEMO_PARENTS`. When
   * true, all queries and mutations are no-ops and `memos` is `[]`.
   */
  unsupported: boolean;
  /** The memo currently open in the editor, or `null` if none. */
  currentMemo: Memo | null;
  /**
   * Switch the editor to the memo with the given id (must be in `memos`).
   * Passing `null` clears the current selection.
   */
  setCurrentMemo: (memoId: string | null) => void;
  /**
   * Create a new memo linked to the regarding record. Uses
   * `PolymorphicResolverService.applyResolverFields` to populate the ADR-024
   * dual-field regarding pattern. On success refetches the list and switches
   * `currentMemo` to the new record.
   *
   * @returns the newly created Memo, or `null` on failure (see `error`).
   */
  createMemo: () => Promise<Memo | null>;
  /**
   * Write the body of `currentMemo`. Debounced by `MEMO_BODY_DEBOUNCE_MS`.
   * Pass `{ immediate: true }` to cancel the pending timer and write now.
   */
  updateBody: (body: string, options?: { immediate?: boolean }) => void;
  /** Write the title of `currentMemo`. Immediate (no debounce). */
  updateName: (name: string) => Promise<void>;
  /** Re-run the list query, preserving `currentMemo` id if still present. */
  refetch: () => Promise<void>;
}

// ---------------------------------------------------------------------------
// Xrm.WebApi shape narrowing (avoids @types/xrm dep in Notepad)
// ---------------------------------------------------------------------------

interface IXrmWebApiLike {
  retrieveMultipleRecords: (
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ) => Promise<{ entities: Array<Record<string, unknown>> }>;
  retrieveRecord: (
    entityLogicalName: string,
    id: string,
    options?: string
  ) => Promise<Record<string, unknown>>;
  createRecord: (
    entityLogicalName: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string }>;
  updateRecord: (
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>
  ) => Promise<{ id: string }>;
}

/**
 * Read `Xrm.WebApi` from the current window, walking `parent` / `top` when the
 * page is iframed (Power Apps hosts the Code Page in a nested iframe). Returns
 * `null` when no Xrm is reachable — the hook then reports an error and returns
 * safe defaults so the React tree does not crash.
 */
function getXrmWebApi(): IXrmWebApiLike | null {
  const frames: Window[] = [window];
  try {
    if (window.parent && window.parent !== window) frames.push(window.parent);
  } catch {
    /* cross-origin */
  }
  try {
    if (window.top && window.top !== window && window.top !== window.parent) {
      frames.push(window.top);
    }
  } catch {
    /* cross-origin */
  }
  for (const frame of frames) {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (frame as any).Xrm;
      if (xrm?.WebApi?.retrieveMultipleRecords) {
        return xrm.WebApi as IXrmWebApiLike;
      }
    } catch {
      /* cross-origin */
    }
  }
  return null;
}

// ---------------------------------------------------------------------------
// Internal: MemoRaw → Memo transform
// ---------------------------------------------------------------------------

/**
 * Flatten the OData-wrapped `MemoRaw` shape into `Memo`. The Xrm.WebApi
 * `$expand=createdby($select=fullname,systemuserid)` result nests createdby
 * as an object with `fullname` + `systemuserid`; we hoist to `{id, name}`.
 */
function flattenMemo(raw: MemoRaw): Memo {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const rawAny = raw as any;
  const cb = rawAny.createdby as
    | { fullname?: string; systemuserid?: string }
    | undefined;
  const cbFallbackName =
    (rawAny['_createdby_value@OData.Community.Display.V1.FormattedValue'] as
      | string
      | undefined) ?? '';
  const cbFallbackId = (rawAny._createdby_value as string | undefined) ?? '';
  return {
    sprk_memoid: raw.sprk_memoid,
    sprk_name: raw.sprk_name,
    sprk_memobody: raw.sprk_memobody,
    sprk_regardingrecordid: raw.sprk_regardingrecordid,
    createdby: {
      id: cb?.systemuserid ?? cbFallbackId,
      name: cb?.fullname ?? cbFallbackName,
    },
    createdon: raw.createdon,
    modifiedon: raw.modifiedon,
  };
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * `useSprkMemoRepository(regardingEntity, regardingId)`.
 *
 * See `UseSprkMemoRepositoryResult` for the returned surface.
 */
export function useSprkMemoRepository(
  regardingEntity: string | null,
  regardingId: string | null
): UseSprkMemoRepositoryResult {
  // Determine unsupported-ness FIRST. When true, everything short-circuits.
  const lookupField: string | null = regardingEntity
    ? (SUPPORTED_MEMO_PARENTS[regardingEntity] ?? null)
    : null;
  const unsupported = regardingEntity !== null && lookupField === null;

  const [memos, setMemos] = React.useState<Memo[]>([]);
  const [currentMemoId, setCurrentMemoId] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState<boolean>(!unsupported);
  const [error, setError] = React.useState<Error | null>(null);

  // Debounce timer — ref so re-renders don't reset it. Cleared on unmount.
  const debounceTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(
    null
  );
  // Latest debounced payload — captured so the deferred write uses the most
  // recent value even if the ref is called mid-way through the settle window.
  const pendingBodyRef = React.useRef<string | null>(null);

  // Latest currentMemoId — captured in a ref so setTimeout callback sees it.
  const currentMemoIdRef = React.useRef<string | null>(null);
  React.useEffect(() => {
    currentMemoIdRef.current = currentMemoId;
  }, [currentMemoId]);

  // ─── List query ────────────────────────────────────────────────────────

  const runList = React.useCallback(async (): Promise<void> => {
    if (unsupported || !lookupField || !regardingId) return;
    setLoading(true);
    setError(null);
    const webApi = getXrmWebApi();
    if (!webApi) {
      setError(
        new Error(
          'Xrm.WebApi is not available in this environment. Notepad cannot load memos.'
        )
      );
      setLoading(false);
      return;
    }
    try {
      const filter = `_${lookupField}_value eq ${regardingId}`;
      const query =
        '?$select=sprk_memoid,sprk_name,sprk_memobody,sprk_regardingrecordid,createdon,modifiedon' +
        '&$expand=createdby($select=fullname,systemuserid)' +
        `&$filter=${filter}` +
        '&$orderby=createdon desc';
      const result = await webApi.retrieveMultipleRecords('sprk_memo', query);
      const list: Memo[] = (result.entities as unknown as MemoRaw[]).map(flattenMemo);
      setMemos(list);
      // Preserve the current selection if still present; otherwise pick top.
      const priorId = currentMemoIdRef.current;
      const priorStillPresent =
        priorId !== null && list.some(m => m.sprk_memoid === priorId);
      if (priorStillPresent) {
        // no-op; ref already tracks it
      } else if (list.length > 0) {
        setCurrentMemoId(list[0].sprk_memoid);
      } else {
        setCurrentMemoId(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
    } finally {
      setLoading(false);
    }
  }, [unsupported, lookupField, regardingId]);

  // Fire list on mount and whenever regarding context changes.
  React.useEffect(() => {
    if (unsupported) {
      setMemos([]);
      setCurrentMemoId(null);
      setLoading(false);
      return;
    }
    void runList();
  }, [runList, unsupported]);

  // Cleanup: drop any pending debounced write on unmount so we don't leak.
  React.useEffect(() => {
    return () => {
      if (debounceTimerRef.current !== null) {
        clearTimeout(debounceTimerRef.current);
        debounceTimerRef.current = null;
      }
    };
  }, []);

  // ─── Derived: currentMemo ──────────────────────────────────────────────

  const currentMemo = React.useMemo<Memo | null>(() => {
    if (currentMemoId === null) return null;
    return memos.find(m => m.sprk_memoid === currentMemoId) ?? null;
  }, [memos, currentMemoId]);

  // ─── setCurrentMemo ────────────────────────────────────────────────────

  const setCurrentMemo = React.useCallback((memoId: string | null): void => {
    setCurrentMemoId(memoId);
  }, []);

  // ─── createMemo ────────────────────────────────────────────────────────

  const createMemo = React.useCallback(async (): Promise<Memo | null> => {
    if (unsupported || !regardingEntity || !regardingId || !lookupField) {
      return null;
    }
    setError(null);
    const webApi = getXrmWebApi();
    if (!webApi) {
      setError(
        new Error(
          'Xrm.WebApi is not available in this environment. Notepad cannot create a memo.'
        )
      );
      return null;
    }
    try {
      // 1. Resolve parent record's display name for the resolver `recordname` field.
      const primaryNameField =
        PARENT_PRIMARY_NAME_FIELD[regardingEntity] ?? 'sprk_name';
      let parentName = '';
      try {
        const parent = await webApi.retrieveRecord(
          regardingEntity,
          regardingId,
          `?$select=${primaryNameField}`
        );
        parentName = (parent[primaryNameField] as string | undefined) ?? '';
      } catch (err) {
        // Non-fatal: proceed with empty name; applyResolverFields still populates
        // the ID and URL. The resolver name will just be blank on the memo.
        // eslint-disable-next-line no-console
        console.warn(
          `[useSprkMemoRepository] Failed to resolve parent name for ${regardingEntity}(${regardingId}):`,
          err
        );
      }

      // 2. Discover nav-props for sprk_memo.
      const navProps = await discoverMemoNavProps('sprk_memo');

      // 3. Build the entity payload and populate resolver fields.
      const entity: Record<string, unknown> = {
        sprk_name: 'Untitled',
        sprk_memobody: '',
      };
      // The PolymorphicResolverService IPolymorphicWebApi only requires
      // retrieveMultipleRecords; Xrm.WebApi satisfies that signature.
      const polyWebApi: IPolymorphicWebApi = {
        retrieveMultipleRecords: (
          entityLogicalName: string,
          query: string
        ) => webApi.retrieveMultipleRecords(entityLogicalName, query),
      };
      const parentEntitySet = `${regardingEntity}s`; // e.g. sprk_matter → sprk_matters
      // Entity-lookup hint: last segment of the entity name (Matter, Project, ...)
      // matches the entity-specific nav-prop names on sprk_memo
      // (sprk_regardingmatter, sprk_regardingproject, ...).
      const entityLookupHint = regardingEntity.replace(/^sprk_/, '');
      await applyResolverFields(
        polyWebApi,
        entity,
        navProps,
        regardingEntity,
        parentEntitySet,
        regardingId,
        parentName,
        entityLookupHint
      );

      // 4. Create.
      const created = await webApi.createRecord('sprk_memo', entity);

      // 5. Refetch list so the new record appears with its full server-side
      //    fields (createdon, createdby.fullname, etc.), then focus it.
      await runList();
      setCurrentMemoId(created.id);
      // Return the newly created Memo from the refreshed list. We can't use
      // the closure-captured `memos` (stale); read latest via the ref-derived
      // list one tick later — but since `runList` sets state synchronously
      // via `setMemos`, we can't inspect it here. Callers that need the object
      // typically read `currentMemo` on the next render. For the return value,
      // synthesise a minimal Memo from the create response.
      const synthetic: Memo = {
        sprk_memoid: created.id,
        sprk_name: 'Untitled',
        sprk_memobody: '',
        sprk_regardingrecordid: regardingId,
        createdby: { id: '', name: '' },
        createdon: new Date().toISOString(),
        modifiedon: new Date().toISOString(),
      };
      return synthetic;
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
      return null;
    }
  }, [unsupported, regardingEntity, regardingId, lookupField, runList]);

  // ─── updateBody (debounced) ────────────────────────────────────────────

  const writeBodyNow = React.useCallback(
    async (body: string): Promise<void> => {
      const memoId = currentMemoIdRef.current;
      if (!memoId) return;
      const webApi = getXrmWebApi();
      if (!webApi) {
        setError(
          new Error(
            'Xrm.WebApi is not available in this environment. Notepad cannot save.'
          )
        );
        return;
      }
      try {
        await webApi.updateRecord('sprk_memo', memoId, {
          sprk_memobody: body,
        });
        // Reflect the write in local state so the list surface stays coherent
        // without a full refetch.
        setMemos(prev =>
          prev.map(m =>
            m.sprk_memoid === memoId
              ? { ...m, sprk_memobody: body, modifiedon: new Date().toISOString() }
              : m
          )
        );
      } catch (err) {
        setError(err instanceof Error ? err : new Error(String(err)));
      }
    },
    []
  );

  const updateBody = React.useCallback(
    (body: string, options?: { immediate?: boolean }): void => {
      pendingBodyRef.current = body;
      if (options?.immediate) {
        // Cancel any pending timer, then write NOW with the fresh value.
        if (debounceTimerRef.current !== null) {
          clearTimeout(debounceTimerRef.current);
          debounceTimerRef.current = null;
        }
        void writeBodyNow(body);
        return;
      }
      // Debounce: reset the timer on every call so we only fire after
      // MEMO_BODY_DEBOUNCE_MS of typing silence.
      if (debounceTimerRef.current !== null) {
        clearTimeout(debounceTimerRef.current);
      }
      debounceTimerRef.current = setTimeout(() => {
        debounceTimerRef.current = null;
        const bodyToWrite = pendingBodyRef.current ?? body;
        pendingBodyRef.current = null;
        void writeBodyNow(bodyToWrite);
      }, MEMO_BODY_DEBOUNCE_MS);
    },
    [writeBodyNow]
  );

  // ─── updateName (immediate) ────────────────────────────────────────────

  const updateName = React.useCallback(async (name: string): Promise<void> => {
    const memoId = currentMemoIdRef.current;
    if (!memoId) return;
    const webApi = getXrmWebApi();
    if (!webApi) {
      setError(
        new Error(
          'Xrm.WebApi is not available in this environment. Notepad cannot save.'
        )
      );
      return;
    }
    try {
      await webApi.updateRecord('sprk_memo', memoId, { sprk_name: name });
      setMemos(prev =>
        prev.map(m =>
          m.sprk_memoid === memoId
            ? { ...m, sprk_name: name, modifiedon: new Date().toISOString() }
            : m
        )
      );
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
    }
  }, []);

  // ─── refetch ───────────────────────────────────────────────────────────

  const refetch = React.useCallback(async (): Promise<void> => {
    await runList();
  }, [runList]);

  // ─── Return ────────────────────────────────────────────────────────────

  return {
    memos,
    loading,
    error,
    unsupported,
    currentMemo,
    setCurrentMemo,
    createMemo,
    updateBody,
    updateName,
    refetch,
  };
}
