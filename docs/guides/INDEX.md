# Developer Guides

> **Purpose**: Operational procedures, configuration guides, and how-to documentation for Spaarke
> **Audience**: Developers, IT administrators, AI coding agents
> **Last Reviewed**: 2026-04-05

## Overview

This directory contains practical guides for deploying, configuring, administering, and using the Spaarke platform. For architecture decisions, see `docs/architecture/`. For ADRs, see `docs/adr/`.

---

## Guide Index

### Repository & Project Navigation

- [REPOSITORY-NAVIGATION-GUIDE.md](REPOSITORY-NAVIGATION-GUIDE.md) - Quick navigation reference: top-level directory map, source code layout, naming conventions, file-location lookup
- [HOW-TO-INITIATE-NEW-PROJECT.md](HOW-TO-INITIATE-NEW-PROJECT.md) - Starting new development projects using AI-assisted workflows

### Deployment & Infrastructure

- [PRODUCTION-DEPLOYMENT-GUIDE.md](PRODUCTION-DEPLOYMENT-GUIDE.md) - Full production deployment procedure: BFF API, Bicep infrastructure, Dataverse solutions, verification
- [ENVIRONMENT-DEPLOYMENT-GUIDE.md](ENVIRONMENT-DEPLOYMENT-GUIDE.md) - New environment provisioning via Bicep
- [CUSTOMER-DEPLOYMENT-GUIDE.md](CUSTOMER-DEPLOYMENT-GUIDE.md) - Customer-tenant deployment procedures
- [AZURE-SETUP-SELF-SERVICE-REGISTRATION.md](AZURE-SETUP-SELF-SERVICE-REGISTRATION.md) - Azure, Entra ID, Exchange, and Dataverse setup for the Self-Service Registration system
- [GITHUB-ENVIRONMENT-PROTECTION.md](GITHUB-ENVIRONMENT-PROTECTION.md) - GitHub environment protection rules for CI/CD
- [PCF-DEPLOYMENT-GUIDE.md](PCF-DEPLOYMENT-GUIDE.md) - PCF control build, pack, and solution import procedures
- [DEPLOYMENT-VERIFICATION-GUIDE.md](DEPLOYMENT-VERIFICATION-GUIDE.md) - Consolidated post-deploy verification: BFF API, PCF, code pages, web resources, infrastructure
- [CONFIGURATION-MATRIX.md](CONFIGURATION-MATRIX.md) - Complete reference of all BFF API configuration settings: sections, defaults, locations, Key Vault secrets
- [SECRET-ROTATION-PROCEDURES.md](SECRET-ROTATION-PROCEDURES.md) - Key Vault secret rotation procedures
- [SPAARKE-SELF-SERVICE-USER-REGISTRATION.md](SPAARKE-SELF-SERVICE-USER-REGISTRATION.md) - Self-service user registration system setup
- [DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md](DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md) - M365 Declarative Agent build and deployment

### Customer Onboarding

- [CUSTOMER-ONBOARDING-RUNBOOK.md](CUSTOMER-ONBOARDING-RUNBOOK.md) - End-to-end customer onboarding runbook
- [CUSTOMER-QUICK-START-CHECKLIST.md](CUSTOMER-QUICK-START-CHECKLIST.md) - Quick start checklist for new customers

### AI — Deployment & Configuration

- [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) - AI feature deployment procedures
- [AI-EMBEDDING-STRATEGY.md](AI-EMBEDDING-STRATEGY.md) - Embedding model strategy, dimensions, versioning
- [AI-MODEL-SELECTION-GUIDE.md](AI-MODEL-SELECTION-GUIDE.md) - OperationType-based model selection, cost management
- [AI-MONITORING-DASHBOARD.md](AI-MONITORING-DASHBOARD.md) - AI telemetry and monitoring dashboard setup
- [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) - RAG pipeline implementation deep dive (complementary to `docs/architecture/rag-architecture.md`)
- [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) - RAG indexing configuration, Document Intelligence settings, embedding strategy
- [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) - RAG pipeline troubleshooting
- [SCOPE-CONFIGURATION-GUIDE.md](SCOPE-CONFIGURATION-GUIDE.md) - Dataverse admin guide: creating Tools, Skills, Knowledge, Actions; Playbook Builder UI
- [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md) - JPS schema reference, playbook design, scope catalog, model selection

### AI — User-Facing

