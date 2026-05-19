# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: Pre-flight (Phase 0)
> **Last Updated**: 2026-05-18

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 002 - Apply STOP banners to 5 partially-superseded pattern/constraint/architecture docs |
| **Step** | Begin Step 1 of task 002 |
| **Status** | not-started |
| **Next Action** | `work on task 002` |

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 1/5 tasks complete (001 ✅; 002–005 remaining)
- Overall: 1/49 tasks complete

## Handoff Notes (from task 001)

- Task 001 deprecated `msal-client.md` + `spaarke-auth-initialization.md` via `git mv`; all 11 in-scope references updated.
- Out-of-scope-but-flagged for future cleanup: `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` line 800 still describes "three patterns are partly/fully superseded" by their pre-rename names — this is correct narrative context but if Phase F task 091/094 wants a fully consistent doc, those occurrences can be updated then.
- Phase 0 tasks 002–005 also touch `.claude/` paths — main-session-only (no parallelism). Phase A unlocks parallel groups (A-Parallel-1, A-Parallel-2).

## Key Learnings

- Edits to many files in parallel still require Read before each Edit (per harness rule). Group Read calls in one message, then Edit calls in one message — fastest pattern.
- The audit doc is the planning artifact; references inside it that name `msal-client.md`/`spaarke-auth-initialization.md` are intentional rename-action descriptions and should NOT be auto-updated by future grep sweeps.
