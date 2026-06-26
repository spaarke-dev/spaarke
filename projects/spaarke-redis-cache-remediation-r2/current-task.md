# Current Task State — spaarke-redis-cache-remediation-r2

> **Last Updated**: 2026-06-26 (initialized by `/project-pipeline`)
> **Status**: 🔄 ready — task 001 dispatching (autonomous mode)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarke-redis-cache-remediation-r2 |
| **Active task** | 001 — `cache.failures` Counter + try/finally + `ClassifyException` (FR-01) |
| **Status** | not-started → about to begin via `task-execute` |
| **Next action** | `task-execute` with `tasks/001-cache-failures-counter.poml` |

---

## Active Task Details

- **Task ID**: 001
- **Title**: `cache.failures` Counter + try/finally + `ClassifyException` helper in `MetricsDistributedCache`
- **FR**: FR-01 (Theme A — Cache observability hardening)
- **Rigor**: FULL (`bff-api` tag + `.cs` modification — code-review + adr-check at Step 9.5)
- **Files**: `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` (modify)
- **Acceptance**: KQL `customMetrics | where name == 'cache.failures' | summarize sum(value) by tostring(customDimensions.outcome)` returns ≥1 row after `az redis force-reboot` against dev (verified at task 030)

### Steps Completed (will populate as work progresses)

- [ ] Step 0 — Context check
- [ ] Step 1 — Review TASK-INDEX + dependencies (none for task 001)
- [ ] Step 2 — Gather resources (ADR-009, ADR-010, ADR-029, ADR-032; constraints/bff-extensions.md; MetricsDistributedCache.cs)
- [ ] Step 3 — Plan implementation (try/finally wrap + ClassifyException helper + cache.failures Counter)
- [ ] Step 4 — Implement
- [ ] Step 5 — Verify (build clean + unit tests if applicable)
- [ ] Step 6 — Document (update TASK-INDEX 🔲 → ✅)
- [ ] Step 9.5 — Quality gates (code-review + adr-check)

### Files Modified (will populate during execution)

| File | Change | Status |
|---|---|---|

### Decisions / Notes (will populate during execution)

---

## Parallel Execution Tracker

Task 001 is in **Group 0** (foundational — serial). Group A (tasks 002, 004, 006) dispatches after 001 completes.

---

## Resume Protocol

If context is reset / new session:
1. Read this file (current-task.md)
2. Read `tasks/TASK-INDEX.md` for overall status
3. Invoke `task-execute` with the active task file shown above
4. `task-execute` will load `CLAUDE.md` + `spec.md` + applicable ADRs + the task POML

---

*Updated by `task-execute` Step 1 + Step 6. Reset to next pending task per CLAUDE.md §7.*
