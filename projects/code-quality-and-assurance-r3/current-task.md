# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-16 (context-handoff — POST-PROGRAM: review + follow-on fixes + RED project setup + RED-4/DEF-1)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | code-quality-and-assurance-r3 — **COMPLETE (35/35)** + extensive post-program follow-on. **All merged to master `3146837a7`.** Nothing uncommitted. |
| **State** | ✅ Program closed (aggregate un-gated **F→D**, maintainability mean **C+**; A+ = multi-cycle). ✅ Merged to master + **BFF deployed to dev** (Finance recalc 401 verified live). ✅ Post-program review (4 evidence agents). ✅ Follow-on fixes merged. ✅ RED follow-on projects defined/set up. **No active in-flight task** — this is a coordination/handoff state. |
| **Next Action** | **RED-4 "B" mostly landed.** ✅ dead-code deletion (`e94987071`, **merged to master**, main repo synced). ✅ **DEF-2 fixed** (implemented `GetEntitySetNameAsync` on WebApi + live-gated regression; verified full BFF suite green) — commit pending push/merge. **ONE B item remains**: SDK silent-empty stubs→throw, still gated on **DEF-1** (smart-todo-r5). DEF-2 needs a **live-dev smoke** (field-mapping write vs spaarke-dev) to close behaviorally. |

### ✅ Completed this session (all on master `3146837a7`)
1. **r3 closeout**: 061 (`5a6ad556e`), 041 (`e1f06c5a9`), 042+063 (`cec19d374`), 090 (`704b6a213`). Merged to master; BFF deployed to dev.
2. **Review + follow-on fixes**: eliminated the masked nullable class CS8601/CS8604 (Fable-verified SAFE); tightened analyzer allowlist; **redesigned the God-class ratchet** (per-file freeze + 2,000 new-file ceiling, replacing arbitrary 2,700) + documented it (`.claude/patterns/testing/god-class-ratchet.md` + CLAUDE.md §17 + memory); hardened the naming gate.
3. **Handoffs**: `customer-provisioning-orchestration-r1` (unblocked; `projects/customer-provisioning-orchestration-r1/notes/r3-handoff.md`) · `ci-cd-unit-test-remediation-r1` (RED-3) · **`email-communication-intelligence-r2` merged to master** for them.
4. **RED follow-on projects**: **RED-1** `projects/speadmin-decomposition-r1/` + **RED-2** `projects/chatendpoints-decomposition-r1/` (folders, initialize-only) · RED-3 routed to ci-cd-r1 · **RED-4** Fable-verified assessment (`notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`) → set up **C** `projects/dataverse-access-unification-r1/`, routed **#3b** MI→task 011/NG1 (`notes/task-011-ng1-3b-mi-migration.md`), delivered **B keystone** (`docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md`), found **DEF-1**.

### ▶ Open threads (next)
- **DEF-1** (`notes/defer-issues.md`, ROUTED→smart-todo-r5): 2 of 5 `TodoGenerationService` rules (Overdue events :322, Deadline :~460) silently make zero To Dos — query `sprk_event` via composite→SDK silent-empty stub. **smart-todo-r5 decides** (A: inject `IEventDataverseService`; B: remove Rules 1&3 as legacy) per `projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md`. **Then** the RED-4 B "silent-empty stubs→throw" step can run (sequenced after — else it crashes the generator). GitHub issue for DEF-1 left in r5 per operator.
- **RED-4 B** (branch `work/dataverse-access-hardening`, `e94987071`, NOT pushed): ✅ **dead-code deletion DONE** — `DataverseWebApiService` 2,822→1,409 LOC (−1,414), class narrowed to `IEventDataverseService, IFieldMappingDataverseService`, waiver removed (frozen 14→13), all builds + BFF 10,402 tests + ArchTests 38/38 green. ⏳ **silent-empty→throw** still gated on DEF-1. 🔔 **split-brain → DEF-2** (see below) — owner decision needed.
- **DEF-2** (`notes/defer-issues.md`, ✅ FIXED RED-4 B): `DataverseWebApiService.GetEntitySetNameAsync` (`:176`) threw NotImplementedException, breaking the 3 live field-mapping methods (`RetrieveRecordFieldsAsync:785`, `QueryChildRecordIdsAsync:839`, `UpdateRecordFieldsAsync:1050`). Owner chose "fix now" → implemented against `EntityDefinitions` metadata (cached, fails loud), mirroring `GetEntityObjectTypeCodeAsync`. Live-gated regression added (`Spe.Integration.Tests/DataverseWebApiFieldMappingRegressionTests.cs`). **Remaining**: live-dev smoke to confirm behaviorally; #3b MI migration must keep EntityDefinitions read for the app-user.
- **#3b MI migration** → task 011/NG1 (both Dataverse impls; operator prereqs: register MI as Dataverse App User + grant `prvActOnBehalfOfAnotherUser`; never remove secret until MI proven live).
- **Operator-gated execution**: RED-1, RED-2, C (worktree + task breakdown created at start via `/design-to-spec`→`/project-pipeline`).
- **Backlog carryovers**: CI-workflow gate wiring (042/063) coordinated PR w/ ci-cd-r1 · TS per-surface mechanical baseline · CS0618 retirement (DemoExpiration refactor) · Console→ILogger (39 DI sites) · #772 deferred pkg majors.

