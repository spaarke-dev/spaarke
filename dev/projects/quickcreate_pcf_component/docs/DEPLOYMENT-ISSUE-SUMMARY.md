# Custom Page Dialog Deployment Issue - Expert Consultation

**Date:** 2025-10-21
**Environment:** SPAARKE DEV 1
**Sprint:** Custom Page Migration v3.0.0
**Status:** ❌ BLOCKED - Dialog closes immediately

---

## 🎯 Objective

Migrate Universal Document Upload from Quick Create form to Custom Page dialog approach.

**Expected Behavior:**
- User clicks "Upload Documents" button on Matter record
- Custom Page dialog opens (modal, 800x600px)
- PCF control v3.0.0 loads inside dialog
- User can upload files
- Dialog closes after successful upload
- Subgrid auto-refreshes

**Actual Behavior:**
- Dialog opens and immediately closes
- Returns `undefined`
- No error messages visible to user
- No notification popups from OnStart formula

---

## ✅ What Has Been Successfully Deployed

### 1. PCF Control v3.0.0
- **Status:** ✅ Deployed to Dataverse
- **Control Name:** sprk_Spaarke.Controls.UniversalDocumentUpload
- **Version:** 3.0.0.0
- **Method:** `pac pcf push --publisher-prefix sprk`
- **Verification:** Visible in UniversalQuickCreate solution, shows v3.0.0 in Custom Page editor

### 2. Web Resource v3.0.0
- **Status:** ✅ Deployed to Dataverse
- **File:** sprk_subgrid_commands.js
- **Version:** 3.0.0
- **Method:** Manual upload via Power Apps portal
- **Verification:** Dataverse Web API query confirms version 3.0.0, uses `Xrm.Navigation.navigateTo()`
- **Modified:** 2025-10-21T05:04:44Z

### 3. Custom Page Created
- **Status:** ✅ Created and published
- **Logical Name:** sprk_documentuploaddialog_e52db
- **Display Name:** Document Upload
- **Type:** Dialog (modal)
- **PCF Control:** UniversalDocumentUpload v3.0.0 added to canvas
- **Location:** Default Solution → Pages

---

## ⚙️ Current Configuration

### Custom Page: App.OnStart Formula

```powerfx
// Debug: Show that OnStart is running
Notify("🔧 Custom Page Loading...", NotificationType.Information);

// Extract parameters
Set(varParentRecordId, Param("parentRecordId"));
Set(varParentEntityName, Param("parentEntityName"));
Set(varContainerId, Param("containerId"));
Set(varParentDisplayName, Param("parentDisplayName"));

// Debug: Show what we received
Notify(
    "✅ Params Received: " & varParentRecordId & " | " & varParentEntityName,
    NotificationType.Success,
    5000
);

// Validate we got real values
If(
    IsBlank(varParentRecordId) Or varParentRecordId = "val",
    Notify("❌ ERROR: Invalid parentRecordId: " & varParentRecordId, NotificationType.Error);
    Back()
);

If(
    IsBlank(varContainerId),
    Notify("❌ ERROR: Missing containerId", NotificationType.Error);
    Back()
)
```

### Custom Page: PCF Control Properties (Advanced Tab)

| Property | Value | Type |
|----------|-------|------|
| containerId | `varContainerId` | Variable |
| parentDisplayName | `varParentDisplayName` | Variable |
| parentEntityName | `varParentEntityName` | Variable |
| parentRecordId | `varParentRecordId` | Variable |
| sdapApiBaseUrl | `"https://spe-api-dev-67e2xz.azurewebsites.net"` | String literal |

**Confirmed:** All 5 properties are set correctly (screenshot provided by user)

### Web Resource: Navigation Call (sprk_subgrid_commands.js)

```javascript
// Parameters passed from ribbon button
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    data: {
        parentEntityName: 'sprk_matter',
        parentRecordId: '3A785F76-C773-F011-B4CB-6045BDD8B757', // GUID without braces
        containerId: 'b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50',
        parentDisplayName: '345345345345'
    }
};

const navigationOptions = {
    target: 2,      // Dialog
    position: 1,    // Center
    width: { value: 800, unit: 'px' },
    height: { value: 600, unit: 'px' }
};

Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
    function success(result) {
        console.log("[Spaarke] Custom Page Dialog closed successfully", result);
        selectedControl.refresh(); // Refresh subgrid
    },
    function error(err) {
        // Handle error
    }
);
```

