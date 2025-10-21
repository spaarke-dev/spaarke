# Remedial Plan Checklist - Custom Page Dialog Fix

**Date:** 2025-10-21
**Based On:** Custom-Page-Dialog-Diagnostic-and-Fix-10-21-2025.md
**Status:** 📋 PENDING APPROVAL - DO NOT EXECUTE UNTIL REVIEWED

---

## 🎯 Root Causes Identified

1. **❌ Custom Page NOT added to Model-Driven App** (CRITICAL - you discovered this!)
2. **❌ Parameter hydration race condition** - Param() values are blank in App.OnStart
3. **❌ Early validation guards** - Back() calls in App.OnStart close dialog before params load
4. **❌ PCF throws on missing inputs** - Control throws error before parameters are hydrated
5. **❌ Missing appId in navigateTo** - Can cause edge-case resolution issues
6. **❌ Direct Param() binding to PCF** - Should use variables instead

---

## ✅ Remedial Plan Overview

### Phase 1: Infrastructure (Power Apps Configuration)
- Add Custom Page to Model-Driven App
- Verify page is published and accessible

### Phase 2: Custom Page Canvas Layer
- Move parameter reading from App.OnStart to Screen.OnVisible
- Add visibility gating for PCF control
- Remove early Back()/Exit() guards
- Implement proper Exit() with result payload

### Phase 3: Web Resource (Launcher)
- Add appId to navigateTo call
- Adjust dialog position and width per recommendations
- Add result handling

### Phase 4: PCF Control (TypeScript)
- Make updateView idempotent
- Never throw on missing inputs
- Initialize only when all required inputs present

### Phase 5: Testing & Validation
- Test with Power Apps Monitor
- Verify parameter flow
- Validate result payload

---

## 📋 DETAILED CHECKLIST

### ⬜ PHASE 1: Infrastructure Setup (15 minutes)

