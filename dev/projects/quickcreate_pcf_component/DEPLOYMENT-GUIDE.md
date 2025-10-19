# Deployment Guide: Universal Document Upload PCF v2.0.0.0

## Overview

This guide covers packaging, deployment, and verification of the Universal Document Upload PCF control migrated to Custom Page architecture.

---

## Version Information

**Version:** 2.0.0.0
**Build Date:** 2025-01-10
**Type:** Major Release (Breaking Change - migrated from Quick Create to Custom Page)

**Version Format:** `Major.Minor.Patch.Build`
- **Major (2):** Breaking change - Custom Page migration
- **Minor (0):** Initial release of new architecture
- **Patch (0):** No patches yet
- **Build (0):** Initial build

---

## Solution Components

### Package Contents
```
SpaarkeDocumentUpload_2_0_0_0.zip
│
├── Controls/
│   └── sprk_Spaarke.Controls.UniversalDocumentUploadPCF_2_0_0_0.xml
│
├── CustomPages/
│   └── sprk_DocumentUploadDialog_2_0_0_0.json
│
├── WebResources/
│   ├── sprk_subgrid_commands_2_0_0_0.js
│   └── sprk_entity_document_config_2_0_0_0.json
│
├── RibbonCustomizations/
│   ├── sprk_matter_document_button.xml
│   ├── sprk_project_document_button.xml
│   ├── sprk_invoice_document_button.xml
│   ├── sprk_account_document_button.xml
│   └── sprk_contact_document_button.xml
│
└── solution.xml (Version: 2.0.0.0)
```

---

## Pre-Deployment Checklist

### Environment Requirements
- [ ] Power Platform environment (Development/UAT/Production)
- [ ] System Administrator or System Customizer role
- [ ] Dataverse API enabled
- [ ] Modern browser (Edge, Chrome - for testing)

### Schema Prerequisites
- [ ] `sprk_document` entity exists
- [ ] Document entity has lookup fields for all parent types:
  - [ ] `sprk_matter` (Lookup to sprk_matter)
  - [ ] `sprk_project` (Lookup to sprk_project)
  - [ ] `sprk_invoice` (Lookup to sprk_invoice)
  - [ ] `sprk_account` (Lookup to account)
  - [ ] `sprk_contact` (Lookup to contact)
- [ ] Parent entities have `sprk_containerid` field (Single Line Text)

### Backup
- [ ] Export current solution (if upgrading)
- [ ] Document current configuration
- [ ] Test in non-production environment first

---

## Deployment Steps

### Step 1: Import Solution

1. Navigate to **Power Apps** → **Solutions**
2. Click **Import solution**
3. Browse and select `SpaarkeDocumentUpload_2_0_0_0.zip`
4. Click **Next**
5. Review components list
6. Click **Import**
7. Wait for import to complete (~2-5 minutes)

**Verification:**
```
✅ Solution imported successfully
✅ No import errors in log
✅ All components visible in solution
```

---

### Step 2: Publish Customizations

1. In the imported solution, click **Publish all customizations**
2. Wait for publish to complete (~1-2 minutes)

**Verification:**
```bash
# Check publish status in browser console
Xrm.Utility.getGlobalContext().getVersion()
# Should show current Dataverse version
```

---

### Step 3: Configure Parent Entities

For **each parent entity** (Matter, Project, Invoice, Account, Contact):

#### 3.1: Add Container ID Field (if not exists)

1. Navigate to **Settings** → **Customizations** → **Customize the System**
2. Expand **Entities** → Select parent entity (e.g., **sprk_matter**)
3. Select **Fields** → **New**
4. Configure:
   - **Display Name:** Container ID
   - **Name:** sprk_containerid
   - **Data Type:** Single Line of Text
   - **Maximum Length:** 500
   - **Required:** Optional
5. **Save and Close**

#### 3.2: Add Documents Subgrid to Form

1. Open parent entity main form (e.g., Matter Information)
2. Navigate to **Documents** tab (or create new tab)
3. **Insert** → **Subgrid**
4. Configure:
   - **Name:** subgrid_documents
   - **Label:** Documents
   - **Entity:** Document (sprk_document)
   - **Default View:** Active Documents
   - **Parent:** Current Record
5. **Save** → **Publish**

#### 3.3: Add Command Button to Subgrid

**Option A: Using Ribbon Workbench (Recommended)**

1. Open solution in **Ribbon Workbench**
2. Navigate to **sprk_document** → **SubGrid**
3. Add new button:
   - **Label:** New Document
   - **Icon:** Upload icon (16x16)
   - **Command:** `sprk.commands.openDocumentUploadDialog`
   - **Enable Rule:** At least one record selected in parent
4. **Publish** ribbon customization

**Option B: Manual XML (Advanced)**

