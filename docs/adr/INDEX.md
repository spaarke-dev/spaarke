# Architecture Decision Records (ADRs)

> **Purpose**: Documents architectural decisions for the Spaarke platform
> **Audience**: Developers, architects, AI coding agents
> **Status**: Under review - content needs systematic revision

## About ADRs

Architecture Decision Records capture important architectural decisions made during the development of Spaarke, including the context, decision, and consequences.

## ADR Index

<!-- TODO: Phase 3 - Review and add descriptions for each ADR -->

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | Minimal API and BackgroundService Workers | Accepted |
| [ADR-002](ADR-002-thin-plugins.md) | Thin Dataverse Plugins | Accepted |
| [ADR-003](ADR-003-entity-data-contract-spe-link.md) | Entity Data Contract for SPE Link | Accepted |
| [ADR-004](ADR-004-bff-api-authorization.md) | BFF API Authorization | Accepted |
| [ADR-005](ADR-005-graph-api-usage-in-bff.md) | Graph API Usage in BFF | Accepted |
| [ADR-006](ADR-006-pcf-over-webresources.md) | PCF Controls Over Web Resources | Accepted |
| [ADR-007](ADR-007-spefilestore-facade.md) | SpeFileStore Facade Pattern | Accepted |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint Filters for Authorization | Accepted |
| [ADR-009](ADR-009-redis-first-caching.md) | Redis-First Caching Strategy | Accepted |
| [ADR-010](ADR-010-di-minimalism.md) | Dependency Injection Minimalism | Accepted |
| [ADR-011](ADR-011-serverless-function-rejection.md) | Rejection of Serverless Functions | Accepted |
| [ADR-012](ADR-012-shared-component-library.md) | Shared Component Library | Accepted |
| [ADR-013](ADR-013-ai-architecture.md) | AI Tool Framework Architecture | Accepted |
| [ADR-014](ADR-014-pcf-metadata-caching.md) | PCF Metadata Caching | Accepted |
| [ADR-015](ADR-015-pcf-module-structure.md) | PCF Module Structure | Accepted |
| [ADR-016](ADR-016-dataverse-auth-patterns.md) | Dataverse Authentication Patterns | Accepted |
| [ADR-017](ADR-017-bff-resiliency.md) | BFF Resiliency Patterns | Accepted |
| [ADR-018](ADR-018-pcf-error-handling.md) | PCF Error Handling | Accepted |
| [ADR-019](ADR-019-spe-container-lifecycle.md) | SPE Container Lifecycle Management | Accepted |
| [ADR-020](ADR-020-telemetry-strategy.md) | Telemetry and Observability | Accepted |
| [ADR-021](ADR-021-configuration-management.md) | Configuration Management | Accepted |
| [ADR-022](ADR-022-testing-strategy.md) | Testing Strategy | Accepted |

## For AI Agents

**When to reference ADRs**: Before creating new components or making architectural changes, check relevant ADRs to ensure alignment.

**Concise versions**: For AI context optimization, concise versions of ADRs (100-150 lines) will be maintained in `.claude/adr/`

## Phase 3 TODO

- [ ] Review each ADR for accuracy and completeness
- [ ] Add brief descriptions to index table
- [ ] Create concise versions for `.claude/adr/`
- [ ] Identify deprecated or superseded ADRs
- [ ] Add cross-references between related ADRs
