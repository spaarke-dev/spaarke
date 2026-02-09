# ADR-024: Polymorphic Resolver Pattern (Concise)

> **Status**: Accepted
> **Domain**: Dataverse Data Model
> **Last Updated**: 2026-02-09

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
- **MUST** populate ALL resolver fields when an association is made
- **MUST** clear the previous lookup when changing parent entity type
- **MUST** use AssociationResolver PCF for user-facing association selection
- **MUST** use RegardingLink PCF for grid views that display regarding records
- **MUST** reference `sprk_recordtype_ref` (not hard-coded choice fields)

### ❌ MUST NOT

- **MUST NOT** use native "Regarding" lookup in model-driven apps (use resolver pattern instead)
- **MUST NOT** allow multiple entity-specific lookups to be populated simultaneously
- **MUST NOT** hard-code entity types in choice fields (use sprk_recordtype_ref lookup)
- **MUST NOT** skip resolver fields (they enable cross-entity views)

---

## Implementation Components

| Component | Purpose | Location |
|-----------|---------|----------|
| **AssociationResolver PCF** | User interface for selecting parent record | `src/client/pcf/AssociationResolver/` |
| **RegardingLink PCF** | Grid customizer that renders regarding name as clickable link | `src/client/pcf/RegardingLink/` |
| **UpdateRelatedButton PCF** | Parent form button to push field mappings to children | `src/client/pcf/UpdateRelatedButton/` |
| **FieldMappingService** | Applies parent field values to child records | `src/client/shared/Spaarke.UI.Components/services/FieldMappingService.ts` |
| **RecordSelectionHandler** | Logic to populate/clear regarding fields | `src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts` |

---

## Usage Patterns

### On Child Entity Form

```xml
<!-- Event Form: Add AssociationResolver control -->
<control id="AssociationResolver"
         classid="{...AssociationResolver}"
         bound to="sprk_regardingrecordtype"
/>
```

**User Experience**:
1. User selects entity type from dropdown (Matter, Project, Invoice, etc.)
2. User clicks "Select Record" - opens Xrm.Utility.lookupObjects dialog
3. User selects parent record
4. **Control automatically**:
   - Populates the corresponding entity-specific lookup (e.g., `sprk_regardingmatter`)
   - Clears all other entity-specific lookups
   - Populates all resolver fields (type, id, name, url)
   - **Applies field mappings** if a profile exists (e.g., copy client from Matter to Event)

### In Unified View

```xml
<!-- All Events View: Show Regarding column -->
<fetch>
  <entity name="sprk_event">
    <attribute name="sprk_regardingrecordname" />
    <!-- Use RegardingLink PCF as grid customizer -->
  </entity>
</fetch>
```

**Grid Display**: "Smith v. Jones" as clickable link (opens parent record)

### In Entity-Specific Subgrid

```xml
<!-- Matter Form: Events Subgrid -->
<fetch>
  <entity name="sprk_event">
    <filter>
      <condition attribute="sprk_regardingmatter"
                 operator="eq"
                 value="{current-matter-id}" />
    </filter>
  </entity>
</fetch>
```

**Filtering Works**: Native Dataverse lookup filtering using entity-specific field

---

## Field Mapping Integration

The resolver pattern integrates with the Field Mapping Framework:

```typescript
// When user selects a Matter as parent of an Event
// 1. AssociationResolver populates regarding fields
// 2. FieldMappingHandler checks for active profile (Matter -> Event)
// 3. If profile exists, applies field mappings automatically
//    Example: Copy sprk_client from Matter to Event

const result = await fieldMappingHandler.applyMappingsForSelection(
    "sprk_matter",  // source entity
    matterId,       // source record GUID
    targetRecord    // Event fields to populate
);

// Result: Event automatically gets client, location, etc. from Matter
```

**See**: `.claude/patterns/dataverse/polymorphic-resolver.md` for implementation details

---

## Auto-Detection Feature

AssociationResolver supports **automatic parent detection** when child record is created from a subgrid:

```javascript
// When Event is created from Matter subgrid
// Dataverse pre-populates: sprk_regardingmatter = {matter-id}
// AssociationResolver detects this and:
// 1. Auto-completes resolver fields
// 2. Shows read-only association display
// 3. Applies field mappings automatically
```

**User sees**: "Matter: Smith v. Jones" (read-only, with "Refresh from Parent" button)

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

| Entity | Polymorphic Association | Resolver Fields |
|--------|------------------------|-----------------|
| `sprk_event` | Can relate to Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget | sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname, sprk_regardingrecordurl |
| `sprk_memo` | Can relate to Matter, Project, Event, Document | sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname, sprk_regardingrecordurl |
| `sprk_document` | Can relate to Matter, Project, Invoice | sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname, sprk_regardingrecordurl |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | AssociationResolver and RegardingLink are PCF controls |
| [ADR-021](ADR-021-fluent-design-system.md) | PCF controls use Fluent UI v9 |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF controls use React 16 APIs |

---

## Source Documentation

**Full ADR**: This is the complete ADR. See pattern file for implementation details.

**Implementation Pattern**: [.claude/patterns/dataverse/polymorphic-resolver.md](../patterns/dataverse/polymorphic-resolver.md)

**Project Source**: events-and-workflow-automation-r1

---

**Lines**: ~200
