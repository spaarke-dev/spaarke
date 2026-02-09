# Polymorphic Resolver Pattern

> **Domain**: Dataverse Data Model / PCF
> **ADR**: [ADR-024](../../adr/ADR-024-polymorphic-resolver-pattern.md)
> **Last Validated**: 2026-02-09

---

## When to Use This Pattern

Use the Polymorphic Resolver Pattern when:
- A child entity needs to associate with multiple different parent entity types
- You need both unified views (all children) and entity-specific subgrids
- Native "Regarding" lookup limitations are blocking your requirements
- You want automatic field mapping from parent to child

**Examples**:
- Events can be related to Matters, Projects, Invoices, etc.
- Memos can be attached to Events, Matters, Projects, Documents
- Tasks can be created for any entity type

---

## Schema Setup

### Step 1: Create Record Type Entity (sprk_recordtype_ref)

This entity serves as the lookup target for the resolver pattern:

```xml
<entity name="sprk_recordtype_ref">
  <attributes>
    <attribute name="sprk_name" type="string" length="200" />
    <attribute name="sprk_recordlogicalname" type="string" length="100" />
    <attribute name="sprk_recorddisplayname" type="string" length="200" />
    <attribute name="sprk_regardingfield" type="string" length="100" />
  </attributes>
</entity>
```

**Seed Data**:
```javascript
[
  { name: "Matter", logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter" },
  { name: "Project", logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject" },
  { name: "Invoice", logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice" },
  // ... add all supported parent types
]
```

### Step 2: Add Fields to Child Entity

Add both entity-specific lookups and resolver fields:

```xml
<entity name="sprk_event">
  <!-- Entity-Specific Lookups (one per parent type) -->
  <attribute name="sprk_regardingmatter" type="lookup" target="sprk_matter" />
  <attribute name="sprk_regardingproject" type="lookup" target="sprk_project" />
  <attribute name="sprk_regardinginvoice" type="lookup" target="sprk_invoice" />
  <attribute name="sprk_regardinganalysis" type="lookup" target="sprk_analysis" />
  <attribute name="sprk_regardingaccount" type="lookup" target="account" />
  <attribute name="sprk_regardingcontact" type="lookup" target="contact" />
  <attribute name="sprk_regardingworkassignment" type="lookup" target="sprk_workassignment" />
  <attribute name="sprk_regardingbudget" type="lookup" target="sprk_budget" />

  <!-- Resolver Fields (denormalized for cross-entity views) -->
  <attribute name="sprk_regardingrecordtype" type="lookup" target="sprk_recordtype_ref" />
  <attribute name="sprk_regardingrecordid" type="string" length="50" />
  <attribute name="sprk_regardingrecordname" type="string" length="200" />
  <attribute name="sprk_regardingrecordurl" type="string" subtype="url" length="2000" />
</entity>
```

**Field Descriptions**:
- `sprk_regardingrecordtype`: Which entity type (Matter, Project, etc.)
- `sprk_regardingrecordid`: GUID of the parent record
- `sprk_regardingrecordname`: Display name for grid display
- `sprk_regardingrecordurl`: Clickable URL to parent record

---

## PCF Control Setup

### AssociationResolver on Form

Add the AssociationResolver PCF to the child entity form:

```xml
<control id="AssociationResolver"
         classid="{guid-of-association-resolver}"
         library="$webresource:sprk_Spaarke.Controls.AssociationResolver"
         version="1.0.6">
  <parameters>
    <regardingRecordType>sprk_regardingrecordtype</regardingRecordType>
    <regardingRecordId>sprk_regardingrecordid</regardingRecordId>
    <regardingRecordName>sprk_regardingrecordname</regardingRecordName>
  </parameters>
</control>
```

**What it does**:
1. Shows dropdown of entity types (loaded dynamically from sprk_recordtype_ref)
2. "Select Record" button opens lookup dialog
3. On selection:
   - Populates corresponding entity-specific lookup (e.g., `sprk_regardingmatter`)
   - Clears all other entity-specific lookups
   - Populates all resolver fields
   - Applies field mappings if profile exists
4. Auto-detects if created from subgrid (pre-populated lookup)
5. Shows "Refresh from Parent" button to re-apply field mappings

### RegardingLink in Grid

