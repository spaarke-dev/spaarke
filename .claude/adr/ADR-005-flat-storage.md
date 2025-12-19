# ADR-005: Flat Storage in SPE (Concise)

> **Status**: Accepted
> **Domain**: Data/Storage
> **Last Updated**: 2025-12-18

---

## Decision

Use **flat storage** in SharePoint Embedded containers. No folder hierarchies. Represent hierarchy via Dataverse metadata and associations.

**Rationale**: Deep folder hierarchies cause brittle permissions, path-based access issues, and poor UX.

---

## Constraints

### ✅ MUST

- **MUST** store documents flat in SPE containers (no folders)
- **MUST** manage associations via Dataverse records
- **MUST** evaluate permissions via UAC (not SPE native)
- **MUST** access SPE only via `SpeFileStore`

### ❌ MUST NOT

- **MUST NOT** create folder hierarchies in SPE
- **MUST NOT** assign user permissions directly in SPE
- **MUST NOT** duplicate documents across contexts (use associations)

---

## Data Model

| Entity | Purpose |
|--------|---------|
| `sprk_document` | Global document record (SPE file reference) |
| `sprk_documentassociation` | Link to business context (matter, project) |

### Access Pattern

```
User Request → BFF → AuthorizationService → SpeFileStore → SPE
                          ↓
              Dataverse UAC (associations, permissions)
```

**See**: [Data Access Pattern](../patterns/data/spefilestore-usage.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-003](ADR-003-authorization-seams.md) | UAC for permissions |
| [ADR-007](ADR-007-spefilestore.md) | SpeFileStore facade |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-005-flat-storage-spe.md](../../docs/adr/ADR-005-flat-storage-spe.md)

---

**Lines**: ~70
