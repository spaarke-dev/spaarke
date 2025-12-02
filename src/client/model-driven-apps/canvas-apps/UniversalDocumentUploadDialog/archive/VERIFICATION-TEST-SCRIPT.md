# Verification & Testing Script - Custom Page v3.0.0

**Date:** 2025-10-21
**Environment:** SPAARKE DEV 1
**Sprint:** Custom Page Migration v3.0.0

---

## Test Plan Overview

This document provides step-by-step verification and testing instructions for the Custom Page deployment.

---

## Part 1: Verify Deployment in Power Apps

### Step 1.1: Verify PCF Control Version

1. Open browser and navigate to: https://make.powerapps.com
2. Sign in as: ralph.schroeder@spaarke.com
3. Select environment: **SPAARKE DEV 1**
4. Click **Solutions** (left nav)
5. Find and click: **UniversalQuickCreate**
6. Click **Controls** (or use search: type "Universal")
7. Verify:
   - ✅ Control Name: **sprk_Spaarke.Controls.UniversalDocumentUpload**
   - ✅ Version: **3.0.0.0** (or 3.0.0)

**Expected Result:** Version shows 3.0.0.0

**Screenshot Location:** Save to dev/projects/quickcreate_pcf_component/screenshots/pcf-version-verification.png

---

### Step 1.2: Verify Web Resource Version

1. In the **UniversalQuickCreate** solution
2. Click **Web resources** (left panel)
3. Search for: **sprk_subgrid_commands**
4. Click on: **sprk_subgrid_commands**
5. Click **Edit** (or **View content**)
6. Verify line 9 shows:
   ```javascript
   * @version 3.0.0
   ```
7. Verify line 4 shows:
   ```javascript
   * PURPOSE: Opens Custom Page Dialog for uploading multiple documents
   ```
8. Search for (Ctrl+F): `navigateTo`
   - ✅ Should find: `Xrm.Navigation.navigateTo(pageInput, navigationOptions)`
9. Search for: `sprk_documentuploaddialog_e52db`
   - ✅ Should find: `name: 'sprk_documentuploaddialog_e52db'`

**Expected Result:**
- Version 3.0.0
- Uses navigateTo (Custom Page approach)
- References sprk_documentuploaddialog_e52db

**Screenshot Location:** Save to dev/projects/quickcreate_pcf_component/screenshots/webresource-version-verification.png

---

### Step 1.3: Verify Custom Page Exists

1. In Power Apps, click **Solutions** → **Default Solution**
2. Click **Pages** (or **Custom pages**)
3. Search for: **sprk_documentuploaddialog** or **Document Upload**
4. Verify:
   - ✅ Name: **sprk_documentuploaddialog_e52db**
   - ✅ Display Name: **Document Upload Dialog** (or similar)
   - ✅ Type: **Dialog**

**Expected Result:** Custom Page exists and is published

**Screenshot Location:** Save to dev/projects/quickcreate_pcf_component/screenshots/custom-page-verification.png

---

## Part 2: Browser Console Verification

### Step 2.1: Verify Web Resource Loads in Browser

1. Open a new browser tab/window
2. Navigate to: https://spaarkedev1.crm.dynamics.com/
3. Open **Developer Tools** (F12)
4. Go to **Console** tab
5. Paste and run this script:

```javascript
fetch(Xrm.Utility.getGlobalContext().getClientUrl() + "/WebResources/sprk_subgrid_commands.js")
  .then(r => r.text())
  .then(content => {
    const versionMatch = content.match(/@version\s+([\d.]+)/);
    const usesNavigateTo = content.includes("Xrm.Navigation.navigateTo");
    const usesOpenForm = content.includes("Xrm.Navigation.openForm");
    const customPageRef = content.match(/sprk_documentuploaddialog_\w+/);

    console.log("========================================");
    console.log("Web Resource Verification");
    console.log("========================================");
    console.log("Version:", versionMatch ? versionMatch[1] : "NOT FOUND");
    console.log("Uses navigateTo (Custom Page):", usesNavigateTo ? "✓ YES" : "✗ NO");
    console.log("Uses openForm (Old):", usesOpenForm ? "✗ YES (BAD)" : "✓ NO (GOOD)");
    console.log("Custom Page Name:", customPageRef ? customPageRef[0] : "NOT FOUND");
    console.log("========================================");

    if (versionMatch && versionMatch[1] === "3.0.0" && usesNavigateTo && !usesOpenForm && customPageRef) {
      console.log("✅ WEB RESOURCE v3.0.0 DEPLOYED CORRECTLY!");
    } else {
      console.log("❌ Web resource verification FAILED");
      console.log("⚠️ Try hard refresh (Ctrl+Shift+R) and check again");
    }
  });
```