Add RegardingLink as a grid customizer for unified views:

```xml
<grid name="All Events View">
  <columns>
    <column name="sprk_regardingrecordname" width="200">
      <customcontrol formfactor="3" name="Spaarke.Controls.RegardingLink">
        <!-- Control automatically uses resolver fields to render clickable link -->
      </customcontrol>
    </column>
  </columns>
</grid>
```

**Grid Display**:
```
Event Name           | Regarding
---------------------|------------------------
Call client         | Smith v. Jones (Matter)
Send invoice        | Q1 Budget Report (Project)
Follow up reminder  | ABC Corp (Account)
```

Each "Regarding" value is clickable and opens the parent record.

---

## Code Implementation

### RecordSelectionHandler (TypeScript)

Handles populating/clearing regarding fields:

```typescript
import { ComponentFramework } from "powerapps-component-framework";

export interface IRecordSelection {
    entityType: string;      // e.g., "sprk_matter"
    recordId: string;        // GUID
    recordName: string;      // Display name
}

export interface IRecordSelectionResult {
    success: boolean;
    regardingFieldSet: boolean;
    otherLookupsCleared: number;
    errors: string[];
}

/**
 * Handle record selection from lookup dialog
 * Populates entity-specific lookup + resolver fields
 * Clears all other entity-specific lookups
 */
export async function handleRecordSelection(
    selection: IRecordSelection,
    webAPI: ComponentFramework.WebApi
): Promise<IRecordSelectionResult> {
    const result: IRecordSelectionResult = {
        success: true,
        regardingFieldSet: false,
        otherLookupsCleared: 0,
        errors: []
    };

    try {
        // Step 1: Query sprk_recordtype_ref to get configuration
        const query = `?$filter=sprk_recordlogicalname eq '${selection.entityType}' and statecode eq 0`;
        const recordTypes = await webAPI.retrieveMultipleRecords("sprk_recordtype_ref", query);

        if (!recordTypes.entities || recordTypes.entities.length === 0) {
            result.errors.push(`No Record Type found for ${selection.entityType}`);
            result.success = false;
            return result;
        }

        const recordType = recordTypes.entities[0];
        const regardingField = recordType.sprk_regardingfield as string;
        const recordTypeId = recordType.sprk_recordtype_refid as string;
        const displayName = recordType.sprk_recorddisplayname as string;

        // Step 2: Build record URL
        const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
        const clientUrl = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() || "";
        const recordUrl = `${clientUrl}/main.aspx?etn=${selection.entityType}&id=${selection.recordId}&pagetype=entityrecord`;

        // Step 3: Set resolver fields
        xrm.Page.getAttribute("sprk_regardingrecordtype")?.setValue([{
            id: recordTypeId,
            name: displayName,
            entityType: "sprk_recordtype_ref"
        }]);
        xrm.Page.getAttribute("sprk_regardingrecordid")?.setValue(selection.recordId);
        xrm.Page.getAttribute("sprk_regardingrecordname")?.setValue(selection.recordName);
        xrm.Page.getAttribute("sprk_regardingrecordurl")?.setValue(recordUrl);

        // Step 4: Set entity-specific lookup
        xrm.Page.getAttribute(regardingField)?.setValue([{
            id: selection.recordId,
            name: selection.recordName,
            entityType: selection.entityType
        }]);
        result.regardingFieldSet = true;

        // Step 5: Clear all other entity-specific lookups
        const allRegardingFields = [
            "sprk_regardingmatter",
            "sprk_regardingproject",
            "sprk_regardinginvoice",
            "sprk_regardinganalysis",
            "sprk_regardingaccount",
            "sprk_regardingcontact",
            "sprk_regardingworkassignment",
            "sprk_regardingbudget"
        ];

        for (const field of allRegardingFields) {
            if (field !== regardingField) {
                const attr = xrm.Page.getAttribute(field);
                if (attr && attr.getValue() !== null) {
                    attr.setValue(null);
                    result.otherLookupsCleared++;
                }
            }
        }

        return result;

    } catch (error) {
        console.error("[RecordSelectionHandler] Error:", error);
        result.success = false;
        result.errors.push(error instanceof Error ? error.message : "Unknown error");
        return result;
    }
}

/**
 * Clear all regarding fields
 */
export function clearAllRegardingFields(): void {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;

    // Clear resolver fields
    xrm.Page.getAttribute("sprk_regardingrecordtype")?.setValue(null);
    xrm.Page.getAttribute("sprk_regardingrecordid")?.setValue(null);
    xrm.Page.getAttribute("sprk_regardingrecordname")?.setValue(null);
    xrm.Page.getAttribute("sprk_regardingrecordurl")?.setValue(null);

    // Clear all entity-specific lookups
    const allRegardingFields = [
        "sprk_regardingmatter",
        "sprk_regardingproject",
        "sprk_regardinginvoice",
        "sprk_regardinganalysis",
        "sprk_regardingaccount",
        "sprk_regardingcontact",
        "sprk_regardingworkassignment",
        "sprk_regardingbudget"
    ];

    for (const field of allRegardingFields) {
        xrm.Page.getAttribute(field)?.setValue(null);
    }
}
```

