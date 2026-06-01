# sdap.bff.api-test-suite-repair-r2 — AI Context

> **Purpose**: Project-scoped AI context for Claude Code when working on `sdap.bff.api-test-suite-repair-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 — Project Setup
- **Last Updated**: 2026-06-01
- **Current Task**: Not started — see [`current-task.md`](current-task.md)
- **Next Action**: Run `/task-create` (or continue `/project-pipeline` Step 3) to decompose plan into task files

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — AI-optimized specification (225 lines; 16 FRs / 11 NFRs / 14 SC)
- [`design.md`](design.md) — Full design document (402 lines)
- [`README.md`](README.md) — Project overview and graduation criteria
- [`plan.md`](plan.md) — Implementation plan with WBS + discovered resources
- [`current-task.md`](current-task.md) — **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Task tracker (created by `/task-create`)

### Predecessor Artifacts (load for calibration at every task)

- [r1 lessons-learned](../sdap-bff.api-test-suite-repair/notes/lessons-learned.md) — sibling-fixture pattern, cluster classification
- [r1 real-bug-ledger](../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) — the 20 entries r2 closes
- [r1 design](../sdap-bff.api-test-suite-repair/design.md) — inherited decisions

### Project Metadata

- **Project Name**: sdap.bff.api-test-suite-repair-r2
- **Type**: Closure / quality (production fixes + audits + pilots)
- **Complexity**: HIGH (5 HIGH-severity production fixes; 4-week parallel-track Phase 4)
- **Predecessor**: `sdap-bff.api-test-suite-repair` (r1, closed 2026-06-01)
- **Hard Deadline**: 2026-08-31

---

## Context Loading Rules

When working on this project, Claude Code MUST:

1. **Always load this file first** when starting work on any task
2. **Check `current-task.md`** for active work state (especially after compaction / new session)
3. **Reference `spec.md`** for FR / NFR / Success Criteria
4. **Reference `design.md`** for Locked Decisions D-01..D-06 and Phased Delivery detail
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware`):
   - **ADR-010** (DI minimalism) — DIRECTLY binding for RB-T028 cluster
   - **ADR-018** (kill switches) — DIRECTLY binding for RB-T028 cluster
   - ADR-001, ADR-007, ADR-008, ADR-013, ADR-021, ADR-022, ADR-028, ADR-029
7. **For production-fix tasks**: load r1's `real-bug-ledger.md` and locate the entry being closed
8. **For Phase 4 Track C tasks**: load `.claude/patterns/testing/*` + reference existing TimeProvider usage in `tests/unit/Sprk.Bff.Api.Tests/Services/Insights/Precedents/PrecedentProjectionSyncTests.cs` and `Services/Ai/Insights/Ingest/IngestOrchestratorTests.cs`

**Context Recovery**: see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via `task-execute` |
| "continue" / "keep going" / "next task" | Read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Parallel Task Execution

When tasks can run in parallel (no inter-task dependencies; disjoint file sets — see TASK-INDEX.md "Parallel Execution Groups"):
- Send ONE message with MULTIPLE Skill tool invocations (one per task)
- Each invocation calls `task-execute` independently with full context loading
- **Cap: 6 agents per wave** (per CLAUDE.md §5 / project-pipeline Step 5)
- **Build verification mandatory between waves**: `dotnet build src/server/api/Sprk.Bff.Api/` for any wave touching `.cs` files; STOP and report if it fails

### Sub-Agent Write Boundary (per CLAUDE.md §3)

**`.claude/` paths are MAIN-SESSION-ONLY** — sub-agents launched via Agent tool cannot write to skills, patterns, constraints, catalogs, agents, settings.

**In r2 this means**:
- Phase 5 task 081 (extend `.claude/constraints/bff-extensions.md` § F) MUST execute sequentially in main session — `parallel-safe: false`
- Phase 5 task 080 (update `docs/procedures/testing-and-code-quality.md`) is parallel-safe (`docs/`, not `.claude/`)
- Phase 4 Track E (anti-drift report) writes to `projects/.../notes/` / `baseline/` — parallel-safe

If a sub-agent reports "Edit denied on `.claude/...`" — that's the boundary working correctly. Main session picks up the task.

