# R7 (spaarke-ai-platform-unification-r7) — What Was Delivered + Updated UAT Checklist

> **Generated**: 2026-07-02 (end of Linear AI Consumer migration session)
> **Scope**: full R7 project (Waves 1-12, 33 FRs, ~90-120 tasks)
> **Master tip after tonight**: `ac783660d` (PR #542 merge commit)
> **Not** just Wave 12 — R7 as a whole. Earlier version of this doc under-scoped.

---

## R7 One-Line Purpose

Collapse Spaarke's three-layer playbook dispatch model into a single typed Choice column (`sprk_executortype`) on `sprk_playbooknode`, build the missing `AiCompletionNodeExecutor` to close R4's `/narrate` end-to-end, promote `sprk_playbookconsumer` to first-class, add typed config schemas per executor, migrate all 94 existing playbook nodes in spaarkedev1, AND (via Wave 11-12) drive the whole platform to MVP-in-production state across three feature groups.

---

## Full Wave Structure — What Was Delivered

### Wave 1 — `AiCompletionNodeExecutor` build (FR-12 to FR-15)

**Status**: ✅ Shipped

- New executor mirrors `EntityNameValidatorNodeExecutor` shape (Singleton, ILogger only, ConfigJson read + Validate).
- Reuses `PromptSchemaOverrideMerger` for per-node Role/Task/Constraints/Output Fields overrides.
- Wraps `IOpenAiClient.GetStructuredCompletionRawAsync`.
- DI-registered via `AnalysisServicesModule.AddNodeExecutors`.
- **Closes R4 graduation gate** — `/narrate` now has a working AI Completion node.

### Wave 2 — Dispatch refactor + enum rename (FR-07 to FR-10)

**Status**: ✅ Shipped

- Enum rename: `ActionType` → `ExecutorType` (~1000+ references).
- `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.sprk_executortype` directly.
- Removed structural-fallback dispatch chain.
- Removed Action ActionType override branch.

### Wave 3 — Typed config schemas (FR-16)

**Status**: ✅ Shipped

- Each executor declares its config shape.
- BFF endpoint `GET /api/ai/playbook-builder/executor-config-schemas` serves schemas to PlaybookBuilder.
- PlaybookBuilder uses these schemas to render dynamic per-node config forms.

### Wave 4 — Schema cleanup + remove legacy direct-path (FR-03, FR-04, FR-11)

**Status**: ✅ Shipped

- Deleted `AnalysisOrchestrationService.ExecuteAnalysisAsync` (legacy direct-path).
- Dropped 2 unused columns on `sprk_analysisaction`.
- Legacy chat-summarize path retired (see Wave 9).
- BFF publish size shrunk after this wave.

### Wave 5 — Existing-playbook backfill (FR-19, FR-20)

**Status**: ✅ Shipped

- All **94 existing playbook nodes** in spaarkedev1 backfilled with `sprk_executortype` values.
- New scripts: `Review-PlaybookNodes-Dispatch.ps1`, `Migrate-PlaybookNodes-to-ExecutorType.ps1`.
- `Deploy-Playbook.ps1` updated to write executor type explicitly (no more name-detection).
- Playbook redeploys verified for Daily Briefing, Insights, Chat.

### Wave 6 — Documentation deletion + updates (FR-28 to FR-31)

**Status**: ✅ Shipped

- Deleted outdated R4 canonical-truth sections in `docs/architecture/ai-architecture-*`.
- Rewrote `docs/guides/JPS-AUTHORING-GUIDE.md` + `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` for the executortype model.
- Created `docs/guides/ai-guide-consumer-wiring.md` (maker tutorial for wiring a new consumer).

### Wave 7 — Skill rewrites (FR-32, FR-33)

**Status**: ✅ Shipped (sequential main-session per Sub-Agent Write Boundary)

- Rewrote 5 skills: `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh`.
- All now node-first (dispatch by `sprk_executortype`, not by name).

### Wave 8 — Playbook Builder UI updates (FR-21 to FR-27)

**Status**: ✅ Shipped

- PlaybookBuilder canvas migrated `sprk_nodetype` → `sprk_executortype`.
- Node config forms render dynamically from schemas served by Wave 3 endpoint.
- New AI Completion node type visible + editable in the maker UI.

### Wave 9 — Consumer migration (FR-17, FR-18)

**Status**: ✅ Shipped

- Migrated `chat-summarize` to `IConsumerRoutingService`-driven dispatch (Path A.5).
- Wired Playbook Library into ≥3 consumer surfaces.

### Wave 10 — Wrap-up

**Status**: ✅ Wrap-up ran; task 100 marked 15 success criteria green at verification level. Task 101 (`/narrate` UAT — R4 graduation gate) failed initially → triggered Wave 11.

### Wave 11 — Orchestrator runtime variable resolution + UAT drive (added 2026-06-29)

**Status**: ✅ Shipped

Added post-Wave-10 when `/narrate` UAT couldn't pass. Root cause: `PlaybookOrchestrationService`'s template engine did LITERAL `{{paramName}}` substitution only — it didn't carry node outputs forward as resolvable context, lacked custom helpers (`{{json}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`), and lacked fan-out iteration semantics that the deployed `DAILY-BRIEFING-NARRATE` playbook depended on.

**Wave 11 landed**:
- Two-layer JPS render architecture (Layer 1 orchestrator template resolution + Layer 2 `PromptSchemaRenderer` `## Input` section) — see [`SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- Custom Handlebars helpers: `{{json}}`, `{{map}}`, `{{flatten}}`, `{{distinct}}`, `{{concat}}`, `{{join}}`
- Fan-out iteration for `flatMap` sources
- POC of the code-defined narrator pattern (`DailyBriefingNarrator`) — the seed that led to Wave 12's Linear AI Consumer architecture
- `/narrate` end-to-end proven working — R4 graduation gate closed

### Wave 12 — MVP Completion Push (added 2026-06-30)

**Status**: ✅ Shipped end-to-end (this was the last two weeks of R7)

Three feature groups + a late-added architectural refactor (Linear AI Consumer, tonight's work):

**12.1 Daily Briefing — 6-Entity Widget + UI Polish** ✅
- Widget cutover (`sprk_daily_briefing_widget`) — `/render` is sole data source (`ad53af431`)
- 6-entity collector: Tasks (Upcoming + Overdue), Documents, Matters, Projects, To-Dos
- Owner-polymorphic membership resolver fix (`02d925ae9`)
- Membership FetchXml fallback with per-entity `owninguser` + widened date windows
- High Priority section above TL;DR + inline hyperlinks + `[N]` citations + orphan-fallback entity links
- "Add to To Do" tool checkmark (PascalCase OData binding fix tonight in `2a7e47771`)

**12.2 Wizards — All 5 Migrated to Linear AI Consumer path** ✅ (tonight)
- New shared library `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/`
- 4 primitives: `IActionResolver` (config-driven ActionId map), `IDocumentTextSource` (SPE + OBO + direct-file), `IActionRunner` (wraps `GetStructuredCompletionRawAsync` with JPS rendering + per-consumer `MaxOutputTokens` / `ModelDeployment` overrides), typed records
- Consumer services: `DocumentProfileService`, `FileSummarizeService` + refactored `MatterPreFillService.ExtractFieldsViaLinearAsync` + `ProjectPreFillService.ExtractFieldsViaLinearAsync`
- Endpoint dispatch shims in `AnalysisEndpoints.ExecuteAnalysis` + `WorkspaceFileEndpoints.HandleSummarize` (Linear-if-configured, else fall through to engine)
- Client-side shared substrate: `useLinearRunProgress` hook + `<LinearRunProgressList>` presenter (Fluent v9); Summarize wizard migrated
- Dataverse: `sprk_outputschemajson` populated on all 4 target Action rows
- App Service settings wired for all 4 consumers on `spaarke-bff-dev`

**12.3 Assistant ↔ Workspace ↔ Context** ⏳ Partial
- T151 — server-side EntityName lazy-fetch in `PlaybookChatContextProvider` ✅
- T152 — default PageType in `PlaybookChatContextProvider` ✅
- T153 — audit 120 Gaps D-H dispositional closure ✅
- **Residual UAT scope unclear** — you noted "other R7 UAT after we get this phase done"

---

## What Was NOT Delivered / Explicit Deferrals (per plan)

Recorded so the UAT scope doesn't accidentally chase deferred work:

| Item | Reason | Where it went |
|---|---|---|
| Action Engine R1 (Spaarke Claw, Tool Registry classification, gate resolvers, three meta-tools, Action Templates, agent UX) | Different project; R7 laid the foundation | `ai-spaarke-action-engine-r1` (on HOLD until R7 ships) |
| Polished maker UX beyond Playbook Builder | Not in R7 scope | Future work |
| Multi-tenant rollout | Single-tenant MVP | Future |
| Backward-compat shims | Big-bang cutover per Q6 | N/A |
| Tunable config tables (`sprk_briefingdatasource` Tier B) | "Config-with-rules IS interpreter" per operator | Future project IF justified |
| `IMembershipResolverService` root-cause fix | FetchXml fallback works | Follow-on |
| Retrieval over SharePoint Embedded | R5 deferred; too big for MVP | `spaarke-ai-retrieval-r1` (proposed) |
| Playbook-Engine → code-defined compilation for remaining engine consumers | Will address per-consumer as UAT surfaces the need | R8 or later |

---

## Updated UAT Checklist (Full R7 Scope)

Grouped by the wave / functional area. ✅ = signed off; ⏳ = pending your smoke; 🚧 = optional / infra-gated.

### Wave 1-2 — Dispatch + AiCompletion

- [x] `sprk_executortype` reads canonical in orchestrator; no structural fallback
- [x] `AiCompletionNodeExecutor` compiles + DI-registered + runs the AiCompletion node in `DAILY-BRIEFING-NARRATE`
- [x] R4 `/narrate` end-to-end works (Wave 11 closure)

### Wave 3 — Typed config schemas

- [x] `GET /api/ai/playbook-builder/executor-config-schemas` returns schemas per executor
- [ ] **UAT** (if you want to reconfirm): open PlaybookBuilder → new node → config form renders dynamically per executor type

### Wave 4 — Legacy cleanup

- [x] `AnalysisOrchestrationService.ExecuteAnalysisAsync` deleted (no callers, no crashes)
- [x] BFF publish size shrunk after wave (verified earlier tonight: 46.82 MB, well under 60 MB ceiling)

### Wave 5 — Backfill

- [x] 94 spaarkedev1 nodes populated with `sprk_executortype`; no nulls remain
- [x] Daily Briefing, Insights, Chat playbook redeploys verified

### Wave 6 — Docs

- [x] Outdated R4 canonical-truth sections deleted from `docs/architecture/`
- [x] `JPS-AUTHORING-GUIDE.md` + `PLAYBOOK-AUTHOR-GUIDE.md` reflect executortype model
- [x] `ai-guide-consumer-wiring.md` published

### Wave 7 — Skills

- [x] 5 skills rewritten: `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh`
- [ ] **UAT** (optional): run `/jps-playbook-audit` on a canonical playbook and verify it dispatches by executortype cleanly

### Wave 8 — Playbook Builder UI

- [x] Canvas + node forms migrated to `sprk_executortype`
- [x] AI Completion node type visible + editable in maker portal
- [ ] **UAT** (optional): create a new playbook via PlaybookBuilder end-to-end, add an AI Completion node, save + deploy

### Wave 9 — Consumer migration

- [x] Chat-summarize migrated to `IConsumerRoutingService` dispatch
- [x] Playbook Library wired into ≥3 consumer surfaces

### Wave 10-11 — Wrap-up + Runtime resolution

- [x] `/narrate` UAT closure (R4 graduation gate)
- [x] Two-layer JPS render architecture ratified
- [x] Custom Handlebars helpers + fan-out iteration working (Daily Briefing depends on these)

### Wave 12.1 — Daily Briefing (AC1-AC7)

- [x] AC1 6 channels render / AC2 real Dataverse records / AC3 membership filter / AC4 TL;DR ↔ Notes consistency / AC5 entity links / AC6 Add-to-ToDo tool / AC7 timezone

### Wave 12.2 — Wizards (AC8-AC12)

- [x] AC8 File Summary structured / AC9 Doc Upload Profile / AC10 Matter+Project+WA Prefill / AC11 Action schema editing still affects behavior / AC12 all 5 wizards operator-verified end-to-end

### Wave 12.3 — Assistant ↔ Workspace (AC13-AC15) — ⏳ **NEXT UAT SURFACE**

- [ ] **AC13** — Assistant chat in workspace context knows current matter ID
- [ ] **AC14** — Assistant responses reference matter-specific data (not generic) when present
- [ ] **AC15** — Operator-verified end-to-end UAT (specifics TBD; T120 audit produced the gap list)

**Smoke plan for AC13-AC15**:
1. Open a matter form (with docs / tasks / people attached)
2. Launch assistant → ask a matter-specific question ("who is assigned?", "what documents are attached?")
3. Verify: matter-scoped response + working entity links + no generic fallback
4. Repeat on project form + work-assignment form

### System (AC16-AC17)

- [x] AC16 BFF publish ≤60 MB compressed (46.82 MB at last deploy tonight)
- [x] AC17 0 new HIGH-severity CVEs (Wave 12 introduced no new packages)

### Coexistence — Optional Post-Wave-12 Sanity (🚧)

Playbook Engine remains for Chat + Insight Engine + Compose R1. Should confirm nothing regressed:

- [ ] **CX1** — Send a message in Sprk Chat, verify normal reply
- [ ] **CX2** — Run one Insight Engine action, verify normal output
- [ ] **CX3** — Compose R1 `compose-summarize` — their team owns this smoke (they synced master post-PR #539)

---

## Deferred to Next Session

- **Phase E** (Wave 12.2 follow-on) — deactivate 4 migrated `sprk_analysisplaybook` rows in Dataverse. Safe + reversible; recommended after coexistence smoke passes.
- **Phase G** (Wave 12.2 follow-on) — `BUILD-A-NEW-LINEAR-AI-CONSUMER.md` maker tutorial + Wave 12 changelog entry.
- **AC13-AC15** — Assistant↔Workspace UAT.
- Client-side follow-on: migrate Document Upload's `useAiSummary` to the shared `useLinearRunProgress`; SprkChat Context pane pickup.

---

## Sibling / Downstream Projects Now Unblocked

| Project | State |
|---|---|
| `spaarke-daily-update-service-r5` | R7 Wave 11 pattern proven; R5 project inherits R7 W12 Doc Profile Linear consumer + Daily Briefing narrator pattern. Feedback from R7 W12 UAT captured in `projects/spaarke-daily-update-service-r5/notes/inbound-from-r7/`. |
| `spaarke-daily-update-service-r4` | R4 graduation gate closed by R7 W11 `/narrate` UAT + R7 W12 Linear consumer library (which makes future narrative-output consumers ~10× less code). |
| `ai-spaarke-action-engine-r1` | Was HOLD at Phase 0 spike waiting for R7 to ship. R7 now shipped through Wave 12; Action Engine can resume. |
| `spaarke-ai-platform-chat-routing-redesign-r1` | Owned `ai-architecture-consumer-routing.md` as READ-ONLY in R7. R7 doesn't touch it; chat-routing-redesign-r1 continues independently. |
| `spaarkeai-compose-r1` | Compose team synced master post-PR #539 tonight; their `compose-summarize` path unchanged by R7 W12 Linear migration. |

---

## Reference

- Spec: [`spec.md`](../spec.md) (33 FRs)
- Design: [`design.md`](../design.md) (v0.6)
- Plan: [`plan.md`](../plan.md) (Waves 1-11 in original scope, Wave 12 added mid-project)
- Wave 12 MVP plan: [`wave12-mvp-completion-plan.md`](wave12-mvp-completion-plan.md) (AC1-AC17)
- Wave 12 Linear consumer architecture: [`docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md)
- Wave 11 Playbook Engine narrator pattern: [`docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](../../../docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md)
- PRs shipped tonight: [#539](https://github.com/spaarke-dev/spaarke/pull/539) (Phase B) + [#542](https://github.com/spaarke-dev/spaarke/pull/542) (Phase C-D)

---

*Regenerate after Assistant↔Workspace UAT + coexistence smoke.*
