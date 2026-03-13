# ADR-024: Polymorphic Resolver Pattern (Concise)

> **Status**: Accepted
> **Domain**: Dataverse Data Model
> **Last Updated**: 2026-03-12

---

## Decision

Use a **dual-field strategy** for polymorphic associations where a child record can be related to multiple parent entity types. This pattern combines entity-specific lookup fields (for subgrid filtering) with denormalized resolver fields (for unified cross-entity views).

**Rationale**: Dataverse's native "Regarding" lookup has limitations - it cannot be used in views, filters, or Advanced Find. The dual-field approach provides the benefits of both native lookups and polymorphic flexibility.

---

## The Pattern

### Dual-Field Strategy

Each polymorphic entity (e.g., `sprk_event`, `sprk_memo`, `sprk_document`) has:

**1. Entity-Specific Lookup Fields** (for subgrid filtering)
- `sprk_regardingmatter` (Lookup to sprk_matter)
- `sprk_regardingproject` (Lookup to sprk_project)
- `sprk_regardinginvoice` (Lookup to sprk_invoice)
- ... one for each supported parent type

**2. Resolver Fields** (for unified views)
- `sprk_regardingrecordtype` (Lookup to sprk_recordtype_ref) - Identifies which entity type
- `sprk_regardingrecordid` (Single Line Text, 50 chars) - GUID of the parent record
- `sprk_regardingrecordname` (Single Line Text, 200 chars) - Display name of parent
- `sprk_regardingrecordurl` (URL) - Clickable link to parent record

### Visual Representation

```
┌────────────────────────────────┐
│ sprk_event                     │
├────────────────────────────────┤
│ Entity-Specific Lookups:       │
│ ├─ sprk_regardingmatter        │ ← For Matter subgrid filtering
│ ├─ sprk_regardingproject       │ ← For Project subgrid filtering
│ ├─ sprk_regardinginvoice        │ ← For Invoice subgrid filtering
│ └─ ... (8 total)               │
│                                 │
│ Resolver Fields (Denormalized):│
│ ├─ sprk_regardingrecordtype    │ ← Lookup to sprk_recordtype_ref
│ ├─ sprk_regardingrecordid      │ ← "abc-123-def..."
│ ├─ sprk_regardingrecordname    │ ← "Smith v. Jones"
│ └─ sprk_regardingrecordurl     │ ← "/main.aspx?..."
└────────────────────────────────┘
```

### Record Type Entity (`sprk_recordtype_ref`)

The Record Type entity serves as the lookup target for resolver fields:

| Field | Purpose |
|-------|---------|
| `sprk_recordlogicalname` | Entity logical name (e.g., "sprk_matter") |
| `sprk_recorddisplayname` | Entity display name (e.g., "Matter") |
| `sprk_regardingfield` | Name of the corresponding lookup field (e.g., "sprk_regardingmatter") |

**Why a lookup instead of choice?**
- Admin-configurable (add new entity types without schema changes)
- Supports metadata for field mapping framework
- Enables dynamic entity picker in AssociationResolver PCF

---

## Constraints

### ✅ MUST

- **MUST** include both entity-specific lookups AND resolver fields for polymorphic entities
- **MUST** populate only ONE entity-specific lookup at a time (mutually exclusive)
- **MUST** populate ALL 4 resolver fields when an association is made
- **MUST** clear the previous lookup when changing parent entity type
- **MUST** use the shared `PolymorphicResolverService` for all client-side programmatic record creation
- **MUST** use `IncomingAssociationResolver` (or equivalent) for server-side resolver field population
- **MUST** reference `sprk_recordtype_ref` (not hard-coded choice fields)
- **MUST** pass `parentRecordName` when calling `EntityCreationService.createDocumentRecords()`

### ❌ MUST NOT

- **MUST NOT** use native "Regarding" lookup in model-driven apps (use resolver pattern instead)
- **MUST NOT** allow multiple entity-specific lookups to be populated simultaneously
- **MUST NOT** hard-code entity types in choice fields (use sprk_recordtype_ref lookup)
- **MUST NOT** skip resolver fields (they enable cross-entity views)
- **MUST NOT** duplicate resolver logic in individual services — use the shared `PolymorphicResolverService`

---

## Implementation Components

### Active (Shared Services)

