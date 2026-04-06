# Data Model

> **Purpose**: Dataverse entity schemas, field references, and relationship documentation for Spaarke
> **Audience**: Developers, AI coding agents, data modelers

## Overview

This directory contains authoritative documentation for Dataverse entity schemas, field definitions, alternate keys, JSON field payloads, and entity relationships used throughout the Spaarke platform.

## Data Model Index

### Cross-Entity References

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| **[entity-relationship-model.md](entity-relationship-model.md)** | Complete ERD across all `sprk_*` entities: relationships (1:N, N:1, N:N), cascade rules, dependency graph | 2026-04-05 | 2026-04-05 | New |
| **[field-mapping-reference.md](field-mapping-reference.md)** | Field mapping reference: Dataverse logical names to DTO property names to PCF/Code Page bindings, with type conversions | 2026-04-05 | 2026-04-05 | New |
| **[json-field-schemas.md](json-field-schemas.md)** | JSON field payload schemas for fields that store structured JSON (scope metadata, event config, AI results, workspace layouts) | 2026-04-05 | 2026-04-05 | New |
| [schema-additions-alternate-keys.md](schema-additions-alternate-keys.md) | Alternate key additions and schema enhancements | 2026-04-05 | 2026-04-05 | Current |
| [schema-corrections.md](schema-corrections.md) | Schema corrections applied during refactoring | 2026-02-12 | — | — |

### Entity Documentation — Communication

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_communication.md](sprk_communication.md) | Communication entity | 2026-04-05 | 2026-04-05 | Current |
| [sprk_communicationaccount.md](sprk_communicationaccount.md) | Communication account entity | 2026-04-05 | 2026-04-05 | Current |

### Entity Documentation — Financial

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_billingevent.md](sprk_billingevent.md) | Billing event entity | 2026-02-12 | — | — |
| [sprk_budget.md](sprk_budget.md) | Budget entity | 2026-02-12 | — | — |
| [sprk_invoice-matter-project.md](sprk_invoice-matter-project.md) | Invoice / matter / project entities | 2026-02-13 | — | — |
| [sprk_financial-related-entities.md](sprk_financial-related-entities.md) | Includes `sprk_budgetbucket`, `sprk_invoice`, `sprk_spendsignal`, `sprk_spendsnapshot` (stub entity pages archived under `archive/`) | 2026-02-12 | — | — |
| [sprk_workassignment.md](sprk_workassignment.md) | Work assignment entity | 2026-03-12 | — | — |
| [sprk_kpiassessment.md](sprk_kpiassessment.md) | KPI assessment entity | 2026-02-13 | — | — |

### Entity Documentation — AI Analysis

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_ERD-ai-analysis-entities.md](sprk_ERD-ai-analysis-entities.md) | AI analysis entity ERD | 2026-02-13 | — | — |
| [sprk_ai-analysis-related-entities.md](sprk_ai-analysis-related-entities.md) | AI analysis related entities | 2026-02-13 | — | — |

### Entity Documentation — Matter / Event

| Document | Description | Last Updated | Last Reviewed | Status |
|----------|-------------|--------------|---------------|--------|
| [sprk_matter-related-tables.md](sprk_matter-related-tables.md) | Matter-related tables | 2026-02-20 | — | — |
| [sprk_event-forms-guids.md](sprk_event-forms-guids.md) | Event form GUID reference | 2026-02-12 | — | — |
| [sprk_event-views-guids.md](sprk_event-views-guids.md) | Event view GUID reference | 2026-02-12 | — | — |
| [sprk_event-related-tables.md](sprk_event-related-tables.md) | Event-related tables | 2026-02-18 | — | — |

## For AI Agents

**Loading strategy**: Load specific entity docs when implementing features that touch those entities. For cross-entity work, start with `entity-relationship-model.md` and `field-mapping-reference.md`.

**Common scenarios**:
- Implementing a new field on an entity → Load the entity doc and `field-mapping-reference.md`
- Writing code that reads/writes JSON fields → Load `json-field-schemas.md`
- Understanding entity relationships and cascade behavior → Load `entity-relationship-model.md`
- Adding alternate keys → Load `schema-additions-alternate-keys.md`
