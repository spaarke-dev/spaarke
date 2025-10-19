# Debug: PCF Control Not Rendering

**Issue:** Field label shows but no control/input area appears

---

## Diagnostic Steps

### Step 1: Check Browser Console for Errors

1. Open the Quick Create form in form designer
2. Press **F12** to open Developer Tools
3. Go to **Console** tab
4. Look for errors (red text) - specifically:
   - Failed to load bundle.js
   - PCF initialization errors
   - CORS errors
   - Any errors mentioning "UniversalQuickCreate"

**Take a screenshot of any errors and share them**

### Step 2: Check Network Tab

1. In Developer Tools, go to **Network** tab
2. Refresh the form designer (Ctrl+F5)
3. Look for requests to:
   - `bundle.js` (should be 200 status)
   - Any 404 or 500 errors
   - Failed resource loads

**Filter by "bundle" to find the control's JavaScript**

### Step 3: Verify Control is Actually Selected

In the form designer:

1. Click on the Document Name field
2. Look at the right panel → **Properties** tab
3. Click on **Components** section
4. Under "Control", what do you see?
   - Should show: **Universal Quick Create** selected
   - If it shows "Text Box" → control isn't actually being used

### Step 4: Check Control Type in Solution

1. Go to https://make.powerapps.com
2. **Solutions** → **UniversalQuickCreateSolution**
3. Expand **Custom controls**
4. Click on `sprk_Spaarke.Controls.UniversalQuickCreate`
5. What does it show?
   - Version?
   - Display name?
   - Resources tab - are bundle.js and CSS listed?

---

## Possible Issues

### Issue 1: Control Type Mismatch

**Symptom:** PCF control is a **field control** but the manifest shows `control-type="standard"`

**Quick Check:**
- Does UniversalDatasetGrid work on your forms?
- If yes, what does its manifest show for `control-type`?

**Fix:** The control might need to be `control-type="virtual"` for field-level controls

### Issue 2: Quick Create Form Incompatibility

**Symptom:** PCF controls don't work on Quick Create forms at all in your environment

**Test:**
1. Add the control to a **Main form** instead of Quick Create
2. Does it render there?
3. If Main form works but Quick Create doesn't → environment limitation

### Issue 3: Missing Dependencies

**Symptom:** Control requires dependencies not available in Quick Create context

**Common causes:**
- WebAPI not available in Quick Create
- Form context not fully initialized
- Parent record context missing

### Issue 4: Control Initialization Failing

**Symptom:** Control loads but init() fails silently

**Check in console:**
```javascript
// Look for errors like:
// "Cannot read property 'getFormContext'"
// "WebAPI is undefined"
// "Utility is undefined"
```

---

## Quick Tests

### Test 1: Check if ANY PCF control works on Quick Create

Try adding a different PCF control (if you have one) to the Quick Create form:
- If other PCF controls work → issue specific to UniversalQuickCreate
- If NO PCF controls work → Quick Create doesn't support PCF in your environment

### Test 2: Preview vs Designer

1. **Save** the form with the control configured
2. **Publish** the form
3. Try to **open the Quick Create form** from the actual app (not designer)
4. Does the control appear in the running form?

Sometimes controls don't render in the designer but DO work in the actual form.

### Test 3: Different Browser

- Try Edge (Chromium) if using Chrome
- Try Chrome if using Edge
- Try Incognito/Private mode
- Completely clear cache and cookies

---

## Known Limitation: Quick Create + PCF

**IMPORTANT:** Some PCF control features don't work in Quick Create forms:

**Quick Create limitations:**
- Simplified form context
- Limited API access
- No navigation APIs
- No parent form context (sometimes)
- Dataset controls NOT supported at all

**Our control uses:**
- ✅ WebAPI - should work
- ✅ Utility - should work
- ❓ Form context - might be limited
- ❓ Parent record retrieval - might fail in Quick Create

---

## Workaround: Use Main Form Instead

If Quick Create forms don't support this PCF control, alternative approach:

### Option A: Quick View Form
Instead of Quick Create, use a Quick View form with the PCF control

### Option B: Dialog/Popup
Create a custom command button that opens a dialog with the control

### Option C: Main Form with Popup
Configure the subgrid to open the Main form in a popup instead of Quick Create

---

## Next Steps - Please Provide

1. **Browser console screenshot** when form is open
2. **Network tab screenshot** showing bundle.js request
3. **Confirm:** Is "Universal Quick Create" actually selected in the Components section?
4. **Test:** Does the control work on a **Main form** (not Quick Create)?
5. **Version check:** Go to Solutions → UniversalQuickCreateSolution → Custom controls - what version shows?

---

## Emergency Fix: Simplify the Control

If nothing else works, I can create a minimal test version:
- Remove all features
- Just show "Hello World" text
- Verify basic PCF rendering works
- Then add features back one by one

But first, let's get the diagnostic information above.
