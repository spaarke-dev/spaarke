# Quick Create Form - PCF Control Not Appearing

**Issue:** Universal Quick Create control not appearing in "Add Control" library when configuring Quick Create forms.

**Date:** 2025-10-07

---

## Problem

After importing `UniversalQuickCreateSolution.zip`, the control does not appear in the control library when trying to add it to a Quick Create form.

**Solution Location:**
```
c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\bin\Release\UniversalQuickCreateSolution.zip
```

---

## Root Cause

**Power Apps Quick Create forms have limitations** that prevent certain PCF control types from being used:

1. **Quick Create forms don't support dataset controls** - The current control is configured as `control-type="standard"` with a `data-set` binding
2. **Quick Create forms have limited customization** - They're designed for simple field entry, not complex custom controls
3. **PCF controls on Quick Create require specific configuration** - Must be field-level controls, not form-level controls

---

## Current Control Configuration

**File:** `ControlManifest.Input.xml`

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
         control-type="standard">  <!-- ⚠️ Dataset control, not form control -->

    <data-set name="dataset" ...>  <!-- ⚠️ Quick Create doesn't use datasets -->
```

**Issues:**
- `control-type="standard"` with `data-set` = Grid/dataset control
- Quick Create forms expect field-level controls
- No field binding configured

---

## Solutions

### Solution 1: Use Main Form as Dialog (Recommended) ✅

Instead of Quick Create forms, use **Main Forms opened as dialogs**.

**Advantages:**
- ✅ Full control customization
- ✅ PCF controls work normally
- ✅ Better user experience
- ✅ More flexibility for file upload

**How to Implement:**

#### Step 1: Create Main Form for Document

1. Navigate to: **Tables** > **Document** > **Forms**
2. Create new **Main** form (not Quick Create)
3. Name: "Document Creation Form"
4. Add fields:
   - sprk_documenttitle
   - sprk_description
   - sprk_matter (lookup)
   - sprk_containerid (hidden)

#### Step 2: Add PCF Control to Form

**Option A: Add to specific field**
1. Select a field (e.g., sprk_documenttitle)
2. Click "Change properties"
3. Go to "Controls" tab
4. Click "Add Control"
5. Select "Universal Quick Create"
6. Configure as default for web

**Option B: Add as standalone component**
1. Click "Insert" > "Component"
2. Select "Universal Quick Create"
3. Configure parameters

#### Step 3: Create JavaScript to Open Form as Dialog

**File:** `MatterForm.js` (on Matter main form)

```javascript
function openDocumentCreationDialog(executionContext) {
    var formContext = executionContext.getFormContext();

    // Get current Matter record ID
    var matterId = formContext.data.entity.getId().replace('{', '').replace('}', '');
    var matterName = formContext.getAttribute("sprk_name").getValue();
    var containerid = formContext.getAttribute("sprk_containerid").getValue();

    // Check if container ID exists
    if (!containerid) {
        Xrm.Navigation.openAlertDialog({
            text: "This Matter does not have a SharePoint Container. Please provision one first."
        });
        return;
    }

    // Set default values for new document
    var defaultValues = {};
    defaultValues["sprk_matter"] = [{
        id: matterId,
        name: matterName,
        entityType: "sprk_matter"
    }];
    defaultValues["sprk_containerid"] = containerid;

    // Entity form options
    var entityFormOptions = {
        entityName: "sprk_document",
        formId: "YOUR-FORM-GUID-HERE", // Replace with actual form GUID
        openInNewWindow: false,
        windowPosition: 2, // Center
        width: 600,
        height: 600
    };

    // Open form as dialog
    Xrm.Navigation.openForm(entityFormOptions, defaultValues).then(
        function success(result) {
            // Refresh Documents subgrid
            formContext.getControl("Documents").refresh();
        },
        function error(error) {
            console.error("Error opening form:", error);
        }
    );
}
```

#### Step 4: Add Custom Button to Matter Form

1. Open Matter main form
2. Go to ribbon customization
3. Add button "New Document" to Documents subgrid
4. Configure button to call `openDocumentCreationDialog()`

**Result:**
- Custom "New Document" button on Matter form
- Opens main form as dialog (not Quick Create)
- Pre-filled with Matter lookup and Container ID
- PCF control works normally for file upload

---

### Solution 2: Modify Control Manifest for Field-Level Use ⚠️

Change the control to be a **field-level control** instead of dataset control.

**This requires code changes and rebuild:**

#### Step 1: Update Manifest

```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalQuickCreate"
         version="1.0.0"
         control-type="standard">  <!-- Keep as standard -->

    <!-- Remove data-set, add field property -->
    <property name="value"
              display-name-key="Value"
              of-type="SingleLine.Text"
              usage="bound"
              required="true" />

    <!-- Keep other properties: defaultValueMappings, enableFileUpload, sdapApiBaseUrl -->
