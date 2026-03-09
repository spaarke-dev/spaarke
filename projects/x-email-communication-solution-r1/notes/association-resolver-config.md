# AssociationResolver PCF Configuration Guide

**Entity**: sprk_communication (Communication)
**Control**: AssociationResolver PCF (v1.0.6)
**Pattern**: Polymorphic Resolver (ADR-024)
**Status**: Configuration Documentation
**Last Updated**: February 21, 2026

---

## Executive Summary

The AssociationResolver PCF control enables users to associate Communication records with multiple entity types through a polymorphic association pattern. Users select an entity type (Matter, Project, Invoice, etc.), search for a specific record of that type, and the control automatically populates both the entity-specific lookup field (e.g., `sprk_regardingmatter`) and denormalized resolver fields.

**Key Outcome**: When a user selects "Matter" and chooses "Smith v. Jones", the control:
- Populates `sprk_regardingrecordtype` with the Matter record type reference
- Populates `sprk_regardingmatter` with the Matter record GUID
- Clears all other entity-specific lookups (`sprk_regardingproject`, `sprk_regardinginvoice`, etc.)
- Auto-populates `sprk_regardingrecordid`, `sprk_regardingrecordname`, and `sprk_regardingrecordurl`
- (Task 022+) Triggers Field Mapping Service to auto-populate communication fields from the parent record

---

## Part 1: PCF Control Binding

### Bound Properties

The AssociationResolver control is bound to the following field on the sprk_communication form:

| Field Logical Name | Field Display Name | Control Property Name | Type | Required | Usage |
|-------------------|-------------------|----------------------|------|----------|-------|
| `sprk_regardingrecordtype` | Regarding Record Type | `regardingRecordType` | Lookup to `sprk_recordtype_ref` | Yes (Bound) | Primary binding - user selects entity type |

### Output Properties

The control writes values to these fields after record selection:

| Field Logical Name | Field Display Name | Control Property Name | Type | Required | Auto-Populated By |
|-------------------|-------------------|----------------------|------|----------|-------------------|
| `sprk_regardingrecordid` | Regarding Record ID | `regardingRecordId` | Text (max 100) | No | Control on selection |
| `sprk_regardingrecordname` | Regarding Record Name | `regardingRecordName` | Text (max 100) | No | Control on selection |

### Input Properties

The control accepts optional configuration via:

| Property Name | Type | Default | Purpose | Configured Via |
|---------------|------|---------|---------|-----------------|
| `apiBaseUrl` | Text | `https://spe-api-dev-67e2xz.azurewebsites.net/api` | BFF API endpoint for field mapping service | Form property editor or hardcoded in control |

---

## Part 2: Entity Type Configuration

### 8 Supported Entity Types

The AssociationResolver dynamically loads all supported entity types from the `sprk_recordtype_ref` entity. All 8 types must have seed data for the control to function correctly.

#### Entity Type Reference Table

| Entity Display Name | Entity Logical Name | Regarding Lookup Field | Regarding Lookup Target | Record Type Entity | Status |
|-------------------|-------------------|----------------------|------------------------|-------------------|--------|
| **Matter** | `sprk_matter` | `sprk_regardingmatter` | `sprk_matter` | sprk_recordtype_ref (Matter) | Active |
| **Project** | `sprk_project` | `sprk_regardingproject` | `sprk_project` | sprk_recordtype_ref (Project) | Active |
| **Invoice** | `sprk_invoice` | `sprk_regardinginvoice` | `sprk_invoice` | sprk_recordtype_ref (Invoice) | Active |
| **Analysis** | `sprk_analysis` | `sprk_regardinganalysis` | `sprk_analysis` | sprk_recordtype_ref (Analysis) | Active |
| **Organization** | `sprk_organization` | `sprk_regardingorganization` | `sprk_organization` | sprk_recordtype_ref (Organization) | Active |
| **Person (Contact)** | `contact` | `sprk_regardingperson` | `contact` | sprk_recordtype_ref (Contact) | Active |
| **Work Assignment** | `sprk_workassignment` | `sprk_regardingworkassignment` | `sprk_workassignment` | sprk_recordtype_ref (Work Assignment) | Active |
| **Budget** | `sprk_budget` | `sprk_regardingbudget` | `sprk_budget` | sprk_recordtype_ref (Budget) | Active |

