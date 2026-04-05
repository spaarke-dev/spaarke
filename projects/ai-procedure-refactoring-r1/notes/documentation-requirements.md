# Documentation Requirements — Spaarke Solution Library

> **Created**: April 4, 2026
> **Purpose**: Master requirements table for all documentation needed to support world-class AI-driven development.
> **Skills**: `/docs-architecture`, `/docs-guide` — use these to draft each document.

---

## Document Types

| Type | Directory | Purpose | Skill |
|------|-----------|---------|-------|
| **Architecture** | `docs/architecture/` | How systems work technically — components, data flows, design decisions, constraints, known pitfalls | `/docs-architecture` |
| **Guide** | `docs/guides/` | How to do things operationally — procedures, configuration, setup, troubleshooting | `/docs-guide` |
| **Data Model** | `docs/data-model/` | Entity schemas, relationships, alternate keys, field mappings, ERDs | `/docs-data-model` (new) |
| **Standards** | `docs/standards/` | Cross-cutting coding standards, patterns, and anti-patterns that apply across all modules | `/docs-standards` (new) |
| **Procedures** | `docs/procedures/` | Development workflow procedures — CI/CD, testing, code review, context recovery | `/docs-procedures` (new) |
| **Product Docs** | `docs/product-documentation/` | End-user and admin documentation for deployed features | (manual) |

## How to Use This Table

- **Status**: `existing` = file exists, may need update; `new` = must be created; `over-trimmed` = exists but needs depth restored; `enhance` = exists but needs additional sections
- **Prompt**: Copy-paste into Claude Code to draft the document using the appropriate skill

---

## Group 1: Core Platform

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `sdap-bff-api-patterns.md` | arch | over-trimmed | BFF API entry point, 22 startup modules, endpoint map, DI module system, service registration patterns | `/docs-architecture` update `docs/architecture/sdap-bff-api-patterns.md` — restore depth for the 22 startup modules in `api/Infrastructure/Startup/`, endpoint registration in `api/Api/`, and the module-per-concern DI pattern. Read `Program.cs` and all `*Module.cs` files. Include Known Pitfalls section (missing DI registration, endpoint filter ordering). |
| `sdap-auth-patterns.md` | arch | over-trimmed | Nine-pattern auth taxonomy, OBO vs app-only flows, endpoint filters, Graph client factory | `/docs-architecture` update `docs/architecture/sdap-auth-patterns.md` — restore depth for OBO flow through `GraphClientFactory.cs`, endpoint filter chain in `api/Api/Filters/`, and the auth strategy selection in `Spaarke.Auth/`. Include integration contracts (Graph scopes required per operation) and Known Pitfalls (token not propagated, wrong scope, tenant ID race). |
| `configuration-architecture.md` | arch | new | 17 options classes, validators, runtime config resolution, appsettings hierarchy | `/docs-architecture` create `docs/architecture/configuration-architecture.md` — document the configuration surface: all options classes in `api/Configuration/` and `api/Options/`, validation strategy, appsettings layering, and Key Vault integration. |
| `resilience-architecture.md` | arch | new | Circuit breakers, retry policies, resilient search client, error handling middleware | `/docs-architecture` create `docs/architecture/resilience-architecture.md` — document `api/Infrastructure/Resilience/` (CircuitBreakerRegistry, ResilientSearchClient, RetryPolicies) and `api/Infrastructure/Errors/`. |
| `shared-libraries-architecture.md` | arch | new | Spaarke.Core (Cache, Auth, Constants, Entities, Interfaces) and Spaarke.Dataverse (WebApiClient, service interfaces) | `/docs-architecture` create `docs/architecture/shared-libraries-architecture.md` — document `src/server/shared/Spaarke.Core/` and `src/server/shared/Spaarke.Dataverse/`. Cover the cross-cutting utilities, cache abstractions, and Dataverse client abstraction. |
| `CONFIGURATION-MATRIX.md` | guide | new | Every configurable behavior mapped to its setting location, allowed values, and defaults | `/docs-guide` create `docs/guides/CONFIGURATION-MATRIX.md` — build a single reference mapping: feature → setting name → location (appsettings/env var/Key Vault/Dataverse) → allowed values → default. Source from all options classes in `api/Configuration/` and `api/Options/`. |

