# Architecture Documentation

> **Purpose**: Technical architecture documentation for Spaarke platform components
> **Audience**: Developers, architects, AI coding agents

## Overview

This directory contains comprehensive architecture documentation covering system design, component interactions, authentication patterns, and platform-specific implementations.

## Document Index

### Authentication & Authorization

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [auth-security-boundaries.md](auth-security-boundaries.md) | Security boundary definitions and trust zones | 2026-04-05 | 2026-04-05 | Current |
| [AUTH-AND-BFF-URL-PATTERN.md](AUTH-AND-BFF-URL-PATTERN.md) | BFF URL normalization, auth cascade, PCF/Code Page auth init, authenticatedFetch retry semantics | 2026-04-02 | 2026-04-05 | Verified |
| [auth-performance-monitoring.md](auth-performance-monitoring.md) | Auth performance metrics and monitoring (illustrative — see doc for actual OBO cache implementation) | 2026-04-05 | 2026-04-05 | Current |
| [auth-AI-azure-resources.md](auth-AI-azure-resources.md) | AI resource endpoints, models, CLI commands | 2026-04-05 | 2026-04-05 | Current |
| [auth-azure-resources.md](auth-azure-resources.md) | Full Azure resource inventory | 2026-04-05 | 2026-04-05 | Current |
| [uac-access-control.md](uac-access-control.md) | UAC access control patterns | 2026-03-16 | 2026-04-05 | Verified |

### SDAP (SharePoint Document Access Platform)

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sdap-overview.md](sdap-overview.md) | SDAP system overview and concepts | 2026-04-05 | 2026-04-05 | Current |
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Authentication patterns for SDAP | 2026-04-05 | 2026-04-05 | Current |
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF API patterns for SDAP | 2026-04-05 | 2026-04-05 | Current |
| [sdap-pcf-patterns.md](sdap-pcf-patterns.md) | PCF control patterns for SDAP | 2026-04-05 | 2026-04-05 | Current |
| [sdap-component-interactions.md](sdap-component-interactions.md) | Component interaction diagrams | 2026-04-05 | 2026-04-05 | Current |
| [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) | Document processing pipeline | 2026-04-05 | 2026-04-05 | Current |
| [sdap-workspace-integration-patterns.md](sdap-workspace-integration-patterns.md) | Entity-agnostic creation, document operations, workspace pre-fill, app-only analysis | 2026-04-05 | 2026-04-05 | Current |

### AI

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) | Four-tier AI framework: Scope Library, Composition, Runtime, Infrastructure | 2026-04-05 | 2026-04-05 | Current |
| [playbook-architecture.md](playbook-architecture.md) | Playbook system — node types, execution engine, canvas model, all node executors | 2026-04-05 | 2026-04-05 | Current |
| [chat-architecture.md](chat-architecture.md) | SprkChat conversational AI: session management, playbook dispatch, compound intent detection, streaming pipeline | 2026-04-05 | 2026-04-05 | New |
| [rag-architecture.md](rag-architecture.md) | RAG pipeline: text chunking, embedding cache, dual-index strategy, hybrid search, scheduled indexing | 2026-04-05 | 2026-04-05 | New |
| [scope-architecture.md](scope-architecture.md) | Scope management: resolution chain, SYS-/CUST- ownership, single-level inheritance, gap detection | 2026-04-05 | 2026-04-05 | New |
| [ai-semantic-relationship-graph.md](ai-semantic-relationship-graph.md) | Multi-modal document discovery combining Dataverse structural lookups with AI Search vector similarity | 2026-02-23 | 2026-04-05 | Verified |
| [M365-COPILOT-INTEGRATION-ARCHITECTURE.md](M365-COPILOT-INTEGRATION-ARCHITECTURE.md) | M365 Copilot integration: Declarative Agent, API Plugin, agent gateway layer, Adaptive Cards | 2026-03-26 | 2026-04-05 | Verified |

> For document processing and summarization pipeline (which consolidates former ai-document-summary-architecture.md), see [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) in the SDAP section above.

### UI Components

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [shared-ui-components-architecture.md](shared-ui-components-architecture.md) | @spaarke/ui-components shared library: component catalog, theming, composition patterns, PCF-safe exports | 2026-04-05 | 2026-04-05 | New |
| [code-pages-architecture.md](code-pages-architecture.md) | React 19 Code Pages: auth bootstrap, webpack single-chunk pipeline, deployment as HTML web resources | 2026-04-05 | 2026-04-05 | New |
| [universal-dataset-grid-architecture.md](universal-dataset-grid-architecture.md) | Universal DataGrid for PCF and Custom Pages with OOB parity | 2026-04-05 | 2026-04-05 | Current |
| [SIDE-PANE-PLATFORM-ARCHITECTURE.md](SIDE-PANE-PLATFORM-ARCHITECTURE.md) | ⚠️ **SUPERSEDED** — describes removed SidePaneManager/auto-injection model. Retained for historical context only. For current SprkChat side pane implementation see `code-pages-architecture.md` and `chat-architecture.md`. | 2026-03-04 | 2026-04-05 | SUPERSEDED |
| [ui-dialog-shell-architecture.md](ui-dialog-shell-architecture.md) | Dialog shell architecture | 2026-03-19 | 2026-04-05 | Verified |
| [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md) | VisualHost architecture | 2026-04-05 | 2026-04-05 | Verified |
| [wizard-framework-architecture.md](wizard-framework-architecture.md) | Shared WizardShell: domain-free multi-step wizard dialog, dynamic steps, all wizard implementations | 2026-04-05 | 2026-04-05 | New |
| [workspace-architecture.md](workspace-architecture.md) | Declarative workspace layout: WorkspaceShell, section registration, layout templates, Layout Wizard | 2026-04-05 | 2026-04-05 | New |

