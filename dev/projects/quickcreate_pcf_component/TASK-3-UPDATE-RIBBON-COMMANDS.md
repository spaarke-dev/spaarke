# Task 3: Update Ribbon Commands

**Sprint:** Custom Page Migration v3.0.0
**Estimate:** 6 hours
**Status:** Not Started
**Depends On:** Task 1 (Custom Page created), Task 2 (PCF updated)

---

## Pre-Task Review Prompt

Before starting Task 3, verify Tasks 1 & 2 are complete:

```
TASK REVIEW: Verify prerequisites before updating ribbon commands.

1. Verify Task 1 Complete:
   - Custom Page exists in SPAARKE DEV 1
   - Custom Page name: sprk_documentuploaddialog
   - Test navigation worked (from Task 1.4)

2. Verify Task 2 Complete:
   - PCF control version 3.0.0
   - Build successful
   - Test harness validation passed

3. Review current ribbon customization:
   - Find existing ribbon button files
   - Understand current navigation logic
   - Identify all entities using document upload

4. List current ribbon button locations:
   cd /c/code_files/spaarke
   find . -name "*ribbon*" -o -name "*command*" | grep -i document

5. Check for any web resources related to commands:
   cd /c/code_files/spaarke
   find . -name "*.js" | xargs grep -l "openQuickCreate\|document.*upload"

6. Output findings:
   - "Ready to proceed" OR
   - "Missing prerequisites: [list]"
```

---

## Task Context

**What:** Update ribbon button commands to navigate to Custom Page instead of Quick Create form

**Why:** Enable users to access new Custom Page dialog from entity forms

**Critical Constraints:**
- ⚠️ Must work on all 5 entities (Matter, Project, Invoice, Account, Contact)
- ⚠️ Must pass all required parameters correctly
- ⚠️ Must handle missing container ID gracefully
- ⚠️ Must refresh subgrid after dialog closes

---

## Knowledge Required

**Files to Find and Review:**

```bash
# Find ribbon customization files
find ./src -name "*Ribbon*" -o -name "*Command*"

# Find web resources with command logic
find ./src -name "*.js" | xargs grep -l "Xrm.Utility.openQuickCreate"

# Common locations:
# - src/Entities/{EntityName}/RibbonDiff.xml
# - src/WebResources/sprk_subgrid_commands.js
# - Solution customizations XML
```

**Microsoft Documentation:**
- [Ribbon Command Definitions](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/define-ribbon-commands)
- [Xrm.Navigation.navigateTo](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto)

---

## Implementation Steps

### Step 3.1: Locate Existing Ribbon Command Files

**Prompt:**
```
Find all ribbon customization files and web resources that implement document upload button.
```

**Search Strategy:**

1. Find RibbonDiff.xml files:
   ```bash
   find ./src/Entities -name "RibbonDiff.xml"
   ```

2. Find command web resources:
   ```bash
   find ./src/WebResources -name "*.js" | xargs grep -l "document"
   ```

3. Read each found file and identify:
   - Command ID (e.g., "sprk.OpenDocumentUpload")
   - JavaScript function name (e.g., "openDocumentUploadDialog")
   - Web resource name (e.g., "sprk_subgrid_commands")
   - Entities where button appears (Matter, Project, etc.)

4. Document current implementation:

Create file: `dev/projects/quickcreate_pcf_component/ribbon-analysis.md`

```markdown
# Current Ribbon Implementation Analysis

## Entities with Document Upload Button

1. sprk_matter
   - RibbonDiff: src/Entities/sprk_matter/RibbonDiff.xml
   - Command ID: sprk.Matter.OpenDocumentUpload
   - Function: openDocumentUpload()

2. sprk_project
   - RibbonDiff: src/Entities/sprk_project/RibbonDiff.xml
   - Command ID: sprk.Project.OpenDocumentUpload
   - Function: openDocumentUpload()

... (list all entities)

## Web Resources

- sprk_subgrid_commands.js
  - Location: src/WebResources/sprk_subgrid_commands.js
  - Current function: Uses Xrm.Utility.openQuickCreate()
  - Needs update: Yes

## Current Navigation Logic

```javascript
function openDocumentUpload(primaryControl) {
    // Current implementation
    var formContext = primaryControl;
    var recordId = formContext.data.entity.getId();

    Xrm.Utility.openQuickCreate("sprk_document", {
        sprk_matter: { id: recordId, name: "..." }
    });
}
```
```

**Guardrails:**
- ⚠️ Read ALL RibbonDiff.xml files completely
- ⚠️ Don't assume structure - each entity may differ slightly
- ⚠️ Note any custom parameters or special handling
- ⚠️ If files are not found where expected, search entire repo

---

### Step 3.2: Update Web Resource JavaScript Function

**File to Modify:** `src/WebResources/sprk_subgrid_commands.js` (or identified file from Step 3.1)

**Context:**
- Current: Uses Xrm.Utility.openQuickCreate()
- New: Uses Xrm.Navigation.navigateTo() with Custom Page
- Must support all entity types dynamically

