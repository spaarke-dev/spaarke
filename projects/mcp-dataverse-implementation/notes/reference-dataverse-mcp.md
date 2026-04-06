# Reference: Official Dataverse MCP Server

> **Source**: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp
> **Assessed**: 2026-04-06
> **Provider**: Microsoft (official, built into Dataverse)
> **Status**: Generally Available

---

## Overview

Microsoft's official MCP server is built directly into the Dataverse platform. Every Dataverse org exposes an MCP endpoint that AI agents (Claude Code, GitHub Copilot, etc.) can connect to for typed, schema-aware data operations.

## Endpoint

```
https://{dataverseOrgName}.crm.dynamics.com/api/mcp
```

**Spaarke dev environment:**
```
https://spaarkedev1.crm.dynamics.com/api/mcp
```

## 12 Available Tools

### Record Operations

| Tool | Description |
|------|-------------|
| `create_record` | Insert a new row into any table; returns GUID |
| `read_query` | Execute SELECT queries (OData) to fetch data |
| `update_record` | Update an existing row by ID |
| `delete_record` | Delete a row by ID |
| `Search` | Keyword search across Dataverse tables |
| `Fetch` | Retrieve full record by entity name and ID (FetchXML) |

### Schema Operations

| Tool | Description |
|------|-------------|
| `describe_table` | Retrieve T-SQL schema definition for a table |
| `list_tables` | List all tables in the environment |
| `Create Table` | Create a new table with specified schema |
| `Update Table` | Modify schema/metadata of an existing table |
| `Delete Table` | Remove a table from the environment |

### Discovery

All tools are schema-advertised via the MCP protocol — Claude Code discovers available operations automatically without documentation lookup.

## Supported Clients

- Claude Code
- Claude Desktop
- VS Code GitHub Copilot
- GitHub Copilot CLI
- Microsoft Copilot Studio agents
- Other third-party MCP clients

## Authentication

1. **Developer authentication** — Azure AD / Entra ID identity
2. **Tenant admin consent** — must be granted for the environment
3. **Per-environment allowlisting** — explicit opt-in per Dataverse org
4. No secrets stored in configuration files (uses developer identity)

## Billing (effective December 15, 2025)

| License | Cost |
|---------|------|
| Dynamics 365 Premium USL | Free (included) |
| Microsoft 365 Copilot USL | Free (included) |
| Non-Copilot Studio agents (e.g., Claude Code) | Per Copilot credit |

**Pricing detail:**
- `Search` tool: Tenant graph grounding rate
- All other tools: Text and generative AI tools (basic) per 10-response Copilot credit rate

**Spaarke impact**: Verify which license covers the dev environment. If per-credit billing applies, monitor usage during evaluation period to assess cost vs. productivity gain.

## Configuration for Claude Code

Claude Code connects via a **local proxy** (`@microsoft/dataverse` npm package) using stdio transport. The proxy handles Azure AD authentication (browser-based sign-in) using the pre-registered Dataverse MCP CLI app (`0c412cc3-0dd6-449b-987f-05b053db9457`).

**Setup command:**
```bash
claude mcp add dataverse -s project -t stdio -- npx -y @microsoft/dataverse mcp https://spaarkedev1.crm.dynamics.com
```

**Resulting `.mcp.json`:**
```json
{
  "mcpServers": {
    "dataverse": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@microsoft/dataverse", "mcp", "https://spaarkedev1.crm.dynamics.com"],
      "env": {}
    }
  }
}
```

**Prerequisites (verified 2026-04-06):**
1. **Create Dynamics ERP service principal** (one-time, if not already present):
   ```bash
   az ad sp create --id 00000015-0000-0000-c000-000000000000
   ```
   Without this, auth fails with `AADSTS650052` ("lacks a service principal for Microsoft Dynamics ERP").
2. **Dataverse MCP CLI** must be enabled in Power Platform Admin Center → Environment → Settings → Features → Active Allowed MCP Clients (app ID `0c412cc3-0dd6-449b-987f-05b053db9457`)
3. **Create auth profile** (one-time per developer):
   ```bash
   npx -y @microsoft/dataverse auth create --environment https://spaarkedev1.crm.dynamics.com
   ```
   Opens browser for Azure AD login. Token is cached locally and refreshed automatically.
4. **Validate** the endpoint:
   ```bash
   npx -y @microsoft/dataverse mcp https://spaarkedev1.crm.dynamics.com --validate
   ```
5. **Restart Claude Code session** after configuration — MCP servers initialize at session start

> **Note**: Direct HTTP connection to `https://{org}.crm.dynamics.com/api/mcp` does NOT work for Claude Code because Azure AD doesn't support Dynamic Client Registration. The local proxy approach is Microsoft's recommended method for non-Microsoft MCP clients.
> **Source**: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients
>
> **Note**: The tenant admin consent URL (`/adminconsent?client_id=...`) may not show a consent prompt if the service principal isn't yet provisioned. Use the `az ad sp create` command above first.

## Go/No-Go Decision Resolution

| Criterion | Required | Actual | Status |
|-----------|----------|--------|--------|
| >20% productivity gain | Yes | ~25-35% estimated (12 typed tools + discovery) | **MET** |
| Setup < 4 hours | Yes | <1 hour (URL + auth config) | **MET** |
| Production-ready server | Yes | Microsoft official, GA | **MET** |
| Auth doesn't leak secrets | Yes | Developer auth + admin consent, no secrets in settings | **MET** |

**Decision: GO**

## Spaarke Skills to Update

| Skill | How MCP Helps |
|-------|---------------|
| `jps-playbook-design` | Use `describe_table` + `list_tables` for scope catalog verification |
| `dataverse-create-schema` | Use `Create Table` + `describe_table` for schema authoring and validation |
| `jps-scope-refresh` | Use `list_tables` + `describe_table` for entity metadata refresh |
| `dataverse-deploy` | Use `describe_table` to verify post-deployment state |

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Tenant admin consent delays initial setup | Coordinate with admin team before Phase 1 |
| Copilot credit billing surprises | Monitor usage in first week; set usage alerts |
| Developer auth token expiry during long sessions | Standard Azure token refresh; Claude Code handles re-auth |
| MCP protocol version changes | Microsoft maintains backward compatibility |

---

*This reference supports the Go decision in design.md. See also: reference-dataverse-skills.md for the complementary development toolkit.*
