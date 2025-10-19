# Implementation Summary: Universal Document Upload PCF v2.0.0.0

## Complete Implementation Guide - All Phases

This document consolidates all implementation phases into a single, actionable guide with ready-to-use prompts.

---

## 📋 Pre-Implementation Checklist

- [ ] Reviewed [SPRINT-OVERVIEW.md](./SPRINT-OVERVIEW.md)
- [ ] Reviewed [ARCHITECTURE.md](./ARCHITECTURE.md)
- [ ] Reviewed [ADR-COMPLIANCE.md](./ADR-COMPLIANCE.md)
- [ ] Reviewed [CODE-REFERENCES.md](./CODE-REFERENCES.md)
- [ ] Development environment set up
- [ ] Access to Dataverse environment
- [ ] Universal Dataset Grid available for reference (Fluent UI v9 patterns)

---

## PHASE 1: Setup & Configuration (2 hours)

### Step 1.1: Create EntityDocumentConfig.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Prompt:**
```
Create EntityDocumentConfig.ts for multi-entity Document upload support.

Requirements:
- TypeScript interface for entity configuration
- Configuration map for 5 entities (Matter, Project, Invoice, Account, Contact)
- Helper function getEntityConfig(entityName: string)
- Export all types and constants

Entity configurations needed:
1. sprk_matter: lookup=sprk_matter, container=sprk_containerid, display=sprk_matternumber
2. sprk_project: lookup=sprk_project, container=sprk_containerid, display=sprk_projectname
3. sprk_invoice: lookup=sprk_invoice, container=sprk_containerid, display=sprk_invoicenumber
4. account: lookup=sprk_account, container=sprk_containerid, display=name
5. contact: lookup=sprk_contact, container=sprk_containerid, display=fullname

Follow TypeScript strict mode and single responsibility principle.
```

### Step 1.2: Update types/index.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/types/index.ts`

**Add these new types:**
```typescript
export interface ParentContext {
    entityName: string;
    recordId: string;
    displayName: string;
    containerId: string;
}

export interface UploadedFileMetadata extends SpeFileMetadata {
    localFileName: string;
    uploadTimestamp: Date;
}

export interface CreateResult {
    success: boolean;
    recordId?: string;
    fileName: string;
    error?: string;
}

export interface UploadProgress {
    current: number;  // 1-based index
    total: number;
    currentFileName: string;
    percentage: number;  // 0-100
}

export interface EntityDocumentConfig {
    entityName: string;
    lookupFieldName: string;
    containerIdField: string;
    displayNameField: string;
    entitySetName: string;
}
```

### Step 1.3: Update ControlManifest.Input.xml

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`

**Changes:**
```xml
<control version="2.0.0.0"
         display-name-key="Universal Document Upload v2"
         description-key="Multi-file upload for Custom Page (v2.0.0.0)"
         control-type="standard">

  <!-- REMOVE: bound field property -->

  <!-- ADD: Custom Page input parameters -->
  <property name="parentEntityName"
            display-name-key="Parent Entity Name"
            description-key="Logical name of parent entity (e.g., sprk_matter)"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <property name="parentRecordId"
            display-name-key="Parent Record ID"
            description-key="GUID of parent record"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <property name="containerId"
            display-name-key="Container ID"
            description-key="SharePoint Embedded container ID"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <property name="parentDisplayName"
            display-name-key="Parent Display Name"
            description-key="Display name of parent record"
            of-type="SingleLine.Text"
            usage="input"
            required="false" />
```

**✅ Checkpoint 1:** Config files created, types defined, manifest updated

---

## PHASE 2: Services Refactoring (3 hours)

### Step 2.1: Create DocumentRecordService.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`

