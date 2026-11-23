# G9 — Can a Spaarke runtime agent appear where attorneys work, and can it use Work IQ?

> **Type:** desk research (no tenant changes). **Date:** 2026-11-23, about a week after Ignite 2026.
> **Method:** a researcher agent (opus, high effort) using Microsoft Learn and official 2026 sources. The main session then re-checked the claims G9 depends on (see "Verification").
> **Decides:** D-A1 (the T3 half), T-7(c), and whether a runtime agent is a candidate at all.

## Verdict

| Surface | Custom engine agent (Spaarke reasons, Copilot or Teams shows the chat) |
|---|---|
| **M365 Copilot Chat** (agent list, @mention) | **GA.** Users need **no Copilot licence** to use it there |
| **Teams** | **GA** |
| **Word** (and Excel, PowerPoint, Outlook, Edge) | **Not supported**, according to the Learn known-issues page (verified 2026-11-23, ms.date 2026-11-16). An older message-center post (MC1168289, 2025-10) and release notes say otherwise. **Plan on no** until a tenant test shows yes |
| **Cowork** | **Not documented.** Even declarative agents in Cowork are Frontier private preview. **Plan on no** |

**For the program:** a runtime agent is a **Chat/Teams-only front door**. It cannot be the Word front door, and it cannot call Microsoft's legal skills, because skills are not reusable across agents. That confirms D-A1's default: Practice in Word and Cowork stays the **coded declarative agent plus skills**. A runtime agent becomes an optional T3 channel for long, governed, multi-step work in Chat or Teams.

**A code correction (verified):** the BFF has no live custom-engine endpoint today (see "What this means for our code"). G9 would be a **build**, not just a matter of exposing what we have.

## Status table

| Capability | Status | Source | Date |
|---|---|---|---|
| Custom engine agent in Copilot Chat and Teams | GA | learn.microsoft.com/microsoft-365/copilot/extensibility/overview-custom-engine-agent | 2026-11-09 |
| Custom engine agent in Word, Outlook, Excel, PowerPoint, Edge | **Not supported** (known issues) | learn.microsoft.com/microsoft-365/copilot/extensibility/known-issues | 2026-11-16 |
| Same, "Insert into document" (Copilot Studio / AI Toolkit agents) | GA per message center (contradicts the row above) | MC1168289 | 2025-10-08 |
| Custom engine agent in Cowork | Not documented | — | — |
| Copilot → agent handoff (deep link) | Developer preview | learn.microsoft.com/microsoftteams/platform/bots/how-to/conversations/bot-copilot-handoff | 2024-09-16 |
| Custom engine agent in Chat with no Copilot licence | Allowed (GA) | learn.microsoft.com/plugins/prerequisites | 2026-11-16 |
| Agent 365 (registry, governance, Entra agent identity; **not hosting**) | GA, per-user licence | learn.microsoft.com/microsoft-agent-365/overview | 2026-11-11 |
| Agent 365 agent user account, Word-comment @mention | Frontier preview | learn.microsoft.com/microsoft-agent-365/developer/agent-365-sdk | 2026-11-05 |
| Foundry agent "publish to Copilot and Teams" | Documented; no citations, no file upload; Foundry becomes the orchestrator | learn.microsoft.com/azure/foundry/agents/how-to/publish-copilot-virtual-network | 2026-11-12 |
| Bot Framework SDK | Archived; support ended 2025-12-31. **Azure Bot Service is still the channel** for SSO/OAuth agents | learn.microsoft.com/azure/bot-service/what-is-new | — |
| Bot OAuth connection with a federated credential | **Single-tenant app only**; multitenant "coming soon" (verified) | learn.microsoft.com/microsoft-365/agents-sdk/azure-bot-user-authorization-federated-credentials | 2026-05-13 / 10-16 |
| Work IQ A2A, REST, remote MCP | GA (2026-06-16) | learn.microsoft.com/microsoft-365/copilot/extensibility/work-iq/api-overview | 2026-11-03 |
| Work IQ MCP `retrieve` | Public preview | …/work-iq/mcp/retrieve | 2026-11-11 |
| Foundry Work IQ tool | Preview | learn.microsoft.com/azure/foundry/agents/how-to/tools/work-iq | 2026-11-09 |
| Work IQ scopes `WorkIQ.Read`, `WorkIQ.Act` | New, delegated, admin consent | …/work-iq/permissions | 2026-10-30 |

## Answers

**Q1. Pro-code models.**
- **M365 Agents SDK custom engine agent (GA)** is the pro-code path: we host, and we choose the model and orchestrator. The current .NET package is 1.9.x; the BFF pins 1.0.1.
- **Agent 365** is governance, registry and identity, not hosting. Custom engine agents use standard app registrations with an Azure Bot OAuth connection, not Agent 365's agent identity chain.
- **Foundry "publish to M365"** makes Foundry the orchestrator. Spaarke's playbook runtime would sit behind it, not in front, and the path has no citations.
- **Copilot Studio "bring your own model"** changes prompts only; Studio keeps its own orchestrator. Studio can also call an Agents SDK agent as a connected agent, which is usage-billed.

**Q2. Surfaces.**
- Chat and Teams: yes.
- Word and Outlook: no, per known issues (see Verdict).
- Cowork: undocumented.
- In Chat, an @mentioned custom engine agent **cannot see the earlier Copilot conversation**.
- Chat also has no file attachments or downloads, no proactive messages, immutable messages, no Action.Execute card refresh that persists, and no sensitivity labels.
- Admin deployment: MAC › Agents › Registry, with "available to / installed for" by user or group (fits R7).

