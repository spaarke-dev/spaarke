# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (design v0.2: owner answered Q1–Q4)
> **Phase**: design draft v0.2. No spec, no tasks. Do NOT run `/design-to-spec` until the owner resolves `design.md` §9 Q5, Q7, Q8.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | Owner review of `design.md` v0.2 |
| **Next action** | Get the owner's answers to §9 Q5 (widgets), Q7 (Phase 0 in parallel with r1), Q8 (Dataverse MCP position); record them; then `/design-to-spec` on the owner's go |
| **Uncommitted** | none |

## Decided (owner)
- 2026-11-25: MCP r2 is its own project, high priority. Scope: move the legacy "Spaarke AI" agent's functions to MCP tools, then retire the agent.
- 2026-11-25: remove the Spaarke AI agent NOW (design §4.5): tenant steps E1/E2 are the owner's (coordination runbook); code removal is Phase 0's first task.
- 2026-11-25: Q1 accept §3 dispositions; Q2 one connector; Q3 30-second limit per tool, longer work = start + status; Q4 include email (compose widget + app-only send), unless large.
- 2026-11-25: one MCP server; one route and connector per product area; no per-agent connectors; agents only for practice areas (`docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`, PR #3147).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions (retiring the Spaarke AI registration) are the owner's.
