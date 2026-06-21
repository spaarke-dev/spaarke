# Current Task — Spaarke Platform Foundations (R3)

> **Project**: `spaarke-platform-foundations-r3`
> **Last Updated**: 2026-06-20

---

## Active Task

**Status**: none (task 013 complete 2026-06-21)

**Next Task**: `014-scheduled-job-host-retry-idempotency.poml` (Phase P2 / Group D — extends 013's host)

**To start**:
- Say "work on task 014" or "continue"

---

## Task State (when active)

(populated by `task-execute` when a task starts)

- **Task ID**: —
- **Title**: —
- **Started**: —
- **Rigor Level**: —
- **Step**: —
- **Files Modified**: —
- **Decisions Made**: —
- **Blockers**: —

---

## Recently Completed (this session)

- **2026-06-21 — Task 013**: `ScheduledJobHost : BackgroundService` (cron dispatch + run-record write).
  - Files (new, src): `Spaarke.Scheduling/ScheduledJobHost.cs`, `ScheduledJobRegistry.cs`,
    `ScheduledJobHostOptions.cs`, `IBackgroundJobStore.cs`, `InMemoryBackgroundJobStore.cs`.
  - Files (new, tests): test project `tests/unit/Spaarke.Scheduling.Tests/` + 5 test files (registry,
    in-memory store, Cronos parsing, host lifecycle, FakeScheduledJob helper) — 25 tests pass.
  - Files (modified): `Spaarke.sln` (added test project); `tasks/013-...poml` status → completed;
    `TASK-INDEX.md` row 013 → ✅; this file.
  - Solution build PASS (0 errors); test project PASS 25/25 in 10s; publish size 46.14 MB
    (delta +0.01 MB vs 46.13 baseline). No new HIGH CVE.
  - Design choices: `IBackgroundJobStore` abstraction + `InMemoryBackgroundJobStore` stub —
    decouples task 013 from tasks 015/016 (Dataverse entities) which land in a later wave.
    `ParseCron` transparently supports both 5-field (spec.md default) and 6-field (seconds-mode)
    Cron expressions so internal high-frequency jobs + sub-second tests are first-class.
  - adr-check (self): PASS — ADR-001 ✓ (in-process BackgroundService), ADR-010 ✓ (IBackgroundJobStore
    justified by ≥2 implementations from day 1), ADR-012 ✓ (lives in shared lib), NFR-07 ✓
    (30s drain timeout, linked CT to in-flight tasks), NFR-08 ✓ (fresh correlationId per run).
  - bff-extensions §A/§F: PASS — no BFF csproj changes, no new conditional DI, no new HIGH CVE.
  - Not committed per task brief.

- **2026-06-21 — Task 002**: `joinIds` Handlebars helper registered in `TemplateEngine.cs`.
  - Files: `TemplateEngine.cs` (+76 lines), `TemplateEngineTests.cs` (+243 lines, 8 new tests).
  - 33/33 tests pass. Publish size 44.78 MB compressed (delta -1.34 MB vs 46.12 baseline; no NuGet adds).
  - No new HIGH CVE. adr-check + code-review: PASS.
  - Not committed per task brief.

---

## Project Progress

- **Phase**: P1 not started
- **Completed Tasks**: 0 / ~55-65 (final count after task generation)
- **Critical Path**: P1 (001) → P2 (010) → P3 (020) → P4 (030) → P5 (040) → P6 (050) → P6.5 (054) → P7.5 (070) → P8 (080) → P9 (090) → P10 (100) → P11 (110)
- **Earliest parallel opportunity**: Phase 1 Group B (tasks 003+004 — playbook migrations after 002 lands)

---

## Recovery After Compaction

If context was lost:
1. Read [`CLAUDE.md`](CLAUDE.md) (project context)
2. Read [`spec.md`](spec.md) (requirements + owner clarifications)
3. Read this file ([`current-task.md`](current-task.md)) for active task state
4. Read [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for next pending task (first 🔲)
5. Invoke `task-execute` with the active or next pending task's POML

---

*Initialized 2026-06-20 by `/project-pipeline`. Updated by `task-execute` per CLAUDE.md §5 checkpointing rules.*
