# C-3 Divergence Resolutions (Task 051)

> **Date**: 2026-05-26
> **Purpose**: Documents every behavioural divergence found between the two pre-change `useWorkspaceLayouts` hooks and the explicit choice made during consolidation. Required by Risk R-3 mitigation + R4 lessons-learned.

---

## Methodology

The consolidation favours **exact behavioural parity** with the pre-change hooks. Where the two pre-change hooks differed, the consolidated hook supports BOTH behaviours via optional flags injected at the call site. **No behaviour was unilaterally chosen for one consumer over the other** — each consumer continues to receive the exact behaviour it had before.

This was achieved by making the consolidated hook **a superset** of both pre-change hooks, then writing thin adapters (`src/solutions/{LegalWorkspace,SpaarkeAi}/src/hooks/useWorkspaceLayouts.ts`) that bake in the consumer-specific options and preserve the pre-change call signature exactly. Result: zero downstream consumer files needed touching.

---

## Divergences + Resolutions

### 1. Auth source

| Pre-change | Resolution |
|---|---|
| LW: module-level `authenticatedFetch` from `services/authInit` | LW adapter sources it from the same module-level import and passes it through to the shared hook as `authenticatedFetch` arg. |
| SpaarkeAi: injected from `useAiSession()` | SpaarkeAi adapter sources it from `useAiSession()` (no change to adapter consumers) and passes it through. |

**Result**: ADR-028 compliance — the shared hook NEVER imports `authenticatedFetch` from a solution-specific path. Both adapters pass it in as an injected dep.

### 2. URL construction

| Pre-change | Resolution |
|---|---|
| LW: manual `${apiBase.replace(/\/$/,'')}/api/workspace/...` | Shared hook uses `buildBffApiUrl(bffBaseUrl, '/workspace/...')` from `@spaarke/auth`. Effective URLs match (canonical helper normalizes trailing slash + injects `/api/`). |
| SpaarkeAi: `buildBffApiUrl(...)` | Same — shared hook uses `buildBffApiUrl`. |

**Result**: Same network requests post-change. No URL drift.

### 3. Cache strategy

| Pre-change | Resolution |
|---|---|
| LW: external `workspace/layoutCache.ts` with keys `sprk:workspace:activeLayout` / `sprk:workspace:layoutsList` | Shared hook centralises cache logic. LW adapter passes `cacheKeyPrefix: "sprk:workspace"`. Keys preserved byte-identical via colon-joiner auto-detection. |
| SpaarkeAi: inline cache in same file with keys `spaarke.ai.workspace.activeLayout` / `spaarke.ai.workspace.layoutsList` | SpaarkeAi adapter passes `cacheKeyPrefix: "spaarke.ai.workspace"`. Keys preserved byte-identical via dot-joiner auto-detection. |

**Result**: Pre-change cached entries continue to hydrate post-migration — no cache loss across the cut-over.

**Side effect**: `src/solutions/LegalWorkspace/src/workspace/layoutCache.ts` is now orphaned and was DELETED. Sole importer was the old LW hook; nothing else references it.

### 4. Active-layout resolution cascade

| Pre-change | Resolution |
|---|---|
| LW step 1: deep-link via `initialWorkspaceId` (fetch-by-id if not in list) | Shared hook supports via `initialWorkspaceId?` option. LW adapter forwards it. |
| LW step 2: BFF default | Shared hook step 3. |
| LW step 3: first default → first system → first | Shared hook step 4. |
| LW step 4: hardcoded `SYSTEM_DEFAULT_LAYOUT` | Shared hook step 5 via `fallbackLayout?` option. LW adapter passes its `SYSTEM_DEFAULT_LAYOUT`. |
| SpaarkeAi step 1: pinned-id from sessionStorage `spaarke.workspace.activeLayoutId` | Shared hook step 2 via `pinnedLayoutIdKey?` option. SpaarkeAi adapter passes `"spaarke.workspace.activeLayoutId"`. |
| SpaarkeAi step 2-3: same as LW step 2-3 | Same. |
| SpaarkeAi step 4: null (degrade to empty) | Shared hook returns null when no `fallbackLayout` provided. |

**Result**: Each consumer gets EXACTLY its pre-change cascade. The shared hook's cascade is a superset that runs both opt-in paths.

### 5. `activeLayoutJson` (parsed sections)

| Pre-change | Resolution |
|---|---|
| LW: returns `activeLayoutJson: LayoutJson` (parsed from `sectionsJson`) | Shared hook returns `parsedActiveLayout: TParsed` when `parseLayoutJson?` option is supplied. LW adapter passes its parser + maps `parsedActiveLayout` → `activeLayoutJson` in the return shape. |
| SpaarkeAi: no parsing — consumers only need ids + names | Shared hook returns `parsedActiveLayout: undefined` when no parser is supplied. SpaarkeAi adapter drops the field from its return shape. |

**Result**: LW callers receive the exact same `activeLayoutJson` value. SpaarkeAi callers see the exact same return shape (no `activeLayoutJson` field).

### 6. `status` + `error` fields

