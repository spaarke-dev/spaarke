/**
 * pinnedWorkspaces.ts — Multi-pin workspace persistence (task 092 / round 5).
 *
 * Maintains a list of pinned workspaces in `localStorage` so SpaarkeAi can
 * auto-open them as tabs on next cold load. Replaces task 091's single-pin
 * `sessionStorage` stub.
 *
 * Why localStorage (not sessionStorage):
 *   sessionStorage is cleared when the browser tab/window closes; the operator
 *   requested that pinned workspaces survive browser restarts. localStorage
 *   persists across sessions on the same device.
 *
 * Why not BFF (today):
 *   No `user-preferences` endpoint exists in `Sprk.Bff.Api` (the existing
 *   `UserPreferences` records in `AiIntentClassificationSchema.cs` /
 *   `PlaybookRunContext.cs` are AI-pipeline payloads, not a generic preferences
 *   surface). A BFF-backed migration is required for cross-device sync — TODO
 *   below — but localStorage is sufficient for the immediate UX iteration.
 *
 * Storage shape:
 *   Key: `spaarke:workspace:pinned-list`
 *   Value: JSON array of `{ layoutId, layoutName }` records.
 *
 * Error handling:
 *   All accessors are wrapped in try/catch — localStorage may be unavailable
 *   in private browsing, may throw on quota exceeded, or may contain corrupt
 *   JSON from a prior version. Failures degrade silently to "no pins".
 *
 * @see WorkspacePane — consumes `getPinnedWorkspaces()` in the mount effect
 *      to auto-open pinned workspace tabs.
 * @see WorkspaceTabManagerComponent — consumes `isPinned` / `pinWorkspace` /
 *      `unpinWorkspace` for the per-tab pin/unpin toggle.
 * @see WorkspaceLayoutWizard/src/App.tsx — calls `pinWorkspace` /
 *      `unpinWorkspace` from the Step 3 "Pin to Start" checkbox handler.
 *
 * TODO(BFF migration, next review):
 *   Replace this localStorage layer with a BFF `user-preferences` endpoint
 *   (e.g. `GET/PUT /api/me/preferences`) keyed by user id so pinned workspaces
 *   sync across devices. The function signatures here are designed to be
 *   trivially swapped for async equivalents (`Promise<PinnedWorkspace[]>` etc).
 */

const STORAGE_KEY = "spaarke:workspace:pinned-list";

export interface PinnedWorkspace {
  layoutId: string;
  layoutName: string;
}

/**
 * Read the current pinned-workspace list from localStorage.
 *
 * Returns an empty array if storage is unavailable, the key is unset, or the
 * stored payload is corrupt / malformed. Callers can rely on receiving a
 * well-formed array of records with `layoutId` and `layoutName` string fields.
 */
export function getPinnedWorkspaces(): PinnedWorkspace[] {
  try {
    const raw = window.localStorage?.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    // Defensive normalization — drop entries missing required fields rather
    // than letting consumers blow up on malformed data.
    return parsed.flatMap((entry): PinnedWorkspace[] => {
      if (
        entry &&
        typeof entry === "object" &&
        typeof (entry as PinnedWorkspace).layoutId === "string" &&
        typeof (entry as PinnedWorkspace).layoutName === "string"
      ) {
        return [
          {
            layoutId: (entry as PinnedWorkspace).layoutId,
            layoutName: (entry as PinnedWorkspace).layoutName,
          },
        ];
      }
      return [];
    });
  } catch {
    /* localStorage unavailable or corrupt JSON — no-op, return empty list. */
    return [];
  }
}

/** True if the given layoutId is currently pinned. */
export function isPinned(layoutId: string): boolean {
  if (!layoutId) return false;
  return getPinnedWorkspaces().some((p) => p.layoutId === layoutId);
}

/**
 * Add (or refresh) a pinned workspace.
 *
 * Idempotent — if `layoutId` already exists in the list, its `layoutName` is
 * updated (in case the workspace was renamed). Pin order is preserved on
 * existing entries; new pins append to the end.
 */
export function pinWorkspace(layoutId: string, layoutName: string): void {
  if (!layoutId) return;
  try {
    const current = getPinnedWorkspaces();
    const existing = current.find((p) => p.layoutId === layoutId);
    let next: PinnedWorkspace[];
    if (existing) {
      // Refresh the displayName in case it changed.
      next = current.map((p) =>
        p.layoutId === layoutId ? { layoutId, layoutName } : p,
      );
    } else {
      next = [...current, { layoutId, layoutName }];
    }
    window.localStorage?.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    /* localStorage unavailable / quota exceeded — fail silently. The in-memory
       pin state is held by callers (e.g. tab pin icon) which will re-render
       from getPinnedWorkspaces() on next mount and reflect the unchanged disk
       state. */
  }
}

/**
 * Remove a workspace from the pinned list. No-op if not currently pinned.
 */
export function unpinWorkspace(layoutId: string): void {
  if (!layoutId) return;
  try {
    const current = getPinnedWorkspaces();
    const next = current.filter((p) => p.layoutId !== layoutId);
    if (next.length === current.length) return; // nothing to remove
    window.localStorage?.setItem(STORAGE_KEY, JSON.stringify(next));
  } catch {
    /* localStorage unavailable — fail silently. */
  }
}
