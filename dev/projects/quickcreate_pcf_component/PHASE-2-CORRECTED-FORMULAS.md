# Phase 2 CORRECTED Formulas - Custom Page Parameter Flow

**Issue Discovered:** When parameters are passed as `{value: "..."}` objects, `Param()` returns the entire object, not just the value.

**Solution:** Extract the `.value` property when reading `Param()`.

---

## Corrected Formulas

### App.OnStart - CORRECT (No Changes)
```powerfx
Set(varAppInitialized, true)
```

**Just this one line - nothing else!**

---

### Screen1.OnVisible - CORRECTED

**WRONG (Original):**
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

**PROBLEM:** This reads the entire `{value: "..."}` object into the variable.

**CORRECTED:**
```powerfx
If(
    !varInit,
    Set(varInit, true);
    Set(varParentEntityName, Param("parentEntityName").value);
    Set(varParentRecordId, Param("parentRecordId").value);
    Set(varContainerId, Param("containerId").value);
    Set(varParentDisplayName, Param("parentDisplayName").value)
)
```

**KEY CHANGE:** Added `.value` to extract the string value from the object.

---

## ALTERNATIVE (Simpler) - Use JSON() Function

If the `.value` syntax doesn't work, Power Apps might require using the `JSON()` or `Text()` function:

### Screen1.OnVisible - ALTERNATIVE
```powerfx
If(
    !varInit,
    Set(varInit, true);
    Set(varParentEntityName, Text(Param("parentEntityName")));
    Set(varParentRecordId, Text(Param("parentRecordId")));
    Set(varContainerId, Text(Param("containerId")));
    Set(varParentDisplayName, Text(Param("parentDisplayName")))
)
```

---

## PCF Control Properties - NO CHANGES NEEDED

The PCF control properties should remain bound to the variables:

- **parentEntityName:** `varParentEntityName`
- **parentRecordId:** `varParentRecordId`
- **containerId:** `varContainerId`
- **parentDisplayName:** `varParentDisplayName`
- **sdapApiBaseUrl:** `"https://spe-api-dev-67e2xz.azurewebsites.net"`

---

## PCF Control Visible Property - NO CHANGES NEEDED

```powerfx
!IsBlank(varParentRecordId) && !IsBlank(varContainerId)
```

---

## Quick Test - Add Debug Label

To verify what's in the variables, add a temporary Label control to the Custom Page:

**Label1.Text:**
```powerfx
"Entity: " & varParentEntityName & Char(10) &
"RecordID: " & varParentRecordId & Char(10) &
"Container: " & varContainerId & Char(10) &
"Display: " & varParentDisplayName
```

**Expected Output (if working):**
```
Entity: sprk_matter
RecordID: 3A785F76-C773-F011-B4CB-6045BDD8B757
Container: b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
Display: 345345345345
```

**If you see `{value: sprk_matter}` instead:**
- The `.value` extraction didn't work
- Try the `Text()` function alternative above

**If you see blank:**
- `varInit` flag might already be `true` from previous open
- Try changing `!varInit` to `true` temporarily (forces it to run every time)

---

## Alternative Quick Fix - Direct Param() Binding

If you want to test ASAP without modifying formulas, temporarily change the PCF properties to:

**PCF Control Properties (TEMPORARY TEST):**
- **parentEntityName:** `Param("parentEntityName").value`
- **parentRecordId:** `Param("parentRecordId").value`
- **containerId:** `Param("containerId").value`
- **parentDisplayName:** `Param("parentDisplayName").value`

OR if `.value` doesn't work:

- **parentEntityName:** `Text(Param("parentEntityName"))`
- **parentRecordId:** `Text(Param("parentRecordId"))`
- **containerId:** `Text(Param("containerId"))`
- **parentDisplayName:** `Text(Param("parentDisplayName"))`

---

## Summary

**The Problem:**
Parameters are passed as `{value: "..."}` objects from the web resource. When `Param()` reads them, it gets the whole object, not just the string value.

**The Fix:**
Extract the `.value` property when reading parameters:
```powerfx
Set(varParentRecordId, Param("parentRecordId").value);
```

**OR use Text() function:**
```powerfx
Set(varParentRecordId, Text(Param("parentRecordId")));
```

---

**Created:** 2025-10-21
**Status:** Corrected formulas for parameter extraction