**Expected Output:**
```
========================================
Web Resource Verification
========================================
Version: 3.0.0
Uses navigateTo (Custom Page): ✓ YES
Uses openForm (Old): ✓ NO (GOOD)
Custom Page Name: sprk_documentuploaddialog_e52db
========================================
✅ WEB RESOURCE v3.0.0 DEPLOYED CORRECTLY!
```

**If Failed:**
- Press Ctrl+Shift+R (hard refresh)
- Clear browser cache
- Try in incognito/private window
- Wait 5 minutes for Dataverse cache to clear

**Screenshot Location:** Save console output to dev/projects/quickcreate_pcf_component/screenshots/browser-verification.png

---

## Part 3: Functional Testing - Matter Entity

### Step 3.1: Find Test Matter Record

1. In Dataverse (https://spaarkedev1.crm.dynamics.com/)
2. Navigate to **Matters** (or use search: "Matters")
3. Find a Matter record that has:
   - ✅ Container ID field populated (sprk_containerid)
   - ✅ Existing Documents (optional but helpful)

**If no Matter has Container ID:**
- Open any Matter
- Look for field: **Container ID** or **sprk_containerid**
- If empty, create a container first (may require separate process)

**Test Matter Used:**
- Matter Name: _______________________
- Matter ID: _______________________
- Container ID: _______________________

---

### Step 3.2: Test Dialog Opens

1. Open the test Matter record
2. Scroll down to **Documents** subgrid
3. Look for ribbon button: **"Upload Documents"** or **"Add Documents"** or **"Quick Create: Document"**
4. Open browser **Developer Tools** (F12) → **Console** tab
5. Click the **Upload Documents** button

**Expected Behavior:**
- ✅ Custom Page dialog opens (modal, centered)
- ✅ Dialog size: ~800px × 600px
- ✅ Dialog has header: "Document Upload Dialog" or similar
- ✅ PCF control loads inside dialog
- ✅ File input area is visible
- ✅ Upload button is visible

**Expected Console Logs:**
```
[Spaarke] AddMultipleDocuments: Starting v3.0.0 - CUSTOM PAGE DIALOG
[Spaarke] Parent Entity: sprk_matter
[Spaarke] Parent Record ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
[Spaarke] Container ID: b!yLRdWEOAdkaWXskuRfByI...
[Spaarke] Opening Custom Page Dialog with parameters: {parentEntityName: "sprk_matter", ...}
[Spaarke] Page Input: {pageType: "custom", name: "sprk_documentuploaddialog_e52db", ...}
[Spaarke] Navigation Options: {target: 2, position: 1, width: {...}, height: {...}}
```

**If Dialog Does NOT Open:**
- Check console for errors
- Verify Custom Page exists (Step 1.3)
- Verify web resource deployed (Step 2.1)

**Screenshot Location:** Save to dev/projects/quickcreate_pcf_component/screenshots/dialog-opened.png

---

### Step 3.3: Test File Upload

**Prerequisites:**
- Dialog is open from Step 3.2
- Have a test file ready (PDF or DOCX, < 10 MB)

**Test File:**
- File Name: test-document.pdf (or similar)
- File Size: _______ KB

**Steps:**
1. In the Custom Page dialog, click file input area or "Choose Files"
2. Select your test file
3. Verify file name appears in the control
4. Click **"Upload & Create"** button (or similar)
5. Watch the dialog for:
   - ✅ Upload progress indicator
   - ✅ Success message (e.g., "Document created successfully")
   - ✅ Dialog closes automatically

**Expected Console Logs:**
```
[PCF] Starting file upload...
[PCF] File: test-document.pdf (XXX KB)
[PCF] Uploading to SPE...
[PCF] Upload successful
[PCF] Creating Dataverse record...
[PCF] Document record created: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
[PCF] Closing dialog...
[Spaarke] Custom Page Dialog closed successfully
[Spaarke] Refreshing subgrid...
```

**After Dialog Closes:**
- ✅ Subgrid should refresh automatically
- ✅ New document should appear in subgrid
- ✅ Document name should match uploaded file
- ✅ Document should link to Matter

**Screenshot Locations:**
- dev/projects/quickcreate_pcf_component/screenshots/file-selected.png
- dev/projects/quickcreate_pcf_component/screenshots/upload-progress.png
- dev/projects/quickcreate_pcf_component/screenshots/upload-success.png
- dev/projects/quickcreate_pcf_component/screenshots/subgrid-refreshed.png

---

### Step 3.4: Verify Document Record Created

1. After successful upload, find the new document in the subgrid
2. Click on the document to open it
3. Verify all fields are populated:
   - ✅ **Name**: Matches uploaded file name
   - ✅ **Matter** lookup: Points to test Matter
   - ✅ **Container ID**: Matches Matter's container ID
   - ✅ **File Name**: Correct
   - ✅ **File Size**: Correct
   - ✅ **Modified Date**: Recent (today)

**Screenshot Location:** Save to dev/projects/quickcreate_pcf_component/screenshots/document-record-details.png

---

## Part 4: Error Handling Tests

### Step 4.1: Test Cancel Dialog

1. Open Matter record
2. Click **Upload Documents** button
3. Dialog opens
4. Click **Cancel** or press **ESC** key

**Expected Behavior:**
- ✅ Dialog closes
- ✅ NO error popup appears
- ✅ Subgrid does NOT refresh

**Expected Console:**
```
[Spaarke] Dialog error or cancelled {errorCode: 2}
```
(errorCode 2 = user cancellation, suppressed)

---

### Step 4.2: Test Matter Without Container ID

1. Find or create a Matter with NO Container ID
2. Click **Upload Documents** button

**Expected Behavior:**
- ✅ Error message appears: "This Matter is not configured for document storage"
- ✅ Dialog does NOT open

**Expected Console:**
```
[Spaarke] Container ID validation failed
[Spaarke] Error: This Matter is not configured for document storage
```

---

### Step 4.3: Test Large File (>10 MB)

1. Open Matter with Container ID
2. Click **Upload Documents**
3. Select a file > 10 MB (if available)
4. Click Upload

**Expected Behavior:**
- ✅ Error message: "File size exceeds limit" (or similar)
- ✅ Upload does NOT proceed

**Note:** This test may be skipped if no large file is available.

---

## Part 5: Multi-Entity Testing

Test the same workflow on other entity types to verify multi-entity support.

### Entities to Test:
- ✅ sprk_matter (tested in Part 3)
- ⬜ sprk_project
- ⬜ sprk_invoice
- ⬜ account
- ⬜ contact

**For each entity:**
1. Find record with Container ID
2. Click Upload Documents button
3. Upload 1 test file
4. Verify success and subgrid refresh

**Results:**

| Entity | Dialog Opened | Upload Success | Subgrid Refresh | Notes |
|--------|---------------|----------------|-----------------|-------|
| sprk_matter | ⬜ | ⬜ | ⬜ | |
| sprk_project | ⬜ | ⬜ | ⬜ | |
| sprk_invoice | ⬜ | ⬜ | ⬜ | |
| account | ⬜ | ⬜ | ⬜ | |
| contact | ⬜ | ⬜ | ⬜ | |

---

## Part 6: Performance Testing

### Step 6.1: Dialog Open Time

1. Open Matter record
2. Open browser Dev Tools → **Network** tab
3. Click **Upload Documents**
4. Note time for dialog to fully open

**Expected:** < 2 seconds

**Actual:** _______ seconds

---

### Step 6.2: Upload Time (Small File)

1. Test file: ~1 MB PDF
2. Click Upload & Create
3. Note time from click to dialog close

**Expected:** < 5 seconds

**Actual:** _______ seconds

---

### Step 6.3: Upload Time (Medium File)

1. Test file: ~5 MB PDF
2. Click Upload & Create
3. Note time from click to dialog close

**Expected:** < 10 seconds

**Actual:** _______ seconds

---

## Part 7: Browser Compatibility (Optional)

Test in multiple browsers:

- ⬜ Microsoft Edge (Chromium)
- ⬜ Google Chrome
- ⬜ Firefox
- ⬜ Safari (Mac only)

**Expected:** Works in all modern browsers

---

## Test Summary

**Date Tested:** 2025-10-21
**Tested By:** _______________________
**Environment:** SPAARKE DEV 1
**Overall Status:** ⬜ PASS / ⬜ FAIL / ⬜ PARTIAL

### Critical Tests (Must Pass):
- ⬜ Web Resource v3.0.0 deployed
- ⬜ Custom Page dialog opens
- ⬜ File upload succeeds
- ⬜ Subgrid auto-refreshes
- ⬜ Document record created correctly
- ⬜ Cancel dialog works (no error)

### Non-Critical Tests:
- ⬜ Container ID validation
- ⬜ Multi-entity support (all 5 entities)
- ⬜ Performance acceptable
- ⬜ Browser compatibility

---

## Issues Found

| Issue # | Severity | Description | Screenshot | Status |
|---------|----------|-------------|------------|--------|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |

---

## Sign-Off

**Deployment Verified:** ⬜ YES / ⬜ NO

**Ready for UAT:** ⬜ YES / ⬜ NO

**Notes:**
_______________________________________________
_______________________________________________
_______________________________________________

**Signature:** _______________________
**Date:** _______________________

---

**Next Steps:**
- If all tests pass → Proceed to Task 6 (DEV Deployment documentation)
- If issues found → Document in Issues table above and fix
- Create completion report with test results