---

## (historical) Quick Recovery

> ▶️ **REMEDIATION IN PROGRESS (2026-08-14).** Autonomous (operator: "everything done, efficient, parallel where safe"). Method: disjoint edit-only subagents → I integrate (build+test+commit). **DONE**: Phase 0 ✅ · Phase 1 ✅ (all assessments+aggregate) · **BFF Phase 2 remediation ✅** (020 dead-code · 021 Bug-1 · 022 Bug-2 · 023 **auth F CLOSED** · 024 AI-facade · 025 Endpoints→Api · 026-decompose · 027 tarballs · 028 downcast→1 · 029 Safety-cluster) · **060** #3a · horizontals **030** security(FR-17+CORS) · **032** CVE(pdfjs+STJ) · **033** observability · **034** doc-drift. Every integration: build 0-err, tests green (last 10,392/0/101). Committed through `edc66dfa2` (NOT pushed). **NOW**: 031 test-quality running. **REMAINING**: 040 ArchTests → 041 mechanical baseline → 042 CI gates (forcing-fns, now that remediation makes invariants green) · 061 config-validation · 062 Graph-app-role constants · 063 naming standard · 090 wrapup(+/test-diet). **DEFERRED/ESCALATED**: 026 Finance rename (Dataverse `sprk_analysistool` pre-check, offline-blocked) · 023+030 web-resource MSAL token flow needs LIVE-Dataverse validation + app-reg prereqs (SPA redirect URI + user_impersonation consent) + form-library registration + live Cors:AllowedOrigins list (notes/task-023-notes.md) · 033 deferred 43 AnalysisServicesModule console + 7 email-log sites · 032 PDF-parse smoke test · #772 deferred pkg majors. Residual trivial stale-comment refs (deleted OwnershipValidator/GetServiceClient/SafetyPipelineMiddleware).

> ✅ **PHASE 1 COMPLETE (2026-08-14).** All 7 surface assessments + auth map + aggregate re-baseline DONE. **Grades**: BFF **F**¹ · client-libs B– · server-libs C+ · PCF C+ · dataverse B– · code-pages C · plugins **D** · config-deployment **F**. **AGGREGATE = F** (gating-capped by the live unauthenticated Finance Dataverse-write endpoint — ONE root cause on 2 surfaces); **maintainability mean ≈ C+**. Supersedes March "A 95/100". ¹BFF D3 re-scored B–→F under the standing rubric (task 016). **Highest-leverage fix = BFF task 023** (@spaarke/auth Finance closure) — clears both F's. New cross-cutting findings for horizontals: pdfjs-dist + System.Text.Json HIGH CVEs (→032), `.claude/patterns/dataverse/plugin-structure.md` teaches retired BaseProxyPlugin (→034), PCF RegardingResolver CREATE displayName bug (one-line). Workflow engine hardened (4 runtime bugs fixed). Next: remediation is OPERATOR-GATED (BFF Tranche A + deployment = outward-facing PRs).


