# Spaarke AI Surfaces and Packaging Architecture

> **Last Updated**: 2026-11-25
> **Purpose**: What Spaarke builds for each AI surface (Copilot Chat, Cowork, Word, declarative agents, Spaarke's own apps), and how one capability reaches all of them.

---

## Overview

Spaarke builds each AI capability **once**, in the BFF: a service, an engine, a JPS playbook, a profile. It then ships **thin packaging** for each place an attorney works. What decides the packaging is **who orchestrates the conversation**:
- **Microsoft Copilot** on its own (Chat, Cowork, Word);
- **Microsoft Copilot under Spaarke's instructions** (a declarative agent);
- **Spaarke itself** (the Console's chat, or a Spaarke runtime agent).

The user model **Ask / Do / Practice** says what the attorney wants. This document says what we build for each of the three.

| Intent | The attorney's words | Where it runs |
|---|---|---|
| **Ask** | "Help me understand" | Copilot Chat |
| **Do** | "Carry this out for me" | Copilot Cowork |
| **Practice** | "Do this the firm's way, correctly" | A named Spaarke practice-area agent, or the Spaarke Console |

The key decision: **one MCP server in the BFF is the capability layer for every Microsoft host and every Spaarke agent.** Spaarke's own apps (Console, PCF, Office add-ins, External Access) keep calling BFF services over REST, or in process. They don't go through MCP.

This document replaces [M365-COPILOT-INTEGRATION-ARCHITECTURE.md](M365-COPILOT-INTEGRATION-ARCHITECTURE.md) as the model. The April document still describes the legacy agent and gateway code, which are being retired (see Design Decisions).

## What we build, by orchestrator

| Who orchestrates | Surfaces | What Spaarke ships | Intent | Billing |
|---|---|---|---|---|
| **Microsoft Copilot** | Chat, Cowork (incl. the Office-in-Copilot Word canvas), Word | **MCP connector tools** (tenant connector "Spaarke MCP") + **skills** in a **plugin package**. **No agent.** | Ask, Do | Chat: licence. Cowork: credits per step |
| **Microsoft Copilot, under Spaarke's instructions** | Chat, Word, Teams (`@Spaarke Patents`) | One **coded declarative agent per practice area**, with its own MCP action, generated from the persona definition | Practice | Licence |
| **Spaarke** | Console chat (SprkChat); optionally a Spaarke runtime agent in Chat and Teams | Nothing new: the existing governed agent loop, JPS playbooks, confirmation gate and Decision Record | Practice, where the profile must be enforced in code | Spaarke's Azure OpenAI spend |

All three call the same BFF functions. An engine, its golden set and its output contract are written once; only the orchestrator and the packaging differ.

In **Cowork** a persona is its **skills + the attorney's profile**, because Cowork cannot run declarative agents yet. Home puts Chat and Cowork behind one composer, so the build generates both wrappers from one persona definition.

### Surface constraints that shape the packaging

The capability matrix was researched on 2026-11-23 and changes quickly; re-verify before relying on a cell. The full matrix, with sources, is on branch `work/specialist-legal-agents-r1`, in `projects/specialist-legal-agents-r1/notes/research/copilot-surfaces-post-ignite-2026-11-23.md`. The constraints that matter for packaging:

- **Declarative agents:** run in Chat, Word, Teams and classic Outlook, **not in Cowork** (Frontier preview only).
- **Skills (`SKILL.md`):** shared by Chat and Cowork. Word lists them but doesn't load them.
- **Python engine scripts:** run in Cowork (with `lxml`). Chat's sandbox lacks `lxml`. Word and Outlook can't run them. **So engines that must work everywhere live in the BFF, behind a tool.**
- **MCP Apps widgets:** render in Chat and Cowork, and in Word through a declarative agent.
- **Word:** a declarative agent can read and advise but can't edit. Tracked changes come from Microsoft's Word legal plugin, which Spaarke feeds with the attorney's exported positions as a Word playbook.

## Component Structure

| Component | Path | Responsibility |
|---|---|---|
| Capability (services, engines, playbooks) | `src/server/api/Sprk.Bff.Api/Services/` | The one implementation every surface reaches |
| MCP server | `src/server/api/Sprk.Bff.Api/Api/Mcp/` (`McpEndpoints.cs`, `McpToolAuthorization.cs`) | `/mcp/{area}` routes; tools are thin adapters over services, run as the caller. See [SPAARKE-MCP-SERVER-ARCHITECTURE.md](SPAARKE-MCP-SERVER-ARCHITECTURE.md) |
| MCP Apps widgets | `src/client/widgets/` (e.g. `spaarke-decisions/`) | In-conversation UI, served as MCP resources |
| Plugin package (skills) | `src/solutions/CopilotPlugins/spaarke/` | Skills-only Microsoft 365 app package. The connector is registered once per tenant, not inside the package |
| Spaarke runtime: agent loop | `Services/Ai/Chat/SprkChatAgentFactory.cs`, `Services/Ai/Chat/SideEffectGateAIFunction.cs`, `Services/Ai/Chat/Gate/ConfirmationPolicyEngine.cs`, `Api/Ai/ChatEndpoints.cs` | Governed multi-step tool loop with a confirmation gate; the Practice engine when Spaarke orchestrates |
| Spaarke runtime: playbooks | `Services/Ai/AnalysisOrchestrationService.cs`, `Services/Ai/Chat/SessionDispatchOrchestrator.cs` | JPS playbook execution and session dispatch |
| Work IQ seam | `Services/Ai/PublicContracts/IOrganizationalContextProvider.cs` | Where M365 organisational context plugs in for a runtime agent (no provider wired yet) |
| **Legacy:** "Spaarke AI" agent | `src/solutions/CopilotAgent/` | A general declarative agent on an OpenAPI plugin; retire once MCP has parity (mcp-r2) |
| **Legacy:** agent gateway | `src/server/api/Sprk.Bff.Api/Api/Agent/` | REST gateway for the legacy agent. Its message handler is not a live Bot endpoint |

## Data Flow

**Ask, in Copilot Chat (no agent):**
1. The attorney asks a question; Copilot picks a Spaarke tool from the connector's description and the tool descriptions.
2. Copilot calls `POST /mcp/{area}` with the attorney's token. The tool calls the BFF service on the attorney's behalf, under the same rules as the Console.
3. The tool returns text, structured content and, optionally, a widget; a write asks for approval on Copilot's card first.

**Practice, in a practice-area declarative agent:**
1. The attorney opens `@Spaarke {Area}`. The agent's instructions are the **same for every user** (one manifest).
2. The agent calls a tool that returns that attorney's **compiled profile**: archetype, firm policy, their own positions, and the matter overlay. Personalisation comes from the server, not from the manifest.
3. The agent runs the area's functions as tools. The BFF applies the gates and records the run.

**Practice, in the Spaarke Console:**
1. SprkChat runs the governed loop in process. Tools are `AIFunction`s over the same services; there is no MCP hop.
2. A side effect suspends on the confirmation gate; the outcome is written to the Decision Record.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|---|---|---|---|
| Depends on | MCP server | `/mcp/{area}` | Every Microsoft host and Spaarke agent reaches Spaarke here |
| Depends on | Auth | ADR-028 (A10 for MCP tokens) | Tools run as the caller; no app-only path from Copilot |
| Depends on | AI platform | [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md), [chat-architecture.md](chat-architecture.md) | Playbooks, engines and the agent loop are the capability |
| Consumed by | Specialist legal agents | Persona definition → generated agent + skills | One archetype per practice area; the attorney profile tunes it |
| Consumed by | Intake pipeline | BFF services, not MCP | Email and form intake run server-side, with no Copilot in the loop |

## Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| How Copilot reaches Spaarke | **MCP**, not REST (OpenAPI plugins) | Copilot can call REST through an API plugin, but only inside a declarative agent. The no-agent routes (the tenant connector, Cowork plugin connectors) are MCP. Only MCP brings MCP Apps widgets, tool annotations that drive Copilot's approval cards, and a tool list served at run time (no app-package redeploy when a tool changes). The same server also serves any MCP client (Copilot Studio, Foundry, other vendors' agents) |
| Is MCP an extra layer? | No: an adapter, like a REST endpoint | Tools run inside the BFF and call the same services the REST routes call. No second HTTP hop, no second token exchange, one owner per business rule |
| Where REST stays | Spaarke's own apps, server-to-server work, webhooks, intake | No model in the loop, so MCP adds nothing |
| Connector granularity | **One server; one route and connector per product area** (`/mcp/legal-operations`, later `/mcp/patents`, …) | Copilot's tool choice degrades as one list grows; admins enable what was bought; one Connect per area |
| Per-agent connectors | **No** | An agent is configuration over shared functions; a per-agent connector would copy each function into every agent |
| When to create an agent | Only where Spaarke adds a **governed professional method** (playbook, profile, gates, verification); **one per practice area** | Data, search and CRUD are tools on the connector. A general "ask Spaarke" agent is an anti-pattern |
| Role (partner, associate) | A **profile setting**, not a separate agent | Avoids an area × role catalogue in the `@` picker |
| Personalisation | **Server-side profile**, returned by a tool and versioned per run | A declarative agent's manifest is shared by all users and capped at 8,000 characters of instructions |
| Long-form drafting (e.g. patent applications) | **Cowork** (with its Word canvas) and/or a practice-area agent session; plain Word is not the preferred surface (owner, 2026-11-25) | Cowork can produce and edit documents; a declarative agent in Word can only advise |
| Legacy "Spaarke AI" agent | Keep the code; **retire the tenant registration once MCP has parity** (mcp-r2) | Its 28 REST functions are the Ask tool checklist for MCP |

### Decision order for a new AI capability

1. Does Microsoft own the function? Then feed it Spaarke content (positions, data); don't compete.
2. Is the outcome an answer? Make it a **tool** (and a skill if it needs a procedure). → Ask
3. Is it delegated multi-step work? Make it **skills + tools**. → Do
4. Must it follow a prescribed method? Make it part of a **practice-area persona**. → Practice
5. Does it need control that Copilot's orchestrator can't give (profile enforced in code, state, gates)? Use the **Spaarke runtime**.
6. Is it only search or CRUD? It is **not an agent**.

## Constraints

- **MUST**: implement a capability once, in BFF services; MCP tools, `AIFunction`s and REST endpoints are adapters over it.
- **MUST**: expose Spaarke to Microsoft hosts and Spaarke agents through the one MCP server, as a tool on the right `/mcp/{area}` route.
- **MUST NOT**: add a second MCP server, a per-agent connector, or a new OpenAPI plugin action for Copilot.
- **MUST NOT**: create an agent to expose data, search or CRUD.
- **MUST**: load per-user behaviour from the server-side profile, not from per-user agent manifests.
- **MUST**: put engines that must run on every surface in the BFF; a Cowork-only Python engine is acceptable only for the file-based accelerator tiers.
- **MUST NOT**: route Spaarke's own apps or server-side intake through MCP.

## Related

- [SPAARKE-MCP-SERVER-ARCHITECTURE.md](SPAARKE-MCP-SERVER-ARCHITECTURE.md): the server, auth and tools
- [M365-COPILOT-INTEGRATION-ARCHITECTURE.md](M365-COPILOT-INTEGRATION-ARCHITECTURE.md): the legacy agent and gateway (being retired)
- [chat-architecture.md](chat-architecture.md): SprkChat, the Spaarke runtime
- [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](ASSISTANT-SURFACE-LAUNCH-MECHANISM.md): surfaces inside Spaarke's own apps
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md): auth
- Program design (on branch `work/specialist-legal-agents-r1`): `projects/specialist-legal-agents-r1/design.md` §0.5, §3.9–§3.12
