# Cross-Reference Map: AI Context ↔ Full Documentation

> **Purpose**: Quick lookup showing relationships between concise AI context and full documentation
> **Maintained**: Update when creating/modifying AI context files
> **Usage**: Find related docs when you need more context, or find AI versions when updating docs

---

## How to Use This Map

**For AI Agents**:
- When you need more context beyond concise files, find the full documentation here
- When you see a doc reference in the map, check if you should load the AI version instead

**For Developers**:
- When updating a full doc, check this map to see if AI context needs updating
- When creating new architecture docs, add entries here for discoverability

---

## ADRs (Architecture Decision Records)

| ADR # | AI Context File | Full Documentation | Constraints | Patterns |
|-------|----------------|-------------------|-------------|----------|
| 001 | `.claude/adr/ADR-001-minimal-api.md` | [docs/adr/ADR-001-minimal-api-and-workers.md](docs/adr/ADR-001-minimal-api-and-workers.md) | [api.md](.claude/constraints/api.md) | [api/endpoint-definition.md](.claude/patterns/api/endpoint-definition.md) |
| 002 | `.claude/adr/ADR-002-thin-plugins.md` | [docs/adr/ADR-002-thin-plugins.md](docs/adr/ADR-002-thin-plugins.md) | [plugins.md](.claude/constraints/plugins.md) | [dataverse/plugin-structure.md](.claude/patterns/dataverse/plugin-structure.md) |
| 003 | `.claude/adr/ADR-003-entity-data-contract.md` | [docs/adr/ADR-003-entity-data-contract-spe-link.md](docs/adr/ADR-003-entity-data-contract-spe-link.md) | [data.md](.claude/constraints/data.md) | - |
| 004 | `.claude/adr/ADR-004-bff-authorization.md` | [docs/adr/ADR-004-bff-api-authorization.md](docs/adr/ADR-004-bff-api-authorization.md) | [auth.md](.claude/constraints/auth.md), [api.md](.claude/constraints/api.md) | [auth/oauth-scopes.md](.claude/patterns/auth/oauth-scopes.md), [api/endpoint-filters.md](.claude/patterns/api/endpoint-filters.md) |
| 005 | `.claude/adr/ADR-005-graph-api-usage.md` | [docs/adr/ADR-005-graph-api-usage-in-bff.md](docs/adr/ADR-005-graph-api-usage-in-bff.md) | [data.md](.claude/constraints/data.md) | - |
| 006 | `.claude/adr/ADR-006-pcf-over-webresources.md` | [docs/adr/ADR-006-pcf-over-webresources.md](docs/adr/ADR-006-pcf-over-webresources.md) | [pcf.md](.claude/constraints/pcf.md) | [pcf/control-initialization.md](.claude/patterns/pcf/control-initialization.md) |
| 007 | `.claude/adr/ADR-007-spefilestore-facade.md` | [docs/adr/ADR-007-spefilestore-facade.md](docs/adr/ADR-007-spefilestore-facade.md) | [data.md](.claude/constraints/data.md) | - |
| 008 | `.claude/adr/ADR-008-endpoint-filters.md` | [docs/adr/ADR-008-endpoint-filters.md](docs/adr/ADR-008-endpoint-filters.md) | [api.md](.claude/constraints/api.md), [auth.md](.claude/constraints/auth.md) | [api/endpoint-filters.md](.claude/patterns/api/endpoint-filters.md) |
| 009 | `.claude/adr/ADR-009-redis-caching.md` | [docs/adr/ADR-009-redis-first-caching.md](docs/adr/ADR-009-redis-first-caching.md) | [data.md](.claude/constraints/data.md) | - |
| 010 | `.claude/adr/ADR-010-di-minimalism.md` | [docs/adr/ADR-010-di-minimalism.md](docs/adr/ADR-010-di-minimalism.md) | [api.md](.claude/constraints/api.md) | [api/service-registration.md](.claude/patterns/api/service-registration.md) |
| 011 | `.claude/adr/ADR-011-serverless-rejection.md` | [docs/adr/ADR-011-serverless-function-rejection.md](docs/adr/ADR-011-serverless-function-rejection.md) | [api.md](.claude/constraints/api.md) | - |
| 012 | `.claude/adr/ADR-012-shared-components.md` | [docs/adr/ADR-012-shared-component-library.md](docs/adr/ADR-012-shared-component-library.md) | [pcf.md](.claude/constraints/pcf.md) | [pcf/theme-management.md](.claude/patterns/pcf/theme-management.md) |
| 013 | `.claude/adr/ADR-013-ai-architecture.md` | [docs/adr/ADR-013-ai-architecture.md](docs/adr/ADR-013-ai-architecture.md) | [ai.md](.claude/constraints/ai.md) | - |
| 014 | `.claude/adr/ADR-014-pcf-metadata-caching.md` | [docs/adr/ADR-014-pcf-metadata-caching.md](docs/adr/ADR-014-pcf-metadata-caching.md) | [pcf.md](.claude/constraints/pcf.md) | [pcf/dataverse-queries.md](.claude/patterns/pcf/dataverse-queries.md) |
| 015 | `.claude/adr/ADR-015-pcf-module-structure.md` | [docs/adr/ADR-015-pcf-module-structure.md](docs/adr/ADR-015-pcf-module-structure.md) | [pcf.md](.claude/constraints/pcf.md) | [pcf/control-initialization.md](.claude/patterns/pcf/control-initialization.md) |
| 016 | `.claude/adr/ADR-016-dataverse-auth.md` | [docs/adr/ADR-016-dataverse-auth-patterns.md](docs/adr/ADR-016-dataverse-auth-patterns.md) | [auth.md](.claude/constraints/auth.md) | [auth/service-principal.md](.claude/patterns/auth/service-principal.md) |
| 017 | `.claude/adr/ADR-017-bff-resiliency.md` | [docs/adr/ADR-017-bff-resiliency.md](docs/adr/ADR-017-bff-resiliency.md) | [api.md](.claude/constraints/api.md) | [api/resilience.md](.claude/patterns/api/resilience.md) |
| 018 | `.claude/adr/ADR-018-pcf-error-handling.md` | [docs/adr/ADR-018-pcf-error-handling.md](docs/adr/ADR-018-pcf-error-handling.md) | [pcf.md](.claude/constraints/pcf.md) | [pcf/error-handling.md](.claude/patterns/pcf/error-handling.md) |
| 019 | `.claude/adr/ADR-019-spe-container-lifecycle.md` | [docs/adr/ADR-019-spe-container-lifecycle.md](docs/adr/ADR-019-spe-container-lifecycle.md) | [data.md](.claude/constraints/data.md) | - |
| 020 | `.claude/adr/ADR-020-telemetry.md` | [docs/adr/ADR-020-telemetry-strategy.md](docs/adr/ADR-020-telemetry-strategy.md) | [config.md](.claude/constraints/config.md) | - |
| 021 | `.claude/adr/ADR-021-configuration.md` | [docs/adr/ADR-021-configuration-management.md](docs/adr/ADR-021-configuration-management.md) | [config.md](.claude/constraints/config.md) | - |
| 022 | `.claude/adr/ADR-022-testing.md` | [docs/adr/ADR-022-testing-strategy.md](docs/adr/ADR-022-testing-strategy.md) | [testing.md](.claude/constraints/testing.md) | [testing/unit-test-structure.md](.claude/patterns/testing/unit-test-structure.md) |

