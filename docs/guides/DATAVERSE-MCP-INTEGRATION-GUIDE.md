# Dataverse MCP Integration Guide

> **Last Updated**: 2026-04-06
> **Last Reviewed**: 2026-04-06
> **Reviewed By**: mcp-dataverse-implementation project
> **Status**: Current
> **Purpose**: Setup, configuration, and usage of the Dataverse MCP server and Dataverse Skills plugin with Claude Code

---

## Overview

The Dataverse MCP server provides Claude Code with direct, typed access to Dataverse tables, records, and schema. Combined with the Dataverse Skills plugin, this enables natural-language-driven Dataverse development workflows.

**Two complementary tools:**

| Tool | Purpose | When to Use |
|------|---------|-------------|
| **Dataverse MCP Server** | 12 typed tools for CRUD, schema, search | Querying data, inspecting schema, creating records |
| **Dataverse Skills Plugin** | NL-driven schema authoring, solution management | Development-time schema creation, solution packaging |

---

## Prerequisites

| Requirement | Status |
|-------------|--------|
| Node.js 18+ | Required (verified: 22.14.0) |
| Python 3.10+ | Required for Skills plugin (verified: 3.13.5) |
| Azure CLI | Required for initial service principal setup |
| Dataverse environment | `https://spaarkedev1.crm.dynamics.com` |
| Tenant admin access | Required for one-time setup |

---

## Setup (One-Time)

### Step 1: Create Dynamics ERP Service Principal

This is required for Azure AD authentication to work with the Dataverse MCP proxy.

```bash
az ad sp create --id 00000015-0000-0000-c000-000000000000
```

Without this, auth fails with `AADSTS650052` ("lacks a service principal for Microsoft Dynamics ERP").

### Step 2: Enable MCP Client in Power Platform Admin

1. Go to **Power Platform Admin Center** → **Environments** → **spaarkedev1**
2. Navigate to **Settings** → **Product** → **Features**
3. Under **Dataverse Model Context Protocol** → **Advanced Settings**
4. Ensure **Dataverse MCP CLI** is enabled (app ID: `0c412cc3-0dd6-449b-987f-05b053db9457`)

### Step 3: Create Auth Profile

```bash
npx -y @microsoft/dataverse auth create --environment https://spaarkedev1.crm.dynamics.com
```

Opens a browser for Azure AD login. Token is cached locally and refreshed automatically.

### Step 4: Validate MCP Endpoint

```bash
npx -y @microsoft/dataverse mcp https://spaarkedev1.crm.dynamics.com --validate
```

Expected output: "SUCCESS: GA (Production) endpoint is configured correctly."

### Step 5: Configure Claude Code MCP Server

The `.mcp.json` file in the repository root configures the MCP server:

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

This is already committed to the repository. New developers only need Steps 1-4.

### Step 6: Install Dataverse Skills Plugin

```bash
claude plugin marketplace add microsoft/Dataverse-skills
claude plugin install dataverse@dataverse-skills
```

### Step 7: Restart Claude Code

MCP servers initialize at session start. After configuration, restart your Claude Code session.

---

## Available MCP Tools

### Schema Operations

| Tool | Description | Example |
|------|-------------|---------|
| `mcp__dataverse__list_tables` | List all tables (optionally filtered by scope) | Discover `sprk_*` custom tables |
| `mcp__dataverse__describe_table` | Get T-SQL schema definition | Inspect columns, types, lookups, option sets |
| `mcp__dataverse__create_table` | Create a new custom table | Add new entity with columns |
| `mcp__dataverse__update_table` | Add columns to existing table | Extend entity schema |
| `mcp__dataverse__delete_table` | Delete a table | Remove unused entity (requires confirmation) |

### Record Operations

| Tool | Description | Example |
|------|-------------|---------|
| `mcp__dataverse__create_record` | Insert a new row | Create test data |
| `mcp__dataverse__read_query` | Execute SELECT queries | Query active matters |
| `mcp__dataverse__update_record` | Update an existing row | Modify record fields |
| `mcp__dataverse__delete_record` | Delete a row | Remove test data (requires confirmation) |
| `mcp__dataverse__fetch` | Retrieve full record by ID (FetchXML) | Get complete record with related data |
| `mcp__dataverse__search` | Keyword search across tables | Find records by natural language |

### Discovery

| Tool | Description | Example |
|------|-------------|---------|
| `mcp__dataverse__list_apps` | List all model-driven apps | Discover app entities and configurations |

---

## Common Usage Patterns

### Inspecting Schema Before Implementation

Before writing code that touches Dataverse entities:

```
"Describe the sprk_matter table"
→ Claude uses mcp__dataverse__describe_table to show all columns, types, lookups
```

This replaces manual Web API queries and ensures you code against the actual schema.