**Current Implementation (v2.3.0):**
```javascript
function openDocumentUpload(primaryControl) {
    var formContext = primaryControl;
    var entityName = formContext.data.entity.getEntityName();
    var recordId = formContext.data.entity.getId().replace(/[{}]/g, '');

    // Build Quick Create parameters
    var quickCreateParams = {};
    quickCreateParams["sprk_" + entityName] = {
        id: recordId,
        name: getDisplayName(formContext, entityName)
    };

    // Open Quick Create form
    Xrm.Utility.openQuickCreate("sprk_document", quickCreateParams);
}
```

**New Implementation (v3.0.0):**
```javascript
/**
 * Open Document Upload Custom Page dialog
 *
 * @param {object} primaryControl - Form context from ribbon button
 * @version 3.0.0
 * @since 2025-10-20
 */
function openDocumentUploadDialog(primaryControl) {
    var formContext = primaryControl;

    try {
        // Get parent entity information
        var entityName = formContext.data.entity.getEntityName();
        var recordId = formContext.data.entity.getId().replace(/[{}]/g, '');

        // Get container ID (required for SPE upload)
        var containerIdAttr = formContext.getAttribute('sprk_containerid');
        if (!containerIdAttr) {
            Xrm.Navigation.openErrorDialog({
                message: "The 'sprk_containerid' field is not available on this form. Please add it to the form or contact your administrator."
            });
            return;
        }

        var containerId = containerIdAttr.getValue();
        if (!containerId) {
            Xrm.Navigation.openAlertDialog({
                text: "This record does not have a SharePoint Container ID. Please create a container first.",
                title: "Container Required"
            });
            return;
        }

        // Get display name (entity-specific field)
        var displayName = getDisplayName(formContext, entityName);

        console.log('Opening Document Upload dialog', {
            entityName: entityName,
            recordId: recordId,
            containerId: containerId,
            displayName: displayName
        });

        // Navigate to Custom Page
        var pageInput = {
            pageType: 'custom',
            name: 'sprk_documentuploaddialog',
            data: {
                parentEntityName: entityName,
                parentRecordId: recordId,
                containerId: containerId,
                parentDisplayName: displayName
            }
        };

        var navigationOptions = {
            target: 2,      // Dialog
            position: 1,    // Center
            width: { value: 800, unit: 'px' },
            height: { value: 600, unit: 'px' }
        };

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success() {
                // Dialog closed successfully - refresh the documents subgrid
                console.log('Document upload dialog closed, refreshing subgrid');

                // Find and refresh Documents subgrid
                var gridControl = formContext.getControl('DocumentsGrid') ||
                                 formContext.getControl('DocumentsSubgrid') ||
                                 formContext.getControl('sprk_document_subgrid');

                if (gridControl && gridControl.refresh) {
                    gridControl.refresh();
                    console.log('Documents subgrid refreshed');
                } else {
                    console.warn('Documents subgrid control not found - manual refresh required');
                }
            },
            function error(err) {
                console.error('Failed to open document upload dialog', err);
                Xrm.Navigation.openErrorDialog({
                    message: 'Failed to open document upload dialog: ' + (err.message || 'Unknown error')
                });
            }
        );

    } catch (error) {
        console.error('Error in openDocumentUploadDialog', error);
        Xrm.Navigation.openErrorDialog({
            message: 'An error occurred: ' + (error.message || 'Unknown error')
        });
    }
}

/**
 * Get display name for entity record (entity-specific field)
 *
 * @param {object} formContext - Form context
 * @param {string} entityName - Entity logical name
 * @returns {string} Display name
 */
function getDisplayName(formContext, entityName) {
    var displayNameField;

    // Map entity to display name field
    switch (entityName) {
        case 'sprk_matter':
            displayNameField = 'sprk_matternumber';
            break;
        case 'sprk_project':
            displayNameField = 'sprk_projectname';
            break;
        case 'sprk_invoice':
            displayNameField = 'sprk_invoicenumber';
            break;
        case 'account':
            displayNameField = 'name';
            break;
        case 'contact':
            displayNameField = 'fullname';
            break;
        default:
            displayNameField = 'name'; // fallback
    }

    var attr = formContext.getAttribute(displayNameField);
    return attr ? attr.getValue() : 'Record';
}
```

**Guardrails:**
- ⚠️ Function name changed: openDocumentUpload → openDocumentUploadDialog
- ⚠️ Must handle missing container ID gracefully (error message)
- ⚠️ Must refresh subgrid after dialog closes
- ⚠️ Must handle all 5 entity types
- ⚠️ Must include try/catch for error handling
- ⚠️ Must log to console for debugging
- ⚠️ Update RibbonDiff.xml to call new function name

---

### Step 3.3: Update RibbonDiff.xml Files

**Files to Modify (for each entity):**
- src/Entities/sprk_matter/RibbonDiff.xml
- src/Entities/sprk_project/RibbonDiff.xml
- src/Entities/sprk_invoice/RibbonDiff.xml
- src/Entities/account/RibbonDiff.xml
- src/Entities/contact/RibbonDiff.xml

