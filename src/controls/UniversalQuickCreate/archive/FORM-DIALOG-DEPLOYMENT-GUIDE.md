# Form Dialog Deployment Guide
## Universal Document Upload - Form Dialog Approach v2.1.0

This guide provides step-by-step instructions for deploying the Universal Document Upload control using the Form Dialog architecture.

---

## Architecture Overview

**Form Dialog Approach** (v2.1.0+):
```
Subgrid Button → Xrm.Navigation.openForm() → Form Dialog (sprk_uploadcontext)
                                                ↓
                                           PCF Control (bound properties)
                                                ↓
                                           Xrm.WebApi (unlimited records)
                                                ↓
                                           Document Creation + SPE Upload
```

**Key Benefits**:
- ✅ Fully automated deployment (pure XML)
- ✅ Pro-code compatible (no Canvas Studio required)
- ✅ Better CI/CD (standard solution packaging)
- ✅ Xrm.WebApi access (unlimited record creation)
- ✅ Easier debugging (standard form tools)

---

## Components

### 1. Utility Entity: `sprk_uploadcontext`
**Purpose**: Utility entity with hidden fields for parameter passing
**Location**: `src/Entities/sprk_uploadcontext/`

**Files**:
- `Entity.xml` - Entity definition
- `Fields/sprk_name.xml` - Primary name field
- `Fields/sprk_parententityname.xml` - Parent entity logical name
- `Fields/sprk_parentrecordid.xml` - Parent record GUID
- `Fields/sprk_containerid.xml` - SharePoint Container ID
- `Fields/sprk_parentdisplayname.xml` - Display name for UI header
- `FormXml/UploadDialog.xml` - Form Dialog with PCF control

### 2. PCF Control: `UniversalDocumentUpload` v2.0.1
**Purpose**: React UI for multi-file upload with SPE integration
**Location**: `src/controls/UniversalQuickCreate/UniversalQuickCreate/`

**Key Files**:
- `ControlManifest.Input.xml` - Updated to `usage="bound"` for form binding
- `UniversalDocumentUploadPCF.ts` - Main control (no changes needed)
- `services/DocumentRecordService.ts` - Uses Xrm.WebApi (no changes needed)

### 3. Web Resource: `sprk_subgrid_commands.js` v2.1.0
**Purpose**: Ribbon command button logic
**Location**: `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/`

**Updated**: Uses `Xrm.Navigation.openForm()` instead of `navigateTo()`

### 4. Ribbon Button: Document Entity
**Purpose**: "Quick Create: Document" button on Documents subgrid
**Location**: `src/Entities/sprk_Document/RibbonDiff.xml`

---

## Deployment Steps

### Prerequisites
- PAC CLI installed and authenticated
- Power Platform environment URL
- Publisher prefix: `sprk`

### Step 1: Deploy Utility Entity

The utility entity must be deployed first as the PCF control will reference it in the form.

**Option A: Using Solution Packager** (Recommended for CI/CD):

```bash
# Navigate to solution directory
cd c:\code_files\spaarke\src

# Create solution package with new entity
pac solution pack --folder . --zipfile ..\UniversalQuickCreate_v2.1.0.zip

# Import to Dataverse
pac solution import --path ..\UniversalQuickCreate_v2.1.0.zip
```

**Option B: Manual Import via Power Apps UI**:

