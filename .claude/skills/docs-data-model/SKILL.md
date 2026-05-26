---
description: Draft or update a data model document — entity schemas, relationships, field mappings, and JSON field contracts
tags: [documentation, data-model, dataverse]
techStack: [dataverse]
appliesTo: ["write data model doc", "update data model", "entity relationships", "docs-data-model"]
alwaysApply: false
exemplar: docs/data-model/sprk_matter-related-tables.md
last-reviewed: 2026-05-16
---

# Data Model Document Skill

> **Category**: Documentation
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)

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

- **MUST**: Source field definitions from Dataverse metadata (`pac modelbuilder` output OR live environment via `mcp__dataverse__describe_table`). Hand-derived metadata drifts.
- **MUST**: Verify logical names match what the code uses in FetchXML and WebAPI queries. Mismatches cause silent runtime failures.
- **MUST**: Document ALL alternate keys — these are critical for idempotent operations (upserts depend on them).
- **MUST**: For JSON fields, document the schema contract that parsing services expect. Include the consuming `BFF.Services.*` class.
- **MUST**: Cross-reference the service that owns each entity (which BFF service reads/writes it). Use the `Services/` path in BFF API source.
- **MUST NOT**: Hand-edit the field list without verifying against Dataverse metadata. The model changes; the doc must follow.

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Doc lists a field that no longer exists in Dataverse | Field was removed but doc wasn't updated | Re-verify with `mcp__dataverse__describe_table(<entity>)` before marking the doc current. Add the entity to `doc-drift-audit` watch list if frequently modified. |
| FetchXML queries break because logical name differs from doc | Drift between code and doc — code is source of truth | Search the codebase for the doc's claimed logical name; if it doesn't appear, the doc is wrong. Update doc to match code. |
| JSON field schema doc doesn't match actual parser expectations | Parser was refactored without updating the data-model doc | Find the parser service in `src/server/api/Sprk.Bff.Api/Services/`; copy its expected schema into the doc verbatim. Add a cross-reference to the parser file. |
| Alternate key documentation omits a key that the upsert relies on | Doc was written before the key was added | Query Dataverse for all keys: `mcp__dataverse__describe_table` shows alternate keys. Document every key with its consumer. |
