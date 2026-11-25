# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (project-pipeline Step 2 Part 2 + Step 3 artifacts written; not yet committed)
> **Phase**: planning complete; execution not started.

## Quick Recovery
| Field | Value |
|---|---|
| **Active task** | none (no task in progress) |
| **Status** | none |
| **Active work** | `/project-pipeline` for this project, owner said "Yes, run it". Step 2 Part 2 (plan.md, CLAUDE.md, README.md, this file) and Step 3 (35 POMLs, `tasks/TASK-INDEX.md`, lint PASS) are written by the planning agent |
| **Next** | Main session: review artifacts; Step 4: commit + push, add `projects/INDEX.md` row; Step 4.2: sync #3150 (already registered, Epic #421), `gh label create spaarkeai-mcp-bff-api-r2 --force`, `python scripts/ai-cost/backfill-tasks-planned.py spaarkeai-mcp-bff-api-r2 --apply`, VERIFY; Step 5: Phase 0 Wave 1 = 001, 002, 005, 006, 008, 009 (Wave 2 = 004, 007, 010, 011, 012; 003 in the main session after 001 + 002); build between waves; deploys wait for owner go + runbook E1 |
| **Uncommitted** | `README.md`, `plan.md`, `CLAUDE.md`, `current-task.md`, `notes/decisions.md`, `notes/*/.gitkeep`, `tasks/*.poml`, `tasks/TASK-INDEX.md` |
| **Branch state** | master merged in (7879ff3eff), pushed; baseline BFF build 0/0 |

## Task outline (final: `tasks/TASK-INDEX.md`)
- Phase 0 (now, parallel with r1, no r1 files): 001–012, deploy 019.
- Phase 1 (BLOCKED until r1 PR #3161 + r1 task 042 merge): 020–026, deploy 029.
- Phase 2: 030–036, deploy 039. Phase 3: 040–043, deploy 049. Wrap-up 090.

## Decided (owner)
- 2026-11-25 (pipeline): matter summary = complete the `matter-summary` topic (new column + registry row), not matter-health.
- 2026-11-25 (spec interview): reversible writes model-callable, send email app-only; persist runs to `sprk_playbookrun`; email = matter email + matter documents, archived; matter summary = Insights cache, else start; briefing = AI Daily Briefing (seam in `Spaarke.DailyBriefing.Components`).
- 2026-11-25: Q1–Q8 decided (design v0.3): one connector; 30 s limit; widgets reuse Console components; Phase 0 parallel with r1; Dataverse MCP dev/admin only.
- 2026-11-25: remove the Spaarke AI agent NOW; tenant E1/E2 are the owner's.

## Open
- HARD WARNING: design.md lacks `<hot-path-declaration>` (block is in spec.md); owner may add it.
- Owner: runbook E1 before the removal deploys to dev.
- Owner: go for dev Dataverse schema changes when 005 and 012 reach that step.
- Coordination to file (task 002): customer-provisioning-orchestration-r1's T257 "shared Spaarke Copilot Agent app" (control-plane Bicep, deployment guide §7.12) is outside this project's removal scope.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions are the owner's.
- Commits: `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"`; end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
