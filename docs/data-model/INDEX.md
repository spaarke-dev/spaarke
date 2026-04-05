# Data Model

> **Purpose**: Dataverse entity schemas, field references, and relationship documentation for Spaarke
> **Audience**: Developers, AI coding agents, data modelers

## Overview

This directory contains authoritative documentation for Dataverse entity schemas, field definitions, alternate keys, JSON field payloads, and entity relationships used throughout the Spaarke platform.

## Data Model Index

### Cross-Entity References

- **[entity-relationship-model.md](entity-relationship-model.md)** - Complete ERD across all `sprk_*` entities: relationships (1:N, N:1, N:N), cascade rules, dependency graph
- **[field-mapping-reference.md](field-mapping-reference.md)** - Field mapping reference: Dataverse logical names to DTO property names to PCF/Code Page bindings, with type conversions
- **[json-field-schemas.md](json-field-schemas.md)** - JSON field payload schemas for fields that store structured JSON (scope metadata, event config, AI results, workspace layouts)
- [schema-additions-alternate-keys.md](schema-additions-alternate-keys.md) - Alternate key additions and schema enhancements
- [schema-corrections.md](schema-corrections.md) - Schema corrections applied during refactoring

### Entity Documentation — Communication

- [sprk_communication.md](sprk_communication.md) - Communication entity
- [sprk_communicationaccount.md](sprk_communicationaccount.md) - Communication account entity

### Entity Documentation — Financial

- [sprk_billingevent.md](sprk_billingevent.md)
- [sprk_budget.md](sprk_budget.md)
- [sprk_invoice-matter-project.md](sprk_invoice-matter-project.md)
- [sprk_financial-related-entities.md](sprk_financial-related-entities.md) - Includes `sprk_budgetbucket`, `sprk_invoice`, `sprk_spendsignal`, `sprk_spendsnapshot` (stub entity pages archived under `archive/`)
- [sprk_workassignment.md](sprk_workassignment.md)
- [sprk_kpiassessment.md](sprk_kpiassessment.md)

### Entity Documentation — AI Analysis

- [sprk_ERD-ai-analysis-entities.md](sprk_ERD-ai-analysis-entities.md)
- [sprk_ai-analysis-related-entities.md](sprk_ai-analysis-related-entities.md)

### Entity Documentation — Matter / Event

- [sprk_matter-related-tables.md](sprk_matter-related-tables.md)
- [sprk_event-forms-guids.md](sprk_event-forms-guids.md)
- [sprk_event-views-guids.md](sprk_event-views-guids.md)
- [sprk_event-related-tables.md](sprk_event-related-tables.md)

## For AI Agents

**Loading strategy**: Load specific entity docs when implementing features that touch those entities. For cross-entity work, start with `entity-relationship-model.md` and `field-mapping-reference.md`.

**Common scenarios**:
- Implementing a new field on an entity → Load the entity doc and `field-mapping-reference.md`
- Writing code that reads/writes JSON fields → Load `json-field-schemas.md`
- Understanding entity relationships and cascade behavior → Load `entity-relationship-model.md`
- Adding alternate keys → Load `schema-additions-alternate-keys.md`
