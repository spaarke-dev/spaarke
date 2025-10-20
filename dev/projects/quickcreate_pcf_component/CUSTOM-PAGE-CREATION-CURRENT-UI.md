# Custom Page Creation - Step-by-Step (Current Power Apps UI)

**Page Name:** `sprk_documentuploaddialog`
**Type:** Dialog (Modal)
**Environment:** SPAARKE DEV 1

---

## Prerequisites

### 1. Verify PCF Control is Deployed

First, deploy the PCF control:

```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
pac pcf push --publisher-prefix sprk
```

Expected output:
```
Uploading PowerApps Component Framework control...
Component uploaded successfully
```

---

## Step-by-Step Instructions

### Step 1: Navigate to Power Apps Maker Portal

1. Go to https://make.powerapps.com/
2. Select **SPAARKE DEV 1** environment (top-right dropdown)

---

### Step 2: Create a New Custom Page

1. In the left navigation, click **Solutions**
2. Select your solution (or **Default Solution**)
3. Click **+ New** → **Page**
4. You'll see two options:
   - **Start this screen with: Data**
   - **Start this screen with: With layout**

5. **Choose: "With layout"**
6. Select **"Blank"** template (empty canvas)
7. Click **"Create"**

---

### Step 3: Configure Page Properties

Once the page editor opens:

1. **Name the page:**
   - Look for the page name field at the top (default might be "Screen1")
   - Change it to: **`Document Upload`**

2. **Set the internal name:**
   - In the left Tree view, you'll see the screen
   - Right-click the screen → **Rename**
   - Name: **`Document Upload`**

---

### Step 4: Add Input Parameters

1. Click the **Settings** icon (⚙️) in the top-right
2. Go to the **"Parameters"** section/tab
3. Click **"+ New parameter"** for each of the following:

#### Parameter 1:
- **Name:** `parentEntityName`
- **Data type:** Text
- **Required:** Yes
- Click **Create**

#### Parameter 2:
- **Name:** `parentRecordId`
- **Data type:** Text
- **Required:** Yes
- Click **Create**

#### Parameter 3:
- **Name:** `containerId`
- **Data type:** Text
- **Required:** Yes
- Click **Create**

#### Parameter 4:
- **Name:** `parentDisplayName`
- **Data type:** Text
- **Required:** No
- Click **Create**

You should now have 4 parameters listed.

---

### Step 5: Configure as Dialog

Still in **Settings**:

1. Go to **"Display"** or **"Screen type"** section
2. Set the following:
   - **Type:** Dialog (or Modal)
   - **Width:** 800 (pixels)
   - **Height:** 600 (pixels) or Auto
   - **Position:** Center

3. Click **"Save"** or close settings

---

### Step 6: Add the PCF Control

1. Click **"Insert"** in the left toolbar (or **"+"** button)
2. Look for **"Get more components"** or **"Code components"**
3. In the dialog that opens:
   - Click the **"Code"** tab
   - You should see **"UniversalDocumentUpload"** (or similar)
   - **If you DON'T see it:**
     - Close this dialog
     - Go back to terminal and run: `pac pcf push --publisher-prefix sprk`
     - Wait for it to complete
     - Come back and try again
   - Select the control
   - Click **"Import"** or **"Add"**

4. After importing, the control should appear in your Insert panel
5. **Drag the PCF control** onto the canvas
6. **Resize it to fill the entire screen:**
   - Select the control
   - In properties panel (right side):
     - X: `0`
     - Y: `0`
     - Width: Use formula: `Parent.Width`
     - Height: Use formula: `Parent.Height`

---

### Step 7: Bind PCF Control Properties

With the PCF control selected on the canvas:

1. Look at the **Properties panel** on the right
2. You should see properties like:
   - parentEntityName
   - parentRecordId
   - containerId
   - parentDisplayName
   - sdapApiBaseUrl

3. For **each property**, click the property name to expand it

4. **Bind the parameters using formulas:**