**Important**: Use `sprk_regardingorganization` (not `account`) and `sprk_regardingperson` (not `contact`) to comply with Spaarke schema naming conventions (ADR-035).

---

## Part 3: Form Field Configuration

### Regarding Lookup Fields on sprk_communication

These fields are pre-configured in the Dataverse schema and do NOT require additional setup. The AssociationResolver PCF will populate them automatically.

#### Field Details

```
Entity: sprk_communication (Communication)
Schema Version: 1.0.0+
```

**Entity-Specific Lookup Fields** (One per supported parent type):

```xml
<!-- Matter Association -->
<attribute name="sprk_regardingmatter"
           display-name="Regarding Matter"
           type="Lookup"
           target="sprk_matter" />

<!-- Project Association -->
<attribute name="sprk_regardingproject"
           display-name="Regarding Project"
           type="Lookup"
           target="sprk_project" />

<!-- Invoice Association -->
<attribute name="sprk_regardinginvoice"
           display-name="Regarding Invoice"
           type="Lookup"
           target="sprk_invoice" />

<!-- Analysis Association -->
<attribute name="sprk_regardinganalysis"
           display-name="Regarding Analysis"
           type="Lookup"
           target="sprk_analysis" />

<!-- Organization Association -->
<attribute name="sprk_regardingorganization"
           display-name="Regarding Organization"
           type="Lookup"
           target="sprk_organization" />

<!-- Person (Contact) Association -->
<attribute name="sprk_regardingperson"
           display-name="Regarding Person"
           type="Lookup"
           target="contact" />

<!-- Work Assignment Association -->
<attribute name="sprk_regardingworkassignment"
           display-name="Regarding Work Assignment"
           type="Lookup"
           target="sprk_workassignment" />

<!-- Budget Association -->
<attribute name="sprk_regardingbudget"
           display-name="Regarding Budget"
           type="Lookup"
           target="sprk_budget" />
```

**Denormalized Resolver Fields** (Shared across all entity types):

```xml
<!-- Record Type Lookup (Primary binding) -->
<attribute name="sprk_regardingrecordtype"
           display-name="Regarding Record Type"
           type="Lookup"
           target="sprk_recordtype_ref"
           required="true" />

<!-- Record GUID (Populated by control) -->
<attribute name="sprk_regardingrecordid"
           display-name="Regarding Record ID"
           type="SingleLine.Text"
           max-length="100" />

<!-- Record Display Name (Populated by control) -->
<attribute name="sprk_regardingrecordname"
           display-name="Regarding Record Name"
           type="SingleLine.Text"
           max-length="100" />

<!-- Record URL (Optional - populated by control) -->
<attribute name="sprk_regardingrecordurl"
           display-name="Regarding Record URL"
           type="SingleLine.Text"
           subtype="url"
           max-length="200" />
```

### Form Section Configuration

The AssociationResolver PCF should be placed in a dedicated section on the Communication form for organization:

```
Communication Form
├── Section: Association
│   ├── AssociationResolver PCF
│   │   └── Bound to: sprk_regardingrecordtype
│   ├── Field: sprk_regardingrecordtype (hidden or read-only - control manages it)
│   └── Field: sprk_regardingrecordid (hidden - for record tracking)
└── Section: Details
    ├── Name, Subject, Body, etc.
    └── Type, Direction, Status
```

**Form Layout Best Practices**:
- Place AssociationResolver PCF in a dedicated "Association" section
- Hide `sprk_regardingrecordtype` from direct user editing (control manages it)
- Hide `sprk_regardingrecordid` and other resolver fields (populated by control)
- Display entity-specific lookups in a read-only subgrid or secondary form section
- Use grid customizers to display associated records (Task 025: RegardingLink PCF)

---

## Part 4: PCF Control Properties

### ControlManifest.Input.xml Definition

