# Deployment Package: Universal Document Upload v3.0.0

**Package:** UniversalQuickCreate_3_0_0_0.zip
**Version:** 3.0.0.0
**Created:** 2025-10-21
**Type:** Unmanaged (for DEV/UAT)
**Environment:** SPAARKE DEV 1

---

## Overview

This package contains the Custom Page migration for Universal Document Upload, transitioning from Form Dialog approach to modern Custom Page dialog.

### Architecture Change

**Before (v2.x):**
```
Ribbon Button → Quick Create Form (sprk_uploadcontext) → PCF Control
```

**After (v3.0.0):**
```
Ribbon Button → Custom Page Dialog → PCF Control v3.0.0
```

---

## Package Contents

### 1. PCF Control: Universal Document Upload
- **Name:** sprk_Spaarke.Controls.UniversalDocumentUpload
- **Version:** 3.0.0.0
- **File:** Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/
- **Size:** ~603 KB (bundle.js + CSS)

**Changes in v3.0.0:**
- Added Custom Page mode detection (`isCustomPage()`)
- Added `closeDialog()` method for autonomous dialog closure
- Added `parentContext` parameter handling
- Maintains Phase 7 dynamic metadata discovery
- Maintains autonomous workflow (no form dependency)

**Code Location:** src/controls/UniversalQuickCreate/UniversalQuickCreate/

### 2. Web Resource: Ribbon Command Script
- **Name:** sprk_subgrid_commands.js
- **Version:** 3.0.0
- **File:** WebResources/sprk_subgrid_commandsAECBD6C8-46A6-F011-BBD3-7C1E5217CD7C

**Changes in v3.0.0:**
- Updated `openDocumentUploadDialog()` function
- Changed from `Xrm.Navigation.openForm()` to `Xrm.Navigation.navigateTo()`
- Updated Custom Page reference: sprk_documentuploaddialog_e52db
- Maintains support for 5 entity types (Matter, Project, Invoice, Account, Contact)
- Maintains subgrid auto-refresh
- Maintains error handling

**Code Location:** sprk_subgrid_commands.js (root)

### 3. Additional Web Resources (Existing)
- sprk_DocumentOperations (JavaScript utility)
- sprk_SPRK_Project_Wrench (Image resource)

---

## Components NOT in This Package

### Custom Page (Separate Deployment)
- **Name:** sprk_documentuploaddialog_e52db
- **Display Name:** Document Upload Dialog
- **Status:** Already deployed in SPAARKE DEV 1
- **Location:** Power Apps > Solutions > Default Solution > Pages

**Why Separate:** Custom Page was created directly in Power Apps Maker Portal during Task 1 and exists in Default Solution, not in UniversalQuickCreate solution.

### Ribbon Customizations (Separate Solutions)
- **DocumentRibbons solution:** Contains ribbon for sprk_Document entity
- **MatterRibbons solution:** Contains ribbon for sprk_Matter entity (if applicable)
- **Status:** Already deployed with RibbonDiff.xml configurations

**Why Separate:** Ribbon customizations are managed per-entity in dedicated solutions.

---

## Prerequisites

### Environment Requirements
- ✅ Dataverse environment with Power Apps
- ✅ Custom Pages feature enabled
- ✅ Power Platform CLI (pac) v1.46.1 or higher
- ✅ System Customizer role minimum (System Administrator recommended)

### Dependencies Already Deployed
- ✅ BFF API operational: https://spe-api-dev-67e2xz.azurewebsites.net
- ✅ Phase 7 NavMap endpoints deployed
- ✅ Redis cache configured
- ✅ SharePoint Embedded containers configured
- ✅ Custom Page: sprk_documentuploaddialog_e52db
- ✅ Ribbon buttons configured on Documents subgrid

### No Changes Required
- ✅ Dataverse schema (no entity changes)
- ✅ Security roles (no permission changes)
- ✅ BFF API code (no API changes)
- ✅ Service layer code (no backend changes)

---

## Deployment Steps

### Pre-Deployment Checklist

1. **Verify Prerequisites**
   ```bash
   pac --version  # Should show 1.46.1 or higher
   pac auth list  # Should show target environment
   ```

