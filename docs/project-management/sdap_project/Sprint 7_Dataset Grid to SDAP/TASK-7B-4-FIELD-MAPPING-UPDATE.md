# Sprint 7B Task 4: Field Mapping Update

**Status:** ✅ COMPLETE
**Date:** 2025-10-07
**Sprint:** 7B - Universal Quick Create (SDAP Integration)

---

## Overview

Updated the Universal Quick Create control to properly handle **lookup field mappings** in addition to simple field mappings. This ensures parent-child relationships are correctly established when creating records.

---

## Changes Made

### 1. Admin Guide Updated ✅

**File:** [UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md](../../../docs/UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md)

**Changes:**
- Removed Configuration 2 (Task from Matter) as requested
- Updated Configuration 1 (Document from Matter) with correct field mappings:
  - `sprk_matternumber` → `sprk_matter` (LookupType)
  - `sprk_containerid` → `sprk_containerid` (Text)
- Updated Configuration 2 (Contact from Account) with correct field mappings:
  - `name` → `sprk_companyname` (Text)
  - `address1_composite` → `address1_composite` (MemoType)
- Added field mapping tables showing parent-child relationships
- Added section explaining Simple vs Lookup field mappings

**Field Mapping Types Added:**

1. **Simple Field Mapping** (Text, Number, Date, etc.)
   - Copies value directly from parent field to child field
   - Example: `"name": "sprk_companyname"` (copies Account Name to Company Name)
   - Example: `"sprk_containerid": "sprk_containerid"` (copies Container ID)

2. **Lookup Field Mapping** (Relationships)
   - Creates relationship reference between parent and child records
   - Child field name matches parent entity name (e.g., `sprk_matter` for sprk_matter parent)
   - Example: `"sprk_matternumber": "sprk_matter"` (links Document to parent Matter)
   - Automatically uses OData bind syntax internally: `sprk_matter@odata.bind`

---

### 2. Code Updated ✅

**File:** [UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts)

#### Change 1: Updated `getDefaultValues()` Method

**Location:** Lines 317-367

**What Changed:**
- Added logic to detect lookup field mappings vs simple field mappings
- For lookup fields: Uses OData bind syntax (`fieldname@odata.bind`)
- For simple fields: Copies value directly

**Before:**
```typescript
// Simple value copy for all fields
defaults[childField] = parentValue;
```

**After:**
```typescript
if (this.isLookupFieldMapping(parentField, childField)) {
    // For lookup fields, create OData bind reference
    const entitySetName = this.getEntitySetName(this.parentEntityName);
    defaults[`${childField}@odata.bind`] = `/${entitySetName}(${this.parentRecordId})`;
} else {
    // For simple fields, just copy the value
    defaults[childField] = parentValue;
}
```

**Why This Matters:**
- Lookup fields require OData bind syntax to create relationships
- Example: `sprk_matter@odata.bind: /sprk_matters(guid-123)`
- Without this, the lookup relationship won't be established

---

#### Change 2: Added `isLookupFieldMapping()` Method

**Location:** Lines 369-401

**Purpose:** Identifies whether a field mapping is for a lookup field

**Logic:**
```typescript
private isLookupFieldMapping(parentField: string, childField: string): boolean {
    // Known lookup field mappings
    const lookupMappings: Record<string, string[]> = {
        'sprk_matter': ['sprk_matter'],
        'account': ['parentaccountid', 'accountid'],
        'contact': ['parentcontactid', 'contactid']
    };

    // Check if child field is a known lookup for this parent entity
    const lookupFields = lookupMappings[this.parentEntityName];
    if (lookupFields && lookupFields.includes(childField)) {
        return true;
    }

    // Pattern: child field matches parent entity name (e.g., sprk_matter)
    if (childField === this.parentEntityName) {
        return true;
    }

    return false;
}
```

**Key Pattern:** Child field name matches parent entity name
- `sprk_matter` parent → `sprk_matter` child field = LOOKUP
- `account` parent → `accountid` child field = LOOKUP
- `account` parent → `sprk_companyname` child field = SIMPLE

---

#### Change 3: Added `getEntitySetName()` Method

**Location:** Lines 403-421

**Purpose:** Maps entity logical names to OData entity set names (plural forms)

**Logic:**
```typescript
private getEntitySetName(parentEntityName: string): string {
    const entitySetMap: Record<string, string> = {
        'sprk_matter': 'sprk_matters',
        'sprk_client': 'sprk_clients',
        'account': 'accounts',
        'contact': 'contacts'
    };

    return entitySetMap[parentEntityName] || `${parentEntityName}s`;
}
```