```xml
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="AssociationResolver"
           version="1.0.6"
           display-name-key="Association Resolver"
           description-key="Allows users to select a parent entity type and record, then auto-populates fields via field mapping"
           control-type="standard">

    <!-- Required: Bound lookup to Record Type entity -->
    <property name="regardingRecordType"
              display-name-key="Regarding Record Type"
              description-key="Lookup to Record Type entity for the selected parent entity type"
              of-type="Lookup.Simple"
              usage="bound"
              required="true" />

    <!-- Output: Record GUID after selection -->
    <property name="regardingRecordId"
              display-name-key="Regarding Record ID"
              description-key="GUID of the selected regarding record"
              of-type="SingleLine.Text"
              usage="bound"
              required="false" />

    <!-- Output: Record display name -->
    <property name="regardingRecordName"
              display-name-key="Regarding Record Name"
              description-key="Display name of the selected regarding record"
              of-type="SingleLine.Text"
              usage="bound"
              required="false" />

    <!-- Input: API endpoint for field mapping -->
    <property name="apiBaseUrl"
              display-name-key="API Base URL"
              description-key="Base URL for BFF API field mapping endpoints"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="https://spe-api-dev-67e2xz.azurewebsites.net/api" />

    <resources>
      <code path="index.ts" order="1" />
      <css path="styles.css" order="1" />
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### TypeScript Interface Definition

```typescript
interface AssociationResolverAppProps {
    context: ComponentFramework.Context<IInputs>;
    regardingRecordType: RecordTypeReference | null;  // Current value of sprk_regardingrecordtype
    apiBaseUrl: string;                               // BFF API endpoint
    onRecordSelected: (recordId: string, recordName: string) => void;  // Callback on selection
    version: string;                                  // Control version for footer
}

interface RecordTypeReference {
    id: string;                    // GUID of sprk_recordtype_ref record
    name: string;                  // Display name (e.g., "Matter")
    entityLogicalName?: string;    // Logical name of parent entity (e.g., "sprk_matter")
}
```

---

## Part 5: Entity Configuration Data (sprk_recordtype_ref)

### Seed Data Required

The AssociationResolver PCF reads entity configurations from the `sprk_recordtype_ref` entity at runtime. All 8 entity types must be pre-seeded with the following data:

#### Seed Data Schema

```
Entity: sprk_recordtype_ref (Record Type Reference)
Purpose: Dynamic configuration store for AssociationResolver PCF
Status: Requires manual seeding before first use
```

#### Required Records

| Display Name | Logical Name | Record Display Name | Regarding Field | Status Code | Notes |
|--------------|--------------|-------------------|-----------------|-------------|-------|
| Matter | `sprk_matter` | Matter | `sprk_regardingmatter` | 1 (Active) | Case/legal work unit |
| Project | `sprk_project` | Project | `sprk_regardingproject` | 1 (Active) | Billable/operational work |
| Invoice | `sprk_invoice` | Invoice | `sprk_regardinginvoice` | 1 (Active) | Billing document |
| Analysis | `sprk_analysis` | Analysis | `sprk_regardinganalysis` | 1 (Active) | AI-generated analysis |
| Organization | `sprk_organization` | Organization | `sprk_regardingorganization` | 1 (Active) | Client/partner organization |
| Contact | `contact` | Contact | `sprk_regardingperson` | 1 (Active) | Person (contact entity) |
| Work Assignment | `sprk_workassignment` | Work Assignment | `sprk_regardingworkassignment` | 1 (Active) | Resource assignment |
| Budget | `sprk_budget` | Budget | `sprk_regardingbudget` | 1 (Active) | Financial allocation |

#### Example Seed Data (WebAPI JSON)

```json
[
  {
    "sprk_name": "Matter",
    "sprk_recordlogicalname": "sprk_matter",
    "sprk_recorddisplayname": "Matter",
    "sprk_regardingfield": "sprk_regardingmatter"
  },
  {
    "sprk_name": "Project",
    "sprk_recordlogicalname": "sprk_project",
    "sprk_recorddisplayname": "Project",
    "sprk_regardingfield": "sprk_regardingproject"
  },
  {
    "sprk_name": "Invoice",
    "sprk_recordlogicalname": "sprk_invoice",
    "sprk_recorddisplayname": "Invoice",
    "sprk_regardingfield": "sprk_regardinginvoice"
  },
  {
    "sprk_name": "Analysis",
    "sprk_recordlogicalname": "sprk_analysis",
    "sprk_recorddisplayname": "Analysis",
    "sprk_regardingfield": "sprk_regardinganalysis"
  },
  {
    "sprk_name": "Organization",
    "sprk_recordlogicalname": "sprk_organization",
    "sprk_recorddisplayname": "Organization",
    "sprk_regardingfield": "sprk_regardingorganization"
  },
  {
    "sprk_name": "Contact",
    "sprk_recordlogicalname": "contact",
    "sprk_recorddisplayname": "Contact",
    "sprk_regardingfield": "sprk_regardingperson"
  },
  {
    "sprk_name": "Work Assignment",
    "sprk_recordlogicalname": "sprk_workassignment",
    "sprk_recorddisplayname": "Work Assignment",
    "sprk_regardingfield": "sprk_regardingworkassignment"
  },
  {
    "sprk_name": "Budget",
    "sprk_recordlogicalname": "sprk_budget",
    "sprk_recorddisplayname": "Budget",
    "sprk_regardingfield": "sprk_regardingbudget"
  }
]
```

**Where to Seed**: These records can be created via:
1. **Solution Import**: Include in the email-communication-solution definition
2. **BFF API** (if implemented): Create via POST to `/api/communication/recordtypes`
3. **Dataverse Admin**: Manual creation via Model-Driven App

---

## Part 6: How the Control Works

### User Flow

```
1. User opens Communication form
   ↓