## Group 2: AI Pipeline

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `AI-ARCHITECTURE.md` | arch | over-trimmed | Tool framework, handler registry, execution context, streaming paths, scope resolution | `/docs-architecture` update `docs/architecture/AI-ARCHITECTURE.md` — restore depth for the tool framework (~100 files). Cover `ToolHandlerRegistry`, `AnalysisOrchestrationService`, streaming via `AnalysisEndpoints.cs`, and scope resolution chain. Include Known Pitfalls (HttpContext not propagated, missing tool handler registration, SSE flush timing). |
| `playbook-architecture.md` | arch | over-trimmed | Execution engine, 11 node executors, builder subsystem, embedding/indexing, scheduler | `/docs-architecture` update `docs/architecture/playbook-architecture.md` — restore depth for `PlaybookExecutionEngine`, all 11 node executors in `Services/Ai/Nodes/`, the builder agent (`Services/Ai/Builder/`), and background indexing. |
| `chat-architecture.md` | arch | new | SprkChat agent, history manager, compound intent detection, playbook dispatch, session management | `/docs-architecture` create `docs/architecture/chat-architecture.md` — document `api/Services/Ai/Chat/` (15+ files). Cover the conversation flow, intent routing, and session persistence. |
| `rag-architecture.md` | arch | new | RAG indexing pipeline, text chunking, embedding cache, scheduled indexing, vector backfill | `/docs-architecture` create `docs/architecture/rag-architecture.md` — document the full RAG pipeline end-to-end. Include integration contracts (AI Search index schemas, embedding dimensions, chunking parameters). |
| `scope-architecture.md` | arch | new | Scope management, inheritance, resolution chain, gap detection, fallback catalog | `/docs-architecture` create `docs/architecture/scope-architecture.md` — document `api/Services/Scopes/`. Cover the inheritance model and resolution chain. |
| `finance-intelligence-architecture.md` | arch | over-trimmed | VisualHost integration, invoice analysis, signal evaluation, spend snapshots, budget rules | `/docs-architecture` update — restore depth for `api/Services/Finance/` (8 files). |
| `communication-service-architecture.md` | arch | over-trimmed | Email subscription, webhook/polling hybrid, deduplication, mailbox verification | `/docs-architecture` update — restore depth for `api/Services/Communication/` (13 files). Include Known Pitfalls (duplicate webhook/poll processing, subscription expiry). |
| `email-processing-architecture.md` | arch | over-trimmed | Merge `email-to-document-architecture.md` + `email-to-document-automation.md` into one doc | `/docs-architecture` merge and update into single `email-processing-architecture.md`. Delete the two source files after merge. |
| `AI-MODEL-SELECTION-GUIDE.md` | guide | existing | Model selector config, OperationType mapping, deployment status, cost management | `/docs-guide` update — verify against current code and model deployments. |
| `SCOPE-CONFIGURATION-GUIDE.md` | guide | existing | Scope types, tool/skill/knowledge creation, builder canvas, pre-fill integration | `/docs-guide` update — verify procedures match current services. |
| `RAG-CONFIGURATION.md` | guide | existing | RAG indexing config, Document Intelligence settings, embedding strategy | `/docs-guide` update — verify all config keys match current options classes. |

## Group 3: Document Management

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `sdap-overview.md` | arch | existing | SPE platform overview, container model, file store facade, document lifecycle | `/docs-architecture` update — verify against `SpeFileStore.cs` and operation classes. |
| `sdap-document-processing-architecture.md` | arch | existing | Document processing routes, classification, profiling (765 lines, not trimmed) | Verify accuracy against current code only. |
| `HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` | guide | existing | SPE container provisioning, admin setup | `/docs-guide` update — verify against current `SpeAdmin` services. |

