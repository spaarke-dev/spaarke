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
 * - v1.1.67 bumps to `.v3` because v1.1.66 added MAX_WIDTHS caps in
 *   ListView (e.g. Document max 600px) BUT Fluent v9 DataGrid's
 *   `resizableColumns` plugin maintains its own internal state for
 *   resized widths separate from the `idealWidth` prop. Persisted
 *   widths in `.v2` larger than the new caps were ignoring the
 *   render-time clamp in `columnSizingOptions` and stretching the
 *   Document column off the visible right edge of the grid (the
 *   sibling columns got clipped by v1.1.65's `overflow: hidden`).
 *   Bumping to `.v3` invalidates the over-large persisted widths;
 *   the grid starts fresh with DEFAULT_WIDTHS which obey MAX_WIDTHS.
 *
 * Old-version keys remain in users' localStorage as orphans; no
 * migration needed because they're scoped per matter and silently
 * ignored under the new prefix.
 */
const COLWIDTHS_KEY_PREFIX = 'spaarke.docs.colwidths.v3';

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
        return next;
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
        setColumnWidthsState(next);
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
      setColumnWidthsState(prev => {
        const next = { ...prev, [columnId]: Math.round(width) };
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
