# Field Inheritance Flow - Parent to Child Records

**Component:** Universal Quick Create PCF Control
**Version:** 1.0.0
**Date:** 2025-10-07

---

## Overview

This document explains exactly how field values are inherited from parent records (e.g., Matter, Account) to child records (e.g., Document, Contact) in the Universal Quick Create control.

---

## The Complete Flow

### Step 1: User Opens Quick Create Form

**User Action:**
- User clicks "+ New Document" from Matter subgrid
- OR clicks "+ New Contact" from Account subgrid

**Power Apps Automatically Provides:**
- Parent entity name (e.g., "sprk_matter")
- Parent record ID (GUID)
- Child entity name (e.g., "sprk_document")

---

### Step 2: PCF Control Initializes

**Function:** `init()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 50-88)

**Execution Order:**
1. Initialize MSAL authentication (async, background)
2. **Load parent context** → `loadParentContext()`
3. **Load parent record data** → `loadParentRecordData()`
4. Load configuration from manifest parameters → `loadConfiguration()`
5. Load entity field configuration → `loadEntityFieldConfiguration()`
6. Render React component → `renderReactComponent()`

```typescript
public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // ...
    await this.loadParentContext(context);  // 👈 Gets parent info
    this.loadConfiguration(context);         // 👈 Gets field mappings
    this.loadEntityFieldConfiguration();     // 👈 Gets field definitions
    this.renderReactComponent();             // 👈 Renders form with defaults
}
```

---

### Step 3: Load Parent Context

**Function:** `loadParentContext()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 174-202)

**Purpose:** Extract parent entity information from Power Apps context

**Code:**
```typescript
private async loadParentContext(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // Power Apps automatically provides this context when Quick Create is opened from a subgrid
    const formContext = (context as any).mode?.contextInfo;

    if (formContext) {
        // Extract parent and child entity information
        this.parentEntityName = formContext.regardingEntityName || '';  // e.g., "sprk_matter"
        this.parentRecordId = formContext.regardingObjectId || '';      // e.g., "guid-123"
        this.entityName = formContext.entityName || '';                 // e.g., "sprk_document"

        // Retrieve parent record data (next step)
        if (this.parentRecordId && this.parentEntityName) {
            await this.loadParentRecordData(context);  // 👈 Fetch parent field values
        }
    }
}
```

**What Gets Stored:**
- `this.parentEntityName` = "sprk_matter" (or "account", "contact", etc.)
- `this.parentRecordId` = GUID of the parent record
- `this.entityName` = "sprk_document" (or "task", "contact", etc.)

---

### Step 4: Load Parent Record Data

**Function:** `loadParentRecordData()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 204-225)

**Purpose:** Fetch actual field values from the parent record in Dataverse

**Code:**
```typescript
private async loadParentRecordData(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // Determine which fields to retrieve based on parent entity type
    const selectFields = this.getParentSelectFields(this.parentEntityName);
    // e.g., "sprk_name,sprk_containerid,_ownerid_value,sprk_matternumber"

    // Retrieve parent record from Dataverse
    this.parentRecordData = await context.webAPI.retrieveRecord(
        this.parentEntityName,      // e.g., "sprk_matter"
        this.parentRecordId,         // GUID
        `?$select=${selectFields}`   // Fields to retrieve
    );

    // Example result:
    // {
    //   "sprk_name": "Smith Corp Acquisition",
    //   "sprk_containerid": "b!ABC123...",
    //   "_ownerid_value": "guid-456",
    //   "sprk_matternumber": "MAT-2025-001"
    // }
}
```

**What Gets Stored:**
- `this.parentRecordData` = Object with parent record field values

---

### Step 5: Determine Which Fields to Retrieve

**Function:** `getParentSelectFields()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 227-249)

**Purpose:** Define which fields to retrieve from parent record (optimized query)

