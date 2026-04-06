# Developer Guides

> **Purpose**: Operational procedures, configuration guides, and how-to documentation for Spaarke
> **Audience**: Developers, IT administrators, AI coding agents
> **Last Reviewed**: 2026-04-05

## Overview

This directory contains practical guides for deploying, configuring, administering, and using the Spaarke platform. For architecture decisions, see `docs/architecture/`. For ADRs, see `docs/adr/`.

---

## Guide Index

### Repository & Project Navigation

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [REPOSITORY-NAVIGATION-GUIDE.md](REPOSITORY-NAVIGATION-GUIDE.md) | Quick navigation reference: top-level directory map, source code layout, naming conventions, file-location lookup | 2026-04-05 | 2026-04-05 | Verified |
| [HOW-TO-INITIATE-NEW-PROJECT.md](HOW-TO-INITIATE-NEW-PROJECT.md) | Starting new development projects using AI-assisted workflows | 2025-12-24 | — | — |

### Deployment & Infrastructure

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [PRODUCTION-DEPLOYMENT-GUIDE.md](PRODUCTION-DEPLOYMENT-GUIDE.md) | Full production deployment procedure: BFF API, Bicep infrastructure, Dataverse solutions, verification | 2026-04-05 | 2026-04-05 | Current |
| [ENVIRONMENT-DEPLOYMENT-GUIDE.md](ENVIRONMENT-DEPLOYMENT-GUIDE.md) | New environment provisioning via Bicep | 2026-03-26 | — | Production Ready |
| [CUSTOMER-DEPLOYMENT-GUIDE.md](CUSTOMER-DEPLOYMENT-GUIDE.md) | Customer-tenant deployment procedures | 2026-04-05 | — | — |
| [AZURE-SETUP-SELF-SERVICE-REGISTRATION.md](AZURE-SETUP-SELF-SERVICE-REGISTRATION.md) | Azure, Entra ID, Exchange, and Dataverse setup for the Self-Service Registration system | 2026-04-04 | — | — |
| [GITHUB-ENVIRONMENT-PROTECTION.md](GITHUB-ENVIRONMENT-PROTECTION.md) | GitHub environment protection rules for CI/CD | 2026-03-13 | — | — |
| [PCF-DEPLOYMENT-GUIDE.md](PCF-DEPLOYMENT-GUIDE.md) | PCF control build, pack, and solution import procedures | 2026-04-05 | — | — |
| [DEPLOYMENT-VERIFICATION-GUIDE.md](DEPLOYMENT-VERIFICATION-GUIDE.md) | Consolidated post-deploy verification: BFF API, PCF, code pages, web resources, infrastructure | 2026-04-05 | 2026-04-05 | New |
| [CONFIGURATION-MATRIX.md](CONFIGURATION-MATRIX.md) | Complete reference of all BFF API configuration settings: sections, defaults, locations, Key Vault secrets | 2026-04-05 | 2026-04-05 | New |
| [SECRET-ROTATION-PROCEDURES.md](SECRET-ROTATION-PROCEDURES.md) | Key Vault secret rotation procedures | 2026-04-05 | — | — |
| [SPAARKE-SELF-SERVICE-USER-REGISTRATION.md](SPAARKE-SELF-SERVICE-USER-REGISTRATION.md) | Self-service user registration system setup | 2026-04-04 | — | Phase 1 |
| [DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md](DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md) | M365 Declarative Agent build and deployment | 2026-03-26 | — | Validated |

### Customer Onboarding

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [CUSTOMER-ONBOARDING-RUNBOOK.md](CUSTOMER-ONBOARDING-RUNBOOK.md) | End-to-end customer onboarding runbook | 2026-03-20 | — | — |
| [CUSTOMER-QUICK-START-CHECKLIST.md](CUSTOMER-QUICK-START-CHECKLIST.md) | Quick start checklist for new customers | 2026-03-20 | — | — |