### Auto-Detection Feature

Detects if record is created from a subgrid (lookup pre-populated):

```typescript
export interface IDetectedParentContext {
    entityType: string;        // e.g., "sprk_matter"
    recordId: string;          // GUID
    recordName: string;        // Display name
    entityDisplayName: string; // "Matter", "Project", etc.
    regardingField: string;    // "sprk_regardingmatter"
}

/**
 * Detect if a regarding lookup is pre-populated (from subgrid creation)
 * Returns context if detected, null otherwise
 */
export function detectPrePopulatedParent(): IDetectedParentContext | null {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;

    // Check all entity-specific lookups
    const regardingFields = [
        { field: "sprk_regardingmatter", entity: "sprk_matter", display: "Matter" },
        { field: "sprk_regardingproject", entity: "sprk_project", display: "Project" },
        { field: "sprk_regardinginvoice", entity: "sprk_invoice", display: "Invoice" },
        { field: "sprk_regardinganalysis", entity: "sprk_analysis", display: "Analysis" },
        { field: "sprk_regardingaccount", entity: "account", display: "Account" },
        { field: "sprk_regardingcontact", entity: "contact", display: "Contact" },
        { field: "sprk_regardingworkassignment", entity: "sprk_workassignment", display: "Work Assignment" },
        { field: "sprk_regardingbudget", entity: "sprk_budget", display: "Budget" }
    ];

    for (const config of regardingFields) {
        const attr = xrm.Page.getAttribute(config.field);
        const value = attr?.getValue();

        if (value && value.length > 0) {
            return {
                entityType: config.entity,
                recordId: value[0].id.replace(/[{}]/g, ''),
                recordName: value[0].name,
                entityDisplayName: config.display,
                regardingField: config.field
            };
        }
    }

    return null; // No pre-populated parent found
}
```

---

## Field Mapping Integration

AssociationResolver integrates with the Field Mapping Framework:

```typescript
import { FieldMappingHandler, createFieldMappingHandler } from "./handlers/FieldMappingHandler";

// After record selection, apply field mappings
const fieldMappingHandler = createFieldMappingHandler(context.webAPI);

const mappingResult = await fieldMappingHandler.applyMappingsForSelection(
    "sprk_matter",  // source entity
    matterId,       // source record GUID
    {}              // target record (mutated in place)
);

if (mappingResult.profileFound && mappingResult.fieldsMapped > 0) {
    // Apply to form
    fieldMappingHandler.applyToForm(mappingResult.mappingResult.mappedValues, true);

    console.log(`Auto-populated ${mappingResult.fieldsMapped} fields from Matter`);
}
```

**Result**: Event automatically gets client, location, billing contact, etc. from Matter

---

## View Configuration

### Unified View (All Children)

```xml
<fetch>
  <entity name="sprk_event">
    <attribute name="sprk_eventname" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_priority" />
    <attribute name="sprk_regardingrecordname" />
    <!-- RegardingLink PCF renders sprk_regardingrecordname as clickable -->
  </entity>
</fetch>
```

### Entity-Specific Subgrid

```xml
<!-- Matter Form: Events Subgrid -->
<fetch>
  <entity name="sprk_event">
    <filter>
      <condition attribute="sprk_regardingmatter"
                 operator="eq"
                 value="{current-matter-id}" />
    </filter>
    <attribute name="sprk_eventname" />
    <attribute name="sprk_duedate" />
    <attribute name="sprk_priority" />
  </entity>
</fetch>
```

