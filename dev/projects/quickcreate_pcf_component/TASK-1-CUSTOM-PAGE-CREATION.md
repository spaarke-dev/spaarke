# Task 1 Step 1.3: Create Custom Page in Power Apps Maker Portal

**Status:** 🔵 Ready to Execute
**Estimated Time:** 30-45 minutes
**Prerequisites:** Power Apps Maker Portal access, PCF control deployed

---

## Overview

You will create a Custom Page dialog in Power Apps Maker Portal that embeds the UniversalDocumentUpload PCF control.

**Important:** Follow these steps exactly as Custom Pages have specific requirements for parameter binding.

---

## Step-by-Step Instructions

### Step 1: Access Power Apps Maker Portal

1. **Navigate to Power Apps:**
   - URL: https://make.powerapps.com/
   - Sign in with your account

2. **Select Environment:**
   - Click environment selector (top right)
   - Select **"SPAARKE DEV 1"**
   - Verify environment name shows in top bar

3. **Screenshot:** Take screenshot showing SPAARKE DEV 1 selected
   - Save as: `task1-step1-environment.png`

---

### Step 2: Verify PCF Control is Deployed

**Before creating Custom Page, verify PCF control exists:**

1. **Navigate to Solutions:**
   - Click "Solutions" in left navigation

2. **Find UniversalQuickCreate Control:**
   - Look for solution containing the control (might be "Default Solution" or custom solution)
   - Click solution name to open it

3. **Verify Control:**
   - In solution, look for "Controls" section
   - Find "UniversalDocumentUpload" or "Spaarke.Controls.UniversalDocumentUpload"
   - Note the version: Should be 2.2.0 or 2.3.0

4. **If Control NOT Found:**
   - ⚠️ STOP - PCF control must be deployed first
   - Run: `cd src/controls/UniversalQuickCreate && pac pcf push --publisher-prefix sprk`
   - Wait for deployment to complete
   - Refresh solutions and try again

5. **Screenshot:** Take screenshot showing PCF control in solution
   - Save as: `task1-step2-pcf-control.png`

---

### Step 3: Create New Custom Page

1. **Start Page Creation:**
   - In Solutions view, click your solution (or Default Solution)
   - Click **"+ New"** → **"Page"**
   - Select **"Blank page with columns"**

2. **Page Settings:**
   - **Name:** Document Upload Dialog
   - **Layout:** Single column
   - Click **"Create"**

3. **Wait for Editor:**
   - Custom Page editor will open
   - May take 10-30 seconds to load
   - You'll see a blank canvas with "Insert" panel on left

4. **Screenshot:** Take screenshot of blank Custom Page editor
   - Save as: `task1-step3-blank-page.png`

---

### Step 4: Configure Page as Dialog

1. **Open Page Settings:**
   - Click **"..."** (ellipsis) at top right
   - Select **"Settings"**

2. **Display Settings:**
   - Go to **"Display"** tab
   - Set **Type:** Dialog
   - Set **Width:** 800 px
   - Set **Height:** 600 px  (or leave as Auto)
   - Set **Position:** Center

3. **General Settings:**
   - Go to **"General"** tab
   - Verify **Name:** `sprk_documentuploaddialog` (system will auto-generate if needed)
   - **Display Name:** Document Upload Dialog
   - **Description:** Custom page for uploading documents to SharePoint Embedded - v3.0.0

4. **Click "Save" on Settings dialog**

5. **Screenshot:** Take screenshot of Settings dialog (Display tab)
   - Save as: `task1-step4-dialog-settings.png`

---

### Step 5: Add Page Parameters

**CRITICAL:** Parameters must match exactly (case-sensitive)

1. **Open Settings Again:**
   - Click "..." → "Settings"

2. **Navigate to Parameters Tab:**
   - Click **"Parameters"** tab

3. **Add Parameter 1: parentEntityName**
   - Click **"+ New parameter"**
   - **Name:** `parentEntityName` (exact case)
   - **Data type:** Text
   - **Required:** Yes
   - **Default value:** (leave empty)
   - **Description:** Parent entity logical name (e.g., sprk_matter)
   - Click **"Add"** or **"Save"**

4. **Add Parameter 2: parentRecordId**
   - Click **"+ New parameter"**
   - **Name:** `parentRecordId` (exact case)
   - **Data type:** Text
   - **Required:** Yes
   - **Default value:** (leave empty)
   - **Description:** Parent record GUID (without curly braces)
   - Click **"Add"** or **"Save"**

5. **Add Parameter 3: containerId**
   - Click **"+ New parameter"**
   - **Name:** `containerId` (exact case)
   - **Data type:** Text
   - **Required:** Yes
   - **Default value:** (leave empty)
   - **Description:** SharePoint Embedded container ID
   - Click **"Add"** or **"Save"**

6. **Add Parameter 4: parentDisplayName**
   - Click **"+ New parameter"**
   - **Name:** `parentDisplayName` (exact case)
   - **Data type:** Text
   - **Required:** No
   - **Default value:** (leave empty)
   - **Description:** Display name for UI header
   - Click **"Add"** or **"Save"**

7. **Verify All Parameters Added:**
   - You should see 4 parameters listed
   - Verify names match exactly (case-sensitive!)

8. **Click "Save" on Settings dialog**

9. **Screenshot:** Take screenshot of Parameters tab showing all 4 parameters
   - Save as: `task1-step5-parameters.png`

---

### Step 6: Add PCF Control to Canvas

1. **Open Component Gallery:**
   - Click **"+ Insert"** in left panel (or "Get more components")
   - Select **"Code"** tab or **"Custom"** tab