## Group 4: UI Framework

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `sdap-pcf-patterns.md` | arch | over-trimmed | 14 PCF controls overview, categories, shared patterns, initialization lifecycle | `/docs-architecture` update — list all 14 PCF controls with purpose and type. Include Known Pitfalls (React 16 vs 18 confusion, notifyOutputChanged not triggering updateView, stale shared lib dist/). |
| `shared-ui-components-architecture.md` | arch | new | 37 components, composition patterns, theming contracts, PCF-safe exports | `/docs-architecture` create — document component categories, theming via FluentProvider, and export strategy. |
| `code-pages-architecture.md` | arch | new | 4 code pages, React 18 entry, auth bootstrap, webpack vs Vite pipelines | `/docs-architecture` create — cover the auth bootstrap sequence and deployment model. |
| `wizard-framework-architecture.md` | arch | new | Shared Wizard component, 7 wizard solutions, step pattern, AI pre-fill | `/docs-architecture` create — cover step configuration, validation, and AI pre-fill hook. |
| `workspace-architecture.md` | arch | new | LegalWorkspace, layout wizard, panel composition, layout service | `/docs-architecture` create — cover declarative layout system and panel registration. |
| `external-access-spa-architecture.md` | arch | over-trimmed | B2B auth, BFF-only data access, three-plane model, HashRouter | `/docs-architecture` update — restore external access auth flow depth. |
| `SHARED-UI-COMPONENTS-GUIDE.md` | guide | existing | Component inventory, build, troubleshooting | `/docs-guide` update — verify component list against current code. |
| `WORKSPACE-ENTITY-CREATION-GUIDE.md` | guide | existing | Creating workspace-aware entities, wizard wiring, AI pre-fill config | `/docs-guide` update — verify file paths and procedures. |

## Group 5: Data Layer & Caching

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `caching-architecture.md` | arch | new | Redis strategy, distributed/request/token/embedding cache, TTL tiers, key conventions | `/docs-architecture` create `docs/architecture/caching-architecture.md` — document the full caching strategy across all cache types. |
| `dataverse-infrastructure-architecture.md` | arch | new | Repository pattern, security layer, document storage resolver, access data source | `/docs-architecture` create — document `api/Infrastructure/Dataverse/`. |
| `uac-access-control.md` | arch | existing | Three-plane model, fail-closed, dual-mode access data source (123 lines) | Verify accuracy against current code. |

## Group 6: Jobs & Background Processing

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `jobs-architecture.md` | arch | new | Service Bus processor, 11 handlers, idempotency, dead-letter, batch status, job contract | `/docs-architecture` create — document `api/Services/Jobs/` entirely. Include integration contracts (job message schemas, handler registration pattern). |
| `background-workers-architecture.md` | arch | new | Hosted services, scheduled indexing, playbook scheduler, communication processor | `/docs-architecture` create — document all `IHostedService` implementations. |

## Group 7: Integration

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `sdap-component-interactions.md` | arch | over-trimmed | **HIGH PRIORITY** — Cross-module impact map, change propagation paths, integration seams | `/docs-architecture` update `docs/architecture/sdap-component-interactions.md` — this is the most critical doc for preventing breaking changes. Restore the full component interaction map: when you change X, what else is affected? Cover integration seams between BFF ↔ PCF, BFF ↔ Jobs, BFF ↔ Graph, shared lib ↔ consumers. |
| `sdap-workspace-integration-patterns.md` | arch | over-trimmed | Entity-agnostic creation, fire-and-forget analyze, job handler registry | `/docs-architecture` update — restore workspace integration depth. |
| `event-to-do-architecture.md` | arch | existing | Events/todos R1→R2, inline panel vs side pane (159 lines) | Verify accuracy only. |
| `office-outlook-teams-integration-architecture.md` | arch | over-trimmed | Dialog API vs NAA, manifest config, background job pipeline | `/docs-architecture` update — restore Office add-in architecture depth. |