### Querying Data During Debugging

```
"Show me the last 5 active matters"
→ Claude uses mcp__dataverse__read_query with SELECT + WHERE + ORDER BY
```

### Verifying Post-Deployment State

After deploying a solution with schema changes:

```
"Verify that sprk_workassignment has the priority field"
→ Claude uses mcp__dataverse__describe_table to confirm column exists
```

### Creating Test Data

```
"Create a test matter named 'MCP Test Matter' with status Draft"
→ Claude uses mcp__dataverse__create_record
```

### Schema Discovery for New Projects

During project pipeline resource discovery:

```
"What Dataverse tables exist with the sprk_ prefix?"
→ Claude uses mcp__dataverse__list_tables and filters results
```

---

## Integration with Spaarke Skills

### Skills That Benefit from MCP Tools

| Skill | How MCP Helps |
|-------|---------------|
| `dataverse-create-schema` | Replace manual `Invoke-DataverseApi` validation with `describe_table` |
| `dataverse-deploy` | Post-deployment verification via `describe_table` |
| `jps-playbook-design` | Schema discovery for scope catalog verification |
| `jps-scope-refresh` | Entity metadata refresh via `list_tables` + `describe_table` |
| `project-pipeline` | Schema validation during resource discovery (Step 2) |

### When to Use MCP vs. PAC CLI vs. BFF API

| Need | Use | Why |
|------|-----|-----|
| Query schema structure | **MCP** `describe_table` | Typed, instant, no scripting |
| Query records (dev/debug) | **MCP** `read_query` or `search` | Direct from Claude Code |
| Deploy solutions | **PAC CLI** via `dataverse-deploy` skill | Solution-aware packaging |
| Create schema (scripted) | **PAC CLI** or Web API | Repeatable, version-controlled |
| Create schema (exploratory) | **MCP** `create_table` + **Skills** | NL-driven, fast iteration |
| Runtime data access | **BFF API** (`IGenericEntityService`) | Production code path |
| Bulk data operations | **BFF API** or Power Automate | Performance, transactions |

---

## Authentication

### How Auth Works

The `@microsoft/dataverse` npm package acts as a local stdio proxy:

1. Claude Code starts the proxy as an MCP server via `.mcp.json`
2. Proxy authenticates using the cached auth profile (Step 3 above)
3. Proxy forwards MCP tool calls to `https://spaarkedev1.crm.dynamics.com/api/mcp`
4. Tokens refresh automatically

### Token Expiry

Auth tokens expire periodically. If MCP tools start failing with auth errors:

```bash
npx -y @microsoft/dataverse auth create --environment https://spaarkedev1.crm.dynamics.com
```

Then restart the Claude Code session.

### Managing Auth Profiles

```bash
# List profiles
npx -y @microsoft/dataverse auth list

# Show current profile
npx -y @microsoft/dataverse auth who

# Remove a profile
npx -y @microsoft/dataverse auth remove --name {profile-name}
```

---

## Billing

| License | Cost |
|---------|------|
| Dynamics 365 Premium USL | Free (included) |
| Microsoft 365 Copilot USL | Free (included) |
| Non-Copilot Studio agents (e.g., Claude Code) | Per Copilot credit |

**Monitoring**: Check Power Platform Admin Center → **Analytics** → **Copilot** for usage tracking.

---

## Troubleshooting

### MCP Server Fails to Connect

**Symptom**: `dataverse: ✗ Failed to connect` on session start

**Check**:
1. Auth profile exists: `npx -y @microsoft/dataverse auth who`
2. Endpoint validates: `npx -y @microsoft/dataverse mcp https://spaarkedev1.crm.dynamics.com --validate`
3. Service principal exists: `az ad sp show --id 00000015-0000-0000-c000-000000000000`

### AADSTS650052 Error

**Cause**: Missing Microsoft Dynamics ERP service principal in tenant

**Fix**: `az ad sp create --id 00000015-0000-0000-c000-000000000000`

### Tools Return Empty Results

**Check**: Your Dataverse security role has read access to the tables you're querying.

### Direct HTTP Connection Fails

Claude Code uses Dynamic Client Registration (DCR) for HTTP MCP servers, but Azure AD doesn't support DCR. The stdio local proxy is the only supported method for non-Microsoft clients.

**Reference**: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients

---

## Reference

- [Microsoft Dataverse MCP Documentation](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp)
- [Non-Microsoft Client Setup](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp-other-clients)
- [Dataverse Skills GitHub](https://github.com/microsoft/Dataverse-skills)
- Project notes: `projects/mcp-dataverse-implementation/notes/reference-dataverse-mcp.md`
- Project notes: `projects/mcp-dataverse-implementation/notes/reference-dataverse-skills.md`
