# Universal Document Upload - Deployment Guide

## Overview
This guide covers deploying the Universal Document Upload PCF control, Custom Page, and Web Resource to Dataverse.

## ✅ Completed: PCF Control Deployment

The PCF control has been successfully deployed to Dataverse:
- **Control Name**: `sprk_Spaarke.Controls.UniversalDocumentUpload`
- **Version**: 2.0.0
- **Status**: ✅ Published and Available
- **Deployment Method**: `pac pcf push`

## Remaining Deployment Steps

### Step 1: Deploy Web Resource (Automated Option)

The Web Resource contains the command button logic for launching the Custom Page dialog.

#### Option A: Manual Upload via Power Apps Portal

1. Go to https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Navigate to **Solutions** → **Universal Quick Create** (or create new solution)
4. Click **+ New** → **More** → **Web resource**
5. Configure:
   - **Name**: `sprk_subgrid_commands.js`
   - **Display Name**: "Subgrid Commands - Universal Document Upload"
   - **Type**: Script (JScript)
   - **Upload file**: Browse to `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/WebResources/sprk_subgrid_commands.js`
6. Click **Save** and **Publish**

#### Option B: Automated via Solution Export/Import

```bash
# TODO: Create solution package script
# This will package the Web Resource into the solution ZIP
cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
pac solution export --name "UniversalQuickCreate" --path ./export
pac solution import --path ./UniversalQuickCreate.zip
```

### Step 2: Create Custom Page (Manual - Required)

Custom Pages for Model-Driven Apps must be created through the Power Apps Studio interface. There is currently no fully automated deployment method for Custom Pages with PCF control bindings.

#### Manual Creation Steps:

1. **Open Power Apps Maker Portal**
   - Navigate to: https://make.powerapps.com
   - Select environment: **SPAARKE DEV 1**

2. **Create New Custom Page**
   - Click **+ Create** in the left navigation
   - Select **Custom page** (or **Blank page**)
   - Name: `sprk_universaldocumentupload_page`
   - Display Name: "Universal Document Upload"

3. **Add the PCF Control to Canvas**
   - In the left panel, click **+ Insert**
   - Scroll to **Code components** section
   - Find and select: **UniversalDocumentUpload** (Spaarke.Controls)
   - Drag it onto the canvas
   - Set width to **100%** and height to **100%**

4. **Configure Custom Page Parameters**
   - In the left panel, expand **Custom page settings** or click the settings icon
   - Under **Parameters**, click **+ New parameter** for each of the following:

   | Parameter Name | Data Type | Required | Description |
   |---|---|---|---|
   | `parentEntityName` | Text | Yes | Logical name of parent entity (e.g., sprk_matter, account) |
   | `parentRecordId` | Text | Yes | GUID of parent record (without curly braces) |
   | `containerId` | Text | Yes | SharePoint Embedded Container ID |
   | `parentDisplayName` | Text | No | Display name for UI header (e.g., "Matter #12345") |

