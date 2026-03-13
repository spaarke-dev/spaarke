# Polymorphic Resolver Pattern

> **Domain**: Dataverse Data Model
> **ADR**: [ADR-024](../../adr/ADR-024-polymorphic-resolver-pattern.md)
> **Last Updated**: 2026-03-12

---

## When to Use This Pattern

Use the Polymorphic Resolver Pattern when:
- A child entity needs to associate with multiple different parent entity types
- You need both unified views (all children) and entity-specific subgrids
- Native "Regarding" lookup limitations are blocking your requirements

**Entities using this pattern**: `sprk_event`, `sprk_document`, `sprk_workassignment`, `sprk_communication`, `sprk_memo`

---

## Schema

### Dual-Field Strategy

Each polymorphic child entity has two field groups:

**1. Entity-Specific Lookup Fields** (one per supported parent type)
```
sprk_regardingmatter        → Lookup to sprk_matter
sprk_regardingproject       → Lookup to sprk_project
sprk_regardinginvoice       → Lookup to sprk_invoice
sprk_regardingorganization  → Lookup to sprk_organization (or account)
sprk_regardingperson        → Lookup to contact
sprk_regardingworkassignment → Lookup to sprk_workassignment
sprk_regardingbudget        → Lookup to sprk_budget
sprk_regardinganalysis      → Lookup to sprk_analysis
```

**2. Resolver Fields** (denormalized for cross-entity views)

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref` | Which entity type (Matter, Project, etc.) |
| `sprk_regardingrecordid` | Text (50 chars) | GUID of the parent record |
| `sprk_regardingrecordname` | Text (200 chars) | Display name of parent |
| `sprk_regardingrecordurl` | URL (2000 chars) | Clickable link to parent record |

### Record Type Reference Entity (`sprk_recordtype_ref`)

Seed data table mapping entity logical names to display names:

| Field | Example Value |
|-------|--------------|
| `sprk_recordlogicalname` | `sprk_matter` |
| `sprk_recorddisplayname` | `Matter` |
| `sprk_regardingfield` | `sprk_regardingmatter` |

**Why a lookup instead of a choice?** Admin-configurable — add new entity types without schema changes.

---

## Implementation: Shared Client Service

### PolymorphicResolverService

**Location**: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`
**Exports**: Via `src/client/shared/Spaarke.UI.Components/src/services/index.ts`

This is the **single source of truth** for client-side resolver logic. All wizard dialogs and entity creation services use this.

```typescript
import {
  applyResolverFields,
  resolveRecordType,
  buildRecordUrl,
  findNavProp,
  type IPolymorphicWebApi,
  type INavPropEntry,
  type IRecordTypeRef,
  type IResolverFieldValues,
} from '@spaarke/ui-components';
```

### Key Functions

| Function | Purpose | Caches? |
|----------|---------|---------|
| `resolveRecordType(webApi, entityLogicalName)` | Query `sprk_recordtype_ref` for record type GUID + name | Yes (page lifetime) |
| `buildRecordUrl(entityLogicalName, recordId)` | Build Dataverse URL for `sprk_regardingrecordurl`. Resolves `clientUrl` and `appId` from Xrm context; falls back to relative URL | No |
| `findNavProp(entries, referencedEntity, columnHint?)` | Find navigation property name from metadata discovery results | No |
| `applyResolverFields(webApi, entity, navProps, ...)` | **High-level**: sets entity-specific lookup + all 4 resolver fields on an entity payload | Uses resolveRecordType cache |

### Usage Pattern: Full Wizard Service

When a service (e.g., WorkAssignmentService, EventService) creates a child record with a parent association:

```typescript
// Step 1: Discover nav-props for the child entity (one-time, cached per service)
const metaQuery =
  `EntityDefinitions(LogicalName='sprk_workassignment')/ManyToOneRelationships` +
  `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;
const metaResult = await webApi.retrieveMultipleRecords('', metaQuery);
const navProps: INavPropEntry[] = metaResult.entities.map((r) => ({
  columnName: r['ReferencingAttribute'] as string,
  navPropName: r['ReferencingEntityNavigationPropertyName'] as string,
  referencedEntity: r['ReferencedEntity'] as string,
}));

// Step 2: Build entity payload with business fields
const entity: Record<string, unknown> = {
  sprk_name: 'Review contract for Smith case',
  sprk_priority: 100000001, // Normal
};