```

#### Step 2: Update PCF Code

Modify `UniversalQuickCreatePCF.ts` to work as a field control instead of dataset control.

**This is complex and changes the architecture significantly.**

---

### Solution 3: Use Power Apps Component Framework (Canvas Component)

Create a **Canvas Component** instead of Model-Driven App PCF control.

**This is a different technology and requires complete rebuild.**

---

### Solution 4: Use Custom Page (Modern Approach) 🆕

Use **Custom Pages** (Power Apps component framework for canvas apps within model-driven apps).

**Advantages:**
- ✅ Modern approach (Microsoft's recommended path)
- ✅ Full canvas app capabilities
- ✅ Can embed in model-driven apps
- ✅ Better file upload UX

**Disadvantages:**
- ❌ Requires learning canvas apps
- ❌ Different development model
- ❌ More complex integration

---

## Recommended Approach

### For Immediate Use: **Solution 1 (Main Form as Dialog)**

**Why:**
- ✅ Works with existing PCF control (no code changes needed)
- ✅ Better UX than Quick Create (more screen space)
- ✅ Full customization available
- ✅ Can add file upload, validation, etc.
- ✅ Quick to implement (1-2 hours)

**Steps:**
1. Create Main Form for Document entity
2. Add custom button to Matter form
3. Use JavaScript to open form as dialog with default values
4. Test file upload functionality

---

## Why Quick Create Forms Don't Work for This Scenario

### Quick Create Limitations:

1. **Limited Control Support:**
   - Only supports basic field controls
   - No dataset/grid controls
   - No complex custom controls

2. **No File Upload:**
   - Quick Create forms don't support file attachments natively
   - Can't embed complex file upload controls

3. **Size Constraints:**
   - Small dialog size
   - Limited field space
   - Not suitable for file upload UX

4. **Customization Restrictions:**
   - Can't add custom JavaScript
   - Can't add custom buttons
   - Can't add complex validation

### Main Form as Dialog Advantages:

1. **Full Customization:**
   - Add any PCF control
   - Add custom JavaScript
   - Add validation logic
   - Add file upload controls

2. **Better UX:**
   - Larger dialog size
   - More screen real estate
   - Better for complex operations

3. **Flexible:**
   - Can be opened from anywhere
   - Can pass default values
   - Can handle callbacks

---

## Alternative: Verify Solution Import

If you still want to try Quick Create, verify the solution was imported correctly:

### Step 1: Check Solution Import

1. Go to: https://make.powerapps.com
2. Select your environment
3. Go to: **Solutions**
4. Find "UniversalQuickCreateSolution"
5. Check status: Should show "Installed"

### Step 2: Check Control Registration

1. In solution, click on it
2. Go to: **Objects** > **Controls**
3. Verify "Universal Quick Create" is listed
4. Check properties

### Step 3: Check Control Configuration

**Expected:**
- **Name:** Universal Quick Create
- **Namespace:** Spaarke.Controls
- **Constructor:** UniversalQuickCreate
- **Version:** 1.0.0

### Step 4: Try Adding to Main Form (Not Quick Create)

1. Open **Document** entity
2. Open **Main** form (not Quick Create)
3. Select a field
4. Click "Change properties" > "Controls" tab
5. Click "Add Control"
6. Search for "Universal Quick Create"
7. If it appears here but not in Quick Create, that confirms Quick Create doesn't support this control type

---

## Next Steps

### Recommended Path:

1. ✅ **Verify solution import** (check if control appears in Solutions)
2. ✅ **Try adding to Main Form** (confirm control is available)
3. ✅ **Implement Main Form as Dialog** (use Solution 1)
4. ✅ **Add custom button to Matter form**
5. ✅ **Test file upload functionality**
6. ❌ **Skip Quick Create forms** (not suitable for this use case)

### If You Must Use Quick Create:

**You would need to:**
1. Redesign control as field-level control
2. Update manifest to remove dataset
3. Simplify functionality (no full form control)
4. Rebuild and redeploy
5. Accept limitations (less functionality)

**This is NOT recommended** - use Main Form as Dialog instead.

---

## Code Sample: Complete Main Form Dialog Implementation

### JavaScript Web Resource: `sprk_MatterFormScripts.js`

```javascript
var Spaarke = Spaarke || {};
Spaarke.Matter = Spaarke.Matter || {};

