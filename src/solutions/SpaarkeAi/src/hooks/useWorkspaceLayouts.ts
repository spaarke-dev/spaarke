/**
 * useWorkspaceLayouts (SpaarkeAi adaptation) — fetches workspace layouts from
 * the BFF API and manages the active layout state for the SpaarkeAi
 * `WorkspacePaneMenu` Switch Workspace section.
 *
 * # Why this file exists (Round 4 Fix 1, 2026-05-21)
 *
 * This is a FAITHFUL ADAPTATION of LegalWorkspace's `useWorkspaceLayouts`
 * hook (the WORKING reference implementation) — NOT a parallel reimplementation.
 *
 * Operator feedback (2026-05-21): the previous task-081 inline
 * `useWorkspaceLayoutsList` (inside `WorkspacePaneMenu.tsx`) was a parallel
 * implementation that diverged from the working LegalWorkspace pattern
 * (missing cache-first hydration, missing fallback selection, missing
 * sessionStorage layout cache). The deployed SpaarkeAi menu still showed
 * "No workspaces available" intermittently.
 *
 * Per the project's reuse principle ("don't constantly rebuild the same
 * thing; when we have working components reuse them"), this file copies the
 * LegalWorkspace `useWorkspaceLayouts` hook with three surgical adaptations:
 *
 *   1. **Auth surface** — sources `authenticatedFetch` + `bffBaseUrl` +
 *      `isAuthenticated` from `useAiSession()` (SpaarkeAi pattern, ADR-028)
 *      rather than module-level `authenticatedFetch` / `getBffBaseUrl()`.
 *      The module-level call races the runtime-config bootstrap (task 081
 *      root cause) — funnelling auth through hook deps means the effect
 *      auto-defers until config is ready.
 *
 *   2. **Drops LayoutJson parsing** — LegalWorkspace's hook returns
 *      `activeLayoutJson: LayoutJson` for its section registry. SpaarkeAi's
 *      `WorkspacePaneMenu` only needs the layouts list + active layout ID,
 *      so we don't parse `sectionsJson` here.
 *
 *   3. **Drops SYSTEM_DEFAULT_LAYOUT fallback** — LegalWorkspace falls back
 *      to a hardcoded system default so its WorkspaceShell always renders
 *      sections. SpaarkeAi's menu degrades gracefully to "No workspaces
 *      available" empty state when the BFF returns no layouts (this is the
 *      correct UX — no fake stub layout in the menu).
 *
 * Everything else — cache-first hydration via sessionStorage layout cache,
 * parallel fetch of list + default, pinned-id resolution, BFF default
 * resolution, first-default/first-system/first-layout selection cascade,
 * setActiveLayoutById fetch-by-ID fallback, refetch + cache invalidation —
 * matches LegalWorkspace's working hook 1:1.
 *
 * # Source reference
 *
 * @see src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts — canonical
 *      working implementation. KEEP THIS FILE IN SYNC if the LegalWorkspace
 *      hook changes its fetch shape, cache strategy, or selection cascade.
 *
 * # Standards
 *
 *   - ADR-012: SpaarkeAi-local hook (no cross-solution dependency)
 *   - ADR-028: BFF calls via `authenticatedFetch`; no token snapshots
 */

import * as React from "react";
import { buildBffApiUrl } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Types — mirror LegalWorkspace's WorkspaceLayoutDto exactly (same BFF shape)
// ---------------------------------------------------------------------------

/**
 * Client-side representation of a workspace layout from the BFF.
 * Matches `WorkspaceLayoutDto` returned by `GET /api/workspace/layouts` and
 * `GET /api/workspace/layouts/default`. SHAPE-EQUIVALENT to the LegalWorkspace
 * type — both consume the same BFF endpoint.
 */
export interface WorkspaceLayoutDto {
  id: string;
  name: string;
  layoutTemplateId: string;
  sectionsJson: string;
  isDefault: boolean;
  sortOrder: number | null;
  isSystem: boolean;
}

export interface UseWorkspaceLayoutsResult {
  /** All available layouts (system + user). Empty array while loading or on fetch failure. */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. Null while loading or if no layouts exist. */
  activeLayout: WorkspaceLayoutDto | null;
  /** True while the initial fetch is in progress (mirrors LegalWorkspace semantics). */
  isLoading: boolean;
  /** Switch to a different layout by ID. Fetches layout details from BFF if needed. */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF (invalidates the local cache first). */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// SessionStorage cache — mirrors LegalWorkspace's layoutCache.ts strategy
// (cache-first hydration so menu opens instantly on same-session navigation).
//
// Distinct cache keys from LegalWorkspace's `sprk:workspace:*` prefix because
// SpaarkeAi may run alongside LegalWorkspace in different code pages — keeping
// the caches isolated avoids one app's cache poisoning the other.
// ---------------------------------------------------------------------------

const CACHE_KEY_ACTIVE = "spaarke.ai.workspace.activeLayout";
const CACHE_KEY_LIST = "spaarke.ai.workspace.layoutsList";

function getCachedActive(): WorkspaceLayoutDto | null {
  try {
    const cached = sessionStorage.getItem(CACHE_KEY_ACTIVE);
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto) : null;
  } catch {
    return null;
  }
}

