# Task Index — pcf-orphan-cleanup-r1

> Generated: 2026-06-22 (project setup)
> Status legend: 🔲 not-started | 🔄 in-progress | ✅ complete | ⏸ blocked

---

## Task tracker

| # | Title | Status | Effort | Dependencies | Parallel group | Rigor | Notes |
|---:|---|:---:|:---:|---|:---:|:---:|---|
| 001 | Pre-flight: solution backups + per-control 4-check (spaarkedev1) | ✅ 2026-06-22 | 2-4h | — | P1-W1 | STANDARD | 10 baseline ZIPs captured; 10/11 controls ready-to-delete; 1 deviation filed (DEV-001 AnalysisWorkspace) |
| 002 | Source-tree deletion PR (UQC + DTW + SDV) | ✅ 2026-06-22 (PR #412) | 1-2h | — | P1-W1 | STANDARD | UQC + DTW + SDV deleted; tsconfig + CLAUDE.md refreshed |
| 003 | Dataverse cleanup session — spaarkedev1 (re-scoped 2026-10-14: 4 customcontrols + 3 canvas apps deleted; 6 held, DEV-002–005) | ✅ 2026-10-15 | 4-6h | 001, 002 | (sequential) | **FULL** | Deletions verified 2026-10-15 02:32–02:34 UTC; drill PASS; owner smoke tests complete, no cleanup regressions (pre-existing defects filed #1950–#1952, #1956). Log: notes/dataverse-cleanup-log.md. PR #1897 |
| **(soak)** | **7-day calendar wait — monitor for regressions** (ends 2026-10-22) | — | 7 days | 003 | (sequential) | — | Not needed: no later task depends on it. Any problem traced to the 2026-10-15 deletions is restored from the backups (restore commands in the cleanup log). |
| 004 | Shared lib `@types/react` peerDep declaration (PR-D1) | [cancelled] 2026-10-15 | 2-4h | 003+soak | (sequential) | **FULL** | Dropped (owner, 2026-10-15): a peerDependency is install-time metadata only. It cannot change where `tsc` resolves `@types/react`, which is the cause of TS2786, and `--legacy-peer-deps` ignores peers anyway. ADR-022 sanctions the per-boundary casts. Audit: no React 18+ API is used in the shared lib. |
| 005 | VisualHost re-pin to React 16 (PR-D2) — deploy + smoke | [cancelled] 2026-10-15 | 3-5h | 004 | (sequential) | **FULL** | Dropped (owner, 2026-10-15): VisualHost is a virtual control; the live bundle carries no React and uses the host's (17.0.2 in model-driven). The `^18.2.0` pin only affects jest, Storybook and types, so a re-pin changes nothing at runtime and would break its tests. The task was written for v1.4.16; the control is now v1.4.40. |
| 006 | Dataverse cleanup session — spaarkedev2 replay | [cancelled] 2026-10-15 | 3-5h | 005 | (sequential) | **FULL** | Dropped (owner, 2026-10-15): spaarkedev2 does not exist (no DNS; `config/environments.json` lists only spaarkedev1). Other environments get changes via the standard release procedure (spec D-06). |
| 007 | Cleanup-log finalize + inventory refresh | [cancelled] 2026-10-15 | 1-2h | 006 | (sequential) | STANDARD | Dropped (owner, 2026-10-15): closeout recorded directly in `notes/dataverse-cleanup-log.md`, `current-task.md`, `README.md` and `client-resources-inventory.md` §0. The inventory refresh was superseded (its counts and the SDV removal were wrong). |

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
