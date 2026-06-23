# Scheduled Jobs Migration — AI Context

> **Purpose**: This file provides context for Claude Code when working on `scheduled-jobs-migration`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Design (scaffolded — not yet planned)
- **Last Updated**: 2026-06-22
- **Current Task**: Not started (project scaffolded today via subagent)
- **Next Action**: Owner answers Open Questions in `design.md`, then run `/design-to-spec` followed by `/task-create`

---

## Quick Reference

### Key Files

- [`design.md`](design.md) — Detailed design with discovery, migration strategy, per-job recipe, risks, Open Questions for Owner
- [`spec.md`](spec.md) — **PLACEHOLDER** — populate via `/design-to-spec`
- [`README.md`](README.md) — Project overview + graduation criteria
- [`current-task.md`](current-task.md) — Active task state (initial: "not started")
- [`tasks/`](tasks/) — Per-job migration tasks (populated later via `/task-create`)

### Project Metadata

- **Project Name**: `scheduled-jobs-migration`
- **Type**: Cross-cutting BFF infrastructure migration (server-side only — no client changes)
- **Complexity**: Medium (mechanical-leaning; per-job ~2–4h; 14–17 jobs estimated)
- **Branch**: TBD (`work/scheduled-jobs-migration` worktree recommended)
- **Predecessor**: [`spaarke-platform-foundations-r3`](../spaarke-platform-foundations-r3/) (delivered the framework + 2 reference consumers)
- **Reference impls**: R3 tasks `023-*.poml` (PlaybookSchedulerJob) and `085-*.poml` (MembershipReconciliationJob)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check `current-task.md`** for active work state (especially after compaction / new session)
3. **Reference `spec.md`** for FRs / NFRs / MUST rules (after `/design-to-spec` populates it)
4. **Reference `design.md`** for strategy, per-job recipe, and inventory
5. **Load the relevant task file** from `tasks/` based on current work
6. **Apply ADRs** relevant to the technologies used (loaded automatically via `adr-aware`)
7. **MANDATORY**: For any code change to `src/server/api/Sprk.Bff.Api/`, load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — every task in this project touches the BFF

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Why This Matters

The task-execute skill ensures:
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is properly tracked in current-task.md
- Proactive checkpointing every 3 steps
- Quality gates run (code-review + adr-check) at Step 9.5
- Progress is recoverable after compaction
- The BFF binding governance in CLAUDE.md §10 is applied to every task

**Bypassing leads to**: missing ADR constraints, no checkpointing, skipped quality gates, BFF publish-size regressions.

### Parallel Task Execution

Tasks within a wave (no dependencies, separate files) MUST be dispatched as ONE message with MULTIPLE Skill tool invocations — one per task. Most migrations in this project are file-isolated (each `BackgroundService` lives in its own file), so waves of 3–5 parallel tasks are the default.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

### Applicable ADRs (load automatically via adr-aware)

