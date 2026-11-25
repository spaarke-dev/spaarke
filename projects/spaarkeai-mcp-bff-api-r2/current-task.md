# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (design v0.3: all §9 decided)
> **Phase**: design v0.3, all questions decided. No spec, no tasks yet.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | Ready for `/design-to-spec` (owner Q7: spec now) |
| **Next action** | Run `/design-to-spec` on `design.md` v0.3. Phase 0 (legacy removal) may run in parallel with r1; tool phases wait for r1's open PRs |
| **Uncommitted** | none |

## Decided (owner)
- 2026-11-25: MCP r2 is its own project, high priority. Scope: move the legacy "Spaarke AI" agent's functions to MCP tools, then retire the agent.
- 2026-11-25: remove the Spaarke AI agent NOW (design §4.5): tenant steps E1/E2 are the owner's (coordination runbook); code removal is Phase 0's first task.
- 2026-11-25: Q1 accept §3 dispositions; Q2 one connector; Q3 30-second limit per tool, longer work = start + status; Q4 include email (compose widget + app-only send), unless large.
- 2026-11-25: Q5 reuse Console components as widgets (briefing, search, email compose first); Q7 spec now, Phase 0 parallel with r1, tool phases after r1 PRs merge; Q8 Dataverse MCP = dev/admin only, not end-user surfaces (§4.7).
- 2026-11-25: one MCP server; one route and connector per product area; no per-agent connectors; agents only for practice areas (`docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`, PR #3147).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions (retiring the Spaarke AI registration) are the owner's.
