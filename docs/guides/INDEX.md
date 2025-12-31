# Developer Guides

> **Purpose**: Step-by-step guides for common development tasks
> **Audience**: Developers, AI coding agents
> **Status**: Under review - content needs systematic revision

## Overview

This directory contains practical guides for implementing features, configuring components, and following development workflows in the Spaarke platform.

## Guide Index

<!-- TODO: Phase 3 - Organize by category and add descriptions -->

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

### Email Automation

- [EMAIL-TO-DOCUMENT-ARCHITECTURE.md](EMAIL-TO-DOCUMENT-ARCHITECTURE.md) - Email-to-document automation architecture, webhooks, filtering, and processing

### Dataverse & Platform

- [DATAVERSE-LOOKUP-INTEGRATION.md](DATAVERSE-LOOKUP-INTEGRATION.md)
- [DATAVERSE-NAVMAP-API.md](DATAVERSE-NAVMAP-API.md)
- [DATAVERSE-PLUGIN-DEVELOPMENT.md](DATAVERSE-PLUGIN-DEVELOPMENT.md)

### Deployment & Infrastructure

- [RIBBON-CUSTOMIZATION-GUIDE.md](RIBBON-CUSTOMIZATION-GUIDE.md)
- [TASK-MANAGEMENT-GUIDE.md](TASK-MANAGEMENT-GUIDE.md)

### How-To Guides

- [HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md](HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md)
- [HOW-TO-CREATE-NEW-PCF-CONTROL.md](HOW-TO-CREATE-NEW-PCF-CONTROL.md)
- [HOW-TO-DEPLOY-PCF-CONTROL.md](HOW-TO-DEPLOY-PCF-CONTROL.md)

## For AI Agents

**Loading strategy**: Load specific guides when working on related tasks.

**Common scenarios**:
- Creating PCF control → Load HOW-TO-CREATE-NEW-PCF-CONTROL.md, PCF-CONTROL-DEVELOPMENT.md
- Adding document support → Load HOW-TO-ADD-DOCUMENT-SUPPORT-TO-ENTITY.md
- AI integration → Load AI-TOOL-FRAMEWORK-GUIDE.md, AI-STREAMING-SSE-IMPLEMENTATION.md
- Dataverse work → Load DATAVERSE-*.md guides
- Email automation → Load EMAIL-TO-DOCUMENT-ARCHITECTURE.md

## Phase 3 TODO

- [ ] Organize guides by category (PCF, AI, Dataverse, etc.)
- [ ] Review for accuracy and completeness
- [ ] Identify outdated guides
- [ ] Add descriptions and when-to-use guidance
- [ ] Create concise quick-reference versions for `.claude/patterns/`
- [ ] Add cross-references between related guides
- [ ] Consolidate overlapping guides
