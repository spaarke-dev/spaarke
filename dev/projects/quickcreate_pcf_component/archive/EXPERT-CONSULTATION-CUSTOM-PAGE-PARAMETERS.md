# Expert Consultation: Custom Page Parameters Not Flowing to PCF Control

**Date:** 2025-10-21
**Environment:** SPAARKE DEV 1 (Dataverse)
**Custom Page:** sprk_documentuploaddialog_e52db
**PCF Control:** Spaarke.Controls.UniversalDocumentUpload v3.0.1

---

## Objective

Migrate Universal Document Upload functionality from Quick Create form (using sprk_uploadcontext utility entity) to **Custom Page dialog** approach.

### What We're Trying to Achieve

1. User clicks "Upload Documents" ribbon button on Documents subgrid
2. Ribbon command calls `Xrm.Navigation.navigateTo()` to open Custom Page as a side panel dialog
3. Custom Page receives 4 parameters via `navigateTo()`:
   - `parentEntityName` (e.g., "sprk_matter")
   - `parentRecordId` (GUID without braces)
   - `containerId` (SharePoint Embedded Container ID)
   - `parentDisplayName` (display name for UI header)
4. Custom Page passes these parameters to embedded PCF control
5. PCF control uses parameters to initialize upload context and render upload form

---

## Current Issue

**Parameters are NOT flowing from navigateTo() to the Custom Page.**

### Evidence

**Web Resource (sprk_subgrid_commands.js) - Working Correctly:**
```javascript
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    parentEntityName: 'sprk_matter',              // ✓ Passing correctly
    parentRecordId: '3A785F76-C773-F011-B4CB-6045BDD8B757',  // ✓ Passing correctly
    containerId: 'b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50',  // ✓ Passing correctly
    parentDisplayName: '345345345345'             // ✓ Passing correctly
};

const navigationOptions = {
    target: 2,      // Dialog
    position: 2,    // Right side pane
    width: { value: 640, unit: 'px' }
};

Xrm.Navigation.navigateTo(pageInput, navigationOptions);
```

**Console Output Confirms Parameters Sent:**
```
[Spaarke] Page Input: {
  pageType: 'custom',
  name: 'sprk_documentuploaddialog_e52db',
  parentEntityName: 'sprk_matter',
  parentRecordId: '3A785F76-C773-F011-B4CB-6045BDD8B757',
  containerId: 'b!yLRd...',
  parentDisplayName: '345345345345'
}
```

**Custom Page - NOT Receiving Parameters:**

When testing with a debug label in the Custom Page:
```powerfx
"Param: " & Param("parentEntityName") & " | Var: " & varParentEntityName
```

**Result:** Both `Param("parentEntityName")` and `varParentEntityName` are **BLANK/EMPTY**.

**PCF Control Console Logs:**
```
[UniversalDocumentUpload] Initializing PCF control v3.0.1 (IDEMPOTENT UPDATEVIEW FIX)
[UniversalDocumentUpload] Waiting for parameters to hydrate {
  hasEntityName: false,
  hasRecordId: false,
  hasContainerId: false
}
```

All parameters remain `false` (empty) indefinitely - `updateView()` is called multiple times but parameters never populate.

---

## Custom Page Configuration

### Current Setup

**Custom Page Name:** sprk_documentuploaddialog_e52db (logical name with Dataverse suffix)

**Screen.OnVisible Formula:**
```powerfx
Set(varParentEntityName, Param("parentEntityName"));
Set(varParentRecordId, Param("parentRecordId"));
Set(varContainerId, Param("containerId"));
Set(varParentDisplayName, Param("parentDisplayName"))
```

**PCF Control Property Bindings:**
- parentEntityName: `varParentEntityName`
- parentRecordId: `varParentRecordId`
- containerId: `varContainerId`
- parentDisplayName: `varParentDisplayName`
- sdapApiBaseUrl: `"https://spe-api-dev-67e2xz.azurewebsites.net/api"`

**PCF Control Visible:** `true`

**App.OnStart:**
```powerfx
Set(varAppInitialized, true)
```

### Variables Defined

In Custom Page Variables panel, we see:
- varAppInitialized (Boolean: true)
- varContainerId (Text: Blank)
- varParentDisplayName (Text: Blank)
- varParentEntityName (Text: Blank)
- varParentRecordId (Text: Blank)