Add this to ribbon customization XML:
```xml
<CommandDefinition Id="sprk.commands.openDocumentUploadDialog">
  <EnableRules>
    <EnableRule Id="Mscrm.Enabled" />
  </EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction FunctionName="openDocumentUploadDialog" Library="sprk_subgrid_commands_2_0_0_0.js">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

---

### Step 4: Verify Custom Page

1. Navigate to **Solutions** → **SpaarkeDocumentUpload**
2. Select **Custom Pages**
3. Verify `sprk_DocumentUploadDialog` exists
4. Click to open in designer
5. Verify PCF control is bound
6. **Publish** custom page

---

### Step 5: Test Deployment

#### Test 1: Open Dialog

1. Open a Matter record (or Project/Invoice/Account/Contact)
2. Navigate to **Documents** subgrid
3. Click **New Document** button
4. Verify dialog opens (600px width, 700px height, centered)
5. Verify version in footer: `v2.0.0.0 - Build 2025-01-10`

#### Test 2: Upload Single File

1. Click **Add File** button
2. Select 1 PDF file (< 10MB)
3. Enter optional description
4. Click **Upload & Create**
5. Verify:
   - Progress bar shows "1 of 1"
   - Dialog closes after upload
   - Subgrid refreshes automatically
   - 1 new Document record appears
   - Document is linked to parent Matter
   - File is accessible in SharePoint Embedded

#### Test 3: Upload Multiple Files

1. Click **New Document** again
2. Select 5 files (< 100MB total)
3. Click **Upload & Create**
4. Verify:
   - Progress shows "1 of 5", "2 of 5", etc.
   - All 5 files upload successfully
   - All 5 Document records created
   - All linked to parent Matter

#### Test 4: Validation Errors

1. Try to upload 11 files → Verify error message
2. Try to upload file > 10MB → Verify error message
3. Try to upload .exe file → Verify blocked
4. Try to upload 101MB total → Verify error message

#### Test 5: Cross-Entity

Repeat Test 2 & 3 for:
- [ ] Project entity
- [ ] Invoice entity
- [ ] Account entity
- [ ] Contact entity

---

## Rollback Procedure

If deployment fails or issues are found:

### Step 1: Delete Solution

1. Navigate to **Solutions**
2. Select `SpaarkeDocumentUpload` solution
3. Click **Delete**
4. Confirm deletion

### Step 2: Remove Custom Components

1. Delete custom page: `sprk_DocumentUploadDialog`
2. Delete web resources:
   - `sprk_subgrid_commands_2_0_0_0.js`
   - `sprk_entity_document_config_2_0_0_0.json`
3. Remove command buttons from subgrids (each entity)

### Step 3: Restore Previous Version

1. Import previous solution backup
2. Publish customizations
3. Verify functionality

---

## Post-Deployment Tasks

### Configuration

#### 1. Update Entity Document Config

If adding new parent entity types, update web resource:

**File:** `sprk_entity_document_config_2_0_0_0.json`

```json
{
    "entities": {
        "sprk_newentity": {
            "entityName": "sprk_newentity",
            "lookupFieldName": "sprk_newentity",
            "containerIdField": "sprk_containerid",
            "displayNameField": "sprk_name",
            "entitySetName": "sprk_newentities"
        }
    }
}
```

Then:
1. Publish web resource
2. Clear browser cache
3. Test with new entity

#### 2. Customize Field Labels

Entity-specific field labels can be customized:

1. Open `sprk_document` entity
2. Edit field labels:
   - `sprk_documentname` → "Document Name"
   - `sprk_description` → "Description"
3. Update form layouts
4. Publish

---

## Monitoring & Troubleshooting

### Check Version

**In Browser Console:**
```javascript
// Check PCF control version
document.querySelector('[data-control-name="UniversalDocumentUploadPCF"]')?.getAttribute('data-version');

// Check custom page version
Xrm.Navigation.getCurrentPageType() // Should be 'custom'
```

**In Dialog Footer:**
Look for version text: `v2.0.0.0 - Build 2025-01-10`

### Common Issues

#### Issue 1: Dialog Doesn't Open

**Symptom:** Click button, nothing happens

**Troubleshooting:**
```javascript
// Check console for errors
// Common errors:
// - "navigateTo is not a function" → Xrm API not loaded
// - "Custom page not found" → Page not published
// - "Insufficient permissions" → User lacks access

// Test navigation manually:
Xrm.Navigation.navigateTo({
    pageType: "custom",
    name: "sprk_DocumentUploadDialog",
    data: {
        parentEntityName: "sprk_matter",
        parentRecordId: "{GUID}",
        containerId: "{CONTAINER-ID}"
    }
}, {
    target: 2,
    position: 1
});
```

**Fix:**
- Verify custom page is published
- Check user has read permission on custom page
- Verify `sprk_subgrid_commands.js` is registered

#### Issue 2: File Upload Fails

**Symptom:** Files don't upload to SPE

**Troubleshooting:**
```javascript
// Check MSAL authentication
const authProvider = MsalAuthProvider.getInstance();
console.log('MSAL initialized:', authProvider.isAuthenticated());

