# Final Diagnostic Checklist - Custom Page Parameters Not Working

**Date:** 2025-10-21
**Status:** Parameters under `data:` wrapper confirmed in web resource, but Custom Page not receiving them

---

## What We Know is Working ✅

1. ✅ Web resource passes parameters under `data:` wrapper
   ```javascript
   {
     pageType: 'custom',
     name: 'sprk_documentuploaddialog_e52db',
     data: {
       parentEntityName: 'sprk_matter',
       parentRecordId: '3A785F76...',
       containerId: 'b!yLRd...',
       parentDisplayName: '345345345345'
     }
   }
   ```

2. ✅ PCF manifest has `usage="input"` for all properties
3. ✅ PCF v3.0.1 deployed at 3:12 PM
4. ✅ Screen.OnVisible uses `Not(varInit)` syntax

---

## What's NOT Working ❌

- Custom Page shows: "Invalid page inputs, page might not work as expected. Details: Invalid input to custom page, input needs to be an object"
- PCF shows: "Missing parameters"
- Variables remain blank

---

## Complete Diagnostic Steps

### Step 1: Verify Web Resource is Actually Loaded

Run this in browser console:
```javascript
Xrm.WebApi.retrieveMultipleRecords("webresource", "?$filter=name eq 'sprk_subgrid_commands'&$select=modifiedon")
  .then(r => console.log("Web resource modified:", new Date(r.entities[0].modifiedon)));
```

**Expected:** Should show time after 2:54 PM today

---

### Step 2: Add Comprehensive Debug Label to Custom Page

1. Open Custom Page in Power Apps editor
2. Add a **Label** control
3. Set its **Text** property to:

```powerfx
"=== DIAGNOSTIC ===" & Char(10) &
"varInit: " & Text(varInit) & Char(10) &
"Param(parentEntityName): " & Param("parentEntityName") & Char(10) &
"Param(parentRecordId): " & Param("parentRecordId") & Char(10) &
"Param(containerId): " & Param("containerId") & Char(10) &
"varParentEntityName: " & varParentEntityName & Char(10) &
"varParentRecordId: " & varParentRecordId & Char(10) &
"varContainerId: " & varContainerId
```

4. Save and Publish
5. Test - click "Upload Documents"

**What to look for:**
- If `Param(...)` shows values but `var...` is blank → Screen.OnVisible not firing
- If both are blank → Custom Page not receiving data
- If `varInit` is blank → App.OnStart didn't run

---

### Step 3: Verify Current Formulas

**Please copy/paste your EXACT current formulas:**

#### App.OnStart
Current formula: `[PASTE HERE]`

#### Screen.OnVisible
Current formula: `[PASTE HERE]`

#### PCF Visible
Current formula: `[PASTE HERE]`

#### PCF Properties
- parentEntityName: `[PASTE HERE]`
- parentRecordId: `[PASTE HERE]`
- containerId: `[PASTE HERE]`
- parentDisplayName: `[PASTE HERE]`

---

### Step 4: Check for Formula Errors in Custom Page

In the Custom Page editor:

1. Select **App** in tree view
2. Look at **OnStart** property - any red error icons? ⚠️
3. Select **Screen1** in tree view
4. Look at **OnVisible** property - any red error icons? ⚠️
5. Select **PCF control** in tree view
6. Look at **Visible** property - any red error icons? ⚠️

**Report:** Any errors showing? If yes, what do they say?

---

### Step 5: Verify PCF Control Version

In the Custom Page editor:

1. Click on the PCF control
2. Look at the top of the properties panel
3. What does it say for the control name/version?

**Expected:** Should show v3.0.1 or reference to usage="input"

---

### Step 6: Test Param() Function Directly

Try this as a TEMPORARY test:

1. In Custom Page, set the **PCF parentEntityName property** to:
   ```powerfx
   Param("parentEntityName")
   ```
   (Direct binding, no variable)

2. Do the same for the other 3 properties:
   ```powerfx
   Param("parentRecordId")
   Param("containerId")
   Param("parentDisplayName")
   ```

3. Set PCF Visible to: `true`

4. Save, Publish, Test

**Question:** Does the PCF now receive the parameters directly?
- If YES → Problem is in the variable flow (App.OnStart/Screen.OnVisible)
- If NO → Problem is Param() itself not working in this environment

---

### Step 7: Check Custom Page Type

In the solution:

1. Go to Solutions → UniversalQuickCreate
2. Find the Custom Page: `sprk_documentuploaddialog_e52db`
3. Look at its properties
4. What **type** of Custom Page is it?

**Question:** Was it created as:
- [ ] Custom Page (Dialog)
- [ ] Custom Page (Standard)
- [ ] Other?

---

### Step 8: Verify Custom Page is Added to Model-Driven App

1. Open the Model-Driven App in App Designer
2. Go to Pages
3. Is `sprk_documentuploaddialog_e52db` listed there?
4. Is it published?

**Expected:** Should be listed (even if not in navigation)

---

## Possible Root Causes

### A) Power Apps Environment Issue
Some environments have issues with `Param()` in dialog Custom Pages. This might require:
- Using a different approach (global variables, context)
- Creating the Custom Page differently
- Environment update/configuration

### B) Custom Page Created Wrong Way
If the Custom Page wasn't created specifically as a "dialog" type, it might not support parameters via navigateTo().

**Solution:** May need to recreate the Custom Page using a specific template

### C) Parameter Name Case Sensitivity
The keys in `data:` must EXACTLY match the `Param()` calls.

**Verify:**
- Web resource uses: `parentEntityName` (camelCase)
- Custom Page uses: `Param("parentEntityName")` (exact match)

### D) Timing Issue
`Param()` might not be available when App.OnStart or Screen.OnVisible fires.

**Solution:** May need to use a Timer control or different trigger

---

## Next Steps

**Please provide:**

1. Screenshot or text of the debug label output
2. Copy/paste of all formulas (App.OnStart, Screen.OnVisible, PCF Visible, PCF properties)
3. Result of Step 6 (direct Param() binding test)
4. Any formula errors showing in the Custom Page editor

With this information, we can pinpoint exactly where the flow is breaking.

---

**Created:** 2025-10-21
**Status:** Awaiting diagnostic results
