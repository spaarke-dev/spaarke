# Phase 3 & 4 Deployment Instructions

**Date:** 2025-10-21
**Sprint:** Custom Page Migration v3.0.0 - Remedial Plan
**Status:** Ready for Web Resource Upload

---

## What Was Deployed Automatically

### PCF Control v3.0.0 (Phase 4)
**Status:** ✅ DEPLOYED via `pac pcf push`

**Changes Included:**
- Added `_initialized` flag to prevent multiple initializations
- Rewrote `updateView()` method to be idempotent
- Added parameter hydration wait logic
- Logs "Waiting for parameters to hydrate" when params not ready

**Verification:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build              # ✅ Build successful
pac pcf push --publisher-prefix sprk  # ✅ Deployed and published
```

---

## Manual Step Required: Upload Web Resource

### Web Resource Changes (Phase 3)

The file `sprk_subgrid_commands.js` has been updated with:

1. **Added appId** to pageInput:
   ```javascript
   appId: Xrm.Utility.getGlobalContext().client.getAppId()
   ```

2. **Changed position to right-side pane**:
   ```javascript
   position: 2,    // Right side pane (Quick Create style) - CHANGED from 1
   ```

3. **Adjusted width**:
   ```javascript
   width: { value: 640, unit: 'px' }  // CHANGED from 800
   ```

4. **Removed height** - Let Custom Page control its own height

---

## Upload Instructions

### Step 1: Open Power Apps Maker Portal

1. Navigate to: https://make.powerapps.com
2. Sign in as: ralph.schroeder@spaarke.com
3. Select environment: **SPAARKE DEV 1**

### Step 2: Find Web Resource

1. Click **Solutions** (left navigation)
2. Open solution: **UniversalQuickCreate**
3. Click **Web resources** (left panel or search)
4. Search for: **sprk_subgrid_commands**
5. Click on **sprk_subgrid_commands**

### Step 3: Upload Updated File

1. Click **Edit** (or **Update content**)
2. Click **Upload file** or **Choose File**
3. Select file: `C:\code_files\spaarke\sprk_subgrid_commands.js`
4. Verify file size shows recent timestamp
5. Click **Save**

### Step 4: Publish

1. After saving, click **Publish** (or use the Publish button in toolbar)
2. Wait for "Published successfully" message
3. Close the web resource editor

**Alternative: Publish All Customizations**
- In the solution, click **Publish all customizations** at the top

---

## Verification After Upload

### Browser Console Verification

1. Open Dataverse in browser: https://spaarkedev1.crm.dynamics.com/
2. Press F12 → Console tab
3. Paste and run this script:

```javascript
Xrm.WebApi.retrieveMultipleRecords("webresource", "?$filter=name eq 'sprk_subgrid_commands'&$select=content,modifiedon")
  .then(result => {
    const wr = result.entities[0];
    const content = atob(wr.content);

    const versionMatch = content.match(/@version\s+([\d.]+)/);
    const hasAppId = content.includes("appId: Xrm.Utility.getGlobalContext().client.getAppId()");
    const hasPosition2 = content.includes("position: 2");
    const hasWidth640 = content.includes("width: { value: 640");

    console.log("========================================");
    console.log("Web Resource Verification - Phase 3");
    console.log("========================================");
    console.log("Version:", versionMatch ? versionMatch[1] : "NOT FOUND");
    console.log("Modified:", new Date(wr.modifiedon));
    console.log("Has appId:", hasAppId ? "✓ YES" : "✗ NO");
    console.log("Position = 2 (right pane):", hasPosition2 ? "✓ YES" : "✗ NO");
    console.log("Width = 640px:", hasWidth640 ? "✓ YES" : "✗ NO");
    console.log("========================================");

    if (versionMatch && versionMatch[1] === "3.0.0" && hasAppId && hasPosition2 && hasWidth640) {
      console.log("✅ WEB RESOURCE PHASE 3 UPDATES DEPLOYED CORRECTLY!");
    } else {
      console.log("❌ Web resource verification FAILED");
      console.log("⚠️ Try hard refresh (Ctrl+Shift+R) or wait 5 minutes for cache");
    }
  });
