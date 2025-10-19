# Simplest Deployment Method - 5 Minutes

**The control is built and ready - we just need to get the new bundle.js file into Dataverse.**

---

## Quick Deploy (5 steps, 5 minutes)

### Step 1: Export Current Solution from Dataverse (1 min)

1. Open: https://spaarkedev1.crm.dynamics.com
2. Go to: Settings → Solutions
3. Find: "UniversalDatasetGridSolution"
4. Click: Export
5. Choose: **Unmanaged**
6. Click: Export
7. Download and save: `UniversalDatasetGridSolution.zip`

---

### Step 2: Extract the ZIP (30 seconds)

1. Create folder: `C:\temp\solution_update`
2. Extract `UniversalDatasetGridSolution.zip` to this folder
3. You should see folders like: `Controls`, `Other`, etc.

---

### Step 3: Replace bundle.js with New MSAL Version (30 seconds)

1. Navigate to: `C:\temp\solution_update\Controls\spk_Spaarke.UI.Components.UniversalDatasetGrid`
2. You'll see: `bundle.js` (OLD version without MSAL)
3. Replace it with NEW version from:
   `C:\code_files\spaarke\src\controls\UniversalDatasetGrid\out\controls\spk_Spaarke.UI.Components.UniversalDatasetGrid\bundle.js`

**PowerShell command to copy:**
```powershell
Copy-Item "C:\code_files\spaarke\src\controls\UniversalDatasetGrid\out\controls\spk_Spaarke.UI.Components.UniversalDatasetGrid\bundle.js" `
          "C:\temp\solution_update\Controls\spk_Spaarke.UI.Components.UniversalDatasetGrid\bundle.js" -Force
```

---

### Step 4: Re-ZIP the Solution (30 seconds)

1. Select ALL files/folders in `C:\temp\solution_update\`
2. Right-click → Send to → Compressed (zipped) folder
3. Name it: `UniversalDatasetGridSolution_MSAL.zip`
4. Move to desktop or known location

**Important:** ZIP the CONTENTS, not the folder itself. The ZIP root should contain `Controls`, `Other`, etc. directly.

---

### Step 5: Import Updated Solution (2 min)

1. Open: https://spaarkedev1.crm.dynamics.com
2. Go to: Settings → Solutions
3. Click: Import
4. Browse to: `UniversalDatasetGridSolution_MSAL.zip`
5. Click: Next
6. Choose: **Upgrade** (if prompted)
7. Wait for import to complete
8. Click: Publish All Customizations

---

### Step 6: Test (1 min)

1. **Hard refresh browser:** Ctrl+Shift+R
2. Open form with control
3. Press F12 → Console
4. Click Download button
5. Look for: `[MsalAuthProvider] Getting token for scopes...`

**If you see MSAL logs → SUCCESS!** ✅

---

## Troubleshooting

**If bundle.js not found in step 3:**

```bash
# Find the actual bundle location
find "C:\code_files\spaarke\src\controls" -name "bundle.js" -type f
```

**If import fails:**
- Make sure you selected "Upgrade" not "New solution"
- Make sure ZIP structure is correct (Controls folder at root level)

---

This is the FASTEST and SAFEST way to deploy. Would you like me to help with any of these steps?