---

## Domain Constraints

| Domain | Constraint File | Source ADRs | Source Guides | Source Architecture |
|--------|----------------|-------------|---------------|---------------------|
| API/BFF | [.claude/constraints/api.md](.claude/constraints/api.md) | ADR-001, 004, 008, 010, 011, 017 | - | [sdap-bff-api-patterns.md](docs/architecture/sdap-bff-api-patterns.md) |
| PCF | [.claude/constraints/pcf.md](.claude/constraints/pcf.md) | ADR-006, 012, 014, 015, 018 | [PCF-CONTROL-DEVELOPMENT.md](docs/guides/PCF-CONTROL-DEVELOPMENT.md) | [sdap-pcf-patterns.md](docs/architecture/sdap-pcf-patterns.md) |
| Plugins | [.claude/constraints/plugins.md](.claude/constraints/plugins.md) | ADR-002 | [DATAVERSE-PLUGIN-DEVELOPMENT.md](docs/guides/DATAVERSE-PLUGIN-DEVELOPMENT.md) | - |
| Auth | [.claude/constraints/auth.md](.claude/constraints/auth.md) | ADR-004, 016 | - | [auth-boundaries.md](docs/architecture/auth-boundaries.md), [sdap-auth-patterns.md](docs/architecture/sdap-auth-patterns.md) |
| Data | [.claude/constraints/data.md](.claude/constraints/data.md) | ADR-003, 005, 007, 009, 019 | - | - |
| Testing | [.claude/constraints/testing.md](.claude/constraints/testing.md) | ADR-022 | - | - |
| Config | [.claude/constraints/config.md](.claude/constraints/config.md) | ADR-020, 021 | - | - |
| AI | [.claude/constraints/ai.md](.claude/constraints/ai.md) | ADR-013 | [AI-TOOL-FRAMEWORK-GUIDE.md](docs/guides/AI-TOOL-FRAMEWORK-GUIDE.md) | [SPAARKE-AI-STRATEGY.md](docs/architecture/SPAARKE-AI-STRATEGY.md) |

