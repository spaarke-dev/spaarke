# Work Item 3: Update Control Manifest

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 1 hour
**Prerequisites:** Work Items 1-2 completed
**Status:** Ready to Start

---

## Objective

Update PCF control manifest to configure field binding, properties, and required features for Quick Create compatibility.

---

## Context

Quick Create forms only support **field-level controls**, not dataset controls. The manifest must:
1. Bind to a single field (`sprk_fileuploadmetadata`)
2. Define configuration properties (API URL, multi-file support)
3. Declare required features (WebAPI, Utility)
4. Reference React and CSS resources

**Result:** Control appears in Quick Create form configuration and runs with correct settings.

---

## Implementation Steps

### Step 1: Review Current Manifest

File: [src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml](src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml)

Current binding is to a dataset (lines 12-15). This won't work in Quick Create.

---

### Step 2: Change to Field Binding

Replace dataset binding with single field binding:

```xml
<!-- Binding field for Quick Create compatibility -->
<property name="speMetadata"
          display-name-key="SPE_Metadata_Field"
          description-key="Field to bind PCF control (value not used)"
          of-type="SingleLine.Text"
          usage="bound"
          required="true" />
```

**Key Points:**
- `usage="bound"` makes it field-level control
- `required="true"` ensures it's bound to a field
- Field value is NOT used (PCF creates records directly)
- Binding is just to satisfy Quick Create technical requirement

---

### Step 3: Configure Properties

Keep these existing properties (currently at lines 18-42):

1. **defaultValueMappings** (optional, for future field inheritance)
   - Type: SingleLine.Text
   - Default: JSON mapping object
   - Usage: Currently not used (future sprint)

2. **enableFileUpload** (keep but always true in Quick Create)
   - Type: TwoOptions
   - Default: true
   - Usage: Show/hide file upload UI

3. **sdapApiBaseUrl** (CRITICAL - required for SPE uploads)
   - Type: SingleLine.Text
   - Default: "https://localhost:7299/api"
   - Usage: SDAP BFF API endpoint

Add new property for multi-file support:

```xml
<!-- Configuration: Allow multiple file selection -->
<property name="allowMultipleFiles"
          display-name-key="Allow_Multiple_Files"
          description-key="Allow users to select multiple files at once"
          of-type="TwoOptions"
          usage="input"
          required="false"
          default-value="true" />
```

---

### Step 4: Verify Resources Section

Current resources (lines 44-47) should remain:

```xml
<resources>
  <code path="index.ts" order="1"/>
  <css path="css/UniversalQuickCreate.css" order="1" />
</resources>
```

**Note:** React and Fluent UI are bundled by webpack in index.ts.

---

### Step 5: Verify Feature Usage

Current features (lines 49-52) should remain:

```xml
<feature-usage>
  <uses-feature name="WebAPI" required="true" />
  <uses-feature name="Utility" required="true" />
</feature-usage>
```

**Why needed:**
- `WebAPI`: For `context.webAPI.createRecord()` to create Document records
- `Utility`: For `context.utils` (logging, formatting)

---

### Step 6: Update Control Metadata

At top of manifest (lines 3-6), verify control type:

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
         display-name-key="Universal_Quick_Create_Display_Key"
         description-key="Universal_Quick_Create_Desc_Key"
         control-type="standard">
```

**Important:** `control-type="standard"` allows use in Quick Create.

---

## Final Manifest Structure

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls"
           constructor="UniversalQuickCreate"
           version="1.0.0"
           display-name-key="Universal_Quick_Create_Display_Key"
           description-key="Universal_Quick_Create_Desc_Key"
           control-type="standard">

    <external-service-usage enabled="false" />

    <!-- Field binding for Quick Create -->
    <property name="speMetadata"
              display-name-key="SPE_Metadata_Field"
              description-key="Field to bind PCF control (value not used)"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Configuration Properties -->
    <property name="sdapApiBaseUrl" ... />
    <property name="allowMultipleFiles" ... />
    <property name="enableFileUpload" ... />
    <property name="defaultValueMappings" ... />

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

## Testing Checklist

After updating manifest:

- [ ] Remove dataset binding, add field binding (`speMetadata`)
- [ ] Add `allowMultipleFiles` property
- [ ] Verify all existing properties retained
- [ ] Verify `WebAPI` and `Utility` features declared
- [ ] Rebuild solution: `npm run build`
- [ ] No build errors
- [ ] Solution packages successfully: `pac solution pack`
- [ ] Control appears in "Add Control" list for text fields in Quick Create

---

## Verification Commands

```bash
# 1. Verify manifest syntax
grep "property name=\"speMetadata\"" ControlManifest.Input.xml

# 2. Rebuild control
npm run build

# 3. Check for build errors
# (should complete with no errors)

# 4. Package solution
cd ../../../solutions/SpaarkeControls
pac solution pack --zipfile SpaarkeControls.zip
```

---

## Common Issues

### Issue: Control not in "Add Control" list
**Cause:** Field binding missing or incorrect type
**Fix:** Verify `usage="bound"` and `required="true"`

### Issue: Build errors after manifest change
**Cause:** TypeScript expects different input signature
**Fix:** Update `IInputs` interface in generated types

### Issue: Properties not visible in configuration
**Cause:** Property keys not in resx file
**Fix:** Add keys to `UniversalQuickCreate.resx` (auto-generated on build)

---

## Integration Notes

### Accessing Properties in Code

In `UniversalQuickCreatePCF.ts`:

```typescript
public init(context: ComponentFramework.Context<IInputs>): void {
    // Access configuration properties
    const apiBaseUrl = context.parameters.sdapApiBaseUrl.raw || 'https://localhost:7299/api';
    const allowMultiple = context.parameters.allowMultipleFiles.raw !== false;

    // Bound field (NOT used, just for binding)
    const boundFieldValue = context.parameters.speMetadata.raw;
    // We ignore this value - PCF creates records directly
}
```

---

## Next Steps

After completing manifest update:
1. Rebuild control: `npm run build`
2. Proceed to **Work Item 4** - File Upload UI
3. Build and deploy in **Work Item 8**

---

**Status:** Ready for implementation
**Time:** 1 hour
**Dependencies:** None (can do in parallel with Work Items 1-2)
**Next:** Work Item 4 - File Upload UI Component
