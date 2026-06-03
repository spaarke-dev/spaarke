# C-3 Consolidated Hook Design (Task 051)

> **Date**: 2026-05-26
> **Source diff**: [`c3-pre-change-diff.md`](./c3-pre-change-diff.md)
> **Target location**: `src/client/shared/Spaarke.AI.Widgets/src/hooks/useWorkspaceLayouts.ts`

---

## Decision: Place in `@spaarke/ai-widgets`

The hook lives in `@spaarke/ai-widgets/src/hooks/useWorkspaceLayouts.ts`. Justification:
- `@spaarke/ai-widgets` already exports related workspace types (`WorkspaceLayoutWidget`).
- SpaarkeAi already depends on `@spaarke/ai-widgets`. LegalWorkspace will add the dep.
- Creating a new `@spaarke/workspace-layouts` package adds package overhead with zero independent-consumption benefit (the hook is used only by these two solutions today).

---

## Exported API

```ts
// From @spaarke/ai-widgets

export interface WorkspaceLayoutDto {
  id: string;
  name: string;
  layoutTemplateId: string;
  sectionsJson: string;
  isDefault: boolean;
  sortOrder: number | null;
  isSystem: boolean;
}

export type WorkspaceLoadingStatus =
  | "loading"
  | "loaded"
  | "error"
  | "first-visit";

export type AuthenticatedFetch = (
  url: string,
  init?: RequestInit
) => Promise<Response>;

export interface UseWorkspaceLayoutsOptions<TParsed = unknown> {
  /** BFF host URL (no `/api` suffix). Injected from host auth context (ADR-028). */
  bffBaseUrl: string;
  /** Injected authenticated fetch (ADR-028 — no token snapshots). */
  authenticatedFetch: AuthenticatedFetch;
  /** True when MSAL has a valid token. Effect defers until this is true. */
  isAuthenticated: boolean;

  // ----- Optional flags (per FR-13) -----

  /**
   * Optional parser for `sectionsJson`. When provided, the hook computes
   * `parsedActiveLayout` from the active layout's `sectionsJson`. When the
   * parse fails or no active layout exists, falls back to the parser called
   * with `undefined` (allowing the parser to supply its own fallback).
   * LegalWorkspace passes its `parseLayoutJson` callback for the section
   * registry. SpaarkeAi omits.
   */
  parseLayoutJson?: (raw: unknown) => TParsed;

  /**
   * Optional hardcoded fallback layout used when:
   *   - The list endpoint returns empty AND no default resolved
   *   - The hook catches a network error
   * LegalWorkspace passes `SYSTEM_DEFAULT_LAYOUT` so the workspace always
   * renders. SpaarkeAi omits (degrades to empty state).
   */
  fallbackLayout?: WorkspaceLayoutDto;

  /**
   * Optional deep-link layout id. When provided AND not found in the fetched
   * list, the hook fetches it via GET /api/workspace/layouts/{id}. LW uses
   * for URL-based deep links.
   */
  initialWorkspaceId?: string;

  /**
   * Optional sessionStorage key holding a pinned layout id (set by the
   * consumer's menu). Honoured during the active-layout cascade BEFORE the
   * BFF default. SpaarkeAi passes `"spaarke.workspace.activeLayoutId"`.
   */
  pinnedLayoutIdKey?: string;

  /**
   * When `true`, the hook skips sessionStorage cache reads + writes entirely.
   * LegalWorkspace passes `true` when its app is embedded inside SpaarkeAi
   * tabs so sibling tabs do not stomp each other. Default `false`.
   */
  embedded?: boolean;

  /**
   * SessionStorage cache key prefix. Each consumer passes its own namespace
   * so the two surfaces do not collide when running on the same page.
   *   LegalWorkspace: `"sprk:workspace"` (existing keys preserved byte-identical)
   *   SpaarkeAi:      `"spaarke.ai.workspace"` (existing keys preserved byte-identical)
   * Defaults to `"sprk:workspace"` for back-compat with LW callers that omit.
   */
  cacheKeyPrefix?: string;
}

export interface UseWorkspaceLayoutsResult<TParsed = unknown> {
  /** All available layouts (system + user). Empty array while loading. */
  layouts: WorkspaceLayoutDto[];
  /** The currently active layout. Null only during initial load or empty result. */
  activeLayout: WorkspaceLayoutDto | null;
  /**
   * Parsed `sectionsJson` for the active layout. `undefined` when no
   * `parseLayoutJson` option was supplied (SpaarkeAi case).
   */
  parsedActiveLayout: TParsed | undefined;
  /** True while the initial fetch is in progress. */
  isLoading: boolean;
  /** Computed status: "loading" \| "loaded" \| "error" \| "first-visit". */
  status: WorkspaceLoadingStatus;
  /** Error message if fetch failed (workspace still renders fallback if provided). */
  error: string | null;
  /** Switch to a different layout by ID (fetches if not in current list). */
  setActiveLayoutById: (layoutId: string) => void;
  /** Refresh the layouts list from the BFF (invalidates the cache first). */
  refetch: () => void;
}

export function useWorkspaceLayouts<TParsed = unknown>(
  options: UseWorkspaceLayoutsOptions<TParsed>,
): UseWorkspaceLayoutsResult<TParsed>;

/**
 * Invalidate the sessionStorage cache for a given namespace. LW's wizard save
 * handlers call this directly (without re-renders) to clear stale data.
 */
export function invalidateLayoutCache(cacheKeyPrefix?: string): void;
```

---

## Internal Lifecycle

