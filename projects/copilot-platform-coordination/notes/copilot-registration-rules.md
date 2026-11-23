# Copilot registration rules

> **Status: DRAFT, awaiting owner approval** (2026-11-23). Once approved, every project that puts a Spaarke artifact into a Microsoft 365 Copilot tenant follows these rules. Changes go through this project.
> **Applies to:** connectors, plugin/app packages, declarative agents, Copilot Studio agents, Cowork skills, Teams Developer Portal OAuth registrations, and Entra apps made only for Copilot — in the dev tenant and in every customer tenant.

## R1. One tenant registration of the BFF MCP server

Each MCP URL (`/mcp/{area}`) is registered **once** per tenant: either as a federated connector or as one package `agentConnectors` entry, never both, and never in two packages.

- **Why:** when "Spaarke MCP" (federated connector) and "Spaarke MCP spike A" (package connector) both pointed at `/mcp/legal-operations`, one Cowork prompt ran `spike_echo` on both and drew two widgets, and Copilot said so (mcp-r1 `phase0-results.md` T2, 2026-11-23). Write tools would run twice too.
- **Today in the dev tenant:** the single registration is the federated connector **"Spaarke MCP"** (inventory C1). The spike package (C2) breaks this rule until it is removed.
- **Not an off switch:** the Chat Sources toggle does not stop tool calls (T6b). Turning a duplicate "off" there does not fix a double registration; removing it does.

## R2. Packages carry skills (and agents where needed), not a second connector

A Spaarke plugin package adds `agentSkills` and, where a practice needs one, a declarative agent. It does **not** add an `agentConnectors` entry for an MCP URL already registered under R1.

- **Pending evidence:** whether a package skill can drive the tools of a connector registered outside the package is mcp-r1's **T10** ("a skill naming a connector tool", not yet run). If T10 fails, the owner chooses between (a) the package carries the one connector registration and the federated connector is removed, or (b) another shape. This rule is re-decided then; until then, no new package connector for an already-registered URL.
- **Known host gap:** a package connector worked in Cowork but failed in Chat before reaching the BFF (T2/T13/T14). Another reason not to rely on package connectors for Chat yet.

## R3. Naming

As approved on 2026-11-06 in `projects/spaarkeai-mcp-bff-api-r1/notes/coordination/2026-11-06-copilot-naming-and-ownership.md`:

| Thing | Rule |
|---|---|
| Copilot connector | Display "Spaarke" or "Spaarke {Area}", one per product area |
| BFF route | `/mcp/{area-slug}`, kebab-case |
| Plugin package | slug `spaarke-{area}` (`spaarke` for core), explicit display name |
| Skill | `spaarke-{area}-{capability}`; area dropped only in the core plugin. Build gate `^spaarke(-[a-z0-9]+)+$`, ≤ 64 characters |
| MCP tool | `spaarke_{verb}_{object}`, snake_case |
| Declarative agent | "Spaarke AI" for the core agent; practice agents "Spaarke {Practice}" |
| Test artifacts | Must contain `spike`, `probe` or `test` in the display name so they cannot be mistaken for product |

Existing skills `nda-review`, `legal-hold-review` and `accelerator-setup` predate the gate; the accelerator renames them at its next plugin build.

## R4. Inventory first

Before an artifact is uploaded, created or installed in a tenant, its owning project adds a row to `projects/copilot-platform-coordination/notes/tenant-inventory.md` (or sends this session the row): name, kind, owner project, purpose, status and removal plan. Removal of an artifact is also recorded there, in the "Removed" table.

## R5. Throwaway artifacts get a removal date

A test, spike or probe artifact's inventory row names a removal date or a concrete trigger ("after task 040 ships"). When the trigger passes, this session asks the owner to remove it.

## R6. The owner does tenant actions

Every admin-center, Cowork-sharing, Copilot Studio, Teams Developer Portal and Entra change is made by the owner. Sessions write the steps and ask for screenshots; they never sign in, enter credentials, click admin actions or handle secrets (root CLAUDE.md §9, ADR-028).

## R7. Install scope

Test artifacts are installed for **the owner and the test user only** ("Only you" or specific users), not "All users", unless the test needs it; the row says which. (C2 was installed for All users.)

## Open questions for the owner

- **Q1.** Approve R1–R7 as written? (R2 stays provisional until T10.)
- **Q2.** R7 is new (not in the original brief). Keep or drop?
- **Q3.** Do these rules also bind customer tenants (provisioning, `/provision-environment`), or only the dev tenant for now?
