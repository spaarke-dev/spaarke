/**
 * useWorkspaceLayouts — LegalWorkspace adapter for the consolidated shared-lib hook.
 *
 * # Why this file exists (R4 task 051 / C-3, 2026-05-26)
 *
 * The two pre-existing divergent copies of `useWorkspaceLayouts` (one here in
 * LegalWorkspace, one in `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts`)
 * have been consolidated into a single context-agnostic hook in
 * `@spaarke/ai-widgets/src/hooks/useWorkspaceLayouts.ts` (per FR-13).
 *
 * This file is now a thin LegalWorkspace-specific ADAPTER that:
 *
 *   1. Sources `authenticatedFetch` + `bffBaseUrl` from LegalWorkspace's
 *      module-level `services/authInit.ts` + `config/runtimeConfig.ts`
 *      (the pre-change behaviour).
 *
 *   2. Wires LegalWorkspace's `parseLayoutJson` (returning the in-app
 *      `LayoutJson` type from `@spaarke/ui-components`) so consumers receive
 *      `activeLayoutJson` exactly as before.
 *
 *   3. Wires the `SYSTEM_DEFAULT_LAYOUT` hardcoded fallback so the workspace
 *      always renders even when the BFF is unavailable (the pre-change
 *      LegalWorkspace behaviour — `status: "first-visit"`).
 *
 *   4. Preserves the LegalWorkspace cache namespace `sprk:workspace:*` so
 *      sessionStorage entries written by the old hook continue to hydrate
 *      mounts post-migration (zero cache loss across the cut-over).
 *
 *   5. Preserves the existing call signatures:
 *        - Positional: `useWorkspaceLayouts("layout-id")`
 *        - Options:    `useWorkspaceLayouts({ initialWorkspaceId, embedded })`
 *      So zero consumer file changes are required (FR-13 acceptance,
 *      Risk R-3 mitigation — behavioural parity with the pre-change hook).
 *
 *   6. Re-exports `invalidateLayoutCache` so wizard save handlers continue
 *      to clear the cache without importing from a new path.
 *
 * # Source reference
 *
 * Consolidated implementation: `src/client/shared/Spaarke.AI.Widgets/src/hooks/useWorkspaceLayouts.ts`
 *
 * # Standards
 *
 *   - ADR-012 (consolidated logic in `@spaarke/*` lib; this file is a thin
 *     consumer adapter — not a re-implementation)
 *   - ADR-028 (function-based auth — adapter sources `authenticatedFetch`
 *     from LW's `services/authInit.ts` and forwards it as an injected dep)
 */

import { useMemo } from "react";
// Deep-import directly from the hook module to avoid pulling in the heavy
// `@spaarke/ai-widgets` barrel (which registers ALL widget components via
// side-effect imports). LegalWorkspace only needs the hook — NOT the widget
// registry — so deep-importing keeps the LW bundle byte-equivalent to
// pre-change (NFR-08 bundle-size budget).
import {
  useWorkspaceLayouts as useSharedWorkspaceLayouts,
  invalidateLayoutCache as invalidateSharedLayoutCache,
  type WorkspaceLayoutDto,
  type WorkspaceLoadingStatus,
  type UseWorkspaceLayoutsResult as SharedUseWorkspaceLayoutsResult,
} from "@spaarke/ai-widgets/hooks/useWorkspaceLayouts";
import { authenticatedFetch } from "../services/authInit";
import { getBffBaseUrl } from "../config/runtimeConfig";
import type { LayoutJson } from "../workspace/buildDynamicWorkspaceConfig";
import { SYSTEM_DEFAULT_LAYOUT_JSON } from "../workspace/buildDynamicWorkspaceConfig";

// Re-export type aliases so existing LegalWorkspace imports continue to resolve
// from this path.
export type { WorkspaceLayoutDto, WorkspaceLoadingStatus };

/** Cache namespace prefix used by the LegalWorkspace adapter. */
const LW_CACHE_KEY_PREFIX = "sprk:workspace";

/**
 * Re-exported invalidate helper. Wizard save handlers call this to clear
 * stale data so the next mount re-fetches fresh from the BFF.
 *
 * (LegalWorkspace uses the `sprk:workspace` cache namespace — passed
 * explicitly so this adapter remains the only place the namespace literal
 * appears in LegalWorkspace.)
 */
export function invalidateLayoutCache(): void {
  invalidateSharedLayoutCache(LW_CACHE_KEY_PREFIX);
}

// ---------------------------------------------------------------------------
// LegalWorkspace's parseLayoutJson — maps raw `sectionsJson` to LayoutJson.
//
// The shared hook calls this with either the parsed JSON object (from the
// active layout's `sectionsJson`) OR `undefined` when there is no active
// layout / parsing failed. LegalWorkspace's pre-change behaviour returns
// `SYSTEM_DEFAULT_LAYOUT_JSON` in both fallback cases, so we mirror that.
// ---------------------------------------------------------------------------

function parseLayoutJson(raw: unknown): LayoutJson {
  if (raw && typeof raw === "object") {
    const candidate = raw as Partial<LayoutJson>;
    if (
      typeof candidate.schemaVersion === "number" &&
      Array.isArray(candidate.rows)
    ) {
      return candidate as LayoutJson;
    }
  }
  return SYSTEM_DEFAULT_LAYOUT_JSON;
}