---

## ❌ The Problem

### Symptom
When clicking "Upload Documents" button on a Matter record:
1. Dialog **does not appear** visually (no modal, no flash, nothing)
2. Console shows: `[Spaarke] Custom Page Dialog closed successfully undefined`
3. Promise resolves immediately with `undefined`
4. **No notification popups** from OnStart formula (should see "🔧 Custom Page Loading..." and "✅ Params Received...")

### Browser Console Output

```
[Spaarke] Opening Custom Page Dialog with parameters: {parentEntityName: 'sprk_matter', parentRecordId: '3A785F76-C773-F011-B4CB-6045BDD8B757', ...}
[Spaarke] Page Input: {pageType: 'custom', name: 'sprk_documentuploaddialog_e52db', data: {…}}
[Spaarke] Navigation Options: {target: 2, position: 1, width: {…}, height: {…}}
[Spaarke] Custom Page Dialog closed successfully undefined
[Spaarke] Refreshing subgrid...
```

### Test with Empty Parameters

Even calling `navigateTo()` with **no data parameter** returns `undefined`:

```javascript
Xrm.Navigation.navigateTo(
  { pageType: "custom", name: "sprk_documentuploaddialog_e52db" },
  { target: 2, position: 1, width: { value: 400, unit: "px" }, height: { value: 300, unit: "px" }}
).then(result => console.log(result)); // Returns: undefined
```

**This suggests the Custom Page itself has a critical error, not a parameter issue.**

---

## 🧪 Diagnostic Tests Performed

### Test 1: Web Resource Verification ✅ PASSED
**Method:** Dataverse Web API query
**Result:** Web resource contains v3.0.0 code with `navigateTo()` and correct Custom Page reference

### Test 2: PCF Control in Test Harness ✅ PASSED (with expected error)
**Method:** `npm start` in UniversalQuickCreate folder, open http://localhost:8181
**Result:**
- PCF control loads successfully
- Shows expected error: `Invalid parentRecordId format: "val"` (test harness passes default values)
- Error is correct behavior - validates GUID format

### Test 3: Custom Page Editor ✅ PASSED (with expected error)
**Method:** Edit Custom Page in Power Apps
**Result:**
- PCF control v3.0.0 visible on canvas
- Shows banner: "UNIVERSAL DOCUMENT UPLOAD V3.0.0 - CUSTOM PAGE DIALOG"
- Shows expected errors: "Parent context not loaded" (no real parameters in design mode)
- All properties correctly bound to variables

### Test 4: Runtime Dialog Test ❌ FAILED
**Method:** Click "Upload Documents" on Matter record
**Result:** Dialog closes immediately, no notifications appear, returns `undefined`

### Test 5: Empty Dialog Test ❌ FAILED
**Method:** Open Custom Page with no parameters
**Result:** Still returns `undefined` immediately

---

## 🔍 Key Observations

1. **OnStart Never Runs:** The Notify() statements never appear, suggesting OnStart is not executing or Custom Page crashes before OnStart

2. **No Visual Dialog:** The dialog doesn't appear on screen at all - not even a flash

3. **Immediate Resolution:** The navigateTo() promise resolves instantly (< 50ms) instead of waiting for user interaction

4. **No JavaScript Errors:** Browser console shows no red errors, only the success log from our code

5. **Works in Test Harness:** PCF control itself is functional when tested in isolation

6. **Version Mismatch History:** Custom Page was originally created with PCF v2.2.0 (Task 1), now has v3.0.0

---

## 🔧 Attempted Fixes

### Fix 1: Parameter Binding with Param()
- **Action:** Set PCF properties to `Param("parentRecordId")`, etc.
- **Result:** Failed - same behavior

### Fix 2: Parameter Binding with Variables via OnStart
- **Action:** Added OnStart with Set() statements, changed PCF properties to `varParentRecordId`, etc.
- **Result:** Failed - same behavior, no notifications appear

### Fix 3: Add Debug Notifications
- **Action:** Added Notify() calls in OnStart to see if it runs
- **Result:** No notifications appear - OnStart likely not running

### Fix 4: Manual Web Resource Update
- **Action:** Uploaded sprk_subgrid_commands.js v3.0.0 manually via Power Apps portal
- **Result:** Web resource deployed successfully, but dialog still fails

