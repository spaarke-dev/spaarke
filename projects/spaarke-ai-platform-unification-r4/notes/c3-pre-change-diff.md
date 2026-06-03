# C-3 Pre-Change Behavioral Snapshot (Task 051)

> **Date**: 2026-05-26
> **Purpose**: Regression-test reference for the dual `useWorkspaceLayouts` hook consolidation. Captures every divergence between LegalWorkspace's and SpaarkeAi's copies BEFORE the change so post-change behaviour can be verified parity-equal.

---

## Source Files Compared

| Surface | File |
|---|---|
| LegalWorkspace (canonical / original) | `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` |
| SpaarkeAi (Round 4 Fix 1 copy, 2026-05-21) | `src/solutions/SpaarkeAi/src/hooks/useWorkspaceLayouts.ts` |

SpaarkeAi copy header explicitly notes it is a "FAITHFUL ADAPTATION" of LegalWorkspace's hook with three surgical adaptations.

---

## Divergences

### 1. Auth source

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| `authenticatedFetch` source | module-level import from `../services/authInit` | injected as arg (from `useAiSession()`) |
| `bffBaseUrl` source | module-level `getBffBaseUrl()` from `../config/runtimeConfig` | injected as arg (from `useAiSession()`) |
| `isAuthenticated` gating | NO explicit guard (relies on `getBffBaseUrl()` throwing) | YES — `if (!isAuthenticated \|\| !bffBaseUrl) { setIsLoading(true); return; }` |
| Deferral until config ready | catches `getBffBaseUrl()` throw → fallback layout | early-returns the effect; runs again when deps change |

**Consolidation decision**: Per FR-13 / ADR-028, the consolidated hook accepts `bffBaseUrl`, `authenticatedFetch`, `isAuthenticated` as injected args. LegalWorkspace's call sites will be adapted to source these from its existing `authInit` + `runtimeConfig` modules.

### 2. URL construction

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| List endpoint | `${apiBase}/api/workspace/layouts` (manual trim) | `buildBffApiUrl(bffBaseUrl, "/workspace/layouts")` (canonical helper) |
| Default endpoint | `${apiBase}/api/workspace/layouts/default` | `buildBffApiUrl(bffBaseUrl, "/workspace/layouts/default")` |
| By-id endpoint | `${apiBase}/api/workspace/layouts/${id}` | `buildBffApiUrl(bffBaseUrl, \`/workspace/layouts/${layoutId}\`)` |

**Same effective URL**. `buildBffApiUrl` normalizes trailing slash + injects `/api/` prefix.

**Consolidation decision**: Use `buildBffApiUrl` from `@spaarke/auth` (the canonical helper per ADR-028 + auth-v2). Effective URLs match.

### 3. Cache strategy

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| Cache module | external `../workspace/layoutCache.ts` | inline in same file |
| Active key | `sprk:workspace:activeLayout` | `spaarke.ai.workspace.activeLayout` |
| List key | `sprk:workspace:layoutsList` | `spaarke.ai.workspace.layoutsList` |
| Re-export `invalidateLayoutCache` | YES (from layoutCache) | NO |
| Pinned-id read (`spaarke.workspace.activeLayoutId`) | NO | YES (sessionStorage pin honoured on mount) |

**Different cache key namespaces** prevent cross-pollution when both apps run side-by-side. Consolidation MUST preserve namespaced cache keys per consumer or risk the two apps stomping each other's cache.

**Consolidation decision**: Hook accepts a `cacheNamespace: "legalworkspace" \| "spaarkeai"` (or simpler: pass cache key prefix as an internal config arg). LW passes `"sprk:workspace"` (existing); SpaarkeAi passes `"spaarke.ai.workspace"` (existing). Keys remain byte-identical.

### 4. Active layout resolution cascade

| Step | LegalWorkspace | SpaarkeAi |
|---|---|---|
| 1 | Deep-link via `initialWorkspaceId` (fetch by id if not in list) | Pinned-id from sessionStorage (`spaarke.workspace.activeLayoutId`) |
| 2 | BFF default layout | BFF default layout |
| 3 | First default → first system → first layout | First default → first system → first layout |
| 4 | `SYSTEM_DEFAULT_LAYOUT` (hardcoded stub) | `null` (graceful empty) |

**Different "step 1" semantics** but same intent (pre-selected layout). **Different "step 4" fallback** — LW falls back to a hardcoded stub; SpaarkeAi degrades to null. These are real behavioural differences the consumers depend on.

**Consolidation decision**: 
- Accept optional `initialWorkspaceId?: string` (LW deep-link) OR optional `pinnedLayoutIdKey?: string` (SpaarkeAi sessionStorage key). Both are "pre-selected layout" hints.
- Accept optional `fallbackLayout?: WorkspaceLayoutDto` per FR-13. LW passes `SYSTEM_DEFAULT_LAYOUT`; SpaarkeAi omits (degrades to null).

### 5. `activeLayoutJson` derived field

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| Returns `activeLayoutJson: LayoutJson` | YES — parses `sectionsJson` via inline `parseLayoutJson` | NO — consumer only uses layouts list + active layout id |

