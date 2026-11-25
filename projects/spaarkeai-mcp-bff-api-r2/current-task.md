# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (project-pipeline Step 5: Phase 0 wave 1 dispatched)
> **Phase**: Phase 0 executing.

## Quick Recovery
| Field | Value |
|---|---|
| **Active task** | Phase 0 wave 1 (parallel sub-agents, each in its own isolated agent worktree under `C:\code_files\spaarke\.claude\worktrees\agent-*`, committing to its own branch; not pushed) |
| **Status** | 001, 002, 005, 006 running (🔄 in TASK-INDEX). 008, 009 queued: the session cap is 4 concurrent sub-agents |
| **Active work** | `/project-pipeline` Step 5. Steps 2–4.2 done: artifacts committed (cd7cf78b70) and pushed; INDEX.md row added; #3150 synced (Task Count 35, Tasks Planned 35); label created |
| **Next** | As each agent finishes: review its result, merge its branch into `work/spaarkeai-mcp-bff-api-r2` (merge, never rebase), mark the task in TASK-INDEX, dispatch 008 then 009 into the free slots. After wave 1: `dotnet build src/server/api/Sprk.Bff.Api/`, run missing adversarial-verifier passes, then wave 2 (004, 007, 010, 011, 012), and 003 in the main session after 001 + 002. 005 + 012 dev schema steps need the owner's go; 019 needs runbook E1 + per-SHA go via uac-r2 |
| **Uncommitted** | `tasks/TASK-INDEX.md` (🔄 marks), this file |
| **Branch state** | `cd7cf78b70` pushed; master merged in at 7879ff3eff |

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