### AI — Deployment & Configuration

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) | AI feature deployment procedures | 2026-03-31 | — | — |
| [AI-EMBEDDING-STRATEGY.md](AI-EMBEDDING-STRATEGY.md) | Embedding model strategy, dimensions, versioning | 2026-03-31 | — | — |
| [AI-MODEL-SELECTION-GUIDE.md](AI-MODEL-SELECTION-GUIDE.md) | OperationType-based model selection, cost management | 2026-04-05 | 2026-04-05 | Current |
| [AI-MONITORING-DASHBOARD.md](AI-MONITORING-DASHBOARD.md) | AI telemetry and monitoring dashboard setup | 2026-04-05 | — | — |
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | RAG pipeline implementation deep dive (complementary to `docs/architecture/rag-architecture.md`) | 2026-03-05 | — | R3 Phases 1-5 Complete |
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | RAG indexing configuration, Document Intelligence settings, embedding strategy | 2026-04-05 | 2026-04-05 | Current |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | RAG pipeline troubleshooting | 2026-02-17 | — | — |
| [SCOPE-CONFIGURATION-GUIDE.md](SCOPE-CONFIGURATION-GUIDE.md) | Dataverse admin guide: creating Tools, Skills, Knowledge, Actions; Playbook Builder UI | 2026-04-05 | 2026-04-05 | Verified |
| [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md) | JPS schema reference, playbook design, scope catalog, model selection | 2026-04-05 | — | Production |

### AI — User-Facing

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [ai-document-summary.md](ai-document-summary.md) | AI document summarization user guide | 2026-04-05 | — | — |
| [ai-assistant-theming.md](ai-assistant-theming.md) | AI assistant theming and customization | 2026-04-05 | — | — |
| [ai-troubleshooting.md](ai-troubleshooting.md) | AI feature troubleshooting | 2025-12-29 | — | — |

> **Moved**: `SPAARKE-AI-STRATEGY-AND-ROADMAP.md` was relocated to [`docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md`](../enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md) — it's a strategy/positioning document, not a how-to guide.

### M365 Copilot

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [M365-COPILOT-ADMIN-GUIDE.md](M365-COPILOT-ADMIN-GUIDE.md) | Deployment, configuration, monitoring, troubleshooting for the M365 Copilot integration | 2026-03-26 | — | — |
| [M365-COPILOT-DEPLOYMENT-GUIDE.md](M365-COPILOT-DEPLOYMENT-GUIDE.md) | M365 Copilot deployment procedures | 2026-03-26 | — | — |
| [M365-COPILOT-USER-GUIDE.md](M365-COPILOT-USER-GUIDE.md) | Using Spaarke AI capabilities from M365 Copilot in model-driven apps | 2026-03-26 | — | — |
| [COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md](COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md) | Configuring M365 Copilot with Spaarke's data model and vocabulary | 2026-03-27 | — | — |

### Communication & Email

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [COMMUNICATION-ADMIN-GUIDE.md](COMMUNICATION-ADMIN-GUIDE.md) | Email Communication Service admin guide: accounts, send modes, inbound monitoring, document archival | 2026-03-12 | — | — |
| [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md) | Communication Service deployment: BFF API, Dataverse config, Graph API setup | 2026-03-09 | — | — |
| [communication-user-guide.md](communication-user-guide.md) | User guide for creating, sending, and tracking communications | 2026-02-21 | — | — |

### Office Add-ins

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [office-addins-admin-guide.md](office-addins-admin-guide.md) | Office add-in administration | 2026-01-24 | — | — |
| [office-addins-deployment-checklist.md](office-addins-deployment-checklist.md) | Pre-deployment verification for IT administrators | 2026-01-24 | — | — |

### Finance Intelligence

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [finance-intelligence-user-guide.md](finance-intelligence-user-guide.md) | Finance Intelligence user guide | 2026-02-13 | — | — |
| [finance-spend-snapshot-visualization-guide.md](finance-spend-snapshot-visualization-guide.md) | Spend Snapshot visualizations using VisualHost PCF Field Pivot | 2026-02-15 | — | Design Document |

### External Access

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) | External access administration setup | 2026-03-19 | — | — |
| [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md) | External access SPA user guide | 2026-03-19 | — | — |

### PCF & UI Components

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [SHARED-UI-COMPONENTS-GUIDE.md](SHARED-UI-COMPONENTS-GUIDE.md) | `@spaarke/ui-components` library: component inventory, consumption patterns, build workflow | 2026-03-30 | 2026-04-05 | Current |
| [VISUALHOST-SETUP-GUIDE.md](VISUALHOST-SETUP-GUIDE.md) | VisualHost chart configuration and form placement | 2026-03-10 | — | — |
| [DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md](DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md) | Document Relationship Viewer feature guide | 2026-04-05 | — | — |
| [DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md](DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md) | Document Upload Wizard integration | 2026-04-05 | — | — |

