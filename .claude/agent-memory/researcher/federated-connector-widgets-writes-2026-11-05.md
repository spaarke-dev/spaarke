---
name: federated-connector-widgets-writes-2026-11-05
description: Federated connector vs Cowork plugin vs declarative agent for an MCP server with a widget and writes — surfaces, widget rendering, write consent, auth, Work IQ (Learn, 2026-11-05)
metadata:
  type: reference
---

**Question.** For project `spaarkeai-mcp-bff-api-r1` (an MCP server in the BFF, a Decisions widget, a write), which registration model fits? The owner prefers a federated connector, which avoids an agent context switch.

**Tags.** C = primary source (Learn and Message Center pages, retrieved 2026-11-05). S = secondary. U = unknown.

## Federated connector
- **Status (C):**
  - GA 2026-05-05 (RM501120).
  - **Writes GA October 2026 (MC1476316)**, with per-tool approval. The connectors overview page still says "read-only" and is stale.
- **Creation (C):**
  - **Per tenant:** admin center › Copilot › Connectors › Gallery › Created by your org › Connect to MCP server. Enter a description (≤ 4,000 chars) and a Teams Developer Portal registration ID. Needs the Global Admin or AI Admin role.
  - **ISV:** a Partner Center plugin package; certified Gallery listing; each tool needs `readOnlyHint` and a title.
- **Auth (C):**
  - Options: Entra SSO, OAuth 2.0 (Dev Portal registration, PKCE optional), or none.
  - The ISV gallery path documents only OAuth.
- **Licence (C):** a Copilot licence (overview page, 2026-10-19).
- **Surfaces:**
  - Chat and Cowork confirmed (C).
  - Word, Excel, PowerPoint and Outlook: Learn pages conflict (U).
  - Researcher: read-only.
  - Teams: not listed.
- **Invocation (C):** after a one-time Connect in Settings › Sources, Copilot picks the tools at runtime. **No agent picker, no context switch.**
- **Widgets:** **nothing documented (U).** M365 Copilot documents widgets only for declarative agents. Vendors that ship widgets (monday, Canva) also ship declarative agents (S), which is evidence against widget support in connectors.

## Cowork plugin (`agentConnectors`, app manifest 1.29/1.30)
- **Widgets:** render in Cowork (C). Copilot Chat: U.
- **Write confirmation:** for third-party servers, "rolling out progressively" (C).
- **Convergence (S):** the plugin package and the ISV connector appear to be converging on one Microsoft 365 app package.

## Writes
- **Connector, model-initiated:** an approval card with Allow once / for the conversation / always (C).
- **Declarative agent:** a prompt on every non-read call; `destructiveHint` is ignored (C).
- **Widget-initiated `callServerTool` writes:** U in every host. The MCP Apps spec (2026-01-26) says the host "**MAY**" ask for approval. *Correction: the earlier 11-05 note said "SHOULD".*
- **Design rule:** never rely on the host to confirm a write. Enforce it on the server: an explicit action, idempotency, audit.

## Instructions and Work IQ
- **Connector:** carries tool descriptions (≤ 1,024 chars read) plus the admin-entered description. No documented host reads the MCP `instructions` field (U).
- **Work IQ (C, Learn 2026-10-26):** the workplace intelligence layer (chat, context, tools, workspaces) plus an API. Connectors "enrich Work IQ".

## One server, three registrations
Yes, by configuration only (C/S):
- accept each registration's token audience;
- annotate every tool;
- respond in under 30 s;
- run statelessly.

## Spaarke-specific consequence (ADR-028 A8)
Entra SSO through Copilot issues home-tenant tokens. For Model 1 guests, use OAuth with the authority pinned to Spaarke's tenant. See [[mcp-server-dotnet-bff-copilot-widget-2026-11-05]] and [[b2b-guest-vs-multitenant-home-authority-2026-10-13]].

**Sources:**
- Learn: `federated-connectors-overview`, `set-up-custom-federated-connectors`, `submit-federated-connector`, `plugin-mcp-apps`, `plugin-confirmation-prompts`, `plugin-authentication`, `isv-publisher-guide`, `work-iq`, Cowork `mcp-apps-support` and `cowork-plugin-development`;
- MC1476316;
- Tech Community GA blog (2026-05-05);
- devblogs "MCP Apps now available in Copilot Chat" (2026-04-07).