| Field | Value |
|-------|-------|
| **Task** | Autonomous remediation orchestration. **30 of 35 tasks ✅** (Phase 0, Phase 1, all BFF Phase 2, 060, 062, all 5 horizontals, 040 ArchTests). **HEAD = `2b6b05676`** (NOT pushed; branch `work/code-quality-and-assurance-r3`). |
| **Step** | **1 background agent IN-FLIGHT**: **061** uniform fail-fast config validation (#2) — id `a20e79c65517907e0`, edits BFF Options classes + their DI registration sites (adds `.ValidateDataAnnotations().ValidateOnStart()` + `[Required]` on genuinely-required members; e.g. `TenantEnvironmentRoutingOptions`). Its edits will be uncommitted on disk. |
| **Status** | Method: dispatch disjoint-file **edit-only** subagents → main session integrates (build `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release` + `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` → commit + mark POML `<status>completed` + flip TASK-INDEX 🔲→✅ **via Edit tool** (emoji sed fails on git-bash) + selective `git add`, never `git add .`). Last full BFF test 10,388/0/97; ArchTests 38/0. |
| **Next Action** | **(1) On 061 completion**: build Release + full BFF test. ⚠️ WATCH: 061 adds `[Required]` DataAnnotations → a test that boots with partial config could newly fail; if so, that test was relying on invalid config — fix the test's config or scope the annotation. Then commit + mark 061 ✅. **(2) REMAINING (4): the build/CI-config trio 041** mechanical baseline (TreatWarningsAsErrors/analyzers/.editorconfig/ESLint `--max-warnings 0`) → **042** CI gates (deps 040 ✅; `.github/workflows` — **coordinate ci-cd-unit-test-remediation-r1**) → **063** naming standard + conformance gate (deps 017 ✅; docs/architecture + `.github/workflows`) — run SEQUENTIALLY (shared build/CI config; each edits `.github/workflows` or Directory.Build.props). **Then 090** wrap-up: invoke `/test-diet` (BINDING gate, CLAUDE.md §7) → `notes/test-diet-report.md`, then close + report. **(3)** Do NOT push (operator hasn't asked). |