All text variables remain blank after navigateTo() is called.

---

## What We've Tried (All Failed)

### Attempt 1: Direct Param() Binding
**Tried:** Bind PCF properties directly to `Param()` instead of variables:
```powerfx
parentEntityName: Param("parentEntityName")
```
**Result:** PCF still receives empty values

### Attempt 2: If(!varInit) Gating Pattern
**Tried:** Use initialization flag to prevent re-execution:
```powerfx
If(!varInit,
    Set(varInit, true);
    Set(varParentEntityName, Param("parentEntityName"));
    ...
)
```
**Result:** Variables remain blank (OnVisible might not fire or Param() returns empty)

### Attempt 3: Object Wrapper Format
**Tried:** Pass parameters wrapped in `{value: "..."}` objects:
```javascript
pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    parentEntityName: { value: 'sprk_matter' },
    ...
}
```
**Result:** Power Apps formula error: "The '.' operator cannot be used on Text values"

### Attempt 4: Text() Function Extraction
**Tried:** Extract parameter values using `Text()` function:
```powerfx
Set(varParentEntityName, Text(Param("parentEntityName")));
```
**Result:** Variables remain blank

### Attempt 5: Remove and Re-add PCF Control
**Tried:** Delete PCF control from Custom Page, save, re-add, reconfigure bindings
**Result:** No change - parameters still empty

### Attempt 6: PCF Version Bump
**Tried:** Update PCF manifest from v3.0.0 to v3.0.1 to force cache refresh
**Result:** New version loads correctly, but parameters still empty

### Attempt 7: Cache Clearing
**Tried:**
- Hard refresh (Ctrl+Shift+R)
- Clear browser cache, localStorage, sessionStorage, IndexedDB
- Incognito window
- Clear Service Workers
**Result:** No change

### Attempt 8: Republish Custom Page
**Tried:** Save and Publish Custom Page multiple times
**Result:** No change

---

## Missing Configuration: Custom Page Input Parameters?

### The Core Question

**In the Custom Page Settings UI, we CANNOT find where to define input parameters.**

**What we checked:**
1. ⚙️ Settings gear icon → No "Parameters" section visible
2. Variables panel → Shows Global variables, Named formulas, Context variables, Collections - but no "Input Parameters" section
3. App properties → No parameter configuration options
4. Screen properties → No parameter definitions

**What Microsoft documentation says:**

