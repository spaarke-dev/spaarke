/**
 * useDocumentListPrefs hook
 *
 * Manages localStorage-backed UI preferences scoped per `(userId, matterId)`
 * for the Documents PCF list/card surface (FR-DOC-04):
 *   - View preference (list | card)
 *   - Pin state (Set<documentId>)
 *
 * Persists per-matter so switching matters yields independent preference.
 * Selection state is intentionally NOT persisted (v1 — in-memory only across
 * view toggles, not across reloads — per spec FR-DOC-04 Owner Clarification).
 *
 * Standards:
 *   - ADR-021  Fluent v9 only — no DOM color manipulation
 *   - ADR-022  React 16/17-safe — uses only useState + useEffect + useCallback
 *
 * @see spec.md FR-DOC-04
 */

import { useCallback, useEffect, useState } from 'react';

/** Document list view mode */
export type DocumentListView = 'list' | 'card';

/** localStorage key prefix for view preference */
const VIEW_KEY_PREFIX = 'spaarke.docs.view';

/** localStorage key prefix for pin state */
const PIN_KEY_PREFIX = 'spaarke.docs.pinned';

/**
 * localStorage key prefix for column widths.
 *
 * History:
 * - v1.1.45 introduced `spaarke.docs.colwidths.{userId}.{matterId}`.
 * - v1.1.59 bumped to `.v2` to invalidate widths from the wider
 *   v1.1.45–v1.1.58 defaults.
 * - v1.1.67 bumped to `.v3` because v1.1.66 added MAX_WIDTHS caps in
 *   ListView (e.g. Document max 600px) BUT Fluent v9 DataGrid's
 *   `resizableColumns` plugin maintains its own internal state for
 *   resized widths separate from the `idealWidth` prop.
 * - v1.1.68 keeps `.v3` and ADDS a runtime clamp + heal at hook
 *   read-time (see `clampAndHealColumnWidths` below). The prefix
 *   bump alone wasn't sufficient: any user who dragged a column past
 *   the cap in v1.1.67 (after the .v3 invalidation) re-persisted a
 *   stale over-cap width, and Fluent's internal reducer state held
 *   the dragged value even though our `columnSizingOptions` memo
 *   passed the clamped `idealWidth` (the Fluent layout-effect
 *   re-dispatches `COLUMN_SIZING_OPTIONS_UPDATED` but the in-session
 *   drag-time render still showed the unclamped width for one frame
 *   before the re-merge). Heal-on-read + a remount counter in
 *   ListView (Option D = A + C) jointly close this.
 *
 * Old-version keys remain in users' localStorage as orphans; no
 * migration needed because they're scoped per matter and silently
 * ignored under the new prefix.
 */
const COLWIDTHS_KEY_PREFIX = 'spaarke.docs.colwidths.v3';

/**
 * v1.1.68 — Hard-coded MAX_WIDTHS caps mirrored from ListView.tsx.
 *
 * Why duplicate? The hook is consumed by `SemanticSearchControl.tsx` BEFORE
 * `ListView.tsx` mounts — clamping at read-time requires the caps to be
 * known at hook level, not at component level. Keeping the values in sync
 * is a small maintenance cost (the caps are stable and ListView.tsx still
 * carries the canonical comment block explaining the rationale).
 *
 * If ListView.tsx's MAX_WIDTHS change, update here too. The hook prefers
 * to err on the side of healing rather than risk an unclamped persisted
 * width sticking around indefinitely.
 *
 * Column ids must match the constants in ListView.tsx exactly.
 */
const COLUMN_MAX_WIDTHS: Record<string, number> = {
  select: 40,
  pin: 36,
  name: 600,
  relationship: 160,
  combinedScore: 100,
  documentType: 48,
  modifiedAt: 160,
  menu: 44,
};

/**
 * v1.1.68 — Heal stale over-cap widths at read-time.
 *
 * Step 1 of the Option D fix (Option C component). Reads the parsed
 * column widths map and clamps each entry to the cap defined in
 * `COLUMN_MAX_WIDTHS`. Returns:
 *   - the (possibly clamped) widths map
 *   - a boolean indicating whether ANY entry was clamped (so the caller
 *     can re-persist the healed values back to localStorage)
 *
 * This addresses the case where a user persisted an over-cap width
 * during an earlier session — without read-time healing, the over-cap
 * value sits in localStorage forever and is re-applied on every page
 * load. With healing, the first load after v1.1.68 ships silently
 * normalizes the stored value.
 */