function setCachedActive(layout: WorkspaceLayoutDto): void {
  try {
    sessionStorage.setItem(CACHE_KEY_ACTIVE, JSON.stringify(layout));
  } catch {
    /* quota / privacy mode — ignore */
  }
}

function getCachedList(): WorkspaceLayoutDto[] | null {
  try {
    const cached = sessionStorage.getItem(CACHE_KEY_LIST);
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto[]) : null;
  } catch {
    return null;
  }
}

function setCachedList(layouts: WorkspaceLayoutDto[]): void {
  try {
    sessionStorage.setItem(CACHE_KEY_LIST, JSON.stringify(layouts));
  } catch {
    /* quota / privacy mode — ignore */
  }
}

function invalidateCache(): void {
  try {
    sessionStorage.removeItem(CACHE_KEY_ACTIVE);
    sessionStorage.removeItem(CACHE_KEY_LIST);
  } catch {
    /* ignore */
  }
}

// ---------------------------------------------------------------------------
// Pinned-layout sessionStorage key (set by WorkspacePaneMenu when the user
// picks a layout from Switch Workspace). Honoured on the next mount so the
// menu remembers the user's choice within the session.
// ---------------------------------------------------------------------------

const ACTIVE_LAYOUT_PIN_KEY = "spaarke.workspace.activeLayoutId";

// ---------------------------------------------------------------------------
// Hook arguments
// ---------------------------------------------------------------------------