### Why This Matters

The `task-execute` skill ensures:
- ✅ Knowledge files loaded (ADRs, constraints, patterns, r1 ledger entries)
- ✅ Context tracked in `current-task.md`
- ✅ Proactive checkpointing every 3 steps
- ✅ Quality gates at Step 9.5 (`code-review` + `adr-check` for FULL-rigor tasks; PLUS security review for HIGH severity per NFR-03)
- ✅ Progress recoverable after compaction

---

## Multi-File Work Decomposition

**For tasks modifying 4+ files, Claude Code MUST:**

1. **Decompose into dependency graph**:
   - Group files by module/component
   - Identify which changes depend on others
   - Separate parallel-safe work from sequential work

2. **Delegate to subagents in parallel where safe**:
   - Use Agent tool with `subagent_type="general-purpose"`
   - Send ONE message with MULTIPLE Agent tool calls for independent work
   - Each subagent handles one module/component

3. **Parallelize when**:
   - Files in different modules → CAN parallelize
   - Files share no interfaces → CAN parallelize
   - Work is independent (no imports between files) → CAN parallelize

4. **Serialize when**:
   - Files share tight coupling (shared state, imports)
   - One file must be created before another uses it
   - Sequential logic required (e.g., RB-T028 cluster fix is one-edit-shared-by-many-callers)

**In r2 this means**:
- Phase 1 RB-T028 cluster fix is SEQUENTIAL (single registration-path edit; cluster exception under D-02)
- Phase 2 MED entries are mostly parallel (each fix in a different `Services/` file)
- Phase 3 LOW entries are mostly parallel (1-line fixes, disjoint files)
- Phase 4 tracks are fully parallel (independent deliverables, disjoint domains)

---

## Key Technical Constraints (from spec.md §Technical Constraints)

### Binding ADRs

- **ADR-010** (DI minimalism, NFR-03 in BFF) — DIRECTLY binding for RB-T028-03/04/05/06: conditional service registration + unconditional endpoint mapping is the root cause
- **ADR-018** (kill switches) — DIRECTLY binding for RB-T028 cluster: feature-flag application is the root-cause question
- **ADR-013 refined 2026-05-20** (AI extends BFF) — RB-T044-* + RB-T053-01 (AI/Safety/CapabilityRouter)
- **ADR-001** (Minimal API + Workers) — endpoint registration discipline
- **ADR-008** (endpoint filters) — auth patterns (RB-T028-06)
- **ADR-007** (SpeFileStore facade) — RB-T012-01 SessionRestoreService
- **ADR-021** (Fluent design / dark mode) — Phase 4 Track A PCF audit
- **ADR-022** (PCF platform libraries) — Phase 4 Track A React boundary
- **ADR-028** (Spaarke Auth v2) — RB-T028-06 Auth tests
- **ADR-029** (BFF Publish Hygiene) — Phase 4 Track D Coverlet must preserve baseline

### MUST Rules (NFRs)

- ✅ MUST cite ledger entry ID in production code change commits (NFR-04)
- ✅ MUST follow the test-update obligation per `.claude/constraints/bff-extensions.md` § F (codified by r1) when modifying production code
- ✅ MUST run triple-run validation before each phase exit (NFR-05)
- ✅ MUST get security review for HIGH severity entries (NFR-03)
- ✅ MUST declare `<production-fix-per-ledger>true</production-fix-per-ledger>` in task POMLs for production changes (NFR-09, NEW for r2)
- ❌ MUST NOT modify tests outside the resolved ledger entries' Skip → Pass scope or the TestClock PoC (NFR-01 — inverts r1)
- ❌ MUST NOT delete tests; archive via `*.cs.archived-YYYY-MM-DD` rename (predecessor NFR-06)
- ❌ MUST NOT add Coverlet thresholds to required-status-checks (D-04; defers to r3)
- ❌ MUST NOT bypass `enforce_admins` outside the admin-merge window of a specific PR (NFR-03 pattern from r1)

### r2-Specific Discipline (inverts r1's NFR-01)

