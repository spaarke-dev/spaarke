# Task 4: Solution Packaging

**Sprint:** Custom Page Migration v3.0.0
**Estimate:** 4 hours
**Status:** Not Started
**Depends On:** Tasks 1, 2, 3 complete

---

## Pre-Task Review Prompt

Verify all components are ready for packaging:

```
TASK REVIEW: Verify all components ready for packaging.

1. Verify Task 1 Complete:
   - [ ] Custom Page exists in SPAARKE DEV 1
   - [ ] Custom Page name: sprk_documentuploaddialog

2. Verify Task 2 Complete:
   - [ ] PCF control version: 3.0.0
   - [ ] Build output exists: src/controls/UniversalQuickCreate/out/
   - [ ] No build errors

3. Verify Task 3 Complete:
   - [ ] Web resource updated
   - [ ] All 5 RibbonDiff.xml updated
   - [ ] Changes committed locally

4. Check Solution Structure:
   cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
   ls -la

   Expected: Solution project files (.cdsproj)

5. pac CLI Verification:
   pac --version

   Expected: Power Platform CLI version 1.x.x or higher

6. pac Authentication:
   pac auth list

   Expected: SPAARKE DEV 1 environment listed

Output: "Ready to package" OR "Issues: [list]"
```

---

## Task Context

**What:** Package all components into deployable solution (.zip)

**Why:** Create single artifact for deployment to environments

**Components to Package:**
1. Custom Page (sprk_documentuploaddialog)
2. PCF Control (UniversalQuickCreate v3.0.0)
3. Web Resources (sprk_subgrid_commands.js)
4. Ribbon Customizations (RibbonDiff.xml for 5 entities)

---

## Implementation Steps

### Step 4.1: Deploy PCF Control to Dataverse

**Prompt:**
```
Deploy PCF control v3.0.0 to SPAARKE DEV 1 environment.

Context:
- pac pcf push deploys PCF and updates solution
- Existing solution: Check if UniversalQuickCreate is in a solution already
- If yes: Updates existing control
- If no: Creates new solution
```

**Commands:**

1. Navigate to PCF project:
   ```bash
   cd src/controls/UniversalQuickCreate
   ```

2. Verify authentication:
   ```bash
   pac auth list
   ```

   Look for SPAARKE DEV 1:
   ```
   * [Dev] SPAARKE DEV 1
     URL: https://spaarkedev1.crm.dynamics.com
     Type: Dataverse
   ```

   If not active (no *), set as active:
   ```bash
   pac auth select --index 0
   ```

3. Deploy PCF Control:
   ```bash
   pac pcf push --publisher-prefix sprk
   ```

   Expected Output:
   ```
   Uploading PowerApps Component Framework control to Dataverse...
   Component 'sprk_Spaarke.Controls.UniversalQuickCreate' uploaded successfully.
   Import completed successfully.
   ```

4. Verify Deployment:
   ```bash
   pac pcf list
   ```

   Look for:
   ```
   sprk_Spaarke.Controls.UniversalQuickCreate (v3.0.0.0)
   ```

5. Test in Power Apps:
   - Open Power Apps Maker Portal
   - Go to Solutions → Default Solution
   - Look for "UniversalQuickCreate" control
   - Version should show 3.0.0

**Troubleshooting:**
- If "Authentication required": Run `pac auth create`
- If "Publisher prefix not found": Verify 'sprk' prefix exists in environment
- If "Control already exists": Update is automatic, that's OK
- If errors: Read error message carefully, may need to delete old version first

**Guardrails:**
- ⚠️ Use --publisher-prefix sprk (must match existing prefix)
- ⚠️ Deploy to DEV environment only (verify URL)
- ⚠️ If deployment fails, do NOT proceed - fix errors first

---

### Step 4.2: Export Solution from Dataverse

**Context:**
- Solution should contain: Custom Page, PCF Control, Web Resources, Ribbon Customizations
- Export as Unmanaged for DEV/UAT, Managed for PROD

**Option A: Export via pac CLI (Recommended)**

1. List solutions:
   ```bash
   pac solution list
   ```

   Find solution containing document upload components.
   Note the solution name (e.g., "SpaarkeDocumentUpload")

2. Export solution:
   ```bash
   pac solution export \
     --name SpaarkeDocumentUpload \
     --path ./SpaarkeDocumentUpload_3_0_0_0.zip \
     --managed false
   ```

   Expected Output:
   ```
   Exporting solution...
   Solution exported successfully: ./SpaarkeDocumentUpload_3_0_0_0.zip
   ```

**Option B: Export via Power Apps Maker Portal**