#### ⬜ 1.1: Add Custom Page to Model-Driven App
**Priority:** 🔴 CRITICAL (This is why dialog wasn't working!)

**Steps:**
1. Open Power Apps Maker Portal: https://make.powerapps.com
2. Go to **Apps** → Find your Model-Driven App (e.g., "Spaarke")
3. Click **Edit** (opens App Designer)
4. In left navigation, find **Pages** or **Custom Pages**
5. Click **+ Add page** → **Add existing page**
6. Search for: `sprk_documentuploaddialog_e52db`
7. **Add the page** (but don't add it to navigation - keep it hidden)
8. **Save** the app
9. **Publish** the app

**Verification:**
- [ ] Custom Page appears in App Designer's page list
- [ ] Page is NOT in navigation (hidden)
- [ ] App is published

**Why This Matters:**
Custom Pages must be explicitly added to the app to be accessible via navigateTo(). This was the fundamental issue!

---

### ⬜ PHASE 2: Custom Page Canvas Layer Changes (30 minutes)

#### ⬜ 2.1: Clear App.OnStart
**File:** Custom Page → App object → OnStart property

**Current State:**
```powerfx
// Debug: Show that OnStart is running
Notify("🔧 Custom Page Loading...", NotificationType.Information);

// Extract parameters
Set(varParentRecordId, Param("parentRecordId"));
Set(varParentEntityName, Param("parentEntityName"));
Set(varContainerId, Param("containerId"));
Set(varParentDisplayName, Param("parentDisplayName"));

// ... validation and Back() calls ...
```

**New State:**
```powerfx
// Leave completely empty or just set a flag
Set(varAppInitialized, true)
```

**Rationale:** Param() values are not hydrated yet in OnStart. Reading them here gets empty values.

**Actions:**
- [ ] Open Custom Page editor
- [ ] Click **App** in tree view
- [ ] Find **OnStart** property
- [ ] **DELETE** all Param() reads
- [ ] **DELETE** all Notify() calls
- [ ] **DELETE** all Back() calls
- [ ] **OPTIONAL:** Add `Set(varAppInitialized, true)` if needed for diagnostics

---

#### ⬜ 2.2: Add Screen.OnVisible Parameter Hydration
**File:** Custom Page → Screen1 → OnVisible property

**Current State:** (Probably empty)

**New State:**
```powerfx
If(
    !varInit,
    Set(varInit, true);
    Set(varParentEntityName, Param("parentEntityName"));
    Set(varParentRecordId, Param("parentRecordId"));
    Set(varContainerId, Param("containerId"));
    Set(varParentDisplayName, Param("parentDisplayName"))
)
```

**Rationale:** Screen.OnVisible fires AFTER Param() hydration is complete.

**Actions:**
- [ ] Click **Screen1** (or Document Upload screen) in tree view
- [ ] Find **OnVisible** property in properties panel
- [ ] Paste the formula above
- [ ] **Verify semicolons** between Set() statements (Power Fx syntax)

---

#### ⬜ 2.3: Add Optional Error Notification (Non-Blocking)
**File:** Custom Page → Screen1 → OnVisible property (append to existing)

**Addition to OnVisible:**
```powerfx
If(
    !varInit,
    Set(varInit, true);
    Set(varParentEntityName, Param("parentEntityName"));
    Set(varParentRecordId, Param("parentRecordId"));
    Set(varContainerId, Param("containerId"));
    Set(varParentDisplayName, Param("parentDisplayName"))
);

// Show friendly error if context is missing (but don't close!)
If(
    varInit && (IsBlank(varParentRecordId) || IsBlank(varContainerId)),
    Notify("Context missing. Close and retry.", NotificationType.Error)
)
```

**Rationale:** Inform user of missing params but let them see the page (don't auto-close).

**Actions:**
- [ ] Append the error notification to Screen1.OnVisible
- [ ] **DO NOT** add Back() or Exit() here
- [ ] This is informational only

---

#### ⬜ 2.4: Wrap PCF Control in Container with Visibility Gate
**File:** Custom Page → Canvas

**Current State:** PCF control directly on screen

**New State:** PCF inside a Container with visibility condition

**Actions:**
- [ ] In Custom Page editor, click **Insert** → **Container**
- [ ] **Drag the PCF control** into the container
- [ ] Click on the **Container**
- [ ] Set Container **Visible** property to:
  ```powerfx
  !IsBlank(varParentEntityName) && !IsBlank(varParentRecordId) && !IsBlank(varContainerId)
  ```
- [ ] Resize container to full screen or desired dimensions

**Rationale:** Don't render PCF until all required parameters are present.

---

#### ⬜ 2.5: Verify PCF Property Bindings (Already Done)
**File:** Custom Page → UniversalDocumentUpload control → Properties

**Current State:** (Already configured - verify)

**Expected Values:**
- parentEntityName: `varParentEntityName`
- parentRecordId: `varParentRecordId`
- containerId: `varContainerId`
- parentDisplayName: `varParentDisplayName`
- sdapApiBaseUrl: `"https://spe-api-dev-67e2xz.azurewebsites.net"`

**Actions:**
- [ ] Click on **UniversalDocumentUpload** control
- [ ] Verify **Advanced** tab shows variables (NOT Param() calls)
- [ ] Confirm no outer quotes on variable names
- [ ] Confirm sdapApiBaseUrl has quotes (is a string literal)

**Status:** ✅ Already correct (per screenshot from earlier)

---

#### ⬜ 2.6: Add Exit() Call for Success Flow (Future Enhancement)
**File:** Custom Page → After successful upload (if you have a success handler)

**Recommended Addition:**
```powerfx
Exit({ status: "uploaded", count: varUploadedCount })
```

**Rationale:** Return structured result to launcher instead of undefined.

**Actions:**
- [ ] **SKIP FOR NOW** - This requires finding where upload success is handled
- [ ] **FUTURE:** Add Exit() call when upload completes successfully
- [ ] Document as enhancement for later sprint

**Priority:** 🟡 MEDIUM (Nice to have, not critical for initial fix)

---

#### ⬜ 2.7: Save and Publish Custom Page
**File:** Custom Page

**Actions:**
- [ ] Click **Save** in Custom Page editor
- [ ] Wait for save confirmation
- [ ] **Publish** button should appear - click it
- [ ] **DO NOT TEST YET** - Publish Model-Driven App first (Phase 1)

---

### ⬜ PHASE 3: Web Resource (Launcher) Updates (20 minutes)

#### ⬜ 3.1: Add appId to navigateTo Call
**File:** sprk_subgrid_commands.js (root directory)
**Function:** `openDocumentUploadDialog()`
**Lines:** ~280-310

**Current Code:**
```javascript
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    data: {
        parentEntityName: params.parentEntityName,
        parentRecordId: params.parentRecordId,
        containerId: params.containerId,
        parentDisplayName: params.parentDisplayName
    }
};
```

**Updated Code:**
```javascript
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    appId: Xrm.Utility.getGlobalContext().client.getAppId(), // ADD THIS LINE
    data: {
        parentEntityName: params.parentEntityName,
        parentRecordId: params.parentRecordId,
        containerId: params.containerId,
        parentDisplayName: params.parentDisplayName
    }
};
```

**Rationale:** Ensures platform resolves page in correct app context.

**Actions:**
- [ ] Open `sprk_subgrid_commands.js` in editor
- [ ] Find `openDocumentUploadDialog` function
- [ ] Locate the `pageInput` object definition
- [ ] Add `appId` line after `name` property
- [ ] Ensure proper comma syntax

---

#### ⬜ 3.2: Update Dialog Position and Width (Recommended)
**File:** sprk_subgrid_commands.js
**Function:** `openDocumentUploadDialog()`

**Current Code:**
```javascript
const navigationOptions = {
    target: 2,      // Dialog
    position: 1,    // Center
    width: { value: 800, unit: 'px' },
    height: { value: 600, unit: 'px' }
};
```

**Recommended Code:**
```javascript
const navigationOptions = {
    target: 2,      // Dialog
    position: 1,    // Right side (per diagnostic doc recommendation)
    width: { value: 640, unit: 'px' }  // Narrower, no height specified
    // height removed - let page control its height
};
```

**Rationale:** Diagnostic doc recommends right-side dialog at 640px width.

**Actions:**
- [ ] **OPTIONAL:** Change position to 1 (right side) if desired
- [ ] **OPTIONAL:** Change width from 800 to 640
- [ ] **OPTIONAL:** Remove height property
- [ ] **DECISION NEEDED:** Confirm desired position (center vs right) with team

**Priority:** 🟢 LOW (Optional visual preference)

---

#### ⬜ 3.3: Add Result Handling (Future Enhancement)
**File:** sprk_subgrid_commands.js
**Function:** `openDocumentUploadDialog()`

**Current Code:**
```javascript
Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
    function success(result) {
        console.log("[Spaarke] Custom Page Dialog closed successfully", result);

        if (selectedControl && typeof selectedControl.refresh === "function") {
            console.log("[Spaarke] Refreshing subgrid...");
            selectedControl.refresh();
        }
    },
    function error(err) { /* ... */ }
);
```

**Enhanced Code:**
```javascript
Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
    function success(result) {
        console.log("[Spaarke] Custom Page Dialog closed", result);

        // Check if Exit() returned a result
        if (result && result.status === "uploaded") {
            console.log("[Spaarke] Upload successful, count:", result.count);
            // Refresh only on successful upload
            if (selectedControl && typeof selectedControl.refresh === "function") {
                selectedControl.refresh();
            }
        } else if (result === undefined) {
            // User clicked Cancel or X
            console.log("[Spaarke] Dialog cancelled by user");
        }
    },
    function error(err) { /* ... */ }
);
```

**Rationale:** Handle structured Exit() payload from Custom Page.

**Actions:**
- [ ] **SKIP FOR NOW** - Requires Custom Page to implement Exit()
- [ ] **FUTURE:** Add after Phase 2.6 is implemented
- [ ] Document as enhancement

**Priority:** 🟡 MEDIUM (Depends on Phase 2.6)

---

#### ⬜ 3.4: Update Web Resource in Dataverse
**File:** sprk_subgrid_commands.js

**Actions:**
- [ ] Save changes to `sprk_subgrid_commands.js`
- [ ] Open Power Apps Maker Portal
- [ ] Go to Solutions → UniversalQuickCreate → Web Resources
- [ ] Find `sprk_subgrid_commands`
- [ ] Click → **Edit** → **Upload file**
- [ ] Select updated `sprk_subgrid_commands.js`
- [ ] Click **Save**
- [ ] Click **Publish**

---

### ⬜ PHASE 4: PCF Control Updates (45 minutes)

#### ⬜ 4.1: Make updateView Idempotent (Prevent Throwing on Missing Inputs)
**File:** src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
**Method:** `updateView()`

**Current Approach:** Control validates and throws in `loadParentContext()` if inputs are missing

**New Approach:** Wait for inputs to be present, don't throw

**Recommended Changes:**

1. **Add initialization flag:**
```typescript
private _initialized = false;
```

2. **Modify updateView:**
```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    logInfo('UniversalDocumentUpload', 'updateView called');

    // Extract parameter values
    const parentEntityName = context.parameters.parentEntityName?.raw ?? "";
    const parentRecordId = context.parameters.parentRecordId?.raw ?? "";
    const containerId = context.parameters.containerId?.raw ?? "";
    const parentDisplayName = context.parameters.parentDisplayName?.raw ?? "";

    // Only initialize once when all required params are present
    if (!this._initialized) {
        if (parentEntityName && parentRecordId && containerId) {
            logInfo('UniversalDocumentUpload', 'Initializing with parameters', {
                parentEntityName,
                parentRecordId,
                containerId,
                parentDisplayName
            });

            // Initialize parent context
            this.parentContext = {
                parentEntityName,
                parentRecordId,
                containerId,
                parentDisplayName: parentDisplayName || parentEntityName
            };

            this._initialized = true;
            this.renderControl();
        } else {
            // Params not ready yet - wait for next update
            logInfo('UniversalDocumentUpload', 'Waiting for parameters to hydrate');
            return;
        }
    }

    // Handle updates if needed (params changed, etc.)
    this.renderControl();
}
```

3. **Remove throwing from loadParentContext:**
```typescript
// DELETE OR COMMENT OUT this throw:
// throw new Error(`Invalid parentRecordId format...`);

// REPLACE WITH:
logWarning('UniversalDocumentUpload', 'Invalid parentRecordId format', parentRecordId);
return; // Exit gracefully
```

**Rationale:** Never throw in updateView when inputs are missing - just wait.

**Actions:**
- [ ] Open `index.ts`
- [ ] Add `_initialized` private field
- [ ] Refactor `updateView` to check inputs first
- [ ] Only initialize when all required inputs present
- [ ] Return early if not initialized
- [ ] Remove or soften validation throws in `loadParentContext`
- [ ] Log warnings instead of throwing

---

#### ⬜ 4.2: Update Logging for Parameter Wait State
**File:** src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts

**Actions:**
- [ ] Add log statement when waiting for params: `"Waiting for parameters to hydrate"`
- [ ] Add log statement when initialized: `"Initialized with parameters"`
- [ ] Change error logs to warnings for validation issues

---

#### ⬜ 4.3: Build and Deploy Updated PCF
**File:** PCF project

**Actions:**
- [ ] Save all TypeScript changes
- [ ] Run `npm run build` to build PCF
- [ ] Verify build succeeds with no errors
- [ ] Deploy via `pac pcf push --publisher-prefix sprk`
- [ ] **OR** use solution pack method from earlier
- [ ] Publish all customizations

---

### ⬜ PHASE 5: Testing & Validation (30 minutes)

#### ⬜ 5.1: Enable Power Apps Monitor
**Tool:** Power Apps Monitor

**Actions:**
- [ ] Open Power Apps Maker Portal
- [ ] Go to **Apps** → Your Model-Driven App
- [ ] Click **Play** to open the app
- [ ] In the app, click **Settings** (gear icon) → **Advanced Settings**
- [ ] Enable **Monitor** or open Monitor in separate tab
- [ ] **Filter:** Show Custom Page events

**Purpose:** See parameter hydration in real-time.

---

#### ⬜ 5.2: Test Dialog Opens and Stays Open
**Test:** Basic open/close

**Steps:**
1. [ ] Open a Matter record with Container ID
2. [ ] Open browser DevTools Console
3. [ ] Click "Upload Documents" button
4. [ ] **Verify:** Dialog appears and stays open
5. [ ] **Verify:** No console errors
6. [ ] **Verify:** Power Apps Monitor shows Custom Page loaded
7. [ ] **Verify:** PCF control is visible inside dialog

**Expected Result:**
- ✅ Dialog opens and remains open
- ✅ PCF control renders after ~100-500ms (after param hydration)
- ✅ Console shows: "Initialized with parameters"
- ✅ No "Parent context not loaded" error

**If Dialog Still Closes:**
- Check Power Apps Monitor for errors
- Check browser console for exceptions
- Verify Custom Page is in Model-Driven App
- Verify Model-Driven App is published

---

#### ⬜ 5.3: Test Parameter Flow
**Test:** Verify parameters reach PCF

**Steps:**
1. [ ] Add temporary label to Custom Page: `Text: varParentRecordId`
2. [ ] Open dialog
3. [ ] **Verify:** Label shows actual GUID (not empty, not "val")
4. [ ] Remove temporary label after test

**Expected Result:**
- ✅ Label shows: "3A785F76-C773-F011-B4CB-6045BDD8B757" (actual GUID)

---

#### ⬜ 5.4: Test File Upload Workflow
**Test:** End-to-end upload

**Steps:**
1. [ ] Open dialog
2. [ ] Select a test file
3. [ ] Click "Upload & Create"
4. [ ] **Verify:** Upload progress shows
5. [ ] **Verify:** Success message appears
6. [ ] **Verify:** Dialog closes (or stays open, depending on UX choice)
7. [ ] **Verify:** Subgrid refreshes
8. [ ] **Verify:** New document record appears

**Expected Result:**
- ✅ File uploads successfully
- ✅ Document record created
- ✅ Subgrid shows new record

---

#### ⬜ 5.6: Test Multi-Entity Support
**Test:** Verify works on all 5 entity types

**Steps:**
- [ ] Test on sprk_matter (already tested)
- [ ] Test on sprk_project
- [ ] Test on sprk_invoice
- [ ] Test on account
- [ ] Test on contact

**Expected Result:**
- ✅ All 5 entity types work correctly

---

### ⬜ PHASE 6: Documentation & Commit (15 minutes)

#### ⬜ 6.1: Update DEPLOYMENT-ISSUE-SUMMARY.md
**File:** dev/projects/quickcreate_pcf_component/DEPLOYMENT-ISSUE-SUMMARY.md

**Actions:**
- [ ] Add section: "Root Cause Found"
- [ ] Document: Custom Page not added to Model-Driven App
- [ ] Document: Parameter hydration race condition
- [ ] Add section: "Resolution Applied"
- [ ] List all phases completed

---

#### ⬜ 6.2: Create Test Results Report
**File:** dev/projects/quickcreate_pcf_component/REMEDIAL-PLAN-TEST-RESULTS.md

**Actions:**
- [ ] Document each test performed
- [ ] Include screenshots
- [ ] Note any issues encountered
- [ ] Confirm all acceptance criteria met

---

#### ⬜ 6.3: Commit All Changes
**Files to commit:**
- sprk_subgrid_commands.js (web resource with appId)
- index.ts (PCF with idempotent updateView)
- REMEDIAL-PLAN-CHECKLIST.md (this file, marked complete)
- REMEDIAL-PLAN-TEST-RESULTS.md (test results)
- DEPLOYMENT-ISSUE-SUMMARY.md (updated with resolution)

**Actions:**
- [ ] Review all changes
- [ ] Create comprehensive commit message
- [ ] Commit to git

---

## 📊 Estimated Time

| Phase | Duration | Priority |
|-------|----------|----------|
| Phase 1: Infrastructure | 15 min | 🔴 CRITICAL |
| Phase 2: Custom Page | 30 min | 🔴 CRITICAL |
| Phase 3: Web Resource | 20 min | 🟡 HIGH |
| Phase 4: PCF Control | 45 min | 🟡 HIGH |
| Phase 5: Testing | 30 min | 🟡 HIGH |
| Phase 6: Documentation | 15 min | 🟢 MEDIUM |
| **TOTAL** | **~2.5 hours** | |

---

## ⚠️ Critical Success Factors

1. **✅ Add Custom Page to Model-Driven App** - Without this, nothing will work
2. **✅ Publish Model-Driven App** - After adding page, MUST publish
3. **✅ Move Param() to Screen.OnVisible** - Don't read params in App.OnStart
4. **✅ Remove Back() guards** - Don't close dialog in OnStart
5. **✅ Gate PCF visibility** - Don't render until params are ready
6. **✅ Make PCF idempotent** - Don't throw on missing inputs

---

## 🚦 Go/No-Go Decision Points

**After Phase 1:**
- [ ] Custom Page appears in Model-Driven App
- [ ] App is published
- **If NO:** Stop and troubleshoot infrastructure

**After Phase 2:**
- [ ] Custom Page changes saved and published
- **If NO:** Review Power Fx syntax errors

**After Phase 3 & 4:**
- [ ] PCF builds without errors
- [ ] Web resource uploaded successfully
- **If NO:** Review build errors and fix

**After Phase 5.2:**
- [ ] Dialog opens and stays open
- **If NO:** Review Power Apps Monitor logs, check all previous phases

---

## 📞 Escalation Path

If issues persist after completing all phases:
1. Check Power Apps Monitor for runtime errors
2. Review Application Insights for BFF API errors
3. Consult Power Apps community forums
4. Contact Microsoft Support (Custom Pages are preview/early GA)

---

**Status:** 📋 AWAITING APPROVAL
**Next Step:** Review with team and approve before execution
**Estimated Completion:** 2.5 hours after approval

---

**Created:** 2025-10-21
**Author:** Claude Code + User Insights
**Version:** 1.0
