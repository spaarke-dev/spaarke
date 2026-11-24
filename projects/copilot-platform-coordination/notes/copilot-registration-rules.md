# Copilot registration rules

> **Status: DRAFT, awaiting owner approval** (2026-11-23). Once approved, every project that puts a Spaarke artifact into a Microsoft 365 Copilot tenant follows these rules. Changes go through this project.
> **Applies to:** connectors, plugin/app packages, declarative agents, Copilot Studio agents, Cowork skills, Teams Developer Portal OAuth registrations, and Entra apps made only for Copilot — in the dev tenant and in every customer tenant.

## R1. One tenant registration of the BFF MCP server

Each MCP URL (`/mcp/{area}`) is registered **once** per tenant: either as a federated connector or as one package `agentConnectors` entry, never both, and never in two packages.

- **Why (revised 2026-11-23, see `copilot-structure.md` §3):** each tool call goes to one server; the platform does not fan out. But a second registration gives the model a second copy of every tool, and it may call one, the other, or both. When "Spaarke MCP" (federated connector) and "Spaarke MCP spike A" (package connector) both pointed at `/mcp/legal-operations`, one Cowork prompt ran `spike_echo` on **both** and drew two widgets ("there are now two of them, so I ran both"; mcp-r1 `phase0-results.md` T2). Duplicates also add ambiguity, double the tool descriptions in context, and may sign in as a different identity. They add nothing.
- **Status: holds as written.** S-1 (2026-11-24) found no visible duplicate on the current build in Chat or Cowork, but T2 (2026-11-23) did see a double call. Duplicates can bite and add nothing, so the rule stays.
- **Today in the dev tenant:** the single registration is the federated connector **"Spaarke MCP"** (inventory C1). The spike package (C2) breaks this rule until it is removed.
- **Not an off switch:** the Chat Sources toggle does not stop tool calls (T6b). Turning a duplicate "off" there does not fix a double registration; removing it does.
- **Proposed amendment R1′ (specialist-legal-agents-r1, 2026-11-23; NOT decided — owner):** "**One tenant-wide registration per URL** (federated connector or package connector). **Agent-private MCP actions on the same URL are allowed**, using the same OAuth registration (A1)." Reason: G8 (S-3) showed an Agent Builder agent cannot see the tenant connector C1 at all, so persona agents can only reach our tools through their own action. An agent's action is documented as private to that agent [D], and inside the agent C1 is not visible [O], so the model should see one copy inside the agent and none outside. The T2 double call came from two **tenant-wide** registrations both visible in plain Chat. **Evidence (G8-P2, 2026-11-24): passes in Chat.** An org-catalog agent with its own action on `/mcp/legal-operations` (A1 OAuth) made exactly one `tools/call` per turn and rendered the widget (App Insights). Still to confirm: Word and Teams, and that plain Chat's call count is unchanged with the agent installed. **Ready for the owner's decision.**
- **Packaging note (G8-P2):** an org-catalog update of an existing app id did not load a changed action (five versions of `802fd9cc`); a fresh app id did. Until this is understood, a test agent whose action changes gets a new app id.

## R2. Packages carry skills (and agents where needed), not a second connector

A Spaarke plugin package adds `agentSkills` and, where a practice needs one, a declarative agent. It does **not** add an `agentConnectors` entry for an MCP URL already registered under R1.

- **Status (2026-11-24): skills part CONFIRMED in Cowork by T10** — a skills-only package drove the tool through the separate federated connector, so no package needs its own connector and no "core connector package" is needed (mcp-r1 option (a) stands). Chat not yet tested. **Agents part (2026-11-24):** an agent cannot use the tenant connector (G8), but it can use its own private MCP action, and in Chat it called once per turn (G8-P2). So a packaged agent carries its own action on the same URL, which is allowed under R1′. Earlier: NOT confirmed (owner, 2026-11-23). Microsoft documents skills naming tools "from the same package"; it says nothing about a skill using a server registered outside its package. Agents: G8 (S-3, 2026-11-23) showed an Agent Builder agent cannot use the tenant connector, so an agent that needs our tools needs its own private action (see R1′). The packaged-agent case (G8-p) is still to run.
- **Pending evidence:** whether a package skill can drive the tools of a connector registered outside the package is mcp-r1's **T10** ("a skill naming a connector tool", not yet run). If T10 fails, mcp-r1's fallback (2026-11-23) is one "core" package carrying the single connector registration (the federated connector is then removed), with every other package carrying skills only; that still satisfies R1. The owner decides. This rule is re-decided then; until then, no new package connector for an already-registered URL.
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

