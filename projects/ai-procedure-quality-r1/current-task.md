# Current Task

> **Project**: ai-procedure-quality-r1
> **Last Updated**: 2026-05-14

## Active Task

**Status**: not-started
**Next task**: 001-inventory-skills
**Phase**: 0 (Inventory + Baseline)

## Quick Recovery

To resume this project in a new session:

1. Confirm you're on branch `work/ai-procedure-quality-r1` (or create it: see plan.md Phase 0)
2. Read [TASK-INDEX.md](tasks/TASK-INDEX.md) for the parallel group strategy
3. Phase 0's first wave: tasks 001–004 are all parallel-safe (inventory only, no `.claude/` writes)
4. Invoke `task-execute` on the first available task
5. After each wave, run `dotnet build src/server/api/Sprk.Bff.Api/` as a discipline check (per /project-pipeline rules)

## Completed Steps

(none yet — project just initialized)

## Files Modified This Task

(none yet)

## Decisions Made This Task

(none yet)

## Notes

This project modifies the AI procedure surface that the agent itself uses. Two human-gate points (Phase 2b prerequisite, Phase 3b prerequisite) enforce review before destructive changes. Everything is reversible via `.claude/archive/<date>/`.

When you start the first task, this file will be updated by the `task-execute` skill with detailed step tracking.
