# Work Item 6: PCF Integration Layer

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 5 hours
**Prerequisites:** Work Items 1-5 completed
**Status:** Ready to Start

---

## Objective

Integrate all components and services into the main PCF control class. Connect UI components, button management, upload service, and record creation into a cohesive workflow.

---

## Context

This work item brings everything together:
- MultiFileUploadService (Work Item 1)
- Button management (Work Item 2)
- File upload UI (Work Item 4)
- Progress component (Work Item 5)

The PCF control orchestrates the entire workflow:
1. Render file upload UI
2. Enable custom button when files selected
3. Handle button click
4. Show progress UI
5. Create Document records
6. Close form and refresh subgrid

**Result:** Complete end-to-end functionality from file selection to record creation.

---

## Implementation Steps

### Step 1: Update PCF Class Properties

In `UniversalQuickCreatePCF.ts`, add all required properties:

```typescript
export class UniversalQuickCreatePCF implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    // React root
    private theReactRoot: Root | undefined;

    // Services
    private multiFileService: MultiFileUploadService;
    private recordService: DataverseRecordService;

    // UI State
    private selectedFiles: File[] = [];
    private isUploading: boolean = false;
    private uploadProgress: FileUploadStatus[] = [];
    private currentFileIndex: number = 0;
    private overallProgress: number = 0;

    // Button management
    private customSaveButton: HTMLButtonElement | null = null;
    private footerObserver: MutationObserver | null = null;

    // Parent context
    private parentEntityName?: string;
    private parentRecordId?: string;

    // Context
    private context: ComponentFramework.Context<IInputs>;
    private notifyOutputChanged: () => void;
}
```

---

### Step 2: Implement init() Method

Initialize all services and setup:

```typescript
public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary
): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;

    // Initialize services
    const apiBaseUrl = context.parameters.sdapApiBaseUrl.raw || 'https://localhost:7299/api';
    this.multiFileService = new MultiFileUploadService(context, apiBaseUrl);
    this.recordService = new DataverseRecordService(context);

    // Get parent context (for relationships and subgrid refresh)
    this.extractParentContext();

    // Button management
    this.hideStandardButtons();
    this.injectCustomButtonInFooter();
    this.setupFooterWatcher();

    logger.info('UniversalQuickCreatePCF', 'Control initialized', {
        apiBaseUrl,
        parentEntityName: this.parentEntityName,
        parentRecordId: this.parentRecordId
    });
}
```

---

### Step 3: Extract Parent Context

Get parent entity information for relationships:

```typescript
private extractParentContext(): void {
    try {
        // Access parent window (Quick Create is in iframe)
        const parentXrm = (window.parent as any)?.Xrm;
        if (!parentXrm) {
            logger.warn('UniversalQuickCreatePCF', 'Parent window Xrm not accessible');
            return;
        }

        // Get parent form context
        const formContext = parentXrm.Page;
        if (formContext) {
            this.parentEntityName = formContext.data.entity.getEntityName();
            this.parentRecordId = formContext.data.entity.getId().replace(/[{}]/g, '');

            logger.info('UniversalQuickCreatePCF', 'Parent context extracted', {
                parentEntityName: this.parentEntityName,
                parentRecordId: this.parentRecordId
            });
        }
    } catch (error) {
        logger.error('UniversalQuickCreatePCF', 'Failed to extract parent context', error);
    }
}
```

**Why needed:** To create relationship with parent record (e.g., Matter → Document).

---

### Step 4: Implement updateView()

