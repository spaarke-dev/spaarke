# Dataverse MCP Server Implementation

## Executive Summary
Configure Microsoft's official Dataverse MCP server for Claude Code productivity, and evaluate Dataverse Skills toolkit for development workflow acceleration. Go/No-Go decision resolved: **GO** based on official Microsoft MCP server availability (12 typed tools, GA, built into Dataverse).

## Scope

### In Scope
- Configure official Dataverse MCP server endpoint in `.claude/settings.json`
- Validate all 12 MCP tools against dev environment (`spaarkedev1.crm.dynamics.com`)
- Evaluate and integrate Dataverse Skills for development-time workflows (schema creation, solution management)
- Update existing skills (`jps-playbook-design`, `dataverse-create-schema`, `jps-scope-refresh`, `dataverse-deploy`) to leverage MCP
- Document setup procedure and development workflow guide
- Assess billing implications for non-Copilot Studio agent usage

### Out of Scope
- Building a custom MCP server (official one exists)
- Replacing PAC CLI for solution deployment
- Replacing BFF API's runtime Dataverse client (`IGenericEntityService`)
- Integrating Dataverse Skills into the Spaarke runtime platform (development-time tool only)
- Production write operations via MCP (stays CLI + reviewed)

## Requirements
1. Official MCP server must be configured and validated for dev environment (`spaarkedev1.crm.dynamics.com`)
2. If implemented: >20% productivity measured on schema-discovery tasks
3. At least 4 skills integrated with MCP tools
4. Dataverse Skills must be evaluated for schema creation and solution management workflows
5. Auth must not leak secrets into git (developer auth + admin consent model)
6. CLI fallback must always remain available
7. Billing implications must be documented (Copilot credit charges for non-USL licenses)
8. Tenant admin consent must be obtained before configuration

## Success Criteria
1. Official MCP server configured and all 12 tools validated
2. >20% productivity improvement measured on schema-discovery tasks
3. At least 4 skills integrated with MCP
4. Dataverse Skills installed and development workflow documented
5. Billing model understood and acceptable for dev environment usage
6. Team reports measurable productivity improvement after 1 week

## Technical Approach

### Track A: Official Dataverse MCP Server
1. Configure endpoint URL in `.claude/settings.json` mcpServers block
2. Obtain tenant admin consent for dev environment
3. Validate all 12 tools:
   - Record operations: `create_record`, `read_query`, `update_record`, `delete_record`, `Search`, `Fetch`
   - Schema operations: `describe_table`, `list_tables`, `Create Table`, `Update Table`, `Delete Table`
4. Integrate into existing skills (4 skills identified)
5. Document setup procedure in `docs/guides/DATAVERSE-MCP-SETUP.md`

### Track B: Dataverse Skills (Development Workflow)
1. Install Dataverse Skills from GitHub as Claude Code plugin
2. Configure for dev Dataverse environment
3. Test schema creation workflow (Connection Setup → Metadata Authoring)
4. Test solution management workflow (export, import)
5. Document development workflow guide

### Configuration Example
```json
{
  "mcpServers": {
    "dataverse": {
      "url": "https://spaarkedev1.crm.dynamics.com/api/mcp"
    }
  }
}
```

> Note: Exact syntax may vary — verify against Claude Code MCP documentation for current format.

## References
- design.md in this directory
- [notes/reference-dataverse-mcp.md](notes/reference-dataverse-mcp.md) — Official MCP server reference
- [notes/reference-dataverse-skills.md](notes/reference-dataverse-skills.md) — Dataverse Skills reference
- Microsoft Learn: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp
- GitHub: https://github.com/microsoft/Dataverse-skills
- CLAUDE.md "MCP Server Integration" section