**Prompt:**
```
Create DocumentRecordService.ts using Xrm.WebApi for unlimited record creation.

Requirements:
- Main method: createDocuments(files, parentContext, formData)
- Uses Xrm.WebApi.createRecord() (NOT context.webAPI)
- Builds OData payload with proper lookup field formatting
- Gets entity config from EntityDocumentConfig
- Sequential record creation (one at a time)
- Proper error handling with CreateResult[] return type
- TypeScript strict mode compliant

Lookup field pattern:
{
    sprk_documentname: fileName,
    sprk_graphitemid: file.itemId,
    sprk_graphdriveid: containerId,
    sprk_filesize: file.size,
    sprk_description: formData.description || '',
    [lookupFieldName]: null,  // e.g., sprk_matter: null
    [`${lookupFieldName}@odata.bind`]: `/${entitySetName}(${parentRecordId})`
}

Reference: Existing DataverseRecordService.ts for payload structure (but use Xrm.WebApi, not context.webAPI)
```

### Step 2.2: Modify MultiFileUploadService.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MultiFileUploadService.ts`

**Changes:**
1. Remove Phase 2 (record creation) from `handleSyncParallelUpload()`
2. Keep only Phase 1 (file upload)
3. Return `uploadedFiles: SpeFileMetadata[]` instead of `documentRecordIds: string[]`

**Modified method:**
```typescript
private async handleSyncParallelUpload(request, onProgress): Promise<UploadFilesResult> {
    const { files, formData, parentEntityName, parentRecordId } = request;
    const uploadedFiles: SpeFileMetadata[] = [];
    const errors: { fileName: string; error: string }[] = [];

    const driveId = this.extractDriveId(formData, parentEntityName, parentRecordId);
    if (!driveId) {
        // ... error handling
    }

    // PHASE 1 ONLY: Upload files in parallel
    const uploadResults = await Promise.allSettled(
        files.map(file => this.fileUploadService.uploadFile({ file, driveId }))
    );

    // Process results (no record creation)
    for (let i = 0; i < files.length; i++) {
        const result = uploadResults[i];
        if (result.status === 'fulfilled' && result.value.success) {
            uploadedFiles.push(result.value.data);
        } else {
            errors.push({ fileName: files[i].name, error: 'Upload failed' });
        }
    }

    return {
        success: errors.length === 0,
        totalFiles: files.length,
        successCount: uploadedFiles.length,
        failureCount: errors.length,
        documentRecordIds: [],  // No longer used
        uploadedFiles,
        errors
    };
}
```

### Step 2.3: Delete old DataverseRecordService.ts

**Action:** Delete `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DataverseRecordService.ts`

**Rationale:** Replaced by new DocumentRecordService.ts using Xrm.WebApi

**✅ Checkpoint 2:** Services refactored, Xrm.WebApi integrated

---

## PHASE 3: PCF Control Migration (4 hours)

### Step 3.1: Refactor UniversalQuickCreatePCF.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts`

**Major Changes:**

**1. Remove ButtonManagement:**
```typescript
// DELETE these imports and usages
import { ButtonManagement } from './utils/ButtonManagement';
private buttonManager?: ButtonManagement;
this.buttonManager.hideStandardButtons();
```

**2. Add Custom Page parameter handling:**
```typescript
public init(context: ComponentFramework.Context<IInputs>): void {
    // Get parameters from custom page navigation
    this.parentEntityName = context.parameters.parentEntityName.raw || '';
    this.parentRecordId = context.parameters.parentRecordId.raw || '';
    this.containerId = context.parameters.containerId.raw || '';
    this.parentDisplayName = context.parameters.parentDisplayName.raw || '';

    logger.info('UniversalDocumentUploadPCF', 'Custom page parameters received', {
        parentEntityName: this.parentEntityName,
        parentRecordId: this.parentRecordId,
        containerId: this.containerId
    });

    // NO parent context detection needed (already provided)
    // Initialize services
    const apiClient = SdapApiClientFactory.create(apiBaseUrl);
    this.fileUploadService = new FileUploadService(apiClient);
    this.documentRecordService = new DocumentRecordService();  // NEW
    this.multiFileService = new MultiFileUploadService(context, this.fileUploadService);

    // Render React component
    this.renderReactComponent();
}
```

