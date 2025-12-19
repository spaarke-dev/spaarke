# ADR-005: Flat storage model in SharePoint Embedded (SPE)

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

Deep folder hierarchies in document systems produce brittle, path-based permissions and poor UX. SDAP needs matter-centric metadata and many-to-many associations without nesting complexity.

## Decision

| Rule | Description |
|------|-------------|
| **Flat storage** | No folder hierarchies in SPE containers |
| **Metadata hierarchy** | Represent hierarchy via metadata and associations in Dataverse |
| **Single canonical** | One document object with associations to multiple business contexts |
| **App-mediated access** | Permissions via Dataverse UAC; SPE is headless storage |

## Consequences

**Positive:**
- Simpler synchronization, resilient associations, cleaner access decisions
- Easier cross-matter reuse and search

**Negative:**
- Requires robust metadata profile and association management UI

## Alternatives Considered

| Alternative | Rejection Reason |
|-------------|------------------|
| Deep folder trees with inherited permissions | Permission drift and duplication |
| Multiple physical copies per context | Versioning and compliance risks |

## Operationalization

### Data Model

| Entity | Purpose |
|--------|---------|
| `sprk_document` | Global document record (SPE file reference) |
| `sprk_documentassociation` | Link record to business context (matter, project) |
| Search index | Includes association metadata |

### Access Pattern

| Rule | Implementation |
|------|----------------|
| SPE operations | Only via `SpeFileStore` |
| User permissions | Never direct in SPE containers |
| Projection views | Support fast grids and filters |

## Exceptions

If a specific integration demands fixed folder paths, emit a derived "presentation path" while retaining flat storage and metadata source of truth.

## Success Metrics

| Metric | Target |
|--------|--------|
| Permission drift incidents | Reduced |
| Cross-context retrieval | Faster |
| Audit complexity | Simpler |

## Compliance

**Code review checklist:**
- [ ] No folder creation in SPE (except container root)
- [ ] Document associations managed via Dataverse records
- [ ] Permissions evaluated via UAC, not SPE native
- [ ] Search queries include association metadata