2. AssociationResolver PCF initializes
   ├─ Loads all entity types from sprk_recordtype_ref
   ├─ Displays dropdown: "Select Entity Type"
   └─ Shows current selection if form has pre-populated sprk_regardingrecordtype
   ↓
3. User selects entity type (e.g., "Matter")
   ↓
4. Control shows "Select Record" button
   ↓
5. User clicks "Select Record"
   ├─ Opens Lookup dialog for selected entity type
   ├─ User searches for record (e.g., "Smith v. Jones")
   └─ User selects record from results
   ↓
6. Control populates fields automatically:
   ├─ sprk_regardingrecordtype ← Matter record type reference
   ├─ sprk_regardingrecordid ← Matter GUID
   ├─ sprk_regardingrecordname ← "Smith v. Jones"
   ├─ sprk_regardingmatter ← Matter GUID (entity-specific)
   ├─ All other regarding fields ← null (cleared)
   └─ sprk_regardingrecordurl ← Clickable URL to Matter
   ↓
7. (Task 022+) Field Mapping Service integrates:
   ├─ Queries BFF API: GET /api/field-mapping/{sourceEntity}/{sourceId}
   ├─ Auto-populates communication fields from parent
   └─ Shows toast notification with results
   ↓
8. User saves form
   └─ All regarding fields persisted
```

### Field Population Logic

#### RecordSelectionHandler Process

When a user selects a record, the AssociationResolver invokes `handleRecordSelection()`:

```typescript
export async function handleRecordSelection(
    selection: IRecordSelection,
    webAPI: ComponentFramework.WebApi
): Promise<IRecordSelectionResult>
```

**Steps**:

1. **Query sprk_recordtype_ref** to get configuration for selected entity type
   ```
   GET /sprk_recordtype_ref
   ?$filter=sprk_recordlogicalname eq 'sprk_matter' and statecode eq 0
   ```
   Result: Record Type reference with ID, display name, regarding field name

2. **Set Resolver Fields**
   ```javascript
   sprk_regardingrecordtype ← { id: recordTypeId, name: "Matter" }
   sprk_regardingrecordid ← "a1b2c3d4-e5f6-..."
   sprk_regardingrecordname ← "Smith v. Jones"
   sprk_regardingrecordurl ← "/main.aspx?etn=sprk_matter&id=a1b2c3d4-e5f6-..."
   ```

3. **Set Entity-Specific Lookup**
   ```javascript
   sprk_regardingmatter ← { id: "a1b2c3d4-e5f6-...", name: "Smith v. Jones" }
   ```

4. **Clear All Other Entity-Specific Lookups**
   ```javascript
   sprk_regardingproject ← null
   sprk_regardinginvoice ← null
   sprk_regardinganalysis ← null
   sprk_regardingorganization ← null
   sprk_regardingperson ← null
   sprk_regardingworkassignment ← null
   sprk_regardingbudget ← null
   ```

5. **Return Success Result**
   ```javascript
   {
       success: true,
       regardingFieldSet: true,
       otherLookupsCleared: 7,
       errors: []
   }
   ```

---

## Part 7: Auto-Detection Feature

### Pre-Populated Lookup Scenario

If a Communication record is created from a subgrid (e.g., from Matter → Communications subgrid), Dataverse pre-populates the entity-specific lookup:

```javascript
// User clicks "New Communication" from Matter subgrid
// Dataverse pre-populates: sprk_regardingmatter = {current-matter-id}