Existing skills `nda-review`, `legal-hold-review`, `accelerator-setup` and `nda-library-review` predate the gate. The new names were agreed between sessions on 2026-11-06 (accelerator OD-42: `spaarke-nda-review`, `spaarke-nda-setup`, `spaarke-nda-library-review`, plugin `spaarke-nda`). OD-42 still needs the owner's decision, and the rename happens at the first build after OD-20, which waits on the Word probe (WP-2). OD-42 stays the accelerator's decision record; approving R3 here does not by itself settle it unless the owner says so.

## R4. Inventory first

Before an artifact is uploaded, created or installed in a tenant, its owning project adds a row to `projects/copilot-platform-coordination/notes/tenant-inventory.md` (or sends this session the row): name, kind, owner project, purpose, status and removal plan. Removal of an artifact is also recorded there, in the "Removed" table.

## R5. Throwaway artifacts get a removal date

A test, spike or probe artifact's inventory row names a removal date or a concrete trigger ("after task 040 ships"). When the trigger passes, this session asks the owner to remove it.

## R6. The owner does tenant actions

Every admin-center, Cowork-sharing, Copilot Studio, Teams Developer Portal and Entra change is made by the owner. Sessions write the steps and ask for screenshots; they never sign in, enter credentials, click admin actions or handle secrets (root CLAUDE.md §9, ADR-028).

## R7. Install scope

Test artifacts are installed for **the owner and the test user only** ("Only you" or specific users), not "All users", unless the test needs it; the row says which. (C2 was installed for All users.)

## R8 (proposed). Write tools are safe to repeat

A model can call a tool twice (a duplicate registration, a retry, or a skill that says "try again"). Every Spaarke MCP write tool checks before it creates, or takes a caller key, so a repeat cannot create a second record. The Spaarke MCP server already meets it: its only real write tool, `spaarke_complete_decision`, is app-only and replay-safe through `DecisionCommitService` (idempotency key, stored outcome returned on repeat; mcp-r1 FR-08 / NFR-06; deliberate "send again" = new intent, new key). R8 therefore binds **other** projects' write tools (for example the specialist agents' T2/T3 tools). Spike tools are exempt as throwaways.

## Which rules wait on structure tests

| Rule | State | Waits on |
|---|---|---|
| R1 one registration | Holds; amendment R1′ (agent-private actions allowed) passed G8-P2 in Chat 2026-11-24 | owner; Word/Teams and the plain-Chat leak check |
| R2 packages carry skills/agents, not a connector | Skills part confirmed (T10, Cowork); agents part confirmed with R1′ (G8-P2, Chat) | owner; a Chat run of T10 |
| R3 naming | Holds for new artifacts | accelerator OD-42 for existing skills |
| R4–R7 process rules | Independent of structure | — |
| R8 safe-to-repeat writes | Proposed; mcp-r1 already compliant and agrees it fits other projects | owner |

Tests S-1 to S-4 are in `copilot-structure.md` §4.

## Open questions for the owner

- **Q1.** Approve the rules? **Owner 2026-11-24: wait for G8-P2** (agents part of R2 and R1′), then decide all rules together. T10 (skills part of R2) passed 2026-11-24.
- **Q2.** R7 is new (not in the original brief). Keep or drop?
- **Q3.** Do these rules also bind customer tenants (provisioning, `/provision-environment`), or only the dev tenant for now?
