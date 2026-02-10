# Architecture Documentation

> **Purpose**: Technical architecture documentation for Spaarke platform components
> **Audience**: Developers, architects, AI coding agents
> **Status**: Under review - content needs systematic revision

## Overview

This directory contains comprehensive architecture documentation covering system design, component interactions, authentication patterns, and platform-specific implementations.

## Document Index

<!-- TODO: Phase 3 - Categorize and add descriptions -->

### Authentication & Authorization

- [auth-boundaries.md](auth-boundaries.md)
- [dataverse-oauth-authentication.md](dataverse-oauth-authentication.md)
- [oauth-obo-anti-patterns.md](oauth-obo-anti-patterns.md)
- [oauth-obo-errors.md](oauth-obo-errors.md)
- [oauth-obo-implementation.md](oauth-obo-implementation.md)

### SDAP (SharePoint Document Access Platform)

- [sdap-overview.md](sdap-overview.md)
- [sdap-auth-patterns.md](sdap-auth-patterns.md)
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md)
- [sdap-pcf-patterns.md](sdap-pcf-patterns.md)
- [sdap-troubleshooting.md](sdap-troubleshooting.md)

### UI Components

- [universal-dataset-grid-architecture.md](universal-dataset-grid-architecture.md) - Universal DataGrid for PCF and Custom Pages with OOB parity

### Platform Components

- [SPAARKE-AI-STRATEGY.md](SPAARKE-AI-STRATEGY.md)
- [SPAARKE-REPOSITORY-ARCHITECTURE.md](SPAARKE-REPOSITORY-ARCHITECTURE.md)
- [SPAARKE-UX-MANAGEMENT.md](SPAARKE-UX-MANAGEMENT.md)

### Infrastructure

- [AZURE-RESOURCE-NAMING-CONVENTION.md](AZURE-RESOURCE-NAMING-CONVENTION.md)
- [INFRASTRUCTURE-PACKAGING-STRATEGY.md](INFRASTRUCTURE-PACKAGING-STRATEGY.md)

## For AI Agents

**Loading strategy**: Load specific architecture docs on-demand when working with related components. Do not load all architecture docs proactively.

**Common scenarios**:
- Working with authentication → Load auth-boundaries.md, oauth-obo-*.md
- Working with SDAP → Load sdap-*.md files
- Creating new resources → Load AZURE-RESOURCE-NAMING-CONVENTION.md
- Understanding AI features → Load SPAARKE-AI-STRATEGY.md

## Phase 3 TODO

- [ ] Categorize documents by component/concern
- [ ] Review for duplication and consolidation opportunities
- [ ] Add brief descriptions for each document
- [ ] Identify which docs should have concise versions in `.claude/`
- [ ] Create cross-reference links between related docs
- [ ] Consider breaking large docs into focused topics