**Key Difference**: Entity-specific subgrid uses the lookup field for filtering, not resolver fields.

---

## Best Practices

### ✅ DO

- **DO** use AssociationResolver PCF for all user-facing association selection
- **DO** populate all resolver fields when setting entity-specific lookup
- **DO** clear all other entity-specific lookups when changing parent type
- **DO** use RegardingLink PCF for grid display of regarding records
- **DO** add new parent types to sprk_recordtype_ref (no schema changes needed)
- **DO** create Field Mapping Profiles to auto-populate child fields
- **DO** use auto-detection feature for subgrid creation scenarios

### ❌ DON'T

- **DON'T** use native "Regarding" lookup in model-driven apps
- **DON'T** allow multiple entity-specific lookups to be populated simultaneously
- **DON'T** hard-code entity types in choice fields
- **DON'T** skip populating resolver fields (they enable unified views)
- **DON'T** create new resolver fields without adding to RecordSelectionHandler

---

## Related Components

| Component | Purpose | Location |
|-----------|---------|----------|
| AssociationResolver | Selection UI | [src/client/pcf/AssociationResolver/](../../../src/client/pcf/AssociationResolver/) |
| RegardingLink | Grid display | [src/client/pcf/RegardingLink/](../../../src/client/pcf/RegardingLink/) |
| UpdateRelatedButton | Push mappings | [src/client/pcf/UpdateRelatedButton/](../../../src/client/pcf/UpdateRelatedButton/) |
| FieldMappingService | Auto-population | [src/client/shared/Spaarke.UI.Components/services/FieldMappingService.ts](../../../src/client/shared/Spaarke.UI.Components/services/FieldMappingService.ts) |

---

## Common Scenarios

### Scenario 1: Add New Parent Entity Type

```javascript
// 1. Add new record to sprk_recordtype_ref
{
  sprk_name: "Document",
  sprk_recordlogicalname: "sprk_document",
  sprk_recorddisplayname: "Document",
  sprk_regardingfield: "sprk_regardingdocument"
}

// 2. Add new lookup field to child entity
ALTER TABLE sprk_event ADD sprk_regardingdocument LOOKUP(sprk_document)

// 3. Update RecordSelectionHandler.ts (add to regardingFields array)
{ field: "sprk_regardingdocument", entity: "sprk_document", display: "Document" }

// Done! AssociationResolver automatically shows "Document" option
```

### Scenario 2: Child Created from Subgrid

```javascript
// User clicks "New Event" from Matter subgrid
// Dataverse pre-populates: sprk_regardingmatter = {current-matter-id}

// AssociationResolver.componentDidMount():
const detected = detectPrePopulatedParent();
if (detected) {
    // Auto-complete resolver fields
    await completeAutoDetectedAssociation(detected, webAPI);

    // Apply field mappings automatically
    await fieldMappingHandler.applyMappingsForSelection(
        detected.entityType,
        detected.recordId,
        {}
    );

    // Show read-only UI: "Matter: Smith v. Jones"
}
```

### Scenario 3: User Changes Parent Type

```javascript
// User selects "Matter" → "Project"
// RecordSelectionHandler clears sprk_regardingmatter
// Then populates sprk_regardingproject
// All resolver fields updated to new parent

result.otherLookupsCleared = 1 // sprk_regardingmatter cleared
result.regardingFieldSet = true // sprk_regardingproject set
```

---

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| "No Record Type found" | sprk_recordtype_ref missing entry | Add seed data for entity type |
| Multiple lookups populated | Manual field editing | Use AssociationResolver PCF only |
| Resolver fields empty | Direct lookup edit bypassed control | Always use AssociationResolver |
| Grid link not clickable | RegardingLink PCF not configured | Add as grid customizer |
| Subgrid filtering broken | Using resolver field instead of lookup | Use entity-specific lookup for filter |

---

**See Also**:
- [ADR-024](../../adr/ADR-024-polymorphic-resolver-pattern.md) - Architecture decision
- [Field Mapping Pattern](../pcf/field-mapping.md) - Auto-population pattern
- [events-and-workflow-automation-r1](../../../projects/events-and-workflow-automation-r1/) - Original implementation project

---

**Lines**: ~400