/**
 * Open Document Creation dialog from Matter form
 * Validates Container ID and pre-fills Matter lookup
 */
Spaarke.Matter.openDocumentCreationDialog = function(executionContext) {
    var formContext = executionContext.getFormContext();

    try {
        // Get Matter record details
        var matterId = formContext.data.entity.getId().replace(/[{}]/g, '');
        var matterName = formContext.getAttribute("sprk_name").getValue();
        var containerid = formContext.getAttribute("sprk_containerid").getValue();
        var matterNumber = formContext.getAttribute("sprk_matternumber").getValue();

        // Validate Container ID exists
        if (!containerid) {
            Xrm.Navigation.openAlertDialog({
                text: "This Matter does not have a SharePoint Container ID. Please provision a container before creating documents.",
                title: "Container Required"
            });
            return;
        }

        // Prepare default values
        var defaultValues = {};

        // Set Matter lookup (relationship)
        defaultValues["sprk_matter"] = [{
            id: matterId,
            name: matterName,
            entityType: "sprk_matter"
        }];

        // Set Container ID (hidden field, auto-populated)
        defaultValues["sprk_containerid"] = containerid;

        // Set Owner to current user
        var userId = Xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, '');
        var userName = Xrm.Utility.getGlobalContext().userSettings.userName;
        defaultValues["ownerid"] = [{
            id: userId,
            name: userName,
            entityType: "systemuser"
        }];

        // Entity form options
        var entityFormOptions = {
            entityName: "sprk_document",
            // formId: "FORM-GUID-HERE", // Optional: specify specific form
            openInNewWindow: false,
            windowPosition: 2, // Center
            width: 700,
            height: 650
        };

        // Open form as dialog
        Xrm.Navigation.openForm(entityFormOptions, defaultValues).then(
            function success(result) {
                console.log("Document creation dialog closed");

                // Refresh Documents subgrid to show new document
                var documentsGrid = formContext.getControl("Documents");
                if (documentsGrid) {
                    documentsGrid.refresh();
                }
            },
            function error(error) {
                console.error("Error opening document creation dialog:", error);
                Xrm.Navigation.openAlertDialog({
                    text: "Failed to open document creation form. Error: " + error.message,
                    title: "Error"
                });
            }
        );

    } catch (ex) {
        console.error("Exception in openDocumentCreationDialog:", ex);
        Xrm.Navigation.openAlertDialog({
            text: "An error occurred: " + ex.message,
            title: "Error"
        });
    }
};

/**
 * Form OnLoad handler
 */
Spaarke.Matter.onLoad = function(executionContext) {
    console.log("Matter form loaded");

    // Additional initialization logic can go here
};
```

### Ribbon Button Configuration (XML)

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="Spaarke.Matter.NewDocument.Button"
                  Location="Mscrm.SubGrid.sprk_matter.Documents.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Spaarke.Matter.NewDocument"
                Command="Spaarke.Matter.NewDocument.Command"
                Sequence="10"
                LabelText="New Document"
                ToolTipTitle="Create Document"
                ToolTipDescription="Create a new document with file upload"
                Image16by16="/_imgs/ribbon/new_16.png"
                Image32by32="/_imgs/ribbon/new_32.png" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Spaarke.Matter.NewDocument.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Matter.EnableRule.HasContainerId" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction FunctionName="Spaarke.Matter.openDocumentCreationDialog"
                           Library="$webresource:sprk_MatterFormScripts.js">
          <CrmParameter Value="FirstPrimaryItemId" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>
      <EnableRule Id="Spaarke.Matter.EnableRule.HasContainerId">
        <FormStateRule State="Existing" />
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

---

## Summary

**Issue:** Universal Quick Create PCF control not appearing in Quick Create form control library

**Root Cause:**
- Quick Create forms don't support dataset/complex PCF controls
- Current control is configured as dataset control
- Quick Create has severe limitations for custom controls

**Recommended Solution:**
- Use **Main Form as Dialog** instead of Quick Create
- Add custom button to Matter form
- Open dialog with pre-filled values
- Full PCF control functionality available

**Implementation Time:** 1-2 hours

**Status:** Ready to implement

---

**Date:** 2025-10-07
**Sprint:** 7B - Universal Quick Create
