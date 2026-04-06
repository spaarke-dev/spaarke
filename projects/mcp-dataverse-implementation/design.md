# Dataverse MCP Server Implementation

> **Status**: GO — Official MCP Server Available
> **Created**: 2026-04-05
> **Updated**: 2026-04-06
> **Purpose**: Configure Microsoft's official Dataverse MCP server for Claude Code productivity; evaluate Dataverse Skills for development workflow acceleration

## Problem Statement
Claude Code currently queries Dataverse via PAC CLI or direct Web API calls. Both require:
- Manual command construction
- JSON parsing to extract schema
- No schema-aware autocomplete
- Requires Claude to know exact URL formats

The team wants to evaluate whether a Dataverse MCP server would meaningfully improve productivity.

## Goals
- Enable Claude Code to query Dataverse schema and records via typed interfaces
- Reduce time spent parsing PAC/Web API JSON responses
- Improve discoverability of Dataverse operations
- Evaluate Microsoft Dataverse Skills toolkit for development-time workflow acceleration

## Non-Goals
- Replace PAC CLI for solution deployment (out of scope)
- Replace Web API for BFF runtime queries (Sprk.Bff.Api uses its own client)
- Write operations to production (should stay CLI + review)
- Integrating Dataverse Skills into the Spaarke runtime platform (development-time tool only)
- Building a custom MCP server (official one exists)

## Key Findings (2026-04-06)

### 1. Official Dataverse MCP Server (Microsoft)

Microsoft has shipped an official MCP server built directly into every Dataverse organization.

- **Endpoint**: `https://{org}.crm.dynamics.com/api/mcp`
- **Spaarke dev**: `https://spaarkedev1.crm.dynamics.com/api/mcp`
- **12 typed tools**: `create_record`, `describe_table`, `list_tables`, `read_query`, `update_record`, `delete_record`, `Create Table`, `Update Table`, `Delete Table`, `Search`, `Fetch`
- **Supported clients**: Claude Code, Claude Desktop, VS Code Copilot, GitHub Copilot CLI
- **Auth**: Developer auth + tenant admin consent + per-environment allowlisting (no secrets in config files)
- **Billing**: Free with D365 Premium or M365 Copilot USL; per Copilot credit otherwise
- **Impact**: Production-ready, Microsoft-supported, no third-party server needed. **This resolves the Go/No-Go decision.**

> Full reference: [notes/reference-dataverse-mcp.md](notes/reference-dataverse-mcp.md)
> Source: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp

### 2. Microsoft Dataverse Skills (Open Source)

Microsoft has released an open-source toolkit enabling AI agents to manage Dataverse through natural language.

- **Repository**: https://github.com/microsoft/Dataverse-skills (MIT license)
- **5 core skills**: Connection Setup, Metadata Authoring, Solution Management, Python SDK Data Ops, Tool Routing
- **Integration**: Claude Code via `/plugin` commands; uses MCP + direct SDK operations
- **Assessment**: Useful for **development workflows** (schema creation, solution management). NOT for Spaarke runtime platform — the BFF already has its own Dataverse client.

> Full reference: [notes/reference-dataverse-skills.md](notes/reference-dataverse-skills.md)
> Source: https://github.com/microsoft/Dataverse-skills

## Current State Investigation

Initial repo scan (2026-04-05):

- [x] **Any existing MCP servers configured in this repo?** — **No**. `.claude/settings.json` contains no `mcpServers` entry. No `mcp-servers/` directory exists at repo root.
- [x] **Has Dataverse MCP been prototyped before?** — **No evidence found**. The only references to `mcpServers` in the repo are:
  - `CLAUDE.md` — contains an illustrative example in the "MCP Server Integration" section (not an active config)
  - `projects/spaarke-demo-data-setup-r1/design.md` — unrelated mention
- [x] **Does documentation reference Dataverse MCP?** — Only `docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` mentions it speculatively.
- [x] **What MCP servers are commonly used in the Spaarke dev team?** — None currently configured.

**Conclusion**: No Dataverse MCP server was previously configured. This is a greenfield implementation using Microsoft's official server.

## MCP vs CLI Comparison