export interface UseWorkspaceLayoutsArgs {
  /** BFF host URL (no `/api` suffix). Typically `useAiSession().bffBaseUrl`. */
  bffBaseUrl: string;
  /** Authenticated fetch from `useAiSession()` (ADR-028). */
  authenticatedFetch: (url: string, init?: RequestInit) => Promise<Response>;
  /** True when MSAL has a valid token. The effect defers until this is true. */
  isAuthenticated: boolean;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetches workspace layouts from the BFF and manages active-layout state.
 *
 * Mirrors LegalWorkspace's `useWorkspaceLayouts` 1:1 except for the three
 * surgical adaptations documented in the file header.
 *
 * Lifecycle:
 *   1. On mount, hydrates from sessionStorage cache (instant render).
 *   2. In parallel, fetches `/api/workspace/layouts` + `/api/workspace/layouts/default`.
 *   3. Resolves active layout by precedence: pinned-id → BFF default →
 *      first-default → first-system → first-layout → null.
 *   4. Updates sessionStorage cache for next visit.
 *
 * Deferral: when `!isAuthenticated || !bffBaseUrl`, the effect returns
 * immediately and waits for the next render with truthy deps. This is the
 * task 081 root-cause fix carried forward.
 */
export function useWorkspaceLayouts(
  args: UseWorkspaceLayoutsArgs,
): UseWorkspaceLayoutsResult {
  const { bffBaseUrl, authenticatedFetch, isAuthenticated } = args;

  const [layouts, setLayouts] = React.useState<WorkspaceLayoutDto[]>([]);
  const [activeLayout, setActiveLayout] =
    React.useState<WorkspaceLayoutDto | null>(null);
  const [isLoading, setIsLoading] = React.useState(true);
  const [fetchKey, setFetchKey] = React.useState(0);

  // Track mount state to avoid setState after unmount (mirrors LegalWorkspace)
  const mountedRef = React.useRef(true);
  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // -------------------------------------------------------------------------
  // Fetch layouts on mount + when fetchKey / auth / bffBaseUrl change
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    // Defer until auth + runtime config are ready — task 081 root-cause fix.
    // Without this guard, `buildBffApiUrl(bffBaseUrl, ...)` would throw on the
    // first render when bffBaseUrl is still "".
    if (!isAuthenticated || !bffBaseUrl) {
      setIsLoading(true);
      return;
    }

    let cancelled = false;

    async function fetchLayouts(): Promise<void> {
      // -----------------------------------------------------------------
      // Cache-first hydration (mirrors LegalWorkspace pattern). If we have
      // valid cached data, render it immediately and continue to revalidate
      // from the API in the background.
      // -----------------------------------------------------------------
      const cachedList = getCachedList();
      const cachedActive = getCachedActive();
      const hasCachedData = cachedList && cachedList.length > 0 && cachedActive;

      if (hasCachedData && !cancelled && mountedRef.current) {
        setLayouts(cachedList);
        setActiveLayout(cachedActive);
        setIsLoading(false);
        // Continue to fetch from API below to revalidate
      } else {
        setIsLoading(true);
      }

      try {
        const listUrl = buildBffApiUrl(bffBaseUrl, "/workspace/layouts");
        const defaultUrl = buildBffApiUrl(
          bffBaseUrl,
          "/workspace/layouts/default",
        );

        const [listRes, defaultRes] = await Promise.all([
          authenticatedFetch(listUrl),
          authenticatedFetch(defaultUrl),
        ]);

        if (cancelled || !mountedRef.current) return;

        // Parse layouts list
        let allLayouts: WorkspaceLayoutDto[] = [];
        if (listRes.ok) {
          allLayouts = (await listRes.json()) as WorkspaceLayoutDto[];
        } else {
          console.warn(
            `[useWorkspaceLayouts:SpaarkeAi] Failed to fetch layouts list: ${listRes.status}`,
          );
        }

        // Parse default layout
        let defaultLayout: WorkspaceLayoutDto | null = null;
        if (defaultRes.ok) {
          defaultLayout = (await defaultRes.json()) as WorkspaceLayoutDto;
        } else {
          console.warn(
            `[useWorkspaceLayouts:SpaarkeAi] Failed to fetch default layout: ${defaultRes.status}`,
          );
        }

        if (cancelled || !mountedRef.current) return;

        // Apply results
        setLayouts(allLayouts);

        // -----------------------------------------------------------------
        // Active layout resolution cascade (mirrors LegalWorkspace logic
        // minus the deep-link branch — SpaarkeAi doesn't expose deep links
        // through this menu, but it DOES honour sessionStorage pinning set
        // by the menu's prior layout selection).
        // -----------------------------------------------------------------

        let resolvedActive: WorkspaceLayoutDto | null = null;

        // 1. Prefer a sessionStorage-pinned layout (set by menu selection)
        let pinnedId: string | null = null;
        try {
          pinnedId =
            window.sessionStorage?.getItem(ACTIVE_LAYOUT_PIN_KEY) ?? null;
        } catch {
          /* ignore */
        }
        if (pinnedId) {
          resolvedActive = allLayouts.find((l) => l.id === pinnedId) ?? null;
        }

        // 2. Otherwise use the BFF default
        if (!resolvedActive && defaultLayout) {
          resolvedActive = defaultLayout;
        }

        // 3. Otherwise pick the first default / system / first layout
        if (!resolvedActive && allLayouts.length > 0) {
          resolvedActive =
            allLayouts.find((l) => l.isDefault) ??
            allLayouts.find((l) => l.isSystem) ??
            allLayouts[0];
        }

        if (!cancelled && mountedRef.current) {
          setActiveLayout(resolvedActive);

          // Update sessionStorage cache for instant render on next visit
          if (resolvedActive) {
            setCachedActive(resolvedActive);
          }
          if (allLayouts.length > 0) {
            setCachedList(allLayouts);
          }

          setIsLoading(false);
        }
      } catch (err) {
        if (cancelled || !mountedRef.current) return;

        // Log to console.error so failures are visible in devtools.
        // Render empty state (degrade gracefully) but do NOT swallow silently.
        console.error(
          "[useWorkspaceLayouts:SpaarkeAi] Layouts fetch failed; rendering empty Switch Workspace section:",
          err,
        );
        setLayouts([]);
        setActiveLayout(null);
        setIsLoading(false);
      }
    }

    fetchLayouts();

    return () => {
      cancelled = true;
    };
  }, [fetchKey, bffBaseUrl, isAuthenticated, authenticatedFetch]);

  // -------------------------------------------------------------------------
  // setActiveLayoutById — switch to a different layout (mirrors LegalWorkspace)
  // -------------------------------------------------------------------------

  const setActiveLayoutById = React.useCallback(
    (layoutId: string) => {
      // Look up in the current layouts list first
      const found = layouts.find((l) => l.id === layoutId);
      if (found) {
        setActiveLayout(found);
        setCachedActive(found);
        return;
      }

      // Not found locally — fetch by ID from the BFF
      if (!isAuthenticated || !bffBaseUrl) {
        console.warn(
          "[useWorkspaceLayouts:SpaarkeAi] setActiveLayoutById called before auth ready",
        );
        return;
      }

      (async () => {
        try {
          const url = buildBffApiUrl(
            bffBaseUrl,
            `/workspace/layouts/${layoutId}`,
          );
          const res = await authenticatedFetch(url);
          if (res.ok) {
            const layout = (await res.json()) as WorkspaceLayoutDto;
            if (mountedRef.current) {
              setActiveLayout(layout);
              setCachedActive(layout);
            }
          } else {
            console.warn(
              `[useWorkspaceLayouts:SpaarkeAi] Failed to fetch layout ${layoutId}: ${res.status}`,
            );
          }
        } catch (err) {
          console.warn(
            "[useWorkspaceLayouts:SpaarkeAi] Failed to fetch layout by ID:",
            err,
          );
        }
      })();
    },
    [layouts, isAuthenticated, bffBaseUrl, authenticatedFetch],
  );

  // -------------------------------------------------------------------------
  // Refetch — invalidates cache and re-runs the effect
  // -------------------------------------------------------------------------

  const refetch = React.useCallback(() => {
    invalidateCache();
    setFetchKey((k) => k + 1);
  }, []);

  return {
    layouts,
    activeLayout,
    isLoading,
    setActiveLayoutById,
    refetch,
  };
}