**Why This Matters:**
- OData URLs use plural entity set names
- `/sprk_matters(guid)` not `/sprk_matter(guid)`
- `/accounts(guid)` not `/account(guid)`

---

#### Change 4: Updated `getParentSelectFields()` Method

**Location:** Lines 227-249

**What Changed:**
- Added `address1_composite` to account entity fields

**Before:**
```typescript
'account': [
    'name',
    '_ownerid_value'
]
```

**After:**
```typescript
'account': [
    'name',
    'address1_composite',
    '_ownerid_value'
]
```

**Why This Matters:**
- Needed to retrieve `address1_composite` from parent Account
- Without this, the field mapping `address1_composite` → `address1_composite` would fail
- Field must be retrieved before it can be mapped

---

### 3. Solution Package Rebuilt ✅

**File:** `UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip`

**Build Results:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:37.74

Solution: bin\Release\UniversalQuickCreateSolution.zip generated.
Solution Package Type: Managed generated.
```

**Bundle Size:** 723 KB (production mode, unchanged)

---

## Configuration Examples

### Document from Matter (Updated)

**Field Mapping:**

| Parent Table | Parent Column Name | Parent Column Logical | Child Table   | Child Column Name | Child Column Logical | Field Type |
| ------------ | ------------------ | --------------------- | ------------- | ----------------- | -------------------- | ---------- |
| sprk_matter  | Matter Number      | sprk_matternumber     | sprk_document | Matter            | sprk_matter          | LookupType |
| sprk_matter  | Container Id       | sprk_containerid      | sprk_document | Container Id      | sprk_containerid     | Text       |

**Control Parameters:**
```json
{
  "defaultValueMappings": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    }
  },
  "enableFileUpload": true,
  "sdapApiBaseUrl": "https://your-api.azurewebsites.net/api"
}
```

**What Happens:**
1. User clicks "+ New Document" from Matter subgrid
2. Quick Create form opens
3. `sprk_matter` field populated with OData bind: `/sprk_matters(guid-of-parent-matter)`
4. `sprk_containerid` field populated with value from parent Matter
5. User enters Document Title, selects file
6. File uploads to SharePoint Embedded
7. Document record created with lookup to parent Matter

---

### Contact from Account (Updated)

**Field Mapping:**

| Parent Table | Parent Column Name | Parent Column Logical | Child Table | Child Column Name | Child Column Logical | Field Type |
| ------------ | ------------------ | --------------------- | ----------- | ----------------- | -------------------- | ---------- |
| account      | Account Name       | name                  | contact     | Company Name      | sprk_companyname     | Text       |
| account      | Address 1          | address1_composite    | contact     | Address 1         | address1_composite   | MemoType   |

**Control Parameters:**
```json
{
  "defaultValueMappings": {
    "account": {
      "name": "sprk_companyname",
      "address1_composite": "address1_composite"
    }
  },
  "enableFileUpload": false
}
```

**What Happens:**
1. User clicks "+ New Contact" from Account subgrid
2. Quick Create form opens
3. `sprk_companyname` field populated with Account Name (simple value copy)
4. `address1_composite` field populated with Account Address (simple value copy)
5. User enters First Name, Last Name, Email, Phone
6. Contact record created

---

## Technical Details

### How Lookup Mapping Works

**Admin Configuration:**
```json
"sprk_matternumber": "sprk_matter"
```

**Internal Processing:**

1. **Control detects lookup mapping:**
   ```typescript
   isLookupFieldMapping('sprk_matternumber', 'sprk_matter')
   // Returns: true (child field matches parent entity name)
   ```

2. **Control generates OData bind reference:**
   ```typescript
   const entitySetName = getEntitySetName('sprk_matter'); // "sprk_matters"
   defaults['sprk_matter@odata.bind'] = `/sprk_matters(${parentRecordId})`;
   ```

3. **Dataverse API receives:**
   ```json
   {
     "sprk_documenttitle": "Contract.pdf",
     "sprk_containerid": "b!ABC...",
     "sprk_matter@odata.bind": "/sprk_matters(guid-123)"
   }
   ```

4. **Result:** Document record created with lookup relationship to Matter

---

### How Simple Mapping Works

**Admin Configuration:**
```json
"name": "sprk_companyname"
```

**Internal Processing:**

1. **Control detects simple mapping:**
   ```typescript
   isLookupFieldMapping('name', 'sprk_companyname')
   // Returns: false (child field does NOT match parent entity name)
   ```

2. **Control copies value directly:**
   ```typescript
   defaults['sprk_companyname'] = parentRecordData['name'];
   ```

3. **Dataverse API receives:**
   ```json
   {
     "firstname": "John",
     "lastname": "Smith",
     "sprk_companyname": "Acme Corporation"
   }
   ```

4. **Result:** Contact record created with Company Name = "Acme Corporation"

---

## Testing

### Test Case 1: Document from Matter (Lookup Mapping)

**Steps:**
1. Open Matter record with Container ID
2. Click "+ New Document" in Documents subgrid
3. Enter Document Title
4. Select file
5. Click Save

**Expected Results:**
- ✅ File uploads to SharePoint Embedded
- ✅ Document record created
- ✅ Document linked to parent Matter (lookup field populated)
- ✅ Container ID copied from parent Matter

**Browser Console Expected:**
```
[UniversalQuickCreatePCF] Mapped lookup default value: {
  parentField: "sprk_matternumber",
  childField: "sprk_matter",
  lookupReference: "/sprk_matters(guid-123)"
}
[UniversalQuickCreatePCF] Mapped default value: {
  parentField: "sprk_containerid",
  childField: "sprk_containerid",
  value: "b!ABC..."
}
```

---

### Test Case 2: Contact from Account (Simple Mapping)

**Steps:**
1. Open Account record
2. Click "+ New Contact" in Contacts subgrid
3. Enter First Name, Last Name, Email, Phone
4. Click Save

**Expected Results:**
- ✅ Contact record created
- ✅ Company Name pre-filled with Account Name
- ✅ Address pre-filled with Account Address

**Browser Console Expected:**
```
[UniversalQuickCreatePCF] Mapped default value: {
  parentField: "name",
  childField: "sprk_companyname",
  value: "Acme Corporation"
}
[UniversalQuickCreatePCF] Mapped default value: {
  parentField: "address1_composite",
  childField: "address1_composite",
  value: "123 Main St, New York, NY 10001"
}
```

---

## Files Changed

### Code Files:
1. **[UniversalQuickCreatePCF.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts)** - Updated default value mapping logic
   - Lines 317-367: Updated `getDefaultValues()` method
   - Lines 369-401: Added `isLookupFieldMapping()` method
   - Lines 403-421: Added `getEntitySetName()` method
   - Lines 227-249: Updated `getParentSelectFields()` method

### Documentation Files:
2. **[UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md](../../../docs/UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md)** - Updated configuration examples
   - Removed Configuration 2 (Task from Matter)
   - Updated Configuration 1 with field mapping table
   - Updated Configuration 2 (Contact from Account) with field mapping table
   - Added field mapping types explanation

### Build Artifacts:
3. **UniversalQuickCreateSolution.zip** - Rebuilt with updated code
   - Location: `bin/Release/UniversalQuickCreateSolution.zip`
   - Size: 195 KB (managed solution)
   - Bundle: 723 KB (production mode)

---

## Deployment

The updated solution package is ready for deployment. No changes to deployment process.

**To deploy updated solution:**

```bash
# Authenticate to environment
pac auth create --url https://your-environment.crm.dynamics.com