#### Bind parentEntityName:
- Click **fx** (formula icon) next to parentEntityName
- Enter: `parentEntityName`
- Press Enter

#### Bind parentRecordId:
- Click **fx** next to parentRecordId
- Enter: `parentRecordId`
- Press Enter

#### Bind containerId:
- Click **fx** next to containerId
- Enter: `containerId`
- Press Enter

#### Bind parentDisplayName:
- Click **fx** next to parentDisplayName
- Enter: `parentDisplayName`
- Press Enter

#### Set sdapApiBaseUrl:
- Click **fx** next to sdapApiBaseUrl
- Enter: `"https://spe-api-dev-67e2xz.azurewebsites.net"`
- Press Enter
- **NOTE:** No `/api` suffix - the PCF control adds it

---

### Step 8: Save the Custom Page

1. Click **"Save"** button (top-right)
2. In the save dialog:
   - **Name:** `sprk_documentuploaddialog`
   - **Description:** Custom page for document upload with SharePoint Embedded
3. Click **"Save"**

---

### Step 9: Publish the Custom Page

1. After saving, click **"Publish"** button (top-right)
2. Wait for publish to complete
3. You should see a success message

---

## Verification Steps

### Verify Custom Page Exists

1. Go back to **Solutions** in Power Apps
2. Open your solution
3. Look for **"Pages"** in the solution components
4. You should see **"sprk_documentuploaddialog"**

### Verify Properties

1. Open the Custom Page again (edit mode)
2. Check that:
   - ✅ 4 parameters are defined
   - ✅ PCF control is on the canvas
   - ✅ All 5 properties are bound
   - ✅ Page type is "Dialog"
   - ✅ Width is 800, Height is 600

---

## Test the Custom Page

Once published, test it using the test script:

1. Open a **Matter** record in Dataverse (SPAARKE DEV 1)
2. Open browser DevTools (F12) → Console tab
3. Copy and paste the contents of: `test-custom-page-navigation.js`
4. Press Enter
5. The Custom Page dialog should open

---

## Troubleshooting

### Issue: PCF Control Not Found

**Solution:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
pac pcf push --publisher-prefix sprk
```

Wait for deployment, then refresh Power Apps Maker Portal.

### Issue: Can't Bind Parameters

**Cause:** Parameters might not be created yet

**Solution:**
1. Make sure you added all 4 parameters in Settings → Parameters
2. Parameter names are **case-sensitive**: use exact names
3. Close and reopen the page editor

### Issue: Properties Panel Empty

**Cause:** PCF control not loaded properly

**Solution:**
1. Delete the control from canvas
2. Re-import it from Insert → Code components
3. Drag it onto canvas again

### Issue: Dialog Settings Not Available

**Cause:** Page might be created as regular canvas app

**Solution:**
1. In Settings → Display
2. Look for "App type" or "Page type"
3. Change from "Canvas app" to "Custom page"

---

## Key Points

- **Parameter names are case-sensitive:** `parentEntityName` NOT `ParentEntityName`
- **API URL has NO /api suffix:** `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Page name must be lowercase:** `sprk_documentuploaddialog`
- **PCF control namespace:** `Spaarke.Controls.UniversalDocumentUpload`
- **Dialog size:** 800px × 600px

---

## What You'll Have When Complete

- ✅ Custom Page named `sprk_documentuploaddialog`
- ✅ Type: Dialog (modal)
- ✅ 4 input parameters defined
- ✅ PCF control embedded and filling entire dialog
- ✅ All properties bound correctly
- ✅ Published and ready for testing

---

## Next Step After Creation

Once the Custom Page is created and tested, proceed to **Task 2: Update PCF Control**

File: [TASK-2-UPDATE-PCF-CONTROL.md](TASK-2-UPDATE-PCF-CONTROL.md)

---

**Created:** 2025-10-20
**Environment:** SPAARKE DEV 1
**Version:** v3.0.0
