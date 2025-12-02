# Work Item 1: Update Control Manifest

**Estimated Time:** 30 minutes
**Prerequisites:** None
**Status:** Ready to Start

---

## Objective

Update the PCF control manifest to support field binding instead of dataset binding.

---

## Context

The original control used `data-set` binding (for grids). We need **field binding** to work with Quick Create forms.

### What We're Removing:
- Dataset binding (for grids)
- defaultValueMappings parameter (field inheritance - out of scope)

### What We're Adding:
- Field binding for `sprk_fileuploadmetadata`
- Simpler parameter set

---

## File to Modify

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`

---

## Current Manifest (Simplified View)

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         control-type="standard">

    <data-set name="dataset" ...>  <!-- ❌ REMOVE THIS -->
    </data-set>

    <property name="defaultValueMappings" .../>  <!-- ❌ REMOVE THIS -->
    <property name="enableFileUpload" .../>
    <property name="sdapApiBaseUrl" .../>
</control>
```

---

## New Manifest (Target)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="SpeFileUpload"
           version="2.0.0"
           display-name-key="SPE_File_Upload"
           description-key="Upload files to SharePoint Embedded"
           control-type="standard">

    <!-- ✅ NEW: Bound field for SPE metadata -->
    <property name="speMetadata"
              display-name-key="SPE_Metadata"
              description-key="SharePoint Embedded file metadata (JSON)"
              of-type="Multiple"
              usage="bound"
              required="false" />

    <!-- ✅ KEEP: SDAP API URL -->
    <property name="sdapApiBaseUrl"
              display-name-key="SDAP_API_Base_URL"
              description-key="Base URL for SDAP BFF API"
              of-type="SingleLine.Text"
              usage="input"
              required="true"
              default-value="https://localhost:7299/api" />

    <!-- ✅ KEEP: Allow multiple files -->
    <property name="allowMultipleFiles"
              display-name-key="Allow_Multiple_Files"
              description-key="Allow multiple file uploads"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <!-- ✅ NEW: Container ID (from form field) -->
    <property name="containerid"
              display-name-key="Container_ID"
              description-key="SharePoint Container ID (from form field)"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/UniversalQuickCreate.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

---

## Step-by-Step Instructions

### Step 1: Open File

```bash
# Navigate to control folder
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate

# Open manifest file
code ControlManifest.Input.xml
```

---

### Step 2: Update Control Info

**Find:**
```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
```

**Replace with:**
```xml
<control namespace="Spaarke.Controls"
         constructor="SpeFileUpload"
         version="2.0.0"
         display-name-key="SPE_File_Upload"
         description-key="Upload files to SharePoint Embedded"
```

**Why:** New version, clearer name

---

### Step 3: Remove Dataset Binding

**Find and DELETE:**
```xml
<data-set name="dataset" display-name-key="Dataset_Display_Key">
  <property-set name="field" display-name-key="Field_Display_Key"
                of-type="SingleLine.Text" usage="bound" required="false" />
</data-set>
```

**Why:** Quick Create uses field binding, not dataset

---

### Step 4: Remove defaultValueMappings

**Find and DELETE:**
```xml
<property name="defaultValueMappings"
          display-name-key="Default_Value_Mappings"
          description-key="JSON mapping of parent fields to child default values"
          of-type="SingleLine.Text"
          usage="input"
          required="false"
          default-value='{"sprk_matter":{"sprk_containerid":"sprk_containerid"}}' />
```

**Why:** Field inheritance out of scope for this sprint

---

### Step 5: Add speMetadata Property

**Add after `<control>` tag:**
```xml
<!-- Bound field: SPE metadata JSON -->
<property name="speMetadata"
          display-name-key="SPE_Metadata"
          description-key="SharePoint Embedded file metadata (JSON)"
          of-type="Multiple"
          usage="bound"
          required="false" />
```

**Why:** This is the field the control binds to (`sprk_fileuploadmetadata`)

---

### Step 6: Update enableFileUpload

**Find:**
```xml
<property name="enableFileUpload"
          display-name-key="Enable_File_Upload"
          description-key="Enable file upload field (for Documents)"
          of-type="TwoOptions"
          usage="input"
          required="false"
          default-value="true" />
```

