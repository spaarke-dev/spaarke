# Developer Guides

> **Purpose**: Step-by-step guides for common development tasks
> **Audience**: Developers, AI coding agents
> **Status**: Under review - content needs systematic revision

## Overview

This directory contains practical guides for implementing features, configuring components, and following development workflows in the Spaarke platform.

## Guide Index

<!-- TODO: Phase 3 - Organize by category and add descriptions -->

### Shared Libraries

- **[SHARED-UI-COMPONENTS-GUIDE.md](SHARED-UI-COMPONENTS-GUIDE.md)** - `@spaarke/ui-components` library: component inventory, consumption patterns (barrel vs deep import), build workflow, adding components

### AI & Document Processing

- **[JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md)** - JPS schema reference, playbook design with Claude Code, scope catalog, model selection, migration guide
- **[SCOPE-CONFIGURATION-GUIDE.md](SCOPE-CONFIGURATION-GUIDE.md)** - Dataverse admin guide: creating Tools, Skills, Knowledge, Actions; Playbook Builder UI; pre-fill integration
- [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) - RAG pipeline architecture and knowledge-augmented execution

### Code Quality & Refactoring

- **[SERVICE-DECOMPOSITION-GUIDE.md](SERVICE-DECOMPOSITION-GUIDE.md)** - When and how to decompose God classes — zero-breaking-change strategy, feature module patterns, R2 examples (OfficeService, AnalysisOrchestrationService)
- **[INTERFACE-SEGREGATION-GUIDE.md](INTERFACE-SEGREGATION-GUIDE.md)** - Interface Segregation Principle applied to Dataverse services — composite interface pattern, consumer migration, R2 examples (IDataverseService → 9 focused interfaces)

### Deployment & Infrastructure

- **[CONFIGURATION-MATRIX.md](CONFIGURATION-MATRIX.md)** - Complete reference of all BFF API configuration settings: sections, defaults, locations (appsettings/env var/Key Vault/Dataverse), and Key Vault secrets
- **[DEPLOYMENT-VERIFICATION-GUIDE.md](DEPLOYMENT-VERIFICATION-GUIDE.md)** - Consolidated post-deploy verification steps for BFF API, PCF controls, code pages, web resources, and infrastructure
- [RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md](RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md)

### Workspace & Entity Creation

- **[WORKSPACE-ENTITY-CREATION-GUIDE.md](WORKSPACE-ENTITY-CREATION-GUIDE.md)** - **NEW**: Full guide for entity creation wizards (Matter, Project, etc.) — SPE upload, Dataverse records, AI analysis, follow-on actions
- **[WORKSPACE-AI-PREFILL-GUIDE.md](WORKSPACE-AI-PREFILL-GUIDE.md)** - **NEW**: AI pre-fill process, playbook integration, and r2 roadmap for configurable extraction scopes

### How-To Guides

- [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md)
- [PCF-DEPLOYMENT-GUIDE.md](PCF-DEPLOYMENT-GUIDE.md)

## For AI Agents

**Loading strategy**: Load specific guides when working on related tasks.

**Common scenarios**:
- Shared UI components → Load SHARED-UI-COMPONENTS-GUIDE.md
- Deploying PCF control → Load PCF-DEPLOYMENT-GUIDE.md
- Adding SDAP to an entity → Load HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md
- **Creating AI playbook scopes → Load SCOPE-CONFIGURATION-GUIDE.md**
- **Designing AI playbooks → Load JPS-AUTHORING-GUIDE.md** (or use `/jps-playbook-design` skill)
- **Builder UI usage → Load SCOPE-CONFIGURATION-GUIDE.md**
- **JPS prompt schema → Load JPS-AUTHORING-GUIDE.md**
- Workspace entity creation → Load WORKSPACE-ENTITY-CREATION-GUIDE.md
- AI pre-fill → Load WORKSPACE-AI-PREFILL-GUIDE.md

## Phase 3 TODO

- [ ] Organize guides by category (PCF, AI, Dataverse, etc.)
- [ ] Review for accuracy and completeness
- [ ] Identify outdated guides
- [ ] Add descriptions and when-to-use guidance
- [ ] Create concise quick-reference versions for `.claude/patterns/`
- [ ] Add cross-references between related guides
- [ ] Consolidate overlapping guides
