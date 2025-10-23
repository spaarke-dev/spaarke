# Custom Page Parameter Debugging

**Issue:** Custom Page opens but shows "Context missing. Close and retry."
**Root Cause:** Parameters are not being passed from Custom Page to PCF control

---

## Diagnostic Steps

### Step 1: Check What Parameters Are Being Sent

Open the browser console and look at the console logs when you click "Upload Documents":

**Expected to see:**
```
[Spaarke] Opening Custom Page Dialog with parameters: {...}
[Spaarke] client.getAppId() not available in this context (ribbon commands), using default app resolution
[Spaarke] Page Input: {pageType: "custom", name: "sprk_documentuploaddialog_e52db", parentEntityName: {value: "sprk_matter"}, ...}
```

**What to verify:**
- Are all 4 parameters present? (parentEntityName, parentRecordId, containerId, parentDisplayName)
- Are they wrapped in `{ value: "..." }` format?

---

### Step 2: Add Debugging to Custom Page

In the Custom Page editor, you need to temporarily add debugging to see if parameters are being received.

#### Option A: Add Label to Screen (Temporary Debug)

1. Open Custom Page in editor
2. Add a **Label** control to the screen (temporary)
3. Set the Label's Text property to:
   ```powerfx
   "parentRecordId: " & Param("parentRecordId") & " | " &
   "parentEntityName: " & Param("parentEntityName") & " | " &
   "containerId: " & Param("containerId") & " | " &
   "parentDisplayName: " & Param("parentDisplayName")
   ```
4. Publish the Custom Page
5. Test - you should see the parameter values displayed in the label

**Expected Result:**
```
parentRecordId: 3A785F76-C773-F011-B4CB-6045BDD8B757 | parentEntityName: sprk_matter | containerId: b!yLRd... | parentDisplayName: 345345345345
```

**If you see blank values:**
- Parameters are not being passed correctly from web resource
- Check that web resource was uploaded with `{ value: "..." }` format

**If you see the values:**
- Parameters ARE being passed correctly
- Problem is in Phase 2 configuration (Screen.OnVisible not setting variables)

---

### Step 3: Verify Phase 2 Configuration

You mentioned you completed Phase 2. Let me verify what should be configured:

#### App.OnStart - Should be:
```powerfx
Set(varAppInitialized, true)
```

**Just this one line - nothing else!**

#### Screen1.OnVisible - Should be:
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

#### PCF Control Properties - Should be bound to variables:

The PCF control on the Custom Page should have these property bindings:

- **parentEntityName:** `varParentEntityName`
- **parentRecordId:** `varParentRecordId`
- **containerId:** `varContainerId`
- **parentDisplayName:** `varParentDisplayName`
- **sdapApiBaseUrl:** Your API URL (e.g., `"https://spe-api-dev-67e2xz.azurewebsites.net"`)

#### PCF Control Visible Property - Should be:
```powerfx
!IsBlank(varParentRecordId) && !IsBlank(varContainerId)
```

This ensures the PCF control doesn't render until the parameters have been loaded into variables.

---

## Most Likely Issue

Based on the error "Context missing. Close and retry.", the most likely issue is:

**The PCF control is rendering before the variables are set.**

This happens if:
1. PCF Visible property is not gated with `!IsBlank(varParentRecordId) && !IsBlank(varContainerId)`
2. Screen.OnVisible is not setting the variables
3. PCF properties are bound to `Param()` directly instead of to variables

---

## Quick Fix to Test

### Temporary Test: Bind PCF Directly to Param()

To test if the parameters are being received at all, temporarily change the PCF control properties to bind directly to `Param()`:

**PCF Control Properties (TEMPORARY TEST ONLY):**
- **parentEntityName:** `Param("parentEntityName")`
- **parentRecordId:** `Param("parentRecordId")`
- **containerId:** `Param("containerId")`
- **parentDisplayName:** `Param("parentDisplayName")`

**Publish and test.**

**If this works:**
- Parameters are being passed correctly
- Problem is in the variable flow (Screen.OnVisible or variable bindings)

**If this still shows "Context missing":**
- Parameters are not being passed from web resource
- Check web resource format (should use `{ value: "..." }`)

---

## Expected Console Logs from PCF Control

When the Custom Page opens, you should see these logs in the browser console:

**Scenario 1: Parameters not hydrated yet (normal for first few calls):**
```
[UniversalDocumentUpload] Waiting for parameters to hydrate
```

**Scenario 2: Parameters hydrated successfully:**
```
[UniversalDocumentUpload] Parameters hydrated - initializing async
[UniversalDocumentUpload] Parent entity: sprk_matter
[UniversalDocumentUpload] Parent record ID: 3A785F76-C773-F011-B4CB-6045BDD8B757
[UniversalDocumentUpload] Container ID: b!yLRd...
```

**Scenario 3: Context loaded successfully:**
```
[UniversalDocumentUpload] Initialization complete
[UniversalDocumentUpload] Parent context loaded successfully
```

---

## Action Items

1. **Check browser console** - Are there any logs from `[UniversalDocumentUpload]`?
   - If NO logs at all → PCF not rendering or not loaded
   - If "Waiting for parameters to hydrate" only → Parameters not reaching PCF
   - If "Parameters hydrated" appears → Check what values it received

2. **Add temporary debug label** to Custom Page showing `Param()` values
   - If blank → Web resource parameter format wrong
   - If populated → Phase 2 configuration wrong

3. **Verify Phase 2 configuration** exactly as specified above

4. **Report back** with:
   - What console logs you see (if any)
   - What the debug label shows (if you add it)
   - Current Screen.OnVisible formula
   - Current PCF property bindings

---

**Created:** 2025-10-21
**Status:** Diagnostic guide for "Context missing" error
