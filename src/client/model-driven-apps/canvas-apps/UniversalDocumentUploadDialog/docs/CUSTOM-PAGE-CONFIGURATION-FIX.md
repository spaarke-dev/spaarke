# Custom Page Configuration - COMPLETE FIX

**Date:** 2025-10-21
**Issue:** Parameters not flowing from navigateTo() to Custom Page
**Root Cause:** Parameters must be under `data` property + Power Apps formula syntax issues
**Status:** Ready to configure

---

## ✅ Fix #1: Web Resource (COMPLETED)

**File:** sprk_subgrid_commands.js

**Change:** Wrapped parameters under `data` property

```javascript
const pageInput = {
    pageType: 'custom',
    name: 'sprk_documentuploaddialog_e52db',
    data: {                                      // ✅ Parameters under 'data'
        parentEntityName: params.parentEntityName,
        parentRecordId: params.parentRecordId,
        containerId: params.containerId,
        parentDisplayName: params.parentDisplayName
    }
};

if (appId) {
    pageInput.appId = appId;
}
```

**Status:** ✅ COMPLETE - upload this web resource after testing Custom Page config

---

## ⏳ Fix #2: Custom Page App.OnStart

**What to do:** Declare variables with `Blank()` so the designer recognizes them

**Current (if exists):**
```powerfx
Set(varAppInitialized, true)
```

**NEW - Replace with:**
```powerfx
Set(varInit, false);
Set(varParentEntityName, Blank());
Set(varParentRecordId, Blank());
Set(varContainerId, Blank());
Set(varParentDisplayName, Blank())
```

**Why:** This initializes all variables as empty text values, preventing "Name isn't valid" errors in the designer.

**Steps:**
1. Open Custom Page in Power Apps editor
2. Select **App** in the left tree view
3. Find **OnStart** property
4. Replace the formula with the above
5. DON'T save yet - continue to next fixes

---

## ⏳ Fix #3: Custom Page Screen.OnVisible

**What to do:** Use `Param("data")` to access all parameters as a single object

