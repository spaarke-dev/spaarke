/**
 * useWorkspaceLayouts — consolidated workspace-layouts hook (R4 task 051 / C-3).
 *
 * # Purpose
 *
 * Fetches workspace layouts from the BFF API and manages the active layout
 * state for any consumer surface (LegalWorkspace, SpaarkeAi, and any future
 * surface that needs workspace layouts).
 *
 * Replaces the two divergent copies that previously lived at:
 *   - `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts`
 *   - `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`
 *
 * Both consumers now import from `@spaarke/ai-widgets` and pass injected
 * dependencies (per FR-13 + ADR-028).
 *
 * # Design tenets
 *
 *   1. **Context-agnostic** (ADR-012) — accepts `bffBaseUrl`,
 *      `authenticatedFetch`, `isAuthenticated` as injected props; no module-
 *      level imports of solution-specific auth/config.
 *
 *   2. **Function-based auth** (ADR-028) — the hook NEVER stores tokens in
 *      state, refs, or closures. The injected `authenticatedFetch` is the
 *      only path to BFF data.
 *
 *   3. **SessionStorage cache centralized** (FR-13) — cache reads/writes /
 *      invalidation live inside the hook. Consumers pass `cacheKeyPrefix` to
 *      pick a namespace so two consumers on the same page do not collide.
 *
 *   4. **Behavioural parity** (R4 Risk R-3) — preserves every behaviour the
 *      pre-change LegalWorkspace + SpaarkeAi hooks had, gated by optional
 *      flags: `parseLayoutJson`, `fallbackLayout`, `initialWorkspaceId`,
 *      `pinnedLayoutIdKey`, `embedded`. Consumers opt in to each.
 *
 * # Standards
 *
 *   - ADR-012 (shared components, context-agnostic)
 *   - ADR-022 (React 19 only — uses standard React APIs)
 *   - ADR-028 (function-based auth — no token snapshots)
 *
 * @see projects/spaarke-ai-platform-unification-r4/notes/c3-pre-change-diff.md
 * @see projects/spaarke-ai-platform-unification-r4/notes/c3-consolidated-hook-design.md
 */

import { useState, useEffect, useCallback, useRef } from "react";
import { buildBffApiUrl } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Types (mirror BFF WorkspaceLayoutDto shape)
// ---------------------------------------------------------------------------

/** Client-side representation of a workspace layout from the BFF. */
export interface WorkspaceLayoutDto {
  id: string;
  name: string;
  layoutTemplateId: string;
  sectionsJson: string;
  isDefault: boolean;
  sortOrder: number | null;
  isSystem: boolean;
  /**
   * R4 task 053 (B-4 / FR-07): ISO-8601 timestamp of the layout's last
   * modification. Wire shape mirrors C# `DateTimeOffset` (e.g.,
   * "2026-05-26T14:23:11+00:00"). Format consumer-side with locale-aware
   * helpers — do NOT pre-format on the server.
   *
   * Hard-coded system layouts (e.g., "Corporate Workspace") emit the
   * Unix-epoch sentinel ("1970-01-01T00:00:00+00:00") since they never
   * persist in Dataverse. Consumer UI (ManageWorkspacesPane) treats this
   * sentinel as "—" / no modified date.
   */
  modifiedOn: string;
}

/**
 * Workspace loading status — determines which UI state to render.
 *
 * - "loading":     Initial fetch in progress (no cached data available).
 * - "loaded":      Layouts fetched successfully (or served from cache).
 * - "error":       Fetch failed; consumer may render fallback layout.
 * - "first-visit": Fetch succeeded but user has zero user-created layouts
 *                  (only system layouts). Consumers may show an onboarding
 *                  banner. Only computed when `fallbackLayout` is supplied.
 */
export type WorkspaceLoadingStatus =
  | "loading"
  | "loaded"
  | "error"
  | "first-visit";

/** Authenticated fetch function (ADR-028 — function-based contract). */
export type AuthenticatedFetch = (
  url: string,
  init?: RequestInit,
) => Promise<Response>;

