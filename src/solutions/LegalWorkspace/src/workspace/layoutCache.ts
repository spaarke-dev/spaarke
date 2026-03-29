/**
 * layoutCache — sessionStorage cache for workspace layout data.
 *
 * Enables instant rendering on same-session navigation by caching the active
 * layout and layouts list. Cache is invalidated on wizard save (via refetch)
 * and naturally cleared when the browser session ends.
 *
 * All operations are wrapped in try/catch to gracefully handle quota errors,
 * private browsing restrictions, and other storage failures.
 *
 * @see WKSP-051 — sessionStorage caching for workspace layouts
 */

import type { WorkspaceLayoutDto } from "../hooks/useWorkspaceLayouts";

// ---------------------------------------------------------------------------
// Cache keys — scoped with "sprk:workspace:" prefix to avoid collisions
// ---------------------------------------------------------------------------

const CACHE_KEY_ACTIVE = "sprk:workspace:activeLayout";
const CACHE_KEY_LIST = "sprk:workspace:layoutsList";

// ---------------------------------------------------------------------------
// Active layout cache
// ---------------------------------------------------------------------------

/** Retrieve the cached active layout, or null on miss / error. */
export function getCachedActiveLayout(): WorkspaceLayoutDto | null {
  try {
    const cached = sessionStorage.getItem(CACHE_KEY_ACTIVE);
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto) : null;
  } catch {
    return null;
  }
}

/** Cache the active layout. Failures are silently ignored. */
export function setCachedActiveLayout(layout: WorkspaceLayoutDto): void {
  try {
    sessionStorage.setItem(CACHE_KEY_ACTIVE, JSON.stringify(layout));
  } catch {
    // Quota exceeded or storage unavailable — skip caching
  }
}

// ---------------------------------------------------------------------------
// Layouts list cache
// ---------------------------------------------------------------------------

/** Retrieve the cached layouts list, or null on miss / error. */
export function getCachedLayoutsList(): WorkspaceLayoutDto[] | null {
  try {
    const cached = sessionStorage.getItem(CACHE_KEY_LIST);
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto[]) : null;
  } catch {
    return null;
  }
}

/** Cache the full layouts list. Failures are silently ignored. */
export function setCachedLayoutsList(layouts: WorkspaceLayoutDto[]): void {
  try {
    sessionStorage.setItem(CACHE_KEY_LIST, JSON.stringify(layouts));
  } catch {
    // Quota exceeded or storage unavailable — skip caching
  }
}

// ---------------------------------------------------------------------------
// Invalidation
// ---------------------------------------------------------------------------

/**
 * Clear all cached workspace layout data.
 *
 * Call this when layouts are modified (e.g., after wizard save success)
 * to ensure stale data is never served on next navigation.
 */
export function invalidateLayoutCache(): void {
  try {
    sessionStorage.removeItem(CACHE_KEY_ACTIVE);
    sessionStorage.removeItem(CACHE_KEY_LIST);
  } catch {
    // Storage unavailable — nothing to clear
  }
}