**Code:**
```typescript
private getParentSelectFields(entityName: string): string {
    // Define fields to retrieve based on parent entity type
    const fieldMappings: Record<string, string[]> = {
        'sprk_matter': [
            'sprk_name',                    // Matter Name
            'sprk_containerid',             // SharePoint Container ID
            '_ownerid_value',               // Owner (lookup)
            '_sprk_primarycontact_value',   // Primary Contact (lookup)
            'sprk_matternumber'             // Matter Number (unique identifier)
        ],
        'account': [
            'name',                // Account Name
            'address1_composite',  // Full Address
            '_ownerid_value'       // Owner (lookup)
        ],
        'contact': [
            'fullname',            // Full Name
            '_ownerid_value'       // Owner (lookup)
        ]
    };

    return (fieldMappings[entityName] || ['name']).join(',');
    // Returns: "sprk_name,sprk_containerid,_ownerid_value,sprk_matternumber"
}
```

**Why This Matters:**
- Only retrieves fields that might be mapped to child entity
- Reduces API payload and improves performance
- Must include ALL fields that admin configured in `defaultValueMappings`

---

### Step 6: Load Configuration (Field Mappings)

**Function:** `loadConfiguration()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 251-278)

**Purpose:** Load admin-configured field mappings from manifest parameters

**Code:**
```typescript
private loadConfiguration(context: ComponentFramework.Context<IInputs>): void {
    // Load default value mappings from manifest parameter
    const mappingJson = context.parameters.defaultValueMappings?.raw;

    if (mappingJson) {
        try {
            this.defaultValueMappings = JSON.parse(mappingJson);
            // Example:
            // {
            //   "sprk_matter": {
            //     "sprk_matternumber": "sprk_matter",
            //     "sprk_containerid": "sprk_containerid"
            //   }
            // }
        } catch (error) {
            logger.error('Failed to parse default value mappings', error);
        }
    }

    // Load other config: enableFileUpload, sdapApiBaseUrl
    // ...
}
```

**What Gets Stored:**
- `this.defaultValueMappings` = Object mapping parent fields to child fields

---

### Step 7: Calculate Default Values

**Function:** `getDefaultValues()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 317-367)

**Purpose:** Apply field mappings to create default values for child record

**Code:**
```typescript
private getDefaultValues(): Record<string, unknown> {
    const defaults: Record<string, unknown> = {};

    if (!this.parentRecordData || !this.parentEntityName) {
        return defaults;  // No parent data available
    }

    // Get mapping for this parent entity type
    const mappings = this.defaultValueMappings[this.parentEntityName];
    // e.g., { "sprk_matternumber": "sprk_matter", "sprk_containerid": "sprk_containerid" }

    if (!mappings) {
        return defaults;  // No mappings configured
    }

    // Apply each mapping
    for (const [parentField, childField] of Object.entries(mappings)) {
        // Get value from parent record
        const parentValue = this.parentRecordData[parentField];
        // e.g., parentField = "sprk_matternumber", parentValue = "MAT-2025-001"

        if (parentValue !== undefined && parentValue !== null) {
            // Check if this is a LOOKUP field or SIMPLE field
            if (this.isLookupFieldMapping(parentField, childField)) {
                // 👈 LOOKUP FIELD: Create OData bind reference
                const entitySetName = this.getEntitySetName(this.parentEntityName);
                defaults[`${childField}@odata.bind`] = `/${entitySetName}(${this.parentRecordId})`;

                // Example:
                // childField = "sprk_matter"
                // defaults["sprk_matter@odata.bind"] = "/sprk_matters(guid-123)"

            } else {
                // 👈 SIMPLE FIELD: Copy value directly
                defaults[childField] = parentValue;

                // Example:
                // childField = "sprk_containerid"
                // defaults["sprk_containerid"] = "b!ABC123..."
            }
        }
    }

    return defaults;
    // Example result:
    // {
    //   "sprk_matter@odata.bind": "/sprk_matters(guid-123)",
    //   "sprk_containerid": "b!ABC123..."
    // }
}
```