Render appropriate UI based on state:

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
    const allowMultiple = context.parameters.allowMultipleFiles.raw !== false;

    if (this.isUploading) {
        // Show progress UI during upload
        return React.createElement(UploadProgress, {
            files: this.uploadProgress,
            currentFileIndex: this.currentFileIndex,
            totalFiles: this.selectedFiles.length,
            overallProgress: this.overallProgress
        });
    } else {
        // Show file upload UI
        return React.createElement(FileUploadField, {
            allowMultiple,
            onFilesChange: this.handleFilesChange.bind(this),
            disabled: this.isUploading
        });
    }
}
```

---

### Step 5: Handle File Selection

Update button when files selected:

```typescript
private handleFilesChange(files: File[]): void {
    this.selectedFiles = files;

    logger.info('UniversalQuickCreatePCF', 'Files selected', {
        count: files.length,
        totalSize: files.reduce((sum, f) => sum + f.size, 0)
    });

    // Update custom button state
    this.updateButtonState(
        files.length > 0,  // hasFiles
        files.length,       // fileCount
        false               // isUploading
    );
}
```

---

### Step 6: Handle Save and Create Documents

Main workflow orchestration:

```typescript
private async handleSaveAndCreateDocuments(): Promise<void> {
    if (this.selectedFiles.length === 0) {
        logger.warn('UniversalQuickCreatePCF', 'No files selected');
        return;
    }

    logger.info('UniversalQuickCreatePCF', 'Starting upload and record creation', {
        fileCount: this.selectedFiles.length
    });

    try {
        // Initialize progress tracking
        this.initializeProgress(this.selectedFiles);
        this.isUploading = true;
        this.updateButtonState(false, 0, true);  // Disable button
        this.notifyOutputChanged();  // Show progress UI

        // Get form data (other fields user filled)
        const formData = this.getFormData();

        // Upload files and create records
        const result = await this.multiFileService.uploadFiles(
            {
                files: this.selectedFiles,
                formData,
                parentEntityName: this.parentEntityName,
                parentRecordId: this.parentRecordId
            },
            (progress) => {
                this.handleUploadProgress(progress);
            }
        );

        // Handle results
        if (result.success) {
            logger.info('UniversalQuickCreatePCF', 'Upload completed successfully', {
                successCount: result.successCount,
                failureCount: result.failureCount
            });

            // Close form and refresh parent subgrid
            setTimeout(() => {
                this.closeQuickCreateForm();
                this.refreshParentSubgrid();
            }, 1000);  // Brief delay to show completion
        } else {
            logger.error('UniversalQuickCreatePCF', 'Upload failed', result.error);
            // Show error in UI (keep progress component visible)
            this.isUploading = false;
            this.notifyOutputChanged();
        }
    } catch (error) {
        logger.error('UniversalQuickCreatePCF', 'Upload error', error);
        this.isUploading = false;
        this.updateButtonState(true, this.selectedFiles.length, false);
        this.notifyOutputChanged();
    }
}
```

**Key Flow:**
1. Validate files selected
2. Initialize progress tracking
3. Disable button and show progress UI
4. Get form data from Quick Create
5. Call upload service with progress callback
6. Handle success: close form + refresh subgrid
7. Handle failure: show error message

---

### Step 7: Get Form Data

Extract user-entered data from Quick Create form:

```typescript
private getFormData(): Record<string, unknown> {
    const formData: Record<string, unknown> = {};

    try {
        // Access Quick Create form (in iframe)
        const formContext = (window as any)?.Xrm?.Page;
        if (!formContext) {
            logger.warn('UniversalQuickCreatePCF', 'Form context not accessible');
            return formData;
        }

        // Get all form attributes
        const attributes = formContext.data.entity.attributes.get();
        attributes.forEach((attr: any) => {
            const name = attr.getName();
            const value = attr.getValue();

            // Skip our control's bound field
            if (name !== 'sprk_fileuploadmetadata' && value !== null) {
                formData[name] = value;
            }
        });

        logger.info('UniversalQuickCreatePCF', 'Form data extracted', {
            fieldCount: Object.keys(formData).length
        });
    } catch (error) {
        logger.error('UniversalQuickCreatePCF', 'Failed to extract form data', error);
    }

    return formData;
}
```

**Important:** Extract ALL form fields user filled (Description, Owner, etc.) to include in Document records.

---

### Step 8: Progress Tracking

Initialize and update progress state:

```typescript
private initializeProgress(files: File[]): void {
    this.uploadProgress = files.map(file => ({
        fileName: file.name,
        status: 'pending' as const
    }));
    this.currentFileIndex = 0;
    this.overallProgress = 0;
}