- **r1 NFR-01**: NO `src/` changes. **r2 NFR-01 (inverted)**: `src/` changes ARE in scope. Tests modified ONLY for Skip→Pass transitions associated with closed ledger entries OR for Phase 4 Track C TestClock PoC (FR-13).
- **No "while we're here" test repairs.** If a test is observed degraded but not in scope, file a NEW ledger entry — do not absorb.

---

## Decisions Made

<!-- Inherited from design.md §6 — LOCKED at project start -->

| Date | Decision | Rationale |
|---|---|---|
| 2026-06-01 | **D-01**: NFR-01 RELAXED — `src/` changes IN scope | r1 was test-only by design; r2's purpose is fixing the production bugs r1 surfaced. NFR-01' applies inversely. |
| 2026-06-01 | **D-02**: One fix = one entry closed; cluster exception for shared root cause | Clean attribution; RB-T028-03/04/05/06 share root cause and may close as one |
| 2026-06-01 | **D-03**: HIGH gets security review; MED + LOW get FULL-rigor `code-review` + `adr-check` | RB-T044-01 taught: "obvious" fixes still need scrutiny |
| 2026-06-01 | **D-04**: Phase 4 tracks are pilot-grade; full execution = r3 | Protect 2026-08-31 deadline |
| 2026-06-01 | **D-05**: Real-bug ledger is source of truth for state transitions | Per-entry lifecycle is auditable |
| 2026-06-01 | **D-06**: r3 NOT pre-committed | Decision based on r2 findings, not in advance |

Additional decisions (added during execution):

*None yet*

---

## Implementation Notes

### r1 Calibration

- **Sibling-fixture pattern is canonical**: 5 fixture sites share 7 missing DI config keys. r2 loads `docs/procedures/testing-and-code-quality.md` upfront — does NOT re-discover.
- **RB-T028 cluster (5 HIGH) shares one root cause**: conditional service registration + unconditional endpoint mapping. ONE production change can close 4 entries (D-02 cluster exception).
- **Cluster-level classification scales**: r1 task 028 dispositioned 47 residuals via ~6 cluster entries. Use cluster commits where root cause shared.
- **"Absorb deviations, don't defer"**: r1 added additive `<notes>` to POMLs rather than re-planning mid-task. r2 follows for Phase 4 audit findings.
- **Parallel dispatch: 13 waves at 6-agent cap with anti-parallelism guard on shared fixtures**. r2 Phase 1 (RB-T028 cluster) is SEQUENTIAL. Phases 2-5 parallelize where files disjoint.
- **Repair-not-rewrite escalation rate 1.23%** (1/81 files) — r2 NFR-02 keeps <50% line-replacement rule.

### Greenfield areas

- **No existing TestClock/IClock/ISystemClock/TimeProvider in `src/`** (Phase 4 Track C is greenfield design)
- **No existing seeded-Guid provider** (Phase 4 Track C greenfield)
- **No existing Stryker.NET config** (Phase 4 Track B is greenfield — first Stryker run)

### Already-wired infrastructure (do not re-build)

- **Coverlet ALREADY collects coverage in CI** (`.github/workflows/sdap-ci.yml` lines 85-102 via `config/coverlet.runsettings`). Phase 4 Track D is minimal: surface % per project — do NOT add threshold (D-04).

---

## Resources

### Applicable ADRs

(See "Binding ADRs" section above for full list with relevance)

### Related Projects

- [`projects/sdap-bff.api-test-suite-repair/`](../sdap-bff.api-test-suite-repair/) — **PREDECESSOR**; load lessons-learned + real-bug-ledger at every task
- [`projects/github-actions-rationalization-r1/`](../github-actions-rationalization-r1/) — coordinate CI workflow changes (FR-14 Coverlet sequencing)
- [`projects/ai-spaarke-insights-engine-r1/`](../ai-spaarke-insights-engine-r1/) — sibling for RB-T028-02 HOLD resolution (FR-05)

### External Documentation

- [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) — Phase 4 Track B
- [Coverlet](https://github.com/coverlet-coverage/coverlet) — Phase 4 Track D (already-wired baseline)

---

*This file should be kept updated throughout project lifecycle. Add to "Decisions Made" and "Implementation Notes" as work progresses.*
