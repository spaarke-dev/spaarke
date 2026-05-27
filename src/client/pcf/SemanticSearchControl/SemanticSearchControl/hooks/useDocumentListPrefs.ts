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

/** Default view per spec FR-DOC-04 */
const DEFAULT_VIEW: DocumentListView = 'list';

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

  return {
    view,
    setView,
    pinnedIds,
    togglePin,
    isPinned,
  };
}
