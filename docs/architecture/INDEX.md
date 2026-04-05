# Architecture Documentation

> **Purpose**: Technical architecture documentation for Spaarke platform components
> **Audience**: Developers, architects, AI coding agents

## Overview

This directory contains comprehensive architecture documentation covering system design, component interactions, authentication patterns, and platform-specific implementations.

## Document Index

### Authentication & Authorization

- [auth-security-boundaries.md](auth-security-boundaries.md) - Security boundary definitions and trust zones
- [auth-performance-monitoring.md](auth-performance-monitoring.md) - Auth performance metrics and monitoring
- [auth-AI-azure-resources.md](auth-AI-azure-resources.md) - AI resource endpoints, models, CLI commands
- [auth-azure-resources.md](auth-azure-resources.md) - Full Azure resource inventory
- [uac-access-control.md](uac-access-control.md) - UAC access control patterns

### SDAP (SharePoint Document Access Platform)

- [sdap-overview.md](sdap-overview.md) - SDAP system overview and concepts
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Authentication patterns for SDAP
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) - BFF API patterns for SDAP
- [sdap-pcf-patterns.md](sdap-pcf-patterns.md) - PCF control patterns for SDAP
- [sdap-component-interactions.md](sdap-component-interactions.md) - Component interaction diagrams
- [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) - Document processing pipeline
- [sdap-workspace-integration-patterns.md](sdap-workspace-integration-patterns.md) - Entity-agnostic creation, document operations, workspace pre-fill, app-only analysis

### AI

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) - Four-tier AI framework: Scope Library, Composition, Runtime, Infrastructure
- [playbook-architecture.md](playbook-architecture.md) - Playbook system — node types, execution engine, canvas model, all node executors
- [chat-architecture.md](chat-architecture.md) - SprkChat conversational AI: session management, playbook dispatch, compound intent detection, streaming pipeline
- [rag-architecture.md](rag-architecture.md) - RAG pipeline: text chunking, embedding cache, dual-index strategy, hybrid search, scheduled indexing
- [scope-architecture.md](scope-architecture.md) - Scope management: resolution chain, SYS-/CUST- ownership, single-level inheritance, gap detection
- [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) - Consolidated document processing and summarization pipeline (supersedes ai-document-summary-architecture.md)
- [ai-semantic-relationship-graph.md](ai-semantic-relationship-graph.md) - Semantic relationship graph design

### UI Components

- [shared-ui-components-architecture.md](shared-ui-components-architecture.md) - @spaarke/ui-components shared library: component catalog, theming, composition patterns, PCF-safe exports
- [code-pages-architecture.md](code-pages-architecture.md) - React 19 Code Pages: auth bootstrap, webpack single-chunk pipeline, deployment as HTML web resources
- [universal-dataset-grid-architecture.md](universal-dataset-grid-architecture.md) - Universal DataGrid for PCF and Custom Pages with OOB parity
- [SIDE-PANE-PLATFORM-ARCHITECTURE.md](SIDE-PANE-PLATFORM-ARCHITECTURE.md) - Always-available side panes: SidePaneManager, context detection, cross-pane communication, auth patterns
- [ui-dialog-shell-architecture.md](ui-dialog-shell-architecture.md) - Dialog shell architecture
- [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md) - VisualHost architecture
- [wizard-framework-architecture.md](wizard-framework-architecture.md) - Shared WizardShell: domain-free multi-step wizard dialog, dynamic steps, all wizard implementations
- [workspace-architecture.md](workspace-architecture.md) - Declarative workspace layout: WorkspaceShell, section registration, layout templates, Layout Wizard

### Email & Communication

- [email-processing-architecture.md](email-processing-architecture.md) - Consolidated email-to-document pipeline (hybrid triggers, idempotency, .eml archival, RAG indexing)
- [communication-service-architecture.md](communication-service-architecture.md) - Communication service: outbound/inbound email, webhook/polling hybrid, mailbox verification, deduplication
- [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) - Office, Outlook, and Teams integration

### Feature Architectures

- [event-to-do-architecture.md](event-to-do-architecture.md) - Event-to-do feature architecture
- [external-access-spa-architecture.md](external-access-spa-architecture.md) - External access SPA architecture
- [finance-intelligence-architecture.md](finance-intelligence-architecture.md) - Finance intelligence feature design
- [multi-environment-portability-strategy.md](multi-environment-portability-strategy.md) - Multi-environment portability strategy

> **Moved**: `AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` is a strategy/positioning doc — relocated to [`docs/enhancements/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md`](../enhancements/AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md). For current SprkChat technical architecture, see [`chat-architecture.md`](chat-architecture.md).

### Background Processing

- [jobs-architecture.md](jobs-architecture.md) - Service Bus job processing: contract, routing, 13 handlers, idempotency, dead-letter management
- [background-workers-architecture.md](background-workers-architecture.md) - All 17 BackgroundService/IHostedService implementations: queue processors, timers, channels, migrations

### Cross-Cutting Concerns

- [configuration-architecture.md](configuration-architecture.md) - Options pattern, validators, appsettings hierarchy, Key Vault integration
- [resilience-architecture.md](resilience-architecture.md) - Circuit breakers, retry policies, resilient search client, ProblemDetails error handling
- [shared-libraries-architecture.md](shared-libraries-architecture.md) - Spaarke.Core (auth, caching, utilities) and Spaarke.Dataverse (Web API client, entity models, ISP interfaces)
- [caching-architecture.md](caching-architecture.md) - Redis-first caching: 5 cache types, TTL tiers, key conventions, fail-open pattern, OpenTelemetry metrics

### Infrastructure & Repository

- [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md) - Azure resource naming standards
- [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md) - Infrastructure packaging approach
- [dataverse-infrastructure-architecture.md](dataverse-infrastructure-architecture.md) - Dataverse infrastructure: document storage resolver, access data source, cached permissions, fail-closed security
- [ci-cd-architecture.md](ci-cd-architecture.md) - CI/CD: 13 GitHub Actions workflows, slot-swap deployment, multi-stage promotion, quality gates

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
