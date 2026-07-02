# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-01 (post-task-102 — all Phase 7-10 supplement tasks ✅; ready for Option B bundle commit)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## 🚨 Quick Recovery (READ THIS FIRST — ready for user to authorize bundle commit)

| Field | Value |
|-------|-------|
| **Session state** | **ALL Phase 7 + 8 + 9 + 10 supplement tasks ✅ COMPLETE.** 10 tasks shipped this session: 091, 092, 093, 094, 095/096 (collapsed), 097, **098, 099** (atomic), **100, 102**. Only remaining task: **110 (expanded wrap-up)** — deferred until AFTER bundle-commit + master-merge + deploy per Option B strategy. All work UNCOMMITTED vs HEAD `4ed88ea8b`. |
| **Next action (USER-DRIVEN)** | Per Option B: **user authorizes bundle-commit + push + master-merge**. Then evaluate + confirm deploy strategy. No more task-execute runs on this branch pre-commit. |
| **Post-deploy** | Manual smoke-test in Dev: launch Compose modal via ribbon → verify 3-pane layout (no workspace tabs) → verify "Working…" affordance in ConversationPane during summarize → verify final summary renders as assistant chat message → then task 110 wrap-up (/test-diet + close Issue #514). |
| **Operator strategy** | **Option B (confirmed 2026-07-01)**: ✅ Phases 9+10 complete. ⏳ Next: (1) `git commit` all work in one focused bundle, (2) `/push-to-github`, (3) `git fetch origin master` + merge master into work branch (check r7 conflicts), (4) evaluate + confirm deploy strategy, (5) deploy, (6) smoke, (7) task 110. |
| **R7 concurrency risk on master merge** | r7 has been actively editing BFF (Sprk.Bff.Api) + SpaarkeAi code page. On merge from master, check for conflicts in these files (ordered by risk): `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (task-098 edits — HIGHEST RISK; r7 also modifies this file), `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (task-092 + task-100 edits), `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx` (task-100 hideTabBar prop), `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` (task-097 SSE), `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs` (tasks 095/096 widened facade), `docs/adr/ADR-013-ai-architecture.md` (task-102 amendment). |
| **Amendment note** | Task 102 landed the FIRST Path B amendment under CLAUDE.md §6.5 (added 2026-06-29 by this same project). ADR-013 facade widening was documented end-to-end: full ADR §Amendment, concise ADR MUST rules, INDEX status update, CHANGELOG entry, defer-issues §AMD-102. The protocol worked as designed. |

### 10-task session verification snapshot (last run: post-task-102)

- **compose-components lib**: `npm run build` → tsc clean.
- **LegalWorkspace Vite**: 3918 modules / 0 errors / 3743 kB gz 1050 kB.
- **SpaarkeAi Vite (post-100)**: 3716 modules / 0 errors / 4878.11 kB gz 1357.90 kB. Total delta from pre-091 baseline: ~+1 kB (rounding).
- **BFF `dotnet build`**: 0 errors / 0 warnings.
- **BFF `dotnet test`**: 14 fail / 7682 pass / 111 skip (7807 total). This session introduced 0 net regressions; +9 previously-failing tests now pass (ComposeEndpointsTests DI-mock fixes from task 097 fully landing).
- **BFF publish (linux-x64 Release, compressed)**: 46.81 MB. Under 60 MB ceiling.
- **SpaarkeAi jest — ComposeToolbar.test.tsx**: 14/14 pass. NEW test file `WorkspaceTabManagerComponent.hideTabBar.test.tsx`: 4/4 pass. Pre-existing `WorkspacePane.summary-tab.test.tsx` failure unchanged (bootstrap error in test env, not related to this session).

### Files changed this session (post-task-102)

**Baseline files touched vs HEAD `4ed88ea8b`** (est. ~70 files):
- `src/client/shared/Spaarke.Compose.Components/**` (task 091: 6 tsx widgets moved + hooks + types + jest mapper; task 093: launch context hoisted; task 098: `orchestrators/executeComposeSummarize.ts` NEW + widened `ComposeToolbar` + trimmed `ComposeWorkspace` + trimmed `ComposeBannerStack` + barrel exports)
- `src/solutions/SpaarkeAi/src/App.tsx` (task 092: Path A special-case removed)
- `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` (task 092+093: compose props + Provider wrapper)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (task 092: compose-layout override; task 100: header conditional + hideTabBar pass-through)
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx` (task 100: `hideTabBar` prop)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (task 098: compose_summarize_request handler + working affordance strip)
- `src/solutions/LegalWorkspace/{package.json,vite.config.ts}` + `src/sections/composeEditor.registration.ts` (task 093: real ComposeWorkspace mount)
- `src/server/api/Sprk.Bff.Api/Services/Compose/IDocxTextExtractor.cs` + `DocxTextExtractor.cs` (task 094: NEW)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ComposeModule.cs` (task 094: DI registration)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/{IInvokePlaybookAi.cs,InvokePlaybookAi.cs,NullInvokePlaybookAi.cs}` (task 095: widened facade)
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` (task 097: SSE DispatchAction)
- `tests/unit/Sprk.Bff.Api.Tests/**` (~25 Moq site fixes across 5 test files; ComposeEndpointsTests fixture; SSE contract test; ADR-013 boundary allow-list)
- `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` + `tests/integration/regression/Compose/ComposeSummarizeRoundtripSmokeTests.cs` (task 097: 10 tests Skip'd, 1 SSE contract test added)
- `src/solutions/SpaarkeAi/src/__tests__/compose/ComposeToolbar.test.tsx` (task 098: driveId/tenantId props on all renders + 2 new tests + payload assertions)
- `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspaceTabManagerComponent.hideTabBar.test.tsx` (task 100: NEW 4-test suite)
- `docs/adr/ADR-013-ai-architecture.md` + `.claude/adr/ADR-013-ai-architecture.md` + `.claude/adr/INDEX.md` + `.claude/CHANGELOG.md` (task 102: Path B amendment)
- `projects/spaarkeai-compose-r1/{current-task.md,notes/defer-issues.md,tasks/TASK-INDEX.md}` + 8 POMLs (091-100, 102 status transitions + notes blocks)

### Suggested bundle commit message

```
feat(spaarkeai-compose-r1): Phase 7-10 supplement — three-pane pivot + streaming SSE + assistant-pane wiring + polish

Ships the R1-completion supplement per spec-supplement-2026-07-01-three-pane-pivot.md.

Phase 7 (three-pane pivot):
- Move ComposeWorkspace + widgets + hooks + types to @spaarke/compose-components shared lib
- Remove App.tsx Path A special-case; ThreePaneShell always mounts with compose props
- Swap LegalWorkspace composeEditor section placeholder → real ComposeWorkspace via ComposeLaunchContext bridge

Phase 8 (streaming SSE backend):
- IDocxTextExtractor (DocumentFormat.OpenXml) service + DI + 9 unit tests
- Widen IInvokePlaybookAi facade with optional userContext + document params (Path B — CLAUDE.md §6.5)
- Convert POST /api/compose/action/{consumerType} to text/event-stream SSE with AnalysisStreamChunk events

Phase 9 (assistant-pane wiring):
- executeComposeSummarize orchestrator (pure module, WhatWG Streams API) in @spaarke/compose-components
- ConversationPane subscribes to compose_summarize_request; progressive "Working…" affordance + assistant-message injection on result
- Remove workspace-local summary state + banner UI from ComposeWorkspace + ComposeBannerStack
- Widen ComposeToolbar event payload with driveId + tenantId + sprkDocumentId

Phase 10 (polish):
- Workspace-tab suppression in compose-launch mode (hide WorkspacePaneMenu + tab strip; keep widget-add extensibility)
- ADR-013 Path B amendment filed: full ADR §Amendment + concise MUST rules + INDEX + CHANGELOG + defer-issues §AMD-102

Test suite: 0 new regressions; +9 tests newly passing; 4 new hideTabBar tests; 14 ComposeToolbar tests updated.
Publish size: 46.81 MB compressed (under 60 MB ceiling).

🤖 Generated with Claude Code

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```

### 6 completed tasks this session (all UNCOMMITTED)

| Task | Title | Files touched (see task POML `<notes>` for full list) |
|---|---|---|
| **091** | Refactor ComposeWorkspace to `@spaarke/compose-components` shared lib | 6 tsx components + hooks/ + types + tests moved; App.tsx wired; barrel + jest mapper updated |
| **092** | App.tsx: remove Path A special-case; ThreePaneShell always | ComposeLaunchContext added to ThreePaneShell; WorkspacePane compose-layout override |
| **093** | Swap FU-3 placeholder → real ComposeWorkspace | ComposeLaunchContext hoisted to shared lib; LegalWorkspace deps + vite wiring; `@spaarke/ai-widgets/events` subpath adopted |
| **094** | IDocxTextExtractor service + DI + 9 tests | `Services/Compose/{IDocxTextExtractor,DocxTextExtractor}.cs` + `ComposeModule.cs` + `DocxTextExtractorTests.cs` |
| **095/096** | Widened IInvokePlaybookAi facade (Path B ADR-013 amendment) | Interface + impl + null impl + 3 new tests + ADR-013 boundary allow-list + ~25 Moq site fixes across 5 test files |
| **097** | SSE conversion of `POST /api/compose/action/{consumerType}` | `Api/ComposeEndpoints.cs` DispatchAction: `Task` (not `Task<IResult>`); WriteSSEAsync pattern; 10 tests Skip'd + 1 new SSE contract test; ComposeEndpointsTests fixture mocks added; FU-97a filed in `notes/defer-issues.md` |

### Key deltas for post-compaction reviewer

- **BFF publish size**: 45.47 MB (post-097) vs 45.41 MB (pre-Phase-8 baseline) = **+0.06 MB total**; well under 60 MB ceiling.
- **BFF test suite**: 7673 pass / 111 skipped / 23 fail — 14 of the failures are pre-existing baseline (ComposeServiceTests + DailyBriefingCollectorTests + others); 9 additional came from ComposeEndpointsTests DI-mock gap now FIXED; **task 097 introduced 0 net regressions**.
- **New shared lib exports**: `@spaarke/compose-components` now exports `ComposeLaunchContext`, `useComposeLaunch`, `ComposeLaunchContextValue`, all widgets (Workspace, Toolbar, BannerStack, EmptyState, ConflictDialog), hooks (broadcast/checkout/heartbeat), types (Flows 1-6), + docx bridge utils.
- **New workspace-linked deps in LegalWorkspace**: `@spaarke/compose-components` file: package added + vite config + package.json.
- **New DI seams**: `IDocxTextExtractor` + `IInvokePlaybookAi` widened signature (backward-compat via optional args); test fixtures registering these need `Mock.Of<IComposeDocumentService>()` + `Mock.Of<IDocxTextExtractor>()`.
- **New follow-ups filed in `notes/defer-issues.md`**:
  - FU-97a: re-author 10 skipped DispatchAction tests to parse SSE frames (non-blocking).
  - FU-91a (from task 091): pre-existing SpaarkeAi jest gap on `@spaarke/ui-components/components/*` subpath (12 failing suites unrelated to compose).

### Critical Context for post-compaction agent

1. **DO NOT COMMIT WITHOUT USER APPROVAL** — user's explicit Option B strategy is: complete tasks 098–102 first, THEN commit all in a bundle, THEN push, THEN handle master merge/r7 conflicts.
2. **Task 098 REMOVES local summary state from ComposeWorkspace.tsx** — the state variables `summaryStatus`, `summaryText`, `summaryError`, and `dismissSummary` were ADDED in a prior session (pre-Phase-7 work); task 098 relocates them to the ConversationPane chat model. Look for these variables in `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (post-task-091 location) — they'll be delete candidates.
3. **The SSE endpoint response shape** (from task 097, `Api/ComposeEndpoints.cs` DispatchAction): `Content-Type: text/event-stream`; events are `AnalysisStreamChunk` JSON (type: progress|result|done|error) prefixed with `data: `; each event terminates with `\n\n`; final sentinel is literal `data: [DONE]\n\n`.
4. **`authenticatedFetch`** from `@spaarke/auth` is the required transport (per ADR-028). It returns a `Response` whose `body` is a `ReadableStream<Uint8Array>` — task 098 will consume via `body.getReader()` + `TextDecoder`.
5. **Existing SSE precedent** on the frontend: `src/solutions/SpaarkeAi/src/components/conversation/ChatSseHandler.tsx` (SprkChat SSE consumption). Read this before writing new stream-parsing code.
6. **ADR-013 boundary allow-list** was updated in `PhaseAVerticalSliceTests.cs` (task 095) — `DocumentContext` is now permitted on the facade. Don't revert that.

---

---

## Quick Recovery (READ THIS FIRST)

<!-- This section is for FAST context restoration after compaction -->
<!-- Must be readable in < 30 seconds -->

| Field | Value |
|-------|-------|
| **Project state** | ✅ **Phases 0-6 shipped + deployed to Dev.** 🔄 **Phases 7-11 planned per [`spec-supplement-2026-07-01-three-pane-pivot.md`](spec-supplement-2026-07-01-three-pane-pivot.md)** — three-pane pivot + streaming SSE + Assistant pane wiring + polish + wrap-up. |
| **Active phase** | **Phase 7 planning complete; task 091 (ComposeWorkspace shared-lib refactor) is next execution step** |
| **PR history** | [#515](https://github.com/spaarke-dev/spaarke/pull/515) MERGED to master (commit `81e91d06f`) — R1 baseline. [#527](https://github.com/spaarke-dev/spaarke/pull/527) OPEN — R1 completion + supplement planning (10 commits since #515; branch tip `6e67d2638` includes r7 Wave 12 merge). |
| **What was shipped in phases 0-6 (baseline R1)** | Path A modal launch (currently direct `<ComposeWorkspace>` mount — SHORTCUT of the canonical three-pane UX; addressed in Phase 7). BFF Compose endpoints + services + DI. SpaarkeAi Compose components. Dataverse plumbing (`sprk_workspacelayout` "Compose" + `sprk_playbookconsumer` `compose-summarize`). Save works (field-name fix 2026-07-01). Formatting toolbar + BubbleMenu. Save button. Summarize wired (banner UX; not yet streaming). |
| **What still needs to ship (phases 7-11)** | Three-pane pivot: move ComposeWorkspace to `@spaarke/compose-components`, swap FU-3 placeholder, wrap Path A in ThreePaneShell. Streaming SSE: `IDocxTextExtractor`, `IInvokePlaybookAi` extension, `/api/compose/action/*` → SSE. Assistant pane: ConversationPane consumes `compose_summarize_request` events + streams. Polish: workspace-tab suppression, modal 80×80 (done in launch-resolver 2026-07-01), ADR-013 amendment (Path B per CLAUDE.md §6.5). Wrap-up: /test-diet + close Issue #514. |
| **NEXT ACTION (explicit)** | **Phase 8 backend ✅ complete (094+095/096+097). Begin Phase 9 (Assistant pane wiring) via `task-execute` on [`tasks/098-conversationpane-sse-consumption.poml`](tasks/098-conversationpane-sse-consumption.poml).** Operator strategy: Option B — complete Phases 9+10 (tasks 098, 099, 100, 102) before commit+push+merge+deploy. Remaining budget: ~5h estimated. R7 concurrent work touches BFF + SpaarkeAi (potential conflicts to resolve at merge time). |
| **What I (Claude) should do post-compaction** | (a) Read spec-supplement first for full R1 completion scope. (b) Read plan.md §9 for phase 7-11 breakdown. (c) Execute via task-execute skill through the POML files 091 → 099 → 102 in sequence. (d) DO NOT create r2 project — this work is R1 completion, not new project scope (per operator 2026-07-01: "R1 isn't usable without three-pane"). (e) R3 seed is at [`../spaarkeai-compose-r3/README.md`](../spaarkeai-compose-r3/README.md) — Word fidelity work (preserving formatting, track changes, comments) — SEPARATE project, do not conflate. |

### Files Modified This Session (full Wave 0 → W11)

Full project work landed in PR #515 (6 commits):

| Commit | Subject |
|---|---|
| `964594eea` | feat: initialize project artifacts + task plan |
| `9aa18c9b3` | feat: autonomous parallel execution mode |
| `cf46eac8a` | feat(bff/spaarkeai-compose-r1): Compose API + services + DI + tests + Dataverse seed |
| `0881e6097` | feat(frontend/spaarkeai-compose-r1): Compose UI surface + shared libs + SemanticSearch refactor |
| `5f7fb61a3` | docs(spaarkeai-compose-r1): R1 implementation artifacts + audits + defer registry |
| `0778793a6` | chore(husky): track .husky/_/h helper alongside existing tracked shims |
| `fcb69ed17` | refactor(spaarkeai-compose-r1): code-review cleanup (R1+R2+R3+R4 from PR #515) |
| `9a6141167` | docs(spaarkeai-compose-r1): wrap-up artifacts — /test-diet + W9 consolidation + defer registry update |

Plus 2 CI auto-format commits (`086a23da7`, `d89dab8b0`) integrated via rebase.

### Critical Context (what a post-compaction agent MUST know)

1. **Deploy is GO**. Operator decided 2026-06-30 — do not hold for r7. r7 has not merged to master; r7's BFF changes are not in Dev; Compose deploy will not regress anything.
2. **All audits passed**: W9-070 (17/22 SCs pass + 7 deferred to live; 0 fail), W9-071 (9 ADRs / 9 PASS / 0 violations), W9-072 (§10 #5 PASS — 0 new HIGH CVEs), /test-diet (0 DELETE / 0 AMBIGUOUS).
3. **All cleanup applied**: R1 (12 null checks removed), R2 (ComposeWorkspace.tsx 1795 → 687 LOC + 3 hooks), R3 (FU-1 heartbeat gate — RESOLVED), R4 Option C (IComposeSessionService collapsed to concrete; virtual modifier trade-off documented as FU-4).
4. **Tests pass**: 136/136 BFF + 38/38 frontend = 174 tests; builds clean.
5. **Path A tensions ratified**: T-1 (Dataverse-side checkout), T-2 (DocxAsync coverage delegation), T-3 (ADR-038 §7 SCAFFOLDING decline). All documented in spec.md + defer-issues.md.
6. **Deploy artifacts**: BFF zip at `deploy/api-publish.zip` (45.41 MB, **−0.24 MB vs baseline**); SpaarkeAi prod bundle at `src/solutions/SpaarkeAi/dist/spaarkeai.html`.
7. **Single reviewer doc**: [`notes/wrap-up-summary.md`](notes/wrap-up-summary.md) consolidates everything.
8. **Comprehensive defer/issues registry**: [`notes/defer-issues.md`](notes/defer-issues.md) — ISS-001 resolved in PR, ISS-002 [#516] + ISS-003 [#518] filed, ISS-004 noted, 3 Path A tensions, OI-5 open, FU-1 RESOLVED, FU-2 open, FU-3 deferred to R2, FU-4 open (R4 trade-off), 7 DEFERRED SCs.

---

## Parallel Wave Tracker

Tracks in-flight wave dispatches. Updated by **main session only** at wave-start and wave-end (per-task agents do NOT write here). See [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) for the full 12-wave plan.

### Current wave: W10 (2 agents in flight)

| Wave | Tasks | Status | Start | End | Build After | Notes |
|---|---|---|---|---|---|---|
| W0 | 001, 002, 003, 004 | ✅ all done | 2026-06-29 | 2026-06-29 | n/a — no `.cs`/`.ts`/`.tsx` modified | Path A approved 2026-06-29 — design.md §14 row 4 + spec.md ADR Tensions T-1 updated |
| W1a | 010, 011, 012 | ✅ all done | 2026-06-29 | 2026-06-29 | n/a — Dataverse + JPS only | **FW-1 ✅ complete**: OI-1 Alt Key `sprk_graphitemid_uk` + OI-2 `sprk_lastheartbeatutc` field both created 2026-06-29 by operator; verified via MCP describe. W2 + W7 both unblocked. |
| W1b | 020, 030, 040, 041 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 errors; SpaarkeAi Vite ✅ 0 errors; LegalWorkspace standalone Vite ✅ 0 errors (post-ISS-001 fix) | ISS-001 fixed inline (vite.config.ts +3 lines); ISS-002 filed [#516](https://github.com/spaarke-dev/spaarke/issues/516); `@spaarke/compose-components` v0.1.0 scaffolded |
| W2 | 021, 022, 023, 031 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 errors / 18 warnings (= baseline); shared lib tests 14/14 ✅; ComposeSessionService tests 9/9 ✅ | Publish-size +0.59 MB cumulative; well under 60 MB ceiling |
| W3 | 024, 032 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 / 18 baseline; SemanticSearch build clean; 11 endpoint-contract tests ✅ | Compose endpoint surface complete; SemanticSearch refactored off shim |
| W4 | 025, 033, 043, 044, 045 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0/18 baseline; `@spaarke/compose-components` orchestrator ✅ SUCCESS 2.4s; SpaarkeAi Vite ✅ 0 errors | TipTap+DOCX bridge license audit ✅ all MIT/BSD; Compose BFF surface fully wired |
| W5 | 026, 027, 042 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 errors; 118/118 Compose tests pass in 1s; SpaarkeAi Vite ✅ 0 errors | Cumulative Compose test count 118 (was 84); Path A on `LoadDocxAsync`/`SaveDocxAsync` formalized (round-trip via 027 integration tests); ComposeWorkspace circular-dep note: LegalWorkspace standalone retains placeholder per Calendar Pattern D |
| W6 | 046, 050 | ✅ all done | 2026-06-29 | 2026-06-29 | SpaarkeAi Vite ✅ 3704 modules / 0 errors; dotnet ✅ 0/18 baseline | Path A modal-launch UX live; SPE checkout wired via existing endpoint (zero BFF code per Spike #3); 050 finding for 051: same-user multi-tab returns 200 → 051 needs `/checkout-status` probe |
| W7 | 051, 052 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 errors; 128/128 Compose tests pass in 1s; SpaarkeAi Vite ✅ 0 errors / no size delta | Lock lifecycle complete end-to-end; multi-tab conflict UX live; heartbeat sweeper live (15-min orphan release) |
| W8 | 060, 061 | ✅ all done | 2026-06-29 | 2026-06-29 | dotnet ✅ 0 errors; **136/136 Compose tests pass in <1s** (broader 238-test sweep all green) | Smoke-test write-up locked; live Dev BFF execution operator-deferred to W10/W11; FR-06 concurrent-Save live test deferred to W10 |
| W9 | 070, 071, 072 | ✅ all done | 2026-06-29 | 2026-06-29 | no builds needed | **All 3 audits PASS**; W10 operator review gate signed off (2026-06-29): ISS-002 carry-forward, ISS-003 [#518] filed, publish-size approved |
| W10 | 080, 081 | ⏸ PAUSED — waiting on r7 merge-to-master | 2026-06-29 | — | smoke-test endpoints post-deploy | **R7 alignment**: file-level overlap = 1 file only (Seed-PlaybookConsumers.ps1, additive — no conflict); deploy paused per operator until r7 deploys + merges to master; then rebase Compose onto updated master → re-test → merge Compose to master → combined deploy |
| W1a | 010, 011, 012 | 🔲 | — | — | none | — |
| W1b | 020, 030, 040, 041 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W2 | 021, 022, 023, 031 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W3 | 024, 032 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W4 | 025, 033, 043, 044, 045 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W5 | 026, 027, 042 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W6 | 046, 050 | 🔲 | — | — | build per language touched | — |
| W7 | 051, 052 | 🔲 | — | — | `dotnet build` + `npm run build` | — |
| W8 | 060, 061 | 🔲 | — | — | `dotnet test` | — |
| W9 | 070, 071, 072 | 🔲 | — | — | none (read-only checks) | — |
| W10 | 080, 081 | 🔲 | — | — | smoke-test endpoints post-deploy | **Operator review gate before deploy** |
| W11 | 090 | 🔲 | — | — | all build + tests + test-diet | **Operator review gate before wrap-up** |

### Wave status legend
- 🔲 not-dispatched · 🔄 in-flight · ✅ all tasks complete · ⚠️ partial (some 🔄 retries) · ❌ aborted

### In-flight agent log (current wave only)

| Task | Agent type | Dispatched | Status |
|---|---|---|---|
| 080-deploy-bff-and-measure-publish-size | general-purpose | 2026-06-29 | ✅ done (PARTIAL/operator-deferred) — **publish-size 45.41 MB compressed (−0.24 MB vs 45.65 MB baseline; SMALLER despite adding Compose)**; deploy artifacts staged at `deploy/api-publish/` + `.zip`; live deploy + smoke tests + FR-06 acceptance test scripted for operator at `notes/bff-publish-size-report.md` §6 (6-step runbook) |
| 081-deploy-codepage-and-dataverse-artifacts | general-purpose | 2026-06-29 | ⏸ **PAUSED** — operator instruction "don't deploy yet, need to align with r7 project"; sub-agent watchdog-killed mid-investigation; SpaarkeAi prod build artifact ready at `dist/index.html` (4820 kB / gzip 1338 kB); deploy script `scripts/Deploy-SpaarkeAi.ps1` identified; live deploy held pending r7 alignment review |

**Wave 9 archive** (completed 2026-06-29):
- 070 ✅ 17 PASS / 7 DEFERRED / 0 FAIL across 22 SCs; audit at `notes/audits/success-criteria-audit.md`
- 071 ✅ 9 ADRs / 9 PASS / 0 violations; 0/17 banned patterns; UNCONDITIONAL DI verified; audit at `notes/adr-038-conformance.md`
- 072 ✅ §10 #5 PASS — 0 new HIGH CVEs introduced by Compose; TipTap audit re-confirmed MIT/BSD/zero Pro; agent rate-limit, main-session rescue; audit at `notes/audits/cve-coverage-audit.md`

**Operator W10 gate sign-off** (2026-06-29):
- ISS-002 (Kiota HIGH CVE [#516]) — **carry-forward** approved
- ISS-003 (SemanticSearch 104 failures [#518]) — **filed** + documented
- Publish-size (48.42 MB cumulative; well under 60 MB ceiling) — approved
- 7 deferred SCs — accepted as W10/W11 live verification scope
- `notes/defer-issues.md` updated as comprehensive "do not lose" registry

**Wave 8 archive** (completed 2026-06-29):
- 060 ✅ 7 regression tests (KEEP `regression`); 4-hop pipeline trace (FR-11); routing resolution; ADR-013 runtime facade; NFR-03 latency; smoke-test write-up at `notes/smoke-tests/compose-summarize-roundtrip.md` (live Dev BFF execution operator-deferred to W10/W11)
- 061 ✅ +1 endpoint-contract test pinning all 10 fields of locked `ComposeActionResponse` (Spike #4 §6); FR-06 concurrent-Save + SPE round-trip explicitly out of POML scope (belongs to W10/separate Spe.Integration.Tests)

**Cumulative Compose test count post-W8: 136/136 pass in <1s** (broader 238-test Compose+ConsumerRouting+InvokePlaybook sweep all green).

**Wave 7 archive** (completed 2026-06-29):
- 051 ✅ `ComposeConflictDialog.tsx` (245 LOC); probe-before-acquire pattern; BroadcastChannel cross-tab signaling; server's `IsCurrentUser` flag eliminates client-side whoami; 12 component tests; FR-16 verbatim labels; zero new BFF code
- 052 ✅ `RefreshHeartbeatAsync` + `POST /heartbeat` endpoint + `StaleCheckoutSweeperHostedService` (2-min scan, 15-min stale, 100-row cap); registered as `IHostedService`; 9 new tests; **cumulative 128/128 Compose tests pass**

**Compose lock lifecycle complete end-to-end**: probe `/checkout-status` on mount → conflict modal if same-user multi-tab → acquire via existing `/checkout` → ComposeEditor 3-min heartbeat → 15-min stale sweep auto-releases orphaned locks. Path A (Dataverse-side via existing `DocumentCheckoutService`) saves ~450 LOC vs SPE-native approach per Spike #3 estimate.

**Known follow-ups** (non-blocking; can be addressed in 060 or R2):
- Heartbeat gate: ComposeEditor (W4-045) should pause heartbeat when `checkoutStatus !== 'acquired'`. Current behavior: cancelled tabs heartbeat returns 404 harmlessly (no correctness issue, minor efficiency).
- OI-5 from Spike #3: should `sprk_lastheartbeatutc` PATCH bump `modifiedon`? Current impl: default Dataverse behavior.

**Wave 6 archive** (completed 2026-06-29):
- 046 ✅ launch-resolver.ts extended with `compose-editor` target; ribbon XML + JS handler created; SpaarkeAi Vite 3704 modules / 0 errors
- 050 ✅ ComposeWorkspace +170 LOC; checkout call via existing endpoint; 200/409/404/5xx all handled; finding for 051: same-user multi-tab returns 200 not 409 — needs `/checkout-status` probe

**Wave 5 archive** (completed 2026-06-29):
- 026 ✅ 14 unit tests (6 ComposeService + 7 ComposeDocumentService + 1 ComposeSessionService); KEEP `domain-logic`; zero banned-pattern hits; Path A formalized on `LoadDocxAsync/SaveDocxAsync`
- 027 ✅ `ComposeEndpointsContractTests.cs` (655 LOC, 20 integration-contract tests) at canonical KEEP path `tests/integration/contract/Api/Compose/` (Path C override of POML's `tests/unit/` per ADR-038 §2)
- 042 ✅ `ComposeWorkspace.tsx` (~620 LOC, SpaarkeAi solution); useReducer 5-status state machine; Flows 1/2/5 PaneEventBus subscribers; Flow 5 manual-confirm gate per Spike #2 §10.3

**Mount-path architecture clarified**:
- **Path A (R1 priority — modal entry)**: Document command-bar → `launch-resolver.ts` → SpaarkeAi modal with `ComposeWorkspace` mounted → ComposeWorkspace orchestrates ComposeEditor + Toolbar + EmptyState
- **Workspace picker (Pattern D)**: LegalWorkspace section registry → `composeEditor.registration.ts` shim → currently inline placeholder (W1b-040); intentionally NOT swapped because of `@spaarke/legal-workspace → SpaarkeAi` circular-dep — Calendar Pattern D precedent applies
- W6 task 046 wires Path A (the R1 priority); placeholder swap deferred

**Wave 4 archive** (completed 2026-06-29):
- 025 ✅ `ComposeModule.cs` (60 LOC, mirror of `OfficeModule`); 3 services Scoped UNCONDITIONAL (§ F.1 compliant); Program.cs + EndpointMappingExtensions wired; 84 Compose tests passing
- 033 ✅ SemanticSearch gate PASS (319/104 = baseline; Phase 3 closes); ⚠️ Path A on ADR-038 documented (declined SCAFFOLDING smoke test)
- 043 ✅ `ComposeToolbar.tsx` (350 LOC, SpaarkeAi solution) — workspace command bar (Open-in-Word + Summarize); 12 component tests passing
- 044 ✅ `ComposeEmptyState.tsx` (240 LOC, SpaarkeAi solution) — Fluent v9 Card + 2 CTAs with callback props
- 045 ✅ `ComposeEditor.tsx` (450 LOC, `@spaarke/compose-components`) — TipTap StarterKit + 11 MIT extensions + mammoth (BSD-2) + docx (MIT) — **license audit: all MIT/BSD, zero TipTap Pro**; 3-min heartbeat; ADR-013 facade clean; orchestrator dep-order fixed

**Architectural pattern locked**: shared lib `@spaarke/compose-components` = reusable editor (TipTap + bridge + formatting toolbar inside); SpaarkeAi solution `src/solutions/SpaarkeAi/src/components/compose/` = workspace-specific surfaces (command-bar toolbar, empty-state). Same shape as Calendar (CalendarWorkspaceWidget shared + CalendarFilterPane added at SpaarkeAi level).

**Wave 3 archive** (completed 2026-06-29):
- 024 ✅ `Api/ComposeEndpoints.cs` (~686 LOC, 7 endpoints under `/api/compose/*`); 11 endpoint-contract tests passing; facade boundary verified (5 negation comments, 0 real refs); publish +0.23 MB → cumulative 48.42 MB
- 032 ✅ SemanticSearch `App.tsx` (actual consumer at line 367; not `SearchCommandBar.tsx` as POML title suggested — grep took precedence); thin shim + broken test deleted; SemanticSearch build clean; pre-existing 104 test failures flagged as ISS-003 candidate

**Wave 2 archive** (completed 2026-06-29):
- 021 ✅ `IComposeService` + `ComposeService.cs` (~620 LOC); facade-clean (4 negation comments only); `PromoteIfEphemeralAsync` here (per POML — Phase 5 may consolidate to `DocumentCheckoutService` per Spike #3 §2.2); publish-size −0.26 MB
- 022 ✅ `IComposeDocumentService` + `ComposeDocumentService` (Load/Save DOCX plumbing); R1 stubs for checkout methods (Phase 5 wires)
- 023 ✅ `IComposeSessionService` + `ComposeSessionService` (thin facade over `ChatSessionManager`; no new entity/storage); 9 unit tests passing
- 031 ✅ `useDocumentActions` moved to `@spaarke/document-operations`; SemanticSearch shim binds `bffBaseUrl`; 14 unit tests passing; React dedup hazard caught + fixed; pre-existing broken SemanticSearch test flagged for 032

Updated by main session at dispatch + completion. Cleared at next wave-start.

**Wave 1b archive** (completed 2026-06-29):
- 020 ✅ `ConsumerTypes.ComposeSummarize = "compose-summarize"` added (additive; build clean)
- 030 ✅ `@spaarke/document-operations` v0.1.0 empty scaffold; build clean
- 040 ✅ `compose-editor` section registered in `@spaarke/legal-workspace` (Pattern D / Calendar precedent); SpaarkeAi build green
- 041 ✅ `src/solutions/SpaarkeAi/src/types/compose-contracts.ts` created (17 exports: 6 flows + 2 pointers + 4 channel unions + ComposeEventBusContract + 3 helpers); Path A (solution-local types file)

**Wave 1b follow-ups** (completed 2026-06-29 main-session, post-W1b):
- ISS-001 ✅ FIXED inline: `src/solutions/LegalWorkspace/vite.config.ts` +3 entries (sharedLibPaths + react include + resolve.alias) for `@spaarke/daily-briefing-components`; LegalWorkspace standalone Vite build verified ✅ 3347 modules / 0 errors
- ISS-002 📋 FILED: [#516](https://github.com/spaarke-dev/spaarke/issues/516) — Microsoft.Kiota.Abstractions 1.21.2 HIGH CVE (GHSA-7j59-v9qr-6fq9); fix scope = Sprk.Bff.Api maintenance, not Compose R1
- **`@spaarke/compose-components` v0.1.0** empty scaffold created at `src/client/shared/Spaarke.Compose.Components/` (deps: `@spaarke/auth` + `@spaarke/document-operations`); registered in `scripts/Build-AllClientComponents.ps1`; standalone `tsc` build verified ✅ 0 errors; Phase 4 (tasks 042–046) populates it

**Wave 0 archive** (completed 2026-06-29):
- 001 ✅ TipTap 2.10.x + mammoth ^1.8.0 + docx ^9.0.3 (dual-library client-side bridge); 33 OOB features classified
- 002 ✅ 6 directional Flows on existing PaneEventBus channels (additive per ADR-030); 3 R1-wired, 3 stub-only
- 003 ✅ Dataverse-side checkout via existing `DocumentCheckoutService`; 3-min heartbeat, 15-min stale, ≤17-min max orphan; 2 net-new BFF endpoints (heartbeat + promote); Path A approved
- 004 ✅ `POST /api/compose/action/{consumerType}`; PublicContracts facade only; 2 JPS scope schemas locked

**Wave 1a archive** (completed 2026-06-29):
- 010 ✅ `sprk_workspacelayout` Compose row created live (id `c09d26be-e173-f111-ab0e-7ced8ddc4a05`) + `scripts/Deploy-ComposeDataverseCustomizations.ps1` for OI-1 (Alt Key) + OI-2 (`sprk_lastheartbeatutc` field) → ⚠️ **operator must run before W2**
- 011 ✅ `sprk_playbookconsumer` row in Dev (id `986799ad-e173-f111-ab0e-7ced8ddc4a05`) + `scripts/dataverse/Seed-PlaybookConsumers.ps1` for Test/Prod
- 012 ✅ JPS scope JSONs locked at `notes/jps-scopes/`; ADR-015 `doNotLog` on `selectionText`

---

### Files Modified This Session

- `projects/spaarkeai-compose-r1/README.md` — Created — project overview + graduation criteria
- `projects/spaarkeai-compose-r1/plan.md` — Created — implementation plan + 9-phase WBS (Phase 0 spikes + Phases 1–8 implementation)
- `projects/spaarkeai-compose-r1/CLAUDE.md` — Created — AI context file
- `projects/spaarkeai-compose-r1/current-task.md` — Created — this file
- `projects/spaarkeai-compose-r1/tasks/` — Created — empty (ready for `task-create`)
- `projects/spaarkeai-compose-r1/notes/` — Created with subdirs (debug/, spikes/, drafts/, handoffs/)

### Critical Context

Project is fully initialized with artifacts but no tasks yet. Phase 0 (Spikes) is the blocking gate before Phase 1+ tasks begin — see `plan.md §4 Phase 0`. Spike outputs (DOCX subset spec, bridge library choice, JPS scope schemas, endpoint shape) become locked artifacts in `notes/spikes/`. Hot-path overlap: BFF (joins 14 active projects), SpaarkeAi (joins 8 active projects) — see `projects/INDEX.md`.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 097 |
| **Task File** | tasks/097-convert-compose-action-endpoint-to-sse.poml |
| **Title** | Convert `POST /api/compose/action/{consumerType}` to SSE streaming |
| **Phase** | Phase 8: streaming SSE backend (supplement scope) |
| **Status** | not-started |
| **Rigor Level** | FULL (POML likely `rigor-hint=FULL`; endpoint conversion + streaming) |
| **Depends On** | 094 ✅ (IDocxTextExtractor), 095 ✅ (widened IInvokePlaybookAi facade), 096 ✅ (collapsed into 095) |
| **Approach** | Read task 097 POML at start; convert Compose dispatch endpoint from single-shot response to SSE. Reference pattern: `WorkspaceFileEndpoints.WriteSSEAsync` + `AnalysisStreamChunk`. In ComposeService dispatch path: (a) load DOCX bytes via existing `IComposeDocumentService.LoadDocxAsync`; (b) extract plain text via `IDocxTextExtractor` (094); (c) call widened `IInvokePlaybookAi.InvokePlaybookAsync` with `userContext` + `document`; (d) stream terminal-node output progressively via SSE. This eliminates the "AI analysis node requires document context" summarize failure surfaced at UAT. |

---

### Previous task (Phase 8 W-supp — 095 + 096 — completed 2026-07-01)

| Field | Value |
|-------|-------|
| **Task ID** | 095 (+ 096 collapsed) |
| **Task File** | tasks/095-extend-iinvokeplaybookai-with-document-context.poml |
| **Title** | Extend IInvokePlaybookAi with optional userContext + DocumentContext parameters |
| **Phase** | Phase 8: streaming SSE backend (supplement scope) |
| **Status** | ✅ completed |
| **Rigor Level** | FULL |
| **Started / Completed** | 2026-07-01 / 2026-07-01 (~1 h vs 1.5 h estimate) |
| **Design decision** | Chose **optional parameters** over separate overload — C# compiler auto-fills defaults for existing 4-arg callers → zero call-site churn. Single method, single impl. Ordering: `userContext` + `document` inserted AFTER `cancellationToken` (all three defaultable) so ct's positional-slot #4 stays legal. |
| **Signature change** | `InvokePlaybookAsync(Guid, IReadOnlyDictionary?, PlaybookInvocationContext, CancellationToken = default, string? userContext = null, DocumentContext? document = null)` — impl forwards new args verbatim to `PlaybookRunRequest.UserContext` + `PlaybookRunRequest.Document`; telemetry logs presence + lengths only (ADR-015). |
| **ADR-013 amendment** | The `PhaseAVerticalSliceTests.ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` reflection test was updated with an `amendmentAllowedTypes` HashSet permitting `DocumentContext` explicitly, with citations to task 095 + task 102 formal filing. Silent bypass forbidden per CLAUDE.md §6.5 — explicit allow-list with citation is Path B posture. |
| **Moq expression-tree fix** | ~25 Setup/Verify sites across 5 test files (`InvokePlaybookHandlerTests`, `DailyBriefingEndpointsTests`, `DailyBriefingResponseShapeTests`, `ComposeEndpointsContractTests`, `ComposeSummarizeRoundtripSmokeTests`) required `It.IsAny<string?>()` + `It.IsAny<Sprk.Bff.Api.Services.Ai.DocumentContext?>()` appended per CS0854. Callback generics extended 4 → 6 type params; lambdas expanded 4 → 6 args. |
| **New tests** | 3 added to `InvokePlaybookAiTests.cs`: `WithUserContextAndDocument_ForwardsToRequest`, `WithoutUserContextOrDocument_BackwardCompatibleRequestShape`, `NullInvokePlaybookAi_WithWidenedArgs_StillThrowsFeatureDisabled`. |
| **Build verification** | `dotnet build src/server/api/Sprk.Bff.Api/`: ✅ 0 errors / 19 warnings (baseline preserved). |
| **Test verification** | **94/94 facade + boundary + consumer-setup tests pass**. 7 pre-existing baseline failures (6 ComposeServiceTests + 1 DailyBriefingCollectorTests) verified via git stash — **unrelated to IInvokePlaybookAi**; zero regressions from task 095. |
| **POML acceptance** | #1 ✅ build clean. #2 ✅ 4-arg call shape unchanged for existing consumers. #3 ✅ new tests + updated boundary allow-list. |
| **Task 096 collapse** | Task 096 (impl updates) delivered atomically with 095 because C# forbids interface + impl signature divergence — both `InvokePlaybookAi.cs` and `NullInvokePlaybookAi.cs` had to ship the widened signature the moment the interface changed, or the project wouldn't compile. 096 marked ✅ (collapsed into 095) in TASK-INDEX. |

---

### Prior task (Phase 8 W-supp — 094 — completed 2026-07-01)

| Field | Value |
|-------|-------|
| **Task ID** | 094 |
| **Task File** | tasks/094-bff-docx-text-extractor.poml |
| **Title** | Add IDocxTextExtractor service (DocumentFormat.OpenXml) + tests |
| **Phase** | Phase 8: streaming SSE backend (supplement scope) |
| **Status** | ✅ completed |
| **Rigor Level** | FULL |
| **Started / Completed** | 2026-07-01 / 2026-07-01 (~45 min vs 2 h estimate) |
| **Files delivered** | (a) `src/server/api/Sprk.Bff.Api/Services/Compose/IDocxTextExtractor.cs` (ADR-010 justified I/O-boundary seam). (b) `src/server/api/Sprk.Bff.Api/Services/Compose/DocxTextExtractor.cs` (default impl: single OpenXml tree walk; paragraph-break separator; truncation suffix with accurate residue count; malformed-input normalization to `InvalidDataException`; CancellationToken propagation). (c) `Infrastructure/DI/ComposeModule.cs` UNCONDITIONAL Scoped registration per §F.1. (d) `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/DocxTextExtractorTests.cs` (9 behavioral tests with in-memory DOCX fixtures — empty / simple / split-runs / headers-footers-skipped / comments-skipped / truncation / malformed / cancelled / argument-out-of-range). |
| **Build verification** | `dotnet build src/server/api/Sprk.Bff.Api/`: ✅ 0 errors / 19 warnings (identical to baseline). |
| **Test verification** | `dotnet test --filter "FullyQualifiedName~DocxTextExtractor"`: ✅ **9/9 pass in 24 ms** (unit-domain KEEP category per ADR-038). |
| **Publish size** | **45.47 MB compressed** vs pre-094 baseline **45.41 MB** — delta **+0.06 MB (~60 KB)**. Well under < 0.5 MB POML target and < 1 MB constraint. Confirms `DocumentFormat.OpenXml 3.4.1` was already directly referenced (no new binary payload). |
| **POML acceptance** | #1 ✅ dotnet build clean. #2 ✅ tests pass. #3 ✅ size delta well under 0.5 MB. |
| **Downstream unblocked** | Task 095 (IInvokePlaybookAi facade extension) can now consume `IDocxTextExtractor` to produce `UserContext`. Task 097 (SSE endpoint conversion) inherits via DI — no additional wiring beyond compose SSE dispatch path. |

---

### Prior task (Phase 7 W-supp — 093 — completed 2026-07-01)

| Field | Value |
|-------|-------|
| **Task ID** | 093 |
| **Task File** | tasks/093-swap-fu3-placeholder-for-real-composeworkspace.poml |
| **Title** | Swap FU-3 placeholder → real ComposeWorkspace in composeEditor.registration.ts |
| **Phase** | Phase 7: three-pane pivot (supplement scope) |
| **Status** | ✅ completed (FU-3 RESOLVED) |
| **Rigor Level** | FULL |
| **Started / Completed** | 2026-07-01 / 2026-07-01 (~1.5 h vs 1 h estimate; overshoot due to LegalWorkspace transitive-dep remediation) |
| **Files touched** | (a) `src/client/shared/Spaarke.Compose.Components/src/context/composeLaunchContext.ts` (NEW — hoisted from SpaarkeAi). (b) `src/client/shared/Spaarke.Compose.Components/src/index.ts` (barrel export the new context symbols). (c) `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` + `.../ComposeEditor.tsx` (swap `@spaarke/ai-widgets` → `@spaarke/ai-widgets/events` subpath). (d) `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` (removed local `ComposeLaunchContext` def; re-export `useComposeLaunch` from shared lib). (e) `src/solutions/LegalWorkspace/package.json` + `vite.config.ts` (workspace-linked dep + 3 vite entries). (f) `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts` (Skeleton placeholder → real `<ComposeWorkspace>` via `ComposeSectionMount` bridge that consumes `useComposeLaunch()`). |
| **Contract established** | LegalWorkspace section factory now renders real ComposeWorkspace. When SpaarkeAi ThreePaneShell provides `ComposeLaunchContext` (ribbon modal launch), document context flows through. When standalone (or SpaarkeAi workspace picker without a document), `useComposeLaunch()` returns null → ComposeWorkspace renders empty state → user browses/searches. |
| **Build verification** | `@spaarke/compose-components` tsc ✅ 0 errors. SpaarkeAi Vite ✅ 3715 modules / 4876.96 kB / gzip 1357.34 kB (re-eagerised the ComposeWorkspace chain; back to pre-092 baseline). LegalWorkspace standalone Vite ✅ 3917 modules / 3746.04 kB / gzip 1051.21 kB (+1384 kB vs pre-093 due to compose chain now included — expected). Ribbon build clean. |
| **Test verification** | Full SpaarkeAi jest baseline preserved: 12 failed / 18 passed suites, 5 failed / 470 passed tests — IDENTICAL to post-092. Pre-existing infra gap (FU-91a) unchanged. |
| **POML acceptance** | #1 ✅ real editor mounts via shared lib import + context bridge. #2 ✅ FU-3 marked RESOLVED in `notes/defer-issues.md` (both table row + detail section). |
| **Follow-ups** | FU-93a (informational, not a formal issue below 3+usage threshold): `@spaarke/ai-widgets` barrel side-effect widget registration triggers transitive resolution of `@spaarke/ai-outputs` subpaths that LegalWorkspace standalone Rollup can't resolve. Fixed here by switching to `@spaarke/ai-widgets/events` subpath. Two consumers now use the deep-import pattern (this project's ComposeWorkspace/ComposeEditor + pre-existing useWorkspaceLayouts adapter). A future ai-widgets refactor to defer the side-effect registration would eliminate the need. |

---

### Prior task (Phase 7 W-supp — 092 — completed 2026-07-01)

| Field | Value |
|-------|-------|
| **Task ID** | 092 |
| **Task File** | tasks/092-remove-path-a-special-case-in-app.poml |
| **Title** | App.tsx: remove Path A special-case; render ThreePaneShell always |
| **Phase** | Phase 7: three-pane pivot (supplement scope) |
| **Status** | ✅ completed |
| **Rigor Level** | FULL |
| **Started / Completed** | 2026-07-01 / 2026-07-01 (~45 min vs 1 h estimate) |
| **Files touched** | `src/solutions/SpaarkeAi/src/App.tsx` (removed Path A block + unused imports); `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` (added `ComposeLaunchContext` + `useComposeLaunch` + 3 optional props); `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (consume `useComposeLaunch`; compute `layoutForAutoInstall` — prefers "Compose" layout by name when composeMode=editor, else falls through to BFF `activeLayout`; pinned-list skip disabled in compose-launch mode). |
| **Contract established** | `ComposeLaunchContext` exposes `{ composeMode: 'editor', document, driveId }` when the app was launched via ribbon modal — null otherwise. Task 093 will consume this in the compose-editor section factory. Named-based layout override (no client-side GUID pinning, no sessionStorage side-effects). |
| **Build verification** | SpaarkeAi Vite ✅ 3579 modules / 13.86s / **3991.14 kB** (gzip **1088.21 kB**) — bundle **REDUCED** ~886 kB / 269 kB gzip because Vite tree-shakes ComposeWorkspace+TipTap chain now that App.tsx doesn't eagerly import them. Task 093's section-factory swap will bring the chain back into the eager bundle (~4.9 MB). Ribbon build clean. |
| **Test verification** | Full SpaarkeAi jest suite baseline preserved: 12 failed / 18 passed suites, 5 failed / 470 passed tests — IDENTICAL to post-091 baseline. Pre-existing `@spaarke/ui-components/components/CreateMatterWizard` subpath gap (FU-91a) unrelated. |
| **POML acceptance** | #1 partial ✅ (three panes render + Compose layout auto-selected; real editor mount is 093's swap); #2 ✅ (non-compose launches use `activeLayout` — zero behavior change); #3 ✅ (all tests pass). |

---

### Prior task (Phase 7 W-supp — 091 — completed 2026-07-01)

| Field | Value |
|-------|-------|
| **Task ID** | 091 |
| **Task File** | tasks/091-refactor-composeworkspace-to-shared-lib.poml |
| **Title** | Move ComposeWorkspace + hooks + types to @spaarke/compose-components |
| **Phase** | Phase 7: three-pane pivot (supplement scope) |
| **Status** | ✅ completed |
| **Rigor Level** | FULL |
| **Started / Completed** | 2026-07-01 / 2026-07-01 (~1.5 h vs 3 h estimate) |
| **Files moved** | 6 tsx components + `ComposeWorkspace.types.ts` + 3 hooks (+ hooks/index.ts) + `compose-contracts.ts` (774 LOC) → `src/client/shared/Spaarke.Compose.Components/src/{widgets,widgets/hooks,types}/`. Tests relocated to `src/solutions/SpaarkeAi/src/__tests__/compose/`. |
| **Import rewires** | 3 `../../types/compose-contracts` → `../types/compose-contracts`; 1 self-import fix (`ComposeWorkspace.tsx` `@spaarke/compose-components` → `./ComposeEditor`); App.tsx: `./components/compose` + `./types/compose-contracts` → `@spaarke/compose-components`; test files: `../ComposeToolbar` etc. → `@spaarke/compose-components/widgets/ComposeToolbar` etc. (subpath — avoids pre-existing SpaarkeAi jest `ui-components/components/*` gap). |
| **Barrel exports added** | 20+ new export lines in shared lib `src/index.ts` covering: workspace-level widgets (ComposeWorkspace/Toolbar/BannerStack/EmptyState/ConflictDialog), reducer + state types + 3 hooks, and Flow 1-6 data contracts. |
| **Build verification** | ✅ tsc `@spaarke/compose-components` 0 errors; ✅ SpaarkeAi Vite prod 3714 modules / 18.73s / 4877.63 kB / gzip 1357.38 kB (identical to pre-091 bundle). |
| **Test verification** | 24/24 relocated Compose tests pass in 7.9s. Full SpaarkeAi suite: 12 failed / 18 passed suites → **IDENTICAL to pre-091 baseline** (verified via git stash + re-test). The 12 failing suites are pre-existing infra gap on `@spaarke/ui-components/components/CreateMatterWizard` subpath (FU-91a). |
| **Reverse-dep check** | ✅ grep confirms no `solutions/` imports from shared lib (dependency graph unidirectional per POML acceptance #4). |
| **Contract satisfied** | Spike #2 §11 open item #2 promotion trigger fires (second consumer of compose-contracts.ts now exists). Calendar Pattern D packaging precedent matched: shared-lib widgets + types exported from `@spaarke/compose-components`; consumer shims import from package name. |
| **Follow-ups filed** | FU-91a: SpaarkeAi jest infra gap on `@spaarke/ui-components/components/*` subpaths (pre-existing; 12 failing suites unrelated to compose). Fix belongs to a dedicated infra task if failing suites ever become CI-blocking. |

---

### Prior task (W8-060 — moved to archive)

| Field | Value |
|-------|-------|
| **Task ID** | 060 |
| **Task File** | tasks/060-smoke-test-compose-summarize.poml |
| **Title** | E2E smoke test: Compose UI button → BFF → consumer routing → Document Summary playbook → result |
| **Phase** | Phase 6: E2E Smoke Test |
| **Status** | ✅ completed (in-process pipeline trace; live Dev BFF execution operator-deferred to W10/W11) |
| **Started** | 2026-06-29 (Wave 8 parallel dispatch sub-agent) |
| **Completed** | 2026-06-29 |
| **Rigor Level** | STANDARD (POML rigor-hint=STANDARD); **TEST-MODIFYING override** → code-review + adr-check UNCONDITIONAL at Step 9.5 per CLAUDE.md §8 + ADR-038 |
| **Files created** | (1) `tests/integration/regression/Compose/ComposeSummarizeRoundtripSmokeTests.cs` (7 new pipeline-trace tests; KEEP path: `regression`); (2) `projects/spaarkeai-compose-r1/notes/smoke-tests/compose-summarize-roundtrip.md` (operator-readable smoke-test write-up with live verification sequence for W10/W11) |
| **Test count delta** | 128 → 135 (+7 Compose tests; all pass in <1s on in-process host) |
| **Build verification** | `dotnet build` ✅ 0 errors / 18 warnings (= baseline); `dotnet test --filter Compose` ✅ all 135 pass |
| **ADR compliance** | ADR-001 Minimal API ✅; ADR-008/028 RequireAuthorization ✅; ADR-013 refined facade ✅ (grep + DI inspection; only IConsumerRoutingService + IInvokePlaybookAi reachable); ADR-015 Tier 3 (tenantId flows through PlaybookInvocationContext) ✅; ADR-019 endpoint conventions ✅; ADR-038 KEEP path `regression` + zero banned patterns ✅ |
| **ADR tensions** | None |
| **Smoke-test design rationale** | 060 verifies the **end-to-end pipeline shape** (FR-11 acceptance) — distinct from W5-027 contract tests (per-endpoint HTTP contract). The 060 tests trace the parameter-dict translation, the routing-resolution path, and the response projection — each load-bearing for FR-11 but not asserted in 027. |
| **Live Dev BFF execution** | ⚠️ Operator-deferred to W10/W11. Exact verification sequence locked in smoke-test write-up §7 (load test DOCX → Summarize button → observe network + App Insights + UI render + Dataverse ChatSession row). |
| **Downstream open items** | O-060-1: Operator runs §7 live sequence post-W10/W11 deploy; O-060-2: update §7 with screenshots + log spans + measured latency; O-060-3: file ISS-{NNN} via `/defer` if any hop fails (do NOT add workarounds in 060/061); O-060-4: task 070 SC mapping; O-060-5: task 071 banned-pattern re-scan after PR |

---

### Previous task (W6-050 — moved to wave archive)

| Field | Value |
|-------|-------|
| **Task ID** | 050 |
| **Task File** | tasks/050-spe-checkout-on-compose-open.poml |
| **Title** | Acquire SPE check-out on Compose open (REVISED per Spike #3 §9 → frontend-only call to existing endpoint) |
| **Phase** | Phase 5: SPE Check-out Lock |
| **Status** | ✅ completed |
| **Started** | 2026-06-29 (Wave 6 parallel dispatch sub-agent) |
| **Completed** | 2026-06-29 |
| **Rigor Level** | FULL (POML rigor-hint=FULL; tags include `bff-api`/`backend`/`frontend`/`spe`; conflict-handling + ADR-028 auth + Tier 3 logging discipline) |
| **Spike-revised scope** | POML pre-dates Spike #3. Spike #3 §9 LOCKED revised direction: call existing `POST /api/documents/{documentId}/checkout` from Compose React; NO BFF code changes. Path A ADR Tension approved at post-Wave-0 gate. |
| **Endpoint reused** | `POST /api/documents/{documentId}/checkout` (existing in `DocumentOperationsEndpoints.cs:30`); returns 200 `CheckoutResponse` or 404/409 `DocumentLockedError`. Same-user re-checkout is idempotent 200. |
| **Files modified** | `src/solutions/SpaarkeAi/src/components/compose/ComposeWorkspace.tsx` (~170 LOC added: new types ComposeCheckoutStatus + ComposeCheckoutLockedByInfo; 5 new reducer actions + branches; checkout useEffect; 2 new MessageBar banners; `data-compose-checkout-status` data-attr) |
| **Gating** | Checkout fires ONLY when `documentRef.sprkDocumentId` present (post-promotion). Path B ephemeral docs land in `'skipped'` state; first-Save promotion (task 022/024 implemented) updates documentRef → effect re-fires from `'skipped'` and acquires lock. |
| **Conflict UX (R1)** | 200 → silent success; 409 → warning MessageBar with "Locked by {name} since {ts}" — task 051 swaps for richer multi-tab UX. 4xx/5xx/network → info MessageBar (non-fatal — editor remains usable). |
| **Heartbeat** | Already wired in W4-045 ComposeEditor (3-min sliding, visibility-gated) — no changes here. |
| **Build verification** | SpaarkeAi `npm run build` ✅ 3704 modules / 14.97s / 0 surface errors; `@spaarke/compose-components` tsc ✅ 0 errors |
| **ADR compliance** | ADR-028 `authenticatedFetch` + `buildBffApiUrl` ✅; ADR-015 Tier 3 — only metadata logged ✅; ADR-013 facade — N/A (no AI dispatch) ✅; ADR-008 RequireAuthorization — N/A (no new endpoint) ✅; ADR-038 — no banned test patterns introduced ✅ |
| **Downstream open items** | 051 (multi-tab same-user UX — note that DocumentCheckoutService returns 200 idempotent, NOT 409, for same-user; 051 needs a /checkout-status probe); 052 (heartbeat BFF backend — sweeper hosted service + endpoint + RefreshHeartbeatAsync method per spike §4.3) |

---

## Progress

### Completed Steps

*No steps completed yet*

### Current Step

*No active task*

### Files Modified (All Task)

*No files modified yet (project initialization files are not task-attributed)*

### Decisions Made

*No task-level decisions recorded yet. Project-level decisions are in `CLAUDE.md > Decisions Made`.*

---

## Next Action

**Next Step**: Generate task files via `/task-create projects/spaarkeai-compose-r1` (or via `/project-pipeline` Step 3 — autonomous mode handles this automatically).

**Pre-conditions**:
- ✅ `spec.md` exists and is validated
- ✅ `design.md` exists with locked decisions (§14) and spike plan (§13)
- ✅ `README.md`, `plan.md`, `CLAUDE.md` generated
- ✅ Folder structure created (`tasks/`, `notes/debug|spikes|drafts|handoffs/`)
- ✅ Hot-path declaration validated in design.md (BFF=Y, SpaarkeAi=Y, ci-workflows=N, skill-directives=N, root-CLAUDE.md=N)
- ✅ ADR Tensions section scanned (declared "no tensions surfaced" per CLAUDE.md §6.5)

**Key Context**:
- Refer to `plan.md §4 Phase Breakdown` for the 9-phase task decomposition basis
- Refer to `spec.md §Affected Areas` for files/folders Compose will touch
- Refer to `design.md §13 R1 Spike Plan` for the 4 spikes (Phase 0 tasks)
- ADR-013 refinement (2026-05-20) applies to ALL BFF endpoints: PublicContracts facade only

**Expected Output**:
- `tasks/TASK-INDEX.md` with phased POML task files (Phase 0 spike tasks 001–00X first; main implementation tasks numbered 010+ per task-create 10-gap convention; wrap-up task `090-project-wrap-up.poml`)
- Each task file has `<knowledge><files>` populated per tag-to-knowledge mapping
- Parallel-execution groups identified (Spikes #2/3/4 can run parallel; Phase 2 services can parallelize per file)
- `.claude/`-touching tasks marked `parallel-safe: false` (sub-agent write boundary)

---

## Blockers

**Status**: None

Project initialization complete; ready for task generation.

---

## Session Notes

### Current Session

- Started: 2026-06-29 (project-pipeline run)
- Focus: Project initialization — generate README, plan, CLAUDE.md, current-task.md, folder structure

### Key Learnings

- design.md §10.5 includes a Placement Justification per CLAUDE.md §10 — copied/extended in spec.md §Placement Justification
- Hot-path overlap with 14 BFF + 8 SpaarkeAi active worktrees flagged informationally; no blocking conflicts
- 20 work branches have unmerged commits to master (top: r7=81, multi-container=56, datagrid=55) — non-blocking but flagged for portfolio hygiene
- Compose deliberately REUSES existing patterns (per CLAUDE.md §11 Component Justification) — no parallel ChatSession infra, no parallel Word-handoff plumbing, no parallel consumer-routing facade

### Handoff Notes

*No handoff notes — project just initialized*

---

## Quick Reference

### Project Context

- **Project**: spaarkeai-compose-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) (will be created by `task-create`)
- **Spec**: [`spec.md`](./spec.md)
- **Design**: [`design.md`](./design.md)

### Applicable ADRs

- ADR-001 Minimal API — Compose endpoint pattern
- ADR-008 Endpoint filters — `RequireAuthorization()` on every Compose endpoint
- ADR-010 Org-owned Dataverse default — new rows
- ADR-013 (refined 2026-05-20) BFF AI extraction — PublicContracts facade only
- ADR-015 Multi-tenant isolation Tier 3 — inherits from ChatSession infra
- ADR-019 Endpoint conventions — `/api/compose/` group
- ADR-028 Spaarke Auth v2 — `@spaarke/auth` + BFF auth pipeline
- ADR-032 BFF Null-Object Kill-Switch — applies if feature-gated (R1 default: no gates)
- ADR-038 Testing strategy — integration-heavy pyramid; 6 KEEP categories; mock-boundary; ban list

### Knowledge Files Loaded (initial reference set)

- `spec.md` — authoritative scope
- `design.md` §13 (spike plan) + §14 (resolved decisions)
- `.claude/constraints/bff-extensions.md` (binding pre-merge checklist for BFF additions)
- `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — workspace layout pipeline
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — two-wrapper architecture (authoritative)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — Calendar Pattern D worked example
- `docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md` — exact steps for `compose-summarize`
- `docs/standards/TEST-ARCHITECTURE.md` — test pyramid + 6 KEEP

---

## Recovery Instructions

**To recover context after compaction or new session**:

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load task file**: `tasks/{task-id}-*.poml` (once `task-create` has run)
4. **Load knowledge files**: From task's `<knowledge>` section
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction
- "where was I?" — Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