private handleUploadProgress(progress: {
    current: number;
    total: number;
    currentFileName: string;
    status: 'uploading' | 'complete' | 'failed';
    error?: string;
}): void {
    // Update current file status
    const index = progress.current - 1;
    if (index >= 0 && index < this.uploadProgress.length) {
        this.uploadProgress[index] = {
            fileName: progress.currentFileName,
            status: progress.status,
            error: progress.error
        };
    }

    // Update overall progress
    this.currentFileIndex = index;
    this.overallProgress = (progress.current / progress.total) * 100;

    // Update button text
    this.updateButtonProgress(progress.current, progress.total);

    // Re-render UI
    this.notifyOutputChanged();
}
```

---

### Step 9: Implement getOutputs()

Return bound field value (not used, but required):

```typescript
public getOutputs(): IOutputs {
    return {
        speMetadata: ''  // Field not actually used
    };
}
```

---

### Step 10: Implement destroy()

Cleanup on control removal:

```typescript
public destroy(): void {
    logger.info('UniversalQuickCreatePCF', 'Control destroying');

    // Remove custom button
    if (this.customSaveButton?.parentElement) {
        this.customSaveButton.parentElement.removeChild(this.customSaveButton);
    }

    // Remove CSS injection
    document.getElementById('spaarke-hide-quickcreate-buttons')?.remove();

    // Disconnect footer observer
    this.footerObserver?.disconnect();

    // Unmount React
    if (this.theReactRoot) {
        this.theReactRoot.unmount();
        this.theReactRoot = undefined;
    }
}
```

---

## Error Handling Strategy

### Network Errors
```typescript
try {
    const result = await this.multiFileService.uploadFiles(...);
} catch (error) {
    if (error instanceof TypeError && error.message.includes('fetch')) {
        logger.error('UniversalQuickCreatePCF', 'Network error', error);
        // Show "Network error - check connection" message
    }
}
```

### Partial Failures
```typescript
if (result.success) {
    const { successCount, failureCount } = result;
    if (failureCount > 0) {
        logger.warn('UniversalQuickCreatePCF', 'Partial success', {
            successCount,
            failureCount
        });
        // Show warning: "3 of 5 files uploaded successfully"
    }
}
```

### Form Close Failure
```typescript
try {
    this.closeQuickCreateForm();
} catch (error) {
    logger.error('UniversalQuickCreatePCF', 'Failed to close form', error);
    // Form stays open, user can manually close
}
```

---

## Testing Checklist

- [ ] Control initializes without errors
- [ ] File upload UI renders
- [ ] Custom button appears in footer
- [ ] Button disabled when no files
- [ ] Button enabled when files selected
- [ ] Button text updates: "Save and Create 3 Documents"
- [ ] Clicking button starts upload
- [ ] Progress UI appears
- [ ] Progress updates in real-time
- [ ] All files upload successfully
- [ ] Document records created in Dataverse
- [ ] Quick Create form closes
- [ ] Parent subgrid refreshes
- [ ] New records visible in subgrid
- [ ] Partial failures handled gracefully
- [ ] Cleanup works (destroy)

---

## Common Issues

### Issue: Button doesn't enable
**Cause:** `handleFilesChange` not firing
**Fix:** Verify callback binding in `updateView`

### Issue: Upload doesn't start
**Cause:** `handleSaveAndCreateDocuments` not bound to button
**Fix:** Check button click handler in Work Item 2

### Issue: Form data empty
**Cause:** Form context not accessible
**Fix:** Verify `window.Xrm.Page` exists in Quick Create

### Issue: Subgrid doesn't refresh
**Cause:** Parent context not extracted
**Fix:** Check `extractParentContext` method

---

## Verification

```bash
# All files exist
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MultiFileUploadService.ts
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx
ls src/controls/UniversalQuickCreate/UniversalQuickCreate/components/UploadProgress.tsx

# Build succeeds
npm run build

# Test end-to-end
# 1. Open Matter record
# 2. Navigate to Documents tab
# 3. Click "+ New Document"
# 4. Select multiple files
# 5. Fill description
# 6. Click "Save and Create 3 Documents"
# 7. Watch progress
# 8. Verify form closes
# 9. Verify subgrid shows new records
```

---

**Status:** Ready for implementation
**Time:** 5 hours
**Dependencies:** Work Items 1-5 must be complete
**Next:** Work Item 7 - Configure Quick Create Form
