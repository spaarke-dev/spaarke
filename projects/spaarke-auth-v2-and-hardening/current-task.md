# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: Pre-flight (Phase 0)
> **Last Updated**: 2026-05-18

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 005 - Add entry to .claude/CHANGELOG.md documenting v2 in-progress markers |
| **Step** | Begin Step 1 of task 005 |
| **Status** | not-started |
| **Next Action** | `work on task 005` (final Phase 0 task) |

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 4/5 tasks complete (001 ✅, 002 ✅, 003 ✅, 004 ✅; only 005 remaining)
- Overall: 4/49 tasks complete

## Handoff Notes (from task 004)

- Root CLAUDE.md §15 Pointers now has the auth v2 design pointer row (positioned after "Active project state").
- This signal reaches OTHER worktrees: any agent loading the root CLAUDE.md for any task gets the heads-up that auth patterns are in flux + a link to the audit doc.
- Task 005 next: closes out Phase 0 with a .claude/CHANGELOG.md entry documenting the v2 in-progress markers (DEPRECATED- renames, STOP banners, prohibition section, pointer row).
- After 005: **Phase 0 gate** — run MSAL binding regression test before starting Phase A (per project CLAUDE.md). Phase A unlocks parallel groups.