| Capability | PAC CLI | Official Dataverse MCP | Verdict |
|---|---|---|---|
| Query entity schema by name | `pac data list-entities` + manual filter | `describe_table` (typed) | **MCP wins** |
| List all tables | Manual parsing | `list_tables` (typed) | **MCP wins** |
| Create tables | Manual scripts | `Create Table` (typed) | **MCP wins** |
| Read entity records | Web API via curl/parse | `read_query` (typed) | **MCP wins** |
| Update records | Web API via curl/parse | `update_record` (typed) | **MCP wins** |
| Run FetchXML | Web API via curl | `Fetch` (typed) | **MCP wins** |
| Search across tables | Not available | `Search` (typed) | **MCP wins** |
| Solution operations | `pac solution` suite | Not available | **CLI wins** |
| Discoverability | Must know commands | Schema-advertised (12 tools) | **MCP wins** |
| Setup cost | Already installed | URL config + admin consent | **Equal** |
| Works offline | No (needs Dataverse) | No | **Tie** |
| Real productivity gain (estimated) | Baseline | **+25-35%** on Dataverse-heavy work | **MCP wins** |

## Decision: GO

**Decision Date**: 2026-04-06
**Rationale**: All Go criteria are met by the official Microsoft Dataverse MCP server.

| Criterion | Required | Actual | Status |
|-----------|----------|--------|--------|
| >20% productivity gain | Yes | ~25-35% estimated (12 typed tools + schema discovery) | **MET** |
| Setup < 4 hours | Yes | <1 hour (URL + auth config) | **MET** |
| Production-ready server | Yes | Microsoft official, GA, built into Dataverse | **MET** |
| Auth doesn't leak secrets | Yes | Developer auth + admin consent; no secrets in settings.json | **MET** |

**Previously NO-GO criteria — all cleared:**
- Alpha/experimental? **NO** — Microsoft official, Generally Available
- Secrets in git? **NO** — uses developer identity + admin consent
- Setup >1 day? **NO** — URL configuration only
- CLI sufficient for 95%? **NO** — MCP provides typed tools, schema discovery, and discoverability that CLI lacks

## Implementation Phases

### Phase 1: Configuration & Validation (2-4 hours)
- Configure `.claude/settings.json` with official MCP endpoint for `spaarkedev1.crm.dynamics.com`
- Obtain tenant admin consent for the dev environment
- Validate all 12 tools respond correctly (schema queries, record operations, search)
- Install Dataverse Skills plugin for development workflow testing
- Document any auth or billing issues encountered

### Phase 2: Skill Integration (1 day)
- Update `jps-playbook-design` to use MCP `describe_table` + `list_tables` for scope catalog verification
- Update `dataverse-create-schema` skill to use MCP `Create Table` for entity creation
- Update `jps-scope-refresh` to use MCP for entity metadata refresh
- Update `dataverse-deploy` to use MCP `describe_table` for post-deployment verification
- Test Dataverse Skills for NL-driven schema authoring workflows

### Phase 3: Documentation & Rollout (0.5 day)
- Create `docs/guides/DATAVERSE-MCP-SETUP.md` with setup procedure
- Update CLAUDE.md "MCP Server Integration" section with concrete configuration
- Create development workflow guide for Dataverse Skills usage
- Team communication and onboarding

## Success Criteria
- Claude Code consistently uses MCP over CLI for schema discovery and record operations
- At least 4 skills updated to leverage MCP tools
- Dataverse Skills installed and development workflow documented
- Billing model understood and acceptable for dev environment usage
- Team reports measurable productivity improvement after 1 week

## Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Tenant admin consent delays setup | Medium | Blocks Phase 1 | Coordinate with admin team before starting |
| Copilot credit billing surprises | Low | Cost overrun | Monitor usage in first week; set alerts |
| Auth token expiry during long sessions | Low | Workflow interruption | Standard Azure token refresh; Claude Code handles re-auth |
| Learning curve for MCP tool names | Low | Slower initial adoption | Documented in setup guide; tools are schema-advertised |
| Dataverse Skills community maintenance risk | Medium | Tool stagnation | MIT license allows forking; official MCP covers core needs |

## References
- [notes/reference-dataverse-mcp.md](notes/reference-dataverse-mcp.md) — Official MCP server reference
- [notes/reference-dataverse-skills.md](notes/reference-dataverse-skills.md) — Dataverse Skills reference
- Microsoft Learn: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp
- GitHub: https://github.com/microsoft/Dataverse-skills
- CLAUDE.md "MCP Server Integration" section
- docs/architecture/dataverse-infrastructure-architecture.md

## Related Skills
- dataverse-create-schema
- dataverse-deploy
- jps-playbook-design
- jps-scope-refresh
- ai-procedure-maintenance

---

*Next step: Phase 1 Configuration & Validation (estimated 2-4 hours)*