**For EACH file, find the `<CommandDefinition>` element.**

**Current (v2.3.0):**
```xml
<CommandDefinition Id="sprk.Matter.OpenDocumentUpload">
  <EnableRules>
    <EnableRule Id="sprk.FormStateRule" />
  </EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction FunctionName="openDocumentUpload" Library="$webresource:sprk_subgrid_commands.js">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

**New (v3.0.0):**
```xml
<CommandDefinition Id="sprk.Matter.OpenDocumentUpload">
  <EnableRules>
    <EnableRule Id="sprk.FormStateRule" />
  </EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction FunctionName="openDocumentUploadDialog" Library="$webresource:sprk_subgrid_commands.js">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

**Change Summary:**
- FunctionName: openDocumentUpload → openDocumentUploadDialog
- Library: (no change)
- Parameters: (no change)
- Everything else: (no change)

**Repeat for ALL 5 entities.**

**Guardrails:**
- ⚠️ Only change FunctionName attribute
- ⚠️ Do NOT change Library attribute
- ⚠️ Do NOT change CommandDefinition Id
- ⚠️ Do NOT change EnableRules or DisplayRules
- ⚠️ Verify XML is well-formed (no syntax errors)
- ⚠️ Make identical change across all entities

---

### Step 3.4: Test Ribbon Button (Local Validation)

**Pre-Deployment Checks:**

1. Validate JavaScript Syntax:
   ```bash
   # Use Node.js to check syntax
   node -c src/WebResources/sprk_subgrid_commands.js
   ```
   Expected: No output (syntax valid)

2. Validate XML Syntax:
   ```bash
   # Use xmllint if available, or Visual Studio XML validation
   xmllint --noout src/Entities/sprk_matter/RibbonDiff.xml
   xmllint --noout src/Entities/sprk_project/RibbonDiff.xml
   # ... repeat for all entities
   ```
   Expected: No errors

3. Review Changes in Git:
   ```bash
   git diff src/WebResources/sprk_subgrid_commands.js
   git diff src/Entities/*/RibbonDiff.xml
   ```

   Verify:
   - Function renamed consistently
   - New navigation logic present
   - Error handling added
   - Subgrid refresh logic present

4. Code Review Checklist:
   - [ ] Function name updated in all RibbonDiff.xml files
   - [ ] JavaScript function handles all 5 entity types
   - [ ] Container ID validation present
   - [ ] Error messages user-friendly
   - [ ] Subgrid refresh logic present
   - [ ] Console logging present for debugging
   - [ ] Custom Page name matches: 'sprk_documentuploaddialog'
   - [ ] Navigation options correct (dialog, centered, 800px)

5. Commit Changes:
   ```bash
   git add src/WebResources/sprk_subgrid_commands.js
   git add src/Entities/*/RibbonDiff.xml
   git commit -m "feat(ribbon): Update document upload to use Custom Page dialog

   - Change navigation from Quick Create to Custom Page
   - Update function: openDocumentUpload → openDocumentUploadDialog
   - Add container ID validation
   - Add subgrid auto-refresh after dialog close
   - Support all 5 entity types (Matter, Project, Invoice, Account, Contact)

   Version: 3.0.0
   Task: Sprint Task 3"
   ```

**Guardrails:**
- ⚠️ Do NOT push to remote yet (wait for full testing)
- ⚠️ Commit locally for rollback capability
- ⚠️ If validation fails, fix before proceeding

---

## Acceptance Criteria

- [ ] Existing ribbon commands located and analyzed
- [ ] Web resource JavaScript updated (openDocumentUploadDialog)
- [ ] All 5 RibbonDiff.xml files updated
- [ ] Container ID validation added
- [ ] Subgrid refresh logic added
- [ ] Error handling added
- [ ] JavaScript syntax valid (node -c passes)
- [ ] XML syntax valid (xmllint passes)
- [ ] Changes committed to git locally
- [ ] Code review checklist complete

---

## Deliverables

1. ✅ `ribbon-analysis.md` (current implementation documented)
2. ✅ Updated `src/WebResources/sprk_subgrid_commands.js`
3. ✅ Updated `src/Entities/sprk_matter/RibbonDiff.xml`
4. ✅ Updated `src/Entities/sprk_project/RibbonDiff.xml`
5. ✅ Updated `src/Entities/sprk_invoice/RibbonDiff.xml`
6. ✅ Updated `src/Entities/account/RibbonDiff.xml`
7. ✅ Updated `src/Entities/contact/RibbonDiff.xml`
8. ✅ Git commit with detailed message

---

## Rollback Plan

If ribbon button issues occur after deployment:
1. Revert JavaScript function to v2.3.0
2. Revert RibbonDiff.xml files to v2.3.0
3. Republish customizations
4. Users can still use Quick Create form

---

**Created:** 2025-10-20
**Sprint:** Custom Page Migration v3.0.0
**Version:** 1.0.0