### Email & Communication

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [email-processing-architecture.md](email-processing-architecture.md) | Consolidated email-to-document pipeline (hybrid triggers, idempotency, .eml archival, RAG indexing) | 2026-04-05 | 2026-04-05 | New |
| [communication-service-architecture.md](communication-service-architecture.md) | Communication service: outbound/inbound email, webhook/polling hybrid, mailbox verification, deduplication | 2026-04-05 | 2026-04-05 | Current |
| [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) | Office, Outlook, and Teams integration | 2026-04-05 | 2026-04-05 | Current |

### Feature Architectures

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [event-to-do-architecture.md](event-to-do-architecture.md) | Event-to-do feature architecture | 2026-04-05 | 2026-04-05 | Implementation Complete |
| [external-access-spa-architecture.md](external-access-spa-architecture.md) | External access SPA architecture | 2026-04-05 | 2026-04-05 | Current |
| [finance-intelligence-architecture.md](finance-intelligence-architecture.md) | Finance intelligence feature design | 2026-04-05 | 2026-04-05 | Current |

> **Moved**: `AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` is a strategy/positioning doc — relocated to [`docs/enhancements/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md`](../enhancements/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md). For current SprkChat technical architecture, see [`chat-architecture.md`](chat-architecture.md).

### Background Processing

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [jobs-architecture.md](jobs-architecture.md) | Service Bus job processing: contract, routing, 13 handlers, idempotency, dead-letter management | 2026-04-05 | 2026-04-05 | New |
| [background-workers-architecture.md](background-workers-architecture.md) | All 17 BackgroundService/IHostedService implementations: queue processors, timers, channels, migrations | 2026-04-05 | 2026-04-05 | New |

### Cross-Cutting Concerns

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [configuration-architecture.md](configuration-architecture.md) | Options pattern, validators, appsettings hierarchy, Key Vault integration | 2026-04-05 | 2026-04-05 | New |
| [resilience-architecture.md](resilience-architecture.md) | Circuit breakers, retry policies, resilient search client, ProblemDetails error handling | 2026-04-05 | 2026-04-05 | New |
| [shared-libraries-architecture.md](shared-libraries-architecture.md) | Spaarke.Core (auth, caching, utilities) and Spaarke.Dataverse (Web API client, entity models, ISP interfaces) | 2026-04-05 | 2026-04-05 | New |
| [caching-architecture.md](caching-architecture.md) | Redis-first caching: 5 cache types, TTL tiers, key conventions, fail-open pattern, OpenTelemetry metrics | 2026-04-05 | 2026-04-05 | New |
| [multi-environment-portability-strategy.md](multi-environment-portability-strategy.md) | Multi-environment deployment strategy: alternate keys, option sets, environment variables architecture | 2026-04-05 | 2026-04-05 | Verified |

### Infrastructure & Repository

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md) | Azure resource naming standards | 2026-04-05 | 2026-04-05 | Verified |
| [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md) | Infrastructure packaging approach | 2026-04-05 | 2026-04-05 | Current |
| [dataverse-infrastructure-architecture.md](dataverse-infrastructure-architecture.md) | Dataverse infrastructure: document storage resolver, access data source, cached permissions, fail-closed security | 2026-04-05 | 2026-04-05 | New |
| [ci-cd-architecture.md](ci-cd-architecture.md) | CI/CD: 13 GitHub Actions workflows, slot-swap deployment, multi-stage promotion, quality gates | 2026-04-05 | 2026-04-05 | New |

## For AI Agents

**Loading strategy**: Load specific architecture docs on-demand when working with related components. Do not load all architecture docs proactively.

**Common scenarios**:
- Working with authentication → Load `auth-security-boundaries.md`, `auth-AI-azure-resources.md`
- Working with SDAP → Load relevant `sdap-*.md` files
- Workspace integration → Load `sdap-workspace-integration-patterns.md`
- Creating new Azure resources → Load `AZURE-RESOURCE-NAMING-CONVENTION.md`
- Understanding AI features → Load `AI-ARCHITECTURE.md`, `playbook-architecture.md`
- Side panes, context awareness, cross-pane communication → Load `SIDE-PANE-PLATFORM-ARCHITECTURE.md`
- Repository structure, directory layout → Load `docs/guides/REPOSITORY-NAVIGATION-GUIDE.md`
- Configuration, options classes → Load `configuration-architecture.md`
- Retry/circuit breaker, error handling → Load `resilience-architecture.md`
- Shared .NET libraries, authorization, caching → Load `shared-libraries-architecture.md`, `caching-architecture.md`
- Wizard dialogs, multi-step workflows → Load `wizard-framework-architecture.md`
- Workspace dashboard, layout personalization → Load `workspace-architecture.md`