Custom Pages support input parameters for scenarios like this. Parameters should be:
1. **Defined in the Custom Page** (as input parameters)
2. **Passed via navigateTo()** (which we're doing correctly)
3. **Accessed via Param() function** (which returns empty)

**Question for Expert:**

> **How do we define/register input parameters for a Custom Page in the current Power Apps environment?**
>
> - Is there a specific version of Power Apps where the Parameters UI is available?
> - Do Custom Pages created a certain way (e.g., from template) have parameters vs. those created blank?
> - Is there a solution XML edit required to add parameter definitions?
> - Could there be a feature flag or environment setting blocking parameter functionality?

---

## Environment Details

**Power Apps Version:** (visible in Custom Page editor) - need to check exact build

**Dataverse Environment:** SPAARKE DEV 1
- Organization: spaarkedev1.crm.dynamics.com
- Region: North America

**Custom Page Creation Method:**
- Created via: Solutions → UniversalQuickCreate → New → Custom Page
- Template used: Blank page
- **Question:** Should we have used a different template or creation method to enable parameters?

**Solution Details:**
- Solution Name: UniversalQuickCreate
- Publisher: Spaarke (prefix: sprk)
- Version: 3.0.0.0

---

## PCF Control Details (Working Correctly)

The PCF control itself is functioning correctly - it's implementing the idempotent `updateView()` pattern:

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Extract parameter values
    const parentEntityName = context.parameters.parentEntityName?.raw ?? "";
    const parentRecordId = context.parameters.parentRecordId?.raw ?? "";
    const containerId = context.parameters.containerId?.raw ?? "";

    // Only initialize once when all required params are present
    if (!this._initialized) {
        if (parentEntityName && parentRecordId && containerId) {
            this._initialized = true;
            this.initializeAsync(context);  // Initialize now that params are ready
        } else {
            // Params not ready yet - wait for next updateView call
            console.log('Waiting for parameters to hydrate', {
                hasEntityName: !!parentEntityName,
                hasRecordId: !!parentRecordId,
                hasContainerId: !!containerId
            });
            return;  // Don't throw - just wait
        }
    }
}
```

**This pattern works perfectly** - the PCF waits patiently for parameters. The issue is that **parameters never arrive** because the Custom Page isn't passing them.

---

## Expected Behavior (What Should Happen)

### Successful Flow:

1. **User clicks "Upload Documents"** → Ribbon command executes
2. **navigateTo() called** with parameters → Custom Page opens on right side (position 2)
3. **Custom Page opens** → App.OnStart runs
4. **Screen.OnVisible fires** → Param() reads parameters, sets variables
5. **PCF control renders** → updateView() called with empty params first
6. **Parameters hydrate** → updateView() called again with populated params
7. **PCF initializes** → Shows upload form

### Current Flow:

1. ✓ User clicks "Upload Documents" → Ribbon command executes
2. ✓ navigateTo() called with parameters → Custom Page opens on right side
3. ✓ Custom Page opens → App.OnStart runs
4. ❌ **Screen.OnVisible fires** → `Param()` returns **EMPTY** for all parameters
5. ✓ PCF control renders → updateView() called with empty params
6. ❌ **Parameters NEVER hydrate** → updateView() keeps getting empty params
7. ❌ PCF never initializes → Shows blank page

---

## Questions for Expert

1. **How do we define input parameters for a Custom Page in the current Power Apps Maker Portal?**
   - Is there a UI we're missing?
   - Does it require editing solution XML?
   - Is there a feature flag that needs to be enabled?

2. **Are Custom Page parameters supported for this use case (dialog opened via navigateTo)?**
   - Documentation suggests yes, but maybe there's a limitation?
   - Do parameters only work for embedded Custom Pages vs. dialog Custom Pages?

3. **Could the issue be related to how the Custom Page was created?**
   - Should we recreate it using a specific template?
   - Is there a "Custom Page with Parameters" template we should use?

4. **Is there an alternative pattern we should use?**
   - Should we use a different approach (e.g., global variables, context, or URL parameters)?
   - Is there a recommended Microsoft pattern for passing context to Custom Page dialogs?

5. **Environment/Version Specific:**
   - Could this be a version-specific issue with our Dataverse/Power Apps environment?
   - Are there known issues with Custom Page parameters in certain builds?

---

## Files for Reference

**Web Resource:** `C:\code_files\spaarke\sprk_subgrid_commands.js`
- Lines 283-329: `openDocumentUploadDialog()` function with navigateTo() call

**PCF Control:** `C:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate\index.ts`
- Lines 95-125: `init()` method
- Lines 341-375: `updateView()` method (idempotent pattern)

**PCF Manifest:** `C:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate\ControlManifest.Input.xml`
- Lines 17-47: Parameter definitions (parentEntityName, parentRecordId, containerId, parentDisplayName)

---

## Success Criteria

When working correctly, we should see:

**Console Logs:**
```
[UniversalDocumentUpload] Initializing PCF control v3.0.1
[UniversalDocumentUpload] Waiting for parameters to hydrate {hasEntityName: false, hasRecordId: false, hasContainerId: false}
[UniversalDocumentUpload] Parameters hydrated - initializing async {
  parentEntityName: 'sprk_matter',
  parentRecordId: '3A785F76-C773-F011-B4CB-6045BDD8B757',
  containerId: 'b!yLRd...'
}
[UniversalDocumentUpload] Initialization complete
```

**Visual:**
- Right-side panel opens (640px width)
- Green version badge visible: "✓ V3.0.1 - IDEMPOTENT UPDATEVIEW - NO INIT CALL"
- Upload form renders with file picker and buttons

---

## Additional Context

This migration is part of Sprint 5 v3.0.0 to eliminate the sprk_uploadcontext utility entity approach and use Custom Pages instead, which is the recommended Microsoft pattern for custom dialogs in Model-Driven Apps.

The PCF control works perfectly in test harness with hardcoded parameters. The ONLY issue is parameter flow from navigateTo() → Custom Page → PCF.

---

**Created:** 2025-10-21
**Status:** Blocked - awaiting expert guidance on Custom Page parameter configuration
