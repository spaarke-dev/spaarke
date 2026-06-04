# sdap-bff.api-test-suite-repair — AI Context

> **Purpose**: This file provides context for Claude Code when working on the BFF test-suite repair project.
> **Always load this file first** when working on any task in this project.
> **Authority**: Per NFR-08, this file is the agent-visible source of truth at execution time. Conflicts between [`design.md`](design.md) and this file resolve in favor of this file.

---

## Project Status

- **Phase**: 🟢 Phase 0 Wave 1 complete; Phase 0 Wave 2 in progress (tasks 004, 005, 008)
- **Last Updated**: 2026-05-31 by Wave 0.2 task 004 (CLAUDE.md refinement post-Phase-0 Wave 1)
- **Current Task**: 004 (CLAUDE.md refinement) — concurrent with 005 (priority-order) + 008 (Phase 2+3 tier reconciliation)
- **Next Action**: After Wave 0.2 completes → dispatch Phase 1 Wave 1.1a + 1.1b (10 parallel-safe tasks across 2 sub-waves of 5; concurrency cap = 6)
- **Wave 1 outcome summary** (2026-05-31): all 5 Wave 1 agents completed; **MATERIAL DEVIATION FLAGGED** — 0 compile-broken files (design.md §3.2 expected 17/138 errors); +73 net runtime failures (342 vs design.md's 269); D-01 verdict = BUILD LOCAL; `Spe.Integration.Tests` still compile-broken (4 × CS1739 in `ExternalAccessIntegrationTests.cs`); CI gate `enforce_admins: false` confirmed. See [`baseline/README.md`](baseline/README.md) and `tasks/TASK-INDEX.md` Wave 1 outcomes section.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (238 lines; 30 FRs, 12 NFRs, 14 success criteria)
- [`design.md`](design.md) — Full design document (759 lines; locked decisions §4–§6)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan + WBS + discovered resources
- [`current-task.md`](current-task.md) — Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker + 13-wave parallel execution map

### Project Metadata
- **Project Name**: sdap-bff.api-test-suite-repair
- **Type**: Test repair + CI gate restoration + anti-drift governance
- **Complexity**: HIGH — 4 outcomes × 4 phases × ~58 tasks; 80–124 person-hours; 16–27 day wall-clock
- **Modules touched**: `tests/unit/Sprk.Bff.Api.Tests/`, `tests/integration/Spe.Integration.Tests/`, `.github/workflows/deploy-bff-api.yml`, `.claude/constraints/bff-extensions.md`, `.github/pull_request_template.md`, `docs/procedures/`, root `CLAUDE.md` §10
- **Sub-agent write boundary**: Tasks 080 + 083 (`.claude/` + root `CLAUDE.md`) are main-session-only sequential

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md AND design.md** — `design.md` has the "why" (§4 resolved decisions, §6 binding rules); `spec.md` has the "what" (FRs, NFRs, success criteria)
4. **Load the relevant task POML** from `tasks/` based on current work
5. **Apply ADRs** listed in [Applicable ADRs](#applicable-adrs) (loaded automatically via `adr-aware`)
6. **Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** — binding for every BFF-touching task per root CLAUDE.md §10

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md). The `current-task.md` file is the authoritative resume point.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in `current-task.md`
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 for FULL-rigor tasks
- ✅ Progress is recoverable after compaction
- ✅ **NFR-09 verification**: `<repair-not-rewrite>true</repair-not-rewrite>` is checked before starting work

**Bypassing this skill leads to**:
- ❌ Missing repair-not-rewrite binding (NFR-09)
- ❌ Missing ADR constraints
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates
- ❌ Triage taxonomy violations (forgetting `[Trait("status", …)]`)

### Parallel Task Execution

Tasks within a wave run in parallel. **For this project, the parallel-task structure is explicit**:

- **Phase 0 Wave 1**: 5 agents (001, 002, 003, 006, 007) — ✅ complete 2026-05-31
- **Phase 0 Wave 2**: **3 agents (004, 005, 008)** — task 008 added 2026-05-31 to absorb the +73 net failures + 4 × CS1739 integration-tests fix into Phase 2+3 tier scope per owner directive (TASK-INDEX.md now totals 59 tasks, was 58)
- **Phase 1 Wave 1**: 10 parallel-safe tasks split into 2 sub-waves of 5 (6-agent concurrency cap)
- **Phase 2+3 Waves 1–5**: 6 agents each (cap)
- **Phase 4 Wave 1**: 4 agents (081, 082, 084, 085) + 2 main-session-sequential (080, 083)

Send one message with multiple Skill tool invocations — each call invokes `task-execute` with a different task POML.

**Parallelism safety contract** (TASK-INDEX.md enforces): a task is `parallel-safe: true` if and only if its `<relevant-files>` list has zero intersection with the `<relevant-files>` of every other `parallel-safe: true` task in the same wave. Verify before dispatching; if intersection found, split or downgrade the wave.

**Sequential gates** (correctly NOT parallelized):
1. **NFR-07 anti-parallelism guard** — Phase 1 tasks 018, 019 (CustomWebAppFactory.cs) run ISOLATED
2. **Sub-agent write boundary** — Phase 4 tasks 080, 083 (`.claude/` paths) are main-session-only
3. **Verification gates** — 014, 023, 032, 074, 086 verify upstream outcomes

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files (most Phase 2+3 tier-batch tasks), Claude Code MUST:

1. **Decompose into dependency graph** — group files by test cluster; identify shared dependencies
2. **Parallelize when** files are in different test classes with no shared assertion helpers
3. **Serialize when** the batch ships a shared helper used by other tests in the same batch
4. **Touch `CustomWebAppFactory.cs` in isolation ONLY** — never in a parallel batch

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints (NON-NEGOTIABLE)

Binding rules from [`spec.md`](spec.md) NFRs + [`design.md`](design.md) §4 resolved decisions + §6 binding rules.

### NEGATIVE rules (MUST NOT)

- ❌ **NFR-01** — MUST NOT modify production code: `src/`, `power-platform/`, `infra/`, `scripts/`. If a failing test reveals a production bug, file the bug + mark test `[Trait("status", "real-bug-pending-fix")]`; separate PR/project fixes production
- ❌ **NFR-02** — MUST NOT rewrite tests. >50% line replacement requires §4.8 escalation BEFORE work proceeds. Code review rejects unescalated >50% replacements. Hard limit: ≤5% of touched files escalated; if exceeded, project pauses for design-review (signals repair-not-rewrite thesis is wrong)
- ❌ **NFR-03** — MUST NOT increase BFF DI registration count via test scaffolding (preserves ADR-010 baseline)
- ❌ **§4.5** — MUST NOT rewrite `CustomWebAppFactory.cs`. Extend only (additive config dictionary entries + `services.RemoveAll<IHostedService>()` calls); no method signatures changed
- ❌ **§4.3 / FR-NFR-10** — MUST NOT leave any test in `Failed` state at project close. Final end-states per §6.2: `repaired` / `real-bug-pending-fix` / `flaky-quarantined` / archived
- ❌ **NFR-06** — MUST NOT silently delete tests. Archive via rename to `*.cs.archived-YYYY-MM-DD` (precedent: `JobProcessorTests.cs.archived-2025-10-14`)
- ❌ **§4.8 hard limit** — MUST NOT bypass the rewrite escalation procedure. Code review rejects diffs with >50% line replacement and no `escalations/rewrite-request-T-XX-FileName.md` record

### POSITIVE rules (MUST)

- ✅ **NFR-09** — MUST declare `<repair-not-rewrite>true</repair-not-rewrite>` in every task POML metadata; task-execute verifies before starting work
- ✅ **§6.3** — MUST cite [`design.md`](design.md) §3 measured numbers (5,215 / 4,844 / 269 / 17), NOT the [`bff.api-repair-overview.txt`](bff.api-repair-overview.txt) stale "283 failures" framing
- ✅ **§6.2** — MUST tag every touched test with `[Trait("status", …)]` from the taxonomy:
  - `repaired` — Pass; test asserts current behavior; signature/namespace/assertion updated
  - `real-bug-pending-fix` — Skip; test is CORRECT; production has a bug; filed in `ledgers/real-bug-ledger.md`
  - `flaky-quarantined` — Skip; non-deterministic with environmental cause; quarantined with fix-by date in `ledgers/flaky-ledger.md`
  - (or removed from suite via §6.5 archive: `archived-duplicate`, `archived-dead-target`, `archived-rewrite`)
- ✅ **NFR-07 (anti-parallelism guard)** — MUST run `CustomWebAppFactory.cs` changes in isolation; never concurrent with other repair tasks
- ✅ **§6.4** — MUST run full suite before AND after any `CustomWebAppFactory.cs` change
- ✅ **NFR-04** — MUST get owner sign-off before exceeding 10 archives in a single phase
- ✅ **NFR-11** — Compile-broken files must compile cleanly under `-warnaserror` after repair (matches CI gate's build requirement)
- ✅ **NFR-12** — Parallelism is the project's structural advantage. Tasks within a phase run concurrently per the [`plan.md`](plan.md) wave structure; serial execution is the fallback only if concurrent agent capacity is unavailable

### Pre-merge checklist (every PR in this project)

Per [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md):
- [ ] Diff touches `tests/` only — NO files in `src/`, `power-platform/`, `infra/`, `scripts/`
- [ ] Every touched test has `[Trait("status", …)]` per §6.2 taxonomy
- [ ] If any file's diff is >50% line replacement → `escalations/rewrite-request-*.md` exists with owner sign-off
- [ ] If archive count >10 in this phase → owner approval recorded in `ledgers/archive-ledger.md`
- [ ] No new DI registrations in tests (NFR-03)
- [ ] `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -warnaserror` returns 0 errors
- [ ] Cluster regression check (`dotnet test --filter "FullyQualifiedName~<touched-area>"`) zero failures

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-05-31**: **Project initialized via `/project-pipeline`.** Master pulled (`f5768d87 docs:`); build verified passing (0 errors, 17 warnings — pre-existing Kiota CVE + obsolete API + CS1998). Branch: `work/sdap-bff.api-test-suite-repair`. Pipeline stops after task generation (per owner decision); user starts Phase 0 task 001 in a fresh session.

- **2026-05-31**: **Researcher subagent deferred to Phase 0 task 003** (per owner decision). Verdict on Microsoft.Extensions.AI.Testing maturity is captured in `decisions/D-01-async-enumerable-helper.md` during task execution, not during pipeline. P1.B task acceptance gated on verdict file presence.

- **2026-05-31**: **Branch convention**: kept existing `work/sdap-bff.api-test-suite-repair` instead of pipeline-default `feature/...` (per owner decision). Matches predecessor `sdap-bff-api-remediation-fix` convention.

- **2026-05-31**: **Parallelism-first task structure** (per owner direction added during planning): 58 of 63 tasks are parallel-safe across 13 waves; 5 sequential gates only where binding rules require (NFR-07 factory anti-parallelism, sub-agent write boundary for `.claude/` writes, upstream-verification gates). **(2026-05-31 update: total tasks now 59 after Wave 1 added task 008; 54 of 59 parallel-safe; 5 sequential gates unchanged.)**

- **2026-05-31** (Phase 0 task 001 baseline captured): **Sprk.Bff.Api.Tests measured baseline = 6,021 total / 5,572 passed (92.5%) / 342 failed (5.7%) / 107 skipped / 0 compile-broken files / 17 warnings only.** Duration 1m 13s on RalphSchroeder Windows dev box (Release). **MATERIAL DEVIATION from design.md §3** (5,215 / 4,844 / 269 / 17 / 138): +806 total, +728 passed, +73 failed, **−17 compile-broken files / −138 errors** (build now succeeds with 0 errors). Hypothesis: §5.6 namespace fixes + downstream compile-recovery were already applied to the worktree between 2026-05-30 design baseline and 2026-05-31 project init (consistent with task 007 NO-OP outcome — see Implementation Notes). Reference: [`baseline/test-baseline-2026-05-31.trx`](baseline/test-baseline-2026-05-31.trx) + [`baseline/README.md`](baseline/README.md). **Implications**: (a) Phase 1 P1.A (FR-05 compile recovery — tasks 010–014) scope is largely absorbed; tasks 010–014 are scope-revised to "verify clean compile + capture runtime delta"; (b) +73 net failures absorbed into Phase 2+3 tier scope via the newly-added Wave 0.2 task 008. **Per §6.3 binding rule**: all Phase 1+ tasks MUST cite these 2026-05-31 numbers, not design.md §3's 2026-05-30 figures.

- **2026-05-31** (Phase 0 task 002 integration baseline): **`Spe.Integration.Tests` is still compile-broken** — 4 × CS1739 in `tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs` (lines 113, 378, 398, 420). Root cause: `InviteExternalUserRequest.ContactId` named argument was renamed/removed; mechanical signature drift matching design.md §3.2 CS1739 pattern ("Parameter renamed"). **No runtime TRX produced** — fallback file captured: [`baseline/integration-build-errors-2026-05-31.txt`](baseline/integration-build-errors-2026-05-31.txt) (5,875 bytes). **Implication**: FR-13 / Phase 1 P1.E1 (task 024) MUST repair this CS1739 cluster as Step 1 before producing `integration-test-triage.md`. Task 024 is scope-extended 2026-05-31 to absorb this fix.

- **2026-05-31** (Phase 0 task 003 D-01 verdict): **BUILD LOCAL** — no `Microsoft.Extensions.AI.Testing` NuGet package exists as of 2026-05-31 (NuGet search returned 0 matches, prerelease inclusive). Microsoft's `TestChatClient` reference impl is internal-only to the dotnet/extensions repo (`test/Libraries/Microsoft.Extensions.AI.Abstractions.Tests/TestChatClient.cs`), not redistributed. **§5.1 criteria mapping**: (a) Stable: ❌ N/A (no package); (b) IChatClient streaming mocks: ⚠️ source-internal only; (c) Referenced in MS samples: ✅; (d) NSubstitute/Moq compatible: ✅. Reference: [`decisions/D-01-async-enumerable-helper.md`](decisions/D-01-async-enumerable-helper.md). **Implication for P1.B1 (task 015)**: hand-roll `tests/unit/Sprk.Bff.Api.Tests/Mocks/AsyncEnumerableHelpers.cs` + optional `FakeChatClient` companion, mirroring Microsoft's callback-property pattern (`GetStreamingResponseAsyncCallback` of type `Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>`); NO `<PackageReference Include="Microsoft.Extensions.AI.Testing">` added. ~4-6h effort estimate from §5.1 stands.

- **2026-05-31** (Phase 0 task 006 D-02..D-06 decisions captured): All 5 §5 locked decisions written verbatim-bounded to `decisions/`:
  - **[D-02 CI gate strict](decisions/D-02-ci-gate-strict.md)** (§5.2): Full `enforce_admins: true` on all 3 status checks + `skip-tests` workflow_dispatch removed + documented emergency procedure (owner-only approver + 5-business-day follow-up clause). Binds: FR-09 / FR-10 / FR-11 / FR-12 (tasks 020-023).
  - **[D-03 integration in scope](decisions/D-03-integration-in-scope.md)** (§5.3): `Spe.Integration.Tests` IN SCOPE — Phase 0 baseline + Phase 2/3 P23.I repair (+12-20h effort). Binds: FR-01 / FR-13 / FR-18 (tasks 002, 024, 060-063).
  - **[D-04 triage authority](decisions/D-04-triage-authority.md)** (§5.4): Agent judges per-file repair-vs-archive, strictly bounded by §6 binding rules + §4.8 escalation; owner reviews per-phase exit ledger (NOT per-decision PRs). Binds: §6.2 taxonomy + §4.8 escalation + NFR-04 archive ceiling + FR-27 exit ledger.
  - **[D-05 anti-drift no CI script](decisions/D-05-anti-drift-no-ci-script.md)** (§5.5): Anti-drift via `bff-extensions.md` "Test update obligation" + PR template question + code review checklist line; NO CI script (avoids PR-process burden + false positives). Binds: FR-22 / FR-23 / FR-24 / FR-25 (Phase 4 tasks 080-083).
  - **[D-06 keep namespace fixes](decisions/D-06-keep-namespace-fixes.md)** (§5.6): KEEP the 3 in-progress namespace fixes; task 007 commits as project's first commit (**operationally N/A** — confirmed by task 007 NO-OP outcome 2026-05-31; the fixes were not in the worktree at task 007 execution time, hypothesis: already merged upstream into baseline).

- **2026-05-31** (Phase 0 task 007 namespace fixes commit): **NO-OP** — `git status` showed 0 modified files in `tests/unit/Sprk.Bff.Api.Tests/` at task execution time. §5.6 lock-in is operationally N/A. No commit performed. This is the load-bearing data point explaining the 0-compile-error baseline — the namespace fixes (and likely follow-on compile recovery) are already in the working tree's baseline state.

- **2026-05-31** (Phase 0 task 008 added — Wave 0.2 absorbs +73 net failures): Task 008 (TRX parsing + Phase 2+3 tier reconciliation) added to Wave 0.2 per owner directive after Wave 1's MATERIAL DEVIATION was reflected back. Task 008 will: (a) parse `baseline/test-baseline-2026-05-31.trx` to cluster 342 failures by tier (HIGH/MEDIUM/INTEGRATION/LOW); (b) write `notes/handoffs/phase23-scope-delta-2026-05-31.md`; (c) update Phase 2+3 task POMLs (030-074) `<notes>` sections to reference the new failure inventory. Disjoint write path from tasks 004 + 005 — all 3 Wave 0.2 agents run in parallel.

- **2026-05-31** (Phase 1 Wave 1.2 task 014 — post-Wave-1.1a baseline captured): **Sprk.Bff.Api.Tests measured post-Wave-1.1a = 6,020 total / 5,627 passed (93.5%) / 284 failed (4.7%) / 109 skipped / 0 build errors / 17 warnings.** Duration 1m 14s on RalphSchroeder Windows dev box (Release). **Delta vs. Wave 1 baseline (342 failures): −58 (−17.0%)**: 53 from task 011's Communications cluster repair (AssociationMapping 29 + DataverseRecordCreation 23 + ArchivalFlow 1 → all pass); 2 from task 012's RB-T012-01 skip-tagging (Failed → Skipped); 3 net residual from minor namespace fluctuation. Tasks 010 + 013 contributed minimal runtime delta (trait-tagging only). Build clean: 0 errors / 17 warnings unchanged from Wave 1 baseline. **Top-5 remaining hot clusters**: Api.Ai.* (89, 31.3%), Integration.Workspace.* (54, 19.0%), Top-level *EndpointTests (39, 13.7%), Services.Ai.* non-Safety (23, 8.1%), Services.Ai.Safety.* (19, 6.7%) = 78.9% of remaining 284. All top-5 already have absorbing Phase 2+3 tasks per task 008 reconciliation; no new tasks required. References: [`baseline/post-wave1.1a-runtime-2026-05-31.trx`](baseline/post-wave1.1a-runtime-2026-05-31.trx) + [`baseline/post-wave1.1a-delta-2026-05-31.md`](baseline/post-wave1.1a-delta-2026-05-31.md). **Per §6.3 binding rule**: Phase 2+3 tasks may cite 342 (Phase 0 anchor) or 284 (post-Wave-1.1a Phase 2+3 starting state); both are authoritative — NEVER cite design.md §3's stale 269.

<!-- Append new decisions during execution; preserve all history -->

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Sub-agent write boundary** (root CLAUDE.md §3): Phase 4 task 080 (`.claude/constraints/bff-extensions.md`) and task 083 (root `CLAUDE.md`) are main-session-only. Sub-agents will fail with "Edit denied" if dispatched against these — that's the boundary working correctly. Tasks 081 (`.github/pull_request_template.md`) and 082 (`docs/procedures/`) ARE agent-safe.
- **CustomWebAppFactory anti-parallelism (NFR-07)**: tasks 018 + 019 are the ONLY Phase 1 tasks that touch `CustomWebAppFactory.cs`. They run in ISOLATION — Wave 3 — after Waves 1+2 complete. NO concurrent work permitted while these tasks are in flight.
- **Archive naming**: `*.cs.archived-YYYY-MM-DD` (precedent: `tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14`). Rename only — never delete-from-disk (NFR-06).
- **Triage taxonomy**: 4 final end-states (`repaired`, `real-bug-pending-fix`, `flaky-quarantined`, archived) + 2 intermediate states (`in-progress-task-T-XX`, `blocked-on-cluster-fix`) that MUST resolve before phase exit. `Failed` is FORBIDDEN at project close.
- **Dependabot coordination**: PRs #287 (FluentAssertions), #265 (coverlet.collector), #236 (Microsoft.AspNetCore.Mvc.Testing) touch test infrastructure. Coordinate merge timing with Phase 2+3 P23.I (integration tests) — don't merge during active P23.I work.
- **Sibling-project coordination**: Communications-related test files (ArchivalFlow, AssociationMapping, AttachmentValidation, CommunicationService, DataverseRecordCreation, EmailAttachmentExtraction) have HIGHEST sibling-coordination risk per design §2.3. Assign owner before touching (Phase 1 task 011 + Phase 2+3 tasks 055/056).
- **First commit of the 3 in-progress namespace fixes** (Phase 0 task 007 per §5.6): these are LEGITIMATE drift fixes from prior work. Reverting would be process theater. Task 007 commits them as the project's first body of work. **(2026-05-31 update: task 007 executed as NO-OP — `git status` showed 0 modified test files at execution time; the §5.6 fixes are operationally absent from the worktree. Hypothesis: already merged upstream into the baseline. See task 007 execution log in `current-task.md`.)**
- **Phase 1 P1.A compile recovery scope is largely absorbed** (2026-05-31): design.md §3.2 expected 17 files / 138 compile errors; task 001 measured 0 errors / 17 warnings. Tasks 010–014 are scope-revised in TASK-INDEX.md to **"verify compile clean already; verify + test-level repair"** mode. The expected 5-8h of P1.A compile-recovery effort is largely saved; however, the 17 files' test logic (constructors, assertions) may still need test-level repair if individual runtime failures within those files surface during Phase 2+3 tier work.
- **Phase 1 P1.E task 024 scope-extended 2026-05-31**: the 4 × CS1739 fix in `tests/integration/Spe.Integration.Tests/ExternalAccess/ExternalAccessIntegrationTests.cs` is now task 024's Step 1 (before producing `integration-test-triage.md`). Mechanical signature drift; `InviteExternalUserRequest.ContactId` was renamed/removed. Per design.md §3.2 estimate: 15-30 min for this one file.
- **+73 net runtime failure delta** (Wave 1 vs. design.md §3): captured 2026-05-31 (342 vs 269). The +73 are absorbed into Phase 2+3 tier scope via new Wave 0.2 task 008 (TRX parsing + tier reconciliation). Task 008 updates POMLs 030-074 `<notes>` sections with the actual 2026-05-31 cluster sizes — no re-generation of task files (additive `<notes>` only, preserving FR-NFR-09 `<repair-not-rewrite>true</repair-not-rewrite>` in every existing POML).
- **CI gate confirmed broken** (2026-05-31): `enforce_admins.enabled: false` observed on master branch protection — matches design.md §5.2 "fictional gate" hypothesis exactly. Phase 1 P1.D task 020 (FR-09) flips this to `true`; the snapshot in `baseline/ci-gate-snapshot-2026-05-31.json` is the "before" state.
- **D-01 verdict consequence for task 015 (P1.B1)**: hand-roll path locked in — author `AsyncEnumerableHelpers.cs` + optional `FakeChatClient` mirroring Microsoft's `TestChatClient` callback-property pattern. NO `<PackageReference>` for `Microsoft.Extensions.AI.Testing` (doesn't exist on NuGet). Effort ~4-6h per §5.1. The callback pattern enables a mechanical swap to a Microsoft-shipped helper if one ever ships (reassessment trigger floor: 2027-05-31 per D-01).

<!-- Append notes during execution; useful for handoffs across compaction boundaries -->

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) | Minimal API + Workers | Tests target this pattern; CI gate enforcement supports it |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) | SpeFileStore facade | Affects SPE-related test repair (Integration + file-operation tests) |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI minimalism | **NFR-03 binding** — tests must NOT increase BFF DI registration count |
| [ADR-013 refined](../../.claude/adr/ADR-013-ai-architecture.md) | AI extends BFF (2026-05-20) | AI-coupled test repair stays in-process; no extraction to external test projects |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Spaarke Auth v2 | Existing `FakeAuthHandler` pattern preserved; no parallel auth fake |
| [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) | BFF Publish Hygiene | CI gate restoration aligns with size-baseline ratchet (informational) |
| ADR-002 | Plugin assembly size | Awareness only (no plugin changes in scope) |

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist; **Phase 4 task 080 extends this with "Test update obligation"**
- [`.claude/constraints/api.md`](../../.claude/constraints/api.md) — for API endpoint test repair (LOW-tier P23.L tasks)
- [`.claude/constraints/ai.md`](../../.claude/constraints/ai.md) — for AI service test repair (P23.A streaming, P23.M Ai/Chat tasks)

