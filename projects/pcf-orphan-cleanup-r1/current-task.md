# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-06-22 (project setup)

## Active Task

**None yet** — project just initialized. Ready to begin execution.

## Next Action

Begin with **Task 001 (pre-flight backups)** OR **Task 002 (source deletion PR)** — both are parallel-safe and can run as the first wave (see CLAUDE.md "Parallel Task Execution" → P1-W1).

To begin execution, the user can say one of:
- "work on task 001" (or 002) → triggers `task-execute` on the chosen task
- "continue" → loads `tasks/TASK-INDEX.md`, picks the first 🔲, dispatches `task-execute`
- "run wave 1" → dispatches Tasks 001 + 002 in parallel as ONE message with TWO Skill invocations

## Recent Files Modified

- (none — project freshly initialized)

## Recent Decisions

See [`spec.md §5`](spec.md#5-decisions-made-binding-for-this-project) for the binding decisions table. All six were made during the 2026-06-22 setup session.

## Blockers

- (none — project is unblocked and ready to execute)

## Recovery Notes

If context is lost mid-execution:

1. Read [`CLAUDE.md`](CLAUDE.md) for project context
2. Read this `current-task.md` for active task state
3. Read [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for what's done / pending
4. Resume via `task-execute` on the next 🔲 task