```

**Expected Output:**
```
========================================
Web Resource Verification - Phase 3
========================================
Version: 3.0.0
Modified: [Recent timestamp - today]
Has appId: ✓ YES
Position = 2 (right pane): ✓ YES
Width = 640px: ✓ YES
========================================
✅ WEB RESOURCE PHASE 3 UPDATES DEPLOYED CORRECTLY!
```

---

## Next Steps: Testing (Phase 5)

After uploading the web resource, proceed to testing:

### Test 1: Dialog Opens on Right Side

1. Open any Matter record with a Container ID
2. Scroll to Documents subgrid
3. Click **Upload Documents** button
4. **Expected Result:**
   - Dialog opens on **right side** of screen (Quick Create pane style)
   - Dialog width is **640px** (narrower than before)
   - Dialog **stays open** (does not close immediately)
   - PCF control renders inside dialog

### Test 2: Parameter Hydration

1. With dialog open, check browser console
2. **Expected Logs:**
   ```
   [Spaarke] Opening Custom Page Dialog with parameters: {...}
   [Spaarke] Page Input: {pageType: "custom", name: "sprk_documentuploaddialog_e52db", appId: "...", ...}
   [Spaarke] Navigation Options: {target: 2, position: 2, width: {...}}

   [UniversalDocumentUpload] Waiting for parameters to hydrate
   [UniversalDocumentUpload] Parameters hydrated - initializing async
   [UniversalDocumentUpload] Initialization complete
   ```

3. **Should NOT see:**
   - "Custom Page Dialog closed successfully undefined"
   - "Invalid parentRecordId format"
   - "Parent context not loaded" error

### Test 3: File Upload End-to-End

1. With dialog open, select a test file (PDF or DOCX, < 10 MB)
2. Click **Upload & Create**
3. **Expected:**
   - Upload succeeds
   - Document record created in Dataverse
   - Dialog closes after success
   - Subgrid refreshes showing new document

---

## Troubleshooting

### Dialog Still Closes Immediately

**Possible Causes:**
1. Web resource not uploaded yet → Upload manually (see above)
2. Browser cache → Hard refresh (Ctrl+Shift+R) or incognito mode
3. Custom Page not in Model-Driven App → Verify Phase 1 completed
4. Custom Page OnStart/Screen.OnVisible not updated → Verify Phase 2 completed

### Parameter Errors in Console

**"Waiting for parameters to hydrate" appears repeatedly:**
- This is normal for the first 1-2 calls
- If it continues > 5 seconds, check Custom Page Screen.OnVisible formula
- Verify variables are set: varParentRecordId, varParentEntityName, varContainerId

**"Invalid parentRecordId format" error:**
- Phase 4 PCF changes should prevent this
- If still occurring, verify `pac pcf push` completed successfully
- Check PCF version in Custom Page editor shows 3.0.0

### Dialog Opens on Wrong Side

**Dialog opens centered instead of right side:**
- Verify web resource has `position: 2`
- Run verification script above
- Clear browser cache and retry

---

## Rollback Plan

If testing fails and you need to revert:

### Rollback Web Resource
1. Open Power Apps → Solutions → UniversalQuickCreate → Web resources
2. Find previous version in solution history
3. Restore previous version
4. Publish

### Rollback PCF Control
PCF control changes are backward compatible (idempotent updateView), so rollback should not be needed.

---

## Summary

**Phase 3 & 4 Status:**
- ✅ Phase 4: PCF Control deployed automatically via `pac pcf push`
- ⏳ Phase 3: Web Resource - **MANUAL UPLOAD REQUIRED**

**File to Upload:**
- Path: `C:\code_files\spaarke\sprk_subgrid_commands.js`
- Location: Power Apps → Solutions → UniversalQuickCreate → Web Resources → sprk_subgrid_commands

**After Upload:**
- Run verification script in browser console
- Proceed to Phase 5 testing

---

**Created:** 2025-10-21
**Last Updated:** 2025-10-21