5. **Bind PCF Control Properties to Parameters**
   - Select the PCF control on the canvas
   - In the properties panel (right side), locate each property:
     - `parentEntityName` → Set to formula: `Parameters.parentEntityName`
     - `parentRecordId` → Set to formula: `Parameters.parentRecordId`
     - `containerId` → Set to formula: `Parameters.containerId`
     - `parentDisplayName` → Set to formula: `Parameters.parentDisplayName`
     - `sdapApiBaseUrl` → Set to: `"spe-api-dev-67e2xz.azurewebsites.net/api"` (or your environment's API URL)

6. **Configure Dialog Settings**
   - In **Custom page settings**, set:
     - **Page type**: Dialog
     - **Width**: 600px or 50%
     - **Height**: 80%
     - **Title**: "Quick Create: Document"

7. **Save and Publish**
   - Click **Save** in the top toolbar
   - Click **Publish** to make it available

#### Reference JSON
The complete Custom Page definition is available in:
`src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/CustomPages/sprk_universaldocumentupload_page.json`

Use this as a reference for the structure and parameters.

### Step 3: Configure Command Buttons on Entity Forms

Add the "Quick Create: Document" button to the Documents subgrid on each entity form.

#### Entities to Configure:
- **Matter** (sprk_matter)
- **Project** (sprk_project)
- **Invoice** (sprk_invoice)
- **Account** (account)
- **Contact** (contact)

#### Option A: Using Ribbon Workbench (Recommended)

1. Install Ribbon Workbench solution if not already installed
2. Open the entity in Ribbon Workbench
3. Add a new button to the subgrid ribbon
4. Configure button:
   - **Label**: "Quick Create: Document"
   - **Command**: Create new command
   - **Action**: JavaScript Function
   - **Library**: `sprk_subgrid_commands.js`
   - **Function Name**: `Spaarke.Commands.AddMultipleDocuments`
   - **Pass execution context**: Yes
5. Save and publish

#### Option B: Using Command Designer (Modern)

1. Open the form in form editor
2. Select the **Documents** subgrid
3. Click **+ Add command**
4. **Command type**: Run JavaScript
5. **Function**: `Spaarke.Commands.AddMultipleDocuments`
6. **Library**: Select `sprk_subgrid_commands.js`
7. **Pass control**: Enable
8. Save and publish the form

#### Command Function Reference

The command function is located in `sprk_subgrid_commands.js` and has this signature:

```javascript
function Spaarke_AddMultipleDocuments(selectedControl) {
    // Gets parent context automatically
    // Opens Custom Page dialog with parameters
    // Refreshes subgrid after dialog closes
}
```

### Step 4: Verify Deployment

1. **Hard Refresh Browser**
   - Press `Ctrl + Shift + R` to clear cached customizations

2. **Test on Matter Form**
   - Open any Matter record
   - Scroll to Documents subgrid
   - Verify the "Quick Create: Document" button appears
   - Click the button
   - Verify the Custom Page dialog opens
   - Verify the PCF control loads with version badge: **"✓ UNIVERSAL DOCUMENT UPLOAD V2.0.0 - CUSTOM PAGE"**

3. **Test File Upload**
   - Select 2-3 small files (< 1MB each)
   - Verify file list displays
   - Fill in Document Type (manual entry or lookup)
   - Click "Save and Create Documents"
   - Verify:
     - Upload progress displays
     - Files upload to SharePoint Embedded
     - Document records create in Dataverse
     - Success message displays
     - Dialog closes
     - Subgrid refreshes showing new documents

4. **Test on Other Entities**
   - Repeat verification on Project, Invoice, Account, Contact
   - Verify dynamic parent context detection works

## Architecture Overview

### Component Interaction Flow

```
1. User clicks "Quick Create: Document" button on entity form
   ↓
2. sprk_subgrid_commands.js command function executes
   - Reads parent entity name, record ID, container ID from form
   - Validates parent context
   ↓
3. Xrm.Navigation.navigateTo() opens Custom Page dialog
   - Passes parameters: parentEntityName, parentRecordId, containerId, parentDisplayName
   ↓
4. Custom Page loads PCF control with parameters bound
   ↓
5. PCF control (index.ts) initializes
   - Validates GUID format
   - Resolves entity configuration from EntityDocumentConfig
   - Renders React UI
   ↓
6. User selects files and clicks "Save and Create Documents"
   ↓
7. Two-phase workflow executes:
   Phase 1: MultiFileUploadService uploads files to SharePoint Embedded (parallel)
   Phase 2: DocumentRecordService creates Dataverse records (sequential via Xrm.WebApi)
   ↓
8. PCF sets shouldClose output property to true
   ↓
9. Custom Page Timer (bound to shouldClose) triggers and calls Exit()
   ↓
9. Command script refreshes subgrid (selectedControl.refresh())
```

### Universal Entity Support

The control supports multiple parent entities through configuration:

**Supported Entities** (configured in `EntityDocumentConfig.ts`):
- sprk_matter
- sprk_project
- sprk_invoice
- account
- contact

**Adding New Entities**:
1. Add configuration entry to `ENTITY_DOCUMENT_CONFIGS` in `EntityDocumentConfig.ts`
2. Add configuration entry to `ENTITY_CONFIGURATIONS` in `sprk_subgrid_commands.js`
3. Rebuild and redeploy PCF control
4. Redeploy Web Resource
5. Configure command button on new entity's form

## Files Deployed

| File | Location | Deployment Method | Status |
|------|----------|------------------|--------|
| PCF Control | Dataverse Control Registry | `pac pcf push` | ✅ Deployed |
| Web Resource | Dataverse Web Resources | Manual upload or solution import | ⏳ Pending |
| Custom Page | Dataverse Custom Pages | Manual creation in Power Apps Studio | ⏳ Pending |
| Command Buttons | Entity Form Ribbons | Ribbon Workbench or Command Designer | ⏳ Pending |

## Troubleshooting

### Control Not Loading
- Hard refresh browser (Ctrl + Shift + R)
- Check browser console for errors
- Verify PAC CLI deployment succeeded
- Verify control is published in Dataverse

### Custom Page Not Found
- Verify Custom Page name matches exactly: `sprk_universaldocumentupload_page`
- Verify Custom Page is published
- Check Custom Page is added to the solution

### Command Button Not Appearing
- Verify Web Resource is published
- Verify form customizations are published
- Clear browser cache
- Check ribbon customization XML

### Upload Fails
- Verify SDAP BFF API is running and accessible
- Verify Container ID is valid
- Check browser network tab for API errors
- Verify MSAL authentication is configured

## Version Information

- **PCF Control Version**: 2.0.0
- **Deployment Date**: October 10, 2025
- **Environment**: SPAARKE DEV 1
- **Publisher Prefix**: sprk

## Support

For issues or questions, refer to:
- Implementation documentation in `dev/projects/` folder
- Phase-by-phase implementation guides
- Code comments in source files
