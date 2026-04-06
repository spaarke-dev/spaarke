# Reference: Microsoft Dataverse Skills

> **Source**: https://github.com/microsoft/Dataverse-skills
> **Assessed**: 2026-04-06
> **License**: MIT (open source)
> **Status**: Active development

---

## Overview

Microsoft Dataverse Skills is an open-source toolkit that enables AI agents (Claude Code, GitHub Copilot) to build, configure, and manage Dataverse solutions through natural language instructions. It bridges conversational AI with the Dataverse platform for development-time workflows.

## 5 Core Skills

### 1. Connection Setup
- Authenticate and establish secure connection to a Dataverse environment
- Per-environment allowlisting for security
- Supports developer auth + credential stores

### 2. Metadata Authoring
- Create and modify Dataverse tables and columns via natural language
- Schema validation before applying changes
- **Use case**: Development-time schema creation ("Create a support ticket table with priority and status fields")

### 3. Solution Management
- Create, export, and import Dataverse solutions
- Component tracking and dependency management
- **Use case**: Development-time solution packaging from Claude Code

### 4. Python SDK Data Operations
- CRUD operations via Dataverse Python SDK
- Bulk operations support
- **Use case**: Seed data, test data generation, data migration scripts

### 5. Tool Routing & Orchestration
- Intelligent routing of NL requests to the appropriate skill/API
- Context-aware tool selection
- Routes between MCP, SDK, and PAC CLI paths as appropriate

## Integration with Claude Code

**Installation (verified 2026-04-06):**
```bash
# Step 1: Add the marketplace
claude plugin marketplace add microsoft/Dataverse-skills

# Step 2: Install the plugin
claude plugin install dataverse@dataverse-skills
```

**Installed scope**: user (available in all Claude Code sessions)

**Local development (alternative):**
```bash
claude --plugin-dir "<repo-path>/.github/plugins/dataverse"
```

**System Requirements:**
- Microsoft Dataverse environment
- Python 3.10+ (verified: 3.13.5)
- Node.js 18+ (verified: 22.14.0)

## Security Model

- **Least-privilege access** — every API call authorized by Dataverse security roles; plugin cannot bypass or escalate
- **No telemetry** — plugin does not collect or transmit usage analytics
- **Credential storage** — OS credential stores or memory only; never transmitted externally
- **Data sovereignty** — credentials remain in tenant; no external transmission
- **Per-environment allowlisting** — MCP paths require explicit opt-in per environment

## Assessment for Spaarke

### AI Development Procedures: YES

Dataverse Skills accelerates development workflows:
- **Schema creation** — "Create a sprk_invoice table with amount, vendor, and due date fields" → executed via NL
- **Solution management** — package and deploy solutions from Claude Code without manual PAC CLI
- **Data seeding** — generate test data for development/demo environments
- **Metadata exploration** — discover table relationships and field definitions conversationally

**Recommendation**: Install as a Claude Code plugin and document in the development workflow guide.

### Client-Side Spaarke Platform: NO (for now)

- The Spaarke BFF (`Sprk.Bff.Api`) already has its own Dataverse client via `IGenericEntityService`
- Dataverse Skills is a development-time tool, not a runtime capability
- The Python SDK dependency makes it unsuitable for the .NET BFF
- Could be revisited if NL-to-Dataverse admin features are needed in the product

## Relationship to Official MCP Server

Dataverse Skills and the official MCP server are **complementary, not competing**:

| Capability | Official MCP Server | Dataverse Skills |
|-----------|---------------------|-----------------|
| Query records | Yes (12 typed tools) | Yes (Python SDK) |
| Schema discovery | Yes (`describe_table`, `list_tables`) | Yes (Metadata Authoring) |
| Schema creation | Yes (`Create Table`, `Update Table`) | Yes (NL-driven, more natural) |
| Solution management | No | Yes (create, export, import) |
| NL-driven operations | No (typed tool calls) | Yes (core design) |
| Auth model | Tenant admin consent | Per-environment allowlist |
| Maintenance | Microsoft platform team | Community (MIT) |
| Runtime suitability | Yes (standard MCP protocol) | No (development-time only) |

**Recommendation**: Use BOTH.
- **Official MCP** for querying, typed operations, and skill integration (always-on, production-grade)
- **Dataverse Skills** for NL-driven schema authoring, solution management, and data seeding (development workflows)

## Workflow Examples

### Schema Creation
```
User: "Create a sprk_workassignment table with fields for assigned_to (lookup to systemuser),
       due_date (datetime), priority (choice: Low/Normal/High/Urgent), and status (choice: Draft/Active/Completed)"

Dataverse Skills: → Validates schema → Creates table → Creates fields → Reports completion
```

### Solution Packaging
```
User: "Export the SpaarkeCore solution as unmanaged to ./exports/"

Dataverse Skills: → Identifies solution → Exports via PAC CLI → Saves to path
```

---

*This reference supports the design.md evaluation. See also: reference-dataverse-mcp.md for the official MCP server.*