// Step 3: Apply all resolver fields in one call
await applyResolverFields(
  webApi,
  entity,           // mutated in place
  navProps,         // from Step 1
  'sprk_matter',    // parentEntityLogicalName
  'sprk_matters',   // parentEntitySet
  matterId,         // parentRecordId (GUID)
  'Smith v. Jones', // parentRecordName
  'matter'          // entityLookupHint (for findNavProp column matching)
);

// Step 4: Create the record
const result = await webApi.createRecord('sprk_workassignment', entity);
```

**What `applyResolverFields` sets on the entity object:**
```javascript
{
  // Entity-specific lookup bind (discovered via navProps + hint)
  "sprk_RegardingMatter@odata.bind": "/sprk_matters(abc-123-def)",

  // 4 resolver fields (denormalized)
  "sprk_regardingrecordid": "abc-123-def",
  "sprk_regardingrecordname": "Smith v. Jones",
  "sprk_regardingrecordurl": "https://org.crm.dynamics.com/main.aspx?etn=sprk_matter&id=abc-123-def",
  "sprk_RegardingRecordType@odata.bind": "/sprk_recordtype_refs(type-guid)"
}
```

### Usage Pattern: EntityCreationService (Documents)

`EntityCreationService.createDocumentRecords()` automatically populates all 4 resolver fields. Callers pass `parentRecordName` in options:

```typescript
await entityService.createDocumentRecords(
  'sprk_matters',           // parentEntityName (entity set)
  matterId,                 // parentEntityId
  'sprk_Matter',            // navigationProperty (entity-specific)
  uploadResult.uploadedFiles,
  {
    containerId: speContainerId,
    parentRecordName: 'Smith v. Jones',
    parentEntityLogicalName: 'sprk_matter', // optional — derived from entity set if omitted
  }
);
```

**Internally**: resolves `sprk_recordtype_ref`, discovers nav-prop for `sprk_document → sprk_recordtype_ref`, and sets all 4 resolver fields on each document record in the batch.

---

## Implementation: Server-Side (BFF API)

### IncomingAssociationResolver

**Location**: `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingAssociationResolver.cs`

Populates resolver fields server-side when the BFF creates/updates `sprk_communication` records from incoming emails.

**Key methods:**

| Method | Purpose |
|--------|---------|
| `PopulateResolverFieldsAsync()` | Determines primary regarding entity from matched lookups, sets all 4 resolver fields |
| `ResolveRecordTypeRefAsync()` | Queries + caches `sprk_recordtype_ref` by entity logical name |
| `BuildRecordUrl()` | Server-side URL builder (relative URL, no Xrm context available) |

**Primary entity priority** (when multiple lookups are set, highest-priority entity becomes the "regarding"):

```
sprk_matter > sprk_project > sprk_invoice > sprk_workassignment >
sprk_budget > sprk_analysis > sprk_organization > sprk_person
```

**IDataverseService dependency**: Uses `QueryRecordTypeRefAsync(entityLogicalName)` added to the shared `IDataverseService` interface in `Spaarke.Dataverse`.

---

## All Consumers

| Service | Entity Created | Layer | File |
|---------|---------------|-------|------|
| **WorkAssignmentService** | `sprk_workassignment` | Client | `src/solutions/LegalWorkspace/.../CreateWorkAssignment/workAssignmentService.ts` |
| **EntityCreationService** | `sprk_document` | Client (shared) | `src/client/shared/.../services/EntityCreationService.ts` |
| **MatterService** | documents via EntityCreationService | Client | `src/solutions/LegalWorkspace/.../CreateMatter/matterService.ts` |
| **ProjectWizardDialog** | documents via EntityCreationService | Client | `src/solutions/LegalWorkspace/.../CreateProject/ProjectWizardDialog.tsx` |
| **EventService** | `sprk_event` | Client | `src/solutions/LegalWorkspace/.../CreateEvent/eventService.ts` |
| **IncomingAssociationResolver** | `sprk_communication` | Server (BFF) | `src/server/api/.../Services/Communication/IncomingAssociationResolver.cs` |

---

## Adding a New Entity to the Pattern

### Step 1: Schema (Dataverse)

```
1. Add entity-specific lookup fields to the child entity:
   sprk_regarding{newentity} → Lookup to sprk_{newentity}

2. Add the 4 resolver fields (if child entity is new):
   sprk_regardingrecordtype  → Lookup to sprk_recordtype_ref
   sprk_regardingrecordid    → Text (50)
   sprk_regardingrecordname  → Text (200)
   sprk_regardingrecordurl   → URL (2000)

