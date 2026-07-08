# Track-B Batch 3 Evidence — Dead PCF dirs + R1 client registries/providers/cross-pane

> Task 072 · FR-TB-01 / NFR-08 · Executed 2026-07-05 · Rigor: STANDARD
> Cross-checked against `notes/audit-inputs/SPAARKE-AI-CODE-INVENTORY.md` §9 (Client shared + PCF) and §6 before every deletion.
> Batch-2 lesson applied: VERIFY-DEAD-FIRST — every item grepped across `src/client/**`, `src/solutions/**`, and all of `src/` before action.

---

## 1. Per-item verdict table

| # | Batch item | Verdict | Detail |
|---|---|---|---|
| 1a | PCF `AIMetadataExtractor` | **DELETED** | Dir contained only `.gitkeep` (empty, per inventory). Removed dir + its stale exclude glob in `src/client/pcf/tsconfig.json`. No references in `controls.pcfproj`, `create-solution.ps1`, `package.json`, or any solution/`*.cdsproj` — **no solution-manifest edits needed**. |
| 1b | PCF `AnalysisWorkspace` | **ALREADY ABSENT** | No `src/client/pcf/AnalysisWorkspace/` exists anywhere in this worktree (inventory listed it as a source-less orphaned build artifact — the artifact was never tracked in git). Same-name survivor: `src/client/code-pages/AnalysisWorkspace/` is a LIVE code page (different surface) — untouched. |
| 1c | PCF `AnalysisBuilder` | **ALREADY ABSENT** | No directory anywhere in the repo. Same-name survivors are all live and intentional: `src/solutions/PlaybookLibrary/` (merged replacement, compat comments), `openAnalysisBuilder()` launcher functions (`DocumentUploadWizard/nextStepLauncher.ts`, AI.Widgets `MeetingScheduleWidget`/`EmailComposeWidget`), `IAnalysisBuilderContext`/`IAnalysisBuilderLaunchMessage` types (LegalWorkspace GetStarted), "Ported from AnalysisBuilder" provenance comments (Playbook components), `sprk_analysis_commands.js` webresource. None reference the deleted PCF dir as code. |
| 1d | PCF `PlaybookBuilderHost` | **ALREADY ABSENT** | No directory anywhere in the repo. Doc drift found and fixed: `src/client/pcf/CLAUDE.md` still documented the control (module-overview bullet + full "PlaybookBuilderHost Architecture (Special Case)" section) — both removed. Grep now zero. |
| 1e | PCF `DrillThroughWorkspace` | **ALREADY ABSENT** | No directory, zero references of any kind in `src/`. |
| 2 | `StandaloneAiProvider` + `useStandaloneAi` (AI.Context) | **DELETED** | `providers/StandaloneAiContext.tsx`, `providers/useStandaloneAi.ts`, and their dedicated types file `types/standalone-context.ts` (`StandaloneAiContextValue`, `StandaloneAiProviderProps`, `StandaloneContextMapping`, `STANDALONE_*` sessionStorage keys — all consumed ONLY by the deleted provider trio). Zero live importers anywhere (`src/solutions/SpaarkeAi` migrated to `AiSessionProvider`/`useAiSession` in R2). Barrels updated: `providers/index.ts`, `types/index.ts`. Stale doc-comment references scrubbed in 10 live files (see §3). **Kept**: `providers/useEntityResolver.ts` — NOT in batch list; still exported from the package barrel (orphan candidate for a later Track-B batch; sole code importer was the deleted provider). |
| 3 | AI.Outputs R1 `output-registry` + `source-registry` | **DELETED** | Entire `src/registry/` dir (`index.ts`, `output-registry.ts`, `source-registry.ts`). Zero importers of `outputWidgetRegistry` / `sourceWidgetRegistry` / `resolveOutputWidget` / `resolveSourceWidget` / `registerOutputWidget` outside the deleted files. Package barrel `src/index.ts` updated. **LIVE registries untouched**: `WorkspaceWidgetRegistry` / `ContextWidgetRegistry` in `Spaarke.AI.Widgets` verified by path — no file under `Spaarke.AI.Widgets` was deleted or functionally modified (comment-only scrubs, see §3). Registry-entry *types* in AI.Outputs `types/index.ts` (`OutputWidgetRegistryEntry` etc.) kept — types module not in batch. |
| 4 | 4 unregistered AI.Outputs widgets (Chart, DataTable, Timeline, DocumentCompare) | **DELETED** | `output-widgets/{ChartWidget,DataTableWidget,TimelineWidget,DocumentCompareWidget}.tsx` + their 4 test files + their 4 mock-prop factories in `__tests__/test-utils.tsx`. Zero importers outside AI.Outputs (the 6 LIVE source-widgets imported by `Spaarke.AI.Widgets` context widgets are a different set — untouched). Barrel `output-widgets/index.ts` updated; 7 registered/live output widgets (BudgetDashboard, SearchResults, AnalysisEditor, ContractComparison, StatusSummary, Recommendation, ActionPlan) retained. |
| 5 | AI.Outputs `cross-pane/` (CustomEvent mechanism) | **DELETED** | Entire `src/cross-pane/` dir (`cross-pane-events.ts`, `CrossPaneLink.tsx`, `useCrossPane.ts`, `index.ts`). Zero importers of `dispatchCrossPaneLink` / `subscribeToCrossPaneLinks` / `useDispatchCrossPaneLink` / `useCrossPaneSubscription` / `CROSS_PANE_LINK_EVENT` outside the deleted dir. Superseded by `PaneEventBus` (Spaarke.AI.Widgets) — **PaneEventBus untouched**. Same-name survivors: the `CrossPaneLink` *interface* (data model) in AI.Outputs `types/` (not in batch) and generic "cross-pane" wording in live PaneEventBus/SprkChat docs. |
| 6 | `SprkChatExportWord` | **DELETED** | `Spaarke.UI.Components/src/components/SprkChat/SprkChatExportWord.tsx`. Already de-wired at R4 task 025 (FR-08): barrel export removed then, zero importers, no dedicated test file. Removal-note comments in `SprkChat.tsx` + `SprkChat/index.ts` reworded so the symbol greps zero. |
| 7 | `SprkChatBridge` | **KEPT-WITH-REASON** | Inventory §9 stale on this item (same failure mode as batch-2 `AddToAssistantToggle`). `SprkChatBridge` is LIVE-WIRED, not dead: (a) `SprkChat` public API — `bridge` prop in `SprkChat/types.ts:717` + `useSelectionListener` hook imports it; (b) `RichTextEditor/hooks/useDocumentStreamConsumer.ts` imports it; (c) LIVE code page `src/client/code-pages/AnalysisWorkspace` constructs it (`App.tsx:403-408`, dynamic import) and consumes via `useReAnalysisProgress` / `useDocumentStreaming` / `useDiffReview`; (d) `useSseStream` forwards document-stream tokens to it (R2-051 pipeline); (e) exported from the `@spaarke/ui-components` services barrel; (f) `__test-harness__/StreamingWriteHarness.tsx` E2E harness. Deleting requires refactoring live SprkChat/RichTextEditor/AnalysisWorkspace surfaces — out of scope for a deadwood batch. Its 3 test files kept with it. Deferred to the surface-cutover wave that retires the BroadcastChannel path. |

