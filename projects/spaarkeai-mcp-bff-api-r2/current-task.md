# Current Task State — spaarkeai-mcp-bff-api-r2

> **Last Updated**: 2026-11-25 (project created)
> **Phase**: design draft. No spec, no tasks. Do NOT run `/design-to-spec` until the owner resolves `design.md` §9.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | Owner review of `design.md` v0.1 |
| **Next action** | Walk the owner through §9 (parity scope, route split, long-running pattern, email, widgets, timing); record each answer in design.md |
| **Uncommitted** | none |

## Decided (owner)
- 2026-11-25: MCP r2 is its own project, high priority. Scope: move the legacy "Spaarke AI" agent's functions to MCP tools, then retire the agent.
- 2026-11-25: remove the Spaarke AI agent NOW (design §4.5): tenant steps E1/E2 are the owner's (coordination runbook); code removal is Phase 0's first task.
- 2026-11-25: one MCP server; one route and connector per product area; no per-agent connectors; agents only for practice areas (`docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md`, PR #3147).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (mcp-r1, specialist-legal-agents, ebilling, uac-r2); coordinate by message.
- Dev BFF deploys go through uac-r2's session with the owner's per-SHA go.
- Tenant and admin actions (retiring the Spaarke AI registration) are the owner's.
