# Troubleshooting: Universal Quick Create Control Deployment

**Issue:** Form won't save when control is added, display name shows as key, no web resources visible

---

## Current Symptoms

1. **Control name in form designer:** `Universal_Quick_Create_Display_Key` (showing localization key)
2. **No web resources visible:** bundle.js and CSS files not appearing in Dataverse Web Resources
3. **Form save fails:** No specific error message when trying to save form with control

---

## Diagnosis Steps

### Step 1: Verify Solution Contents

The solution package DOES contain the required files:
- ✅ `bundle.js` (583 KB)
- ✅ `css/UniversalQuickCreate.css` (144 bytes)
- ✅ `strings/UniversalQuickCreate.1033.resx` (4.2 KB)
- ✅ `ControlManifest.xml` (proper structure)

**Command to verify:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/bin/Release
unzip -l UniversalQuickCreateSolution.zip | grep -E "(bundle|css|resx)"
```

### Step 2: Check Control Registration in Dataverse

**Via Power Apps Maker Portal:**
1. Go to https://make.powerapps.com
2. Select **Solutions** → **UniversalQuickCreateSolution**
3. Expand **Custom controls**
4. Click on `sprk_Spaarke.Controls.UniversalQuickCreate`
5. Check if **Resources** tab shows bundle.js and CSS

**Expected:** Resources should be listed
**Actual:** Resources may be missing or not registered

---

## Possible Root Causes

### Cause 1: PCF Control Not Fully Imported

**Symptom:** Control appears in solution but resources not registered as web resources

**Why:** PCF controls in Dataverse Solutions work differently than standalone web resources:
- Resources are embedded in the control definition
- Not separate web resource records
- Registered internally by the PCF framework

**Solution:** This is actually NORMAL for PCF controls. The resources are bundled with the control, not as separate web resources.

### Cause 2: Localization Keys Not Resolved

**Symptom:** Display name shows `Universal_Quick_Create_Display_Key` instead of "Universal Quick Create"

**Why:** The .resx file might not be processed correctly during import

**Possible Issues:**
1. RESX file format incorrect
2. RESX not included in manifest resources (but we verified it is)
3. Language code mismatch (1033 = English US)
4. Solution import didn't process localization

### Cause 3: Form Designer Caching

**Symptom:** Old control definition cached in form designer

**Solution:**
1. Clear browser cache (Ctrl+Shift+Delete)
2. Hard refresh (Ctrl+F5)
3. Close and reopen form designer
4. Try in incognito/private browser window

---

## Resolution Steps

### Option 1: Manual Publish (via Power Apps Portal)

Since `pac org publish` doesn't exist, publish via UI:

1. Go to https://make.powerapps.com
2. Select **Solutions**
3. Click **UniversalQuickCreateSolution**
4. Click **...** (More) → **Publish all customizations**
5. Wait for publish to complete
6. Clear browser cache
7. Reopen Quick Create form designer

### Option 2: Reimport as Managed (Test)

Try importing as Managed to see if localization works:

```bash
# Edit UniversalQuickCreateSolution.cdsproj
# Change <SolutionPackageType>Unmanaged</SolutionPackageType>
# to <SolutionPackageType>Managed</SolutionPackageType>

# Rebuild
dotnet build --configuration Release

# Delete existing
pac solution delete --solution-name UniversalQuickCreateSolution

# Import as Managed
pac solution import --path UniversalQuickCreateSolution.zip
```

**NOTE:** This is just for testing. We need Unmanaged for development.

### Option 3: Check Form Error Details

The error "no specific errors" might have details in browser console:

1. Open Quick Create form designer
2. Open browser Developer Tools (F12)
3. Go to **Console** tab
4. Try to save the form
5. Look for error messages in console
6. Take screenshot of any errors

### Option 4: Verify Field Binding

The form might fail because of field binding issues:

**Checklist:**
- [ ] `sprk_filename` field exists on sprk_document entity
- [ ] Field is type: Single Line of Text
- [ ] Field is added to the Quick Create form
- [ ] Control is bound to that specific field (not to the form)

**How to verify:**
1. In form designer, click on the `sprk_filename` field
2. In right panel, click **+ Component**
3. Select **Universal Quick Create** (or Universal_Quick_Create_Display_Key)
4. Click **Add**
5. Configure properties
6. Try to save

---

## Verification Commands

### Check if solution is imported:
```bash
pac solution list | grep -i universal
```

**Expected output:**
```
UniversalQuickCreateSolution UniversalQuickCreateSolution 1.0 False
```

### Check solution details (via Power Apps):
1. Solutions → UniversalQuickCreateSolution → **Properties**
2. Verify:
   - Version: 1.0 or higher
   - Publisher: Your publisher
   - Managed: No

---

## Known Limitations of PCF Controls

### Web Resources vs PCF Resources

**Important:** PCF control resources (bundle.js, CSS) are NOT separate web resources in Dataverse.

**Why you don't see them in Web Resources list:**
- PCF bundles resources into the control package
- Resources are deployed as part of the custom control
- They're referenced internally, not as standalone web resources
- This is by design in PCF framework

**Comparison:**
| Traditional Web Resources | PCF Control Resources |
|--------------------------|----------------------|
| Appear in Web Resources list | Embedded in control |
| Named like `prefix_/folder/file.js` | Named like `bundle.js` (internal) |
| Directly editable | Must rebuild control |
| Separate solution components | Part of custom control component |

**Bottom Line:** Not seeing bundle.js in Web Resources is NORMAL and EXPECTED for PCF controls.

---

## If All Else Fails

### Nuclear Option: Rebuild from Scratch

1. **Delete solution:**
   ```bash
   pac solution delete --solution-name UniversalQuickCreateSolution
   ```

2. **Clean build:**
   ```bash
   cd /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate
   npm run clean
   npm install
   npm run build -- --buildMode production
   ```

3. **Rebuild solution:**
   ```bash
   cd /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
   dotnet clean
   dotnet build --configuration Release
   ```

4. **Reimport:**
   ```bash
   pac solution import --path bin/Release/UniversalQuickCreateSolution.zip --async
   ```

5. **Clear browser cache completely**

6. **Try form configuration again**

---

## Action Items for User

**Please try the following and report results:**

1. **Clear Browser Cache:**
   - Press Ctrl+Shift+Delete
   - Select "All time"
   - Clear cached images and files
   - Close and reopen browser

2. **Check Browser Console:**
   - Open form designer (F12 for Dev Tools)
   - Go to Console tab
   - Try to save form
   - Screenshot any errors

3. **Verify Field:**
   - Confirm `sprk_filename` field exists
   - Confirm it's Single Line of Text type
   - Confirm it's on the Quick Create form

4. **Try Publishing via UI:**
   - Go to make.powerapps.com
   - Solutions → UniversalQuickCreateSolution
   - Click **... → Publish all customizations**

5. **Report Back:**
   - Does display name still show as key?
   - What is the exact error when saving form?
   - Any console errors?

---

## Expected Behavior After Fix

✅ Control appears as "Universal Quick Create" (not the key)
✅ Form saves successfully with control configured
✅ Control loads on Quick Create form
✅ File upload functionality works

**Note:** Web resources NOT appearing separately is normal for PCF controls.
