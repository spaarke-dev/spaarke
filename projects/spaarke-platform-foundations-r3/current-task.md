# Current Task State — Spaarke Platform Foundations (R3)

> **Last Updated**: 2026-06-21 (by context-handoff before /compact)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-platform-foundations-r3` |
| **Branch** | `work/spaarke-platform-foundations-r3` — pushed to origin at `bb78dc9c9` (clean, no uncommitted work) |
| **Status** | **27 of 69 tasks complete ✅ + 1 blocked-operator (071) — ~41%** |
| **Build** | ✅ `dotnet build Spaarke.sln` 0 errors / 16 pre-existing warnings |
| **Tests** | ~150+ unit tests added across Spaarke.Scheduling.Tests + Sprk.Bff.Api.Tests; all green |
| **BFF publish-size** | 46.20 MB (~+0.55 vs 45.65 baseline; 60 MB ceiling intact) |
| **CVE** | No new HIGH (pre-existing Microsoft.Kiota.Abstractions 1.21.2 unchanged) |
| **Next Wave** | **Wave 10 — dispatch 024 + 025 + 041 in parallel via Agent tool** |

### Next Action (EXPLICIT)

Resume autonomous parallel-wave execution. Dispatch Wave 10 = three sub-agents in parallel:

1. **Task 024** — `migrate-sprk-analysisplaybook-config-schedule.poml` — verify task 023's PlaybookSchedulerJob reads `sprk_analysisplaybook.sprk_configjson` correctly; this is a small verification/touch-up task. Per FR-2.8 the scheduler is a thin adapter; no data migration needed. May already be ~90% done — see `projects/spaarke-platform-foundations-r3/notes/task-024-config-schedule-verification.md`.
2. **Task 025** — `admin-endpoints-integration-tests.poml` — end-to-end integration tests for `/api/admin/jobs/*` + scheduler against test environment. Covers AC-2.1, AC-2.3, AC-2.4, AC-2.5, AC-2.6, AC-2.7.
3. **Task 041** — `lookupusermembership-node-executor.poml` — implement `LookupUserMembershipNodeExecutor` (handles ActionType=52 added in task 040). In-process call to `IMembershipResolverService`; bind IDs to OutputVariable. Singleton-with-Scoped DI pattern (CreateScope per execution — cite AgentServiceNodeExecutor).

Pattern: ONE message with THREE `Agent` tool calls (subagent_type=claude). Each agent gets a self-contained brief referencing the POML + dependencies + acceptance criteria. After all three return, verify combined build, then dispatch Wave 11 (042 + 043 + 050).

### Files Modified This Session (already committed + pushed)

Last 4 commits (oldest → newest):
- `45bc43bcf` — tasks 001-013 — Phase 1 + P2 foundation (7/69)
- `e0ff2ffc1` — tasks 005-070 — Phase 1 finish + P2 entities + P4 Membership services (16/69 + 1 deferred)
- `e63c88944` — tasks 020-036 — P3 admin endpoints + P4 Membership endpoints (22/69)
- `bb78dc9c9` — tasks 017-040 — Wave 9 + main-session ADRs (27/69)

### Critical Context

This is an autonomous parallel-wave execution of a 69-task project. User invoked `/project-pipeline --parallel-optimized` then said "continue - execute parallel tasks where possible and run autonomously". Pattern is:

1. **Main session orchestrates waves** (3 agents per wave is the sweet spot — user rejected 5-agent waves in Wave 5; 3 agents has run cleanly since Wave 6).
2. **Each Agent dispatch** = self-contained brief with POML path + context-file order + step-by-step instructions + acceptance criteria + constraints + report format. Each agent reads its POML + CLAUDE.md + spec.md + relevant pattern files + does the work + updates POML status + TASK-INDEX row + reports back.
3. **Main session does build-verify after each wave** + checkpoints to git per ~7-10 tasks.
4. **Sub-agents CANNOT write to `.claude/` paths** — main session handles ADRs + patterns directly (tasks 017, 037 done; tasks 066, 100, 101 still pending for later phases).

User's 12 owner clarifications (Q1-Q6 + D1-D4 + task-032 decision) are baked into every relevant agent brief.

---

## Wave-by-Wave Status

| Wave | Tasks | Status | Notes |
|---|---|---|---|
| 1 | 001 + 010 | ✅ | Foundation — TemplateEngine default helper + Spaarke.Scheduling scaffold |
| 2 | 002 + 011 + 012 | ✅ | joinIds + IScheduledJob contracts + MembershipOptions |
| 3 | 003 + 004 + 013 | ✅ | Playbook ?? migrations + ScheduledJobHost |
| 4 | 005 + 014 + 031 + 032 | ✅ | Unrendered-template warning + retry/idempotency + IdentityNormalization + OrganizationMembershipResolver |
| 5 (interrupted) | 030 (partial) + 015 + 071 | ✅ partial | User rejected 5-agent batch; 015 + 071 (Bicep, blocked-operator) completed |
| 6 | 030 + 016 + 070 | ✅ | MembershipFieldDiscovery + 2 more Dataverse entities deployed |
| 7 | 033 + 034 + 020 | ✅ | MembershipResolver + DTO + admin GET endpoints |
| 8 | 021 + 035 + 036 | ✅ | POST /trigger + membership endpoints |
| 9 | 022 + 023 + 040 | ✅ | history/enable/disable + PlaybookSchedulerJob + ActionType enum |
| Main-session | 017 + 037 | ✅ | ADR-036 + ADR-034 (both .claude/ + docs/ versions + INDEX) |
| **10 NEXT** | **024 + 025 + 041** | 🔲 | Config verification + admin integration tests + LookupUserMembership node executor |
| 11 | 042 + 043 + 050 | 🔲 | Canvas mapping + form component + first playbook migration |
| later | P6 (051-053) + P6.5 (054-056) + P7 (060-066) + P8 (080-087) + P9 (090-095) + P10 (100-104) + P11 (110) | 🔲 | Remaining ~30 tasks |

### Tasks by current status

✅ **27 complete**: 001, 002, 003, 004, 005, 010, 011, 012, 013, 014, 015, 016, 017, 020, 021, 022, 023, 030, 031, 032, 033, 034, 035, 036, 037, 040, 070

❌ **1 blocked-operator**: 071 — Service Bus topic Bicep authored + `az bicep build` clean; deferred to operator deploy per runbook at `projects/spaarke-platform-foundations-r3/notes/operator-followup-task071.md`. Unblocks downstream tasks 072, 073, 084 when deployed.

🔲 **41 pending**: 024, 025, 041, 042, 043, 050, 051, 052, 053, 054, 055, 056, 060, 061, 062, 063, 064, 065, 066, 072, 073, 080, 081, 082, 083, 084, 085, 086, 087, 090, 091, 092, 093, 094, 095, 100, 101, 102, 103, 104, 110

---

## Resumption Protocol

When user says "continue" or any work-related message after compaction:

1. **Read this file's Quick Recovery section** (top of file)
2. **Verify branch + build state**: `git status --short` (expect clean); `dotnet build Spaarke.sln -nologo -v q` (expect 0 errors)
3. **Dispatch Wave 10** via three parallel `Agent(subagent_type="claude")` calls with self-contained briefs for tasks 024, 025, 041
4. **After Wave 10 completes**: build verify → consider checkpoint commit → dispatch Wave 11 (042 + 043 + 050)
5. **Continue dependency graph** per [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) Parallel Execution Groups section

### How to construct Agent prompts (reference)

Each Agent dispatch needs:
- **Task POML path**: `projects/spaarke-platform-foundations-r3/tasks/{NNN}-{slug}.poml`
- **Context read order**: CLAUDE.md → spec.md (find specific FRs) → POML → constraints (`bff-extensions.md` for BFF tasks) → patterns → reference files (sibling implementations)
- **Owner clarifications** to honor: Q1 (fresh per-child correlationId), Q2 (fire-and-forget), Q3 (1-hop transitive), Q4 (sprk_organization for lawfirm), Q5 (extend PlaybookBuilder patterns), Q6 (existing SystemAdmin policy), D2 (single-row PlaybookSchedulerJob fan-out), D3 (Service Bus topic), task-032 decision (config-driven Lookup for org mapping)
- **Explicit step list** (mirror POML steps)
- **Acceptance criteria** (cite spec ACs)
- **Constraints**: bff-extensions §A pre-merge checklist, TreatWarningsAsErrors, no .claude/ writes, no --no-verify
- **Report format** (200-350 word cap; STATUS/FILES/TESTS/BUILD/PUBLISH/CVE/TASK-INDEX/NOTES)
- **Coordination notes** if siblings in same wave touch related files (e.g., 042 + 043 both extend PlaybookBuilder)

Past wave dispatches in conversation history are the best templates — search for `Agent(` calls in prior turns.

---

## Owner Clarifications (binding for all agent briefs)

| # | Decision | Impact |
|---|---|---|
| Phase 1D | In-scope for R3 (transitive memberships always built) | Tasks 054-056 must implement |
| AC-1A.5 | App Insights server-side request telemetry | Perf measured post-deploy, not via new instrumentation |
| H3 inventory | P7.0 task 060 produces inventory before P7.1 migration | Sequential, not parallel |
| Phase 2 | In-scope for R3 (full junction + topic + real recon) | Tasks 070-073, 080-087 must ship |
| D3 | Service Bus **topic** `sprk-membership-changes` (NOT queue, NOT reuse SBjp queue) | Topic + subscription-per-consumer |
| Q1 | Fresh `correlationId` per child playbook | PlaybookSchedulerJob records children in ResultJson |
| Q2 | Fire-and-forget event publishing | Nightly recon is backstop |
| Q3 | `includeRelated` 1 hop max | Multi-hop → 400 BadRequest |
| Q4 | `sprk_assignedlawfirm1/2` → `identityType=Organization` | NOT Contact as design.md showed |
| Q5 | Extend existing PlaybookBuilder componentry | H2 (091-093) must not invent new patterns |
| Q6 | Use existing `SystemAdmin` policy (`AuthorizationModule.cs:241`) | NOT new "PlatformAdmin" |
| task-032 | Option (b) config-driven Lookup for sprk_organization mapping | Operator sets `Membership:OrganizationLookup:UserLookupField` |

---

## Files Modified This Session (all committed + pushed)

- 4 commits on `work/spaarke-platform-foundations-r3` from `de96567d6` (project init) to `bb78dc9c9` (latest)
- ~150 files created/modified across:
  - `src/server/shared/Spaarke.Scheduling/` (new shared library)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/` (new namespace)
  - `src/server/api/Sprk.Bff.Api/Api/Admin/` (new namespace — JobsEndpoints, MembershipAdminEndpoints)
  - `src/server/api/Sprk.Bff.Api/Api/Membership/` (new namespace — MembershipEndpoints)
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` (MembershipModule, SchedulingModule, AnalysisServicesModule updates)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` (ActionType.LookupUserMembership=52)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` (replaces legacy PlaybookSchedulerService)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs` (default + joinIds helpers)
  - `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` (unrendered-template warning)
  - `tests/unit/Spaarke.Scheduling.Tests/` (new test project, 57+ tests)
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/` (new namespace, ~65 tests)
  - `tests/unit/Sprk.Bff.Api.Tests/Api/Admin/` (new namespace, ~30 tests)
  - `tests/unit/Sprk.Bff.Api.Tests/Api/Membership/` (new namespace, ~18 tests)
  - `.claude/adr/ADR-034-user-record-membership.md` + `ADR-036-background-job-infrastructure.md` (concise versions)
  - `docs/adr/ADR-034-*.md` + `ADR-036-*.md` (full versions)
  - `.claude/adr/INDEX.md` + `docs/adr/INDEX.md` (entries added)
  - `docs/data-model/sprk_backgroundjob.md` + `sprk_backgroundjobrun.md` + `sprk_userentityassociation.md` (3 new entity docs)
  - `scripts/Create-BackgroundJobEntity.ps1` + `Create-BackgroundJobRunEntity.ps1` + `Create-UserEntityAssociation.ps1` (3 idempotent Dataverse schema scripts; entities deployed to spaarkedev1)
  - `infrastructure/bicep/modules/membership-topic.bicep` + `customer.bicep` + `stacks/model2-full.bicep` (Service Bus topic provision — deployment deferred per task 071 operator-followup)
  - `docs/architecture/background-workers-architecture.md` (Phase 2 topic section added)
  - All `tasks/*.poml` POML files updated to `status: completed` with notes blocks
  - `tasks/TASK-INDEX.md` updated to 27/69 + 1 blocked-operator

---

## Project Progress

- **Phase Status**: P1 ✅ · P2 ✅ · P3 mostly ✅ (4/6 — 024+025 pending) · P4 ✅ · P5 started (1/4) · P6+P6.5 🔲 · P7 🔲 · P7.5 mostly (1/4 + Bicep deferred) · P8 🔲 · P9 🔲 · P10 🔲 · P11 🔲
- **Critical path**: 041 → 042 → 050 → 053 + 056 + 087 + 095 + 104 → 110
- **Estimated remaining wallclock with parallel execution**: ~5-8 hours of agent compute time (varies with task complexity); ~10-15 sequential days if user dispatches manually

---

## Recovery After Compaction — TL;DR

Read top of this file. Run `dotnet build Spaarke.sln`. If clean (expected), dispatch Wave 10 = three parallel `Agent(subagent_type="claude")` calls for tasks 024 + 025 + 041. Use prior wave dispatches in conversation history as prompt templates. Honor all 12 owner clarifications above.

---

*Initialized 2026-06-20 by `/project-pipeline`. Last updated 2026-06-21 by `/context-handoff` before `/compact`. Branch pushed to origin at `bb78dc9c9`.*
