# Manual Entity Creation Steps
## sprk_uploadcontext Utility Entity for Form Dialog

Since pac CLI solution packaging has issues with new entity XML formats, follow these manual steps to create the utility entity.

---

## Step 1: Create the Entity

1. Open Power Apps (https://make.powerapps.com)
2. Select your environment (**SPAARKE DEV 1**)
3. Click **Tables** (left navigation)
4. Click **+ New table** → **Create new tables**
5. Enter table details:
   - **Display name**: `Upload Context`
   - **Plural name**: `Upload Contexts`
   - **Description**: `Utility entity for passing parameters to Universal Document Upload PCF control via Form Dialog`
6. Click **Primary column** (expand):
   - **Display name**: `Name`
   - **Schema name**: `sprk_name`
7. Click **Save**

---

## Step 2: Add Custom Fields

After the entity is created, add these 4 custom fields:

### Field 1: Parent Entity Name
- Click **+ New** → **Column**
- **Display name**: `Parent Entity Name`
- **Data type**: `Text`
- **Schema name**: `sprk_parententityname`
- **Max length**: `100`
- **Required**: No
- **Searchable**: No
- Click **Save**

### Field 2: Parent Record ID
- Click **+ New** → **Column**
- **Display name**: `Parent Record ID`
- **Data type**: `Text`
- **Schema name**: `sprk_parentrecordid`
- **Max length**: `100`
- **Required**: No
- **Searchable**: No
- Click **Save**

### Field 3: Container ID
- Click **+ New** → **Column**
- **Display name**: `Container ID`
- **Data type**: `Text`
- **Schema name**: `sprk_containerid`
- **Max length**: `100`
- **Required**: No
- **Searchable**: No
- Click **Save**

### Field 4: Parent Display Name
- Click **+ New** → **Column**
- **Display name**: `Parent Display Name`
- **Data type**: `Text`
- **Schema name**: `sprk_parentdisplayname`
- **Max length**: `200`
- **Required**: No
- **Searchable**: No
- Click **Save**

---

## Step 3: Configure Entity Settings

1. Click **Properties** for the entity
2. Configure:
   - **Enable for activities**: No
   - **Enable for mobile**: No
   - **Enable for quick find search**: No
   - **Appear in search results**: No
   - **Can be taken offline**: No
3. Click **Save**

---

## Step 4: Create the Form

1. While viewing the `sprk_uploadcontext` entity, click **Forms**
2. Click **+ New form** → **Main form**
3. Form designer opens

### 4.1: Configure Form Properties
1. Click form name (top left), rename to: `Upload Documents`
2. Click **Form settings** (gear icon):
   - **Max width**: 600
   - **Height**: 700
3. Click **Save**

### 4.2: Add Hidden Fields Section
1. In the form designer, click **+ Section**
2. Section properties:
   - **Section label**: `Hidden Parameters`
   - **Show label**: Unchecked
   - **Visible**: Unchecked (IMPORTANT!)
3. Drag these fields into the hidden section:
   - `sprk_parententityname`
   - `sprk_parentrecordid`
   - `sprk_containerid`
   - `sprk_parentdisplayname`
4. For each field, click field → Properties → **Uncheck "Show label"**

### 4.3: Add PCF Control Section
1. Click **+ Section** (create a new visible section)
2. Section properties:
   - **Section label**: `Document Upload`
   - **Show label**: Unchecked
3. Click inside the section
4. Click **+ Component** (from left panel)
5. Select **Get more components** (bottom of panel)
6. Search for: `UniversalDocumentUpload`
7. Select the control, click **Add**
8. Back in form designer, the component appears in the component list
9. Drag `UniversalDocumentUpload` into the visible section

### 4.4: Bind PCF Control Properties
**CRITICAL STEP**: The PCF control needs to be bound to the form fields.

1. Click the PCF control in the form
2. Click **Properties** (right panel)
3. You should see 4 bindable properties:
   - **Parent Entity Name**
   - **Parent Record ID**
   - **Container ID**
   - **Parent Display Name**

4. For each property, click the dropdown and select the matching field:
   - `Parent Entity Name` → Bind to `sprk_parententityname`
   - `Parent Record ID` → Bind to `sprk_parentrecordid`
   - `Container ID` → Bind to `sprk_containerid`
   - `Parent Display Name` → Bind to `sprk_parentdisplayname`

5. **Verify bindings**: Each property should show the field name, NOT "(unbound)"

### 4.5: Resize PCF Control
1. Click the PCF control
2. Drag the bottom edge to make it fill most of the form (recommended height: ~650px)
3. The control should occupy the entire visible section

### 4.6: Save and Publish Form
1. Click **Save**
2. Click **Publish**
3. **Get Form ID** (optional but recommended):
   - Click **Settings** → **Form Properties**
   - Copy the Form ID GUID
   - Save it for later (we'll add it to the Web Resource)

---

## Step 5: Verify Form Configuration

1. Click **Preview** → **Create form**
2. **Expected**: PCF control should be visible
3. **Check Console** (F12):
   - Should NOT see "Missing parameters" errors
   - Should see logger initialization messages

**If you see "Missing parameters"**:
- Go back to form designer
- Click PCF control → Properties
- Verify all 4 properties are bound (not "(unbound)")
- Re-publish form

---

## Step 6: Test Form Dialog (Manual Test)

Before testing from the button, test the form directly:

1. Open a Matter record (with sprk_containerid)
2. Open browser console (F12)
3. Paste and run this test code:

```javascript
const formParameters = {
    sprk_name: "TEST_" + Date.now(),
    sprk_parententityname: "sprk_matter",
    sprk_parentrecordid: "{YOUR_MATTER_GUID}",  // Replace with actual GUID
    sprk_containerid: "{YOUR_CONTAINER_ID}",    // Replace with actual container ID
    sprk_parentdisplayname: "Test Matter"
};

const formOptions = {
    entityName: "sprk_uploadcontext",
    openInNewWindow: false,
    windowPosition: 1,
    width: 600,
    height: 700
};

Xrm.Navigation.openForm(formOptions, formParameters).then(
    result => console.log("Success", result),
    error => console.error("Error", error)
);
```

4. **Expected**: Form Dialog opens with PCF control visible
5. **Verify**: PCF control should show the Matter name in the header
6. **Verify**: File selection should work
7. Close the dialog

---

## Step 7: Update Web Resource with Form ID (Optional)

If you want more reliable form opening, add the Form ID to the Web Resource:

1. Get the Form ID from Step 4.6
2. Edit `subgrid_commands.js`
3. Find line ~299:
```javascript
const formOptions = {
    entityName: "sprk_uploadcontext",
    // ADD THIS LINE:
    formId: "{PASTE-FORM-ID-HERE}",
    openInNewWindow: false,
    windowPosition: 1,
    width: 600,
    height: 700
};
```
4. Re-deploy the Web Resource

---

## Step 8: Test End-to-End from Ribbon Button

1. Open a Matter record (with sprk_containerid)
2. Scroll to **Documents** subgrid
3. Click **"Quick Create: Document"** button
4. **Expected**: Form Dialog opens (same as Step 6)
5. Select files → Fill metadata → Click Upload
6. **Expected**: Files upload to SPE, Document records created
7. **Expected**: Subgrid refreshes showing new documents

---

## Troubleshooting

### Issue: "Missing parameters" error in console

**Cause**: PCF control properties not bound to form fields

**Fix**:
1. Edit form in designer
2. Click PCF control → Properties
3. Verify all 4 properties show field names (NOT "(unbound)")
4. If unbound, use dropdowns to select correct fields
5. Save and Publish

### Issue: Form doesn't open

**Cause**: Entity not created or form not published

**Fix**:
1. Verify entity exists: Power Apps → Tables → Search "Upload Context"
2. Verify form exists: Click entity → Forms → "Upload Documents" should be listed
3. Verify form is published: Edit form → Check status (should not say "Draft")

### Issue: PCF control not in component list

**Cause**: PCF control v2.0.1 not deployed

**Fix**:
1. Redeploy PCF control:
   ```bash
   cd c:\code_files\spaarke\src\controls\UniversalQuickCreate
   pac pcf push --publisher-prefix sprk
   ```
2. Refresh form designer

### Issue: Files upload but no documents created

**Cause**: Lookup field configuration incorrect

**Fix**: Verify [EntityDocumentConfig.ts](c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate\config\EntityDocumentConfig.ts) has correct settings for the parent entity.

---

## Success Criteria

✅ Entity `sprk_uploadcontext` exists
✅ Entity has 5 fields (sprk_name + 4 custom fields)
✅ Form "Upload Documents" exists
✅ Form has hidden section with 4 fields
✅ Form has visible section with PCF control
✅ PCF control properties are bound (NOT unbound)
✅ Form is published
✅ Clicking ribbon button opens Form Dialog
✅ PCF control is visible and functional
✅ Files upload successfully
✅ Documents created in Dataverse
✅ Subgrid refreshes

---

## Next Steps After Manual Creation

Once the entity and form are created manually, you can:

1. Export the solution for version control
2. Add to automated deployment pipeline
3. Test on other entities (Account, Contact, Project, Invoice)

---

**Estimated Time**: 15-20 minutes

**Prerequisites**:
- PCF Control v2.0.1 deployed ✅
- Web Resource v2.1.0 deployed (pending)