function clampAndHealColumnWidths(input: Record<string, number>): {
  widths: Record<string, number>;
  healed: boolean;
} {
  let healed = false;
  const widths: Record<string, number> = {};
  for (const [k, v] of Object.entries(input)) {
    const max = COLUMN_MAX_WIDTHS[k];
    if (max !== undefined && v > max) {
      widths[k] = max;
      healed = true;
    } else {
      widths[k] = v;
    }
  }
  return { widths, healed };
}

/** Default view per spec FR-DOC-04 */
const DEFAULT_VIEW: DocumentListView = 'list';

/** Map of column id → pixel width. Keys must align with ListView's DataGrid column ids. */
export type ColumnWidths = Readonly<Record<string, number>>;

function buildKey(prefix: string, userId: string | null, matterId: string | null): string {
  // Both scope dimensions required per spec FR-DOC-04 — fall back to a stable
  // sentinel when either is missing so localStorage still works (e.g., during
  // initial auth bootstrap before context is fully resolved).
  const u = userId ?? 'anon';
  const m = matterId ?? 'global';
  return `${prefix}.${u}.${m}`;
}

function safeReadLocalStorage(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    // Storage disabled (private mode, quota exceeded) — gracefully degrade
    return null;
  }
}

function safeWriteLocalStorage(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Swallow — non-fatal; the next render still uses the in-memory state
  }
}

/**
 * Hook result for document list preferences.
 */
export interface UseDocumentListPrefsResult {
  /** Current view (list | card) — hydrated from localStorage on mount. */
  view: DocumentListView;
  /** Update view; writes-through to localStorage immediately. */
  setView: (next: DocumentListView) => void;

  /** Set of pinned document IDs — hydrated from localStorage on mount. */
  pinnedIds: Set<string>;
  /** Toggle pin on a single document; writes-through to localStorage. */
  togglePin: (documentId: string) => void;
  /** Whether a document is currently pinned. */
  isPinned: (documentId: string) => boolean;

  /**
   * Column-width overrides for the list view (v1.1.45) — keyed by column id.
   * Empty object when no overrides have been persisted yet.
   */
  columnWidths: ColumnWidths;
  /** Persist a single column-width override; writes-through to localStorage. */
  setColumnWidth: (columnId: string, width: number) => void;
  /** Clear every persisted column-width override (reset to DataGrid defaults). */
  resetColumnWidths: () => void;
}

/**
 * Manage per-(userId, matterId)-scoped document list UI preferences.
 *
 * The hook hydrates from localStorage on mount and again whenever the scoping
 * keys change (matter switch). Writes are synchronous via the returned setters.
 *
 * @param userId - Current user's Dataverse system user ID (from context.userSettings.userId)
 * @param matterId - Current matter/entity ID being viewed (parent form context)
 */