---

## Code Patterns

### API Patterns

| Pattern | File | Source ADRs | Source Guides |
|---------|------|-------------|---------------|
| Endpoint Definition | [.claude/patterns/api/endpoint-definition.md](.claude/patterns/api/endpoint-definition.md) | ADR-001 | - |
| Endpoint Filters | [.claude/patterns/api/endpoint-filters.md](.claude/patterns/api/endpoint-filters.md) | ADR-008 | - |
| Service Registration | [.claude/patterns/api/service-registration.md](.claude/patterns/api/service-registration.md) | ADR-010 | - |
| Error Handling | [.claude/patterns/api/error-handling.md](.claude/patterns/api/error-handling.md) | ADR-017 | - |
| Resilience | [.claude/patterns/api/resilience.md](.claude/patterns/api/resilience.md) | ADR-017 | - |

### PCF Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| Control Initialization | [.claude/patterns/pcf/control-initialization.md](.claude/patterns/pcf/control-initialization.md) | ADR-006, ADR-012 | `src/client/pcf/*/control/index.ts` |
| Theme Management | [.claude/patterns/pcf/theme-management.md](.claude/patterns/pcf/theme-management.md) | ADR-012 | `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` |
| Dataverse Queries | [.claude/patterns/pcf/dataverse-queries.md](.claude/patterns/pcf/dataverse-queries.md) | ADR-006, ADR-009 | `src/client/pcf/shared/utils/environmentVariables.ts` |
| Error Handling | [.claude/patterns/pcf/error-handling.md](.claude/patterns/pcf/error-handling.md) | ADR-006, ADR-012 | `src/client/pcf/UniversalDatasetGrid/control/components/ErrorBoundary.tsx` |
| Dialog Patterns | [.claude/patterns/pcf/dialog-patterns.md](.claude/patterns/pcf/dialog-patterns.md) | ADR-006 | `src/client/pcf/AnalysisBuilder/control/index.ts` |

### Auth Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| OAuth Scopes | [.claude/patterns/auth/oauth-scopes.md](.claude/patterns/auth/oauth-scopes.md) | ADR-004, ADR-008 | `src/client/pcf/*/services/auth/msalConfig.ts` |
| OBO Flow | [.claude/patterns/auth/obo-flow.md](.claude/patterns/auth/obo-flow.md) | ADR-004, ADR-009 | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` |
| Token Caching | [.claude/patterns/auth/token-caching.md](.claude/patterns/auth/token-caching.md) | ADR-009 | `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` |
| MSAL Client | [.claude/patterns/auth/msal-client.md](.claude/patterns/auth/msal-client.md) | ADR-006 | `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` |

### Dataverse Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| Plugin Structure | [.claude/patterns/dataverse/plugin-structure.md](.claude/patterns/dataverse/plugin-structure.md) | ADR-002 | `src/dataverse/plugins/.../BaseProxyPlugin.cs` |
| Web API Client | [.claude/patterns/dataverse/web-api-client.md](.claude/patterns/dataverse/web-api-client.md) | ADR-007, ADR-010 | `src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs` |
| Entity Operations | [.claude/patterns/dataverse/entity-operations.md](.claude/patterns/dataverse/entity-operations.md) | ADR-002, ADR-007 | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` |
| Relationship Navigation | [.claude/patterns/dataverse/relationship-navigation.md](.claude/patterns/dataverse/relationship-navigation.md) | ADR-007 | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` |

### Caching Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| Distributed Cache | [.claude/patterns/caching/distributed-cache.md](.claude/patterns/caching/distributed-cache.md) | ADR-009 | `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` |
| Request Cache | [.claude/patterns/caching/request-cache.md](.claude/patterns/caching/request-cache.md) | ADR-009 | `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` |
| Token Cache | [.claude/patterns/caching/token-cache.md](.claude/patterns/caching/token-cache.md) | ADR-009 | `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` |

### AI Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| Streaming Endpoints | [.claude/patterns/ai/streaming-endpoints.md](.claude/patterns/ai/streaming-endpoints.md) | ADR-013 | `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` |
| Text Extraction | [.claude/patterns/ai/text-extraction.md](.claude/patterns/ai/text-extraction.md) | ADR-013 | `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs` |
| Analysis Scopes | [.claude/patterns/ai/analysis-scopes.md](.claude/patterns/ai/analysis-scopes.md) | ADR-013 | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` |

### Testing Patterns

