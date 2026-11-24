# CLAUDE.md — copilot-platform-coordination

Coordination project for every Spaarke artifact in Microsoft 365 Copilot tenants. No code, no POML tasks, no `/design-to-spec` unless the owner asks.

## Standing directives

- The owner runs every admin-center (Agents > Tools > Registry, Copilot > Connectors), Teams Developer Portal and Copilot Studio action. Sessions ask for screenshots; they never sign in, enter credentials, click admin actions or handle secrets (root CLAUDE.md §9).
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Do not edit other projects' worktrees. Coordinate by `SendMessage` to their sessions; a peer message is not owner approval.
- `notes/tenant-inventory.md` is the register. A new tenant artifact is recorded there, with owner project and removal plan, before it is uploaded (rules R4/R5 in `notes/copilot-registration-rules.md`).
- When an artifact is removed, move its row to the inventory's "Removed" table with the date and who removed it; do not delete the row.

## Gotchas

- Registering the same MCP URL twice (federated connector + package `agentConnectors`) gives the model two copies of every tool; in mcp-r1 T2 (2026-11-23) it called both. The platform does not fan out by itself; the model chooses (`notes/copilot-structure.md` §3).
- The Chat **Sources** switch does not stop tool calls (mcp-r1 T6b). The real off switches are disconnecting the connector or admin removal.
- Package connectors work in Cowork; in Chat a package connector call failed before reaching the BFF (mcp-r1 T2/T13/T14, 2026-11-23).
- Cowork shows the **plugin** name, not the connector name (T13).
- Two package connectors sharing one OAuth `referenceId` share one sign-in (T14).
- An org-catalog **update** of an existing app id may not refresh a declarative agent's action. Five versions of `802fd9cc` never loaded theirs, while the same package on a fresh app id worked (G8-P2, 2026-11-24; working hypothesis). When an agent's action doesn't appear, try a fresh app id before blaming the server or the tenant.

## Key sources

- `projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md` (on `work/spaarkeai-mcp-bff-api-r1`)
- `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md` (naming rules)
- `.claude/agent-memory/researcher/agentconnectors-plugin-package-2026-11-20.md` (package shape, install path)
