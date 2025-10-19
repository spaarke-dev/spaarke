# Field Binding Fix - UniversalQuickCreate PCF Control

**Date:** 2025-10-07
**Issue:** `sprk_fileuploadmetadata` field showing as Long Text (memo) instead of Single Line Text
**Status:** ✅ RESOLVED

---

## Problem

When attempting to configure the UniversalQuickCreate PCF control on the Quick Create form, the `sprk_fileuploadmetadata` field was:
- Showing as **Long Text (memo)** type
- **Not available** for PCF control binding
- PCF controls can only bind to **Single Line Text** fields

**User Screenshot Evidence:** Field picker showed "File Upload Metadata" but it was not selectable because of the wrong field type.

---

## Root Cause

The `sprk_fileuploadmetadata` field:
1. **Not defined in the Document entity solution** (Entity.xml)
2. Either doesn't exist in the environment, OR
3. Was manually created with the wrong field type (Long Text instead of Single Line Text)

**Investigation Results:**
- Checked `src/Entities/sprk_Document/Entity.xml`
- Field `sprk_fileuploadmetadata` is **NOT defined** in the entity schema
- Manifest was referencing a field that doesn't exist in the solution

---

## Solution Applied

### 1. Updated PCF Manifest

**Changed binding from `sprk_fileuploadmetadata` to `sprk_filename`**

**File:** `ControlManifest.Input.xml`

**Before:**
```xml
<property name="speMetadata"
          display-name-key="SPE_Metadata_Field"
          description-key="Field to bind PCF control (value not used for multi-file upload)"
          of-type="SingleLine.Text"
          usage="bound"
          required="true" />
```

**After:**
```xml
<!-- Field binding for Quick Create compatibility -->
<!-- Note: Bind to sprk_filename field (Single Line Text) on Quick Create form -->
<property name="boundField"
          display-name-key="Bound_Field"
          description-key="Field to bind PCF control (bind to sprk_filename - value not used)"
          of-type="SingleLine.Text"
          usage="bound"
          required="true" />
```

**Why `sprk_filename`?**
- Already exists in Document entity (defined in Entity.xml line 131-170)
- Type: **Single Line Text (nvarchar)**
- MaxLength: 1000 characters
- Perfect for PCF binding
- Value will be automatically populated after file upload

---

### 2. Updated Control Version

**Incremented version:** 1.0.1 → 1.0.2

This ensures Dataverse recognizes the updated control.

---

### 3. Rebuilt and Redeployed Solution

**Build Output:**
```
npm run build → bundle.js (6.35 MB development, 569 KB production)
dotnet build → UniversalQuickCreateSolution.zip (Unmanaged)
Solution Package Type: Unmanaged
```

**Deployment:**
```bash
pac solution delete --solution-name UniversalQuickCreateSolution
pac solution import --path UniversalQuickCreateSolution.zip --async
```

**Result:**
- ✅ Solution imported successfully
- ✅ Version: 1.0 (internal PCF version: 1.0.2)
- ✅ Managed: False (Unmanaged for development)

---

### 4. Updated Documentation

**File:** `QUICK-CREATE-CONFIGURATION-GUIDE.md`

**Updated Step 4:** Now instructs users to bind to `sprk_filename` instead of `sprk_fileuploadmetadata`

---

## How to Configure the Form Now

### Step 1: Add the File Name Field

1. Open Quick Create: Document form in form designer
2. From **Table columns** pane, find **sprk_filename** (File Name)
3. Drag it onto the form (e.g., in "Upload File" section)

### Step 2: Add the PCF Control to the Field

1. Click on the **sprk_filename** field on the form
2. Click **+ Component** (right panel)
3. Search for **Universal Quick Create**
4. Select it and click **Add**
5. Configure control properties:
   - **enableFileUpload:** Yes
   - **allowMultipleFiles:** Yes
   - **sdapApiBaseUrl:** https://localhost:7299/api (or your SDAP BFF URL)
   - **defaultValueMappings:** (leave default or customize)

### Step 3: Configure Control Visibility

1. In control properties, set:
   - **Web:** Universal Quick Create (your custom control)
   - **Phone:** Universal Quick Create
   - **Tablet:** Universal Quick Create

### Step 4: Save and Publish

1. Click **Save**
2. Click **Publish**
3. Test by opening Quick Create from Documents subgrid

---

## Verification Steps

After configuration:

1. **Open Quick Create form** from a Matter's Documents subgrid
2. **Verify PCF control loads** (you should see the file upload UI)
3. **Click "+ Add File"** button
4. **Select files** and verify they appear in the list
5. **Verify button updates:** "Save and Create X Documents"
6. **Upload files** and verify records created

---

## Expected Field Behavior

The `sprk_filename` field:
- **Before upload:** Empty (or hidden from user)
- **During upload:** Control manages file selection
- **After upload:** Automatically populated with the uploaded file's name

**Note:** The field is used for PCF binding requirement only. The actual file metadata (name, size, URL, etc.) is populated in separate fields:
- `sprk_filename` - File name
- `sprk_filesize` - File size in bytes
- `sprk_sharepointurl` - SharePoint URL
- `sprk_driveitemid` - Drive item ID
- `sprk_graphdriveid` - Graph drive ID
- `sprk_graphitemid` - Graph item ID

---

## Benefits of Using sprk_filename

✅ **Already exists** - No need to create new field
✅ **Correct type** - Single Line Text (compatible with PCF)
✅ **Properly defined** - In Entity.xml, part of solution
✅ **Appropriate length** - 1000 characters (sufficient for file names)
✅ **Used for actual data** - Stores file name after upload (dual purpose)

---

## Testing Checklist

- [x] PCF manifest updated to `boundField` property
- [x] Version incremented to 1.0.2
- [x] PCF control built successfully
- [x] Solution built as Unmanaged
- [x] Solution deployed to Spaarke Dev 1
- [x] Documentation updated
- [ ] Quick Create form configured with sprk_filename field
- [ ] PCF control added to sprk_filename field
- [ ] Form published
- [ ] End-to-end test: File upload creates Document records

---

## Next Steps

1. **User Action:** Configure Quick Create form using updated instructions
2. **Bind control to:** `sprk_filename` field (NOT `sprk_fileuploadmetadata`)
3. **Test:** Upload files and verify Document records created
4. **Report:** Any issues or success

---

## Summary

**Problem:** Field type mismatch prevented PCF binding
**Root Cause:** Non-existent or incorrectly typed `sprk_fileuploadmetadata` field
**Solution:** Changed manifest to bind to existing `sprk_filename` field
**Status:** Solution redeployed (v1.0.2), ready for form configuration
**User Action Required:** Configure Quick Create form with `sprk_filename` field

---

**Solution Version:** 1.0 (PCF Control: 1.0.2)
**Environment:** Spaarke Dev 1
**Deployment:** Unmanaged
**Ready for:** Quick Create form configuration and testing