**3. Refactor main workflow:**
```typescript
private async handleUploadAndCreate(): Promise<void> {
    try {
        // Phase 1: Upload files to SPE
        const uploadResult = await this.multiFileService.uploadFiles({
            files: this.selectedFiles,
            formData: this.getFormData(),
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId
        });

        if (!uploadResult.success) {
            this.showErrors(uploadResult.errors);
            return;
        }

        // Phase 2: Create Dataverse records using Xrm.WebApi
        const parentContext: ParentContext = {
            entityName: this.parentEntityName,
            recordId: this.parentRecordId,
            displayName: this.parentDisplayName,
            containerId: this.containerId
        };

        const createResults = await this.documentRecordService.createDocuments(
            uploadResult.uploadedFiles,
            parentContext,
            this.getFormData()
        );

        const successCount = createResults.filter(r => r.success).length;
        const failureCount = createResults.filter(r => !r.success).length;

        logger.info('DocumentUpload', 'Process complete', { successCount, failureCount });

        // Close dialog
        this.closeDialog();

    } catch (error) {
        logger.error('DocumentUpload', 'Upload failed', error);
        this.showError('An error occurred during upload. Please try again.');
    }
}
```

**4. Add dialog close method:**
```typescript
private closeDialog(): void {
    // Close custom page dialog
    if ((window as any).parent?.Xrm?.Navigation) {
        (window as any).parent.Xrm.Navigation.navigateTo(
            { pageType: "entityrecord", entityName: this.parentEntityName, entityId: this.parentRecordId },
            { target: 1 }  // Inline (replaces dialog)
        );
    }
}
```

### Step 3.2: Delete ButtonManagement.ts

**Action:** Delete `src/controls/UniversalQuickCreate/UniversalQuickCreate/utils/ButtonManagement.ts`

**✅ Checkpoint 3:** PCF control migrated to custom page context

---

## PHASE 4: UI Components (Fluent UI v9) (4 hours)

### Step 4.1: Create DocumentUploadForm.tsx

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DocumentUploadForm.tsx`

**Prompt:**
```
Create DocumentUploadForm.tsx using Fluent UI v9 components.

Requirements:
- Import from @fluentui/react-components ONLY (NOT @fluentui/react)
- Use makeStyles() for styling
- Use design tokens (tokens.spacingVerticalL, etc.)
- CSS Flexbox for layout (NO Stack component)
- Match Quick Create visual design

Component structure:
- Header: "Quick Create: Document" + close button
- Section 1: File selection (FileSelectionField component)
- Section 2: Profile (Document Description textarea)
- Progress bar (UploadProgressBar component)
- Footer: Buttons + version display

Props:
- onFilesSelected: (files: File[]) => void
- onUpload: () => void
- onCancel: () => void
- isUploading: boolean
- progress: UploadProgress | null
- errors: string[]

Reference: Universal Dataset Grid components for Fluent UI v9 patterns
```

### Step 4.2: Create FileSelectionField.tsx

**Fluent UI v9 file input:**
```typescript
import { Button, Field, makeStyles, tokens } from '@fluentui/react-components';
import { ArrowUpload24Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM
    },
    fileInputRow: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center'
    },
    fileList: {
        marginTop: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusMedium
    }
});

export function FileSelectionField({ onFilesSelected }) {
    const styles = useStyles();
    const inputRef = useRef<HTMLInputElement>(null);

    const handleClick = () => {
        inputRef.current?.click();
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const files = Array.from(e.target.files || []);
        onFilesSelected(files);
    };

    return (
        <div className={styles.container}>
            <Field label="Upload File">
                <div className={styles.fileInputRow}>
                    <input
                        ref={inputRef}
                        type="file"
                        multiple
                        onChange={handleChange}
                        style={{ display: 'none' }}
                    />
                    <Button
                        appearance="primary"
                        icon={<ArrowUpload24Regular />}
                        onClick={handleClick}
                    >
                        Add File
                    </Button>
                </div>
            </Field>
        </div>
    );
}
```

### Step 4.3: Create UploadProgressBar.tsx

**Fluent UI v9 progress bar:**
```typescript
import { ProgressBar, makeStyles, tokens } from '@fluentui/react-components';

