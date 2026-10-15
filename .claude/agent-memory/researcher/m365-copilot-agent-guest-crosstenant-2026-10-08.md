---
name: m365-copilot-agent-guest-crosstenant-2026-10-08
description: Per-customer M365 Copilot declarative agent for B2B guests (T257) — guests can't use Copilot/agents in resource tenant; install in home tenant; OAuth+PKCE AnyTenant auth config; publish APIs delegated-only
metadata:
  type: reference
---

## 2026-10-08: Per-customer Copilot agent for Model 1 B2B guests (T257)

**Question**: How to package and deploy a per-customer M365 Copilot agent that calls only that customer's BFF, when customer staff are B2B guests in Spaarke's tenant?

**Findings**:
- **Guests can't use Copilot or agents in the resource tenant.** Copilot licences can't go to B2B guests (Q&A moderator, 2026-04). Cross-tenant Copilot covers only MTO B2B *members* in Teams meetings, and is "not available to external or federated users". So the agent must be installed in the customer's HOME tenant by their IT, like the add-ins.
- **No licence cost for an actions-only agent.** "Custom actions" work in Copilot Chat with no Copilot licence and no metering (prerequisites capability table, 2026-09-30). This holds only while the declarative agent has no knowledge capabilities.
- **Use OAuth 2.0, not Entra SSO.** OAuth is PKCE by default, can use a public client with no secret, and its auth config can be set to "Any Microsoft 365 organization" (atk `oauth/register targetAudience: AnyTenant`, `applicableToApps: AnyApp`). Entra SSO issues the token from the tenant where Copilot runs (the home tenant), which is wrong for single-tenant BFFs keyed on the guest oid.
- **One auth config per BFF.** The scope and base URL live in the auth config. The repo's `reference_id` base64-decodes to `{tenantId}##{guid}`.
- **Publishing is delegated-only.** Graph `appCatalogs/teamsApps` publish and update have no application permission. The Package Management API (`/beta/copilot/admin/catalog/packages`) is delegated, needs an Agent 365 licence, and has no upload. Auth configs have no documented REST API (only atk, delegated, or the developer portal).
- **Current versions:** app manifest v1.30, declarative agent v1.8, plugin v2.4. No documented catalog size limit. Custom-app updates auto-apply unless permissions or `webApplicationInfo` change. Docs now say "Microsoft Copilot" (renamed from Microsoft 365 Copilot, Oct 2026).

**Sources**:
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/prerequisites (2026-09-30)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-oauth (2026-08-28)
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-authentication-entra-sso (2026-07-14)
- https://learn.microsoft.com/en-us/microsoftteams/copilot-mto ; https://learn.microsoft.com/en-us/answers/questions/5847621
- https://learn.microsoft.com/en-us/graph/api/teamsapp-publish?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/api/admin-settings/package/overview
- https://learn.microsoft.com/en-us/microsoftteams/apps-update-experience
- Agents Toolkit yaml schema v1.11 (oauth/register) on GitHub OfficeDev/microsoft-365-agents-toolkit
- Design note: projects/customer-provisioning-orchestration-r1/notes/t257-copilot-agent-design.md

**Open questions**:
- Does the Teams token store redeem a code for a SPA-platform public client, or does Entra's cross-origin rule (AADSTS9002327) force a Mobile/desktop public client instead?
- Does an AnyTenant auth config created in Spaarke's tenant work for an agent installed in another tenant? Needs a live test in Dewey Cheatham.
- Do guests get the model-driven app's Copilot side pane at all? Probably not.
