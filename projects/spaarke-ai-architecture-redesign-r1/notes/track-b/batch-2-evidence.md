# Track-B Batch 2 Evidence — SpaarkeAi Insights renderer cluster + page deadwood

> **Task**: 071 (`tasks/071-track-b-batch-2-insights-renderers.poml`) · FR-TB-01 / NFR-08
> **Date**: 2026-07-05
> **Rigor**: STANDARD (deadwood deletion batch; grep-zero + build:prod verification per NFR-08)
> **Cross-checked against**: `notes/audit-inputs/SPAARKE-AI-CODE-INVENTORY.md` §9 (lines 303–306) + `notes/audit-inputs/agent-findings/agent-findings-spaarkeai-codepage.md` §§C/D/E/I + `notes/audit-inputs/agent-findings/agent-findings-engine-projects.md` (renderer-cluster liveness verdict: claimed by NO active project)

---

## 1. Per-item verdict table

| # | Item (inventory §9) | Files | Verdict | Liveness evidence |
|---|---|---|---|---|
| 1 | **Insights renderer cluster** (R5-origin, PR #345; superseded by R6 Pillar 5) | 17 files: `src/solutions/SpaarkeAi/src/components/conversation/insights/` (12 src + 3 tests), `src/config/insightsRendererConfig.ts`, `src/services/insightsQueryClient.ts` + `src/services/__tests__/insightsQueryClient.test.ts` | **DELETED** | Pre-delete grep across all `src/`: references only intra-cluster + own tests + 2 stale shared-lib *comments* (`Spaarke.AI.Widgets/src/index.ts:192`, `register-structured-output-stream-widget.ts:28`) — comments updated (see §3). Never imported by ConversationPane or any live file. |
| 2 | `notificationContextLoader.ts` (self-declared dead, header lines 44–49) | `src/services/notificationContextLoader.ts` | **DELETED** | Sole runtime reference was `main.tsx` threading `loadSpaarkeAiNotificationContext` into `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`. Verified dead end-to-end: `Spaarke.DailyBriefing.Components/src/widgets/dailyBriefing.registration.ts:150-155` marks the option `@deprecated R2.1 — Ignored` ("`DailyBriefingApp` reads `appnotification` directly via `useBriefingNotifications(webApi)`; no external loader injection needed") and its module header explicitly awaits this cleanup ("They'll be deprecated in a follow-up PR once SpaarkeAi `main.tsx` is updated to stop passing them through"). main.tsx wiring removed (behavior-neutral: option was ignored). |
| 3a | `SendToWorkspaceButton.tsx` (Pillar-6b, D-C-08) | component + `__tests__/SendToWorkspaceButton.test.tsx` | **DELETED** | Zero non-test importers (grep across `src/`: only self + own test + one historical comment in `__mocks__/sdap-client.ts`, updated). |
| 3b | `PinToMatterButton.tsx` (Pillar-6b, D-C-10) | component + `__tests__/PinToMatterButton.test.tsx` | **DELETED** | Zero non-test importers (grep: self + own test + historical comments in `sdap-client.ts` mock and `AddToAssistantToggle.test.tsx`, both updated). |
| 3c | `AddToAssistantToggle.tsx` (Pillar-6b, D-C-09) + its test | — | **KEPT-WITH-REASON** | **LIVE.** `WorkspaceTabManagerComponent.tsx:44` imports it and `:779` renders `<AddToAssistantToggle tabId=… sessionId=… onChange={(next) => onToggleVisibility(…)}/>` in the tab bar; `WorkspaceTabManagerComponent` is itself rendered by live `WorkspacePane.tsx:977`. The inventory's "zero importers" claim is stale — the comment at `WorkspaceTabManagerComponent.tsx:282` attributes the wiring to "R6 Pillar 9 / task 098", i.e. it was wired AFTER the audit finding was authored. Its test (`__tests__/AddToAssistantToggle.test.tsx`) is a MAINTAIN-class test of a live component and also stays. |
| 4 | `ChatHistoryPanel.tsx` (SpaarkeAi wrapper) | `src/components/ChatHistoryPanel.tsx` | **DELETED** | Zero importers; superseded by `HistoryOverlay.tsx` (two-history-surfaces duplicate, audit §H.5). Only refs were doc comments in `ConversationPane.tsx:56` (`@see`) and `HistoryOverlay.tsx:367` (precedent note) — both stripped. **Same-name distinction**: the presentational `ChatHistoryPanel` in shared `@spaarke/ai-outputs` (`src/client/shared/Spaarke.AI.Outputs/src/chat-history/ChatHistoryPanel.tsx`) is a DIFFERENT component, NOT in this batch, and is RETAINED (it is on the "Client shared" register for a different Track-B batch). |
| 5 | Vestigial refs in `WorkspacePane` / `ConversationPane` | — | **ConversationPane: 1 comment line removed; WorkspacePane: NO EDIT NEEDED** | ConversationPane's only reference to a batch item was the stale `@see ChatHistoryPanel.tsx` docblock line 56 → replaced with `@see HistoryOverlay.tsx`. WorkspacePane contains ZERO references to any deleted batch item (verified by grep). Its `summaryTabIdRef`/`streamFocusOverrideRef` no-op sentinels (audit §D) do not reference any deleted component — they are outside this batch's mandate (dead-reference removal only) and were left untouched. ConversationPane decomposition remains task 045's scope. |

**Totals: 23 files deleted · 1 item kept-with-reason (AddToAssistantToggle + test) · 0 compat shims retained.**

### Deleted file list (git history preserves content)

```
src/solutions/SpaarkeAi/src/components/conversation/insights/DeclineResponseRenderer.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/EmptyResultHint.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsErrorRenderer.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsResponseRenderer.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/LowConfidenceBadge.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/PlaybookResponseRenderer.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/RagResponseRenderer.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/index.ts
src/solutions/SpaarkeAi/src/components/conversation/insights/insightsErrorMessages.ts
src/solutions/SpaarkeAi/src/components/conversation/insights/insightsRetryPolicy.ts
src/solutions/SpaarkeAi/src/components/conversation/insights/retryAfterParser.ts
src/solutions/SpaarkeAi/src/components/conversation/insights/types.ts
src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/InsightsResponseRenderer.error-handling.test.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/InsightsResponseRenderer.test.tsx
src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/LowConfidenceBadge.test.tsx
src/solutions/SpaarkeAi/src/config/insightsRendererConfig.ts
src/solutions/SpaarkeAi/src/services/insightsQueryClient.ts
src/solutions/SpaarkeAi/src/services/__tests__/insightsQueryClient.test.ts
src/solutions/SpaarkeAi/src/services/notificationContextLoader.ts
src/solutions/SpaarkeAi/src/components/ChatHistoryPanel.tsx
src/solutions/SpaarkeAi/src/components/workspace/SendToWorkspaceButton.tsx
src/solutions/SpaarkeAi/src/components/workspace/PinToMatterButton.tsx
src/solutions/SpaarkeAi/src/components/workspace/__tests__/SendToWorkspaceButton.test.tsx
src/solutions/SpaarkeAi/src/components/workspace/__tests__/PinToMatterButton.test.tsx
```

*(Note: 23 deletions + AddToAssistantToggle kept — the "~14 files" inventory estimate for the cluster was 17 on disk.)*

## 2. Grep-zero verification (SHOWN output, post-deletion, NFR-08)

Command pattern: `grep -r -F "<symbol>" src/ | wc -l` from repo root (`rg` unavailable in this shell; `grep -r` used; the identical sweep was also run via the ripgrep-backed Grep tool with "No matches found").

```
InsightsResponseRenderer : 0 hits in src/
RagResponseRenderer : 0 hits in src/
PlaybookResponseRenderer : 0 hits in src/
DeclineResponseRenderer : 0 hits in src/
InsightsErrorRenderer : 0 hits in src/
EmptyResultHint : 0 hits in src/
LowConfidenceBadge : 0 hits in src/
insightsQueryClient : 0 hits in src/
insightsRendererConfig : 0 hits in src/
insightsErrorMessages : 0 hits in src/
insightsRetryPolicy : 0 hits in src/
retryAfterParser : 0 hits in src/
conversation/insights : 0 hits in src/
notificationContextLoader : 0 hits in src/
loadSpaarkeAiNotificationContext : 0 hits in src/
SendToWorkspaceButton : 0 hits in src/
PinToMatterButton : 0 hits in src/
---
ChatHistoryPanel in src/solutions/SpaarkeAi : 0 hits (SpaarkeAi wrapper scope)
ChatHistoryPanel in shared lib (retained, different component):
src/client/shared/Spaarke.AI.Outputs/src/chat-history/ChatHistoryPanel.tsx
src/client/shared/Spaarke.AI.Outputs/src/chat-history/ChatHistoryPanel.types.ts
src/client/shared/Spaarke.AI.Outputs/src/chat-history/ChatSessionCard.tsx
src/client/shared/Spaarke.AI.Outputs/src/chat-history/index.ts
src/client/shared/Spaarke.AI.Outputs/src/chat-history/useChatHistoryFilter.ts
src/client/shared/Spaarke.AI.Outputs/src/types/index.ts
```

**`ChatHistoryPanel` scoping note (per task step 3)**: the symbol is intentionally grepped within `src/solutions/SpaarkeAi/` — the deleted item is the SpaarkeAi *wrapper*. The shared `@spaarke/ai-outputs` `ChatHistoryPanel` is a distinct presentational component outside this batch (it belongs to the "Client shared" dead-code register handled by a different Track-B batch) and is retained.

**`AddToAssistantToggle` is NOT in the sweep** — it was kept (live), see §1 item 3c.

## 3. Reference-site edits (dead-reference removal only)

| File | Edit | Class |
|---|---|---|
| `src/solutions/SpaarkeAi/src/main.tsx` | Removed `loadSpaarkeAiNotificationContext` import (+ its R2-task-002 comment block) and the `dailyBriefing: { loadNotificationContext }` option → `createLegalWorkspaceSectionRegistry({})`; docblocks updated with removal rationale | Functional (behavior-neutral — the option was `@deprecated`/ignored by the factory) |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | Line 56 `@see ChatHistoryPanel.tsx …` → `@see HistoryOverlay.tsx …` | Comment only (the ONLY ConversationPane edit; decomposition = task 045) |
| `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` | Dropped "see ChatHistoryPanel.tsx for the same precedent" from a deps-comment | Comment only |
| `src/solutions/SpaarkeAi/src/__mocks__/sdap-client.ts` | Origin note no longer names `PinToMatterButton.test.tsx` (mock itself STAYS — jest `moduleNameMapper` infrastructure needed by the kept `AddToAssistantToggle.test.tsx` and any test importing the `@spaarke/ui-components` barrel) | Comment only |
| `src/solutions/SpaarkeAi/src/components/workspace/__tests__/AddToAssistantToggle.test.tsx` | Subpath-import rationale comment no longer points at deleted `PinToMatterButton.test.tsx` | Comment only |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` (line 192) | Schema-consumers comment no longer names `InsightsResponseRenderer` | Comment only |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-structured-output-stream-widget.ts` (line 28) | Dispatcher-list comment no longer names `InsightsResponseRenderer` | Comment only |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/widgets/dailyBriefing.registration.ts` (line 73) | Back-compat comment no longer names `loadSpaarkeAiNotificationContext` (notes the last call site was deleted by this task) | Comment only |
| `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` (line 54) | `sections` prop docblock no longer names `loadSpaarkeAiNotificationContext` | Comment only |
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` (line 86) | `dailyBriefing` option docblock no longer names `loadSpaarkeAiNotificationContext`; notes deprecation | Comment only |

The comment-only edits outside WorkspacePane/ConversationPane were required to satisfy NFR-08's repo-wide grep-zero for the deleted symbols; none changes behavior. `@spaarke/legal-workspace` and `@spaarke/daily-briefing-components` resolve `main: ./src/index.ts` (source-only facades — `Spaarke.LegalWorkspace/src/index.ts` re-exports `src/solutions/LegalWorkspace/src/index.ts`), so these edits are compiled by the SpaarkeAi Vite build itself (no separate dist rebuild artifact).

## 4. Build verification (SHOWN)

**Note on `build:prod`**: the SpaarkeAi solution (Vite code page) defines no `build:prod` script — its production build is `npm run build` (`check-html-css-reset` → `tsc-surface-gate` → `vite build` → rename to `spaarkeai.html` → `build:ribbon`). The `build:prod`-not-`build` rule (root CLAUDE.md §12 / FAILURE-MODES AP-1) applies to **PCF controls**, none of which are touched by this batch. Built via the sanctioned `scripts/Build-AllClientComponents.ps1 -Component SharedLibs, SpaarkeAi` (fresh worktree — shared-lib dists were absent); installs use `npm install --legacy-peer-deps --no-audit --no-fund` per §12 (script-internal).

Shared-lib chain (`Build-AllClientComponents.ps1 -Component SharedLibs, SpaarkeAi`, fresh worktree):

```
PASS  Spaarke.Auth (18.6s)
PASS  Spaarke.SdapClient (9.1s)
PASS  Spaarke.AI.Context (42.5s)
PASS  Spaarke.AI.Outputs (59.2s)
PASS  Spaarke.DocumentOperations (12.2s)
FAIL  Spaarke.Events.Components (39.1s)   <- pre-existing, see note below
FAIL  Spaarke.SmartTodo.Components (37.0s) <- pre-existing, see note below
PASS  Spaarke.UI.Components (57.6s)
PASS  Spaarke.AI.Widgets (43.0s)
PASS  Spaarke.Compose.Components (48.1s)
```

**Pre-existing-failure note (NOT batch-2 related)**: the two FAILs are `tsc --noEmit` runs whose cross-package source imports reach into `../Spaarke.UI.Components/src/**` (DataGrid / Kanban files — none of them batch-2 files) and error with `TS2307 Cannot find module 'react'` because UI.Components' `node_modules` had not been installed yet at that point in the fresh-worktree build order. Proof it is an install-order artifact: re-running both packages standalone AFTER UI.Components' install existed:

```
> @spaarke/events-components@0.1.0 build
> tsc --noEmit
EVENTS EXIT: 0

> @spaarke/smart-todo-components@0.1.0 build
> tsc --noEmit
SMARTTODO EXIT: 0
```

(This is the known cross-import issue owned by the parallel `fix-events-smarttodo-cross-imports` worktree.)

SpaarkeAi solution production build — `npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build` (= check-html-css-reset → tsc-surface-gate typecheck → `vite build` → rename → `build:ribbon`; SpaarkeAi is absent from the orchestrator's `$ViteSolutions` list — script drift, built directly). Tail:

```
added 613 packages in 35s
INSTALL EXIT: 0
✓ 3732 modules transformed.
rendering chunks...
[plugin vite:singlefile] Inlining: index-DfDuOuM-.js
computing gzip size...
dist/index.html  4,889.95 kB │ gzip: 1,360.19 kB
✓ built in 18.09s

> spaarke-ai@0.1.0 build:ribbon
> node scripts/build-ribbon.mjs
Building 3 ribbon script(s) → ...\src\solutions\SpaarkeAi\dist-ribbon
Ribbon build complete:
  DocumentComposeLaunch        globalName=Sprk.SpaarkeAi.DocumentComposeLaunch
  EntityFormLaunch             globalName=Sprk.SpaarkeAi.EntityFormLaunch
  WorkspaceLaunch              globalName=Sprk.SpaarkeAi.WorkspaceLaunch
BUILD EXIT: 0
```

Touched-surface test (kept `AddToAssistantToggle.test.tsx`, comment-edited):

```
Test Suites: 1 passed, 1 total
Tests:       8 passed, 8 total
Snapshots:   0 total
Time:        40.934 s
Ran all test suites matching src/components/workspace/__tests__/AddToAssistantToggle.test.tsx.
JEST EXIT: 0
```

## 5. ADR-038 / test-diet register (for task-090 reconciliation)

Deleted tests — all SCAFFOLDING class (tests of components that were themselves dead scaffolding, never wired into a live surface):

| Deleted test | Dead component under test |
|---|---|
| `insights/__tests__/InsightsResponseRenderer.test.tsx` | InsightsResponseRenderer (never wired) |
| `insights/__tests__/InsightsResponseRenderer.error-handling.test.tsx` | InsightsResponseRenderer (never wired) |
| `insights/__tests__/LowConfidenceBadge.test.tsx` | LowConfidenceBadge (never wired) |
| `services/__tests__/insightsQueryClient.test.ts` | insightsQueryClient (dead SSE client) |
| `workspace/__tests__/SendToWorkspaceButton.test.tsx` | SendToWorkspaceButton (zero importers) |
| `workspace/__tests__/PinToMatterButton.test.tsx` | PinToMatterButton (zero importers) |

Kept: `workspace/__tests__/AddToAssistantToggle.test.tsx` — MAINTAIN class (component is live, §1 item 3c).

## 6. Hot-path declaration

SpaarkeAi (`src/solutions/SpaarkeAi/**`) is a hot path (8 active projects per `projects/INDEX.md`). **The wrap-up PR description MUST flag this batch's SpaarkeAi touch per root CLAUDE.md §10 Hot-Path Declaration.** Also touched (comment-only): `Spaarke.AI.Widgets`, `Spaarke.DailyBriefing.Components`, `LegalWorkspace` (2 docblocks). No `src/server/**` files touched.