**Decision Logic: Lookup vs Simple Field**

The `isLookupFieldMapping()` function determines if a field is a lookup:

```typescript
private isLookupFieldMapping(parentField: string, childField: string): boolean {
    // Known lookup field mappings
    const lookupMappings: Record<string, string[]> = {
        'sprk_matter': ['sprk_matter'],           // Matter lookup on Document
        'account': ['parentaccountid', 'accountid'],
        'contact': ['parentcontactid', 'contactid']
    };

    // Check if child field is a known lookup for this parent entity
    const lookupFields = lookupMappings[this.parentEntityName];
    if (lookupFields && lookupFields.includes(childField)) {
        return true;  // 👈 It's a LOOKUP field
    }

    // Pattern: child field matches parent entity name
    if (childField === this.parentEntityName) {
        return true;  // 👈 It's a LOOKUP field
    }

    return false;  // 👈 It's a SIMPLE field
}
```

**Examples:**

| Parent Entity | Parent Field | Child Field | Type | Result |
|---------------|--------------|-------------|------|--------|
| sprk_matter | sprk_matternumber | sprk_matter | LOOKUP | `sprk_matter@odata.bind: /sprk_matters(guid)` |
| sprk_matter | sprk_containerid | sprk_containerid | SIMPLE | `sprk_containerid: "b!ABC..."` |
| account | name | sprk_companyname | SIMPLE | `sprk_companyname: "Acme Corp"` |
| account | address1_composite | address1_composite | SIMPLE | `address1_composite: "123 Main St..."` |

---

### Step 8: Render React Form with Defaults

**Function:** `renderReactComponent()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 423-447)

**Purpose:** Render the QuickCreateForm React component with default values

**Code:**
```typescript
private renderReactComponent(): void {
    if (!this.reactRoot) {
        return;
    }

    // Calculate default values from parent record
    const defaultValues = this.getDefaultValues();  // 👈 From Step 7
    // e.g., {
    //   "sprk_matter@odata.bind": "/sprk_matters(guid-123)",
    //   "sprk_containerid": "b!ABC123..."
    // }

    // Prepare props for React component
    const props: QuickCreateFormProps = {
        entityName: this.entityName,              // "sprk_document"
        parentEntityName: this.parentEntityName,  // "sprk_matter"
        parentRecordId: this.parentRecordId,      // GUID
        defaultValues: defaultValues,             // 👈 Passed to form
        fieldMetadata: this.entityFieldConfig?.fields || [],
        enableFileUpload: this.enableFileUpload,
        sdapApiBaseUrl: this.sdapApiBaseUrl,
        context: this.context,
        onSave: this.handleSave.bind(this),
        onCancel: this.handleCancel.bind(this)
    };

    // Render React component
    this.reactRoot.render(React.createElement(QuickCreateForm, props));
}
```

---

### Step 9: React Form Receives Defaults

**Component:** `QuickCreateForm`
**File:** [QuickCreateForm.tsx](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/components/QuickCreateForm.tsx)

**Purpose:** Display form with pre-populated field values

**Code (Simplified):**
```typescript
export const QuickCreateForm: React.FC<QuickCreateFormProps> = (props) => {
    const { defaultValues, fieldMetadata, enableFileUpload } = props;

    // Initialize form state with default values
    const [formData, setFormData] = React.useState<Record<string, unknown>>(
        defaultValues || {}  // 👈 Starts with inherited values
    );

    // Render dynamic fields
    return (
        <div className="quick-create-form">
            <DynamicFormFields
                fields={fieldMetadata}
                values={formData}              // 👈 Pre-populated from parent
                onChange={setFormData}
            />
            {enableFileUpload && <FileUploadField />}
            <Button onClick={handleSave}>Save</Button>
        </div>
    );
};
```

---

### Step 10: User Interacts and Saves

**User Action:**
- User sees form with pre-filled values
- User can edit pre-filled values if needed
- User fills in remaining required fields
- User clicks Save

**Function:** `handleSave()`
**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts) (Lines 449+)

**What Happens:**
1. Form data collected (includes inherited values + user input)
2. File uploaded to SharePoint (if provided)
3. Record created in Dataverse with all field values

**Example Record Created:**
```json
{
  // User-entered fields
  "sprk_documenttitle": "Contract.pdf",
  "sprk_description": "Service agreement",

  // Inherited from parent (lookup)
  "sprk_matter@odata.bind": "/sprk_matters(guid-123)",

  // Inherited from parent (simple)
  "sprk_containerid": "b!ABC123...",

  // File metadata (from upload)
  "sprk_sharepointurl": "https://...",
  "sprk_driveitemid": "01ABC...",
  "sprk_filename": "Contract.pdf"
}
```

---

## Summary: The Complete Function Chain

```
User Opens Quick Create
         ↓
   init() - PCF initialization
         ↓
   loadParentContext() - Get parent entity info from Power Apps
         ↓
   loadParentRecordData() - Fetch parent field values from Dataverse
         ↓
   getParentSelectFields() - Determine which fields to retrieve
         ↓
   loadConfiguration() - Load admin field mapping config
         ↓
   renderReactComponent() - Render form
         ↓
   getDefaultValues() - Calculate inherited values
         ↓
   isLookupFieldMapping() - Decide lookup vs simple
         ↓
   QuickCreateForm - Display form with pre-filled values
         ↓
   User fills remaining fields + clicks Save
         ↓
   handleSave() - Create record with inherited + user values
