# Current Task — sdap-SPE-admin-app-r2

> **Purpose**: active-task state for context recovery. Tracks ONLY the current task.
> History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md); detail in each `.poml`.

---

## Active Task

| Field | Value |
|---|---|
| **Task** | none — pipeline complete, ready to start **001** |
| **Status** | not-started |
| **Phase** | 1 — Workstream A (Make failures visible) |
| **Rigor** | — |
| **Started** | — |

## Next Action

Run task **001** (`tasks/001-real-error-surface.poml`) via the `task-execute` skill.

Say **"work on task 001"** or **"continue"**.

---

## Steps Completed

_(none — task not started)_

## Files Modified

_(none)_

## Decisions Made

_(none — project-level decisions are in [`spec.md`](spec.md) Owner Clarifications)_

## Blockers

_(none)_

---

## Session Context

**Pipeline completed 2026-08-21.** Artifacts generated: `plan.md`, `CLAUDE.md`, 30 task POMLs,
`TASK-INDEX.md`. Branch `work/sdap-SPE-admin-app-r2`, draft PR
[#811](https://github.com/spaarke-dev/spaarke/pull/811).

### Carry-forward — read before starting

1. **🔔 Task 010 can reopen the auth decision.** The §6.5 ADR gate is resolved as **path C** (comply under
   ADR-028 E-1), but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Tasks 004/005 are uncapped.** Search and Audit Log root causes are not isolated; effort is provisional.
4. **Task 040 was pulled forward** from Workstream D into Phase 2, so Phase 3's property fixes land
   protected. Deliberate deviation from spec §5 ordering — rationale in `plan.md` §4 Phase 2.
5. **Live-tenant safety**: destructive tests need a dedicated throwaway container. Existing containers hold
   real documents.