1. Navigate to https://make.powerapps.com/
2. Select SPAARKE DEV 1 environment
3. Go to Solutions
4. Find "Spaarke Document Upload" solution
5. Click "Export"
6. Select "Unmanaged"
7. Click "Export"
8. Wait for download
9. Save as: SpaarkeDocumentUpload_3_0_0_0.zip

**Verify Solution Contents:**

```bash
# Extract to temp folder
mkdir -p temp_solution
cd temp_solution
unzip ../SpaarkeDocumentUpload_3_0_0_0.zip

# List contents
ls -R
```

Expected files:
```
[Content_Types].xml
customizations.xml
solution.xml

Controls/
  sprk_UniversalQuickCreate_*.xml

CustomPages/
  sprk_documentuploaddialog_*.xml

WebResources/
  sprk_subgrid_commands.js

Other/
  Customizations.xml (contains RibbonDiff)
```

**Verify Version in solution.xml:**
```bash
grep "Version" solution.xml
```

Expected: `<Version>3.0.0.0</Version>`

**Clean up:**
```bash
cd ..
rm -rf temp_solution
```

**Guardrails:**
- ⚠️ Export UNMANAGED for now (managed comes later for PROD)
- ⚠️ Verify version is 3.0.0.0
- ⚠️ Verify all components present (Custom Page, PCF, Web Resources, Ribbon)
- ⚠️ If solution is incomplete, add missing components before exporting

---

### Step 4.3: Update Solution Version and Publisher Info

**Context:**
- Solution version should be 3.0.0.0
- Publisher should be Spaarke Inc. (or correct publisher)
- Description should mention Custom Page migration

**Option A: Update via pac CLI**

```bash
pac solution online-version \
  --solution-name SpaarkeDocumentUpload \
  --solution-version 3.0.0.0
```

**Option B: Update via Power Apps Maker Portal**

1. Open solution in Maker Portal
2. Click "..." → Settings
3. Update version: 3.0.0.0
4. Update description:
   ```
   Universal Document Upload v3.0.0 - Custom Page Migration

   Changes:
   - Replaced Quick Create form with Custom Page dialog
   - Modern modal UI experience
   - Autonomous workflow (no form dependency)
   - Maintains Phase 7 dynamic metadata discovery
   - Supports 5 entity types: Matter, Project, Invoice, Account, Contact
   ```
5. Save

**Option C: Manual XML Update (Advanced)**

If you have the exported solution:

1. Extract solution:
   ```bash
   unzip SpaarkeDocumentUpload_3_0_0_0.zip -d solution_temp
   cd solution_temp
   ```

2. Edit solution.xml:
   ```xml
   <ImportExportXml version="9.1" SolutionPackageVersion="9.1" ...>
     <SolutionManifest>
       <UniqueName>SpaarkeDocumentUpload</UniqueName>
       <LocalizedNames>
         <LocalizedName description="Spaarke Document Upload" languagecode="1033" />
       </LocalizedNames>
       <Descriptions>
         <Description description="Universal Document Upload v3.0.0 - Custom Page Migration" languagecode="1033" />
       </Descriptions>
       <Version>3.0.0.0</Version>
       ...
     </SolutionManifest>
   </ImportExportXml>
   ```

3. Re-zip:
   ```bash
   zip -r ../SpaarkeDocumentUpload_3_0_0_0_updated.zip *
   ```

**Guardrails:**
- ⚠️ Version format: X.Y.Z.W (must have 4 parts for Dataverse)
- ⚠️ Publisher must match existing publisher in environment
- ⚠️ If updating XML manually, validate XML syntax before re-zipping

---

### Step 4.4: Document Solution Contents and Deploy Instructions

Create file: `dev/projects/quickcreate_pcf_component/DEPLOYMENT-PACKAGE.md`