### Fix 5: Solution Pack and Import
- **Action:** Used `pac solution pack` + `pac solution import` to deploy complete solution
- **Result:** PCF control deployed, but dialog still fails

### Fix 6: Publish All Customizations
- **Action:** Ran `pac solution publish` and published via Power Apps portal
- **Result:** No change

### Fix 7: Browser Cache Clear
- **Action:** Hard refresh (Ctrl+Shift+R), incognito mode, cleared all cache
- **Result:** No change

---

## 💡 Hypotheses

### Hypothesis 1: Custom Page Not Properly Configured
**Evidence:**
- OnStart doesn't run (no notifications)
- Returns undefined immediately
- Happens even without parameters

**Counter-Evidence:**
- Custom Page exists and is published
- PCF control shows v3.0.0 in editor
- All properties configured correctly

### Hypothesis 2: Custom Page Type Issue
**Evidence:**
- Custom Page type should be "Dialog" but might not be set correctly
- Dialog doesn't appear visually

**To Check:**
- Custom Page settings → Verify "Type" is set to "Dialog"
- Verify dialog dimensions are set (800x600)

### Hypothesis 3: PCF Control Configuration Error
**Evidence:**
- PCF works in test harness
- Shows errors in Custom Page editor (expected in design mode)

**Counter-Evidence:**
- All 5 properties correctly bound
- No red error indicators on properties

### Hypothesis 4: Power Apps Platform Bug
**Evidence:**
- navigateTo() with Custom Pages can have platform-specific issues
- Behavior is consistent across all tests

**To Investigate:**
- Check Power Apps version/build
- Check if Custom Pages feature is fully enabled
- Check browser compatibility (Edge, Chrome)

---

## 📋 Questions for Expert

1. **Why would OnStart not execute at all?** No Notify() popups appear, suggesting OnStart isn't running or page crashes before OnStart.

2. **Why does navigateTo() resolve with undefined immediately?** Even with no parameters, the promise resolves instantly instead of waiting for user interaction.

3. **Is there a platform-level issue with Custom Pages?** The Custom Page works in the editor but fails at runtime.

4. **Could this be a security/permissions issue?** Does the Custom Page need specific permissions or security roles?

5. **Is there a way to see Custom Page runtime errors?** Browser console shows no errors - is there another log to check?

6. **Should Custom Page parameters be pre-declared?** Do we need to explicitly define input parameters somewhere in the Custom Page settings?

7. **Could the PCF control version mismatch be an issue?** Original page created with v2.2.0, now using v3.0.0 - does the page need to be recreated?

8. **Is there a deployment step we're missing?** Everything appears deployed and published, but something fundamental isn't working.

---

## 🎯 Desired Outcome

Custom Page dialog should:
1. Open when `Xrm.Navigation.navigateTo()` is called
2. Display PCF control inside modal dialog
3. Execute OnStart formula (show notifications)
4. Pass parameters to PCF control via variables
5. Stay open until user uploads files or clicks cancel
6. Return result (not undefined) when closed

---

## 📁 Reference Files

### Task Completion Reports
- [TASK-1-COMPLETION-REPORT.md](TASK-1-COMPLETION-REPORT.md) - Custom Page creation
- [TASK-2-COMPLETION-REPORT.md](TASK-2-COMPLETION-REPORT.md) - PCF Control v3.0.0
- [TASK-3-COMPLETION-REPORT.md](TASK-3-COMPLETION-REPORT.md) - Ribbon commands
- [TASK-4-COMPLETION-REPORT.md](TASK-4-COMPLETION-REPORT.md) - Solution packaging

### Source Code
- PCF Control: `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
- Web Resource: `sprk_subgrid_commands.js`
- Solution: `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/`

### Test Scripts
- [VERIFICATION-TEST-SCRIPT.md](VERIFICATION-TEST-SCRIPT.md) - Complete testing guide

---

## 🔄 Next Steps

1. **Consult Power Apps expert** on Custom Page runtime behavior
2. **Check Custom Page type/settings** - verify Dialog configuration
3. **Investigate platform logs** - Application Insights, Power Apps logs
4. **Consider recreating Custom Page** from scratch if configuration is corrupted
5. **Test on different environment** to rule out environment-specific issues

---

**Created:** 2025-10-21
**Status:** BLOCKED - Awaiting expert consultation
**Priority:** HIGH - Blocks sprint completion