/**
 * Options accepted by `useWorkspaceLayouts`.
 *
 * Required injected deps (FR-13):
 *   - `bffBaseUrl`, `authenticatedFetch`, `isAuthenticated`
 *
 * Optional flags (FR-13 + consumer-specific behaviours):
 *   - `parseLayoutJson` — when present, hook returns `parsedActiveLayout`
 *   - `fallbackLayout` — when present, used on empty-list / network-error
 *   - `initialWorkspaceId` — deep-link target (fetches by id if not in list)
 *   - `pinnedLayoutIdKey` — sessionStorage key holding a pinned layout id
 *   - `embedded` — when true, skip ALL sessionStorage cache reads + writes
 *   - `cacheKeyPrefix` — namespace for sessionStorage cache keys
 */
export interface UseWorkspaceLayoutsOptions<TParsed = unknown> {
  /** BFF host URL (no `/api` suffix). Injected from host auth context. */
  bffBaseUrl: string;
  /** Injected authenticated fetch (ADR-028 — no token snapshots). */
  authenticatedFetch: AuthenticatedFetch;
  /** True when MSAL has a valid token. The effect defers until this is true. */
  isAuthenticated: boolean;
  /** Optional parser for `sectionsJson`. When provided, hook returns `parsedActiveLayout`. */
  parseLayoutJson?: (raw: unknown) => TParsed;
  /** Optional hardcoded fallback layout used on empty list / network error. */
  fallbackLayout?: WorkspaceLayoutDto;
  /** Optional deep-link layout id (LegalWorkspace usage). */
  initialWorkspaceId?: string;
  /** Optional sessionStorage key holding a pinned layout id (SpaarkeAi usage). */
  pinnedLayoutIdKey?: string;
  /** When `true`, hook skips all sessionStorage cache ops. Default `false`. */
  embedded?: boolean;
  /**
   * SessionStorage cache key prefix. Each consumer passes its own namespace.
   * Default `"sprk:workspace"` preserves LegalWorkspace cache keys byte-identical.
   */
  cacheKeyPrefix?: string;
}

export interface UseWorkspaceLayoutsResult<TParsed = unknown> {
  /** All available layouts (system + user). Empty array while loading. */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. Null only during initial load or empty result. */
  activeLayout: WorkspaceLayoutDto | null;
  /**
   * Parsed `sectionsJson` for the active layout (via `parseLayoutJson`).
   * `undefined` when no `parseLayoutJson` option was supplied.
   */
  parsedActiveLayout: TParsed | undefined;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
  /** Computed status: "loading" | "loaded" | "error" | "first-visit". */
  status: WorkspaceLoadingStatus;
  /** Error message if fetch failed (consumer may render fallback). */
  error: string | null;
  /** Switch to a different layout by ID (fetches if not in current list). */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF (invalidates the cache first). */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// SessionStorage cache helpers (namespaced via `cacheKeyPrefix`)
//
// Cache key shape matches the pre-change LegalWorkspace + SpaarkeAi keys
// EXACTLY so the migration preserves cache reads across the cut-over:
//   LegalWorkspace: "sprk:workspace:activeLayout" / "sprk:workspace:layoutsList"
//   SpaarkeAi:      "spaarke.ai.workspace.activeLayout" / "spaarke.ai.workspace.layoutsList"
//
// Joiner is auto-detected: prefixes ending in ":" use ":<key>"; prefixes
// ending in "." use ".<key>"; otherwise default to ":<key>".
// ---------------------------------------------------------------------------

const DEFAULT_CACHE_PREFIX = "sprk:workspace";

function cacheKey(prefix: string, suffix: "activeLayout" | "layoutsList"): string {
  // LW uses "sprk:workspace" + ":activeLayout" (colon joiner).
  // SpaarkeAi uses "spaarke.ai.workspace" + ".activeLayout" (dot joiner).
  // Detect the joiner from the prefix's terminal character convention.
  const joiner = prefix.includes(".") && !prefix.includes(":") ? "." : ":";
  return `${prefix}${joiner}${suffix}`;
}

function getCachedActiveLayout(prefix: string): WorkspaceLayoutDto | null {
  try {
    const cached = sessionStorage.getItem(cacheKey(prefix, "activeLayout"));
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto) : null;
  } catch {
    return null;
  }
}

function setCachedActiveLayout(prefix: string, layout: WorkspaceLayoutDto): void {
  try {
    sessionStorage.setItem(
      cacheKey(prefix, "activeLayout"),
      JSON.stringify(layout),
    );
  } catch {
    /* quota / privacy mode — ignore */
  }
}