- [ai-document-summary.md](ai-document-summary.md) - AI document summarization user guide
- [ai-assistant-theming.md](ai-assistant-theming.md) - AI assistant theming and customization
- [ai-troubleshooting.md](ai-troubleshooting.md) - AI feature troubleshooting

> **Moved**: `SPAARKE-AI-STRATEGY-AND-ROADMAP.md` was relocated to [`docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md`](../enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md) — it's a strategy/positioning document, not a how-to guide.

### M365 Copilot

- [M365-COPILOT-ADMIN-GUIDE.md](M365-COPILOT-ADMIN-GUIDE.md) - Deployment, configuration, monitoring, troubleshooting for the M365 Copilot integration
- [M365-COPILOT-DEPLOYMENT-GUIDE.md](M365-COPILOT-DEPLOYMENT-GUIDE.md) - M365 Copilot deployment procedures
- [M365-COPILOT-USER-GUIDE.md](M365-COPILOT-USER-GUIDE.md) - Using Spaarke AI capabilities from M365 Copilot in model-driven apps
- [COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md](COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md) - Configuring M365 Copilot with Spaarke's data model and vocabulary

### Communication & Email

- [COMMUNICATION-ADMIN-GUIDE.md](COMMUNICATION-ADMIN-GUIDE.md) - Email Communication Service admin guide: accounts, send modes, inbound monitoring, document archival
- [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md) - Communication Service deployment: BFF API, Dataverse config, Graph API setup
- [communication-user-guide.md](communication-user-guide.md) - User guide for creating, sending, and tracking communications

### Office Add-ins

- [office-addins-admin-guide.md](office-addins-admin-guide.md) - Office add-in administration
- [office-addins-deployment-checklist.md](office-addins-deployment-checklist.md) - Pre-deployment verification for IT administrators

### Finance Intelligence

- [finance-intelligence-user-guide.md](finance-intelligence-user-guide.md) - Finance Intelligence user guide
- [finance-spend-snapshot-visualization-guide.md](finance-spend-snapshot-visualization-guide.md) - Spend Snapshot visualizations using VisualHost PCF Field Pivot

### External Access

- [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) - External access administration setup
- [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md) - External access SPA user guide

### PCF & UI Components

- [SHARED-UI-COMPONENTS-GUIDE.md](SHARED-UI-COMPONENTS-GUIDE.md) - `@spaarke/ui-components` library: component inventory, consumption patterns, build workflow
- [VISUALHOST-SETUP-GUIDE.md](VISUALHOST-SETUP-GUIDE.md) - VisualHost chart configuration and form placement
- [DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md](DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md) - Document Relationship Viewer feature guide
- [DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md](DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md) - Document Upload Wizard integration

### Workspace & Entity Creation

- [WORKSPACE-ENTITY-CREATION-GUIDE.md](WORKSPACE-ENTITY-CREATION-GUIDE.md) - Entity creation wizards: SPE upload, Dataverse records, AI analysis, follow-on actions
- [WORKSPACE-AI-PREFILL-GUIDE.md](WORKSPACE-AI-PREFILL-GUIDE.md) - AI pre-fill process, playbook integration, extraction scopes
- [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) - Adding SDAP document support to a new Dataverse entity

### Dataverse

- [DATAVERSE-AUTHENTICATION-GUIDE.md](DATAVERSE-AUTHENTICATION-GUIDE.md) - Definitive guide for Dataverse authentication
- [DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md](DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) - Creating and updating Dataverse schema
- [HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md](HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) - SPE container type and container provisioning
- [EVENT-TYPE-CONFIGURATION.md](EVENT-TYPE-CONFIGURATION.md) - Event type configuration
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) - Adding ribbon buttons via Ribbon Workbench

### Monitoring & Operations

- [MONITORING-AND-ALERTING-GUIDE.md](MONITORING-AND-ALERTING-GUIDE.md) - Monitoring and alerting setup
- [INCIDENT-RESPONSE.md](INCIDENT-RESPONSE.md) - Incident response procedures

### Code Quality & Refactoring

- [SERVICE-DECOMPOSITION-GUIDE.md](SERVICE-DECOMPOSITION-GUIDE.md) - When and how to decompose God classes — zero-breaking-change strategy, feature module patterns
- [INTERFACE-SEGREGATION-GUIDE.md](INTERFACE-SEGREGATION-GUIDE.md) - Interface Segregation Principle applied to Dataverse services — composite interface pattern, consumer migration

### Reporting

- [reporting-admin.md](reporting-admin.md) - Reporting module administration
- [reporting-module.md](reporting-module.md) - Reporting module overview

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
