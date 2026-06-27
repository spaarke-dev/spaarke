# Spaarke Scheduled Jobs Migration

> **Status**: Design (scaffolded — not yet planned)
> **Created**: 2026-06-22
> **Predecessor**: [`spaarke-platform-foundations-r3`](../spaarke-platform-foundations-r3/) (delivered the framework + 2 reference consumers)
> **Owner**: TBD
> **Type**: Cross-cutting BFF infrastructure migration

## Overview

R3 shipped the `Spaarke.Scheduling` framework (`IScheduledJob` contract, `ScheduledJobHost`, `ScheduledJobRegistry`, `IBackgroundJobStore`) plus admin endpoints (`/api/admin/jobs/*`) and the `sprk_backgroundjob` / `sprk_backgroundjobrun` Dataverse entities. Two reference consumers were migrated to prove the pattern (`PlaybookSchedulerJob`, `MembershipReconciliationJob`). This project formalizes the **opportunistic migration of the remaining ~17 schedule-driven `BackgroundService` implementations** so every Spaarke scheduled job shares one operator-visible surface, one retry/idempotency model, and one run-history audit.

The migration is mechanical-leaning (per-job adapter from `BackgroundService` → `IScheduledJob`) and ships in waves of 3–5 per cluster, modeled after the R3 PlaybookSchedulerJob migration template (R3 task 023) and MembershipReconciliationJob (R3 task 085).

## Graduation Criteria

The project is **complete** when:

- [ ] All Tier-1 (schedule-driven) `BackgroundService` implementations in `src/server/api/Sprk.Bff.Api/` have been migrated to `IScheduledJob` and registered with the `ScheduledJobRegistry`
- [ ] Each migrated job appears in `GET /api/admin/jobs` and has at least one row in `sprk_backgroundjobrun`
- [ ] Operator can manually trigger each migrated job via `POST /api/admin/jobs/{jobId}/trigger`
- [ ] Each migration has a per-job acceptance test verifying behavior is functionally equivalent to the pre-migration implementation (or documents intentional deviations)
- [ ] Original `BackgroundService` implementations have been removed (no stale duplicates)
- [ ] `docs/architecture/background-workers-architecture.md` updated — Tier-1 inventory reduced to zero unmigrated; Tier-2 / Tier-3 boundary documented
- [ ] Publish-size unchanged or smaller (no new dependencies; per CLAUDE.md §10 NFR-01)
- [ ] Zero new HIGH-severity CVEs introduced
- [ ] Tier-2 (event-driven) and Tier-3 (long-lived) boundaries explicitly catalogued in design doc with rationale for exclusion

## Key Files

| File | Purpose |
|---|---|
| [`README.md`](README.md) | This file — project overview, graduation criteria |
| [`design.md`](design.md) | Detailed design — problem, inventory, migration strategy, per-job recipe, risks |
| [`spec.md`](spec.md) | AI-optimized spec (placeholder — populate via `/design-to-spec`) |
| [`CLAUDE.md`](CLAUDE.md) | AI context loaded per task — ADRs, patterns, predecessor reference |
| [`current-task.md`](current-task.md) | Active task state (initial: "not started") |
| [`tasks/`](tasks/) | Per-job migration tasks (populated later via `/task-create`) |

## How to Work on This Project

Standard `/project-pipeline` workflow:

1. **Review design** — read [`design.md`](design.md), answer the **Open Questions for Owner** section
2. **Generate spec** — run `/design-to-spec projects/scheduled-jobs-migration/design.md` → produces `spec.md` with FRs / NFRs / ACs
3. **Generate tasks** — run `/task-create projects/scheduled-jobs-migration/` → produces `tasks/NNN-*.poml` files + `TASK-INDEX.md`
4. **Execute** — run tasks via the `task-execute` skill (one per BackgroundService migration; waves of 3–5 parallel where dependencies allow)
5. **Wrap up** — final PR + architecture doc update + project closeout

Per CLAUDE.md §10, every task in this project MUST verify publish-size + run the BFF pre-merge checklist from [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md).

## Discovery Summary

A grep for `: BackgroundService` in `src/server/api/Sprk.Bff.Api/` returned **28 hits** (1 archived, 27 active). Classification:

- **Tier 1 — schedule-driven candidates (in scope)**: 17 jobs
- **Tier 2 — event-driven queue/subscription consumers (out of scope)**: 8 jobs
- **Tier 3 — long-lived workers / null-objects (not migration candidates)**: 2 jobs

See [`design.md` § Discovery](design.md#discovery-current-backgroundservice-inventory) for the full inventory table.

## Predecessor Reference

- **R3 framework spec**: [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
- **R3 reference migration (template)**: `projects/spaarke-platform-foundations-r3/tasks/023-*.poml` — PlaybookSchedulerService → PlaybookSchedulerJob
- **R3 second migration (Service Bus variant — Tier 2 boundary case)**: `projects/spaarke-platform-foundations-r3/tasks/085-*.poml` — MembershipJunctionUpdater (CONFIRMED Tier 2, kept as-is)
- **R3 architecture doc**: [`docs/architecture/spaarke-scheduling-architecture.md`](../../docs/architecture/spaarke-scheduling-architecture.md)
- **R3 operator guide**: [`docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`](../../docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md)
- **Spaarke.Scheduling source**: [`src/server/shared/Spaarke.Scheduling/`](../../src/server/shared/Spaarke.Scheduling/) (IScheduledJob.cs is the contract)

---

*Project scaffolded 2026-06-22. Tasks not yet generated — run `/design-to-spec` then `/task-create` to populate `spec.md` and `tasks/`.*
