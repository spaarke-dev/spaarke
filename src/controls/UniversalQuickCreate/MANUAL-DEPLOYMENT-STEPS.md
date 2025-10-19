# Manual Deployment Steps - Remaining Items

## Status Summary

✅ **PCF Control** - Deployed successfully via `pac pcf push`
- Control: `Spaarke.Controls.UniversalDocumentUpload` v2.0.0
- Status: Published and available

⏳ **Web Resource** - Needs manual upload
⏳ **Ribbon Button** - Needs manual configuration
⏳ **Custom Page** - Needs manual creation

---

## Step 1: Upload Web Resource (5 minutes)

### File Location
`c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\WebResources\sprk_subgrid_commands.js`

### Upload Steps
1. Go to https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Navigate to **Solutions** → **Spaarke Core** (or any solution)
4. Click **+ New** → **More** → **Web resource**
5. Fill in:
   - **Name**: `sprk_subgrid_commands.js`
   - **Display Name**: `Subgrid Commands - Universal Document Upload`
   - **Type**: Script (JScript)
6. Click **Upload file** and browse to the file above
7. Click **Save**
8. Click **Publish**

---

## Step 2: Add Ribbon Button to Matter Entity (10 minutes)

You have two options:

### Option A: Using Command Designer (Modern - Recommended)

1. Go to https://make.powerapps.com
2. Navigate to **Tables** → **Matter** (sprk_matter)
3. Click on **Forms** tab
4. Open the main form where the Documents subgrid appears
5. Click on the **Documents subgrid** in the form
6. In the properties panel, find **Command bar** section
7. Click **Edit command bar**
8. Click **+ New command**
9. Configure:
   - **Label**: `Quick Create: Document`
   - **Icon**: Choose an upload/add icon
   - **Action**: Run JavaScript
   - **Library**: Select `sprk_subgrid_commands.js`
   - **Function name**: `Spaarke_AddMultipleDocuments`
   - **Pass execution context**: ✅ Yes (checked)
10. Click **Save**
11. **Publish** the form

### Option B: Using Ribbon Workbench (Classic)

1. Install Ribbon Workbench from Microsoft AppSource (if not already installed)
2. Open Ribbon Workbench
3. Select solution: **Spaarke Core**
4. Select entity: **Matter** (sprk_matter)
5. In the ribbon editor, navigate to **SubGrid** → **sprk_document** → **Actions**
6. Click **Add Button**
7. Configure button:
   - **Label**: `Quick Create: Document`
   - **Command**: Create new command
   - **Icon**: Select an appropriate icon
8. Create command:
   - **Action**: JavaScript Function
   - **Library**: `sprk_subgrid_commands.js`
   - **Function**: `Spaarke_AddMultipleDocuments`
   - **Parameters**: `SelectedControl` (CRM Parameter)
9. Create enable rule (optional):
   - Always enabled for web client
10. Click **Publish** to save changes

---

## Step 3: Create Custom Page (15 minutes)

### Why Manual?
Custom Pages with PCF control bindings and parameters cannot be programmatically deployed via PAC CLI. They must be created in Power Apps Studio.

### Creation Steps

1. Go to https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Click **+ Create** → **Custom page**
4. Choose **Blank page**
5. Name: `sprk_universaldocumentupload_page`
6. Display Name: `Universal Document Upload`

#### Add PCF Control to Canvas

7. In the left panel, click **+ Insert**
8. Scroll to **Code components** section
9. Find: **UniversalDocumentUpload** (Spaarke.Controls)
10. Drag it onto the canvas
11. Resize to fill canvas: Width = 100%, Height = 100%

#### Configure Custom Page Parameters

12. Click the settings icon or **Custom page settings**
13. Under **Parameters**, click **+ New parameter** for each:

| Parameter Name | Data Type | Required | Default |
|---|---|---|---|
| `parentEntityName` | Text | Yes | _(none)_ |
| `parentRecordId` | Text | Yes | _(none)_ |
| `containerId` | Text | Yes | _(none)_ |
| `parentDisplayName` | Text | No | _(none)_ |

#### Bind Control Properties

14. Select the PCF control on canvas
15. In properties panel (right side), bind each property:
   - `parentEntityName` → Formula: `Parameters.parentEntityName`
   - `parentRecordId` → Formula: `Parameters.parentRecordId`
   - `containerId` → Formula: `Parameters.containerId`
   - `parentDisplayName` → Formula: `Parameters.parentDisplayName`
   - `sdapApiBaseUrl` → Value: `"spe-api-dev-67e2xz.azurewebsites.net/api"`

#### Configure Dialog Settings

16. In **Custom page settings**:
   - **Type**: Dialog
   - **Width**: 600px or 50%
   - **Height**: 80%

#### Save and Publish

17. Click **Save** in top toolbar
18. Click **Publish** to make available

---

## Step 4: Test the Complete Workflow (10 minutes)

1. **Hard refresh browser**: Press `Ctrl + Shift + R`
2. **Open a Matter record**
3. **Scroll to Documents subgrid**
4. **Verify button appears**: "Quick Create: Document"
5. **Click the button**
6. **Verify Custom Page opens** with the PCF control
7. **Test file upload**:
   - Select 2-3 small files (< 1MB each)
   - Fill in Document Type (manual entry)
   - Click "Save and Create Documents"
8. **Verify**:
   - Files upload to SharePoint Embedded
   - Document records created in Dataverse
   - Success message displays
   - Dialog closes
   - Subgrid refreshes with new documents

---

## Step 5: Repeat for Other Entities (Optional)

Repeat **Step 2** (Add Ribbon Button) for these entities:
- **Project** (sprk_project)
- **Invoice** (sprk_invoice)
- **Account** (account)
- **Contact** (contact)

---

## Troubleshooting

### Button Doesn't Appear
- Hard refresh browser (Ctrl + Shift + R)
- Clear browser cache completely
- Close and reopen the form
- Wait 1-2 minutes for ribbon cache to clear
- Check that Web Resource is published
- Verify form customizations are published

### Custom Page Not Found Error
- Verify Custom Page name exactly matches: `sprk_universaldocumentupload_page`
- Verify Custom Page is published
- Check browser console for errors

### Upload Fails
- Verify SDAP BFF API is running
- Check Container ID exists on parent record
- Verify MSAL authentication is configured
- Check browser network tab for API errors

---

## Reference Files

### Web Resource
`c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\WebResources\sprk_subgrid_commands.js`

### Custom Page JSON Reference
`c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\CustomPages\sprk_universaldocumentupload_page.json`

### Ribbon XML Reference
`c:\code_files\spaarke\dev\projects\quickcreate_pcf_component\RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`

---

## Summary

The programmatic deployment approach worked for the PCF control but gets complex for the remaining components due to Dataverse limitations. The manual steps above are straightforward and take about 30-40 minutes total.

Once completed, you'll have a fully functional multi-file document upload system that works across all parent entity types.
