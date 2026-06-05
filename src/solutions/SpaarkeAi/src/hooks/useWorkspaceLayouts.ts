/**
 * useWorkspaceLayouts — SpaarkeAi adapter for the consolidated shared-lib hook.
 *
 * # Why this file exists (R4 task 051 / C-3, 2026-05-26)
 *
 * The two pre-existing divergent copies of `useWorkspaceLayouts` (one in
 * LegalWorkspace, one here in SpaarkeAi) have been consolidated into a single
 * context-agnostic hook in `@spaarke/ai-widgets/src/hooks/useWorkspaceLayouts.ts`
 * (per FR-13).
 *
 * This file is now a thin SpaarkeAi-specific ADAPTER that:
 *
 *   1. Preserves the exact options-object signature `{ bffBaseUrl,
 *      authenticatedFetch, isAuthenticated }` that the three SpaarkeAi
 *      consumers (WorkspacePane, WorkspacePaneMenu, ManageWorkspacesPane)
 *      already pass in. Zero consumer file changes required (Risk R-3
 *      mitigation — behavioural parity with the pre-change hook).
 *
 *   2. Pins the SpaarkeAi cache namespace `spaarke.ai.workspace.*` and the
 *      pinned-id sessionStorage key `spaarke.workspace.activeLayoutId` so
 *      pre-change cached entries continue to hydrate post-migration (zero
 *      cache loss across the cut-over).
 *
 *   3. Re-exports `WorkspaceLayoutDto` so `services/workspaceLayoutMutations.ts`
 *      and other SpaarkeAi callers continue to import the DTO type from this
 *      path.
 *
 * # Source reference
 *
 * Consolidated implementation: `src/client/shared/Spaarke.AI.Widgets/src/hooks/useWorkspaceLayouts.ts`
 *
 * # Standards
 *
 *   - ADR-012 (consolidated logic in `@spaarke/*` lib; this file is a thin
 *     consumer adapter — not a re-implementation)
 *   - ADR-028 (function-based auth — adapter forwards `authenticatedFetch`
 *     + `isAuthenticated` from `useAiSession()` straight through; no token
 *     snapshots anywhere in the call chain)
 */

// Deep-import directly from the hook module — see LegalWorkspace adapter for
// the bundle-size rationale (skip the `@spaarke/ai-widgets` barrel's
// side-effect widget registration).
import {
  useWorkspaceLayouts as useSharedWorkspaceLayouts,
  type WorkspaceLayoutDto,
  type AuthenticatedFetch,
  type UseWorkspaceLayoutsResult as SharedUseWorkspaceLayoutsResult,
} from "@spaarke/ai-widgets/hooks/useWorkspaceLayouts";

// Re-export the DTO type so `services/workspaceLayoutMutations.ts` and other
// SpaarkeAi callers that import `WorkspaceLayoutDto` from this path continue
// to resolve.
export type { WorkspaceLayoutDto };

/** Cache namespace prefix used by the SpaarkeAi adapter. */
const SPAARKEAI_CACHE_KEY_PREFIX = "spaarke.ai.workspace";

/**
 * sessionStorage key holding a pinned layout id (set by WorkspacePaneMenu when
 * the user picks a layout from Switch Workspace). Honoured during the active-
 * layout cascade BEFORE the BFF default. Matches the pre-change SpaarkeAi key
 * byte-identical so existing pin entries continue to apply post-migration.
 */
const SPAARKEAI_PINNED_LAYOUT_ID_KEY = "spaarke.workspace.activeLayoutId";

// ---------------------------------------------------------------------------
// Hook arguments (preserved EXACTLY from pre-change SpaarkeAi signature)
// ---------------------------------------------------------------------------

export interface UseWorkspaceLayoutsArgs {
  /** BFF host URL (no `/api` suffix). Typically `useAiSession().bffBaseUrl`. */
  bffBaseUrl: string;
  /** Authenticated fetch from `useAiSession()` (ADR-028). */
  authenticatedFetch: AuthenticatedFetch;
  /** True when MSAL has a valid token. The effect defers until this is true. */
  isAuthenticated: boolean;
}

// ---------------------------------------------------------------------------
// Result type (preserved EXACTLY from pre-change SpaarkeAi shape)
// ---------------------------------------------------------------------------

export interface UseWorkspaceLayoutsResult {
  /** All available layouts (system + user). Empty array while loading or on fetch failure. */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. Null while loading or if no layouts exist. */
  activeLayout: WorkspaceLayoutDto | null;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
  /** Switch to a different layout by ID. Fetches layout details from BFF if needed. */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF (invalidates the local cache first). */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * SpaarkeAi adapter — delegates to the consolidated `@spaarke/ai-widgets`
 * implementation with SpaarkeAi-specific options baked in.
 *
 * Mirrors the pre-change SpaarkeAi `useWorkspaceLayouts` signature 1:1 so the
 * three call sites (WorkspacePane, WorkspacePaneMenu, ManageWorkspacesPane)
 * require ZERO changes.
 */
export function useWorkspaceLayouts(
  args: UseWorkspaceLayoutsArgs,
): UseWorkspaceLayoutsResult {
  const result: SharedUseWorkspaceLayoutsResult = useSharedWorkspaceLayouts({
    bffBaseUrl: args.bffBaseUrl,
    authenticatedFetch: args.authenticatedFetch,
    isAuthenticated: args.isAuthenticated,
    // SpaarkeAi: pinned-id flow (sessionStorage), NO deep-link, NO fallback.
    pinnedLayoutIdKey: SPAARKEAI_PINNED_LAYOUT_ID_KEY,
    cacheKeyPrefix: SPAARKEAI_CACHE_KEY_PREFIX,
    // No `parseLayoutJson` — SpaarkeAi only needs the layouts list +
    // active layout id (the menu's Switch Workspace UX).
    // No `fallbackLayout` — SpaarkeAi degrades to an empty Switch Workspace
    // section when the BFF returns no layouts (the correct UX — no fake stub
    // layout in the menu).
  });

  // Map the shared-lib result → SpaarkeAi-shaped result (drop status / error /
  // parsedActiveLayout which the SpaarkeAi consumers don't use).
  return {
    layouts: result.layouts,
    activeLayout: result.activeLayout,
    isLoading: result.isLoading,
    setActiveLayoutById: result.setActiveLayoutById,
    refetch: result.refetch,
  };
}
