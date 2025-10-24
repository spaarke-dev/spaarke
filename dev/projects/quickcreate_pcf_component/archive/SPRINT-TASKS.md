# Sprint Tasks: Custom Page Migration - Detailed Task Breakdown

**Sprint:** Custom Page Migration for Universal Quick Create
**Version:** 2.3.0 → 3.0.0
**Sprint Duration:** 2 weeks

---

## Table of Contents

1. [Task 1: Create Custom Page](#task-1-create-custom-page)
2. [Task 2: Update PCF Control for Custom Page](#task-2-update-pcf-control-for-custom-page)
3. [Task 3: Update Ribbon Commands](#task-3-update-ribbon-commands)
4. [Task 4: Solution Packaging](#task-4-solution-packaging)
5. [Task 5: Testing & Validation](#task-5-testing--validation)
6. [Task 6: Deployment to DEV](#task-6-deployment-to-dev)
7. [Task 7: User Acceptance Testing](#task-7-user-acceptance-testing)
8. [Task 8: Documentation & Knowledge Transfer](#task-8-documentation--knowledge-transfer)

---

## Task 1: Create Custom Page

**Estimate:** 8 hours
**Assignee:** [Developer Name]
**Status:** 🔴 Not Started

### Pre-Task Review Prompt

```
TASK REVIEW: Before starting Task 1, review the current project state.

1. Read the following files:
   - dev/projects/quickcreate_pcf_component/ARCHITECTURE.md
   - dev/projects/quickcreate_pcf_component/SPRINT-PLAN.md

2. Verify prerequisites:
   - DEV Dataverse environment accessible
   - pac CLI installed and authenticated
   - Power Apps Maker Portal access

3. Check for any recent changes:
   - Has the PCF control been modified since v2.3.0?
   - Are there any pending commits related to Custom Pages?
   - Is the BFF API healthy and accessible?

4. Review Microsoft documentation:
   - Custom Pages overview
   - PCF controls in Custom Pages
   - Parameter passing to Custom Pages

5. Output your findings and confirm:
   - "Ready to proceed" OR
   - "Blockers found: [list blockers]"

If blockers found, resolve before proceeding.
```

---

### Task Context

**What:** Create a Custom Page dialog that embeds the UniversalQuickCreate PCF control

**Why:** Replace Quick Create form with modern modal dialog experience

**Critical Constraints:**
- ⚠️ Custom Page must pass ALL navigation parameters correctly
- ⚠️ PCF control must receive parameters in init() method
- ⚠️ Dialog must support close/cancel behavior
- ⚠️ No changes to BFF API or backend services

---

### Knowledge Required

**Files to Review:**
1. `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`
   - Review input properties defined
   - Note property types and requirements

2. `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
   - Lines 1-100: Review init() method
   - Understand how parameters are currently read

3. Microsoft Docs:
   - [Custom Pages for Model-Driven Apps](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-overview)
   - [Use PCF controls in Custom Pages](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/component-framework-for-canvas-apps)

---

### Vibe Coding Instructions

#### Step 1.1: Review PCF Control Manifest

**Prompt:**
```
Review the PCF control manifest and identify all input parameters.

File: src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml

Tasks:
1. Read the entire ControlManifest.Input.xml file
2. List all <property> elements with their:
   - name
   - display-name-key
   - of-type
   - usage (bound/input)
   - required (true/false)
3. Identify which properties are required for Custom Page mode
4. Note the control namespace (e.g., "sprk_Spaarke.Controls.UniversalQuickCreate")

Output Format:
```yaml
control:
  namespace: "sprk_Spaarke.Controls.UniversalQuickCreate"
  version: "2.3.0.0"

properties:
  - name: parentEntityName
    type: SingleLine.Text
    required: true
    description: "Parent entity logical name (e.g., sprk_matter)"

  - name: parentRecordId
    type: SingleLine.Text
    required: true
    description: "Parent record GUID"

  # ... list all properties
```

Guardrails:
- Do NOT modify the ControlManifest.Input.xml file
- Only READ and document
- If any property is unclear, flag for clarification
```

---

#### Step 1.2: Create Custom Page JSON

**Prompt:**
```
Create a Custom Page JSON definition for the document upload dialog.

Context:
- Custom Page will be created using Power Apps Maker Portal or pac CLI
- Must embed UniversalQuickCreate PCF control
- Must accept navigation parameters from ribbon button
- Must support dialog mode (modal, centered)

Knowledge:
- Review: dev/projects/quickcreate_pcf_component/ARCHITECTURE.md (lines 287-360)
- Custom Page schema version: 1.0.0.0
- Dialog dimensions: 800px width, auto height

Create file: dev/projects/quickcreate_pcf_component/custom-page-definition.json

Template:
```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/powerapps/custom-pages/v1.0/custom-page.schema.json",
  "name": "sprk_documentuploaddialog",
  "displayName": "Document Upload",
  "description": "Custom page for uploading documents to SharePoint Embedded",
  "type": "Dialog",
  "parameters": {
    "parentEntityName": {
      "type": "String",
      "required": true,
      "description": "Parent entity logical name"
    },
    "parentRecordId": {
      "type": "String",
      "required": true,
      "description": "Parent record GUID"
    },
    "containerId": {
      "type": "String",
      "required": true,
      "description": "SharePoint Embedded container ID"
    },
    "parentDisplayName": {
      "type": "String",
      "required": false,
      "description": "Parent record display name"
    }
  },
  "components": [
    {
      "id": "uploadControl",
      "type": "pcfControl",
      "controlName": "sprk_Spaarke.Controls.UniversalQuickCreate",
      "properties": {
        "parentEntityName": "={Parent.parentEntityName}",
        "parentRecordId": "={Parent.parentRecordId}",
        "containerId": "={Parent.containerId}",
        "parentDisplayName": "={Parent.parentDisplayName}",
        "sdapApiBaseUrl": "https://spe-api-dev-67e2xz.azurewebsites.net"
      }
    }
  ],
  "onClose": {
    "action": "RefreshParent"
  }
}
```

Guardrails:
- ⚠️ Control namespace MUST match ControlManifest.Input.xml exactly
- ⚠️ All required PCF properties MUST be mapped
- ⚠️ Parameter names are case-sensitive
- ⚠️ sdapApiBaseUrl must point to correct BFF API endpoint (DEV: spe-api-dev-67e2xz)
- ⚠️ If unsure about syntax, consult Microsoft Custom Pages schema documentation
```

---

#### Step 1.3: Create Custom Page in Dataverse (Power Apps Maker Portal)

**Prompt:**
```
Create the Custom Page in Dataverse using Power Apps Maker Portal.

Prerequisites Check:
1. Verify you have Power Apps Maker Portal access
2. Verify UniversalQuickCreate PCF control is deployed to DEV environment
3. Verify you have System Customizer role minimum

Steps:

1. Navigate to Power Apps Maker Portal (https://make.powerapps.com/)
   - Select SPAARKE DEV 1 environment

2. Create Custom Page:
   - Go to Solutions → Default Solution (or create new solution)
   - Click "+ New" → "Page"
   - Select "Blank Page with columns"
   - Name: "Document Upload Dialog"
   - Layout: Single column

3. Add PCF Control:
   - Click "+ Insert" → "Get more components"
   - Select "Code" tab
   - Search for "UniversalQuickCreate"
   - Click "Add" (if not already added)
   - Drag PCF control onto canvas

4. Configure Page Parameters:
   - Click "..." (ellipsis) on page → "Settings"
   - Go to "Parameters" tab
   - Add parameters (click "+ New parameter" for each):

     Parameter 1:
     - Name: parentEntityName
     - Data type: Text
     - Required: Yes

     Parameter 2:
     - Name: parentRecordId
     - Data type: Text
     - Required: Yes

     Parameter 3:
     - Name: containerId
     - Data type: Text
     - Required: Yes

     Parameter 4:
     - Name: parentDisplayName
     - Data type: Text
     - Required: No

5. Bind PCF Control Properties:
   - Select the PCF control on canvas
   - In properties pane (right side), bind each property:

     parentEntityName:
     - Click fx → Select "parentEntityName" (page parameter)

     parentRecordId:
     - Click fx → Select "parentRecordId" (page parameter)

     containerId:
     - Click fx → Select "containerId" (page parameter)

     parentDisplayName:
     - Click fx → Select "parentDisplayName" (page parameter)

     sdapApiBaseUrl:
     - Enter directly: "https://spe-api-dev-67e2xz.azurewebsites.net"

6. Configure Page as Dialog:
   - Page Settings → Display
   - Type: Dialog
   - Width: 800
   - Height: Auto
   - Position: Center

7. Save and Publish:
   - Click "Save"
   - Name: "sprk_DocumentUploadDialog"
   - Click "Publish"

8. Verify:
   - Note the Custom Page name (sprk_documentuploaddialog)
   - Test navigation (will do in Task 3)

Guardrails:
- ⚠️ STOP if PCF control not found - must deploy PCF first
- ⚠️ STOP if environment is PROD - use DEV only
- ⚠️ Parameter names must match exactly (case-sensitive)
- ⚠️ Don't forget to Publish after saving
- ⚠️ Take screenshots of configuration for documentation

Troubleshooting:
- If PCF control not showing: Run `pac pcf push` to deploy
- If parameters not binding: Check property names in manifest
- If page won't save: Check for validation errors in properties pane
```

---

#### Step 1.4: Test Parameter Passing (Manual Test)

**Prompt:**
```
Create a test harness to verify Custom Page receives parameters correctly.

Context:
- Before updating ribbon buttons, we need to verify parameter passing works
- Use browser console to test navigation manually

Test Script:

1. Open Power Apps in browser
2. Navigate to any Matter record form in SPAARKE DEV 1
3. Open browser console (F12)
4. Execute test navigation:

```javascript
// Test navigation to Custom Page
(async function testCustomPageNavigation() {
    const formContext = Xrm.Page;

    // Get parameters from current form
    const entityName = formContext.data.entity.getEntityName();
    const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');
    const containerIdAttr = formContext.getAttribute('sprk_containerid');

    if (!containerIdAttr) {
        console.error('❌ sprk_containerid field not found on form');
        return;
    }

    const containerId = containerIdAttr.getValue();

    if (!containerId) {
        console.error('❌ Container ID is empty. Add a container ID to this record first.');
        return;
    }

    const displayNameField = entityName === 'sprk_matter' ? 'sprk_matternumber' :
                            entityName === 'sprk_project' ? 'sprk_projectname' : 'name';
    const displayName = formContext.getAttribute(displayNameField)?.getValue() || 'Test Record';

    console.log('📋 Navigation Parameters:', {
        parentEntityName: entityName,
        parentRecordId: recordId,
        containerId: containerId,
        parentDisplayName: displayName
    });

    // Navigate to Custom Page
    const pageInput = {
        pageType: 'custom',
        name: 'sprk_documentuploaddialog',
        data: {
            parentEntityName: entityName,
            parentRecordId: recordId,
            containerId: containerId,
            parentDisplayName: displayName
        }
    };

    const navigationOptions = {
        target: 2,      // Dialog
        position: 1,    // Center
        width: { value: 800, unit: 'px' },
        height: { value: 600, unit: 'px' }
    };

    console.log('🚀 Opening Custom Page dialog...');

    try {
        const result = await Xrm.Navigation.navigateTo(pageInput, navigationOptions);
        console.log('✅ Dialog closed successfully', result);
    } catch (error) {
        console.error('❌ Navigation failed:', error);
    }
})();
```

Expected Results:
1. Console shows navigation parameters (all populated)
2. Custom Page dialog opens centered on screen
3. PCF control renders inside dialog
4. No errors in console (check for parameter binding errors)

Validation:
- Open browser DevTools → Network tab
- Look for calls to BFF API (https://spe-api-dev-67e2xz.azurewebsites.net)
- Verify OAuth token is being acquired
- Check for any 401/403 errors

Success Criteria:
✅ Dialog opens
✅ PCF control visible
✅ No console errors
✅ Network shows MSAL token acquisition
✅ Can close dialog (click X or Cancel)

If Issues:
- Parameter not passed: Check Custom Page parameter binding
- Control not rendering: Check PCF control deployment
- OAuth errors: Check MSAL configuration in PCF control
- Dialog won't open: Check page name spelling (case-sensitive)

Document Results:
- Take screenshot of dialog open
- Take screenshot of console output (parameters logged)
- Take screenshot of Network tab (MSAL token acquisition)
- Save to: dev/projects/quickcreate_pcf_component/testing/task1-screenshots/
```

---

### Acceptance Criteria

- [ ] Custom Page JSON definition created
- [ ] Custom Page created in Power Apps Maker Portal
- [ ] All input parameters defined and bound
- [ ] PCF control embedded and configured
- [ ] Dialog mode configured (800px width, centered)
- [ ] Manual test passes (dialog opens, parameters received)
- [ ] No console errors during test
- [ ] Screenshots captured for documentation
- [ ] Custom Page name documented: `sprk_documentuploaddialog`

---

### Deliverables

1. ✅ `dev/projects/quickcreate_pcf_component/custom-page-definition.json`
2. ✅ Custom Page deployed to SPAARKE DEV 1
3. ✅ Test results documented with screenshots
4. ✅ Any issues/blockers documented

---

### Rollback Plan

If Custom Page creation fails:
1. Delete Custom Page from solution
2. Document issue in SPRINT-TASKS.md
3. Continue with Task 2 (can test with Quick Create form initially)

---

## Task 2: Update PCF Control for Custom Page

**Estimate:** 12 hours
**Assignee:** [Developer Name]
**Status:** 🔴 Not Started
**Depends On:** Task 1 (Optional - can proceed in parallel)

### Pre-Task Review Prompt

```
TASK REVIEW: Before starting Task 2, review the current PCF control implementation.

1. Read the following files in full:
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DocumentUploadForm.tsx
   - src/controls/UniversalQuickCreate/UniversalQuickCreate/types/index.ts

2. Verify Phase 7 implementation is intact:
   - NavMapClient exists and is imported
   - DocumentRecordService uses NavMapClient
   - No recent breaking changes to service layer

3. Check current build status:
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```
   Expected: Build succeeds with 0 errors

4. Review recent git commits:
   ```bash
   git log --oneline -10 src/controls/UniversalQuickCreate/
   ```
   Look for any uncommitted changes or conflicts

5. Verify BFF API is healthy:
   ```bash
   curl -i https://spe-api-dev-67e2xz.azurewebsites.net/healthz
   ```
   Expected: HTTP 200 OK

6. Output your findings:
   - "Ready to proceed - no blockers" OR
   - "Issues found: [list issues]"

If issues found, resolve before proceeding.
```

---

### Task Context

**What:** Update PCF control to support Custom Page dialog mode while maintaining backward compatibility with Quick Create form

**Why:** PCF control needs to detect context and handle dialog lifecycle independently

**Critical Constraints:**
- ⚠️ MUST maintain backward compatibility (Quick Create form still works)
- ⚠️ NO changes to service layer (NavMapClient, DocumentRecordService, etc.)
- ⚠️ NO changes to BFF API
- ⚠️ Phase 7 functionality must remain intact
- ⚠️ All existing entities must continue to work

---

### Knowledge Required

**Files to Understand:**

1. **Current PCF Control Structure:**
   ```
   src/controls/UniversalQuickCreate/UniversalQuickCreate/
   ├── index.ts                          ← MODIFY (main entry point)
   ├── components/
   │   ├── DocumentUploadForm.tsx        ← REVIEW (may need minor updates)
   │   ├── FileSelectionField.tsx        ← NO CHANGE
   │   └── UploadProgressBar.tsx         ← NO CHANGE
   ├── services/
   │   ├── NavMapClient.ts               ← NO CHANGE (Phase 7)
   │   ├── DocumentRecordService.ts      ← NO CHANGE (Phase 7)
   │   ├── FileUploadService.ts          ← NO CHANGE
   │   ├── MultiFileUploadService.ts     ← NO CHANGE
   │   ├── SdapApiClient.ts              ← NO CHANGE
   │   └── MsalAuthProvider.ts           ← NO CHANGE
   ├── config/
   │   └── EntityDocumentConfig.ts       ← NO CHANGE (Phase 7)
   └── types/
       └── index.ts                      ← MAY ADD (dialog-related types)
   ```

2. **Architecture Documents:**
   - `dev/projects/quickcreate_pcf_component/ARCHITECTURE.md` (lines 364-466)
   - `docs/PHASE-7-DEPLOYMENT-STATUS.md` (Phase 7 implementation details)

3. **PCF Framework Documentation:**
   - [PCF Control Lifecycle](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/control-lifecycle)
   - [Navigation API](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation)

---

### Vibe Coding Instructions

#### Step 2.1: Add Context Detection Logic

**Prompt:**
```
Add logic to detect whether PCF control is running in Custom Page or Quick Create form context.

Context:
- PCF control needs to behave differently based on where it's hosted
- Custom Page: Autonomous workflow, can close dialog programmatically
- Quick Create: Form-dependent workflow, relies on form save

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts

Knowledge:
- Custom Page context: context.page exists and context.page.type === 'custom'
- Quick Create context: context.page doesn't exist or context.page.type === 'quickCreateForm'

Task:
1. Add a private field to track context mode
2. Add a method to detect context in init()
3. Log context for debugging

Add after existing private fields (around line 40):

```typescript
/**
 * Tracks whether control is running in Custom Page or Quick Create form
 * @since 3.0.0
 */
private isCustomPageMode: boolean = false;
```

Add new method (around line 100, after constructor):

```typescript
/**
 * Detect hosting context (Custom Page vs Quick Create Form)
 *
 * Custom Page indicators:
 * - context.page exists
 * - context.page.type === 'custom'
 * - context.navigation.close exists
 *
 * @param context - PCF context
 * @returns true if running in Custom Page, false if Quick Create form
 * @since 3.0.0
 */
private detectHostingContext(context: ComponentFramework.Context<IInputs>): boolean {
    // Check for Custom Page API
    if (context.page && context.page.type === 'custom') {
        logInfo('UniversalQuickCreate', 'Detected Custom Page context', {
            pageType: context.page.type,
            hasNavigationClose: !!(context.navigation && context.navigation.close)
        });
        return true;
    }

    // Default to Quick Create form mode
    logInfo('UniversalQuickCreate', 'Detected Quick Create form context', {
        hasPage: !!context.page,
        pageType: context.page?.type
    });
    return false;
}
```

Update init() method to call detectHostingContext():

Find the init() method (around line 150) and add at the beginning:

```typescript
public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void, state: ComponentFramework.Dictionary, container: HTMLDivElement): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    // NEW: Detect hosting context (Custom Page vs Quick Create)
    this.isCustomPageMode = this.detectHostingContext(context);

    logInfo('UniversalQuickCreate', 'Initializing UniversalDocumentUpload PCF', {
        version: '3.0.0',
        mode: this.isCustomPageMode ? 'Custom Page' : 'Quick Create Form'
    });

    // ... rest of existing init() code
}
```

Guardrails:
- ⚠️ Do NOT remove any existing code
- ⚠️ Only ADD new code
- ⚠️ Use logInfo() for all logging (already imported)
- ⚠️ Test compilation after changes: npm run build
- ⚠️️ If build fails, review TypeScript errors carefully
```

---

#### Step 2.2: Implement Dialog Close Method

**Prompt:**
```
Add method to close Custom Page dialog programmatically after successful upload.

Context:
- In Custom Page mode, control should close dialog automatically after all documents created
- In Quick Create mode, form handles close behavior (no change needed)

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts

Add new method (around line 300, after detectHostingContext):

```typescript
/**
 * Close the Custom Page dialog programmatically
 *
 * Only works in Custom Page mode. In Quick Create form mode, this is a no-op.
 *
 * @since 3.0.0
 */
private closeDialog(): void {
    if (!this.isCustomPageMode) {
        logInfo('UniversalQuickCreate', 'closeDialog() called in Quick Create mode - no-op');
        return;
    }

    if (this.context.navigation && this.context.navigation.close) {
        logInfo('UniversalQuickCreate', 'Closing Custom Page dialog');
        this.context.navigation.close();
    } else {
        logError('UniversalQuickCreate', 'Cannot close dialog - navigation.close not available', {
            hasNavigation: !!this.context.navigation,
            hasClose: !!(this.context.navigation && this.context.navigation.close)
        });
    }
}
```

Guardrails:
- ⚠️ This method should ONLY be called in Custom Page mode
- ⚠️ Always check this.isCustomPageMode first
- ⚠️ Log all actions for debugging
- ⚠️ Do NOT call context.navigation.close() directly elsewhere
```

---

#### Step 2.3: Update Upload Workflow to Support Custom Page Mode

**Prompt:**
```
Update the upload workflow to work independently of form save in Custom Page mode.

Context:
- Current implementation (v2.3.0): Relies on Quick Create form save event
- New requirement (v3.0.0): Custom Page mode needs autonomous workflow
- Must maintain backward compatibility with Quick Create form

File to Modify: src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts

Find the main upload handler method (likely named handleUploadAndCreate or similar).

Current behavior (Quick Create form):
```typescript
// Triggered by form save event
// Creates records during save
// Form handles close/refresh
```

New behavior (Custom Page):
```typescript
// Triggered by button click (no form save)
// Creates records independently
// Control closes dialog on success
```

Locate the method that handles upload completion (search for where documents are created).
It might look like this:

```typescript
private async handleUploadComplete(files: SpeFileMetadata[], formData: FormData): Promise<void> {
    // ... existing code that creates Document records ...

    // Add at the end, after successful creation:
    const successCount = createResults.filter(r => r.success).length;
    const failureCount = createResults.filter(r => !r.success).length;

    logInfo('DocumentUploadForm', 'Upload workflow complete', { successCount, failureCount });

    // NEW: In Custom Page mode, close dialog if all successful
    if (this.isCustomPageMode && failureCount === 0) {
        logInfo('DocumentUploadForm', 'All documents created successfully, closing dialog');

        // Small delay to allow user to see success message
        setTimeout(() => {
            this.closeDialog();
        }, 1500);
    } else if (this.isCustomPageMode && failureCount > 0) {
        // Some failures - show summary, don't auto-close
        logInfo('DocumentUploadForm', 'Some documents failed, keeping dialog open for user review');
    } else {
        // Quick Create mode - form handles close behavior
        logInfo('DocumentUploadForm', 'Quick Create mode - form will handle close behavior');
    }
}
```

Guardrails:
- ⚠️ Do NOT auto-close if there are failures (let user review errors)
- ⚠️ Add 1.5 second delay before closing (user needs to see success message)
- ⚠️ Preserve Quick Create form behavior (don't close form programmatically)
- ⚠️ Log all decision points
- ⚠️ Test both modes after changes
```

---

#### Step 2.4: Update React Components (If Needed)

**Prompt:**
```
Review and update React components to support Custom Page dialog mode.

Context:
- Most React components should work as-is
- May need to update button labels or styling for dialog mode
- Progress indicators should work the same

File to Review: src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DocumentUploadForm.tsx

Review Tasks:

1. Check button labels:
   - "Upload & Create" button - OK as-is
   - "Cancel" button - verify it works in dialog

2. Check progress indicators:
   - Should show during upload
   - Should show during record creation
   - Should show success/error messages

3. Check error handling:
   - Validation errors (file size, count, type)
   - Upload errors (SPE failures)
   - Creation errors (Dataverse failures)

Likely No Changes Needed:
- If form is purely presentational (state managed by index.ts)
- If buttons call handlers passed from parent
- If progress is driven by props

Possible Changes Needed:
- If form has form-specific logic (e.g., "Save" vs "Upload")
- If error messages reference "form" explicitly
- If styling needs dialog-specific adjustments

Action:
1. Read DocumentUploadForm.tsx in full
2. Look for any references to:
   - "form.save"
   - "Quick Create"
   - Form-specific styling
3. If found, update to be context-agnostic
4. If not found, no changes needed

Example Update (if needed):

Before:
```typescript
<Button onClick={handleSave}>Save and Upload</Button>
```

After:
```typescript
<Button onClick={handleUploadAndCreate}>
    {isCustomPageMode ? 'Upload & Create' : 'Save'}
</Button>
```

Guardrails:
- ⚠️ Only change if necessary
- ⚠️ Maintain backward compatibility
- ⚠️ Test both modes after changes
- ⚠️ Keep component as "dumb" as possible (state in index.ts)
```

---

#### Step 2.5: Update Version and Build

**Prompt:**
```
Update PCF control version to 3.0.0 and rebuild.

Files to Modify:

1. src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml

   Find:
   ```xml
   <control namespace="sprk_Spaarke.Controls" ... version="2.3.0.0">
   ```

   Update to:
   ```xml
   <control namespace="sprk_Spaarke.Controls" ... version="3.0.0.0">
   ```

2. src/controls/UniversalQuickCreate/package.json

   Find:
   ```json
   "version": "2.3.0",
   ```

   Update to:
   ```json
   "version": "3.0.0",
   ```

3. Rebuild PCF Control:
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

   Expected Output:
   - No TypeScript errors
   - No compilation warnings
   - Output: [pcf] Code component built successfully
   - Bundle created in: out/controls/UniversalQuickCreate/

4. Test Build Locally:
   ```bash
   npm start
   ```

   - Opens test harness in browser
   - Verify control renders
   - Check console for errors
   - Close test harness (Ctrl+C)

Guardrails:
- ⚠️ Version must be 3.0.0.0 (4 parts) in ControlManifest
- ⚠️ Version must be 3.0.0 (3 parts) in package.json
- ⚠️ Stop if build fails - fix errors before proceeding
- ⚠️ Do NOT deploy yet (deployment in Task 4)
```

---

#### Step 2.6: Test PCF Control Locally (Test Harness)

**Prompt:**
```
Test updated PCF control using test harness before deploying to Dataverse.

Prerequisites:
- Build successful (Step 2.5)
- Test harness configured

Steps:

1. Start test harness:
   ```bash
   cd src/controls/UniversalQuickCreate
   npm start
   ```

2. Simulate Custom Page Parameters:

   In test harness browser, open DevTools console and set test data:

   ```javascript
   // Simulate Custom Page context
   window.testParameters = {
       parentEntityName: 'sprk_matter',
       parentRecordId: '12345678-1234-1234-1234-123456789012',
       containerId: 'b!test-container-id-here',
       parentDisplayName: 'Test Matter #12345',
       sdapApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net'
   };

   console.log('✅ Test parameters set:', window.testParameters);
   ```

3. Test Context Detection:

   - Check console logs for "Detected Custom Page context" or "Detected Quick Create form context"
   - Should default to Quick Create in test harness (no Custom Page API)

4. Test File Selection:

   - Click "Browse" or drag files
   - Try 1 file (should work)
   - Try 10 files (should work)
   - Try 11 files (should show error)
   - Try oversized file (should show error)

5. Mock Upload Test:

   - Since test harness has no real BFF API, upload will fail
   - That's expected - we're testing UI behavior
   - Check for proper error handling

6. Test Build Output:

   - No console errors (except expected API failures)
   - UI renders correctly
   - Buttons are clickable
   - Progress indicators work

Success Criteria:
✅ Test harness starts without errors
✅ Control renders with all UI elements
✅ Context detection logs appear
✅ File validation works
✅ Error messages display correctly
✅ No TypeScript runtime errors

Document Results:
- Screenshot of test harness UI
- Screenshot of console logs
- Note any issues found
```

---

### Acceptance Criteria

- [ ] Context detection logic added (isCustomPageMode)
- [ ] closeDialog() method implemented
- [ ] Upload workflow updated for autonomous mode
- [ ] React components reviewed (updated if needed)
- [ ] Version updated to 3.0.0
- [ ] Build successful (0 errors, 0 warnings)
- [ ] Test harness validation passed
- [ ] Backward compatibility maintained (Quick Create still works)
- [ ] All Phase 7 functionality intact
- [ ] Code documented with comments
- [ ] Changes logged in git (commit but don't push yet)

---

### Deliverables

1. ✅ Modified `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
2. ✅ Updated `ControlManifest.Input.xml` (v3.0.0)
3. ✅ Updated `package.json` (v3.0.0)
4. ✅ Build output in `out/controls/UniversalQuickCreate/`
5. ✅ Test harness screenshots
6. ✅ Git commit with detailed message

---

### Rollback Plan

If PCF control updates break existing functionality:
1. Revert git commit
2. Rebuild from v2.3.0
3. Document issues
4. Review and fix before re-attempting

---

## Task 3: Update Ribbon Commands

**Estimate:** 6 hours
**Assignee:** [Developer Name]
**Status:** 🔴 Not Started
**Depends On:** Task 1 (Custom Page created), Task 2 (PCF updated)

### Pre-Task Review Prompt

```
TASK REVIEW: Before starting Task 3, verify Tasks 1 & 2 are complete.

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
   ```bash
   cd /c/code_files/spaarke
   find . -name "*ribbon*" -o -name "*command*" | grep -i document
   ```

5. Check for any web resources related to commands:
   ```bash
   cd /c/code_files/spaarke
   find . -name "*.js" | xargs grep -l "openQuickCreate\|document.*upload"
   ```

6. Output findings:
   - "Ready to proceed" OR
   - "Missing prerequisites: [list]"
```

---

### Task Context

**What:** Update ribbon button commands to navigate to Custom Page instead of Quick Create form

**Why:** Enable users to access new Custom Page dialog from entity forms

**Critical Constraints:**
- ⚠️ Must work on all 5 entities (Matter, Project, Invoice, Account, Contact)
- ⚠️ Must pass all required parameters correctly
- ⚠️ Must handle missing container ID gracefully
- ⚠️ Must refresh subgrid after dialog closes

---

### Knowledge Required

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

### Vibe Coding Instructions

#### Step 3.1: Locate Existing Ribbon Command Files

**Prompt:**
```
Find all ribbon customization files and web resources that implement document upload button.

Search Strategy:

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

Create file: dev/projects/quickcreate_pcf_component/ribbon-analysis.md

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

Guardrails:
- ⚠️ Read ALL RibbonDiff.xml files completely
- ⚠️ Don't assume structure - each entity may differ slightly
- ⚠️ Note any custom parameters or special handling
- ⚠️ If files are not found where expected, search entire repo
```

---

#### Step 3.2: Update Web Resource JavaScript Function

**Prompt:**
```
Update the web resource JavaScript to navigate to Custom Page instead of Quick Create form.

File to Modify: src/WebResources/sprk_subgrid_commands.js (or identified file from Step 3.1)

Context:
- Current: Uses Xrm.Utility.openQuickCreate()
- New: Uses Xrm.Navigation.navigateTo() with Custom Page
- Must support all entity types dynamically

Find the existing function (likely named openDocumentUpload or similar).

Current Implementation (v2.3.0):
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

New Implementation (v3.0.0):
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

Guardrails:
- ⚠️ Function name changed: openDocumentUpload → openDocumentUploadDialog
- ⚠️ Must handle missing container ID gracefully (error message)
- ⚠️ Must refresh subgrid after dialog closes
- ⚠️ Must handle all 5 entity types
- ⚠️ Must include try/catch for error handling
- ⚠️ Must log to console for debugging
- ⚠️ Update RibbonDiff.xml to call new function name
```

---

#### Step 3.3: Update RibbonDiff.xml Files

**Prompt:**
```
Update RibbonDiff.xml files for each entity to call the new function.

Context:
- Function name changed from openDocumentUpload to openDocumentUploadDialog
- Web resource remains the same (sprk_subgrid_commands.js)
- All other ribbon configuration stays the same

Files to Modify (for each entity):
- src/Entities/sprk_matter/RibbonDiff.xml
- src/Entities/sprk_project/RibbonDiff.xml
- src/Entities/sprk_invoice/RibbonDiff.xml
- src/Entities/account/RibbonDiff.xml
- src/Entities/contact/RibbonDiff.xml

For EACH file, find the <CommandDefinition> element.

Current (v2.3.0):
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

New (v3.0.0):
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

Change Summary:
- FunctionName: openDocumentUpload → openDocumentUploadDialog
- Library: (no change)
- Parameters: (no change)
- Everything else: (no change)

Repeat for ALL 5 entities.

Guardrails:
- ⚠️ Only change FunctionName attribute
- ⚠️ Do NOT change Library attribute
- ⚠️ Do NOT change CommandDefinition Id
- ⚠️ Do NOT change EnableRules or DisplayRules
- ⚠️ Verify XML is well-formed (no syntax errors)
- ⚠️ Make identical change across all entities
```

---

#### Step 3.4: Test Ribbon Button (Local Validation)

**Prompt:**
```
Validate ribbon button changes before deploying.

Pre-Deployment Checks:

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

Guardrails:
- ⚠️ Do NOT push to remote yet (wait for full testing)
- ⚠️ Commit locally for rollback capability
- ⚠️ If validation fails, fix before proceeding
```

---

### Acceptance Criteria

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

### Deliverables

1. ✅ `ribbon-analysis.md` (current implementation documented)
2. ✅ Updated `src/WebResources/sprk_subgrid_commands.js`
3. ✅ Updated `src/Entities/sprk_matter/RibbonDiff.xml`
4. ✅ Updated `src/Entities/sprk_project/RibbonDiff.xml`
5. ✅ Updated `src/Entities/sprk_invoice/RibbonDiff.xml`
6. ✅ Updated `src/Entities/account/RibbonDiff.xml`
7. ✅ Updated `src/Entities/contact/RibbonDiff.xml`
8. ✅ Git commit with detailed message

---

### Rollback Plan

If ribbon button issues occur after deployment:
1. Revert JavaScript function to v2.3.0
2. Revert RibbonDiff.xml files to v2.3.0
3. Republish customizations
4. Users can still use Quick Create form

---

## Task 4: Solution Packaging

**Estimate:** 4 hours
**Assignee:** [Developer Name]
**Status:** 🔴 Not Started
**Depends On:** Tasks 1, 2, 3 complete

### Pre-Task Review Prompt

```
TASK REVIEW: Verify all components are ready for packaging.

1. Verify Task 1 Complete:
   - [ ] Custom Page exists in SPAARKE DEV 1
   - [ ] Custom Page name: sprk_documentuploaddialog

2. Verify Task 2 Complete:
   - [ ] PCF control version: 3.0.0
   - [ ] Build output exists: src/controls/UniversalQuickCreate/out/
   - [ ] No build errors

3. Verify Task 3 Complete:
   - [ ] Web resource updated
   - [ ] All 5 RibbonDiff.xml updated
   - [ ] Changes committed locally

4. Check Solution Structure:
   ```bash
   cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
   ls -la
   ```
   Expected: Solution project files (.cdsproj)

5. pac CLI Verification:
   ```bash
   pac --version
   ```
   Expected: Power Platform CLI version 1.x.x or higher

6. pac Authentication:
   ```bash
   pac auth list
   ```
   Expected: SPAARKE DEV 1 environment listed

Output: "Ready to package" OR "Issues: [list]"
```

---

### Task Context

**What:** Package all components into deployable solution (.zip)

**Why:** Create single artifact for deployment to environments

**Components to Package:**
1. Custom Page (sprk_documentuploaddialog)
2. PCF Control (UniversalQuickCreate v3.0.0)
3. Web Resources (sprk_subgrid_commands.js)
4. Ribbon Customizations (RibbonDiff.xml for 5 entities)

---

### Vibe Coding Instructions

#### Step 4.1: Deploy PCF Control to Dataverse

**Prompt:**
```
Deploy PCF control v3.0.0 to SPAARKE DEV 1 environment.

Context:
- pac pcf push deploys PCF and updates solution
- Existing solution: Check if UniversalQuickCreate is in a solution already
- If yes: Updates existing control
- If no: Creates new solution

Commands:

1. Navigate to PCF project:
   ```bash
   cd src/controls/UniversalQuickCreate
   ```

2. Verify authentication:
   ```bash
   pac auth list
   ```

   Look for SPAARKE DEV 1:
   ```
   * [Dev] SPAARKE DEV 1
     URL: https://spaarkedev1.crm.dynamics.com
     Type: Dataverse
   ```

   If not active (no *), set as active:
   ```bash
   pac auth select --index 0
   ```

3. Deploy PCF Control:
   ```bash
   pac pcf push --publisher-prefix sprk
   ```

   Expected Output:
   ```
   Uploading PowerApps Component Framework control to Dataverse...
   Component 'sprk_Spaarke.Controls.UniversalQuickCreate' uploaded successfully.
   Import completed successfully.
   ```

4. Verify Deployment:
   ```bash
   pac pcf list
   ```

   Look for:
   ```
   sprk_Spaarke.Controls.UniversalQuickCreate (v3.0.0.0)
   ```

5. Test in Power Apps:
   - Open Power Apps Maker Portal
   - Go to Solutions → Default Solution
   - Look for "UniversalQuickCreate" control
   - Version should show 3.0.0

Troubleshooting:
- If "Authentication required": Run `pac auth create`
- If "Publisher prefix not found": Verify 'sprk' prefix exists in environment
- If "Control already exists": Update is automatic, that's OK
- If errors: Read error message carefully, may need to delete old version first

Guardrails:
- ⚠️ Use --publisher-prefix sprk (must match existing prefix)
- ⚠️ Deploy to DEV environment only (verify URL)
- ⚠️ If deployment fails, do NOT proceed - fix errors first
```

---

#### Step 4.2: Export Solution from Dataverse

**Prompt:**
```
Export solution containing all updated components.

Context:
- Solution should contain: Custom Page, PCF Control, Web Resources, Ribbon Customizations
- Export as Unmanaged for DEV/UAT, Managed for PROD

Option A: Export via pac CLI (Recommended)

1. List solutions:
   ```bash
   pac solution list
   ```

   Find solution containing document upload components.
   Note the solution name (e.g., "SpaarkeDocumentUpload")

2. Export solution:
   ```bash
   pac solution export \
     --name SpaarkeDocumentUpload \
     --path ./SpaarkeDocumentUpload_3_0_0_0.zip \
     --managed false
   ```

   Expected Output:
   ```
   Exporting solution...
   Solution exported successfully: ./SpaarkeDocumentUpload_3_0_0_0.zip
   ```

Option B: Export via Power Apps Maker Portal

1. Navigate to https://make.powerapps.com/
2. Select SPAARKE DEV 1 environment
3. Go to Solutions
4. Find "Spaarke Document Upload" solution
5. Click "Export"
6. Select "Unmanaged"
7. Click "Export"
8. Wait for download
9. Save as: SpaarkeDocumentUpload_3_0_0_0.zip

3. Verify Solution Contents:

   ```bash
   # Extract to temp folder
   mkdir -p temp_solution
   cd temp_solution
   unzip ../SpaarkeDocumentUpload_3_0_0_0.zip

   # List contents
   ls -R
   ```

   Expected files:
   ```
   [Content_Types].xml
   customizations.xml
   solution.xml

   Controls/
     sprk_UniversalQuickCreate_*.xml

   CustomPages/
     sprk_documentuploaddialog_*.xml

   WebResources/
     sprk_subgrid_commands.js

   Other/
     Customizations.xml (contains RibbonDiff)
   ```

4. Verify Version in solution.xml:
   ```bash
   grep "Version" solution.xml
   ```

   Expected: <Version>3.0.0.0</Version>

5. Clean up:
   ```bash
   cd ..
   rm -rf temp_solution
   ```

Guardrails:
- ⚠️ Export UNMANAGED for now (managed comes later for PROD)
- ⚠️ Verify version is 3.0.0.0
- ⚠️ Verify all components present (Custom Page, PCF, Web Resources, Ribbon)
- ⚠️ If solution is incomplete, add missing components before exporting
```

---

#### Step 4.3: Update Solution Version and Publisher Info

**Prompt:**
```
Verify and update solution metadata.

Context:
- Solution version should be 3.0.0.0
- Publisher should be Spaarke Inc. (or correct publisher)
- Description should mention Custom Page migration

Option A: Update via pac CLI

```bash
pac solution online-version \
  --solution-name SpaarkeDocumentUpload \
  --solution-version 3.0.0.0
```

Option B: Update via Power Apps Maker Portal

1. Open solution in Maker Portal
2. Click "..." → Settings
3. Update version: 3.0.0.0
4. Update description:
   ```
   Universal Document Upload v3.0.0 - Custom Page Migration

   Changes:
   - Replaced Quick Create form with Custom Page dialog
   - Modern modal UI experience
   - Autonomous workflow (no form dependency)
   - Maintains Phase 7 dynamic metadata discovery
   - Supports 5 entity types: Matter, Project, Invoice, Account, Contact
   ```
5. Save

Option C: Manual XML Update (Advanced)

If you have the exported solution:

1. Extract solution:
   ```bash
   unzip SpaarkeDocumentUpload_3_0_0_0.zip -d solution_temp
   cd solution_temp
   ```

2. Edit solution.xml:
   ```xml
   <ImportExportXml version="9.1" SolutionPackageVersion="9.1" ...>
     <SolutionManifest>
       <UniqueName>SpaarkeDocumentUpload</UniqueName>
       <LocalizedNames>
         <LocalizedName description="Spaarke Document Upload" languagecode="1033" />
       </LocalizedNames>
       <Descriptions>
         <Description description="Universal Document Upload v3.0.0 - Custom Page Migration" languagecode="1033" />
       </Descriptions>
       <Version>3.0.0.0</Version>
       ...
     </SolutionManifest>
   </ImportExportXml>
   ```

3. Re-zip:
   ```bash
   zip -r ../SpaarkeDocumentUpload_3_0_0_0_updated.zip *
   ```

Guardrails:
- ⚠️ Version format: X.Y.Z.W (must have 4 parts for Dataverse)
- ⚠️ Publisher must match existing publisher in environment
- ⚠️ If updating XML manually, validate XML syntax before re-zipping
```

---

#### Step 4.4: Document Solution Contents and Deploy Instructions

**Prompt:**
```
Create deployment documentation for the solution package.

Create file: dev/projects/quickcreate_pcf_component/DEPLOYMENT-PACKAGE.md

```markdown
# Deployment Package: SpaarkeDocumentUpload v3.0.0

**File:** `SpaarkeDocumentUpload_3_0_0_0.zip`
**Version:** 3.0.0.0
**Created:** 2025-10-20
**Type:** Unmanaged (for DEV/UAT)

---

## Package Contents

### 1. Custom Page
- **Name:** sprk_documentuploaddialog
- **Display Name:** Document Upload Dialog
- **Type:** Dialog (Modal)
- **Purpose:** Replace Quick Create form with modern UI

### 2. PCF Control
- **Name:** sprk_Spaarke.Controls.UniversalQuickCreate
- **Version:** 3.0.0.0
- **Changes:**
  - Added Custom Page mode detection
  - Added autonomous workflow support
  - Added closeDialog() method
  - Maintains Phase 7 dynamic metadata discovery

### 3. Web Resources
- **sprk_subgrid_commands.js**
  - Updated function: openDocumentUploadDialog
  - Navigation changed: Quick Create → Custom Page
  - Added container ID validation
  - Added subgrid auto-refresh

### 4. Ribbon Customizations
- **Entities:** sprk_matter, sprk_project, sprk_invoice, account, contact
- **Changes:** Updated command to call new function
- **File:** RibbonDiff.xml (in each entity folder)

---

## Prerequisites

### Environment Requirements
- Dataverse environment (DEV, UAT, or PROD)
- Power Platform CLI (pac) installed
- System Customizer role minimum
- Custom Pages feature enabled

### Dependencies
- BFF API operational: https://spe-api-dev-67e2xz.azurewebsites.net
- Phase 7 deployed (NavMap endpoints)
- Redis cache available
- SharePoint Embedded containers configured

### No Changes Required
- ✅ BFF API (no changes)
- ✅ Dataverse schema (no changes)
- ✅ Security roles (no changes)
- ✅ Service layer code (no changes)

---

## Deployment Steps

### Step 1: Backup Current Configuration

```bash
# Export current solution (v2.3.0) as backup
pac solution export \
  --name SpaarkeDocumentUpload \
  --path ./SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip \
  --managed false
```

### Step 2: Import Solution

Option A: pac CLI
```bash
pac solution import \
  --path SpaarkeDocumentUpload_3_0_0_0.zip \
  --async \
  --publish-changes
```

Option B: Power Apps Maker Portal
1. Navigate to https://make.powerapps.com/
2. Select target environment
3. Go to Solutions
4. Click "Import solution"
5. Upload SpaarkeDocumentUpload_3_0_0_0.zip
6. Click "Next" → "Import"
7. Wait for completion
8. Click "Publish all customizations"

### Step 3: Verify Deployment

1. Custom Page exists:
   - Solutions → SpaarkeDocumentUpload → Pages
   - Look for "Document Upload Dialog"

2. PCF Control version:
   - Solutions → SpaarkeDocumentUpload → Controls
   - Verify "UniversalQuickCreate" shows v3.0.0.0

3. Web Resources updated:
   - Solutions → SpaarkeDocumentUpload → Web Resources
   - Open sprk_subgrid_commands.js
   - Verify function name: openDocumentUploadDialog

4. Ribbon buttons work:
   - Open a Matter record
   - Look for "Upload Documents" button on Documents subgrid
   - Click button
   - Custom Page dialog should open

### Step 4: Smoke Testing

Test on ONE entity first (e.g., Matter):

1. Open existing Matter record with container ID
2. Click "Upload Documents" button
3. Verify dialog opens (modal, centered)
4. Select 1 test file
5. Click "Upload & Create"
6. Verify:
   - File uploads to SPE
   - Document record created in Dataverse
   - Dialog closes automatically
   - Subgrid refreshes
   - New document visible

If successful, test remaining entities:
- sprk_project
- sprk_invoice
- account
- contact

---

## Rollback Procedure

If critical issues occur:

### Option 1: Restore Backup (Quick)
```bash
pac solution import \
  --path SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip \
  --force-overwrite \
  --publish-changes
```

### Option 2: Revert Ribbon Only (Faster)
1. Update Web Resource to v2.3.0 version
2. Publish customizations
3. Users see Quick Create form again
4. Custom Page remains but unused

---

## Known Issues

None at time of packaging.

---

## Support

- **Documentation:** dev/projects/quickcreate_pcf_component/ARCHITECTURE.md
- **Issues:** Contact Development Team
- **Logs:** Check Application Insights for errors
```

Save this file in the project folder.

Guardrails:
- ⚠️ Include ALL deployment steps
- ⚠️ Include rollback procedure
- ⚠️ List all prerequisites
- ⚠️ Document verification steps
- ⚠️ Smoke test procedure detailed
```

---

### Acceptance Criteria

- [ ] PCF control deployed to Dataverse (v3.0.0)
- [ ] Solution exported successfully
- [ ] Solution file verified (all components present)
- [ ] Solution version updated to 3.0.0.0
- [ ] Deployment documentation created
- [ ] Solution file saved: `SpaarkeDocumentUpload_3_0_0_0.zip`
- [ ] Backup of v2.3.0 created
- [ ] All files committed to git

---

### Deliverables

1. ✅ `SpaarkeDocumentUpload_3_0_0_0.zip` (solution package)
2. ✅ `SpaarkeDocumentUpload_2_3_0_0_BACKUP.zip` (backup)
3. ✅ `DEPLOYMENT-PACKAGE.md` (deployment docs)
4. ✅ Solution verification screenshots
5. ✅ Git commit with solution file

---

## Tasks 5-8 Summary

Due to length constraints, I'll provide abbreviated versions of the remaining tasks:

---

## Task 5: Testing & Validation
**Estimate:** 12 hours
- Execute test matrix (5 entities × 6 test scenarios)
- Validate Phase 7 metadata discovery
- Performance benchmarking
- Error scenario testing
- Document all results

---

## Task 6: Deployment to DEV
**Estimate:** 4 hours
- Import solution to SPAARKE DEV 1
- Publish customizations
- Smoke testing
- Monitor Application Insights
- User communication

---

## Task 7: User Acceptance Testing
**Estimate:** 8 hours
- UAT with 3-5 real users
- Feedback collection
- Bug fixes (if needed)
- Final sign-off

---

## Task 8: Documentation & Knowledge Transfer
**Estimate:** 4 hours
- Update user documentation
- Create admin guide
- Knowledge transfer session
- Sprint retrospective

---

**End of Sprint Tasks**

For complete details on Tasks 5-8, refer to SPRINT-PLAN.md or request detailed breakdown.