**Replace with:**
```xml
<property name="allowMultipleFiles"
          display-name-key="Allow_Multiple_Files"
          description-key="Allow multiple file uploads"
          of-type="TwoOptions"
          usage="input"
          required="false"
          default-value="true" />
```

**Why:** Clearer name, file upload always enabled

---

### Step 7: Add containerid Property

**Add after `allowMultipleFiles`:**
```xml
<!-- Container ID parameter (can be bound to form field) -->
<property name="containerid"
          display-name-key="Container_ID"
          description-key="SharePoint Container ID"
          of-type="SingleLine.Text"
          usage="input"
          required="false" />
```

**Why:** Control needs Container ID for upload

---

### Step 8: Keep sdapApiBaseUrl

**Find (KEEP AS-IS):**
```xml
<property name="sdapApiBaseUrl"
          display-name-key="SDAP_API_Base_URL"
          description-key="Base URL for SDAP BFF API"
          of-type="SingleLine.Text"
          usage="input"
          required="false"
          default-value="https://localhost:7299/api" />
```

**Why:** Still needed for API calls

---

### Step 9: Verify Resources Section

**Ensure this exists:**
```xml
<resources>
  <code path="index.ts" order="1"/>
  <css path="css/UniversalQuickCreate.css" order="1" />
</resources>

<feature-usage>
  <uses-feature name="WebAPI" required="true" />
  <uses-feature name="Utility" required="true" />
</feature-usage>
```

---

## Verification

After making changes, the manifest should have:

✅ **Has:**
- `speMetadata` property (bound)
- `sdapApiBaseUrl` property
- `allowMultipleFiles` property
- `containerid` property
- `resources` section
- `feature-usage` section

❌ **Does NOT have:**
- `data-set` element
- `defaultValueMappings` property
- `enableFileUpload` property (renamed to `allowMultipleFiles`)

---

## Generated Types

After updating manifest, regenerate types:

```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build
```

This will update `generated/ManifestTypes.d.ts`

---

## Complete Manifest File

<details>
<summary>Click to see full file</summary>

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="SpeFileUpload"
           version="2.0.0"
           display-name-key="SPE_File_Upload"
           description-key="Upload files to SharePoint Embedded"
           control-type="standard">

    <external-service-usage enabled="false">
    </external-service-usage>

    <!-- Bound field: SPE metadata JSON -->
    <property name="speMetadata"
              display-name-key="SPE_Metadata"
              description-key="SharePoint Embedded file metadata (JSON)"
              of-type="Multiple"
              usage="bound"
              required="false" />

    <!-- Configuration: SDAP API Base URL -->
    <property name="sdapApiBaseUrl"
              display-name-key="SDAP_API_Base_URL"
              description-key="Base URL for SDAP BFF API"
              of-type="SingleLine.Text"
              usage="input"
              required="true"
              default-value="https://localhost:7299/api" />

    <!-- Configuration: Allow multiple files -->
    <property name="allowMultipleFiles"
              display-name-key="Allow_Multiple_Files"
              description-key="Allow multiple file uploads"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <!-- Configuration: Container ID -->
    <property name="containerid"
              display-name-key="Container_ID"
              description-key="SharePoint Container ID"
              of-type="SingleLine.Text"
              usage="input"
              required="false" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/UniversalQuickCreate.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

</details>

---

## Next Steps

After completing this work item:

1. ✅ Verify manifest is valid XML
2. ✅ Run `npm run build` to regenerate types
3. ✅ Move to Work Item 2: Create FileUploadPCF.ts

---

## Troubleshooting

### Error: Invalid XML

**Check:**
- All tags properly closed
- No missing quotes
- Proper nesting

**Fix:**
Use XML validator or VS Code XML extension

---

### Error: Build fails after manifest change

**Check:**
- All property names match what's used in code
- Types regenerated (`npm run build`)

**Fix:**
Delete `out/` folder and rebuild

---

**Status:** Ready for implementation
**Estimated Time:** 30 minutes
**Next:** Work Item 2
