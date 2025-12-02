# Task 4: File Delete Integration

**Estimated Time**: 1 day
**Status**: Pending
**Prerequisites**: Task 1 complete ✅

---

## AI Coding Prompt

> Implement file deletion functionality in the Universal Dataset Grid with user confirmation. When a user selects a document and clicks Delete, show a confirmation dialog, then delete the file from both SharePoint Embedded and Dataverse, and refresh the grid. Include comprehensive error handling and cascade delete logic.

---

## Objective

Enable users to delete files with:
1. Select document record in grid
2. Click Delete button
3. Show confirmation dialog
4. User confirms or cancels
5. Delete file from SharePoint Embedded
6. Delete document record from Dataverse
7. Refresh grid to remove record
8. Handle errors gracefully

---

## Context & Knowledge

### What You're Building
Two components:
1. **ConfirmDialog** - Reusable confirmation dialog (Fluent UI)
2. **FileDeleteService** - Orchestrates cascade delete

### Why This Matters
- **Data Integrity**: Must delete from both SharePoint AND Dataverse
- **User Safety**: Confirmation prevents accidental deletions
- **UX**: Immediate grid update after deletion
- **Error Handling**: Partial failures must be handled

### Existing Components
- **CommandBar**: Has placeholder Delete button
- **SdapApiClient**: Has `deleteFile()` and `deleteDocument()` methods (from Task 1)
- **Fluent UI Dialog**: Available from existing imports

### Delete Workflow
1. User selects document record
2. User clicks Delete button
3. Show confirmation dialog with file name
4. User clicks Cancel → Close dialog, no action
5. User clicks Confirm → Execute delete:
   - Delete file from SharePoint Embedded (SDAP API)
   - Delete document record from Dataverse (SDAP API)
   - Refresh grid
   - Notify output changed

---

## Implementation Steps

### Step 1: Create Confirmation Dialog Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ConfirmDialog.tsx`

**Requirements**:
- Export `ConfirmDialog` React component
- Accept props: open, title, message, confirmLabel, cancelLabel, onConfirm, onCancel
- Use Fluent UI Dialog components
- Follow existing component patterns

**Implementation**:
```typescript
import * as React from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button
} from '@fluentui/react-components';

interface ConfirmDialogProps {
    open: boolean;
    title: string;
    message: string;
    confirmLabel?: string;
    cancelLabel?: string;
    onConfirm: () => void;
    onCancel: () => void;
}

export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
    open,
    title,
    message,
    confirmLabel = 'Confirm',
    cancelLabel = 'Cancel',
    onConfirm,
    onCancel
}) => {
    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onCancel()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>{message}</DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onCancel}>
                            {cancelLabel}
                        </Button>
                        <Button appearance="primary" onClick={onConfirm}>
                            {confirmLabel}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
```

---

### Step 2: Create File Delete Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/FileDeleteService.ts`

**Requirements**:
- Export `FileDeleteService` class
- Accept `SdapApiClient` in constructor
- Implement `deleteFile()` method - cascade delete
- Return `FileOperationResult`
- Use logger throughout
- Handle partial failures (file deleted but record fails, or vice versa)

**Implementation**:
```typescript
import { SdapApiClient } from './SdapApiClient';
import { FileOperationResult } from '../types';
import { logger } from '../utils/logger';

export class FileDeleteService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Delete file and document record (cascade delete)
     *
     * Order matters:
     * 1. Delete file from SharePoint Embedded first
     * 2. Then delete Dataverse document record
     *
     * This prevents orphaned Dataverse records if file delete fails.
     *
     * @param documentId - Dataverse document record ID
     * @param containerId - SharePoint Embedded container ID
     * @param filePath - File path in container
     */
    async deleteFile(
        documentId: string,
        containerId: string,
        filePath: string
    ): Promise<FileOperationResult> {
        try {
            logger.info('FileDeleteService', `Deleting file: ${filePath}`);

            // Step 1: Delete file from SharePoint Embedded
            await this.apiClient.deleteFile(containerId, filePath);
            logger.debug('FileDeleteService', 'File deleted from SPE');

            // Step 2: Delete Dataverse document record
            await this.apiClient.deleteDocument(documentId);
            logger.info('FileDeleteService', 'Document record deleted from Dataverse');

            return {
                success: true,
                documentId,
                filePath
            };

        } catch (error) {
            logger.error('FileDeleteService', 'Delete failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error'
            };
        }
    }
}
```