// Check token acquisition
const token = await authProvider.getAccessToken();
console.log('Token acquired:', !!token);

// Check BFF API connectivity
fetch('https://spe-api-dev-67e2xz.azurewebsites.net/api/health')
    .then(r => console.log('BFF API reachable:', r.ok));
```

**Fix:**
- Verify MSAL config (client ID, tenant ID)
- Check BFF API is running
- Verify user has SPE permissions
- Check container ID is valid

#### Issue 3: Records Not Created

**Symptom:** Files upload but no Document records appear

**Troubleshooting:**
```javascript
// Check Xrm.WebApi access
console.log('Xrm.WebApi available:', !!Xrm.WebApi);

// Test record creation manually
Xrm.WebApi.createRecord('sprk_document', {
    sprk_documentname: 'Test.pdf',
    sprk_matter: null,
    'sprk_matter@odata.bind': '/sprk_matters(GUID-HERE)'
}).then(
    result => console.log('Record created:', result.id),
    error => console.error('Create failed:', error)
);
```

**Fix:**
- Verify user has Create permission on `sprk_document`
- Check lookup field name matches config
- Verify parent record GUID is valid
- Check OData bind syntax is correct

#### Issue 4: Subgrid Doesn't Refresh

**Symptom:** Records created but subgrid not updated

**Fix:**
```javascript
// Manual refresh
const primaryControl = Xrm.Page; // or get from context
const gridControl = primaryControl.getControl('subgrid_documents');
if (gridControl) {
    gridControl.refresh();
}
```

Update command script to ensure refresh is called in success callback.

---

## Performance Benchmarks

**Expected Performance (100MB, 10 files):**

| Phase | Duration | Notes |
|-------|----------|-------|
| Validation | < 1 sec | Client-side only |
| File Upload | 5-15 sec | Depends on network speed |
| Record Creation | 10-20 sec | 1-2 sec per record, sequential |
| Dialog Close | < 1 sec | Immediate |
| Subgrid Refresh | 1-3 sec | Dataverse query |
| **Total** | **17-40 sec** | End-to-end |

**Acceptable:** < 60 seconds for 10 files, 100MB

---

## Security Checklist

Post-deployment security verification:

- [ ] Only authorized users can create Documents
- [ ] Users can only upload to Matters they own/access
- [ ] File upload uses user's delegated permissions (not app-only)
- [ ] Dangerous file types (.exe, .dll) are blocked
- [ ] No PII is logged to browser console
- [ ] MSAL tokens are stored securely (sessionStorage, not localStorage)
- [ ] BFF API validates user tokens (OAuth2 OBO)
- [ ] Dataverse enforces record-level security

---

## Support & Maintenance

### Version Upgrade Path

**Future Versions:**
- **2.1.0.0:** Add support for new entity (e.g., Contract)
- **2.0.1.0:** Bug fix for file size validation
- **2.0.0.1:** Hotfix for lookup field mapping

**Upgrade Process:**
1. Import new solution (will upgrade existing)
2. Publish customizations
3. Clear browser cache
4. Verify version in dialog footer

### Logs & Diagnostics

**Client-Side Logs:**
```javascript
// Enable verbose logging (browser console)
localStorage.setItem('spaarke_log_level', 'debug');

// View logs
// All logs prefixed with [DocumentUpload]
```

**Server-Side Logs (BFF API):**
```bash
# Azure App Service → Log Stream
# Filter for "DocumentUpload" or "OBO"
```

---

## Appendix: Command Script Registration

### Register Web Resource

```xml
<WebResource>
    <DisplayName>Subgrid Commands - Document Upload</DisplayName>
    <Name>sprk_subgrid_commands_2_0_0_0.js</Name>
    <WebResourceType>3</WebResourceType> <!-- JScript -->
    <Content>[Base64 encoded content]</Content>
</WebResource>
```

### Enable in Ribbon

```xml
<RibbonDiffXml>
    <CustomActions>
        <CustomAction Id="sprk.DocumentUpload.AddButton"
                      Location="Mscrm.SubGrid.sprk_document.MainTab.Management.Controls._children"
                      Sequence="30">
            <CommandUIDefinition>
                <Button Id="sprk.DocumentUpload.Button"
                        Command="sprk.commands.openDocumentUploadDialog"
                        LabelText="New Document"
                        ToolTipTitle="Upload Documents"
                        ToolTipDescription="Upload multiple documents to SharePoint"
                        Image16by16="/_imgs/ribbon/NewRecord_16.png"
                        Image32by32="/_imgs/ribbon/NewRecord_32.png"
                        Sequence="30" />
            </CommandUIDefinition>
        </CustomAction>
    </CustomActions>
</RibbonDiffXml>
```

---

**Deployment Complete!**

**Next Step:** Review [PHASE-1-SETUP.md](./PHASE-1-SETUP.md) to begin implementation.
