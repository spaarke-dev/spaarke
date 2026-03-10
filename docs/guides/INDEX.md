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

### PCF Development

- [PCF-BUILDING-BLOCKS.md](PCF-BUILDING-BLOCKS.md)
- [PCF-CONTROL-DEVELOPMENT.md](PCF-CONTROL-DEVELOPMENT.md)
- [PCF-DATASET-CONTROL.md](PCF-DATASET-CONTROL.md)
- [PCF-FIELD-CONTROL.md](PCF-FIELD-CONTROL.md)

### AI & Document Processing

- [AI-AGENT-PROMPT-ENGINEERING.md](AI-AGENT-PROMPT-ENGINEERING.md)
- [AI-DOCUMENT-INTELLIGENCE-INTEGRATION.md](AI-DOCUMENT-INTELLIGENCE-INTEGRATION.md)
- [AI-STREAMING-SSE-IMPLEMENTATION.md](AI-STREAMING-SSE-IMPLEMENTATION.md)
- [AI-TOOL-FRAMEWORK-GUIDE.md](AI-TOOL-FRAMEWORK-GUIDE.md)
- **[HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md](HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md)** - User guide for creating Tools, Skills, Knowledge, and Actions in Dataverse
- **[PLAYBOOK-CREATION-GUIDE.md](PLAYBOOK-CREATION-GUIDE.md)** - **NEW**: End-to-end guide for creating AI playbooks via Claude Code — scope selection, model optimization, deployment
- [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md) - JSON Prompt Schema authoring reference
- [JPS-COMPREHENSIVE-GUIDE.md](JPS-COMPREHENSIVE-GUIDE.md) - Full JPS schema reference with patterns and deployment
- [SPAARKE-AI-ARCHITECTURE.md](SPAARKE-AI-ARCHITECTURE.md) - Platform AI architecture overview (pipeline, tools, agents)
- [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) - RAG pipeline architecture and knowledge-augmented execution

### Email Automation

- [EMAIL-TO-DOCUMENT-ARCHITECTURE.md](EMAIL-TO-DOCUMENT-ARCHITECTURE.md) - Email-to-document automation architecture, webhooks, filtering, and processing

### Dataverse & Platform

- [DATAVERSE-LOOKUP-INTEGRATION.md](DATAVERSE-LOOKUP-INTEGRATION.md)
- [DATAVERSE-NAVMAP-API.md](DATAVERSE-NAVMAP-API.md)
- [DATAVERSE-PLUGIN-DEVELOPMENT.md](DATAVERSE-PLUGIN-DEVELOPMENT.md)

### Deployment & Infrastructure

- [RIBBON-CUSTOMIZATION-GUIDE.md](RIBBON-CUSTOMIZATION-GUIDE.md)
- [TASK-MANAGEMENT-GUIDE.md](TASK-MANAGEMENT-GUIDE.md)

### Workspace Features

- **[EVENT-TODO-ARCHITECTURE.md](EVENT-TODO-ARCHITECTURE.md)** - Smart To Do Kanban board + Todo Detail side pane architecture, dual-entity data model, score formula, cross-component sync

### Workspace & Entity Creation

- **[WORKSPACE-ENTITY-CREATION-GUIDE.md](WORKSPACE-ENTITY-CREATION-GUIDE.md)** - **NEW**: Full guide for entity creation wizards (Matter, Project, etc.) — SPE upload, Dataverse records, AI analysis, follow-on actions
- **[WORKSPACE-AI-PREFILL-GUIDE.md](WORKSPACE-AI-PREFILL-GUIDE.md)** - **NEW**: AI pre-fill process, playbook integration, and r2 roadmap for configurable extraction scopes

### How-To Guides

- [HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md](HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md)
- [HOW-TO-CREATE-NEW-PCF-CONTROL.md](HOW-TO-CREATE-NEW-PCF-CONTROL.md)
- [HOW-TO-DEPLOY-PCF-CONTROL.md](HOW-TO-DEPLOY-PCF-CONTROL.md)

## For AI Agents

**Loading strategy**: Load specific guides when working on related tasks.

**Common scenarios**:
- Shared UI components → Load SHARED-UI-COMPONENTS-GUIDE.md
- Creating PCF control → Load HOW-TO-CREATE-NEW-PCF-CONTROL.md, PCF-CONTROL-DEVELOPMENT.md
- Adding document support → Load HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md
- **Creating AI playbook scopes → Load HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md**
- **Creating AI playbooks → Load PLAYBOOK-CREATION-GUIDE.md** (or use `/jps-playbook-design` skill)
- AI integration → Load AI-TOOL-FRAMEWORK-GUIDE.md, AI-STREAMING-SSE-IMPLEMENTATION.md
- Dataverse work → Load DATAVERSE-*.md guides
- Email automation → Load EMAIL-TO-DOCUMENT-ARCHITECTURE.md
- Workspace entity creation → Load WORKSPACE-ENTITY-CREATION-GUIDE.md
- AI pre-fill → Load WORKSPACE-AI-PREFILL-GUIDE.md
- To Do / Kanban work → Load EVENT-TODO-ARCHITECTURE.md

## Phase 3 TODO

- [ ] Organize guides by category (PCF, AI, Dataverse, etc.)
- [ ] Review for accuracy and completeness
- [ ] Identify outdated guides
- [ ] Add descriptions and when-to-use guidance
- [ ] Create concise quick-reference versions for `.claude/patterns/`
- [ ] Add cross-references between related guides
- [ ] Consolidate overlapping guides