2. **Backup Current Solution (CRITICAL)**
   ```bash
   pac solution export \
     --name UniversalQuickCreate \
     --path UniversalQuickCreate_BACKUP_$(date +%Y%m%d).zip \
     --managed false
   ```

3. **Verify Custom Page Exists**
   - Open Power Apps Maker Portal: https://make.powerapps.com/
   - Select target environment
   - Go to Solutions → Default Solution → Pages
   - Verify "sprk_documentuploaddialog_e52db" exists
   - If missing: See "Custom Page Manual Deployment" section below

### Deployment: Option A (pac CLI - Recommended)

```bash
# 1. Authenticate
pac auth create --url https://[your-environment].crm.dynamics.com/

# 2. Import Solution
pac solution import \
  --path UniversalQuickCreate_3_0_0_0.zip \
  --async \
  --publish-changes

# 3. Monitor Import
# Watch for "Solution import succeeded" message
# Typical duration: 2-5 minutes

# 4. Publish Customizations
pac solution publish
```

### Deployment: Option B (Power Apps Maker Portal)

1. Navigate to https://make.powerapps.com/
2. Select target environment (DEV, UAT, or PROD)
3. Go to **Solutions**
4. Click **Import solution**
5. Click **Browse** → Select UniversalQuickCreate_3_0_0_0.zip
6. Click **Next**
7. Review components list:
   - sprk_Spaarke.Controls.UniversalDocumentUpload (v3.0.0.0)
   - sprk_subgrid_commands.js
   - (other web resources)
8. Click **Import**
9. Wait for completion (2-5 minutes)
10. Click **Publish all customizations**

---

## Post-Deployment Verification

### Step 1: Verify Solution Import

```bash
pac solution list
```

Look for:
```
UniversalQuickCreate    Universal Quick Create    3.0.0.0    False
```

### Step 2: Verify PCF Control Version

**Option A: pac CLI**
```bash
# Note: pac pcf list may not be available - use Power Apps portal instead
```

**Option B: Power Apps Portal**
1. Solutions → UniversalQuickCreate → Controls
2. Find "Universal Document Upload"
3. Verify version: 3.0.0.0

### Step 3: Verify Web Resource Updated

1. Solutions → UniversalQuickCreate → Web Resources
2. Find "sprk_subgrid_commands"
3. Click to view
4. Verify header comment shows:
   ```javascript
   * @version 3.0.0
   * PURPOSE: Opens Custom Page Dialog for uploading multiple documents
   * ARCHITECTURE: Custom Page dialog approach with PCF control
   ```
5. Verify function `openDocumentUploadDialog` uses:
   ```javascript
   Xrm.Navigation.navigateTo(pageInput, navigationOptions)
   ```

### Step 4: Verify Ribbon Button Exists

1. Open any **Matter** record
2. Scroll to **Documents** subgrid
3. Verify "Upload Documents" or "Add Documents" button is visible
4. **DO NOT CLICK YET** (functional testing comes next)

---

## Functional Testing (Smoke Test)

### Test 1: Basic Upload on Matter

1. **Setup:**
   - Open existing Matter record that has container ID
   - Navigate to Documents subgrid

2. **Execute:**
   - Click "Upload Documents" button
   - Verify Custom Page dialog opens (modal, centered, 800x600px)
   - Verify header shows Matter name
   - Verify PCF control loads successfully

3. **Upload:**
   - Click file input area
   - Select 1 test file (PDF or DOCX, < 10 MB)
   - Click "Upload & Create" button
   - Verify upload progress shows
   - Verify success message appears
   - Verify dialog closes automatically

4. **Validation:**
   - Verify subgrid refreshes automatically
   - Verify new sprk_document record appears
   - Verify lookup field points to Matter
   - Open document record → Verify all fields populated

### Test 2: Error Handling

1. **No Container ID:**
   - Open Matter with NO container ID set
   - Click "Upload Documents"
   - Verify error: "This Matter is not configured for document storage"