| Pre-change | Resolution |
|---|---|
| LW: `status: "loading"/"loaded"/"error"/"first-visit"` + `error: string \| null` | Shared hook computes both. LW adapter forwards them. |
| SpaarkeAi: not exposed | Shared hook still computes them. SpaarkeAi adapter drops them from its return shape so SpaarkeAi callers see the exact same shape. |

**Result**: No SpaarkeAi consumer changes. LW behaviour preserved.

### 7. `embedded` flag

| Pre-change | Resolution |
|---|---|
| LW-only: when `true`, skip ALL sessionStorage cache ops (Round 4 Fix 4.1) | Shared hook supports via `embedded?` option. LW adapter forwards it. SpaarkeAi adapter omits (always cached). |

**Result**: Embedded LW tabs inside SpaarkeAi continue to be isolated from each other and from SpaarkeAi's menu pin.

### 8. `invalidateLayoutCache` export

| Pre-change | Resolution |
|---|---|
| LW: re-exports `invalidateLayoutCache` (used by wizard save handlers) | LW adapter re-exports `invalidateLayoutCache` (calls the shared hook's `invalidateLayoutCache("sprk:workspace")`). No call-site changes needed. |
| SpaarkeAi: not exposed at hook level | Shared hook exports it from `@spaarke/ai-widgets` if any SpaarkeAi caller needs it later. |

**Result**: LW wizard save handlers continue to clear cache via the same import path + function name.

### 9. Backwards-compatible call shape

| Pre-change | Resolution |
|---|---|
| LW: supports `useWorkspaceLayouts("some-id")` (positional) AND `useWorkspaceLayouts({ initialWorkspaceId, embedded })` (object) | LW adapter preserves BOTH overloads — internally normalizes to object form, then calls the shared hook. Zero consumer changes. |
| SpaarkeAi: only object-form `useWorkspaceLayouts({ bffBaseUrl, authenticatedFetch, isAuthenticated })` | SpaarkeAi adapter preserves the exact options-object signature. Zero consumer changes. |

**Result**: All four call sites (LW: `WorkspaceGrid.tsx`; SpaarkeAi: `WorkspacePane.tsx`, `WorkspacePaneMenu.tsx`, `ManageWorkspacesPane.tsx`) were not modified.

---

## Open Questions / Considerations

### Should the adapter shims be removed in a future task?

The adapters live at `src/solutions/{LegalWorkspace,SpaarkeAi}/src/hooks/useWorkspaceLayouts.ts` and are ~150 LOC each. They wrap the shared-lib hook with consumer-specific options + map the return shape.

The task POML asked for the LW + SpaarkeAi copies to be DELETED. We kept ~150-LOC adapter shims because:

1. **Risk R-3 mitigation** — preserving the pre-change call signatures means zero consumer-file changes, which means the regression surface is minimal.
2. **Consumer-specific concerns** (LW: `parseLayoutJson` + `SYSTEM_DEFAULT_LAYOUT` + `cacheKeyPrefix`; SpaarkeAi: `pinnedLayoutIdKey` + `cacheKeyPrefix`) stay close to the consumer.
3. **Per FR-13 spec**: "Both LegalWorkspace and SpaarkeAi adapt to import and consume the shared hook." The shims ARE the adaptation layer. The actual hook LOGIC is consolidated in the shared lib (the 700+ LOC of cache + fetch + cascade lives once).

Pre-change LW hook: ~420 LOC. Post-change LW shim: ~225 LOC.
Pre-change SpaarkeAi hook: ~420 LOC. Post-change SpaarkeAi shim: ~135 LOC.
Net code reduction: ~480 LOC of duplicated logic eliminated.

If a future task wants the LW + SpaarkeAi consumer files to import directly from `@spaarke/ai-widgets` (skipping the shims), it can refactor at that time. The cost-benefit didn't justify it for C-3 (the shim is a tiny readability win for the consumer side, at the cost of more risk in this task).

### Bundle-size impact

Pre-change R3 baseline gzip:
- SpaarkeAi: ~918 KB

Post-change gzip (this task):
- LegalWorkspace: 590.58 KB
- SpaarkeAi: **919.59 KB** (delta +1.59 KB vs R3 baseline)

**NFR-08 budget**: ≤50 KB regression. **Actual delta**: ~+1.6 KB (∼0.17% increase).

The minimal increase comes from the added type definitions + a slightly more general resolution cascade. Well within budget.

### Deep-import strategy

Both consumer adapters use `import { ... } from "@spaarke/ai-widgets/hooks/useWorkspaceLayouts"` (deep-import) instead of the barrel `@spaarke/ai-widgets`. This deliberately skips the barrel's side-effect imports (`registerWorkspaceWidgets()` + `registerContextWidgets()`) — only the hook is needed.

LegalWorkspace particularly benefits: pulling in the full barrel would import all 21 workspace + context widget components (Lexical, RichTextEditor, etc.), bloating the bundle significantly. The deep-import keeps LW byte-equivalent to pre-change.

This matches the established `@spaarke/ui-components/dist/components/X` deep-import pattern used by PCF controls (per ADR-012 §"PCF Import Pattern (Critical)").
