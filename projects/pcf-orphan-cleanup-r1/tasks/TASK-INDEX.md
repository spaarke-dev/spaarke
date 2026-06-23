# Task Index — pcf-orphan-cleanup-r1

> Generated: 2026-06-22 (project setup)
> Status legend: 🔲 not-started | 🔄 in-progress | ✅ complete | ⏸ blocked

---

## Task tracker

| # | Title | Status | Effort | Dependencies | Parallel group | Rigor | Notes |
|---:|---|:---:|:---:|---|:---:|:---:|---|
| 001 | Pre-flight: solution backups + per-control 4-check (spaarkedev1) | 🔲 | 2-4h | — | P1-W1 | STANDARD | Captures §1.1 backups + §1.2 verification |
| 002 | Source-tree deletion PR (UQC + DTW + SDV) | 🔲 | 1-2h | — | P1-W1 | STANDARD | One PR, three folders, `git rm -r` each |
| 003 | Dataverse cleanup session — spaarkedev1 (11 customcontrols + 4-6 canvas apps) | 🔲 | 4-6h | 001, 002 | (sequential) | **FULL** | Single focused block; logged per FR-07 |
| **(soak)** | **7-day calendar wait — monitor for regressions** | — | 7 days | 003 | (sequential) | — | NO work; calendar gate only |
| 004 | Shared lib `@types/react` peerDep declaration (PR-D1) | 🔲 | 2-4h | 003+soak | (sequential) | **FULL** | Includes React 18+ API audit pre-merge |
| 005 | VisualHost re-pin to React 16 (PR-D2) — deploy + smoke | 🔲 | 3-5h | 004 | (sequential) | **FULL** | Re-verify VisualHost source pre-merge; bump v1.4.16 → v1.4.17 |
| 006 | Dataverse cleanup session — spaarkedev2 replay | 🔲 | 3-5h | 005 | (sequential) | **FULL** | Same shape as 003, lower risk after spaarkedev1 success |
| 007 | Cleanup-log finalize + inventory refresh | 🔲 | 1-2h | 006 | (sequential) | STANDARD | Updates `pcf-deployment-inventory-2026-06-22.md` + `pcf-bundle-sizes.md` |

## Parallel execution groups

### P1-W1 (early wave) — Tasks 001 + 002 in parallel

**When**: project kickoff
**Why parallel-safe**: disjoint write paths. Task 001 writes `backups-2026-06-22/`. Task 002 writes `src/client/pcf/{UQC,DTW,SDV}/` (deletions). No shared files.
**Dispatch**: ONE message with TWO `Skill(task-execute)` invocations.

### Sequential after P1-W1

Tasks 003 → soak → 004 → 005 → 006 → 007. Each has a real dependency on the previous one — see [plan.md §"Task dependency graph"](../plan.md#task-dependency-graph) for the visual.

## Acceptance gate

This project is complete when:

- [ ] All 7 tasks marked ✅
- [ ] All 12 success criteria in [`spec.md §3`](../spec.md#3-success-criteria) verified
- [ ] Cleanup log at [`notes/dataverse-cleanup-log.md`](../notes/dataverse-cleanup-log.md) contains entries for both environments
- [ ] Backup ZIPs filed in [`backups-2026-06-22/`](../backups-2026-06-22/)
- [ ] Inventory + bundle-size docs refreshed with completion footnotes (Task 007)

## Decisions made during execution

Track newly-discovered decisions here (additive to [`spec.md §5`](../spec.md#5-decisions-made-binding-for-this-project)):

| Date | Task | Decision | Rationale |
|---|---|---|---|
| (none yet) | — | — | — |