**Q3. SSO and auth.**
- The user's identity arrives through the **Azure Bot OAuth connection**: Teams/Copilot SSO token exchange on `api://botid-{appId}`, with the M365 client IDs pre-authorized.
- The agent gets the user token in the turn (`UserAuthorization`) and does **OBO** to Graph, Dataverse and Work IQ.
- Entra agent identity (Agent 365) does not apply to custom engine agents.
- **The secret-free limit:**
  - The OAuth connection with a federated credential supports a **single-tenant** app only.
  - Channel auth can use MI-as-FIC.
  - So the design can be fully secret-free only when the bot app, the OAuth app and the users are all in **one tenant**: the Model 2 per-customer stamp.
  - A Spaarke-tenant multitenant bot (Model 1) would need a **certificate** (or secret) until Microsoft ships multitenant FIC. ADR-028 A4 does not allow that without an exception.
- Work IQ requires the token issuer to be the user's **home tenant**, so B2B guests fail.

**Q4. Work IQ.**
- A2A, REST and remote MCP are GA. `retrieve` and the Foundry tool are preview.
- **Billing:** no Copilot licence needed; metered in Copilot Credits ($0.01 each). The Tools API is 0.1 credit per call; Chat and Context are "variable". Custom agents are **always metered**. Each user must be in a spending policy that includes Work IQ API.
- **OBO:** yes, scope `api://workiq.svc.cloud.microsoft/WorkIQAgent.Ask`, with admin consent.
- **Results:** security-trimmed to the user, with citations and per-source metadata (including sensitivity label). No published latency figures; 429s pass through.
- **Federated connectors:** included in results only if the user connected them in Chat. **A loop risk:** Spaarke's own connector data could come back to us through Work IQ, so the runtime must filter its own source.

**Q5. Microsoft's legal skills from a custom engine agent.** No documented path ("reuse across agents: not supported"). A possible indirect route is Work IQ `ask(agentId)` from `list_agents`; whether the legal plugin is listed there is unknown, so a tenant check is needed.

**Q6. Cost.** No Copilot licence and no message metering to use the agent in Chat. Our costs are hosting, Bot Service, our own Azure OpenAI, and Work IQ credits per call. Agent 365 licences are only needed for its governance features.

## What this means for our code (verified 2026-11-23)

- **`POST /api/agent/message` is not an Activity-protocol endpoint.** It accepts a custom `AgentMessageRequest` JSON body under the BFF's own Entra JWT (`Api/Agent/AgentEndpoints.cs:35-58`, `.RequireAuthorization()`). Nothing maps a `CloudAdapter` or `/api/messages`.
- **`SpaarkeAgentHandler : ActivityHandler` is not registered anywhere.** Its constructor takes only a logger, and the file carries TODOs for every dependency (`SpaarkeAgentHandler.cs:25-29`). **Azure Bot Service cannot reach the BFF today.**
- **`AgentTokenService` does OBO on the incoming HTTP `Authorization` header.** On a real Bot channel that header holds a Bot Connector service token, not the user's token. The user token has to come from `UserAuthorization` inside the turn. Its MSAL OBO client on the MI-FIC credential can be reused as the OBO connection.
- **The runtime itself (governed loop, playbooks, gates, ledger) is unaffected.** Only the front door is missing.

If G9 is built, the work is:
1. Upgrade `Microsoft.Agents.Hosting.AspNetCore` 1.0.1 → 1.9.x.
2. Add `AgentApplication` with Bot-token validation on its own route (`/api/messages`).
3. Set up the Bot OAuth connection: FIC, single-tenant, per-customer stamp; `api://botid-{appId}` SSO.
4. Add a Work IQ delegated scope and implement `IOrganizationalContextProvider`.
5. Write a T-7(c) ADR-028 amendment covering the Bot-channel client plane, the Work IQ downstream, and the Model 1 certificate question.
6. Record the ADR-052 placement (BFF vs a separate agent host) under BFF hygiene.

## Spike list (only if the owner wants a runtime agent)

1. Install a minimal custom engine agent. Check whether it appears in **Word** (desktop and web) and Outlook, and whether Insert works. This settles the known-issues vs MC1168289 conflict.
2. Check SSO token exchange in **Copilot Chat** (not only Teams), with FIC.
3. OBO to Work IQ, Dataverse and Graph; measure Work IQ latency and credits per turn.
4. Model 1 cross-tenant: confirm the FIC limit and try a certificate fallback (needs an ADR-028 A4 exception first).
5. Work IQ `list_agents` / `ask(agentId)` against Microsoft's legal plugin.
6. Confirmation-gate cards under immutable messages (no persisted Action.Execute refresh).

## Verification

The main session re-checked these against primary sources on 2026-11-23:
- **Known issues, custom engine agents:** the "Microsoft 365 app support" row reads: custom engine agents aren't supported in Outlook, Word, Excel, PowerPoint and Edge. ✅
- **The federated-credential OAuth page:** "Single Tenant Microsoft Entra Application supports only AAD v2 with Federated Credentials … Support for multitenant apps is coming soon." ✅
- **The code claims:** `AgentEndpoints.cs` and `SpaarkeAgentHandler.cs` read directly, and grep finds no `CloudAdapter`, `/api/messages` or handler registration in `Sprk.Bff.Api`. ✅

Not re-checked (taken from the researcher, with URLs above): the Work IQ pricing figures, the Agent 365 details, and the Foundry publish limits.