3. Add seed data to sprk_recordtype_ref:
   { sprk_recordlogicalname: "sprk_{newentity}",
     sprk_recorddisplayname: "{Display Name}",
     sprk_regardingfield: "sprk_regarding{newentity}" }
```

### Step 2: Client-Side (Wizard/Service)

```typescript
// In your service's createRecord method:
import { applyResolverFields, findNavProp, type INavPropEntry } from '@spaarke/ui-components';

// Discover nav-props for the child entity (do once, cache result)
const navProps = await this._discoverNavProps('sprk_{childentity}');

// Apply resolver fields to the entity payload
await applyResolverFields(
  this._webApi, entity, navProps,
  'sprk_{newentity}', 'sprk_{newentity}s',
  parentId, parentName, '{newentity}'
);
```

### Step 3: Server-Side (if applicable)

Add the entity to the `RegardingFieldPriority` array in `IncomingAssociationResolver.cs` and add the `GetPrimaryNameField()` mapping.

---

## View Configuration

### Unified View (All Children)

```xml
<fetch>
  <entity name="sprk_event">
    <attribute name="sprk_eventname" />
    <attribute name="sprk_regardingrecordname" />
    <!-- sprk_regardingrecordname shows "Smith v. Jones" -->
    <!-- sprk_regardingrecordurl provides clickable navigation -->
  </entity>
</fetch>
```

### Entity-Specific Subgrid

```xml
<!-- Matter Form: Events Subgrid — uses entity-specific lookup for filtering -->
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

**Key**: Subgrids filter on entity-specific lookup fields. Unified views use resolver fields.

---

## Best Practices

### MUST

- **MUST** populate ALL 4 resolver fields when setting an entity-specific lookup
- **MUST** populate only ONE entity-specific lookup at a time (mutually exclusive)
- **MUST** use the shared `PolymorphicResolverService` for all programmatic record creation
- **MUST** pass `parentRecordName` when calling `EntityCreationService.createDocumentRecords()`
- **MUST** add new parent types to `sprk_recordtype_ref` seed data (no schema changes needed)

### MUST NOT

- **MUST NOT** skip resolver fields — they enable cross-entity views and grids
- **MUST NOT** allow multiple entity-specific lookups to be populated simultaneously
- **MUST NOT** hard-code entity types in choice fields (use `sprk_recordtype_ref` lookup)
- **MUST NOT** duplicate resolver logic in individual services — use the shared service

---

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| "No Record Type found" | `sprk_recordtype_ref` missing seed data for entity | Add record to `sprk_recordtype_ref` |
| Resolver fields empty on document | `parentRecordName` not passed in options | Pass `parentRecordName` to `createDocumentRecords()` |
| Nav-prop not found | Metadata query returned no match for entity | Check `_discoverNavProps()` — verify relationship exists in Dataverse |
| Multiple lookups populated | Manual field editing on form | Only one entity-specific lookup should be set at a time |
| Subgrid filtering broken | Using resolver field instead of entity-specific lookup | Use `sprk_regarding{entity}` for subgrid filter conditions |

---

## Related Components

### Active Components

| Component | Purpose | Location |
|-----------|---------|----------|
| **PolymorphicResolverService** | Shared client-side resolver helpers | `src/client/shared/.../services/PolymorphicResolverService.ts` |
| **EntityCreationService** | Document creation with automatic resolver fields | `src/client/shared/.../services/EntityCreationService.ts` |
| **IncomingAssociationResolver** | Server-side resolver for Communication (BFF) | `src/server/api/.../Services/Communication/IncomingAssociationResolver.cs` |
| **IDataverseService** | `QueryRecordTypeRefAsync()` for server-side lookups | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` |
| **FieldMappingService** | Auto-population of child fields from parent | `src/client/shared/.../services/FieldMappingService.ts` |

### Archival (PCF controls — deployed in solution but not used by wizard/service code paths)

| Component | Status | Location |
|-----------|--------|----------|
| AssociationResolver PCF | Deployed, not actively used | `src/client/pcf/AssociationResolver/` |
| RegardingLink PCF | Deployed, not actively used | `src/client/pcf/RegardingLink/` |
| UpdateRelatedButton PCF | Deployed, not actively used | `src/client/pcf/UpdateRelatedButton/` |

---

**See Also**:
- [ADR-024](../../adr/ADR-024-polymorphic-resolver-pattern.md) - Architecture decision and constraints
- [Field Mapping Pattern](../pcf/field-mapping.md) - Auto-population pattern
