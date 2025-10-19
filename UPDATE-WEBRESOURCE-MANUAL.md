# Manual Web Resource Update - v2.1.0

## Quick Update Steps (5 minutes)

The Web Resource needs to be updated from v2.0.2 (Custom Page) to v2.1.0 (Form Dialog).

---

## Step 1: Get the File

**File Location**: `c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_subgrid_commands.js`

**File Size**: ~17 KB

**Version Check**: Open the file and verify line 9 shows:
```javascript
 * @version 2.1.0
```

---

## Step 2: Upload via Power Apps UI

### Option A: Update Existing Web Resource (Recommended)

1. Open **Power Apps** (https://make.powerapps.com)
2. Select environment: **SPAARKE DEV 1**
3. Click **Solutions** (left navigation)
4. Open your solution (or "Default Solution")
5. In the search box, type: **subgrid_commands**
6. Click on **sprk_subgrid_commands** (Web resource)
7. Click **Edit** or **Upload file**
8. Click **Choose file** and select:
   ```
   c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_subgrid_commands.js
   ```
9. Click **Save**
10. Click **Publish**

### Option B: Create New Web Resource (If not found)

1. Open **Power Apps** (https://make.powerapps.com)
2. Click **Solutions** → Open your solution
3. Click **New** → **More** → **Web resource**
4. Configure:
   - **Display name**: `Subgrid Commands v2.1.0`
   - **Name**: `sprk_subgrid_commands` (without prefix - Dataverse adds it)
   - **Type**: **Script (JScript)**
   - **Upload file**: Select the file above
5. Click **Save**
6. Click **Publish**

---

## Step 3: Verify Upload

Open browser console on any Dataverse page and run:

```javascript
fetch(Xrm.Utility.getGlobalContext().getClientUrl() + "/WebResources/sprk_subgrid_commands.js")
  .then(r => r.text())
  .then(content => {
    const versionMatch = content.match(/@version\s+([\d.]+)/);
    const openFormMatch = content.includes("Xrm.Navigation.openForm");
    console.log("Version:", versionMatch ? versionMatch[1] : "NOT FOUND");
    console.log("Uses openForm:", openFormMatch);
    if (versionMatch && versionMatch[1] === "2.1.0" && openFormMatch) {
      console.log("✅ Web Resource v2.1.0 deployed correctly!");
    } else {
      console.log("❌ Old version still cached or upload failed");
      console.log("Try hard refresh (Ctrl+Shift+R) and check again");
    }
  });
```

**Expected Output**:
```
Version: 2.1.0
Uses openForm: true
✅ Web Resource v2.1.0 deployed correctly!
```

---

## Step 4: Test Form Dialog

1. **Hard refresh** browser (Ctrl+Shift+R) to clear cache
2. Open a **Matter** record (with sprk_containerid)
3. Scroll to **Documents** subgrid
4. Click **"Quick Create: Document"** button

### Expected Console Output:
```
[Spaarke] AddMultipleDocuments: Starting v2.1.0 - FORM DIALOG APPROACH
[Spaarke] Parent Entity: sprk_matter
[Spaarke] Parent Record ID: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
[Spaarke] Container ID: b!yLRdWEOAdkaWXskuRfByI...
[Spaarke] Opening Form Dialog with parameters: {...}
[Spaarke] Form Options: {entityName: "sprk_uploadcontext", ...}
[Spaarke] Form Parameters: {sprk_parententityname: "sprk_matter", ...}
```

### Expected Behavior:
- ✅ Form Dialog opens (NOT "Dialog closed immediately")
- ✅ PCF control is visible in the dialog
- ✅ File selection works
- ✅ Upload button enabled

---

## Troubleshooting

### Issue: Still shows v2.0.2 after upload

**Cause**: Browser cache

**Fix**:
1. Hard refresh: Ctrl+Shift+R
2. Clear Dataverse cache:
   - Settings (gear icon) → **Advanced Settings**
   - Close and reopen
3. Try incognito/private window

### Issue: Web Resource not found in solution

**Cause**: Web Resource in different solution

**Fix**:
1. Go to **Solutions** → **Default Solution**
2. Search for **sprk_subgrid_commands**
3. Update there
4. Or add to your solution: Click **Add existing** → **More** → **Web resource**

### Issue: Upload button disabled

**Cause**: File too large or wrong type

**Fix**:
1. Verify file is `.js` (JavaScript)
2. Verify file size is ~17 KB
3. If using Option B (new web resource), ensure Type is **Script (JScript)**

---

## Alternative: PowerShell Upload (Advanced)

If you prefer scripting, use this PowerShell command:

```powershell
# Read file content
$filePath = "c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_subgrid_commands.js"
$content = Get-Content $filePath -Raw
$base64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($content))

# Get Dataverse URL
$orgUrl = (pac org list --json | ConvertFrom-Json | Where-Object { $_.IsDefault -eq $true }).Url

Write-Host "Manual update required:"
Write-Host "1. Open: $orgUrl/main.aspx"
Write-Host "2. Navigate to Solutions → Web Resources"
Write-Host "3. Find: sprk_subgrid_commands"
Write-Host "4. Upload file: $filePath"
Write-Host "5. Publish"
```

---

## Summary

**File to Upload**: `c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_subgrid_commands.js`

**Where**: Power Apps → Solutions → Web Resources → sprk_subgrid_commands → Edit → Upload

**After Upload**: Hard refresh browser (Ctrl+Shift+R), test button

**Success**: Dialog opens with PCF control (NOT "Dialog closed immediately")

---

**Estimated Time**: 5 minutes