---

### Step 3: Update CommandBar Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**Requirements**:
- Add state for confirmation dialog (open/closed, file to delete)
- Add delete button click handler
- Add delete confirmation handler
- Render `ConfirmDialog` component
- Update Delete button props
- Handle errors gracefully

**State and Handlers**:
```typescript
import { FileDeleteService } from '../services/FileDeleteService';
import { ConfirmDialog } from './ConfirmDialog';

// Inside CommandBarComponent

// State for confirmation dialog
const [deleteDialogOpen, setDeleteDialogOpen] = React.useState(false);
const [fileToDelete, setFileToDelete] = React.useState<{
    documentId: string;
    containerId: string;
    filePath: string;
    fileName: string;
} | null>(null);

// Delete button click handler (shows confirmation)
const handleDeleteClick = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('CommandBar', 'Delete requires exactly one selected record');
            return;
        }

        const record = dataset.records[selectedRecordIds[0]];

        // Get file information
        const documentId = record.getValue('sprk_documentid') as string;
        const filePath = record.getFormattedValue('sprk_filepath');
        const fileName = record.getFormattedValue('sprk_name');

        if (!documentId || !filePath || !fileName) {
            logger.error('CommandBar', 'Missing required fields for delete');
            return;
        }

        // Get container ID from parent matter
        const matterId = record.getValue('sprk_regardingid') as string;
        const matterRecord = await context.webAPI.retrieveRecord(
            'sprk_matter',
            matterId,
            '?$select=sprk_containerid'
        );
        const containerId = matterRecord.sprk_containerid;

        if (!containerId) {
            logger.error('CommandBar', 'Missing container ID');
            return;
        }

        // Store delete info and show confirmation dialog
        setFileToDelete({ documentId, containerId, filePath, fileName });
        setDeleteDialogOpen(true);

    } catch (error) {
        logger.error('CommandBar', 'Delete click handler error', error);
    }
}, [context, dataset, selectedRecordIds]);

// Delete confirmation handler (executes delete)
const handleDeleteConfirm = React.useCallback(async () => {
    if (!fileToDelete) return;

    try {
        // Create delete service and execute delete
        const apiClient = createSdapApiClient(context);
        const deleteService = new FileDeleteService(apiClient);

        const result = await deleteService.deleteFile(
            fileToDelete.documentId,
            fileToDelete.containerId,
            fileToDelete.filePath
        );

        if (result.success) {
            logger.info('CommandBar', 'File deleted successfully');

            // Refresh grid to remove deleted record
            dataset.refresh();
            notifyOutputChanged();

            // TODO: Show success notification (optional)
        } else {
            logger.error('CommandBar', `Delete failed: ${result.error}`);
            // TODO: Show error notification to user
        }

    } catch (error) {
        logger.error('CommandBar', 'Delete confirmation handler error', error);
        // TODO: Show error notification to user
    } finally {
        // Close dialog and clear state
        setDeleteDialogOpen(false);
        setFileToDelete(null);
    }
}, [context, dataset, fileToDelete, notifyOutputChanged]);

// Delete cancellation handler
const handleDeleteCancel = React.useCallback(() => {
    setDeleteDialogOpen(false);
    setFileToDelete(null);
}, []);
```