## 2. Grep-zero verification (SHOWN)

`git grep -n <symbol> -- src` (tracked files; node_modules/dist excluded by tracking), run after deletions + comment scrubs:

```
AIMetadataExtractor: 0 hits
PlaybookBuilderHost: 0 hits          (after src/client/pcf/CLAUDE.md doc-drift fix)
DrillThroughWorkspace: 0 hits
StandaloneAiProvider: 0 hits
useStandaloneAi: 0 hits
StandaloneAiContext: 0 hits
SprkChatExportWord: 0 hits
output-registry: 0 hits
source-registry: 0 hits
outputWidgetRegistry: 0 hits
sourceWidgetRegistry: 0 hits
ChartWidget: 0 hits
DataTableWidget: 0 hits
TimelineWidget: 0 hits
DocumentCompareWidget: 0 hits
dispatchCrossPaneLink: 0 hits
subscribeToCrossPaneLinks: 0 hits
useCrossPaneSubscription: 0 hits
useDispatchCrossPaneLink: 0 hits
CROSS_PANE_LINK_EVENT: 0 hits
mockChartProps / mockDataTableProps / mockTimelineProps / mockDocumentCompareProps: 0 hits each
```

Intentional same-name survivors (explicitly NOT the deleted items):
- `AnalysisWorkspace` — live code page `src/client/code-pages/AnalysisWorkspace/**` (the deleted item was the PCF dir, which never existed in this worktree).
- `AnalysisBuilder` — live merged `PlaybookLibrary` compat code, `openAnalysisBuilder()` launchers, `IAnalysisBuilderContext` types, provenance comments (see §1 row 1c).
- `cross-pane` (generic term) — live `PaneEventBus`, SprkChat selection flow, SpaarkeAi shell comments; the deleted item was the AI.Outputs `cross-pane/` module, whose exported symbols all grep zero (above).
- `CrossPaneLink` *interface* — data-model type in AI.Outputs `types/index.ts` (types module not in batch); the deleted *component* export greps zero via its unique symbols.
- `SprkChatBridge` — kept-with-reason item (§1 row 7), not a survivor of a deletion.