1. Open Power Apps (https://make.powerapps.com)
2. Select your environment
3. Navigate to **Solutions**
4. Click **Import** → Select `UniversalQuickCreate_v2.1.0.zip`
5. Follow import wizard
6. **Publish All Customizations**

**Verify Entity Deployment**:
```bash
# List entities to verify sprk_uploadcontext exists
pac data list-entities --filter "sprk_uploadcontext"
```

### Step 2: Update and Deploy PCF Control

The PCF control manifest has been updated to use `usage="bound"` instead of `usage="input"`.

```bash
# Navigate to PCF control directory
cd c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate

# Build PCF control
npm run build

# Deploy to Dataverse (use publisher prefix)
pac pcf push --publisher-prefix sprk
```

**Expected Output**:
```
Uploading control 'UniversalDocumentUpload' version '2.0.1'
Successfully pushed control to Dataverse
Control ID: sprk_Spaarke.Controls.UniversalDocumentUpload
```

**Note**: Version changed from 2.0.0 → 2.0.1 to reflect form binding changes.

### Step 3: Update Web Resource

The command button script has been updated to use `Xrm.Navigation.openForm()`.

**Deploy via Solution**:

The Web Resource will be included in the solution package from Step 1. If you need to update it separately:

```bash
# Use the deployment script
cd c:\code_files\spaarke\scripts
bash deploy-all-components.sh
```

**Or manually**:
1. Open Power Apps **Solutions**
2. Open your solution (e.g., "Spaarke Core" or "UniversalQuickCreate")
3. Navigate to **Web Resources**
4. Upload `subgrid_commands.js` as `sprk_subgrid_commands`
5. **Publish**

### Step 4: Configure Form Dialog

After deploying the entity, you need to ensure the form is properly configured.

**Verify Form Exists**:
1. Power Apps → Solutions → Open solution
2. Open `sprk_uploadcontext` entity
3. Navigate to **Forms**
4. You should see "Upload Documents" form

**If form doesn't exist**, the `FormXml/UploadDialog.xml` may not have been imported. You'll need to:

1. Open the entity in the form designer
2. Create a new **Main Form** named "Upload Documents"
3. Add the 4 hidden fields to the form (make section invisible):
   - sprk_parententityname
   - sprk_parentrecordid
   - sprk_containerid
   - sprk_parentdisplayname
4. Add a new section for the PCF control
5. Click **Add Component** → Select "UniversalDocumentUpload"
6. Bind PCF properties to form fields:
   - `parentEntityName` → `sprk_parententityname`
   - `parentRecordId` → `sprk_parentrecordid`
   - `containerId` → `sprk_containerid`
   - `parentDisplayName` → `sprk_parentdisplayname`
7. Save and Publish

**Get Form GUID**:
```bash
# Query for the form ID
pac data query --entity savedquery --filter "name eq 'Upload Documents'"
```

**Update Web Resource with Form ID** (Optional but recommended):

Edit `subgrid_commands.js` line ~299:
```javascript
const formOptions = {
    entityName: "sprk_uploadcontext",
    formId: "{YOUR-FORM-GUID-HERE}",  // Add this line
    openInNewWindow: false,
    windowPosition: 1,
    width: 600,
    height: 700
};
```

### Step 5: Test End-to-End Flow

**Test on Matter Entity**:

1. Open an existing **Matter** record with a `sprk_containerid` value
2. Scroll to **Documents** subgrid
3. Click **"Quick Create: Document"** button
4. **Expected**: Form Dialog opens with PCF control visible
5. **Verify**: Console shows parameters populated correctly:
   ```
   [Spaarke] AddMultipleDocuments: Starting v2.1.0 - FORM DIALOG APPROACH
   [Spaarke] Parent Entity: sprk_matter
   [Spaarke] Parent Record ID: {guid}
   [Spaarke] Container ID: {container-guid}
   [Spaarke] Form Options: {entityName: "sprk_uploadcontext", ...}
   [Spaarke] Form Parameters: {sprk_parententityname: "sprk_matter", ...}
   ```
6. Select files → Fill metadata → Click **Upload**
7. **Expected**: Files upload to SPE, Document records created
8. **Verify**: Subgrid refreshes showing new documents

**Troubleshooting**:

| Issue | Cause | Solution |
|-------|-------|----------|
| Form doesn't open | Entity not deployed | Deploy sprk_uploadcontext entity |
| PCF control shows "Missing parameters" | Form binding incorrect | Verify property bindings in form designer |
| Button doesn't appear | Ribbon not deployed | Re-deploy solution and publish |
| "Entity not configured" error | Container ID missing | Add sprk_containerid field to parent record |
| Upload fails | SDAP API not configured | Verify Spe.Bff.Api deployed and accessible |

---

## Comparison: Custom Page vs Form Dialog

| Aspect | Custom Page (v2.0.0) | Form Dialog (v2.1.0) |
|--------|---------------------|---------------------|
| **Deployment** | ❌ Manual Canvas Studio | ✅ Fully automated XML |
| **CI/CD** | ❌ Not pro-code compatible | ✅ Standard solution packaging |
| **Parameter Passing** | `navigateTo()` data object | `openForm()` formParameters |
| **PCF Property Binding** | `usage="input"` | `usage="bound"` |
| **Xrm.WebApi Access** | ✅ Available | ✅ Available |
| **Unlimited Record Creation** | ✅ Yes | ✅ Yes |
| **Debugging** | ⚠️ Limited tools | ✅ Standard form debugging |
| **Automation Issues** | ❌ Yes (Custom Page creation) | ✅ None |

**Recommendation**: Use **Form Dialog (v2.1.0)** for all new deployments.

---

## Migration from Custom Page (v2.0.0)

If you previously deployed v2.0.0 with Custom Page:

1. **Custom Page is NOT required** - Can be deleted if it exists
2. **PCF Control** - Needs minor update (usage="bound"), deploy v2.0.1
3. **Web Resource** - Update to v2.1.0 (openForm instead of navigateTo)
4. **New Entity** - Deploy sprk_uploadcontext
5. **Ribbon Button** - No changes needed

**Migration Steps**:
1. Follow Step 1 (Deploy Utility Entity)
2. Follow Step 2 (Update PCF to v2.0.1)
3. Follow Step 3 (Update Web Resource to v2.1.0)
4. Follow Step 4 (Configure Form Dialog)
5. Test
6. Delete Custom Page if it exists (optional cleanup)

---

## Next Steps

### Add Support for Additional Entities

To enable document upload for new parent entities (e.g., `sprk_case`):

1. **Update Entity Configuration** in `subgrid_commands.js`:
   ```javascript
   const ENTITY_CONFIGURATIONS = {
       // ... existing entities
       "sprk_case": {
           entityLogicalName: "sprk_case",
           containerIdField: "sprk_containerid",
           displayNameFields: ["sprk_casenumber", "sprk_name"],
           entityDisplayName: "Case"
       }
   };
   ```

2. **Update PCF Configuration** in `EntityDocumentConfig.ts`:
   ```typescript
   export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
       // ... existing entities
       'sprk_case': {
           entityName: 'sprk_case',
           lookupFieldName: 'sprk_case',
           containerIdField: 'sprk_containerid',
           displayNameField: 'sprk_casenumber',
           entitySetName: 'sprk_cases'
       }
   };
   ```

3. **Ensure parent entity has**:
   - `sprk_containerid` field (stores SharePoint Container ID)
   - Relationship to `sprk_document` entity

4. **Deploy updates** and test

---

## Support and Resources

- **Architecture Decision**: `dev/projects/quickcreate_pcf_component/ADR-CUSTOM-PAGE-PLUS-PCF.md`
- **Form Dialog Rationale**: `dev/projects/quickcreate_pcf_component/QUICK-CREATE-DIALOG-APPROACH.md`
- **PCF Source**: `src/controls/UniversalQuickCreate/UniversalQuickCreate/`
- **Entity XML**: `src/Entities/sprk_uploadcontext/`
- **Deployment Scripts**: `scripts/deploy-all-components.sh`

---

## Version History

- **v2.1.0** (2025-10-11): Form Dialog approach with sprk_uploadcontext utility entity
- **v2.0.0** (2025-10-10): Custom Page approach (deprecated due to automation issues)

---

## Success Criteria

✅ sprk_uploadcontext entity deployed
✅ PCF control v2.0.1 deployed
✅ Web Resource v2.1.0 deployed
✅ Form Dialog configured with PCF control
✅ Ribbon button appears on Documents subgrid
✅ Clicking button opens Form Dialog (not Custom Page)
✅ PCF control receives parameters from form fields
✅ Files upload to SharePoint Embedded successfully
✅ Multiple Document records created in Dataverse
✅ Subgrid refreshes showing new documents
✅ Works across all configured entities (Matter, Project, Invoice, Account, Contact)

---

**End of Deployment Guide**
