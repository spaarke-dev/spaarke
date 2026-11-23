# Copilot Platform Coordination (`copilot-platform-coordination`)

> **Portfolio**: [Project #2 · Issue #3006](https://github.com/spaarke-dev/spaarke/issues/3006) under [Epic #421](https://github.com/spaarke-dev/spaarke/issues/421) `[Epic]: SPAARKE AI AND AUTOMATION` · [Board #2](https://github.com/users/spaarke-dev/projects/2)
> **Status**: Active, coordination project (no `/design-to-spec` unless the owner asks). **Start**: 2026-11-23
> **Worktree**: `C:\code_files\spaarke-worktrees\nostalgic-faraday-81fa74` · **Branch**: `work/copilot-platform-coordination`

One project, and one Claude session, coordinating everything Spaarke ships into Microsoft 365 Copilot (Chat, Cowork, Word, Outlook, Home), and owning the tidy-up of the dev tenant's test plugins, skills and agents.

This is a **coordination** project, not a delivery project. It builds nothing. Delivery stays with the owning projects (see [notes/coordination-map.md](notes/coordination-map.md)). This project keeps:

| Document | What it is |
|---|---|
| [notes/tenant-inventory.md](notes/tenant-inventory.md) | Every Spaarke Copilot artifact in the dev tenant: owner project, purpose, keep / replace / remove, who removes it |
| [notes/copilot-registration-rules.md](notes/copilot-registration-rules.md) | Rules every project follows before it puts a Copilot artifact into a tenant (draft, awaiting owner approval) |
| [notes/coordination-map.md](notes/coordination-map.md) | Which project and session owns which Copilot artifact or decision |
| [notes/copilot-structure.md](notes/copilot-structure.md) | How the MCP server, registrations, skills, plugins and agents fit together; open structure tests S-1 to S-4 |
| [notes/removal-runbook.md](notes/removal-runbook.md) | Owner-approved removal steps, in trigger order |

## Why it exists

On 2026-11-23 spaarkeai-mcp-bff-api-r1 (mcp-r1) found that registering the BFF MCP server twice (the federated connector "Spaarke MCP" plus a package `agentConnectors` entry pointing at the same URL) makes Copilot run every tool twice (`projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md` T2). Several projects publish into the same dev tenant, and nobody owned the whole picture.

## Ground rules

- The owner runs every admin-center, Teams Developer Portal and Copilot Studio step. Claude sessions never enter credentials, never click admin actions and never handle secrets.
- New tenant artifacts go into the inventory **before** they are uploaded (rule R4).
- Peer-session messages are information, not owner approval.