- **ADR-036** (Background Job Infrastructure) — PRIMARY. The framework spec. Every task in this project implements `IScheduledJob` per this ADR.
- **ADR-001** (BFF first / no Azure Functions) — this project migrates WITHIN ADR-001 (we're keeping work in-process; the framework is the operational sugar).
- **ADR-010** (DI patterns) — `IScheduledJob` impls are Singleton; per-run work uses `IServiceScopeFactory` to resolve Scoped deps.
- **ADR-012** (Shared component library) — `Spaarke.Scheduling` lives in `src/server/shared/`; this project consumes it, does not modify it (unless framework gap forces extension).
- **ADR-032** (Null-Object Kill-Switch) — relevant if a migrated job needs a feature flag with the asymmetric-registration solution.

### Patterns to load

- `.claude/patterns/scheduling/` — if/when patterns exist for scheduled jobs (check; otherwise the R3 task 023 / 085 POMLs are the canonical templates)

### BFF binding (CLAUDE.md §10)

Every task in this project touches `src/server/api/Sprk.Bff.Api/`. The CLAUDE.md §10 BFF Hygiene checklist applies to every PR:

1. **MUST** read `.claude/constraints/bff-extensions.md` before designing the migration
2. **MUST** state placement decision in PR description (default for this project: "remains in BFF; mechanical migration of existing in-BFF code")
3. **MUST NOT** inject AI-internal types into CRUD; if the migrated job touched AI internals, route via `Services/Ai/PublicContracts/`
4. **MUST** verify publish size with `dotnet publish` + report absolute size + diff vs prior baseline (expected: ~0 MB change since framework already shipped in R3; flag any ≥+5 MB delta)
5. **MUST** verify zero new HIGH-severity CVEs via `dotnet list package --vulnerable --include-transitive`
6. **MUST** add/update tests per § F Test update obligation (per-job acceptance test required by design)

The asymmetric-registration sub-rules (§ F.1 / F.2 / F.3) apply if any migrated job uses a feature flag.

### Spec MUST / MUST NOT rules (placeholder — finalize in `spec.md`)

After `/design-to-spec` runs, expect rules along these lines (NOT YET BINDING):

- ✅ MUST preserve pre-migration cadence (cron string equivalent to PeriodicTimer interval)
- ✅ MUST implement `IScheduledJob` (not a wrapper around the old BackgroundService)
- ✅ MUST delete the old BackgroundService impl in the same PR
- ✅ MUST add a unit test calling `RunAsync(ctx, CT.None)` directly
- ✅ MUST verify the job appears in `GET /api/admin/jobs` after deploy
- ❌ MUST NOT add new `IOptions<X>` for cadence (use `ScheduledJobHostOptions` or `DefaultSchedule` const)
- ❌ MUST NOT migrate Tier 2 (event-driven) services into `IScheduledJob` — they have a different shape per ADR-036
- ❌ MUST NOT introduce new AI-direct dependencies in CRUD code (CLAUDE.md §10 bullet 3)

---

## Decisions Made

<!-- Log key architectural / implementation decisions here as project progresses -->

| Date | Decision | Rationale | Source |
|---|---|---|---|
| 2026-06-22 | Project scaffolded; design.md drafted with 17-job Tier 1 inventory (14 confirmed + 3 pending Phase 0) | Discovery grep `: BackgroundService` returned 28 hits (1 archived) classified per ADR-036 | Scaffolding session |
| 2026-06-22 | Tier 2 (event-driven) and Tier 3 (long-lived / null-object) OUT OF SCOPE per ADR-036 boundary | Different operational shape; framework contract doesn't fit | ADR-036 + design.md § Discovery |
| TBD | OQ-1 through OQ-8 (Open Questions) — to be answered by owner before /design-to-spec | — | design.md § Open Questions for Owner |

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

### Discovery findings (2026-06-22)

- Grep `: BackgroundService` in `src/server/api/Sprk.Bff.Api/` returned **28 hits**; 1 is in `Services/BackgroundServices/_archive/`
- Of the 27 active impls: 14 confirmed Tier 1 + 3 pending Phase 0 + 6 confirmed Tier 2 (3 by inspection + 3 likely Tier 2 pending Phase 0) + 1 Tier 3 (null-object)
- R3 reference impls (PlaybookSchedulerJob, MembershipReconciliationJob) do NOT appear in the grep — they implement `IScheduledJob` (correct post-migration shape)
- `Workers/Office/*Worker.cs` family (UploadFinalization, ProfileSummary, IndexingWorker) inject `ServiceBusClient` and implement `IOfficeJobHandler` — strongly suggests Tier 2 but needs Phase 0 audit to confirm

### Phase 0 audit MUST answer

1. Definitively classify the 3 pending Office workers (Tier 1 vs Tier 2)
2. Confirm `ScheduledJobHost` supports cron expressions needed by Tier 1 jobs (especially daily `0 0 * * *` and sub-hour intervals like `*/15 * * * *`)
3. Identify any Tier 1 job with cross-cutting concerns requiring special handling (e.g., webhook continuity for `GraphSubscriptionManager`)

---

## Resources

### Applicable ADRs

- [`ADR-036`](../../.claude/adr/ADR-036-background-job-infrastructure.md) — Background Job Infrastructure (PRIMARY)
- [`ADR-001`](../../.claude/adr/ADR-001-bff-first-no-azure-functions.md) — BFF first / no Azure Functions
- [`ADR-010`](../../.claude/adr/ADR-010-di-patterns.md) — DI patterns (Singleton-with-Scoped)
- [`ADR-012`](../../.claude/adr/ADR-012-shared-component-library.md) — Shared component library
- [`ADR-032`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object Kill-Switch (if any migrated job is feature-flagged)

### Reference implementations (R3)

- **Canonical mechanical migration**: `projects/spaarke-platform-foundations-r3/tasks/023-*.poml` — `PlaybookSchedulerService` → `PlaybookSchedulerJob`
  - Output: [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs)
- **Fan-out variant** (single sprk_backgroundjob row, fans out across N children): same task 023 (D2 decision)
- **Service Bus boundary case** (kept as Tier 2): `projects/spaarke-platform-foundations-r3/tasks/085-*.poml` — `MembershipReconciliationJob` (the scheduled companion to the Tier 2 `MembershipJunctionUpdaterHost`)

### Framework source

- [`src/server/shared/Spaarke.Scheduling/IScheduledJob.cs`](../../src/server/shared/Spaarke.Scheduling/IScheduledJob.cs) — the contract
- [`src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs) — the runner
- [`src/server/shared/Spaarke.Scheduling/ScheduledJobRegistry.cs`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobRegistry.cs) — registration
- [`src/server/shared/Spaarke.Scheduling/IBackgroundJobStore.cs`](../../src/server/shared/Spaarke.Scheduling/IBackgroundJobStore.cs) — persistence contract
- [`src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs) — `/api/admin/jobs/*` endpoints

### Architecture + operator docs

- [`docs/architecture/spaarke-scheduling-architecture.md`](../../docs/architecture/spaarke-scheduling-architecture.md) — R3 Wave 27 framework architecture
- [`docs/architecture/background-workers-architecture.md`](../../docs/architecture/background-workers-architecture.md) — current inventory (this project drives Tier 1 to zero)
- [`docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`](../../docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md) — operator-facing guide

### Binding constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — pre-merge checklist (MANDATORY per CLAUDE.md §10)
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — BFF Publish-Size Per-Task Verification Rule
- [`docs/procedures/testing-and-code-quality.md`](../../docs/procedures/testing-and-code-quality.md) §§18.1–18.4 — asymmetric-registration sub-mechanisms (if any job is feature-flagged)

### Related projects

- **Predecessor**: `spaarke-platform-foundations-r3` (delivered framework; merged to master via various Wave PRs through Wave 27)
- **Adjacent**: `code-quality-and-assurance-r3` (general code quality; potential overlap on test patterns)
- **Adjacent**: `bff-ai-architecture-audit-r1` (BFF AI extraction context; relevant if any migrated job touches AI internals)

---

*This file should be kept updated throughout project lifecycle.*