2. **User Cancellation:**
   - Open Matter with container ID
   - Click "Upload Documents"
   - Click Cancel or press ESC
   - Verify dialog closes
   - Verify NO error popup appears

3. **Large File:**
   - Try uploading file > 10 MB
   - Verify appropriate error message

### Test 3: Multi-Entity Support

Test on each entity type:
- ✅ sprk_matter
- ✅ sprk_project
- ✅ sprk_invoice
- ✅ account
- ✅ contact

For each:
1. Open record with container ID
2. Click ribbon button
3. Upload 1 file
4. Verify success

### Test 4: Browser Console Verification

1. Open browser developer tools (F12)
2. Go to Console tab
3. Click "Upload Documents" button
4. Verify console logs:
   ```
   [Spaarke] AddMultipleDocuments: Starting v3.0.0 - CUSTOM PAGE DIALOG
   [Spaarke] Opening Custom Page Dialog with parameters:
   [Spaarke] Page Input: {pageType: "custom", name: "sprk_documentuploaddialog_e52db", ...}
   [Spaarke] Navigation Options: {target: 2, position: 1, ...}
   [Spaarke] Custom Page Dialog closed successfully
   [Spaarke] Refreshing subgrid...
   ```
5. Verify NO errors in console

---

## Rollback Procedure

### Critical Issue Detected?

If critical bugs are found during testing:

### Option 1: Restore Backup Solution (Complete Rollback)

```bash
pac solution import \
  --path UniversalQuickCreate_BACKUP_[date].zip \
  --force-overwrite \
  --publish-changes
```

**Duration:** 2-5 minutes
**Impact:** Reverts PCF control and web resources to previous version
**Result:** Users see previous behavior (may be old Quick Create form or earlier version)

### Option 2: Revert Web Resource Only (Faster Partial Rollback)

If only ribbon/navigation is broken:

1. Open Power Apps Maker Portal
2. Solutions → UniversalQuickCreate → Web Resources
3. Find sprk_subgrid_commands
4. Click Edit
5. Replace content with v2.x version (use Quick Create form)
6. Save
7. Publish All Customizations

**Duration:** < 5 minutes
**Impact:** Ribbon uses old approach, PCF v3.0.0 stays but unused
**Result:** Users see old Quick Create form behavior

### Option 3: Disable Ribbon Button (Emergency)

If ribbon button is completely broken:

1. Solutions → DocumentRibbons (or appropriate entity solution)
2. Export → Edit RibbonDiff.xml
3. Comment out or remove ribbon button
4. Re-import
5. Publish

**Duration:** 10-15 minutes
**Impact:** Button disappears, users cannot upload
**Result:** Feature temporarily disabled until fix deployed

---

## Known Issues

### Issue 1: Custom Page in Separate Solution
**Problem:** Custom Page (sprk_documentuploaddialog_e52db) is NOT included in this package

**Reason:** Created directly in Default Solution during Task 1

**Impact:** If deploying to new environment, Custom Page must be deployed separately

**Workaround:** See "Custom Page Manual Deployment" section below

### Issue 2: Ribbon Customizations in Separate Solutions
**Problem:** Ribbon customizations are NOT in this package

**Reason:** Managed in entity-specific solutions (DocumentRibbons, MatterRibbons, etc.)

**Impact:** Ribbon buttons must be deployed separately or already exist in target environment

**Workaround:** Export DocumentRibbons solution and deploy alongside this package

---

## Custom Page Manual Deployment

If deploying to a fresh environment without the Custom Page:

### Option A: Export from DEV and Import

```bash
# In SPAARKE DEV 1:
pac solution export \
  --name Default \
  --include sprk_documentuploaddialog_e52db \
  --path CustomPage_DocumentUpload.zip \
  --managed false

# In Target Environment:
pac solution import \
  --path CustomPage_DocumentUpload.zip \
  --publish-changes
```

### Option B: Recreate in Target Environment

Follow Task 1 instructions:
1. Create new Custom Page: "Document Upload Dialog"
2. Set type: Dialog
3. Add PCF control: sprk_Spaarke.Controls.UniversalDocumentUpload
4. Configure width: 800px, height: 600px
5. Publish

