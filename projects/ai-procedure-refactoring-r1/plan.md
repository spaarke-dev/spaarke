# AI Procedure Refactoring R1 — Implementation Plan

> **Created**: 2026-03-31
> **Phases**: 4 + wrap-up
> **Estimated Tasks**: ~20

## Architecture Context

### Design Principles
1. Code is the source of truth for implementation
2. Docs capture what code cannot: why, what-not-to-do, when-to-use
3. Pointers to code, not descriptions of code
4. Three-layer context: CLAUDE.md (rules) → .claude/ (pointers/constraints) → docs/ (history/procedures)

### Target Pattern File Format
```markdown
# {Pattern Name}
## When: {1-2 sentences}
## Read These Files:
1. `{path}` — {what it shows}
## Constraints: ADR-{NNN}, MUST/MUST NOT rules
## Key Rules: {non-obvious rules}
```
Max 25 lines per file.

### Target Architecture Doc Format
Decisions + constraints + component table only. No implementation walkthroughs.

## Phase Breakdown

### Phase 1: Convert Patterns to Pointers (PARALLEL — 8 independent tasks)

Convert all `.claude/patterns/` subdirectories from inline code to pointer format. Each subdirectory is independent — all 8 tasks can run in parallel.

| Task | Scope | Files | Est |
|------|-------|-------|-----|
| 001 | `.claude/patterns/api/` (7 files) | endpoint-definition, endpoint-filters, error-handling, background-workers, resilience, service-registration, send-email-integration | 1h |
| 002 | `.claude/patterns/auth/` (12 files) | msal-client, oauth-scopes, obo-flow, service-principal, token-caching, dataverse-obo, graph-endpoints-catalog, graph-sdk-v5, graph-webhooks, spaarke-auth-initialization, uac-access-control, xrm-webapi-vs-bff-auth | 1.5h |
| 003 | `.claude/patterns/caching/` (3 files) | distributed-cache, request-cache, token-cache | 0.5h |
| 004 | `.claude/patterns/dataverse/` (5 files) | entity-operations, plugin-structure, relationship-navigation, web-api-client, polymorphic-resolver | 1h |
| 005 | `.claude/patterns/pcf/` (5 files) | control-initialization, dataverse-queries, dialog-patterns, error-handling, theme-management | 1h |
| 006 | `.claude/patterns/ai/` (3 files) | analysis-scopes, streaming-endpoints, text-extraction | 0.5h |
| 007 | `.claude/patterns/testing/` (3 files) | integration-tests, mocking-patterns, unit-test-structure | 0.5h |
| 008 | `.claude/patterns/webresource/` + `ui/` (5 files) | full-page-custom-page, custom-dialogs-in-dataverse, code-page-wizard-wrapper, subgrid-parent-rollup, choice-dialog-pattern | 1h |
| 009 | Update `.claude/patterns/INDEX.md` | Reflect new pointer format across all subdirectories | 0.5h |

**Dependencies**: 009 depends on 001-008 completing.

### Phase 2: Split Architecture Docs (PARTIALLY PARALLEL)

Audit and trim `docs/architecture/` files. Group by domain for parallel execution.

| Task | Scope | Files | Est |
|------|-------|-------|-----|
| 010 | Audit all 35 architecture files — classify keep/trim/delete | Produce classification spreadsheet | 1h |
| 011 | Trim AI architecture docs | AI-ARCHITECTURE, ai-implementation-reference (DELETE), playbook-architecture, ai-document-summary-architecture, ai-semantic-relationship-graph | 2h |
| 012 | Trim BFF/API architecture docs | sdap-bff-api-patterns, sdap-overview, sdap-component-interactions, sdap-document-processing-architecture | 1.5h |
| 013 | Trim UI/frontend architecture docs | ui-dialog-shell-architecture, universal-dataset-grid-architecture, SIDE-PANE-PLATFORM-ARCHITECTURE, sdap-pcf-patterns, sdap-workspace-integration-patterns, VISUALHOST-ARCHITECTURE | 1.5h |
| 014 | Trim infrastructure/auth architecture docs | external-access-spa-architecture, communication-service-architecture, email-to-document-*, office-outlook-teams-*, BFF-RENAME-AND-CONFIG-STRATEGY, INFRASTRUCTURE-PACKAGING-STRATEGY, multi-environment-portability-strategy | 1.5h |
| 015 | Trim reference/stable docs (light touch) | auth-azure-resources, auth-AI-azure-resources, auth-security-boundaries, AZURE-RESOURCE-NAMING-CONVENTION, SPAARKE-REPOSITORY-ARCHITECTURE, event-to-do-architecture, finance-intelligence-architecture, uac-access-control | 1h |

**Dependencies**: 011-015 depend on 010 (classification). 011-015 can run in parallel after 010.

### Phase 3: Consolidate Guides (PARTIALLY PARALLEL)

| Task | Scope | Files | Est |
|------|-------|-------|-----|
| 020 | Consolidate 6 playbook guides into 2 | PLAYBOOK-DESIGN-GUIDE, PLAYBOOK-JPS-PROMPT-SCHEMA-GUIDE, PLAYBOOK-SCOPE-CONFIGURATION-GUIDE, PLAYBOOK-BUILDER-GUIDE, PLAYBOOK-PRE-FILL-INTEGRATION-GUIDE, JPS-AUTHORING-GUIDE → 2 files | 2h |
| 021 | Clean up redirect stubs in docs/standards/ | Delete 3 redirect files (oauth-obo-anti-patterns, oauth-obo-implementation, dataverse-oauth-authentication) | 0.5h |
| 022 | Audit remaining guides for implementation drift | Identify top 10 drift-prone guides, add TODO markers or trim | 1.5h |

**Dependencies**: 020-022 can run in parallel (independent domains).

### Phase 4: Update CLAUDE.md & Validate (SERIAL)

| Task | Scope | Est |
|------|-------|-----|
| 030 | Update root CLAUDE.md — replace Documentation section with Architecture Discovery | 1h |
| 031 | Validate all pointer paths (automated grep) | 0.5h |
| 032 | Validate no broken references in skills/tasks | 0.5h |
| 033 | Update `.claude/skills/INDEX.md` if any references changed | 0.5h |

**Dependencies**: 030-033 are serial (each builds on prior). All depend on Phases 1-3 complete.

### Phase 5: Wrap-Up

| Task | Scope | Est |
|------|-------|-----|
| 090 | Project wrap-up — update README status, create lessons-learned, final metrics | 0.5h |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003, 004, 005, 006, 007, 008 | None | 8 independent pattern subdirectories |
| B | 009 | Group A complete | INDEX update needs all patterns done |
| C | 010 | None (can start with Group A) | Architecture audit — read-only |
| D | 011, 012, 013, 014, 015 | 010 complete | 5 independent architecture domains |
| E | 020, 021, 022 | None (can start with any group) | Independent guide work |
| F | 030, 031, 032, 033 | Groups A-E complete | Serial validation chain |
| G | 090 | Group F complete | Wrap-up |

## References

- [Specification](spec.md)
- [Design](design.md)
- `.claude/patterns/INDEX.md` — current pattern registry
- `docs/architecture/INDEX.md` — current architecture doc index
