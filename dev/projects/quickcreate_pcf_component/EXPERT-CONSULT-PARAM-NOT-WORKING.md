# Expert Consultation: Custom Page Param() Not Retrieving Parameters

## Problem Summary

We've successfully implemented `navigateTo()` with the `parameters:` property as you recommended, and it's sending the data correctly. However, **`Param()` in the Custom Page is returning empty/blank values**.

---

## Current Status

### ✅ What's Working:

1. **Web resource sends parameters correctly:**
```javascript
const pageInput = {
    pageType: "custom",
    name: "sprk_documentuploaddialog_e52db",
    parameters: {
        parentEntityName: "sprk_matter",
        parentRecordId: "3a785f76-c773-f011-b4cb-6045bdd8b757",  // ✅ cleaned: lowercase, no braces
        containerId: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50",
        parentDisplayName: "345345345345"
    },
    appId: null
};

Xrm.Navigation.navigateTo(pageInput, navigationOptions);
```

2. **Console confirms parameters are sent:**
```
[Spaarke] Page Input: {
  pageType: 'custom',
  name: 'sprk_documentuploaddialog_e52db',
  parameters: {
    containerId: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
    parentDisplayName: "345345345345"
    parentEntityName: "sprk_matter"
    parentRecordId: "3a785f76-c773-f011-b4cb-6045bdd8b757"
  },
  appId: null
}
```

3. **`navigateTo()` succeeds with no errors:**
```
[Spaarke] Custom Page Dialog closed successfully null
```

4. **Custom Page opens correctly** (right-side pane, 640px width)

### ❌ What's NOT Working:

**Custom Page `Param()` calls return empty/blank values:**

**Screen.OnVisible formula:**
```powerfx
If(
    Not(_init),
    Set(_init, true);
    Set(varParentEntityName, Param("parentEntityName"));
    Set(varParentRecordId, Param("parentRecordId"));
    Set(varContainerId, Param("containerId"));
    Set(varParentDisplayName, Param("parentDisplayName"))
)
```

**Result:**
- All variables (`varParentEntityName`, `varParentRecordId`, etc.) are **blank/empty**
- PCF control properties bound to these variables receive empty values
- PCF shows header but no form fields (because parameters are blank)

---

## What We've Tried

### Attempt 1: Using `data:` Property
**Code:**
```javascript
const pageInput = {
    pageType: "custom",
    name: "sprk_documentuploaddialog_e52db",
    data: {
        parentEntityName: "sprk_matter",
        ...
    }
};
```

**Result:** ❌ "Invalid input to custom page, input needs to be an object" error

**Your Explanation:** `data:` triggers Dataverse validation against declared Custom Page inputs. Since we haven't declared inputs, it fails.

### Attempt 2: Using `parameters:` Property (Current)
**Code:**
```javascript
const pageInput = {
    pageType: "custom",
    name: "sprk_documentuploaddialog_e52db",
    parameters: {
        parentEntityName: "sprk_matter",
        ...
    }
};
```

**Result:** ✅ No errors, Custom Page opens, but ❌ `Param()` returns empty values

**Custom Page Formula:**
```powerfx
Set(varParentEntityName, Param("parentEntityName"));  // Returns empty
```

### Attempt 3: Tried ParseJSON (when we thought it was JSON string)
**Code:**
```powerfx
Set(_dataParsed, ParseJSON(Param("data")));
```

**Result:** ❌ Syntax errors in Power Apps

---

## Environment Details

- **Power Apps Environment:** SPAARKE DEV 1
- **Custom Page:** sprk_documentuploaddialog_e52db
- **PCF Control:** UniversalDocumentUpload v3.0.2
- **PCF Manifest:** All properties use `usage="input"`
- **Model-Driven App:** Spaarke (Custom Page is added to the app)

---

## Questions for Expert

### Question 1: How to Access `parameters:` in Custom Page?

We're using:
```powerfx
Param("parentEntityName")
```

But it returns empty. Should we use:
- A different function? (e.g., `Parameters("parentEntityName")`)
- A different syntax? (e.g., `Param().parentEntityName`)
- Something else entirely?

### Question 2: Do We Need to Declare Inputs?

You mentioned that `parameters:` doesn't require declaring inputs in the Custom Page UI. However, should we:
- Add parameter definitions via the Custom Page settings UI?
- Define them in the Custom Page manifest somewhere?
- Use a different approach altogether?

### Question 3: Alternative Approach?

If `Param()` can't access the `parameters:` property, should we:
- Use the old `recordId` with JSON.stringify() workaround?
- Use a different `navigateTo()` pattern?
- Pass parameters a completely different way?

---

## Verification Tests We Can Run

If you suggest a different approach, we can quickly test:

1. **Add a Label** to Custom Page with any formula you recommend
2. **Check console** for any error messages we might have missed
3. **Try alternative syntax** for accessing parameters
4. **Declare inputs** in Custom Page if needed

---

## Code Files Available for Review

All code is in: `C:\code_files\spaarke\`

**Key Files:**
1. **Web Resource:** `sprk_subgrid_commands.js` (sends `navigateTo()`)
2. **PCF Manifest:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`
3. **PCF Control:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
4. **Custom Page:** Configured via Power Apps Studio (not file-based)

---

## What We Need

**Specific guidance on how to retrieve parameters in the Custom Page** that were sent via `navigateTo()` with the `parameters:` property.

The exact Power FX syntax/formula that will make this work:
```powerfx
// Current (doesn't work):
Set(varParentEntityName, Param("parentEntityName"));

// What should we use instead?
Set(varParentEntityName, ???);
```

---

## Additional Context

- PCF control works perfectly when parameters are provided (tested previously)
- Web resource successfully retrieves all values from form context
- `navigateTo()` succeeds with no errors in console
- Custom Page opens and displays correctly
- **Only issue:** Parameters aren't accessible via `Param()` in Custom Page formulas

---

**Thank you for your continued guidance!**
