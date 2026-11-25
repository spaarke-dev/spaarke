# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (checkpoint: project-pipeline mid-run)
> **Phase**: spec done; project-pipeline running. Steps 0–1.7 and Step 2 Part 1 discovery DONE and owner-approved (plan file `C:\Users\RalphSchroeder\.claude\plans\pure-skipping-yeti-of-bf36b0b5-3feb-4593-a828-6e9f7be9cbdd.md` holds the discovery results and the task outline).

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/project-pipeline` for this project, owner said "Yes, run it" |
| **Step** | Next: Step 2 Part 2 + Step 3 — one opus general-purpose agent runs `project-setup` then `task-create` (POMLs, TASK-INDEX.md, lint `pwsh scripts/Validate-TaskPoml.ps1 projects/spaarkeai-mcp-bff-api-r2/tasks`), writing only under this folder, committing nothing |
| **Then** | Step 4: commit + push, add `projects/INDEX.md` row; Step 4.2: sync #3150 (already registered, Epic #421), `gh label create spaarkeai-mcp-bff-api-r2 --force`, `python scripts/ai-cost/backfill-tasks-planned.py spaarkeai-mcp-bff-api-r2 --apply`, VERIFY; Step 5: Phase 0 wave 1 (001, 002, 004, 005, 006, 008–012; max 6 per wave), build between waves; deploys wait for owner go + runbook E1 |
| **Uncommitted** | this file only |
| **Branch state** | master merged in (7879ff3eff), pushed; baseline BFF build 0/0 |

## Task outline (approved; task-create finalises)
- Phase 0 (now, parallel with r1, no r1 files): 001 BFF legacy removal (move `AgentEndpoints` constants used by `PlaybookAuthorizationFilter` first); 002 non-BFF removal; 003 `.claude/` refs (main session); 004 Dataverse MCP research + guide paragraph; 005 durable run status (+ schema gaps: correlation/idempotency id, error code, regarding lookup, summary; noderun timestamps/sequence); 006 events capability extraction + must-not-leak (frozen bytes like `SignalRoutesContractBaselineTests`); 007 playbook auth shared method (after 001); 008 finance auth shared method; 009 record-search raw-query proof; 010 Daily Briefing seam; 011 EmailComposer MCP host; 012 matter-summary topic (column on sprk_matter + `sprk_aitopicregistry` row for existing Binding `matter-summary` SUM-CHAT@v1); 019 deploy.
- Phase 1 (after r1 PRs #3161 + task 042 merge): 020 McpToolResults + long-running helper; 021 shared widget transport; 022 search; 023 documents; 024 playbook list; 025 task reads; 026 portfolio; 029 deploy.
- Phase 2: 030 runs; 031 analysis; 032 matter summary; 033 finance; 034 briefing tool+widget; 035 search widget; 036 task writes; 039 deploy.
- Phase 3: 040 email widget + app-only send; 041 routing eval (manual, owner runs; template r1 `notes/routing-eval.md`); 042 latency/size; 043 dev E2E; 049 deploy; 090 wrap-up.

## Decided (owner)
- 2026-11-25 (pipeline): matter summary = complete the `matter-summary` topic (new column + registry row), not matter-health.
- 2026-11-25 (spec interview): reversible writes model-callable, send email app-only; persist runs to `sprk_playbookrun`; email = matter email + matter documents, archived; matter summary = Insights cache, else start; briefing = AI Daily Briefing (seam in `Spaarke.DailyBriefing.Components`).
- 2026-11-25: Q1–Q8 decided (design v0.3): one connector; 30 s limit; widgets reuse Console components; Phase 0 parallel with r1; Dataverse MCP dev/admin only.
- 2026-11-25: remove the Spaarke AI agent NOW; tenant E1/E2 are the owner's.

## Open
- HARD WARNING: design.md lacks `<hot-path-declaration>` (block is in spec.md); owner may add it.
- Owner: runbook E1 before the removal deploys to dev.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions are the owner's.
- Commits: `export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"`; end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