```markdown
# Deployment Package: SpaarkeDocumentUpload v3.0.0

**File:** `SpaarkeDocumentUpload_3_0_0_0.zip`
**Version:** 3.0.0.0
**Created:** 2025-10-20
**Type:** Unmanaged (for DEV/UAT)

---

## Package Contents

### 1. Custom Page
- **Name:** sprk_documentuploaddialog
- **Display Name:** Document Upload Dialog
- **Type:** Dialog (Modal)
- **Purpose:** Replace Quick Create form with modern UI

### 2. PCF Control
- **Name:** sprk_Spaarke.Controls.UniversalQuickCreate
- **Version:** 3.0.0.0
- **Changes:**
  - Added Custom Page mode detection
  - Added autonomous workflow support
  - Added closeDialog() method
  - Maintains Phase 7 dynamic metadata discovery

### 3. Web Resources
- **sprk_subgrid_commands.js**
  - Updated function: openDocumentUploadDialog
  - Navigation changed: Quick Create → Custom Page
  - Added container ID validation
  - Added subgrid auto-refresh

### 4. Ribbon Customizations
- **Entities:** sprk_matter, sprk_project, sprk_invoice, account, contact
- **Changes:** Updated command to call new function
- **File:** RibbonDiff.xml (in each entity folder)

---

## Prerequisites

### Environment Requirements
- Dataverse environment (DEV, UAT, or PROD)
- Power Platform CLI (pac) installed
- System Customizer role minimum
- Custom Pages feature enabled

### Dependencies
- BFF API operational: https://spe-api-dev-67e2xz.azurewebsites.net
- Phase 7 deployed (NavMap endpoints)
- Redis cache available
- SharePoint Embedded containers configured

### No Changes Required
- ✅ BFF API (no changes)
- ✅ Dataverse schema (no changes)
- ✅ Security roles (no changes)
- ✅ Service layer code (no changes)

---

## Deployment Steps

### Step 1: Backup Current Configuration

```bash
# Export current solution (v2.3.0) as backup
pac solution export \
  --name SpaarkeDocumentUpload \
  --path ./SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip \
  --managed false
```

### Step 2: Import Solution

Option A: pac CLI
```bash
pac solution import \
  --path SpaarkeDocumentUpload_3_0_0_0.zip \
  --async \
  --publish-changes
```

Option B: Power Apps Maker Portal
1. Navigate to https://make.powerapps.com/
2. Select target environment
3. Go to Solutions
4. Click "Import solution"
5. Upload SpaarkeDocumentUpload_3_0_0_0.zip
6. Click "Next" → "Import"
7. Wait for completion
8. Click "Publish all customizations"

### Step 3: Verify Deployment

1. Custom Page exists:
   - Solutions → SpaarkeDocumentUpload → Pages
   - Look for "Document Upload Dialog"

2. PCF Control version:
   - Solutions → SpaarkeDocumentUpload → Controls
   - Verify "UniversalQuickCreate" shows v3.0.0.0

3. Web Resources updated:
   - Solutions → SpaarkeDocumentUpload → Web Resources
   - Open sprk_subgrid_commands.js
   - Verify function name: openDocumentUploadDialog

4. Ribbon buttons work:
   - Open a Matter record
   - Look for "Upload Documents" button on Documents subgrid
   - Click button
   - Custom Page dialog should open

### Step 4: Smoke Testing

Test on ONE entity first (e.g., Matter):

1. Open existing Matter record with container ID
2. Click "Upload Documents" button
3. Verify dialog opens (modal, centered)
4. Select 1 test file
5. Click "Upload & Create"
6. Verify:
   - File uploads to SPE
   - Document record created in Dataverse
   - Dialog closes automatically
   - Subgrid refreshes
   - New document visible

If successful, test remaining entities:
- sprk_project
- sprk_invoice
- account
- contact

---

## Rollback Procedure

If critical issues occur:

### Option 1: Restore Backup (Quick)
```bash
pac solution import \
  --path SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip \
  --force-overwrite \
  --publish-changes
```

### Option 2: Revert Ribbon Only (Faster)
1. Update Web Resource to v2.3.0 version
2. Publish customizations
3. Users see Quick Create form again
4. Custom Page remains but unused

---

## Known Issues

None at time of packaging.

---

## Support

- **Documentation:** dev/projects/quickcreate_pcf_component/SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md
- **Issues:** Contact Development Team
- **Logs:** Check Application Insights for errors
```

Save this file in the project folder.

**Guardrails:**
- ⚠️ Include ALL deployment steps
- ⚠️ Include rollback procedure
- ⚠️ List all prerequisites
- ⚠️ Document verification steps
- ⚠️ Smoke test procedure detailed

---

## Acceptance Criteria

- [ ] PCF control deployed to Dataverse (v3.0.0)
- [ ] Solution exported successfully
- [ ] Solution file verified (all components present)
- [ ] Solution version updated to 3.0.0.0
- [ ] Deployment documentation created
- [ ] Solution file saved: `SpaarkeDocumentUpload_3_0_0_0.zip`
- [ ] Backup of v2.3.0 created
- [ ] All files committed to git

---

## Deliverables

1. ✅ `SpaarkeDocumentUpload_3_0_0_0.zip` (solution package)
2. ✅ `SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip` (backup)
3. ✅ `DEPLOYMENT-PACKAGE.md` (deployment docs)
4. ✅ Solution verification screenshots
5. ✅ Git commit with solution file

---

**Created:** 2025-10-20
**Sprint:** Custom Page Migration v3.0.0
**Version:** 1.0.0