**Render Confirmation Dialog**:
```typescript
// Add to component JSX (before return statement)
return (
    <>
        {/* Existing CommandBar JSX */}
        <Toolbar>
            {/* ... existing buttons ... */}

            <Button
                icon={<Delete24Regular />}
                onClick={handleDeleteClick}
                disabled={selectedRecordIds.length !== 1}
            >
                Delete
            </Button>

            {/* ... other buttons ... */}
        </Toolbar>

        {/* Confirmation Dialog */}
        <ConfirmDialog
            open={deleteDialogOpen}
            title="Delete File"
            message={`Are you sure you want to delete "${fileToDelete?.fileName}"? This action cannot be undone.`}
            confirmLabel="Delete"
            cancelLabel="Cancel"
            onConfirm={handleDeleteConfirm}
            onCancel={handleDeleteCancel}
        />
    </>
);
```

**Import Statements to Add**:
```typescript
import { FileDeleteService } from '../services/FileDeleteService';
import { ConfirmDialog } from './ConfirmDialog';
import { Delete24Regular } from '@fluentui/react-icons';
```

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `ConfirmDialog.tsx` compiles without errors
- [ ] Dialog renders with correct title and message
- [ ] `FileDeleteService.ts` compiles without errors
- [ ] `deleteFile()` deletes from both SPE and Dataverse
- [ ] Delete button shows confirmation dialog
- [ ] Cancel button closes dialog without deleting
- [ ] Confirm button executes delete
- [ ] Grid refreshes after successful delete
- [ ] `notifyOutputChanged()` called after delete
- [ ] Errors logged appropriately
- [ ] Delete button disabled when no record selected
- [ ] Delete button disabled when multiple records selected
- [ ] Partial failures handled (log error, but don't leave orphans)

---

## Testing Instructions

### Build and Deploy
```bash
npm run build
npx tsc --noEmit
```

### Manual Testing
1. Deploy control to environment
2. Select a document record
3. Click Delete button
4. Verify confirmation dialog appears with correct filename
5. Click Cancel → Verify dialog closes, record still exists
6. Click Delete again
7. Click Confirm → Verify:
   - File deleted from SharePoint
   - Document record deleted from Dataverse
   - Grid refreshes (record removed)
   - No console errors

### Error Testing
1. Delete file that doesn't exist in SharePoint → Should handle gracefully
2. Delete with network disconnected → Should show error
3. Delete with insufficient permissions → Should show error

---

## Expected Outcomes

After completing this task:

✅ **File delete working** with confirmation
✅ **Cascade delete** across SPE and Dataverse
✅ **User safety** with confirmation dialog
✅ **Grid auto-refreshes** after delete
✅ **Reusable ConfirmDialog** component for future use

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#41-create-delete-service) lines 874-1082 for complete code.

**Key sections**:
- ConfirmDialog component: Lines 933-992
- FileDeleteService class: Lines 888-929
- CommandBar integration: Lines 997-1082

---

## Troubleshooting

### Issue: Dialog doesn't close after delete
**Solution**: Ensure `finally` block sets `setDeleteDialogOpen(false)`.

### Issue: Record deleted but file remains in SharePoint
**Solution**: Check delete order. File should be deleted BEFORE Dataverse record.

### Issue: Dialog shows "undefined" filename
**Solution**: Verify `fileToDelete?.fileName` populated correctly in state.

### Issue: Multiple dialogs stack up
**Solution**: Ensure state cleared in cancel handler.

---

## Security Considerations

### Prevent Unauthorized Deletes
The SDAP API should enforce permissions. The PCF control trusts the API to:
- Verify user has delete permissions
- Validate ownership of document
- Block deletes if file is locked/checked out

**Do NOT** implement client-side permission checks - rely on API responses.

---

## Next Steps

After Task 4 completion:
- **Task 5**: File Replace Integration (combines delete + upload logic)
- **Task 6**: Field Mapping & SharePoint Links
- **Task 7**: Testing & Deployment

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#common-workflows) - Delete workflow
- [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - API client reference

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 1 day
**Status**: Ready to Begin (after Task 1)
