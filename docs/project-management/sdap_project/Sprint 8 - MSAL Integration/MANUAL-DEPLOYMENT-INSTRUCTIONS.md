# Manual Deployment Instructions - MSAL Integration

**Date:** 2025-10-06
**Control:** Universal Dataset Grid with MSAL Integration
**Status:** ✅ Built Successfully, Ready for Manual Import

---

## Current Situation

**Build Status:** ✅ **SUCCESS**
- Control rebuilt with MSAL integration (production mode)
- Bundle size: 837 KiB (minified with MSAL included)
- Located in: `src/controls/UniversalDatasetGrid/out/controls/`

**Deployment Issue:**
- `pac pcf push` failed due to publisher prefix conflict
- Solution structure incomplete for automated packaging
- **Recommendation:** Manual import via Dataverse UI (fastest and most reliable)

---

## Option 1: Manual Import via Dataverse UI (RECOMMENDED - 5 minutes)

### Step 1: Export Existing Solution

1. **Open Dataverse:**
   - Navigate to: https://spaarkedev1.crm.dynamics.com
   - Go to: Settings → Solutions

2. **Find UniversalDatasetGridSolution:**
   - Look for solution: "UniversalDatasetGridSolution"
   - Version: 1.0
   - Type: Unmanaged

3. **Export Solution:**
   - Select the solution
   - Click "Export"
   - Choose "Unmanaged"
   - Click "Export"
   - Wait for download
   - Save as: `UniversalDatasetGridSolution_backup.zip`

**Purpose:** Backup in case we need to rollback

---

### Step 2: Delete Old Custom Control (Optional - Only if Import Fails)

**Only do this if Step 3 import fails with publisher error:**

1. **Open Solution:**
   - Open "UniversalDatasetGridSolution"

2. **Find Custom Control:**
   - Navigate to: Custom Controls
   - Find: "Spaarke.UI.Components.UniversalDatasetGrid"

3. **Remove from Solution:**
   - Select the control
   - Click "Remove from this solution"
   - Confirm removal

4. **Delete Custom Control:**
   - Go to: Settings → Customizations → Custom Controls
   - Find: "Spaarke.UI.Components.UniversalDatasetGrid"
   - Delete the control
   - Confirm deletion

---

### Step 3: Import Updated Solution

**Since automated packaging isn't working, we'll add the control directly to the existing solution:**

1. **Open Power Apps CLI (already authenticated)**

2. **Add Control to Existing Solution:**
   ```bash
   # Navigate to control directory
   cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid"

   # Add control to existing solution
   pac solution add-reference --path "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGridSolution"
   ```

3. **Build and Deploy:**
   ```bash
   # Build the solution
   cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGridSolution"
   msbuild /t:build /restore
   ```

---

## Option 2: Use PowerShell to Update Control Directly (ALTERNATIVE)

**If Option 1 doesn't work, we can update the control files directly in Dataverse:**

This requires using the Dataverse Web API to update the custom control resource files.

**Steps:**
1. Get the control ID from Dataverse
2. Upload new bundle.js as a resource
3. Update control version

**Note:** This is more complex and error-prone. Only use if Option 1 fails.

---

## Option 3: Create New Solution from Scratch (LAST RESORT)

**If both above options fail:**

1. **Create New Solution in Dataverse UI:**
   - Name: "UniversalDatasetGrid_MSAL"
   - Publisher: Use existing "Spaarke" publisher
   - Version: 1.0.0.1

2. **Import PCF Control:**
   - We'll need to create a proper solution package manually
   - This requires recreating the solution structure

---

## SIMPLIFIED APPROACH: Quick Test Without Full Deployment

**Since we just need to TEST the MSAL integration, here's the fastest path:**

### Quick Test Deploy (10 minutes)

1. **Create New Test Solution:**
   ```bash
   cd "c:\code_files\spaarke\src\controls"
   pac solution init --publisher-name Spaarke --publisher-prefix spk_test
   ```

2. **Add PCF Control Reference:**
   ```bash
   pac solution add-reference --path ./UniversalDatasetGrid/UniversalDatasetGrid
   ```

3. **Build Solution:**
   ```bash
   msbuild /t:build /restore
   ```

4. **Import via Dataverse UI:**
   - Solution .zip will be in bin/Debug
   - Import to Dataverse
   - Publish customizations

---

## FASTEST PATH: Let Me Guide You Through Manual Steps

**Since automated deployment is hitting issues, let's do this manually step-by-step:**

### What You Need to Do:

1. **Tell me which option you prefer:**
   - Option A: I'll create a complete solution package you can import via UI
   - Option B: I'll provide PowerShell script to update control directly
   - Option C: We delete the old control and recreate it with new publisher

2. **Current Status:**
   - ✅ Control built successfully with MSAL
   - ✅ Bundle includes MSAL integration (verified in build output)
   - ⏳ Just needs to get into Dataverse

---

## Temporary Workaround: Test Locally First

**While we figure out deployment, you can test the control locally:**

1. **PCF Test Harness:**
   ```bash
   cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid"
   npm start watch
   ```

2. **Open test harness:**
   - Browser opens automatically
   - Control loads with latest code
   - You can test MSAL integration locally

**Limitation:** Can't test actual Dataverse integration, but can verify MSAL initialization works.

---

## My Recommendation

**Let's use the simplest approach:**

**I'll create a deployment package you can import manually in 2 steps:**

1. I'll package the solution properly
2. You import the .zip via Dataverse UI → Solutions → Import

**This is the most reliable method and takes 2 minutes once package is ready.**

**Would you like me to:**
- A) Create the deployment package now?
- B) Walk you through deleting the old control first?
- C) Try a different automated approach?

**Let me know which you prefer and I'll proceed immediately.**

---

**Current Status:** ⏸️ **WAITING FOR DECISION**
**Build Status:** ✅ **READY**
**Blocker:** Deployment method choice

---