**Note:** Custom Page name suffix may differ (_xxxxx) - update sprk_subgrid_commands.js accordingly

---

## Monitoring & Validation

### Application Insights Queries

Monitor upload activity:

```kusto
traces
| where timestamp > ago(1h)
| where message contains "Document Upload" or message contains "Custom Page"
| order by timestamp desc
```

Monitor errors:

```kusto
exceptions
| where timestamp > ago(1h)
| where outerMessage contains "Upload" or outerMessage contains "Dialog"
| order by timestamp desc
```

### Performance Validation

Compare v2.x vs v3.0.0:
- Dialog open time: Should be similar (~500ms)
- Upload time: No change expected
- Dialog close time: Should be faster (autonomous closure)

---

## Support & Troubleshooting

### Common Issues

**Issue:** "Custom Page not found"
**Solution:** Verify sprk_documentuploaddialog_e52db exists in environment. Check Custom Page name matches exactly (including suffix).

**Issue:** "Control failed to load"
**Solution:** Verify PCF control v3.0.0 is deployed. Clear browser cache. Check Application Insights for errors.

**Issue:** "Ribbon button missing"
**Solution:** Verify DocumentRibbons solution is deployed. Publish all customizations. Hard refresh browser.

**Issue:** "Upload fails with 401 Unauthorized"
**Solution:** Verify BFF API is operational. Check Azure AD authentication. Verify user has permissions.

### Logs & Diagnostics

**Browser Console:**
- Press F12 → Console tab
- Look for [Spaarke] prefixed messages
- Check for errors or warnings

**Network Tab:**
- F12 → Network tab
- Filter: "api" or "dynamics"
- Look for failed requests (red)

**Application Insights:**
- Navigate to: https://portal.azure.com/
- Find spe-api-dev-67e2xz
- Application Insights → Logs
- Run queries from "Monitoring & Validation" section

### Contact

- **Developer:** Ralph Schroeder (ralph.schroeder@spaarke.com)
- **Documentation:** dev/projects/quickcreate_pcf_component/
- **Architecture Docs:** SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md
- **Task Reports:** TASK-1-COMPLETION-REPORT.md, TASK-2-COMPLETION-REPORT.md, TASK-3-COMPLETION-REPORT.md

---

## Files in Package

### Package Structure

```
UniversalQuickCreate_3_0_0_0.zip
├── [Content_Types].xml
├── solution.xml (Version: 3.0.0.0)
├── customizations.xml
├── Controls/
│   └── sprk_Spaarke.Controls.UniversalDocumentUpload/
│       ├── bundle.js (603 KB)
│       ├── ControlManifest.xml
│       └── css/
│           └── UniversalQuickCreate.css
└── WebResources/
    ├── sprk_subgrid_commandsAECBD6C8-46A6-F011-BBD3-7C1E5217CD7C (v3.0.0)
    ├── sprk_DocumentOperations91C4FB37-669E-F011-BBD3-7C1E5217CD7C
    └── sprk_SPRK_Project_Wrench24902949-6574-F011-B4CB-6045BDD8B757
```

### Checksums (Optional)

```bash
# Verify package integrity
md5sum UniversalQuickCreate_3_0_0_0.zip
```

---

## Version History

### v3.0.0.0 (2025-10-21) - Custom Page Migration
- **Changes:**
  - PCF Control: Added Custom Page mode support
  - Web Resource: Updated to use Xrm.Navigation.navigateTo()
  - Architecture: Replaced Form Dialog with Custom Page dialog
- **Tasks:** Sprint Tasks 1, 2, 3, 4
- **Commits:**
  - d123402: Ribbon command update
  - 564bac9: PCF v3.0.0 update
  - b19cdc0: Custom Page creation

### v2.3.0 (Previous) - Quick Create Form Approach
- Uses sprk_uploadcontext utility entity
- Uses Xrm.Navigation.openForm()
- Form Dialog architecture

---

**Package Created:** 2025-10-21
**Created By:** Claude Code (ralph.schroeder@spaarke.com)
**Sprint:** Custom Page Migration v3.0.0
**Status:** Ready for Deployment