// AssociationResolver detects this and:
1. Reads pre-populated sprk_regardingmatter field
2. Identifies entity type as "Matter"
3. Queries sprk_recordtype_ref for Matter configuration
4. Auto-completes resolver fields without user interaction
5. Shows read-only UI: "Matter: Smith v. Jones"
6. (Task 022+) Applies field mappings automatically
```

**Detection Logic**:

```typescript
export function detectPrePopulatedParent(): IDetectedParentContext | null {
    // Check all entity-specific lookups for pre-populated values
    const regardingFields = [
        { field: "sprk_regardingmatter", entity: "sprk_matter", display: "Matter" },
        { field: "sprk_regardingproject", entity: "sprk_project", display: "Project" },
        // ... 6 more entity types
    ];

    for (const config of regardingFields) {
        const value = Xrm.Page.getAttribute(config.field).getValue();
        if (value && value.length > 0) {
            return {
                entityType: config.entity,
                recordId: value[0].id,
                recordName: value[0].name,
                entityDisplayName: config.display,
                regardingField: config.field
            };
        }
    }
    return null; // No pre-populated parent
}
```

---

## Part 8: Form Event Handlers

### Required Event Handlers

**None required** - AssociationResolver PCF is self-contained.

The control:
- Manages all field population via direct Xrm.Page attribute writes
- Does NOT require OnChange event handlers to function
- Does NOT require custom JavaScript

### Optional Event Handlers (Not Required)

| Event | Handler | Purpose | Status |
|-------|---------|---------|--------|
| OnSave | (None required) | Control auto-saves via Xrm.Page.getAttribute() | N/A |
| OnLoad | (Optional) | Could validate resolver fields after form load | Optional |
| AssociationResolver OnRecordSelected | (Internal) | Control's own callback, not exposed to form | N/A |

**Best Practice**: Let the control manage field population. Do NOT add custom JavaScript to manipulate regarding fields, as this bypasses the control's validation and can result in inconsistent state.

---

## Part 9: User Experience

### Type-Ahead Search

When user clicks "Select Record" button:

1. Control opens Lookup dialog for selected entity type
2. User types search term (e.g., "Smith")
3. Lookup displays matching records (default Dataverse behavior)
4. User clicks to select
5. Control populates all regarding fields

**Search Scope**: Default Dataverse lookup search (respects entity view and permissions)

### Entity Type Selection

```
Dropdown: Select Entity Type
├─ Matter
├─ Project
├─ Invoice
├─ Analysis
├─ Organization
├─ Person (Contact)
├─ Work Assignment
└─ Budget

User selects type → "Select Record" button activates
```

### Clearing Association

**Current Selection**:
```
Selected: Matter - Smith v. Jones
[Clear] [Change to Different Type]
```

**To Clear**:
1. Click "Clear" button (if implemented)
2. Or manually set `sprk_regardingrecordtype` to null

**Result**: All regarding fields cleared, lookups reset

**Important**: Always use AssociationResolver to clear, never manually edit fields

---

## Part 10: Integration with Field Mapping Service

### Task 022 Integration

After record selection, AssociationResolver integrates with Field Mapping Service (BFF API endpoint):

```typescript
const fieldMappingHandler = createFieldMappingHandler(context.webAPI);

const mappingResult = await fieldMappingHandler.applyMappingsForSelection(
    "sprk_matter",           // source entity
    matterId,                // source record ID
    {}                       // target record (empty, will be populated)
);

if (mappingResult.profileFound) {
    // Apply mapped values to form
    fieldMappingHandler.applyToForm(mappingResult.mappingResult.mappedValues, true);

    // Show toast: "Auto-populated 5 fields from Matter"
}
```

**Result**: Communication fields auto-populate from Matter:
- `sprk_client` ← Matter client
- `sprk_location` ← Matter jurisdiction
- `sprk_billingcontact` ← Matter billing contact
- etc.

**See**: Task 022 (Implement FieldMappingService Integration)

---

## Part 11: Troubleshooting

### Issue: "No Record Type found"

**Cause**: sprk_recordtype_ref missing entry for entity type

**Fix**:
```sql
-- Verify seed data exists
SELECT * FROM sprk_recordtype_ref WHERE statecode = 0
-- Should have 8 rows (one for each entity type)