2. **Find PCF Control:**
   - Search for "UniversalDocumentUpload" or "Spaarke"
   - You should see the PCF control listed
   - **Control Name:** Spaarke.Controls.UniversalDocumentUpload

3. **Add Control (if not already added):**
   - If control shows "Add" button, click it
   - If control shows "Added", it's already available
   - Close the component gallery

4. **Drag Control to Canvas:**
   - In left Insert panel, find your PCF control
   - Drag it onto the canvas
   - Position it to fill most of the canvas
   - Resize if needed (should be centered, not too small)

5. **Screenshot:** Take screenshot showing PCF control on canvas
   - Save as: `task1-step6-control-added.png`

---

### Step 7: Bind PCF Control Properties

**CRITICAL:** Property binding connects page parameters to PCF control inputs

1. **Select PCF Control:**
   - Click on the PCF control on canvas
   - Properties panel should appear on right side

2. **Bind parentEntityName:**
   - Find "parentEntityName" property in properties panel
   - Click the **fx** (formula) button next to it
   - Enter formula: `parentEntityName`
   - (This references the page parameter you created)
   - Press Enter

3. **Bind parentRecordId:**
   - Find "parentRecordId" property
   - Click **fx** button
   - Enter formula: `parentRecordId`
   - Press Enter

4. **Bind containerId:**
   - Find "containerId" property
   - Click **fx** button
   - Enter formula: `containerId`
   - Press Enter

5. **Bind parentDisplayName:**
   - Find "parentDisplayName" property
   - Click **fx** button
   - Enter formula: `parentDisplayName`
   - Press Enter

6. **Set sdapApiBaseUrl (hardcoded):**
   - Find "sdapApiBaseUrl" property
   - Enter directly (no fx): `https://spe-api-dev-67e2xz.azurewebsites.net`
   - ⚠️ Do NOT include `/api` suffix - PCF adds it internally
   - Press Enter

7. **Verify All Bindings:**
   - Review all 5 properties in properties panel
   - Each should show either a formula (fx icon) or hardcoded value
   - No properties should be empty or default

8. **Screenshot:** Take screenshot of properties panel showing bindings
   - Save as: `task1-step7-bindings.png`

---

### Step 8: Save and Publish Custom Page

1. **Save Page:**
   - Click **"Save"** button (top left or top right)
   - Wait for save confirmation
   - If prompted for name, use: `sprk_documentuploaddialog`

2. **Publish Page:**
   - Click **"Publish"** button (next to Save)
   - Wait for publish to complete
   - Should see "Published successfully" message

3. **Verify Page Name:**
   - Check page name in title bar or solution
   - Should be: `sprk_documentuploaddialog` (lowercase, no spaces)
   - If different, note the actual name (you'll need it for Task 3)

4. **Screenshot:** Take screenshot of published page with success message
   - Save as: `task1-step8-published.png`

---

## Troubleshooting

### Issue: PCF Control Not Found

**Solution:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
pac pcf push --publisher-prefix sprk
```

Wait for deployment, then refresh Power Apps Maker Portal.

---

### Issue: Properties Don't Show in Properties Panel

**Possible Causes:**
1. PCF control version issue - redeploy control
2. Properties not defined in manifest - verify ControlManifest.Input.xml
3. Control not selected - click control on canvas

**Solution:**
- Select control on canvas
- If properties still not showing, remove control and re-add it
- Verify control version matches (2.2.0 or 2.3.0)

---

### Issue: Can't Bind Parameters (fx button grayed out)

**Possible Causes:**
1. Parameters not created yet - go back to Step 5
2. Property type mismatch - verify parameter is Text type
3. Control property marked as "bound" not "input" - may need manifest update in Task 2

**Workaround:**
- For now, enter parameter names as formulas directly
- Example: `=parentEntityName` (with equals sign)
- Will verify in testing (Step 9)

---

### Issue: Page Won't Save/Publish

**Common Errors:**
1. **"Name already exists"** - Use different name or delete old page
2. **"Control not found"** - PCF control not deployed properly
3. **"Invalid formula"** - Check property bindings for typos

**Solution:**
- Read error message carefully
- Fix the specific issue mentioned
- Try save/publish again

---

## Completion Checklist

- [ ] Custom Page created as Dialog type
- [ ] Page name: `sprk_documentuploaddialog`
- [ ] Dialog settings: 800px width, 600px height, centered
- [ ] 4 parameters added (parentEntityName, parentRecordId, containerId, parentDisplayName)
- [ ] PCF control added to canvas
- [ ] All 5 properties bound/configured
- [ ] Page saved successfully
- [ ] Page published successfully
- [ ] 6 screenshots captured
- [ ] Page name documented for Task 3

---

## Next Step

**After completing this step:**

Proceed to Step 1.4 - Test Parameter Passing

You will test the Custom Page navigation using browser console before updating ribbon buttons.

---

## Screenshots Checklist

Save all screenshots to: `dev/projects/quickcreate_pcf_component/testing/task1-screenshots/`

- [ ] `task1-step1-environment.png` - SPAARKE DEV 1 selected
- [ ] `task1-step2-pcf-control.png` - PCF control in solution
- [ ] `task1-step3-blank-page.png` - Blank Custom Page editor
- [ ] `task1-step4-dialog-settings.png` - Dialog configuration
- [ ] `task1-step5-parameters.png` - All 4 parameters added
- [ ] `task1-step6-control-added.png` - PCF control on canvas
- [ ] `task1-step7-bindings.png` - Property bindings configured
- [ ] `task1-step8-published.png` - Published success message

---

**Created:** 2025-10-20
**Task:** Task 1, Step 1.3
**Status:** Ready for Execution
