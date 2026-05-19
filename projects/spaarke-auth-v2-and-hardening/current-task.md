# Current Task - Spaarke Auth v2 + Hardening

> **Project**: spaarke-auth-v2-and-hardening
> **Status**: not-started (next task)
> **Active Phase**: Pre-flight (Phase 0)
> **Last Updated**: 2026-05-18

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 003 - Verify + finalize project CLAUDE.md prohibition section |
| **Step** | Begin Step 1 of task 003 |
| **Status** | not-started |
| **Next Action** | `work on task 003` |

## State

- Worktree: `c:\code_files\spaarke-wt-spaarke-auth-v2-and-hardening`
- Branch: `work/spaarke-auth-v2-and-hardening`
- Phase 0 progress: 2/5 tasks complete (001 ✅, 002 ✅; 003–005 remaining)
- Overall: 2/49 tasks complete

## Handoff Notes (from task 002)

- 7 files got STOP banners (5 partial-canonical + 2 full-deprecation). All 7 banners verified via grep `🛑 STOP`.
- Banner format from `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md §8.2 Layer 2` was used verbatim. Per-file canonical exceptions follow the PF-4..PF-10 table.
- Task 003 next: verify the project CLAUDE.md prohibition section (already present per `projects/spaarke-auth-v2-and-hardening/CLAUDE.md` "🚨 ACTIVE AUTH V2 REFACTOR" section). Task may be mostly a verification + minor edits.
- Phase 0 tasks 003–005 remain main-session-only (.claude/ paths). No parallelism until Phase A.

## Key Learnings

- The `---` ... `---` banner sandwich pattern from §8.2 works cleanly with the existing `# Title` content below — no markdown rendering conflicts observed.
- Box-drawing chars (═) are preserved as UTF-8 by both the Edit tool and git (no encoding warnings).
- For files that have been renamed (e.g., DEPRECATED-spaarke-auth-initialization.md), each post-rename path needs its own Read before Edit — the harness tracks the renamed file as a distinct entity even if content is unchanged.