export function UploadProgressBar({ progress }: { progress: UploadProgress }) {
    const styles = makeStyles({
        container: {
            display: 'flex',
            flexDirection: 'column',
            gap: tokens.spacingVerticalS,
            padding: tokens.spacingVerticalM,
            backgroundColor: tokens.colorNeutralBackground2,
            borderRadius: tokens.borderRadiusMedium
        }
    });

    return (
        <div className={styles().container}>
            <div>Progress: {progress.current} of {progress.total} files</div>
            <ProgressBar value={progress.percentage} max={100} />
        </div>
    );
}
```

### Step 4.4: Create ErrorMessageList.tsx

**Fluent UI v9 error messages:**
```typescript
import { MessageBar, makeStyles, tokens } from '@fluentui/react-components';

export function ErrorMessageList({ errors }: { errors: string[] }) {
    if (errors.length === 0) return null;

    return (
        <div>
            {errors.map((error, index) => (
                <MessageBar key={index} intent="error">
                    {error}
                </MessageBar>
            ))}
        </div>
    );
}
```

**✅ Checkpoint 4:** Fluent UI v9 components created

---

## PHASE 5: Custom Page Creation (2 hours)

### Step 5.1: Create Custom Page Definition

**File:** `customizations/CustomPages/sprk_DocumentUploadDialog.json`

**Prompt:**
```
Create custom page definition JSON for Document Upload Dialog.

Requirements:
- Name: sprk_DocumentUploadDialog
- Display Name: Upload Documents
- Description: Multi-file document upload
- Type: Dialog
- Width: 600px
- Height: 700px
- PCF control binding: Spaarke.Controls.UniversalDocumentUploadPCF
- Input parameters: parentEntityName, parentRecordId, containerId, parentDisplayName
- Version: 2.0.0.0

Follow Microsoft custom page schema.
```

**✅ Checkpoint 5:** Custom page defined

---

## PHASE 6: Command Integration (3 hours)

### Step 6.1: Create sprk_subgrid_commands.js

**File:** `customizations/WebResources/sprk_subgrid_commands.js`

```javascript
/**
 * Subgrid Command: Open Document Upload Dialog
 * Version: 2.0.0.0
 * Generic - works with any parent entity
 */

// Entity configuration (matches EntityDocumentConfig.ts)
const ENTITY_CONFIGS = {
    'sprk_matter': {
        containerField: 'sprk_containerid',
        displayField: 'sprk_matternumber'
    },
    'sprk_project': {
        containerField: 'sprk_containerid',
        displayField: 'sprk_projectname'
    },
    'sprk_invoice': {
        containerField: 'sprk_containerid',
        displayField: 'sprk_invoicenumber'
    },
    'account': {
        containerField: 'sprk_containerid',
        displayField: 'name'
    },
    'contact': {
        containerField: 'sprk_containerid',
        displayField: 'fullname'
    }
};