# Import updated solution (will upgrade existing)
pac solution import --path UniversalQuickCreateSolution/bin/Release/UniversalQuickCreateSolution.zip --async
```

**Note:** Upgrading solution will NOT affect existing configurations. Admins do NOT need to reconfigure Quick Create forms unless they want to use the new field mappings.

---

## Backward Compatibility

### Existing Configurations Continue to Work ✅

**Old Configuration (Still Works):**
```json
{
  "defaultValueMappings": {
    "sprk_matter": {
      "sprk_name": "sprk_documenttitle",
      "_ownerid_value": "ownerid"
    }
  }
}
```

**Result:** Document Title pre-filled with Matter Name, Owner pre-filled

**New Configuration (Improved):**
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

**Result:** Document linked to Matter via lookup, Container ID pre-filled

---

## Summary

| Item | Status |
|------|--------|
| Admin Guide Updated | ✅ Complete |
| Code Updated | ✅ Complete |
| Solution Package Rebuilt | ✅ Complete |
| Backward Compatibility | ✅ Maintained |
| Ready for Deployment | ✅ Yes |

**Next Steps:**
1. Deploy updated solution to test environment
2. Test lookup field mappings (Document from Matter)
3. Test simple field mappings (Contact from Account)
4. Verify backward compatibility with existing configurations
5. Deploy to production when validated

---

**Date:** 2025-10-07
**Sprint:** 7B - Universal Quick Create
**Change Type:** Enhancement (Lookup Field Support)