### In-flight agent recovery (if compacted mid-run)
- **061** (`a20e79c65517907e0`) running. On resume: auto-notifies on completion; if BFF Options edits are on disk (`git status`), integrate (build + full BFF test — watch for `[Required]`-tripped tests). If not notified + no edits, re-dispatch from `tasks/061-*.poml` (edit-only; behavior-neutral for VALID config; don't mark optional settings `[Required]`).
- **040 ✅ committed** `2b6b05676` (4 ArchTest fitness functions + fixed the inverted layer-doc in `src/server/shared/CLAUDE.md`). **062 ✅ committed** `917322e37`.
- **Deferred (do NOT lose)**: 026 Finance rename (Dataverse `sprk_analysistool.sprk_handlerclass` pre-check — offline-blocked); 023+030 web-resource MSAL token flow needs LIVE-Dataverse validation + app-reg prereqs (SPA redirect URI + `user_impersonation` consent) + form-library registration of `sprk_bff_auth.js` + live `Cors:AllowedOrigins` origins (see `notes/task-023-notes.md`); 033 deferred 43 `AnalysisServicesModule` `Console.WriteLine` (needs /conflict-check) + 7 email-at-Info sites (active worktrees); 032 PDF-parse smoke test; `#772` deferred package majors; residual trivial stale-comment refs.
- **Program result**: aggregate was **F** (gating cap on the live Finance anonymous-write); **023 CLOSED it server-side** → the F is lifted (pending live web-resource validation). Maintainability mean ≈ C+. Formal re-score at 090.

### What happened since init (2026-08-06 → 2026-08-13) — all PLANNING, no execution
- **Initialized** via design-to-spec → project-pipeline (Project #741 under Epic #427; INDEX.md row; NG1 Idea #742). 27 tasks.
- **BFF workstream handoff + Fable verification** integrated: relocated BFF design → `workstreams/bff-api/design.md`; A/B tranche split (020→020+029, 021→021+028); §6 auth resolved to `@spaarke/auth`. 29 tasks.
- **Absorbed r1 deployment-complexity ask** (`notes/deployment-complexity-refactors-ask-2026-08-12.md`) → **Phase 6**; tasks **017** (#1 KV assess), **060** (#3a app-reg drop), **061** (#2 config validation), **062** (#4 Graph app-role constants). #3 SPLIT after Fable grounding (#3a=060 clean; **#3b shared-lib ClientSecret→MI migration → NG1/task-011**). NG1 reframed: deferred → **assess-then-decide (task 011)**. 33 tasks.
- **BFF Auth Surface Map** (owner-requested de-risk): task **019** + `notes/bff-auth-surface-map.md` (Fable). Gates 023/060/061/062. 34 tasks.
- **Resource/secret naming standardization** (owner, productization): task **063** + extended 017; r3 owns standard+gate, **r1 owns apply+live-env remediation** (handback in assessment doc). 35 tasks.
- **Live doc landmine FIXED** (committed): 3 docs told operators `Dataverse-ClientSecret` was safe to remove → crashes BFF. Corrected.
- **Portal confirmations**: A resolved (`BFF-API-ClientSecret`=`1e40baad`), B resolved (no separate `Dataverse-ClientSecret`), #3 CI resolved (OIDC). Remaining self-resolve in tasks (062 role census, PowerBi SP, email Service Endpoint).
- **NET10 (2026-08-14)**: merged net10 master (532 commits); BFF builds clean; baseline 44.96 MB. Integrated `notes/r3-handoff.md` (CVE-no-re-pin, #772 deferred-majors r3-owns via task 032, HELD pkgs, `DiGraphValidationTests` KEEP, ADR-010=153, demo/prod decommissioned→dev-only). **Fable re-verified ALL BFF/auth findings vs net10 HEAD → essentially all STILL HOLD** (§net10 HEAD Reconciliation in `workstreams/bff-api/design.md`); **#3b confirmed still needed**; resolved-by-master: MF-4 (022) + `56ae2188` stale refs (019); new: ~32 dead ServiceException catches (→020 optional). Pushed through `45a7eba51`.

### Critical Context
Standing quality PROGRAM, single worktree, surfaces = workstreams, assessment-first (Fable-verified, gating). Owner decisions live in `CLAUDE.md` §Decisions Made. **Nothing has been executed** — all 35 tasks are 🔲. **@spaarke/auth (ADR-028)** for auth; **#3b credential migration** is the sensitive one (identity-attribution change) on the NG1/011 track; **`BFF-API-ClientSecret`** = 1 KV secret / 5 config keys / 9 consumers (never-remove). Reference docs: `notes/bff-auth-surface-map.md`, `notes/deployment-refactors-assessment-2026-08-12.md`, `workstreams/bff-api/design.md`. **NOTE**: no "daily briefing" work in this project (confirmed 2026-08-13 — that's a different `spaarke-daily-update-service` worktree).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Current Step
*No active task*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-08-06: Finance auth via `@spaarke/auth` (not HMAC) — Reason: owner directive, canonical ADR-028
- 2026-08-06: Assessment-first (Fable-verified) is the gating deliverable — Reason: can't task remediation against un-verified findings
- 2026-08-06: Initialize-only (no auto-execute) — Reason: operator opt-in required for Workflow assessments

---

## Next Action

**Next Step**: Portfolio registration (Epic #427) + INDEX row + NG1 Idea, then Phase 0 task 001 (rubric authoring).

**Pre-conditions**: Epic #427 exists; no orphan R3 Issue.

**Key Context**:
- Refer to `spec.md` FR-01..FR-04 for Phase 0 deliverables
- Refer to `design.md` §5 (rubric D1–D11) + §6 (assessment method)

**Expected Output**: `docs/standards/CODE-QUALITY-RUBRIC.md`, `notes/SCORECARD.md`, `quality-assessment` Workflow, portfolio Issue.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-08-06
- Focus: Project initialization via /design-to-spec → /project-pipeline (initialize-only)

### Key Learnings
*None yet*

### Handoff Notes
See [`notes/SESSION-HANDOFF.md`](notes/SESSION-HANDOFF.md) for the read-first program handoff.

---

## Quick Reference

### Project Context
- **Project**: code-quality-and-assurance-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-028: Spaarke Auth v2 — all client→BFF auth via `@spaarke/auth`
- ADR-013: AI facade — CRUD uses `PublicContracts/`
- ADR-032: Null-object kill-switch — preserve verified seams
- ADR-038: Testing — KEEP categories, coverage = observation
- ADR-010 / ADR-022 / ADR-002

### Knowledge Files Loaded
- `.claude/constraints/bff-extensions.md`, `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read `notes/SESSION-HANDOFF.md` + `spec.md`
3. **Load task file**: `tasks/{task-id}-*.poml`
4. **Resume**: From the "Next Action" section

**Commands**: `/project-continue`, `/context-handoff`, "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