**Current (WRONG - doesn't work):**
```powerfx
If(!varInit,
    Set(varInit, true);
    Set(varParentEntityName, Param("parentEntityName"));
    ...
)
```

**NEW - Replace with:**
```powerfx
If(
    Not(_init),
    Set(_init, true);
    Set(_params, Param("data"));
    Set(varParentEntityName, Coalesce(_params.parentEntityName, ""));
    Set(varParentRecordId, Coalesce(_params.parentRecordId, ""));
    Set(varContainerId, Coalesce(_params.containerId, ""));
    Set(varParentDisplayName, Coalesce(_params.parentDisplayName, ""));
    If(
        Or(IsBlank(varParentRecordId), IsBlank(varContainerId)),
        Notify("Missing parameters", NotificationType.Error);
        Exit(false)
    )
)
```

**Key Changes:**
- `Param("data")` returns the entire data object from navigateTo()
- Access individual fields via `_params.parentEntityName`, `_params.parentRecordId`, etc.
- Use `Coalesce()` to provide empty string fallback
- Add validation to show error if required parameters missing

**Steps:**
1. Select **Screen1** in the left tree view
2. Find **OnVisible** property
3. Replace the formula with the above
4. DON'T save yet - continue to next fix

---

## ⏳ Fix #4: PCF Control Visible Property

**What to do:** Gate PCF visibility until parameters are ready

**Current:**
```powerfx
true
```

**NEW - Replace with:**
```powerfx
And(
    Not(IsBlank(varParentEntityName)),
    Not(IsBlank(varParentRecordId)),
    Not(IsBlank(varContainerId))
)
```

**Key Changes:**
- `&&` → `And()` (Power Apps syntax)
- `!IsBlank()` → `Not(IsBlank())` (Power Apps syntax)

**Why:** Prevents PCF from rendering before parameters are hydrated

**Steps:**
1. Select the **UniversalDocumentUpload** control
2. Find **Visible** property
3. Replace with the above formula
4. NOW save and publish

---

## ⏳ Fix #5: Verify PCF Property Bindings

**Ensure these are set correctly:**

Select the **UniversalDocumentUpload** control and verify:

- **parentEntityName:** `varParentEntityName`
- **parentRecordId:** `varParentRecordId`
- **containerId:** `varContainerId`
- **parentDisplayName:** `varParentDisplayName`
- **sdapApiBaseUrl:** `"https://spe-api-dev-67e2xz.azurewebsites.net/api"`

**Important:** These should bind to **variables**, NOT directly to `Param()`

---

## 📝 Summary of ALL Custom Page Formulas

### App.OnStart
```powerfx
Set(_init, false);
Set(varParentEntityName, Blank());
Set(varParentRecordId, Blank());
Set(varContainerId, Blank());
Set(varParentDisplayName, Blank())
```

### Screen1.OnVisible
```powerfx
If(
    Not(_init),
    Set(_init, true);
    Set(_params, Param("data"));
    Set(varParentEntityName, Coalesce(_params.parentEntityName, ""));
    Set(varParentRecordId, Coalesce(_params.parentRecordId, ""));
    Set(varContainerId, Coalesce(_params.containerId, ""));
    Set(varParentDisplayName, Coalesce(_params.parentDisplayName, ""));
    If(
        Or(IsBlank(varParentRecordId), IsBlank(varContainerId)),
        Notify("Missing parameters", NotificationType.Error);
        Exit(false)
    )
)
```

### UniversalDocumentUpload.Visible
```powerfx
And(
    Not(IsBlank(varParentEntityName)),
    Not(IsBlank(varParentRecordId)),
    Not(IsBlank(varContainerId))
)
```

### UniversalDocumentUpload Properties
- parentEntityName: `varParentEntityName`
- parentRecordId: `varParentRecordId`
- containerId: `varContainerId`
- parentDisplayName: `varParentDisplayName`
- sdapApiBaseUrl: `"https://spe-api-dev-67e2xz.azurewebsites.net/api"`

---

## 🧪 Testing Procedure

### Step 1: Configure Custom Page (DO THIS FIRST)

1. Open Custom Page in Power Apps editor
2. Apply ALL the formula changes above:
   - App.OnStart
   - Screen.OnVisible
   - PCF Visible property
   - Verify PCF property bindings
3. **Save** the Custom Page
4. **Publish** the Custom Page
5. Close the editor

### Step 2: Upload Web Resource

1. Open Power Apps Maker Portal
2. Go to Solutions → UniversalQuickCreate → Web Resources
3. Find **sprk_subgrid_commands**
4. Click Edit → Upload file
5. Select: `C:\code_files\spaarke\sprk_subgrid_commands.js`
6. Save and Publish

### Step 3: Test End-to-End

1. **Hard refresh** browser (Ctrl+Shift+R)
2. Open a Matter record with a Container ID
3. Scroll to Documents subgrid
4. Click **"Upload Documents"** button

### Expected Results ✅

**Console Logs:**
```
[Spaarke] Page Input: {
  pageType: 'custom',
  name: 'sprk_documentuploaddialog_e52db',
  data: {                                        // ✅ Under 'data'
    parentEntityName: 'sprk_matter',
    parentRecordId: '3A785F76...',
    containerId: 'b!yLRd...',
    parentDisplayName: '345345345345'
  },
  appId: '...'
}

[UniversalDocumentUpload] Initializing PCF control v3.0.1
[UniversalDocumentUpload] Waiting for parameters to hydrate {hasEntityName: false, hasRecordId: false, hasContainerId: false}
[UniversalDocumentUpload] Waiting for parameters to hydrate {hasEntityName: false, hasRecordId: false, hasContainerId: false}
[UniversalDocumentUpload] Parameters hydrated - initializing async {  // ✅ SHOULD SEE THIS!
  parentEntityName: 'sprk_matter',
  parentRecordId: '3A785F76...',
  containerId: 'b!yLRd...'
}
[UniversalDocumentUpload] Initialization complete
```

**Visual:**
- ✅ Right-side panel opens (640px width)
- ✅ Green version badge: "✓ V3.0.1 - IDEMPOTENT UPDATEVIEW - NO INIT CALL"
- ✅ Upload form renders with file picker
- ✅ No blank screen
- ✅ No "Context missing" error

---

## 🐛 Troubleshooting

### If parameters are STILL empty:

**Check #1: Web resource uploaded?**
Run this in browser console after uploading:
```javascript
Xrm.WebApi.retrieveMultipleRecords("webresource", "?$filter=name eq 'sprk_subgrid_commands'&$select=modifiedon")
  .then(r => console.log("Last modified:", new Date(r.entities[0].modifiedon)));
```
Should show recent timestamp (within last few minutes).

**Check #2: Custom Page formulas correct?**
In Custom Page editor, verify:
- App.OnStart uses `Blank()` not `""`
- Screen.OnVisible uses `Not()` not `!`
- PCF Visible uses `And()` and `Not()` not `&&` and `!`

**Check #3: Case-sensitive parameter names**
The keys in `data` object MUST exactly match Param() calls:
- `parentEntityName` ✅ (camelCase)
- `ParentEntityName` ❌ (wrong case)
- `parententityname` ❌ (wrong case)

**Check #4: Hard refresh**
- Close Custom Page dialog
- Press Ctrl+Shift+R to hard refresh
- Try again

---

## 📋 Deployment Checklist

Before marking this task complete:

- [ ] Custom Page App.OnStart updated with Blank() initialization
- [ ] Custom Page Screen.OnVisible updated with Not() syntax
- [ ] PCF Visible property updated with And(Not(IsBlank())) syntax
- [ ] PCF property bindings verified (bind to variables)
- [ ] Custom Page saved and published
- [ ] Web resource uploaded with `data` wrapper
- [ ] Web resource published
- [ ] Hard refresh browser
- [ ] Test shows parameters flowing correctly
- [ ] Upload form renders successfully
- [ ] File upload works end-to-end
- [ ] Subgrid refreshes after upload

---

## 🎯 Success Criteria

When working correctly:

1. **Dialog opens on right side** (position 2)
2. **Green v3.0.1 banner visible**
3. **Console shows "Parameters hydrated"** log
4. **Upload form renders** with file picker
5. **Can select and upload files**
6. **Dialog closes after upload**
7. **Subgrid refreshes** showing new documents

---

**Created:** 2025-10-21
**Status:** Configuration guide ready - awaiting user to apply Custom Page changes
