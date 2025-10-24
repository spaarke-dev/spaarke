# Final Custom Page Creation Steps (Current Power Apps UI - October 2025)

**Reality:** Modern Power Apps Custom Pages use the `Param()` function instead of explicitly defining parameters in the UI.

---

## Simplified Steps (No Parameter UI Needed)

### Step 1-4: Create and Name Page

1. **Go to:** https://make.powerapps.com/ (SPAARKE DEV 1)
2. **Create Page:** Solutions → + New → Page
3. **Select:** "With layout" → Choose any template (e.g., "Scrollable")
4. **Clear canvas:** Delete all template components
5. **Name the page:** "Document Upload"
6. **Set screen name:** Right-click screen in Tree view → Rename to "Document Upload"

---

### Step 5: SKIP Parameter Definition

**Skip adding parameters!** The modern interface uses formulas. We'll use `Param()` function instead when binding properties.

---

### Step 6: Configure as Dialog (if available)

1. Click **Settings** (⚙️)
2. Look for **Display** settings
3. If you see Dialog/Width/Height options, set:
   - Width: 800
   - Height: 600
4. If not available, that's OK - the dialog behavior comes from navigation code

---

### Step 7: Add PCF Control

**Finding and Adding the PCF Control - Try these options:**

#### Option A: Via Insert Menu
1. Click **Insert** (+ button) in the top menu bar
2. Look for one of these:
   - **"Custom"** or **"Code component"**
   - **"More"** or **"..."** at the bottom of Insert menu
   - **Library** or **Components**
3. If you see **"Get more components"** - click it
4. Look for **Code** tab or **Custom controls**

#### Option B: Via Add Data
1. Click **"Add data"** in the top toolbar
2. Look for **"Connectors"** or **"Custom"**
3. Find **Code components** or **Custom controls**

#### Option C: Direct Search
1. In the Insert panel (left side after clicking +)
2. Look for a **Search box** at the top
3. Type: **"UniversalDocumentUpload"**
4. The PCF control should appear if deployed

#### Option D: Via Media/Advanced
1. Click **Insert** (+)
2. Scroll down to **"Media"** or **"Advanced"**
3. Look for **"Custom"** or **"Import component"**

---

**Once you find where Code Components are:**

1. You should see: **"UniversalDocumentUpload"** or **"Universal Document Upload"**
   - **If NOT visible:** The control isn't deployed yet
   - Close Power Apps, run: `cd /c/code_files/spaarke/src/controls/UniversalQuickCreate && pac pcf push --publisher-prefix sprk`
   - Reopen Power Apps

2. **Select/Import the control** (might be a checkbox or Add button)

3. After import, the control should appear in your Insert panel

4. **Drag it onto the canvas**

5. **Resize to fill entire screen:**
   - Select the control
   - In properties panel (right side):
     - **X:** `0`
     - **Y:** `0`
     - **Width:** Click "fx" and enter: `Parent.Width`
     - **Height:** Click "fx" and enter: `Parent.Height`

---

### Step 8: Bind PCF Properties Using Param()

With the PCF control selected, look at the properties panel on the right.

You should see 5 properties. For each one, click the property name and enter the formula:

#### 1. parentEntityName
- Click the property
- Click **fx** (formula icon)
- Enter: `Param("parentEntityName")`
- Press Enter

#### 2. parentRecordId
- Click the property
- Click **fx**
- Enter: `Param("parentRecordId")`
- Press Enter

#### 3. containerId
- Click the property
- Click **fx**
- Enter: `Param("containerId")`
- Press Enter

#### 4. parentDisplayName
- Click the property
- Click **fx**
- Enter: `Param("parentDisplayName")`
- Press Enter

#### 5. sdapApiBaseUrl
- Click the property
- Click **fx**
- Enter: `"https://spe-api-dev-67e2xz.azurewebsites.net"`
- Press Enter
- **Note:** No `/api` suffix - the PCF control adds it internally

---

### Step 9: Save the Page

1. Click **Save** (top-right)
2. **Name:** `sprk_documentuploaddialog` (lowercase, no spaces)
3. **Description:** "Custom page for document upload to SharePoint Embedded"
4. Click **Save**

---

### Step 10: Publish

1. Click **Publish** (top-right)
2. Wait for publish to complete
3. Success message should appear

---

## How Param() Works

When the ribbon button JavaScript calls:
```javascript
Xrm.Navigation.navigateTo({
    pageType: 'custom',
    name: 'sprk_documentuploaddialog',
    data: {
        parentEntityName: 'sprk_matter',
        parentRecordId: '12345-...',
        containerId: 'b!ABC...',
        parentDisplayName: 'Matter #12345'
    }
}, ...)
```

The Custom Page automatically receives these values via the `Param()` function:
- `Param("parentEntityName")` returns `"sprk_matter"`
- `Param("parentRecordId")` returns the GUID
- etc.

**No parameter UI definition needed!** This is the modern Power Apps pattern.

---

## Verification Checklist

After saving and publishing, verify:

- [ ] Page name is `sprk_documentuploaddialog`
- [ ] PCF control fills entire canvas
- [ ] All 5 properties have formulas (4 with Param(), 1 with URL)
- [ ] Page is published
- [ ] Page appears in Solutions → Custom Pages

---

## Next Step: Test the Custom Page

Use the test script to verify it works:

1. Open a Matter record in Dataverse (SPAARKE DEV 1)
2. Open browser DevTools (F12) → Console
3. Copy/paste contents of: `test-custom-page-navigation.js`
4. Press Enter
5. Dialog should open with the PCF control

---

**This approach works with the current Power Apps interface shown in your screenshot!**

No need to fight with the Variables/Formulas panel - just use `Param()` in property bindings.

---

**Created:** 2025-10-20
**Interface:** Power Apps Modern Canvas (Formulas-based)
**Version:** v3.0.0