export function useDocumentListPrefs(
  userId: string | null,
  matterId: string | null
): UseDocumentListPrefsResult {
  const viewKey = buildKey(VIEW_KEY_PREFIX, userId, matterId);
  const pinKey = buildKey(PIN_KEY_PREFIX, userId, matterId);
  const colWidthsKey = buildKey(COLWIDTHS_KEY_PREFIX, userId, matterId);

  // ── View state ──────────────────────────────────────────────────────────
  const [view, setViewState] = useState<DocumentListView>(() => {
    const raw = safeReadLocalStorage(viewKey);
    return raw === 'card' || raw === 'list' ? raw : DEFAULT_VIEW;
  });

  // Re-hydrate when the scoping key changes (matter switch / user switch).
  useEffect(() => {
    const raw = safeReadLocalStorage(viewKey);
    setViewState(raw === 'card' || raw === 'list' ? raw : DEFAULT_VIEW);
  }, [viewKey]);

  const setView = useCallback(
    (next: DocumentListView) => {
      setViewState(next);
      safeWriteLocalStorage(viewKey, next);
    },
    [viewKey]
  );

  // ── Pin state ───────────────────────────────────────────────────────────
  const [pinnedIds, setPinnedIds] = useState<Set<string>>(() => {
    const raw = safeReadLocalStorage(pinKey);
    if (!raw) return new Set<string>();
    try {
      const arr = JSON.parse(raw);
      if (Array.isArray(arr)) {
        return new Set(arr.filter((v: unknown): v is string => typeof v === 'string'));
      }
    } catch {
      // Corrupt JSON — reset
    }
    return new Set<string>();
  });

  // Re-hydrate pins when scoping key changes.
  useEffect(() => {
    const raw = safeReadLocalStorage(pinKey);
    if (!raw) {
      setPinnedIds(new Set<string>());
      return;
    }
    try {
      const arr = JSON.parse(raw);
      if (Array.isArray(arr)) {
        setPinnedIds(new Set(arr.filter((v: unknown): v is string => typeof v === 'string')));
        return;
      }
    } catch {
      // Corrupt JSON — fall through to reset
    }
    setPinnedIds(new Set<string>());
  }, [pinKey]);

  const persistPins = useCallback(
    (next: Set<string>) => {
      safeWriteLocalStorage(pinKey, JSON.stringify(Array.from(next)));
    },
    [pinKey]
  );

  const togglePin = useCallback(
    (documentId: string) => {
      setPinnedIds(prev => {
        const next = new Set(prev);
        if (next.has(documentId)) {
          next.delete(documentId);
        } else {
          next.add(documentId);
        }
        persistPins(next);
        return next;
      });
    },
    [persistPins]
  );

  const isPinned = useCallback((documentId: string) => pinnedIds.has(documentId), [pinnedIds]);

  // ── Column widths (v1.1.45) ────────────────────────────────────────────
  // Map of `columnId` → pixel width. Reads/writes localStorage with the same
  // graceful-degradation pattern as view + pins. Defaults to an empty object so
  // unknown columns fall through to the DataGrid's intrinsic sizing.
  //
  // v1.1.68 — Read-time healing: any stored width over `COLUMN_MAX_WIDTHS[id]`
  // is silently clamped on read AND re-persisted at the clamped value, so
  // the storage self-heals on the first load after the cap is introduced
  // (or tightened in a future round). Without this heal, a single drag past
  // the cap in a prior session would stick forever.
  const [columnWidths, setColumnWidthsState] = useState<ColumnWidths>(() => {
    const raw = safeReadLocalStorage(colWidthsKey);
    if (!raw) return {};
    try {
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const next: Record<string, number> = {};
        for (const [k, v] of Object.entries(parsed)) {
          if (typeof v === 'number' && Number.isFinite(v) && v > 0) next[k] = v;
        }
        // v1.1.68 — clamp + heal. If anything was clamped, write back so
        // the storage no longer carries the stale over-cap value.
        const { widths, healed } = clampAndHealColumnWidths(next);
        if (healed) safeWriteLocalStorage(colWidthsKey, JSON.stringify(widths));
        return widths;
      }
    } catch {
      // Corrupt JSON — reset
    }
    return {};
  });

  // Re-hydrate column widths when the scoping key changes (matter switch).
  useEffect(() => {
    const raw = safeReadLocalStorage(colWidthsKey);
    if (!raw) {
      setColumnWidthsState({});
      return;
    }
    try {
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        const next: Record<string, number> = {};
        for (const [k, v] of Object.entries(parsed)) {
          if (typeof v === 'number' && Number.isFinite(v) && v > 0) next[k] = v;
        }
        // v1.1.68 — clamp + heal on re-hydrate too.
        const { widths, healed } = clampAndHealColumnWidths(next);
        if (healed) safeWriteLocalStorage(colWidthsKey, JSON.stringify(widths));
        setColumnWidthsState(widths);
        return;
      }
    } catch {
      // Corrupt JSON — fall through to reset
    }
    setColumnWidthsState({});
  }, [colWidthsKey]);

  const persistColumnWidths = useCallback(
    (next: ColumnWidths) => {
      safeWriteLocalStorage(colWidthsKey, JSON.stringify(next));
    },
    [colWidthsKey]
  );

  const setColumnWidth = useCallback(
    (columnId: string, width: number) => {
      if (!columnId || !Number.isFinite(width) || width <= 0) return;
      // v1.1.68 — defensive clamp at the write path so the hook is the
      // single chokepoint for cap enforcement. ListView.handleColumnResize
      // also clamps before calling, but this guards against any future
      // caller forgetting (or callers from other components if the hook
      // is reused). Tiny CPU cost; large guarantee.
      const max = COLUMN_MAX_WIDTHS[columnId];
      const clamped = max !== undefined ? Math.min(width, max) : width;
      setColumnWidthsState(prev => {
        const next = { ...prev, [columnId]: Math.round(clamped) };
        persistColumnWidths(next);
        return next;
      });
    },
    [persistColumnWidths]
  );

  const resetColumnWidths = useCallback(() => {
    setColumnWidthsState({});
    persistColumnWidths({});
  }, [persistColumnWidths]);

  return {
    view,
    setView,
    pinnedIds,
    togglePin,
    isPinned,
    columnWidths,
    setColumnWidth,
    resetColumnWidths,
  };
}