### Workspace & Entity Creation

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [WORKSPACE-ENTITY-CREATION-GUIDE.md](WORKSPACE-ENTITY-CREATION-GUIDE.md) | Entity creation wizards: SPE upload, Dataverse records, AI analysis, follow-on actions | 2026-04-05 | — | — |
| [WORKSPACE-AI-PREFILL-GUIDE.md](WORKSPACE-AI-PREFILL-GUIDE.md) | AI pre-fill process, playbook integration, extraction scopes | 2026-03-23 | — | — |
| [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) | Adding SDAP document support to a new Dataverse entity | 2026-03-31 | — | — |

### Dataverse

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [DATAVERSE-AUTHENTICATION-GUIDE.md](DATAVERSE-AUTHENTICATION-GUIDE.md) | Definitive guide for Dataverse authentication | 2026-03-09 | — | — |
| [DATAVERSE-MCP-INTEGRATION-GUIDE.md](DATAVERSE-MCP-INTEGRATION-GUIDE.md) | Dataverse MCP server setup, 12 tools, usage patterns, troubleshooting | 2026-04-06 | 2026-04-06 | Current |
| [DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md](DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) | Creating and updating Dataverse schema | 2026-04-05 | — | — |
| [HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md](HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) | SPE container type and container provisioning | 2026-04-05 | — | — |
| [EVENT-TYPE-CONFIGURATION.md](EVENT-TYPE-CONFIGURATION.md) | Event type configuration | 2026-04-05 | — | — |
| [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) | Adding ribbon buttons via Ribbon Workbench | 2025-12-19 | — | — |

### Monitoring & Operations

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [MONITORING-AND-ALERTING-GUIDE.md](MONITORING-AND-ALERTING-GUIDE.md) | Monitoring and alerting setup | 2026-04-05 | — | — |
| [INCIDENT-RESPONSE.md](INCIDENT-RESPONSE.md) | Incident response procedures | 2026-03-13 | — | — |

### Code Quality & Refactoring

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [SERVICE-DECOMPOSITION-GUIDE.md](SERVICE-DECOMPOSITION-GUIDE.md) | When and how to decompose God classes — zero-breaking-change strategy, feature module patterns | 2026-04-05 | — | — |
| [INTERFACE-SEGREGATION-GUIDE.md](INTERFACE-SEGREGATION-GUIDE.md) | Interface Segregation Principle applied to Dataverse services — composite interface pattern, consumer migration | 2026-04-05 | — | — |

### Reporting

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [reporting-admin.md](reporting-admin.md) | Reporting module administration | 2026-04-01 | — | — |
| [reporting-module.md](reporting-module.md) | Reporting module overview | 2026-04-01 | — | — |

---

## For AI Agents

**Loading strategy**: Load specific guides when working on related tasks. Do not load all guides proactively.

**Common scenarios**:

| Task | Load |
|---|---|
| Deploying BFF API | `PRODUCTION-DEPLOYMENT-GUIDE.md`, `DEPLOYMENT-VERIFICATION-GUIDE.md` |
| Deploying PCF control | `PCF-DEPLOYMENT-GUIDE.md` |
| Configuring settings | `CONFIGURATION-MATRIX.md` |
| Adding SDAP to entity | `HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md` |
| Creating AI playbook scopes | `SCOPE-CONFIGURATION-GUIDE.md`, `JPS-AUTHORING-GUIDE.md` |
| Configuring RAG | `RAG-CONFIGURATION.md`, `RAG-TROUBLESHOOTING.md` |
| M365 Copilot integration | `M365-COPILOT-ADMIN-GUIDE.md`, `COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md` |
| Communication admin | `COMMUNICATION-ADMIN-GUIDE.md`, `COMMUNICATION-DEPLOYMENT-GUIDE.md` |
| Workspace entity creation | `WORKSPACE-ENTITY-CREATION-GUIDE.md`, `WORKSPACE-AI-PREFILL-GUIDE.md` |
| Customer onboarding | `CUSTOMER-ONBOARDING-RUNBOOK.md`, `CUSTOMER-QUICK-START-CHECKLIST.md` |
| Incident response | `INCIDENT-RESPONSE.md`, `MONITORING-AND-ALERTING-GUIDE.md` |
| Shared UI components | `SHARED-UI-COMPONENTS-GUIDE.md` |
| Repository structure | `REPOSITORY-NAVIGATION-GUIDE.md` |