-- If missing, insert via WebAPI or solution import
```

### Issue: Multiple Entity-Specific Lookups Populated

**Cause**: User manually edited fields instead of using AssociationResolver

**Fix**:
1. Educate users to only use AssociationResolver control
2. Consider hiding lookup fields from direct form editing
3. Add form validation to prevent inconsistent state

### Issue: Resolver Fields Empty

**Cause**: Direct field editing bypassed control

**Fix**:
1. Clear all regarding fields
2. Re-select entity and record via AssociationResolver
3. Never manually populate regarding fields

### Issue: "Select Record" Button Grayed Out

**Cause**: Entity type not selected or sprk_recordtype_ref missing

**Fix**:
1. Select entity type from dropdown
2. Verify sprk_recordtype_ref has seed data for that entity type
3. Check entity type's logical name matches exactly

### Issue: Field Mapping Not Applied

**Cause**: Task 022+ integration not implemented or BFF API unreachable

**Fix**:
1. Verify BFF API endpoint is accessible
2. Check apiBaseUrl property configuration
3. Verify Field Mapping Profile exists for source entity
4. Check browser console for errors

---

## Part 12: Configuration Checklist

### Pre-Deployment Checklist

- [ ] **Schema Fields**: All 8 regarding lookup fields present in sprk_communication entity
  - `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardinginvoice`, `sprk_regardinganalysis`
  - `sprk_regardingorganization`, `sprk_regardingperson`, `sprk_regardingworkassignment`, `sprk_regardingbudget`
  - Resolver fields: `sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`

- [ ] **Seed Data**: All 8 entity types have records in sprk_recordtype_ref
  - Matter, Project, Invoice, Analysis, Organization, Contact, Work Assignment, Budget
  - Each with correct logical name and regarding field name

- [ ] **PCF Control**: AssociationResolver deployed to Dataverse
  - Version 1.0.6 or later
  - Assembly web resource registered
  - Control published

- [ ] **Form Configuration**: Communication form has AssociationResolver control
  - Control bound to `sprk_regardingrecordtype`
  - Control placed in Association section
  - Control properties: `apiBaseUrl` (optional, defaults to dev endpoint)

- [ ] **Field Visibility**: Entity-specific lookups hidden or in secondary section
  - Users interact only with AssociationResolver
  - Resolver fields hidden (managed by control)

- [ ] **Permissions**: Users have permissions to access all 8 entity types
  - Read on sprk_recordtype_ref
  - Read/Write on all parent entities (Matter, Project, etc.)

### Post-Deployment Validation

- [ ] **Functionality Test**: Create new Communication record
  - Can select entity type from dropdown
  - Can search and select record
  - All regarding fields populate correctly
  - Only selected entity lookup populated, others null

- [ ] **Auto-Detection Test**: Create Communication from parent subgrid
  - Resolver fields auto-populate
  - Type shown as read-only

- [ ] **Field Mapping Test** (Task 022+): After selection
  - Toast notification appears
  - Communication fields auto-populated from parent
  - Notification shows count of fields mapped

- [ ] **Clear Test**: Clear association and change type
  - All regarding fields clear correctly
  - No orphaned values remain

---

## Related Documentation

- **ADR-024**: [Polymorphic Resolver Pattern](../../docs/adr/ADR-024-polymorphic-resolver-pattern.md)
- **Pattern**: [.claude/patterns/dataverse/polymorphic-resolver.md](../../.claude/patterns/dataverse/polymorphic-resolver.md)
- **PCF Source**: [src/client/pcf/AssociationResolver/](../../src/client/pcf/AssociationResolver/)
- **Data Schema**: [docs/data-model/sprk_communication-data-schema.md](../../docs/data-model/sprk_communication-data-schema.md)
- **Task 022**: Field Mapping Service Integration
- **Task 024**: Toast Notifications for Mapping Results
- **Task 025**: RegardingLink PCF for Grid Display

---

## Summary

The AssociationResolver PCF provides a user-friendly interface for managing polymorphic associations in the Communication entity. By leveraging the `sprk_recordtype_ref` configuration table and dynamic field binding, it eliminates the need for hard-coded entity types and scales to support new parent entities without code changes.

**Key Success Factors**:
1. All 8 entity types configured in sprk_recordtype_ref
2. All regarding lookup fields present in sprk_communication schema
3. AssociationResolver PCF deployed and bound to sprk_regardingrecordtype
4. Users trained to use control, not manual field editing
5. Field Mapping Service integration for auto-population (Task 022+)

---

*Last Updated: February 21, 2026*
*Task 021: Configure AssociationResolver PCF*
*Email Communication Solution R1*