function getCachedLayoutsList(prefix: string): WorkspaceLayoutDto[] | null {
  try {
    const cached = sessionStorage.getItem(cacheKey(prefix, "layoutsList"));
    return cached ? (JSON.parse(cached) as WorkspaceLayoutDto[]) : null;
  } catch {
    return null;
  }
}

function setCachedLayoutsList(
  prefix: string,
  layouts: WorkspaceLayoutDto[],
): void {
  try {
    sessionStorage.setItem(
      cacheKey(prefix, "layoutsList"),
      JSON.stringify(layouts),
    );
  } catch {
    /* quota / privacy mode — ignore */
  }
}

/**
 * Invalidate the sessionStorage cache for a given namespace.
 *
 * LegalWorkspace's wizard save handlers call this directly (without a re-
 * render) to clear stale data so the next mount re-fetches fresh from the BFF.
 */
export function invalidateLayoutCache(
  cacheKeyPrefix: string = DEFAULT_CACHE_PREFIX,
): void {
  try {
    sessionStorage.removeItem(cacheKey(cacheKeyPrefix, "activeLayout"));
    sessionStorage.removeItem(cacheKey(cacheKeyPrefix, "layoutsList"));
  } catch {
    /* ignore */
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetches workspace layouts from the BFF and manages active-layout state.
 *
 * Lifecycle:
 *   1. On mount (and when deps change), check `isAuthenticated` + `bffBaseUrl`
 *      readiness — defer if not ready.
 *   2. Hydrate from sessionStorage cache (instant render) unless `embedded`.
 *   3. In parallel, fetch `/api/workspace/layouts` + `/api/workspace/layouts/default`.
 *   4. Resolve active layout via cascade:
 *      a. `initialWorkspaceId` (deep-link, fetch-by-id if not in list)
 *      b. `pinnedLayoutIdKey` value from sessionStorage
 *      c. BFF default
 *      d. first default → first system → first layout
 *      e. `fallbackLayout` (if provided) → null
 *   5. Update cache for next visit (unless `embedded`).
 *
 * Errors:
 *   - List 4xx/5xx: warns, treats list as empty
 *   - Default 4xx/5xx: warns, cascade picks next
 *   - Network error / catch: sets `error`; if `fallbackLayout` present, renders
 *     it; otherwise renders empty.
 */
export function useWorkspaceLayouts<TParsed = unknown>(
  options: UseWorkspaceLayoutsOptions<TParsed>,
): UseWorkspaceLayoutsResult<TParsed> {
  const {
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated,
    parseLayoutJson,
    fallbackLayout,
    initialWorkspaceId,
    pinnedLayoutIdKey,
    embedded = false,
    cacheKeyPrefix = DEFAULT_CACHE_PREFIX,
  } = options;

  const [layouts, setLayouts] = useState<WorkspaceLayoutDto[]>([]);
  const [activeLayout, setActiveLayout] = useState<WorkspaceLayoutDto | null>(
    null,
  );
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState(0);

  // Track mount state to avoid setState after unmount
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  // -------------------------------------------------------------------------
  // Fetch layouts on mount + when fetchKey / auth / bffBaseUrl change
  // -------------------------------------------------------------------------

  useEffect(() => {
    // Defer until auth + runtime config are ready. Without this guard,
    // `buildBffApiUrl(bffBaseUrl, ...)` would throw on the first render when
    // bffBaseUrl is still "". (Carried forward from the SpaarkeAi adaptation —
    // task 081 root-cause fix.)
    if (!isAuthenticated || !bffBaseUrl) {
      // If consumer supplied a fallback AND we have no cache hit, render the
      // fallback immediately so the surface always has something to show.
      // This preserves the pre-change LegalWorkspace behaviour where
      // `getBffBaseUrl()` throwing fell straight to `SYSTEM_DEFAULT_LAYOUT`.
      if (fallbackLayout && !isAuthenticated === false && !bffBaseUrl) {
        // (This branch intentionally narrow — runs when auth is OK but bff
        // config is missing.) Fall through to fallback render below.
      }
      if (fallbackLayout && !bffBaseUrl) {
        setLayouts([fallbackLayout]);
        setActiveLayout(fallbackLayout);
        setIsLoading(false);
        setError(null);
        return;
      }
      setIsLoading(true);
      return;
    }

    let cancelled = false;

    async function fetchLayouts(): Promise<void> {
      // -----------------------------------------------------------------
      // Cache-first hydration (mirrors pre-change pattern). When `embedded`
      // is true, skip cache reads entirely so sibling embedded tabs don't
      // share state through the cache.
      // -----------------------------------------------------------------
      const cachedList = embedded ? null : getCachedLayoutsList(cacheKeyPrefix);
      const cachedActive = embedded
        ? null
        : getCachedActiveLayout(cacheKeyPrefix);
      const hasCachedData =
        cachedList && cachedList.length > 0 && cachedActive;

      if (hasCachedData && !cancelled && mountedRef.current) {
        setLayouts(cachedList);
        setActiveLayout(cachedActive);
        setIsLoading(false);
        // Continue to fetch from API below to revalidate
      } else {
        setIsLoading(true);
      }
      setError(null);

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
            `[useWorkspaceLayouts] Failed to fetch layouts list: ${listRes.status}`,
          );
        }

        // Parse default layout
        let defaultLayout: WorkspaceLayoutDto | null = null;
        if (defaultRes.ok) {
          defaultLayout = (await defaultRes.json()) as WorkspaceLayoutDto;
        } else {
          console.warn(
            `[useWorkspaceLayouts] Failed to fetch default layout: ${defaultRes.status}`,
          );
        }

        if (cancelled || !mountedRef.current) return;

        // -----------------------------------------------------------------
        // Active layout resolution cascade
        // -----------------------------------------------------------------
        let resolvedActive: WorkspaceLayoutDto | null = null;

        // 1. Deep-link via initialWorkspaceId (LegalWorkspace usage)
        if (initialWorkspaceId) {
          const fromList = allLayouts.find(
            (l) => l.id === initialWorkspaceId,
          );
          if (fromList) {
            resolvedActive = fromList;
          } else {
            // Not in list — fetch by id (404 falls through silently)
            try {
              const deepLinkRes = await authenticatedFetch(
                buildBffApiUrl(
                  bffBaseUrl,
                  `/workspace/layouts/${initialWorkspaceId}`,
                ),
              );
              if (cancelled || !mountedRef.current) return;
              if (deepLinkRes.ok) {
                resolvedActive =
                  (await deepLinkRes.json()) as WorkspaceLayoutDto;
              } else {
                console.warn(
                  `[useWorkspaceLayouts] Deep-linked layout ${initialWorkspaceId} not found (${deepLinkRes.status}), falling back to default`,
                );
              }
            } catch (deepLinkErr) {
              console.warn(
                "[useWorkspaceLayouts] Failed to fetch deep-linked layout, falling back to default:",
                deepLinkErr,
              );
            }
          }
        }

        // 2. Pinned-id via sessionStorage (SpaarkeAi usage)
        if (!resolvedActive && pinnedLayoutIdKey) {
          let pinnedId: string | null = null;
          try {
            pinnedId =
              window.sessionStorage?.getItem(pinnedLayoutIdKey) ?? null;
          } catch {
            /* ignore */
          }
          if (pinnedId) {
            resolvedActive =
              allLayouts.find((l) => l.id === pinnedId) ?? null;
          }
        }

        // 3. BFF default layout
        if (!resolvedActive && defaultLayout) {
          resolvedActive = defaultLayout;
        }

        // 4. First default → first system → first layout
        if (!resolvedActive && allLayouts.length > 0) {
          resolvedActive =
            allLayouts.find((l) => l.isDefault) ??
            allLayouts.find((l) => l.isSystem) ??
            allLayouts[0];
        }

        // 5. Consumer-provided hardcoded fallback (LegalWorkspace usage)
        if (!resolvedActive && fallbackLayout) {
          resolvedActive = fallbackLayout;
        }

        if (!cancelled && mountedRef.current) {
          // Apply layouts list — use fallback if list was empty and we have one
          if (allLayouts.length > 0) {
            setLayouts(allLayouts);
          } else if (fallbackLayout) {
            setLayouts([fallbackLayout]);
          } else {
            setLayouts([]);
          }

          setActiveLayout(resolvedActive);

          // Cache updates (skipped when embedded)
          if (!embedded) {
            if (resolvedActive) {
              setCachedActiveLayout(cacheKeyPrefix, resolvedActive);
            }
            const listToCache =
              allLayouts.length > 0
                ? allLayouts
                : fallbackLayout
                  ? [fallbackLayout]
                  : null;
            if (listToCache) {
              setCachedLayoutsList(cacheKeyPrefix, listToCache);
            }
          }

          setIsLoading(false);
        }
      } catch (err) {
        if (cancelled || !mountedRef.current) return;

        const message = err instanceof Error ? err.message : "Unknown error";
        console.warn(
          "[useWorkspaceLayouts] Layout fetch failed:",
          message,
        );

        setError(message);
        if (fallbackLayout) {
          setLayouts([fallbackLayout]);
          setActiveLayout(fallbackLayout);
        } else {
          setLayouts([]);
          setActiveLayout(null);
        }
        setIsLoading(false);
      }
    }

    fetchLayouts();

    return () => {
      cancelled = true;
    };
    // authenticatedFetch is included in deps per ADR-028 — function-based
    // contract means the host is expected to pass a stable reference (typical
    // case: a module-level singleton or a useCallback-memoized closure).
  }, [
    fetchKey,
    bffBaseUrl,
    isAuthenticated,
    authenticatedFetch,
    initialWorkspaceId,
    pinnedLayoutIdKey,
    embedded,
    cacheKeyPrefix,
    fallbackLayout,
  ]);

  // -------------------------------------------------------------------------
  // setActiveLayoutById — switch to a different layout
  // -------------------------------------------------------------------------

  const setActiveLayoutById = useCallback(
    (layoutId: string) => {
      // Look up in the current layouts list first
      const found = layouts.find((l) => l.id === layoutId);
      if (found) {
        setActiveLayout(found);
        if (!embedded) {
          setCachedActiveLayout(cacheKeyPrefix, found);
        }
        return;
      }

      // Not found locally — fetch by ID from the BFF
      if (!isAuthenticated || !bffBaseUrl) {
        console.warn(
          "[useWorkspaceLayouts] setActiveLayoutById called before auth ready",
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
              if (!embedded) {
                setCachedActiveLayout(cacheKeyPrefix, layout);
              }
            }
          } else {
            console.warn(
              `[useWorkspaceLayouts] Failed to fetch layout ${layoutId}: ${res.status}`,
            );
          }
        } catch (err) {
          console.warn(
            "[useWorkspaceLayouts] Failed to fetch layout by ID:",
            err,
          );
        }
      })();
    },
    [
      layouts,
      embedded,
      cacheKeyPrefix,
      isAuthenticated,
      bffBaseUrl,
      authenticatedFetch,
    ],
  );

  // -------------------------------------------------------------------------
  // Refetch — invalidate cache and re-run the effect
  // -------------------------------------------------------------------------

  const refetch = useCallback(() => {
    invalidateLayoutCache(cacheKeyPrefix);
    setFetchKey((k) => k + 1);
  }, [cacheKeyPrefix]);

  // -------------------------------------------------------------------------
  // Derived: parsedActiveLayout (only when parseLayoutJson supplied)
  // -------------------------------------------------------------------------

  const parsedActiveLayout: TParsed | undefined = parseLayoutJson
    ? (() => {
        try {
          if (activeLayout) {
            const raw = JSON.parse(activeLayout.sectionsJson) as unknown;
            return parseLayoutJson(raw);
          }
          return parseLayoutJson(undefined);
        } catch (err) {
          console.warn(
            "[useWorkspaceLayouts] Failed to parse sectionsJson:",
            err,
          );
          return parseLayoutJson(undefined);
        }
      })()
    : undefined;

  // -------------------------------------------------------------------------
  // Derived: status
  // -------------------------------------------------------------------------

  const status: WorkspaceLoadingStatus = (() => {
    if (isLoading) return "loading";
    if (error) return "error";
    // First visit: user has zero user-created layouts (only system layouts).
    // Only meaningful when consumer supplied a fallback (LW case) — otherwise
    // "no user layouts" is just "empty" and status stays "loaded".
    const hasUserLayouts = layouts.some((l) => !l.isSystem);
    if (!hasUserLayouts && fallbackLayout) return "first-visit";
    return "loaded";
  })();

  return {
    layouts,
    activeLayout,
    parsedActiveLayout,
    isLoading,
    status,
    error,
    setActiveLayoutById,
    refetch,
  };
}