**Consolidation decision**: Make `parseLayoutJson` an optional prop per FR-13. When provided, the hook computes `parsedActiveLayout` and returns it; when omitted, the return type's `parsedActiveLayout` is `undefined`. LW passes its `parseLayoutJson` callback; SpaarkeAi omits.

### 6. Status / error fields

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| `status: "loading" \| "loaded" \| "error" \| "first-visit"` | YES | NO |
| `error: string \| null` | YES | NO |

**Consolidation decision**: Both fields are stable, cheap to compute, and useful to ALL consumers. Include in consolidated `UseWorkspaceLayoutsResult`. SpaarkeAi consumers that ignore them lose nothing.

### 7. `embedded` flag (Round 4 Fix 4.1)

LW-only flag: when `true`, hook skips ALL sessionStorage cache reads + writes (each embedded LegalWorkspaceApp inside SpaarkeAi tabs is isolated).

**Consolidation decision**: Preserve as `embedded?: boolean` option. Default false. When true → cache reads/writes are skipped.

### 8. Refetch / `invalidateLayoutCache` export

| Aspect | LegalWorkspace | SpaarkeAi |
|---|---|---|
| Hook re-exports `invalidateLayoutCache` | YES | NO |
| `refetch()` invalidates cache | YES (calls `invalidateLayoutCache()` first) | YES (calls local `invalidateCache()` first) |

**Consolidation decision**: Hook owns the cache. Export an `invalidateLayoutCache(namespace)` function alongside the hook. LW consumers that import the standalone export continue to work (single shim file in LW that re-exports from `@spaarke/ai-widgets`).

### 9. Backwards-compatible call shape

LW supports BOTH `useWorkspaceLayouts("some-id")` (legacy positional) and `useWorkspaceLayouts({ initialWorkspaceId, embedded })` (object form). The positional form is for older call sites that didn't migrate to the options object.

**Consolidation decision**: Single options-object signature. All LW call sites already use the object form (verified — `WorkspaceGrid.tsx:169` uses object form). No back-compat positional needed.

---

## Network Behaviour Parity Check

Both hooks make these network calls on mount:

1. `GET /api/workspace/layouts` (list)
2. `GET /api/workspace/layouts/default` (default)
3. Optional: `GET /api/workspace/layouts/{id}` (by-id, when deep-link or pin doesn't match list)

Both fire 1 + 2 in parallel via `Promise.all`. Both honour `cancelled` + `mountedRef`. Both call `authenticatedFetch` so auth headers are identical (per ADR-028).

**Verdict**: Network behaviour IDENTICAL after consolidation, given the same effective URLs (`buildBffApiUrl` normalization).

---

## Error UX Parity Check

| Scenario | LegalWorkspace | SpaarkeAi |
|---|---|---|
| `getBffBaseUrl()` throws (config not ready) | Falls back to `SYSTEM_DEFAULT_LAYOUT`, sets `isLoading=false` | Defers via `isAuthenticated`/`bffBaseUrl` guard — stays `isLoading=true` until deps arrive |
| List 4xx/5xx | warns, `allLayouts=[]`, falls back to `[SYSTEM_DEFAULT_LAYOUT]` | warns, `allLayouts=[]`, renders empty |
| Default 4xx/5xx | warns, cascade picks first default/system/first | warns, cascade picks first default/system/first |
| Total catch (network error) | sets `error`, falls back to `[SYSTEM_DEFAULT_LAYOUT]` | `console.error`, sets `layouts=[]`, `activeLayout=null` |

**Differences**: LW has a "always show something" guarantee via `SYSTEM_DEFAULT_LAYOUT`. SpaarkeAi degrades to "Switch Workspace" empty state. These are CONSUMER choices, not hook intrinsics.

**Consolidation decision**: `fallbackLayout?: WorkspaceLayoutDto` per FR-13. When provided, used in catch + on empty list. When omitted, hook returns `layouts=[]`/`activeLayout=null`. Each consumer chooses.

---

## Summary of behavioural promises that MUST hold post-change

| Promise | Consumer | Mechanism in consolidated hook |
|---|---|---|
| Cache-first hydration (instant render on revisit) | both | shared logic, cache namespace per consumer |
| Cache invalidate on `refetch()` | both | `refetch` invalidates the namespaced cache |
| Auth-defer (no fetch before isAuthenticated) | both | guard `if (!isAuthenticated \|\| !bffBaseUrl) return` |
| Deep-link via `initialWorkspaceId` (fetch-by-id if not in list) | LW | `initialWorkspaceId?` option |
| Pinned-id via sessionStorage (`spaarke.workspace.activeLayoutId`) | SpaarkeAi | `pinnedLayoutIdKey?` option |
| `embedded` skips all cache ops | LW (embedded in SpaarkeAi) | `embedded?` option |
| `activeLayoutJson` parsed from active layout's `sectionsJson` | LW | `parseLayoutJson?` option |
| Hardcoded fallback layout when API down + first-visit semantics | LW | `fallbackLayout?` option + `status` computed |
| `status: "loading"/"loaded"/"error"/"first-visit"` | LW | computed in hook |
| Empty list = empty state (no fake stub) | SpaarkeAi | when `fallbackLayout` omitted |
| `invalidateLayoutCache` exported alongside hook | LW (wizard save handlers) | export from `@spaarke/ai-widgets` |

If any of these promises are not preserved, R-3 mitigation fails.