### Applicable Skills

- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — invoked for every task; auto-runs adr-aware + adr-check + code-review at Step 9.5
- [`adr-aware`](../../.claude/skills/adr-aware/SKILL.md), [`adr-check`](../../.claude/skills/adr-check/SKILL.md), [`code-review`](../../.claude/skills/code-review/SKILL.md) — quality gates
- [`doc-drift-audit`](../../.claude/skills/doc-drift-audit/SKILL.md) — Phase 4 validation that governance updates are consistent
- [`repo-cleanup`](../../.claude/skills/repo-cleanup/SKILL.md) — wrap-up task 090
- [`push-to-github`](../../.claude/skills/push-to-github/SKILL.md) — incremental commits per phase exit
- [`context-handoff`](../../.claude/skills/context-handoff/SKILL.md) — every 3 steps or >60% context

### Knowledge / Patterns

- [`tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs`](../../tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs) (171 LOC) — **extend; do NOT rewrite** (§4.5)
- [`tests/unit/Sprk.Bff.Api.Tests/Mocks/FakeGraphClientFactory.cs`](../../tests/unit/Sprk.Bff.Api.Tests/Mocks/FakeGraphClientFactory.cs) — fake pattern; replicate for new fakes (e.g., `AsyncEnumerableHelpers.cs` in P1.B)
- `tests/unit/Sprk.Bff.Api.Tests/JobProcessorTests.cs.archived-2025-10-14` — archive naming convention precedent
- [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md) — existing test conventions; Phase 4 task 082 references test-update obligation here
- [`projects/sdap-bff-api-remediation-fix/`](../sdap-bff-api-remediation-fix/) — predecessor patterns (Decisions Made format, TASK-INDEX style, Phase 1 inventory findings approach)
- [`projects/sdap-bff-api-remediation-fix/CLAUDE.md`](../sdap-bff-api-remediation-fix/CLAUDE.md) — reference for project CLAUDE.md decisions-tracking pattern

### Related Projects (coordination required)

| Project | Risk | Coordination action |
|---|---|---|
| `ai-spaarke-action-engine-r1` | HIGH — new BFF endpoints/services | Phase 0 task 005: priority-order sign-off + commitment to use test convention this project establishes |
| `ai-spaarke-insights-engine-r1` | MEDIUM — adds tests under `Services/Ai/` | Daily sync during Phase 2+3 P23.M; priority order sequences Insights-active files last |
| `x-email-communication-solution-r2` | MEDIUM — Communications test files in compile-broken set | Owner-aligned for Phase 1 task 011 + Phase 2+3 tasks 055, 056 |

### External Documentation

- `dotnet test` TRX format — used by Phase 0 task 001 baseline + Phase 4 task 084 validation
- `gh api repos/{owner}/{repo}/branches/master/protection` — used by Phase 1 task 020 (FR-09) and Phase 4 task 086 verification
- Microsoft.Extensions.AI documentation — Phase 0 task 003 researcher subagent investigates `Microsoft.Extensions.AI.Testing` maturity
- xUnit Trait taxonomy — used in §6.2 status traits

---

*This file should be kept updated throughout project lifecycle. The Decisions Made and Implementation Notes sections grow during execution and are referenced for context handoffs.*