function openDocumentUploadDialog(primaryControl) {
    try {
        const formContext = primaryControl;
        const entityName = formContext.data.entity.getEntityName();
        const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');

        // Get entity-specific config
        const config = ENTITY_CONFIGS[entityName];
        if (!config) {
            Xrm.Navigation.openAlertDialog({
                text: `Document upload not configured for entity: ${entityName}`,
                title: 'Configuration Error'
            });
            return;
        }

        // Get container ID from parent record
        const containerAttr = formContext.getAttribute(config.containerField);
        const containerId = containerAttr ? containerAttr.getValue() : null;

        if (!containerId) {
            Xrm.Navigation.openAlertDialog({
                text: 'No SharePoint container configured for this record. Please contact your administrator.',
                title: 'Missing Container'
            });
            return;
        }

        // Get display name
        const displayAttr = formContext.getAttribute(config.displayField);
        const displayName = displayAttr ? displayAttr.getValue() : `${entityName} ${recordId}`;

        // Open custom page dialog
        const pageInput = {
            pageType: "custom",
            name: "sprk_DocumentUploadDialog",
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
            width: { value: 600, unit: "px" },
            height: { value: 700, unit: "px" }
        };

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success() {
                // Dialog closed successfully - refresh subgrid
                const gridControl = formContext.getControl("subgrid_documents");
                if (gridControl && gridControl.refresh) {
                    gridControl.refresh();
                }
            },
            function error(err) {
                console.error("Failed to open document upload dialog:", err);
                Xrm.Navigation.openAlertDialog({
                    text: "Failed to open document upload dialog. Please try again.",
                    title: "Error"
                });
            }
        );

    } catch (error) {
        console.error("Error in openDocumentUploadDialog:", error);
        Xrm.Navigation.openAlertDialog({
            text: "An error occurred. Please contact your administrator.",
            title: "Error"
        });
    }
}
```

**✅ Checkpoint 6:** Command script created

---

## PHASE 7: Testing (4 hours)

### Test Suite

#### Test 1: Single File Upload
```
1. Open Matter record
2. Click "New Document" button
3. Verify dialog opens (600x700, centered)
4. Verify version in footer: "v2.0.0.0 - Build 2025-01-10"
5. Click "Add File", select 1 PDF (< 10MB)
6. Enter description (optional)
7. Click "Upload & Create"
8. Verify progress shows "1 of 1"
9. Verify dialog closes
10. Verify subgrid refreshes
11. Verify 1 Document record created
12. Verify lookup to Matter is correct
13. Verify file accessible in SPE
```

#### Test 2: Multiple Files (10 files)
```
1. Click "New Document"
2. Select 10 files (< 100MB total)
3. Click "Upload & Create"
4. Verify progress updates: "1 of 10", "2 of 10", etc.
5. Verify all 10 files upload
6. Verify all 10 records created
7. Verify all linked to Matter
```

#### Test 3: Validation - Too Many Files
```
1. Click "New Document"
2. Select 11 files
3. Verify error: "Maximum 10 files allowed. Please select fewer files."
4. Verify dialog does NOT close
```

#### Test 4: Validation - File Too Large
```
1. Select 1 file > 10MB
2. Verify error: "File exceeds 10MB limit: [filename]"
```

#### Test 5: Validation - Total Size
```
1. Select 10 files totaling > 100MB
2. Verify error: "Total size [X]MB exceeds 100MB limit."
```

#### Test 6: Validation - Blocked File Type
```
1. Select test.exe file
2. Verify error: "File type not allowed: test.exe"
```

#### Test 7: Cross-Entity (Repeat Test 1 & 2 for each)
```
- [ ] Project entity
- [ ] Invoice entity
- [ ] Account entity
- [ ] Contact entity
```

#### Test 8: Partial Failure
```
1. Select 5 files (4 valid, 1 invalid .exe)
2. Verify 4 files upload successfully
3. Verify error for .exe file
4. Verify 4 Document records created
```

**✅ Checkpoint 7:** All tests passing

---

## Quick Reference: Key Commands

```bash
# Build control
cd src/controls/UniversalQuickCreate
npm run build

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk

# Verify deployment
# Check browser console for version logs
```

---

## Success Indicators

After complete implementation, you should see:

✅ Dialog opens from any entity's Documents subgrid
✅ Version `v2.0.0.0` displayed in footer
✅ Can upload 10 files without errors
✅ All records created using `Xrm.WebApi` (check network tab)
✅ No Quick Create form dependencies
✅ Fluent UI v9 styles applied correctly
✅ Subgrid refreshes automatically

---

## Common Errors & Fixes

| Error | Fix |
|-------|-----|
| "navigateTo is not a function" | Check Xrm API loaded, verify custom page published |
| "Custom page not found" | Publish custom page, verify name matches |
| "MSAL not initialized" | Check MSAL config, verify client ID |
| "Upload failed: 403" | Verify user has SPE permissions |
| "Create failed: 401" | Check Dataverse permissions |
| "Lookup field not found" | Verify field exists in Document entity |

---

**Implementation Complete!** Follow each phase in order, use checkpoints to verify progress.

**Next:** Deploy using [DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md)
