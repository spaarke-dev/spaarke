# Dataverse MCP Server Implementation

> **Status**: Design (Go/No-Go pending)
> **Created**: 2026-04-05
> **Purpose**: Evaluate and potentially implement Dataverse MCP server for Claude Code productivity

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

## Non-Goals
- Replace PAC CLI for solution deployment (out of scope)
- Replace Web API for BFF runtime queries (Sprk.Bff.Api uses its own client)
- Write operations to production (should stay CLI + review)

## Current State Investigation

Initial repo scan (2026-04-05):

- [x] **Any existing MCP servers configured in this repo?** — **No**. `.claude/settings.json` contains no `mcpServers` entry. No `mcp-servers/` directory exists at repo root.
- [x] **Has Dataverse MCP been prototyped before?** — **No evidence found**. The only references to `mcpServers` in the repo are:
  - `CLAUDE.md` — contains an illustrative example in the "MCP Server Integration" section (not an active config)
  - `projects/spaarke-demo-data-setup-r1/design.md` — unrelated mention
- [x] **Does documentation reference Dataverse MCP?** — Only `docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` mentions it speculatively.
- [ ] **What MCP servers are commonly used in the Spaarke dev team?** — To be confirmed with team in Phase 1.

**Conclusion**: Despite user recall of a prior setup, there is no Dataverse MCP server currently configured or previously prototyped in this repository. This is a greenfield evaluation.

## MCP vs CLI Comparison

| Capability | PAC CLI | AZ CLI | Dataverse MCP | Verdict |
|---|---|---|---|---|
| Query entity schema by name | `pac data list-entities` then manual filter | N/A | Direct typed query | MCP wins |
| Create/update entities | `pac data create/update` | N/A | Direct typed write | CLI equal |
| Read entity records | Web API via curl/parse | N/A | Direct typed query | MCP wins |
| Update records | Web API via curl/parse | N/A | Direct typed write | MCP wins |
| Run FetchXML | Web API via curl | N/A | Direct query | MCP wins |
| Solution operations | `pac solution` suite | N/A | Limited (depends on server) | CLI wins |
| Discoverability (Claude knows what's available) | Must know commands | Must know commands | Schema-advertised | MCP wins |
| Setup cost | Already installed | Already installed | Install + auth per env | CLI wins |
| Works offline | No (needs Dataverse) | No | No | Tie |
| Real productivity gain (estimated) | Baseline | Baseline | +10-15% on Dataverse-heavy work | Marginal |

## Decision Criteria (Phase 1 Go/No-Go)

**GO if**:
- We estimate >20% productivity gain on Dataverse schema discovery tasks
- Setup overhead is <4 hours
- A production-ready MCP server exists (not alpha/experimental)
- Auth mechanism doesn't require storing secrets in settings.json

**NO-GO if**:
- Only alpha/experimental servers available
- Auth requires secrets that would leak into git
- Setup requires >1 day
- CLI workflow is already sufficient for 95% of tasks

## Implementation Phases (if GO)

### Phase 1: Research & Decision (1 day)
- Identify production-ready Dataverse MCP servers (check Microsoft, community)
- Test with sample queries in isolated sandbox
- Measure productivity gain on real tasks (schema discovery, record lookup)
- Make Go/No-Go decision

### Phase 2: Installation & Configuration (if GO) (1 day)
- Install chosen MCP server
- Configure `.claude/settings.json` with mcpServers entry
- Test auth with dev Dataverse environment
- Document setup procedure in docs/guides/

### Phase 3: Integration with Skills (if GO) (1 day)
- Update jps-playbook-design to use MCP for scope catalog verification
- Update dataverse-create-schema skill to use MCP for entity creation
- Update relevant patterns to mention MCP as alternative to Web API

### Phase 4: Documentation & Rollout (if GO) (1 day)
- Create guide: docs/guides/DATAVERSE-MCP-SETUP.md
- Update CLAUDE.md MCP section with concrete examples
- Team communication

## Success Criteria (if GO)
- MCP server responds to schema queries in <2 seconds
- Claude Code consistently uses MCP over CLI for schema discovery
- At least 3 skills updated to leverage MCP
- Team reports measurable productivity improvement after 2 weeks

## Risks
- MCP server may be abandoned/unmaintained (mitigation: check last commit, community activity before commit)
- Auth token rotation breaks Claude Code workflows (mitigation: Managed Identity or Key Vault integration)
- Learning curve for team (mitigation: documented in setup guide)
- MCP server becomes a single point of failure (mitigation: CLI fallback always available)

## References
- CLAUDE.md "MCP Server Integration" section
- docs/architecture/dataverse-infrastructure-architecture.md
- projects/ai-procedure-refactoring-r2/notes/lessons-learned.md

## Related Skills
- dataverse-create-schema
- dataverse-deploy
- jps-playbook-design
- jps-scope-refresh

---

*Next step: Phase 1 Research & Go/No-Go decision*