```

---

## Key Functions Summary

| Function | File | Lines | Purpose |
|----------|------|-------|---------|
| `init()` | UniversalQuickCreatePCF.ts | 50-88 | Initialize control, orchestrate loading |
| `loadParentContext()` | UniversalQuickCreatePCF.ts | 174-202 | Extract parent entity info from Power Apps |
| `loadParentRecordData()` | UniversalQuickCreatePCF.ts | 204-225 | Fetch parent record from Dataverse |
| `getParentSelectFields()` | UniversalQuickCreatePCF.ts | 227-249 | Define which parent fields to retrieve |
| `loadConfiguration()` | UniversalQuickCreatePCF.ts | 251-278 | Load admin field mapping config |
| `getDefaultValues()` | UniversalQuickCreatePCF.ts | 317-367 | **MAIN FUNCTION** - Apply field mappings |
| `isLookupFieldMapping()` | UniversalQuickCreatePCF.ts | 369-401 | Determine if field is lookup or simple |
| `getEntitySetName()` | UniversalQuickCreatePCF.ts | 403-421 | Get OData entity set name for lookups |
| `renderReactComponent()` | UniversalQuickCreatePCF.ts | 423-447 | Render form with defaults |
| `QuickCreateForm` | QuickCreateForm.tsx | - | React component that displays form |

---

## Example Walkthrough: Document from Matter

### Configuration:
```json
{
  "defaultValueMappings": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    }
  }
}
```

### Step-by-Step:

1. **User clicks "+ New Document" on Matter "Smith Corp Acquisition"**

2. **`loadParentContext()` extracts:**
   - `parentEntityName` = "sprk_matter"
   - `parentRecordId` = "abc-123-guid"
   - `entityName` = "sprk_document"

3. **`getParentSelectFields("sprk_matter")` returns:**
   - `"sprk_name,sprk_containerid,_ownerid_value,sprk_matternumber"`

4. **`loadParentRecordData()` retrieves from Dataverse:**
   ```json
   {
     "sprk_name": "Smith Corp Acquisition",
     "sprk_containerid": "b!XYZ789...",
     "_ownerid_value": "owner-guid",
     "sprk_matternumber": "MAT-2025-001"
   }
   ```

5. **`loadConfiguration()` loads mapping:**
   ```json
   {
     "sprk_matter": {
       "sprk_matternumber": "sprk_matter",
       "sprk_containerid": "sprk_containerid"
     }
   }
   ```

6. **`getDefaultValues()` processes:**

   **Iteration 1:** `"sprk_matternumber" → "sprk_matter"`
   - `parentValue` = "MAT-2025-001"
   - `isLookupFieldMapping()` = TRUE (child field matches parent entity)
   - `getEntitySetName("sprk_matter")` = "sprk_matters"
   - **Result:** `defaults["sprk_matter@odata.bind"] = "/sprk_matters(abc-123-guid)"`

   **Iteration 2:** `"sprk_containerid" → "sprk_containerid"`
   - `parentValue` = "b!XYZ789..."
   - `isLookupFieldMapping()` = FALSE (simple field)
   - **Result:** `defaults["sprk_containerid"] = "b!XYZ789..."`

7. **Final defaults object:**
   ```json
   {
     "sprk_matter@odata.bind": "/sprk_matters(abc-123-guid)",
     "sprk_containerid": "b!XYZ789..."
   }
   ```

8. **`QuickCreateForm` renders with:**
   - Matter field: Pre-populated with lookup to parent Matter (hidden from user)
   - Container ID field: Pre-populated with "b!XYZ789..." (hidden from user)
   - Document Title field: Empty (user will fill)
   - Description field: Empty (user will fill)
   - File picker: Shown (user will select file)

9. **User fills:**
   - Document Title: "Service Agreement.pdf"
   - Selects file: "Service Agreement.pdf"
   - Clicks Save

10. **Record created in Dataverse:**
    ```json
    {
      "sprk_documenttitle": "Service Agreement.pdf",
      "sprk_matter@odata.bind": "/sprk_matters(abc-123-guid)",
      "sprk_containerid": "b!XYZ789...",
      "sprk_sharepointurl": "https://...",
      "sprk_driveitemid": "file-guid",
      "sprk_filename": "Service Agreement.pdf"
    }
    ```

---

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    UniversalQuickCreatePCF                      │
│                     (Main PCF Control)                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Initialization Flow:                                          │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 1. loadParentContext()                                  │   │
│  │    - Extracts parent entity + record ID from Power Apps│   │
│  └────────────────────────────────────────────────────────┘   │
│                          ↓                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 2. loadParentRecordData()                              │   │
│  │    - Fetches parent field values from Dataverse        │   │
│  │    - Uses getParentSelectFields() for query            │   │
│  └────────────────────────────────────────────────────────┘   │
│                          ↓                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 3. loadConfiguration()                                  │   │
│  │    - Loads admin field mapping config                  │   │
│  └────────────────────────────────────────────────────────┘   │
│                          ↓                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 4. renderReactComponent()                              │   │
│  │    - Calls getDefaultValues()                          │   │
│  │    - Passes defaults to QuickCreateForm                │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Field Mapping Logic:                                          │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ getDefaultValues()          ← MAIN FUNCTION            │   │
│  │ ├─ Loops through mappings                              │   │
│  │ ├─ Calls isLookupFieldMapping()                        │   │
│  │ │  ├─ IF LOOKUP: Uses getEntitySetName()              │   │
│  │ │  │             Creates @odata.bind reference         │   │
│  │ │  └─ IF SIMPLE: Copies value directly                │   │
│  │ └─ Returns defaults object                             │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│                      QuickCreateForm                            │
│                    (React Component)                            │
├─────────────────────────────────────────────────────────────────┤
│  - Receives defaultValues as prop                              │
│  - Initializes formData state with defaults                    │
│  - Renders DynamicFormFields with pre-populated values         │
│  - User edits/fills remaining fields                           │
│  - On Save: Returns formData to PCF                            │
└─────────────────────────────────────────────────────────────────┘
```

---

**Date:** 2025-10-07
**Component:** Universal Quick Create PCF Control
**Version:** 1.0.0
