---
name: copilot-entry-points-mention-slash-prompts-2026-11-05
description: How users start a federated connector / plugin skill in Copilot Chat and Cowork — @ mention, / skills, organizational prompts, conversation starters (Learn + Message Center, 2026-11-05)
metadata:
  type: reference
---

**Question.** For project `spaarkeai-mcp-bff-api-r1` (federated connector plus Cowork plugin package; the "Spaarke AI" declarative agent is a fallback only): can users start it with @, with /, or from a suggested prompt?

**Tags.** C = primary source (Learn and Message Center, retrieved 2026-11-05). S = secondary. U = unknown.

## Findings
- **"@" in Copilot Chat** opens the agent picker; the agent joins the thread with an agent tag (C, Learn *agent-entry-points*, 2026-09-30). **A connector can't be @-mentioned (U):** nothing documents it. RM569213 adds an "Agents and Skills" tab reached by / or @ (GA target September 2026; whether it is live is S).
- **"/" in Chat** opens the Context IQ reference menu: people, files, meetings and Graph connector content (C). Federated (MCP) connectors don't index content, so they're unlikely to appear there (U).
- **Skills in Chat:** MC1493223 (2026-10-09; rollout October to mid-November 2026). Copilot uses relevant skills automatically, and the user can also pick one. Admin-deployed plugins reach both Chat and Cowork.
- **Cowork:** "type `/` in the prompt box or use the + menu" to pick a skill, or Cowork chooses one automatically (C, *cowork-available-plugins*, 2026-11-02). Claude `commands/` are "Not yet supported" (C). Plugins are toggled in Sources & Skills.
- **Organizational prompts** (C, Learn *organizational-prompts*, updated 2026-11-04):
  - published by the AI Administrator or Search Editor role, in the admin center › Copilot › Prompts;
  - up to 1,000 per tenant; CSV import;
  - **pin up to 4** under Suggested on the Chat home page; all appear in autosuggest typeahead and the prompt lab;
  - Chat, Edge and Teams; about 3 hours to appear;
  - no group targeting and no API documented (U);
  - **an ISV can't push them; the customer admin publishes them.**
- **Conversation starters:** declarative agents only, up to 12 (C).
- **Federated connector submission** needs 3 or more sample prompts, but they go to Microsoft's reviewers, not users (C/U).
- **Discoverability:** Learn tells users to name the data source in the prompt (C). No in-chat indicator for an available connector is documented (U).

## Recommendation used in design §5.4
- a distinctive plugin skill (`spaarke-decisions`) for `/`;
- display name "Spaarke", with tool descriptions phrased for "…in Spaarke";
- an org-prompt CSV in the onboarding kit;
- "@" only through the declarative-agent fallback.

**Tests:** skill label under `/` and `@` in Chat; whether `/skill` in Cowork renders the widget; routing by source name; whether org prompts reach Cowork and Teams.

**Sources.** Learn `cowork-available-plugins`, `cowork-plugins`, `cowork-manage-plugins`, `cowork-plugin-development`, `organizational-prompts`, `federated-connectors-overview`, `submit-federated-connector`, `agent-entry-points`, `declarative-agent-manifest-1.6`; MC1493223 and RM569213 (via the mc.merill.net mirror).

Related: [[federated-connector-widgets-writes-2026-11-05]]
