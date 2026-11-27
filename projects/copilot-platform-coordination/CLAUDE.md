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
- An org-catalog **update** that changes a declarative agent's action takes the action offline for a while. In V1 (2026-11-24) it was still missing ~25 min after one upload, and it was back at ~40 min after another. Test an updated agent about 45 min after upload, not minutes after. This probably explains the five failed `802fd9cc` versions, each tested within minutes (not proven). Detail: specialist `notes/agent-package-identity-and-distribution.md` BR-3.
- Skills in Word (accelerator WP-1/WP-5, 2026-11-25..27):
  - Word desktop does not load a skill that ships Python.
  - It does run Office.js that a skill supplies. The code runner wraps it as `async function main(context) { ... }`, with no `Word.run`.
  - The runner does not pass the return value back.
  - It refuses `toString` and a top-level `await`.
  - Detail: accelerator PR #3266.
- Plugin upload refused a package whose `SKILL.md` files were over 20,000 characters ("We couldn't validate this manifest file", accelerator v0.5, 2026-11-27).
  - Learn's published validation rules (Cowork plugin development, checked 2026-11-27) list no `SKILL.md` size limit, so treat 20,000 characters as observed, not documented.
  - Learn says `wiqd plugin validate --mode deep` runs the upload rules locally. Use it, or a size gate, before asking the owner to upload.

## Key sources

- `projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md` (on `work/spaarkeai-mcp-bff-api-r1`)
- `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md` (naming rules)
- `.claude/agent-memory/researcher/agentconnectors-plugin-package-2026-11-20.md` (package shape, install path)
