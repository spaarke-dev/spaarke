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
- [ai-document-summary-architecture.md](ai-document-summary-architecture.md) - Document summarization architecture
- [ai-semantic-relationship-graph.md](ai-semantic-relationship-graph.md) - Semantic relationship graph design

### UI Components

- [universal-dataset-grid-architecture.md](universal-dataset-grid-architecture.md) - Universal DataGrid for PCF and Custom Pages with OOB parity
- [SIDE-PANE-PLATFORM-ARCHITECTURE.md](SIDE-PANE-PLATFORM-ARCHITECTURE.md) - Always-available side panes: SidePaneManager, context detection, cross-pane communication, auth patterns
- [ui-dialog-shell-architecture.md](ui-dialog-shell-architecture.md) - Dialog shell architecture
- [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md) - VisualHost architecture

### Email & Communication

- [email-to-document-architecture.md](email-to-document-architecture.md) - Email-to-document conversion architecture
- [email-to-document-automation.md](email-to-document-automation.md) - Email automation service design
- [communication-service-architecture.md](communication-service-architecture.md) - Communication service patterns
- [office-outlook-teams-integration-architecture.md](office-outlook-teams-integration-architecture.md) - Office, Outlook, and Teams integration

### Feature Architectures

- [event-to-do-architecture.md](event-to-do-architecture.md) - Event-to-do feature architecture
- [external-access-spa-architecture.md](external-access-spa-architecture.md) - External access SPA architecture
- [finance-intelligence-architecture.md](finance-intelligence-architecture.md) - Finance intelligence feature design
- [multi-environment-portability-strategy.md](multi-environment-portability-strategy.md) - Multi-environment portability strategy
- [AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md](AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md) - M365 Copilot vs Sprk Chat strategy analysis

### Infrastructure & Repository

- [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md) - Azure resource naming standards
- [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md) - Infrastructure packaging approach
- [SPAARKE-REPOSITORY-ARCHITECTURE.md](SPAARKE-REPOSITORY-ARCHITECTURE.md) - Repository structure guide for developers and AI agents

## For AI Agents

**Loading strategy**: Load specific architecture docs on-demand when working with related components. Do not load all architecture docs proactively.

**Common scenarios**:
- Working with authentication → Load `auth-security-boundaries.md`, `auth-AI-azure-resources.md`
- Working with SDAP → Load relevant `sdap-*.md` files
- Workspace integration → Load `sdap-workspace-integration-patterns.md`
- Creating new Azure resources → Load `AZURE-RESOURCE-NAMING-CONVENTION.md`
- Understanding AI features → Load `AI-ARCHITECTURE.md`, `playbook-architecture.md`
- Side panes, context awareness, cross-pane communication → Load `SIDE-PANE-PLATFORM-ARCHITECTURE.md`
- Repository structure, directory layout → Load `SPAARKE-REPOSITORY-ARCHITECTURE.md`