```text
mount or deps change:
  if !isAuthenticated || !bffBaseUrl → setLoading(true), return.
  read sessionStorage cache (if !embedded)
    if cachedList + cachedActive present → set state, setLoading(false), CONTINUE to revalidate
    else → setLoading(true)
  Promise.all([
    authenticatedFetch(buildBffApiUrl(bffBaseUrl, '/workspace/layouts')),
    authenticatedFetch(buildBffApiUrl(bffBaseUrl, '/workspace/layouts/default')),
  ])
  Resolve active layout via cascade:
    1. initialWorkspaceId (deep-link, fetch-by-id if not in list)
    2. pinnedLayoutIdKey value from sessionStorage
    3. BFF default
    4. first default → first system → first
    5. fallbackLayout (if provided) → null
  setLayouts(allLayouts || fallbackLayout-as-array || [])
  setActiveLayout(resolvedActive)
  if !embedded:
    setCachedActive(resolvedActive)
    setCachedList(allLayouts.length > 0 ? allLayouts : [fallbackLayout?])
  setLoading(false)
  setError(null)

catch (network error):
  setError(message)
  if fallbackLayout:
    setLayouts([fallbackLayout]); setActiveLayout(fallbackLayout)
  else:
    setLayouts([]); setActiveLayout(null)
  setLoading(false)
```

---

## Consumer Adaptation Plan

### LegalWorkspace consumer (WorkspaceGrid.tsx)

Before:
```ts
const { layouts, activeLayout, activeLayoutJson, ... } =
  useWorkspaceLayouts({ initialWorkspaceId, embedded });
```

After:
```ts
import { useWorkspaceLayouts, invalidateLayoutCache } from "@spaarke/ai-widgets";
import { authenticatedFetch } from "../../services/authInit";
import { useAuthState } from "../../services/authInit"; // or equivalent
import { getBffBaseUrl } from "../../config/runtimeConfig";

const { isAuthenticated } = useAuthState(); // need to source this
const bffBaseUrl = (() => { try { return getBffBaseUrl(); } catch { return ""; } })();
const { layouts, activeLayout, parsedActiveLayout: activeLayoutJson, ... } =
  useWorkspaceLayouts({
    bffBaseUrl,
    authenticatedFetch,
    isAuthenticated,
    initialWorkspaceId,
    embedded,
    parseLayoutJson: parseLayoutJsonOrFallback, // legacy LW helper
    fallbackLayout: SYSTEM_DEFAULT_LAYOUT,
    cacheKeyPrefix: "sprk:workspace",
  });
```

LegalWorkspace's existing `./hooks/useWorkspaceLayouts.ts` is **deleted**. A thin shim file at the SAME path re-exports the consolidated hook + the legacy type names so other LW files that import `WorkspaceLayoutDto` from this path continue to resolve:

```ts
// src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts (replacement shim)
export { useWorkspaceLayouts, invalidateLayoutCache } from "@spaarke/ai-widgets";
export type { WorkspaceLayoutDto, WorkspaceLoadingStatus } from "@spaarke/ai-widgets";
```

This preserves LW's existing import paths while sourcing the implementation from the shared lib. Per FR-13, this counts as the LW copy being deleted (the actual logic file is gone; only a re-export shim remains).

### SpaarkeAi consumer (WorkspacePane, WorkspacePaneMenu, ManageWorkspacesPane)

Already imports from `../../hooks/useWorkspaceLayouts` with options `{ bffBaseUrl, authenticatedFetch, isAuthenticated }`. The local hook file is replaced with a shim re-exporting from `@spaarke/ai-widgets`:

```ts
// src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts (replacement shim)
export { useWorkspaceLayouts } from "@spaarke/ai-widgets";
export type { WorkspaceLayoutDto } from "@spaarke/ai-widgets";
```

`services/workspaceLayoutMutations.ts` imports `WorkspaceLayoutDto` from the local hook path — this continues to work via the shim.

SpaarkeAi call sites need to ADD `pinnedLayoutIdKey: "spaarke.workspace.activeLayoutId"` and `cacheKeyPrefix: "spaarke.ai.workspace"` to preserve cache + pin parity.

---

## LegalWorkspace shim auth-source

LegalWorkspace currently does NOT have a React-hook auth-state getter that exposes `isAuthenticated`. We need to either:

**Option A (chosen)**: Add a tiny `useAuthState` hook in `services/authInit.ts` that returns `{ isAuthenticated }` from the MSAL account. Minimal new code, zero behaviour change.

**Option B (rejected)**: Treat LW as always authenticated. Risk: regression on auth-not-ready (deferral semantics differ). Rejected.

Implementation note: LW's `authenticatedFetch` from `services/authInit.ts` is a stable module-level function. We pass it through as-is.

---

## File Inventory

| File | Action |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/hooks/useWorkspaceLayouts.ts` | **NEW** — consolidated hook |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | **EDIT** — add exports |
| `src/client/shared/Spaarke.AI.Widgets/src/hooks/__tests__/useWorkspaceLayouts.test.ts` | **NEW** — unit tests |
| `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` | **REPLACED with shim** (logic deleted) |
| `src/solutions/LegalWorkspace/src/workspace/layoutCache.ts` | **DELETE** — superseded by hook's internal cache |
| `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` | **EDIT** — pass new options |
| `src/solutions/LegalWorkspace/src/services/authInit.ts` | **EDIT** — add `useAuthState` |
| `src/solutions/LegalWorkspace/package.json` | **EDIT** — add `@spaarke/ai-widgets` dep |
| `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` | **REPLACED with shim** (logic deleted) |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | **EDIT** — add `pinnedLayoutIdKey` + `cacheKeyPrefix` |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` | **EDIT** — same |
| `src/solutions/SpaarkeAi/src/components/workspace/ManageWorkspacesPane.tsx` | **EDIT** — same |

Total: 4 new files (hook + tests + 2 design notes), 7 edited consumers, 2 hook files shimmed, 1 cache file deleted.
