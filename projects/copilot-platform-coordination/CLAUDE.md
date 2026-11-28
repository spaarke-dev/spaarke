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
- In Cowork, a skills-only plugin reaches the tools only while the "Spaarke MCP" source is switched on (specialist G1 load check, 2026-11-28). Unlike the Chat Sources switch (T6b), this one did gate the tools.
- Each declarative agent's MCP action asks every user to sign in to it separately ("Please sign in to Spaarke MCP G1c", demo@, 2026-11-28), even though all the actions use A1's OAuth reference id. Package connectors sharing a reference id did share one sign-in (T14). Plan a first-use sign-in per agent per user.
- When an agent is asked to "list every tool", its first answer in a new chat may name none. It does name them after it has called a tool once in that chat, or on a retry in a new chat (G5, 2026-11-28). An empty first list does not mean the tools failed to load.
- Two package connectors sharing one OAuth `referenceId` share one sign-in (T14).
- An org-catalog **update** that changes a declarative agent's action takes the action offline for a while. In V1 (2026-11-24) it was still missing ~25 min after one upload, and it was back at ~40 min after another. Test an updated agent about 45 min after upload, not minutes after. This probably explains the five failed `802fd9cc` versions, each tested within minutes (not proven). Detail: specialist `notes/agent-package-identity-and-distribution.md` BR-3.
- Skills in Word (accelerator WP-1/WP-5, 2026-11-25..27):
  - Word desktop does not load a skill that ships Python.
  - It does run Office.js that a skill supplies. The code runner wraps it as `async function main(context) { ... }`, with no `Word.run`.
  - The runner does not pass the return value back.
  - It refuses `toString` and a top-level `await`.
  - Word has two code runners (WP-6, 2026-11-27). The Office.js runner works through a sub-agent and returns nothing. The HTML-document runner returns values and errors, but Copilot wrapped our code in a stand-in API there. A Copilot-filled edit list applied through fixed Office.js was hash-exact, carrying 17 KB of code.
  - Detail: accelerator PR #3266.
- **The admin center refuses a plugin whose app id was already used by a Cowork › Customize › Plugins upload, even after that upload is removed.** The message is only "We couldn't validate this manifest file".
  - Proof (2026-11-27, accelerator P1/P1b): the same no-script zip was refused on the Word Probe's Cowork-used app id and accepted on a fresh id.
  - Earlier refusals on that id (v0.5, v0.6, v0.7) are explained by this, not by `SKILL.md` size or a 0.x version.
  - Learn does not document it. The owner has hit it before.
  - So a package tested through Cowork needs a different app id for the admin route. `build_accelerator.py` gives each package a fixed uuid5 id, so admin builds need `--fresh-id` or a different seed.
  - Untested: whether the admin center accepts a skill that ships Python on a fresh id.
- Local validation:
  - `atk validate` checks the Teams manifest only, not `agentSkills` or `SKILL.md`.
  - Learn says `wiqd plugin validate --mode deep` runs the full upload rules. It is not installed on this machine (2026-11-27), and installing it is a download that needs the owner's OK.

## Key sources

- `projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md` (on `work/spaarkeai-mcp-bff-api-r1`)
- `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md` (naming rules)
- `.claude/agent-memory/researcher/agentconnectors-plugin-package-2026-11-20.md` (package shape, install path)