## Group 8: Infrastructure

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `ci-cd-architecture.md` | arch | new | 9 GitHub workflows, slot-swap, multi-stage promotion, quality gates | `/docs-architecture` create — document `.github/workflows/`. |
| `INFRASTRUCTURE-PACKAGING-STRATEGY.md` | arch | existing | Bicep modules, deployment model, resource ownership (191 lines) | Verify accuracy against `infrastructure/bicep/`. |
| `PRODUCTION-DEPLOYMENT-GUIDE.md` | guide | existing | Full production deployment procedure | `/docs-guide` update — verify commands, resources, quotas. |
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` | guide | existing | New environment provisioning | `/docs-guide` update — verify Bicep is referenced as primary. |
| `DEPLOYMENT-VERIFICATION-GUIDE.md` | guide | new | Unified post-deployment verification for all component types | `/docs-guide` create `docs/guides/DEPLOYMENT-VERIFICATION-GUIDE.md` — define: after deploying {BFF API / PCF / code page / web resource / infrastructure}, check {these specific things}. Source verification steps from each deploy skill and consolidate. |

---

## Group 9: Data Model (NEW)

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `entity-relationship-model.md` | data-model | new | Master ERD, entity relationships, lookup chains, cascade behaviors, polymorphic lookups | Create `docs/data-model/entity-relationship-model.md` — document all entity relationships: parent-child hierarchies, many-to-many via intersection, polymorphic regarding lookups. Include a relationship map showing Matter ↔ Document ↔ Analysis ↔ Event ↔ Todo ↔ Communication chains. Source from Dataverse metadata and existing schema files. |
| `alternate-keys-and-constraints.md` | data-model | enhance | Alternate key inventory, uniqueness constraints, required fields by entity | Update `docs/data-model/schema-additions-alternate-keys.md` — verify all alternate keys listed match current Dataverse schema. Add any new keys added since last update. Include which keys are used by which services (idempotency, upsert, lookup). |
| `field-mapping-reference.md` | data-model | new | Field logical names → display names, field types, max lengths, option set values | Create `docs/data-model/field-mapping-reference.md` — consolidate field mappings from scattered entity files into one reference. Critical for Claude generating correct FetchXML and WebAPI queries. Source from existing `sprk_*.md` files. |
| `json-field-schemas.md` | data-model | enhance | JSON field contracts — which fields store JSON, their schemas, and parsing services | Update `docs/data-model/sprk_event-json-fields.md` and expand to cover ALL entities with JSON fields (events, analysis results, workspace layouts, playbook canvas). Document the JSON schema for each field. |
| Existing entity docs (21 files) | data-model | existing | Individual entity schema documentation | Verify accuracy of all 21 existing entity files against current Dataverse schema. Flag any that reference deleted fields or missing relationships. |

## Group 10: Standards & Cross-Cutting (NEW)

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `CODING-STANDARDS.md` | standards | new | Consolidated coding standards: C# conventions, TypeScript conventions, naming rules, error handling patterns, logging conventions | Create `docs/standards/CODING-STANDARDS.md` — consolidate all coding conventions from CLAUDE.md, spaarke-conventions skill, and ADR constraints into one reference. Organize by language (C# / TypeScript) and concern (naming, error handling, logging, DI). |
| `ANTI-PATTERNS.md` | standards | new | Known mistakes and their fixes — cross-cutting anti-patterns that apply across the entire codebase | Create `docs/standards/ANTI-PATTERNS.md` — document the top 20 anti-patterns that have caused bugs or incidents. Source from: deploy skill troubleshooting tables, ADR "DON'T" rules, pcf-deploy/code-page-deploy/bff-deploy gotchas, and known pitfalls from architecture docs. Format: anti-pattern → why it's wrong → correct approach → ADR reference. |
| `oauth-obo-patterns.md` | standards | existing | OBO flow patterns, token handling, scope management | Verify accuracy against current auth code. |
| `INTEGRATION-CONTRACTS.md` | standards | new | Interface contracts between subsystems: BFF ↔ PCF, BFF ↔ Graph, BFF ↔ Service Bus, PCF ↔ Dataverse | Create `docs/standards/INTEGRATION-CONTRACTS.md` — document the contract at each integration seam: what data format, what auth, what error handling, what retry behavior. This prevents bugs at subsystem boundaries. |

## Group 11: Development Procedures (NEW/ENHANCED)

| Document | Type | Status | Scope | Prompt |
|----------|------|--------|-------|--------|
| `testing-and-code-quality.md` | procedures | enhance | Testing strategy by module, coverage targets, test frameworks, how to run tests | Update `docs/procedures/testing-and-code-quality.md` — add module-specific testing guidance: when you modify {AI pipeline / PCF / BFF endpoint / plugin}, run {these specific tests}. Include coverage targets per module and the arch test enforcement model. |
| `ci-cd-workflow.md` | procedures | enhance | CI/CD workflow procedures, PR checks, merge requirements | Update `docs/procedures/ci-cd-workflow.md` — verify against current GitHub workflow files. Add pre-merge checklist. |
| `CODE-REVIEW-BY-MODULE.md` | procedures | new | Module-specific code review checklists that the `/code-review` skill can load contextually | Create `docs/procedures/CODE-REVIEW-BY-MODULE.md` — define review checklists per module: AI pipeline (check HttpContext propagation, scope resolution), PCF (check React 16 APIs, theme tokens, version bump), BFF (check DI registration, endpoint filter, ProblemDetails), Plugin (check <50ms, no HTTP calls). Update `/code-review` skill to load relevant checklist based on file paths. |
| `DEPENDENCY-MANAGEMENT.md` | procedures | new | How dependencies flow: shared lib → PCF, shared lib → code pages, NuGet packages, npm packages | Create `docs/procedures/DEPENDENCY-MANAGEMENT.md` — document the dependency graph: which projects consume which shared libraries, how to update a shared dependency without breaking consumers, the CPM (Central Package Management) approach, and the shared lib dist/ compilation requirement. |

---

## Summary

| Type | Adequate | Over-trimmed | New | Enhance | Total |
|------|----------|-------------|-----|---------|-------|
| Architecture | 7 | 12 | 13 | 0 | 32 |
| Guide | 7 | 0 | 2 | 0 | 9 |
| Data Model | 21 | 0 | 2 | 2 | 25 |
| Standards | 1 | 0 | 3 | 0 | 4 |
| Procedures | 0 | 0 | 2 | 2 | 4 |
| **Total** | **36** | **12** | **22** | **4** | **74** |

## Priority Order for R2

### Tier 1 — Highest Impact (prevents bugs, enables correct code)
1. `sdap-component-interactions.md` (over-trimmed) — cross-module impact map
2. `ANTI-PATTERNS.md` (new) — prevents repeating known mistakes
3. `INTEGRATION-CONTRACTS.md` (new) — prevents seam bugs
4. `entity-relationship-model.md` (new) — correct Dataverse queries
5. `CODING-STANDARDS.md` (new) — single source of truth for conventions
6. `testing-and-code-quality.md` (enhance) — module-specific test guidance

### Tier 2 — Core Architecture (enables understanding before implementation)
7. Over-trimmed architecture docs (12) — restore depth from git history + code
8. `jobs-architecture.md` + `background-workers-architecture.md` (new) — undocumented subsystems
9. `rag-architecture.md` + `scope-architecture.md` + `chat-architecture.md` (new) — AI pipeline gaps

### Tier 3 — UI & Framework (enables correct frontend implementation)
10. `shared-ui-components-architecture.md` + `code-pages-architecture.md` (new)
11. `wizard-framework-architecture.md` + `workspace-architecture.md` (new)
12. `DEPENDENCY-MANAGEMENT.md` (new) — shared lib flow

### Tier 4 — Operational (enables correct deployment & maintenance)
13. `CONFIGURATION-MATRIX.md` + `DEPLOYMENT-VERIFICATION-GUIDE.md` (new)
14. `ci-cd-architecture.md` (new)
15. `CODE-REVIEW-BY-MODULE.md` (new) — module-specific review checklists
16. Guide updates (8) — verify accuracy
17. Data model verification (21 existing files) + new data model docs