## 3. Files changed

**Deleted (22 tracked files):**
- `src/client/pcf/AIMetadataExtractor/.gitkeep` (dir removed)
- `src/client/shared/Spaarke.AI.Context/src/providers/StandaloneAiContext.tsx`
- `src/client/shared/Spaarke.AI.Context/src/providers/useStandaloneAi.ts`
- `src/client/shared/Spaarke.AI.Context/src/types/standalone-context.ts`
- `src/client/shared/Spaarke.AI.Outputs/src/registry/{index,output-registry,source-registry}.ts`
- `src/client/shared/Spaarke.AI.Outputs/src/cross-pane/{index.ts,cross-pane-events.ts,CrossPaneLink.tsx,useCrossPane.ts}`
- `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/{ChartWidget,DataTableWidget,TimelineWidget,DocumentCompareWidget}.tsx`
- `src/client/shared/Spaarke.AI.Outputs/src/output-widgets/__tests__/{ChartWidget,DataTableWidget,TimelineWidget,DocumentCompareWidget}.test.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatExportWord.tsx`

**Edited — barrels/config (7):** `src/client/pcf/tsconfig.json` (stale exclude glob), AI.Context `providers/index.ts` + `types/index.ts`, AI.Outputs `src/index.ts` + `output-widgets/index.ts` + `source-widgets/index.ts` (comment), AI.Outputs `__tests__/test-utils.tsx` (orphaned mock factories removed).

**Edited — stale-comment scrubs for grep-zero (12, comment-only, zero behavior change):** AI.Outputs `no-hardcoded-colors.test.ts`; AI.Widgets `index.ts`, `AiSessionProvider.tsx`, `useAiSession.ts`; UI.Components `SprkChat.tsx`, `SprkChat/index.ts`, `SprkChat/types.ts`, `hooks/useSseStream.ts`; SpaarkeAi `App.tsx`, `ConversationPane.tsx`, `ThreePaneShell.tsx`, `main.tsx`, `launch-resolver.ts`; plus `src/client/pcf/CLAUDE.md` (PlaybookBuilderHost doc-drift section removed).

## 4. Build verification (SHOWN tails)

