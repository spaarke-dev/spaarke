# Architecture Decision Records (ADRs)

> **Purpose**: Documents architectural decisions for the Spaarke platform
> **Audience**: Developers, architects, AI coding agents
> **Last Updated**: 2025-12-22

## About ADRs

Architecture Decision Records capture important architectural decisions made during the development of Spaarke, including the context, decision, and consequences.

## ADR Index

| ADR | Title | Domain | Status |
|-----|-------|--------|--------|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | Minimal API and BackgroundService Workers | Backend | Accepted |
| [ADR-002](ADR-002-no-heavy-plugins.md) | Thin Dataverse Plugins | Dataverse | Accepted |
| [ADR-003](ADR-003-lean-authorization-seams.md) | Lean Authorization Seams | Security | Accepted |
| [ADR-004](ADR-004-async-job-contract.md) | Async Job Contract | Backend | Accepted |
| [ADR-005](ADR-005-flat-storage-spe.md) | Flat Storage for SPE | Storage | Accepted |
| [ADR-006](ADR-006-prefer-pcf-over-webresources.md) | PCF Controls Over Web Resources | Frontend | Accepted |
| [ADR-007](ADR-007-spe-storage-seam-minimalism.md) | SPE Storage Seam Minimalism | Storage | Accepted |
| [ADR-008](ADR-008-authorization-endpoint-filters.md) | Authorization Endpoint Filters | Security | Accepted |
| [ADR-009](ADR-009-caching-redis-first.md) | Redis-First Caching Strategy | Caching | Accepted |
| [ADR-010](ADR-010-di-minimalism.md) | Dependency Injection Minimalism | Backend | Accepted |
| [ADR-011](ADR-011-dataset-pcf-over-subgrids.md) | Dataset PCF Over Native Subgrids | Frontend | Accepted |
| [ADR-012](ADR-012-shared-component-library.md) | Shared Component Library | Frontend | Accepted |
| [ADR-013](ADR-013-ai-architecture.md) | AI Tool Framework Architecture | AI | Accepted |
| [ADR-014](ADR-014-ai-caching-and-reuse-policy.md) | AI Caching and Reuse Policy | AI | Accepted |
| [ADR-015](ADR-015-ai-data-governance.md) | AI Data Governance | AI | Accepted |
| [ADR-016](ADR-016-ai-cost-rate-limit-and-backpressure.md) | AI Cost, Rate Limits, Backpressure | AI | Accepted |
| [ADR-017](ADR-017-async-job-status-and-persistence.md) | Async Job Status and Persistence | Backend | Accepted |
| [ADR-018](ADR-018-feature-flags-and-kill-switches.md) | Feature Flags and Kill Switches | Operations | Accepted |
| [ADR-019](ADR-019-api-errors-and-problemdetails.md) | API Errors and ProblemDetails | Backend | Accepted |
| [ADR-020](ADR-020-versioning-strategy-apis-jobs-client-packages.md) | Versioning Strategy | Operations | Accepted |
| [ADR-021](ADR-021-fluent-ui-design-system.md) | Fluent UI v9 Design System | **UI/UX** | **Accepted** |

## ADRs by Domain

### Frontend / UI

| ADR | Summary |
|-----|---------|
| **[ADR-021](ADR-021-fluent-ui-design-system.md)** | **Authoritative UI/UX standard: Fluent v9, dark mode, accessibility** |
| [ADR-006](ADR-006-prefer-pcf-over-webresources.md) | Use PCF controls, not legacy webresources |
| [ADR-011](ADR-011-dataset-pcf-over-subgrids.md) | Dataset PCF for list-based UI |
| [ADR-012](ADR-012-shared-component-library.md) | Shared component library with Fluent v9 |

### Backend / API

| ADR | Summary |
|-----|---------|
| [ADR-001](ADR-001-minimal-api-and-workers.md) | .NET Minimal API with BackgroundService |
| [ADR-004](ADR-004-async-job-contract.md) | Async job patterns |
| [ADR-010](ADR-010-di-minimalism.md) | Lean DI registrations |
| [ADR-017](ADR-017-async-job-status-and-persistence.md) | Job status persistence |
| [ADR-019](ADR-019-api-errors-and-problemdetails.md) | ProblemDetails for errors |

### AI Features

| ADR | Summary |
|-----|---------|
| [ADR-013](ADR-013-ai-architecture.md) | AI Tool Framework architecture |
| [ADR-014](ADR-014-ai-caching-and-reuse-policy.md) | AI response caching |
| [ADR-015](ADR-015-ai-data-governance.md) | AI data handling rules |
| [ADR-016](ADR-016-ai-cost-rate-limit-and-backpressure.md) | AI cost controls |

### Security

| ADR | Summary |
|-----|---------|
| [ADR-003](ADR-003-lean-authorization-seams.md) | Authorization patterns |
| [ADR-008](ADR-008-authorization-endpoint-filters.md) | Endpoint filter authorization |

### Storage / Caching

| ADR | Summary |
|-----|---------|
| [ADR-005](ADR-005-flat-storage-spe.md) | SPE flat storage model |
| [ADR-007](ADR-007-spe-storage-seam-minimalism.md) | Storage abstraction |
| [ADR-009](ADR-009-caching-redis-first.md) | Redis-first caching |

### Dataverse

| ADR | Summary |
|-----|---------|
| [ADR-002](ADR-002-no-heavy-plugins.md) | Thin plugins, no HTTP calls |

### Operations

| ADR | Summary |
|-----|---------|
| [ADR-018](ADR-018-feature-flags-and-kill-switches.md) | Feature flag patterns |
| [ADR-020](ADR-020-versioning-strategy-apis-jobs-client-packages.md) | Versioning strategy |

---

## For AI Agents

**Concise versions**: AI-optimized summaries (~100-150 lines) are in `.claude/adr/`

**When to load ADRs**:
- Before creating new components
- When making architectural changes
- To understand constraints and patterns

**Key ADR for UI work**: Always load [ADR-021](ADR-021-fluent-ui-design-system.md) when working on any UI component.

---

## Related Resources

- [ADR Validation Process](ADR-VALIDATION-PROCESS.md)
- [README - How to Write ADRs](README-ADRs.md)

