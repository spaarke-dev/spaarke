# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (`/project-pipeline` Steps 0–4 done)
> **Phase**: Planned. 33 tasks in `tasks/` (lint PASS). Execution (pipeline Step 5) not started.

## Quick Recovery
| Field | Value |
|---|---|
| **Task** | none active |
| **Status** | none |
| **Next action** | Wave 0-1: tasks 002, 003, 005 in parallel, plus 007 (ADR-004 A2 merge) in the main session. 001 and 004 wait on PR #3163. See `tasks/TASK-INDEX.md` "Parallel Execution Plan" |
| **Files** | spec.md, plan.md, CLAUDE.md, README.md, tasks/TASK-INDEX.md + 33 POMLs, notes/decisions.md, notes/reuse-scan.md |
| **Unresolved (spec)** | (1) Clean copy with prior markup → settled in task 008 spike with the accelerator; (2) ADR-049 scope → `/adr-check` (task 090); (3) ontology columns on sprk_servicerequest → before task 010 |
| **Open for owner** | design.md lacks `<hot-path-declaration>` + Placement Justification section (pipeline HARD WARNING; spec.md has both). Engine wrapper on stdlib+lxml only (Service Bus/Blob over REST) vs allowing Azure SDKs in the wrapper (task 001 escalation trigger) |
| **External gates** | PR #3163 (engines on master) → 001, 004; eBilling UAT (ebilling-r1 task 163) → 050; ontology coordination → 010 |
| **Issues** | #3162 (email ingestor seam), #1920 (producer, task 042), portfolio #3128 |

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator session: "acclerator-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Don't copy the accelerator engines; build from `src/accelerators` on master; Python 3.11, stdlib + lxml.
- Phase 0 cleanup is one PR with the real fixes; no hygiene-only PRs.
- New tests go under KEEP paths (`tests/CLAUDE.md`); task 012–016 paths were corrected at planning.