| Package | Command | Result |
|---|---|---|
| `Spaarke.AI.Context` | `npm run build` (tsc — library has no build:prod) | ✅ `> tsc` → EXIT=0 |
| `Spaarke.AI.Outputs` | `npm run build` (tsc) | ✅ `> tsc` → EXIT=0 |
| `Spaarke.AI.Outputs` | `npx jest --ci` | ✅ `Test Suites: 14 passed, 14 total · Tests: 100 passed, 100 total` |
| `Spaarke.AI.Widgets` | `npm run build` (tsc) | ✅ `> tsc` → EXIT=0 (proves live registries + consumers of @spaarke/ai-context + @spaarke/ai-outputs intact) |
| `Spaarke.UI.Components` | `npm run build` (tsc) | ✅ `> tsc` → EXIT=0 |
| `Spaarke.UI.Components` | `npx jest SprkChat useSseStream` (touched surfaces) | ✅ `Test Suites: 24 passed, 24 total · Tests: 361 passed, 361 total` |
| `src/solutions/SpaarkeAi` | `npm run build` (Vite + tsc-surface-gate, per batch-2 finding) | ✅ vite build + ribbon build complete → EXIT=0 |

Notes:
- AI.Outputs jest needed `npm install --no-save ts-node` — `jest.config.ts` requires ts-node which was absent from the package's stale node_modules (pre-existing env issue, `--no-save` so `package.json` untouched).
- AI.Context has no test files (no `__tests__/` in package) — no test run applicable.
- `src/client/pcf` build **skipped with justification**: the only change removes the `"AIMetadataExtractor/**"` entry from the `exclude` array of `tsconfig.json` — a glob that now matches nothing (dir deleted; it contained only `.gitkeep`, so it was never compiled even before). Compile input set is byte-identical; `controls.pcfproj` never referenced the dir; pcf root has no node_modules installed in this worktree.
- `src/client/code-pages/AnalysisWorkspace` not rebuilt: untouched by this batch; its imports from `@spaarke/ai-context` (`useChatSession`, `useChatContextMapping`, `IAnalysisChatContextResponse`) and `@spaarke/ui-components` (`SprkChatBridge` — kept) are all preserved, and the AI.Context/UI.Components tsc builds prove the exporting packages are whole.

## 5. ADR-038 scaffolding-test register (for task-090 `/test-diet`)

| Test artifact | Class | Disposition |
|---|---|---|
| `Spaarke.AI.Outputs/src/output-widgets/__tests__/ChartWidget.test.tsx` | SCAFFOLDING (test of deleted dead component) | Deleted this batch |
| `Spaarke.AI.Outputs/src/output-widgets/__tests__/DataTableWidget.test.tsx` | SCAFFOLDING | Deleted this batch |
| `Spaarke.AI.Outputs/src/output-widgets/__tests__/TimelineWidget.test.tsx` | SCAFFOLDING | Deleted this batch |
| `Spaarke.AI.Outputs/src/output-widgets/__tests__/DocumentCompareWidget.test.tsx` | SCAFFOLDING | Deleted this batch |
| `test-utils.tsx` mock factories (mockChart/mockDataTable/mockTimeline/mockDocumentCompare Props) | SCAFFOLDING support code | Deleted this batch |
| `Spaarke.UI.Components/src/services/__tests__/SprkChatBridge{.test,.integration.test,.security.test}.ts` | MAINTAIN (component kept-with-reason; security test enforces ADR-015 no-auth-token constraint) | Kept — revisit when SprkChatBridge itself is retired at surface cutover |

## 6. Inventory-staleness findings (feed back to Track-B)

1. **`SprkChatBridge` is NOT dead** — inventory §9 "Client shared" verdict stale; it is wired into SprkChat's public API, RichTextEditor streaming, and the live AnalysisWorkspace code page. Retirement belongs to the pane-communication cutover (PaneEventBus), not a deadwood sweep.
2. **The 4 "source-less orphaned build artifact" PCF dirs** (`AnalysisWorkspace`, `AnalysisBuilder`, `PlaybookBuilderHost`, `DrillThroughWorkspace`) were never tracked in git — they existed only as untracked build output in the inventory author's checkout. In a fresh worktree there is nothing to delete; only doc drift remained (fixed for PlaybookBuilderHost).
3. **Orphan candidate surfaced**: `Spaarke.AI.Context/src/providers/useEntityResolver.ts` lost its only code importer when the R1 provider was deleted; still barrel-exported. Candidate for a later Track-B batch (not deleted here — outside batch list).
