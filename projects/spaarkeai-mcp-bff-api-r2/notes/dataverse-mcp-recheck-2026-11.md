# Dataverse MCP: platform re-check (2026-11-25)

Task 004, spec FR-18, design section 4.7. Method: the executing session ran the check itself with the Microsoft Learn MCP tools (it cannot launch agents). `knowledge/dataverse-mcp/` (curated 2026-05-14) was read first. Every fact below was read on 2026-11-25 from the cited page.

## Verdict

Nothing found contradicts the design section 4.7 position or its four reasons. Several platform facts moved since 2026-05; they are listed as deltas. The task's escalation trigger did not fire.

## 1. Endpoint and how clients connect

| Fact | Source (read 2026-11-25) |
|---|---|
| GA endpoint is `https://{org}.crm.dynamics.com/api/mcp`. | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp |
| Preview endpoint is `https://<orgUrl>/api/mcp_preview`; it carries extra preview tools and needs a separate admin setting (preview version) in the environment. Preview tools are not covered by support agreements and may change without notice. | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-preview-tools ; FAQ https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-faq |
| Non-Microsoft clients (Claude Desktop, Claude Code) connect by the local proxy `npx -y @microsoft/dataverse mcp <orgUrl>` (stdio), or directly to `/api/mcp` with a custom Entra app holding the Dynamics CRM `mcp.tools` permission. | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients |
| The proxy uses the "Dataverse CLI" app `0c412cc3-0dd6-449b-987f-05b053db9457`; a tenant admin grants consent once per tenant, and the client must be enabled per environment. If it is missing from the list, an admin adds it by app ID. | same page |
| Copilot Studio connects through its own connector; it is the one client enabled by default in every environment. | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-disable |

## 2. Tool list (GA, `/api/mcp`)

`search_data`, `search`, `create_record`, `update_record`, `delete_record`, `create_table`, `update_table`, `delete_table`, `read_query`, `describe`, `upsert_skill`, `delete_skill`, `init_file_upload`, `commit_file_upload`, `file_download`, `invoke_api`.
Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp (read 2026-11-25).

`search_data` appears only when Dataverse search is enabled for the environment (FAQ, same date).

Write-capable tools on the GA surface: `create_record`, `update_record`, `delete_record`, `create_table`, `update_table`, `delete_table`, `upsert_skill`, `delete_skill`, file upload, and `invoke_api` (any Custom API).

## 3. Business Skills status

- Still **preview**. "Dataverse intelligence" is now part of Business Applications in Work IQ, and Microsoft says the standalone Dataverse intelligence service "will no longer be available soon". Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-intelligence (read 2026-11-25, via search excerpt).
- Skills are natural-language instructions, not executable code. They are Dataverse records governed by Dataverse security roles, ownership and sharing (users also need the Skill Sharing Role). The same permissions apply through Copilot, Work IQ-connected agents and the Dataverse MCP server. Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-business-skill-overview (read 2026-11-25).
- Two access routes: Work IQ MCP (admin control at the Work IQ server level) or the Dataverse MCP server (admin control per environment). A direct Dataverse MCP connection is needed only for the second route. Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-business-skills (read 2026-11-25, via search excerpt).

## 4. Metering and licence coverage

- Since 2025-12-15, Dataverse MCP tools are charged when called by agents created outside Copilot Studio. Not charged for Dynamics 365 data when the tenant has qualifying Dynamics 365 Premium licences or a Microsoft 365 Copilot USL. Source: FAQ (read 2026-11-25).
- `search_data` is billed usage-based (Copilot Credits, attributed to the calling service). All other tools, including metadata `search`, use the "text and generative AI tools (basic)" rate per 10 responses. Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp (read 2026-11-25).
- Not verified: whether a given Spaarke customer's licences exempt it. That is a per-tenant licence question.

## 5. Admin controls

| Control | Detail | Source |
|---|---|---|
| Server on/off per environment | Power Platform admin center, Environment, Settings, Product, Features, "Dataverse Model Context Protocol": "Allow MCP clients to interact with Dataverse MCP server". Clearing it stops every tool and agent that relies on it, including Copilot Studio. | https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-disable |
| Allowed-client list per environment | Same page, Advanced Settings: each client record has "Is Enabled". Enable or disable a client by app ID. Applies only to `/api/mcp`; MCP-named custom APIs are ordinary Dataverse APIs and are not restricted by it. | same page |
| Block the whole MCP server by connector policy | Managed Environment plus advanced connector policies. Blocking the whole server is supported; "granular control over individual MCP tools" is not available. | https://learn.microsoft.com/en-us/power-platform/admin/advanced-connector-policies (read 2026-11-25, via search excerpt) |
| Role required | Power Platform administrator. | disable page |

## 6. Is access read-only configurable?

**No MCP-specific read-only switch is documented.** The FAQ says the server respects Dataverse security roles and row-level security, and that no additional MCP-specific access controls are needed (https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-faq, read 2026-11-25). The allowed-client list is per client app and per environment, not per user, and there is no per-tool control on the server side. So "read-only for named analysts" can only be achieved by giving those analysts a Dataverse security role with no create, write, delete or append privileges. A client-side tool allow or deny list (Claude Desktop lets a user switch off individual tools) is a convenience, not a control the admin owns. If a client entry is on for an environment, every user there who can authenticate through that client reaches the server, limited only by their roles.

## Deltas from `knowledge/dataverse-mcp/` (2026-05)

1. **Tool surface changed.** `list_tables`, `describe_table` and `fetch` are removed (replaced by `describe`). The old `search` (data) is now `search_data`; the new `search` finds metadata and business skills. New: `upsert_skill`, `delete_skill`, `init_file_upload`, `commit_file_upload`, `file_download`, `invoke_api`. NOTES.md and `.claude/skills/dataverse-mcp-usage/SKILL.md` still use the old names; allow or deny lists by tool name need review (Microsoft's own warning).
2. **Local proxy.** The documented proxy is the npm package `@microsoft/dataverse` (Dataverse CLI). The knowledge sample uses the older .NET tool.
3. **Remote endpoint.** Direct connection with a custom Entra app and the `mcp.tools` permission is now documented.
4. **Business Skills.** Still preview, now folded into Business Applications in Work IQ; a second access route (Work IQ MCP) exists.
5. **Metering.** `search_data` is usage-based; other tools stay on the per-10-responses rate.
6. **Admin.** Connector-policy blocking of the whole server is documented (Managed Environment); there is no per-tool control.
7. **Skills write tools on the GA surface.** `upsert_skill` and `delete_skill` mean a connected client can change skill content, not just read it.

## Effect on design section 4.7

All four reasons hold: writes bypass the BFF write path (`create_record`, `update_record`, `invoke_api` and others stay on the GA surface); reads are governed by Dataverse roles only; the generic tools still compete for tool choice with the Spaarke MCP server; non-Copilot-Studio calls are metered unless a licence covers them. One refinement for the guide: "read-only for named analysts" is done with security roles, because no server-side read-only mode exists and the allow-list cannot name individual users.

## Follow-ups for the operator (not done here)

- `knowledge/dataverse-mcp/NOTES.md` and `.claude/skills/dataverse-mcp-usage/SKILL.md` carry the removed tool names (`.claude/` is outside this task's write boundary). Suggest an issue to refresh them.