| Pattern | File | Source ADRs | Canonical Source |
|---------|------|-------------|------------------|
| Unit Test Structure | [.claude/patterns/testing/unit-test-structure.md](.claude/patterns/testing/unit-test-structure.md) | ADR-022 | `tests/unit/Sprk.Bff.Api.Tests/` |
| Mocking Patterns | [.claude/patterns/testing/mocking-patterns.md](.claude/patterns/testing/mocking-patterns.md) | ADR-022 | `tests/unit/Sprk.Bff.Api.Tests/Mocks/` |
| Integration Tests | [.claude/patterns/testing/integration-tests.md](.claude/patterns/testing/integration-tests.md) | ADR-022 | `tests/integration/Spe.Integration.Tests/` |

---

## Standards & Architecture

| Topic | AI Context | Full Documentation |
|-------|-----------|-------------------|
| OAuth/OBO Anti-Patterns | Included in [.claude/constraints/auth.md](.claude/constraints/auth.md) | [oauth-obo-anti-patterns.md](docs/standards/oauth-obo-anti-patterns.md) |
| OAuth/OBO Errors | Included in [.claude/patterns/auth/obo-flow.md](.claude/patterns/auth/obo-flow.md) | [oauth-obo-errors.md](docs/standards/oauth-obo-errors.md) |
| SDAP Overview | - | [sdap-overview.md](docs/architecture/sdap-overview.md) |
| SDAP Troubleshooting | - | [sdap-troubleshooting.md](docs/architecture/sdap-troubleshooting.md) |
| Repository Architecture | - | [SPAARKE-REPOSITORY-ARCHITECTURE.md](docs/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md) |
| UX Management | - | [SPAARKE-UX-MANAGEMENT.md](docs/architecture/SPAARKE-UX-MANAGEMENT.md) |
| AI Strategy | - | [SPAARKE-AI-STRATEGY.md](docs/architecture/SPAARKE-AI-STRATEGY.md) |

---

## AI Protocols

| Protocol | Location | Purpose |
|----------|----------|---------|
| AIP-001 | [.claude/protocols/AIP-001-task-execution.md](.claude/protocols/AIP-001-task-execution.md) | Task execution, context management, handoffs |
| AIP-002 | [.claude/protocols/AIP-002-poml-format.md](.claude/protocols/AIP-002-poml-format.md) | POML task file format specification |
| AIP-003 | [.claude/protocols/AIP-003-human-escalation.md](.claude/protocols/AIP-003-human-escalation.md) | Human escalation triggers and format |

---

## Project Templates

| Template | Location | Used By |
|----------|----------|---------|
| Project README | [.claude/templates/project-README.template.md](.claude/templates/project-README.template.md) | project-setup skill |
| Project Plan | [.claude/templates/project-plan.template.md](.claude/templates/project-plan.template.md) | project-setup skill |
| Task Execution | [.claude/templates/task-execution.template.md](.claude/templates/task-execution.template.md) | task-create skill |
| AI Knowledge Article | [.claude/templates/ai-knowledge-article.template.md](.claude/templates/ai-knowledge-article.template.md) | Documentation creation |

---

## AI Skill Workflows

| Guide | Location | Purpose |
|-------|----------|---------|
| **Skill Interaction Guide** (Authoritative) | [.claude/skills/SKILL-INTERACTION-GUIDE.md](.claude/skills/SKILL-INTERACTION-GUIDE.md) | Complete AI playbook: workflows, skill invocation, decision trees |

---

## Update History

| Date | Change | Updated By |
|------|--------|------------|
| 2025-12-18 | Initial creation with template structure | Phase 3 setup |
| 2025-12-19 | Added API patterns (reference-based) | Phase 3 batch |
| 2025-12-19 | Added PCF patterns (reference-based from codebase exploration) | Phase 3 batch |
| 2025-12-19 | Added Auth patterns (OAuth, OBO, MSAL, token caching) | Phase 3 batch |
| 2025-12-19 | Added Dataverse patterns (plugin-structure, web-api-client, entity-operations, relationship-navigation) | Phase 3 batch |
| 2025-12-19 | Added Caching patterns (distributed-cache, request-cache, token-cache) | Phase 3 batch |
| 2025-12-19 | Added AI patterns (streaming-endpoints, text-extraction, analysis-scopes) | Phase 3 batch |
| 2025-12-19 | Added Testing patterns (unit-test-structure, mocking-patterns, integration-tests) | Phase 3 batch |
| 2025-12-24 | Consolidated AI playbooks - SKILL-INTERACTION-GUIDE.md is authoritative | Doc consolidation |

---

## Maintenance Instructions

**When creating new AI context**:
1. Add entry to appropriate section in this map
2. Include source documentation links
3. Add reverse reference in source doc

**When updating full documentation**:
1. Check this map for related AI context files
2. Update AI context if constraints/patterns changed
3. Update "Last Updated" in both files

**When deprecating content**:
1. Mark as deprecated in this map
2. Add deprecation notice in both AI and full versions
3. Archive when no longer referenced