| Component | Purpose | Location |
|-----------|---------|----------|
| **PolymorphicResolverService** | Client-side shared helpers: `resolveRecordType`, `buildRecordUrl`, `findNavProp`, `applyResolverFields` | `src/client/shared/.../services/PolymorphicResolverService.ts` |
| **EntityCreationService** | Document creation with automatic resolver field population | `src/client/shared/.../services/EntityCreationService.ts` |
| **IncomingAssociationResolver** | Server-side resolver for Communication entity (BFF API) | `src/server/api/.../Services/Communication/IncomingAssociationResolver.cs` |
| **IDataverseService** | `QueryRecordTypeRefAsync()` for server-side `sprk_recordtype_ref` lookups | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` |
| **FieldMappingService** | Applies parent field values to child records | `src/client/shared/.../services/FieldMappingService.ts` |

### Archival (PCF Controls — deployed but not actively used in wizard/service code paths)

| Component | Purpose | Location |
|-----------|---------|----------|
| AssociationResolver PCF | Form-based selection UI via Xrm.Page | `src/client/pcf/AssociationResolver/` |
| RegardingLink PCF | Grid customizer for clickable regarding links | `src/client/pcf/RegardingLink/` |
| UpdateRelatedButton PCF | Parent form button to push field mappings | `src/client/pcf/UpdateRelatedButton/` |

---

## Usage Patterns

### Client-Side: Wizard/Service Creating Child Records

```typescript
import { applyResolverFields, findNavProp, type INavPropEntry } from '@spaarke/ui-components';

// 1. Discover nav-props (one-time, cache in service constructor)
const navProps = await this._discoverNavProps('sprk_workassignment');

// 2. Build entity payload, then apply resolver fields
await applyResolverFields(webApi, entity, navProps,
  'sprk_matter', 'sprk_matters', matterId, 'Smith v. Jones', 'matter');

// 3. Create record — entity now has entity-specific bind + all 4 resolver fields
await webApi.createRecord('sprk_workassignment', entity);
```

### Client-Side: Document Creation (EntityCreationService)

```typescript
// EntityCreationService handles resolver fields internally
await entityService.createDocumentRecords(
  'sprk_matters', matterId, 'sprk_Matter', uploadedFiles,
  { containerId, parentRecordName: 'Smith v. Jones' }
);
```

### Server-Side: BFF API (Communication)

```csharp
// IncomingAssociationResolver.PopulateResolverFieldsAsync()
// Determines primary entity from matched lookups, sets all 4 fields
await PopulateResolverFieldsAsync(communication, resolvedFields, ct);
```

### In Views

- **Unified views**: Use `sprk_regardingrecordname` for cross-entity display
- **Entity-specific subgrids**: Filter on entity-specific lookup (e.g., `sprk_regardingmatter`)

---

## Field Mapping Integration

The resolver pattern integrates with the Field Mapping Framework. When a parent association is established, field mappings can auto-populate child fields (e.g., copy `sprk_client` from Matter to Event).

**See**: `.claude/patterns/dataverse/polymorphic-resolver.md` for implementation details

---

## Benefits

| Benefit | How Achieved |
|---------|--------------|
| **Native subgrid filtering** | Entity-specific lookup fields work with Dataverse views |
| **Unified cross-entity views** | Resolver fields enable "All Events" view across all parent types |
| **Admin configurability** | `sprk_recordtype_ref` allows adding entity types without schema changes |
| **User-friendly picker** | AssociationResolver PCF provides single UI for all entity types |
| **Clickable links in grids** | RegardingLink PCF renders resolver name as navigation link |
| **Automatic field mapping** | Integration with Field Mapping Framework auto-populates child fields |

---

## Examples in Spaarke

| Entity | Polymorphic Association | Resolver Service |
|--------|------------------------|-----------------|
| `sprk_event` | Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget | Client: `PolymorphicResolverService` via wizard services |
| `sprk_document` | Matter, Project, Invoice, Work Assignment | Client: `EntityCreationService.createDocumentRecords()` |
| `sprk_workassignment` | Matter, Project, Invoice, Event | Client: `PolymorphicResolverService` via `WorkAssignmentService` |
| `sprk_communication` | Matter, Project, Invoice, Work Assignment, Budget, Analysis, Organization, Person | Server: `IncomingAssociationResolver` (BFF API) |
| `sprk_memo` | Matter, Project, Event, Document | Client: `PolymorphicResolverService` |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | AssociationResolver and RegardingLink are PCF controls |
| [ADR-021](ADR-021-fluent-design-system.md) | PCF controls use Fluent UI v9 |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF controls use React 16 APIs |

---

## Source Documentation

**Implementation Pattern**: [.claude/patterns/dataverse/polymorphic-resolver.md](../patterns/dataverse/polymorphic-resolver.md) — Full implementation guide with code examples

**Original Project**: events-and-workflow-automation-r1 (schema + PCF controls)
**Shared Service Refactor**: sdap-file-upload-document-r2 (PolymorphicResolverService, EntityCreationService, IncomingAssociationResolver)

---

**Lines**: ~200