// ---------------------------------------------------------------------------
// System default layout stub — preserved EXACTLY from pre-change LegalWorkspace
// hook (Round 4 Fix 4.1) so the workspace always renders even when the API
// is unavailable.
// ---------------------------------------------------------------------------

const SYSTEM_DEFAULT_LAYOUT: WorkspaceLayoutDto = {
  id: "00000000-0000-0000-0000-000000000001",
  name: "Corporate Workspace",
  layoutTemplateId: "3-row-mixed",
  sectionsJson: JSON.stringify(SYSTEM_DEFAULT_LAYOUT_JSON),
  isDefault: true,
  sortOrder: 0,
  isSystem: true,
  // R4 task 053 (B-4 / FR-07): Unix-epoch sentinel for hard-coded system
  // layouts — `ManageWorkspacesPane` treats this as "—" / no modified date.
  modifiedOn: "1970-01-01T00:00:00+00:00",
};

// ---------------------------------------------------------------------------
// Options + Result types — preserved exactly so consumers don't change
// ---------------------------------------------------------------------------

/**
 * Options for `useWorkspaceLayouts` (LegalWorkspace adapter).
 *
 * The `embedded` flag and `initialWorkspaceId` come from Round 4 Fix 4.1
 * (2026-05-21) — see the consolidated shared-lib hook for behaviour details.
 */
export interface UseWorkspaceLayoutsOptions {
  /** Deep-link target layout id (preferred over BFF default when set). */
  initialWorkspaceId?: string;
  /**
   * When `true`, suppress sessionStorage cache reads + writes so each embedded
   * tab is isolated from sibling tabs and from the SpaarkeAi menu's pinned
   * choice. Defaults to `false` for backwards compatibility.
   */
  embedded?: boolean;
}

export interface UseWorkspaceLayoutsResult {
  /** All available layouts (system + user). */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. */
  activeLayout: WorkspaceLayoutDto | null;
  /** Parsed LayoutJson from the active layout's sectionsJson. */
  activeLayoutJson: LayoutJson;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
  /** Computed status for rendering loading states. */
  status: WorkspaceLoadingStatus;
  /** Error message if layout fetch failed. */
  error: string | null;
  /** Switch to a different layout by ID. */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF. */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook — preserves the legacy positional + object overloads
// ---------------------------------------------------------------------------

/**
 * Adapter hook — delegates to the consolidated `@spaarke/ai-widgets`
 * implementation with LegalWorkspace-specific options baked in.
 *
 * Accepts either:
 *   - The legacy positional `initialWorkspaceId` argument, OR
 *   - The new options object form.
 *
 * Mirrors the pre-change LegalWorkspace `useWorkspaceLayouts` signature 1:1
 * so consumers (notably `WorkspaceGrid.tsx`) require ZERO changes.
 */
export function useWorkspaceLayouts(
  initialWorkspaceIdOrOptions?: string | UseWorkspaceLayoutsOptions,
): UseWorkspaceLayoutsResult {
  // Normalize the legacy positional arg vs the new options object.
  const opts: UseWorkspaceLayoutsOptions =
    typeof initialWorkspaceIdOrOptions === "object" &&
    initialWorkspaceIdOrOptions !== null
      ? initialWorkspaceIdOrOptions
      : { initialWorkspaceId: initialWorkspaceIdOrOptions };

  // -------------------------------------------------------------------------
  // bffBaseUrl resolution — preserves pre-change LW behaviour of falling back
  // to the SYSTEM_DEFAULT_LAYOUT when runtime config is not initialized.
  //
  // The shared hook handles this internally: when bffBaseUrl === "" AND a
  // fallbackLayout is supplied, the shared hook renders the fallback
  // immediately without attempting a fetch. We compute the safe bffBaseUrl
  // here and let the shared hook do the right thing.
  // -------------------------------------------------------------------------
  const bffBaseUrl = useMemo(() => {
    try {
      return getBffBaseUrl();
    } catch {
      // Runtime config not initialized — pass "" so the shared hook renders
      // the fallback layout instead of attempting an invalid fetch.
      return "";
    }
  }, []);

  // -------------------------------------------------------------------------
  // Delegate to shared-lib hook
  //
  // `isAuthenticated: true` mirrors LegalWorkspace's pre-change behaviour
  // (no auth-state gating — `authenticatedFetch` internally awaits init).
  // The empty-bffBaseUrl branch in the shared hook still renders fallback.
  // -------------------------------------------------------------------------
  const result: SharedUseWorkspaceLayoutsResult<LayoutJson> =
    useSharedWorkspaceLayouts<LayoutJson>({
      bffBaseUrl,
      authenticatedFetch,
      isAuthenticated: true,
      parseLayoutJson,
      fallbackLayout: SYSTEM_DEFAULT_LAYOUT,
      initialWorkspaceId: opts.initialWorkspaceId,
      embedded: opts.embedded === true,
      cacheKeyPrefix: LW_CACHE_KEY_PREFIX,
    });

  // Map shared-lib result → LW-shaped result (rename parsedActiveLayout →
  // activeLayoutJson, ensure non-undefined LayoutJson).
  return {
    layouts: result.layouts,
    activeLayout: result.activeLayout,
    activeLayoutJson: result.parsedActiveLayout ?? SYSTEM_DEFAULT_LAYOUT_JSON,
    isLoading: result.isLoading,
    status: result.status,
    error: result.error,
    setActiveLayoutById: result.setActiveLayoutById,
    refetch: result.refetch,
  };
}
