---
description: Draft or update a data model document — entity schemas, relationships, field mappings, and JSON field contracts
tags: [documentation, data-model, dataverse]
techStack: [dataverse]
appliesTo: ["write data model doc", "update data model", "entity relationships", "docs-data-model"]
alwaysApply: false
---

# Data Model Document Skill

> **Category**: Documentation
> **Last Updated**: April 2026

---

## Purpose

Draft or update a data model document in `docs/data-model/`. Data model documents describe **entity schemas, relationships, field definitions, and data contracts** in the Dataverse environment.

**Data model docs are NOT:**
- How to query or modify entities (→ use `/docs-guide` or architecture docs)
- Design decisions about the data model (→ use `/docs-architecture`)
- Dataverse admin procedures (→ use `/docs-guide`)

---

## When to Use

- "document entity {name}", "update data model", "entity relationships"
- When adding a new entity to Dataverse
- When modifying entity relationships or alternate keys
- When documenting JSON field schemas

---

## Document Structure

### For Entity Schema Docs (`sprk_{entity}.md`)

```markdown
# {Entity Display Name} (`sprk_{logicalname}`)

> **Last Updated**: {date}
> **Solution**: {which Dataverse solution contains this entity}

## Purpose
{What this entity represents in the business domain.}

## Fields

| Display Name | Logical Name | Type | Required | Description |
|-------------|-------------|------|----------|-------------|

## Relationships

| Related Entity | Type | Lookup Field | Cascade |
|---------------|------|-------------|---------|

## Alternate Keys

| Key Name | Fields | Used By |
|----------|--------|---------|

## JSON Fields

| Field | Schema Description | Parsing Service |
|-------|-------------------|-----------------|
```

### For Relationship Model Docs

```markdown
# Entity Relationship Model

> **Last Updated**: {date}

## Core Entity Graph
{Show primary entity chains: Matter → Document → Analysis → etc.}

## Relationship Types
{Categorize: parent-child, many-to-many, polymorphic regarding}

## Lookup Chain Reference
{For each common query pattern, show the relationship traversal path}
```

---

## Drafting Rules

- Source field definitions from Dataverse metadata (pac modelbuilder or environment)
- Verify logical names match what the code uses in FetchXML and WebAPI queries
- Document ALL alternate keys — these are critical for idempotent operations
- For JSON fields, document the schema contract that parsing services expect
- Cross-reference the service that owns each entity (which BFF service reads/writes it)
