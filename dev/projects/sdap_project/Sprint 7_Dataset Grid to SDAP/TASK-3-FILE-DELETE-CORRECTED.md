# Task 3: File Delete Integration (CORRECTED)

**Estimated Time**: 1 day
**Status**: In Progress
**Prerequisites**: Task 1 ✅, Task 2 ✅

---

## AI Coding Prompt

> Implement file deletion functionality in the Universal Dataset Grid with user confirmation. When a user selects a document and clicks Delete, show a confirmation dialog, then delete the file from SharePoint Embedded via SDAP API, update the Dataverse record to mark hasFile=false, and refresh the grid. Include comprehensive error handling.

---

## Objective

Enable users to delete files with:
1. Select document record in grid
2. Click Delete button (Remove File)
3. Show confirmation dialog
4. User confirms or cancels
5. Delete file from SharePoint Embedded (SDAP API)
6. Update Dataverse record: `sprk_hasfile = false`, clear SPE metadata fields
7. Refresh grid to update record
8. Handle errors gracefully

**IMPORTANT**: We do NOT delete the Dataverse document record itself, only the file attachment.

---

## Context & Knowledge

### What You're Building
Two components:
1. **ConfirmDialog** - Reusable confirmation dialog (Fluent UI v9)
2. **FileDeleteService** - Orchestrates file delete + Dataverse update

### Why This Matters
- **Data Integrity**: File deleted from SPE, record marked as "no file"
- **User Safety**: Confirmation prevents accidental deletions
- **UX**: Immediate grid update after deletion
- **Record Preservation**: Document metadata remains in Dataverse

### Existing Components
- **CommandBar**: Has "Remove File" button
- **SdapApiClient**: Has `deleteFile({ driveId, itemId })` method (from Task 1)
- **Fluent UI Dialog**: Available from @fluentui/react-components
- **PCF Context**: Has `webAPI.updateRecord()` for Dataverse updates

### Delete Workflow (CORRECTED)
1. User selects document record with file (hasFile = true)
2. User clicks "Remove File" button
3. Show confirmation dialog with file name
4. User clicks Cancel → Close dialog, no action
5. User clicks Confirm → Execute delete:
   - Delete file from SharePoint Embedded via `SdapApiClient.deleteFile({ driveId, itemId })`
   - Update Dataverse record via `context.webAPI.updateRecord()`:
     - `sprk_hasfile = false`
     - `sprk_graphitemid = null`
     - `sprk_filesize = null`
     - `sprk_createddatetime = null`
     - `sprk_lastmodifieddatetime = null`
     - `sprk_etag = null`
     - `sprk_filepath = null`
   - Refresh grid
   - Notify output changed

---

## Implementation Steps

### Step 1: Create Confirmation Dialog Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ConfirmDialog.tsx`

**Requirements**:
- Export `ConfirmDialog` React component
- Accept props: open, title, message, confirmLabel, cancelLabel, onConfirm, onCancel
- Use Fluent UI v9 Dialog components
- Follow existing component patterns

**Implementation**:
```typescript
/**
 * Reusable Confirmation Dialog Component
 * Uses Fluent UI v9 Dialog components
 */

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
    /** Dialog open state */
    open: boolean;

    /** Dialog title */
    title: string;

    /** Dialog message/content */
    message: string;

    /** Confirm button label */
    confirmLabel?: string;

    /** Cancel button label */
    cancelLabel?: string;

    /** Confirm button callback */
    onConfirm: () => void;

    /** Cancel button callback */
    onCancel: () => void;
}

/**
 * Confirmation dialog with confirm/cancel actions
 */
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
- Accept `SdapApiClient` and `ComponentFramework.Context` in constructor
- Implement `deleteFile()` method
- Return `ServiceResult`
- Use logger throughout
- Handle partial failures gracefully

**Implementation**:
```typescript
/**
 * File Delete Service
 *
 * Handles deleting files from SharePoint Embedded and updating
 * Dataverse records to mark files as removed.
 */

import { SdapApiClient } from './SdapApiClient';
import type { ServiceResult } from '../types';
import { logger } from '../utils/logger';

/**
 * Service for deleting files and updating records
 */
export class FileDeleteService {
    constructor(
        private apiClient: SdapApiClient,
        private context: ComponentFramework.Context<any>
    ) {}

    /**
     * Delete file from SPE and update Dataverse record
     *
     * Order matters:
     * 1. Delete file from SharePoint Embedded first
     * 2. Then update Dataverse record to mark hasFile = false
     *
     * This prevents orphaned files if Dataverse update fails.
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @param fileName - File name for logging
     * @returns ServiceResult indicating success or failure
     */
    async deleteFile(
        documentId: string,
        driveId: string,
        itemId: string,
        fileName: string
    ): Promise<ServiceResult> {
        try {
            logger.info('FileDeleteService', `Deleting file: ${fileName}`, {
                documentId,
                driveId,
                itemId
            });

            // Step 1: Delete file from SharePoint Embedded
            await this.apiClient.deleteFile({ driveId, itemId });
            logger.debug('FileDeleteService', 'File deleted from SPE');

            // Step 2: Update Dataverse record to clear file metadata
            await this.context.webAPI.updateRecord(
                'sprk_document',
                documentId,
                {
                    sprk_hasfile: false,
                    sprk_graphitemid: null,
                    sprk_filesize: null,
                    sprk_createddatetime: null,
                    sprk_lastmodifieddatetime: null,
                    sprk_etag: null,
                    sprk_filepath: null
                }
            );
            logger.info('FileDeleteService', 'Dataverse record updated (hasFile=false)');

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileDeleteService', 'Delete failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown delete error'
            };
        }
    }
}
```

**Key Changes from Original**:
- Uses `driveId` and `itemId` instead of `containerId` and `filePath`
- Uses `context.webAPI.updateRecord()` instead of non-existent `deleteDocument()`
- Clears file metadata fields instead of deleting the record

---

### Step 3: Update UniversalDatasetGridRoot Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**Requirements**:
- Add state for confirmation dialog (open/closed, file to delete)
- Add delete button handler (shows confirmation)
- Add delete confirmation handler (executes delete)
- Render `ConfirmDialog` component
- Handle errors gracefully

**Add Imports**:
```typescript
import { FileDeleteService } from '../services/FileDeleteService';
import { ConfirmDialog } from './ConfirmDialog';
```

**Add State** (inside component):
```typescript
// State for delete confirmation dialog
const [deleteDialogOpen, setDeleteDialogOpen] = React.useState(false);
const [fileToDelete, setFileToDelete] = React.useState<{
    documentId: string;
    driveId: string;
    itemId: string;
    fileName: string;
} | null>(null);
```

**Add Delete Handler** (before handleCommandExecute):
```typescript
/**
 * Handle file delete command
 */
const handleDeleteFile = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('UniversalDatasetGridRoot', 'Delete requires exactly one selected record');
            return;
        }

        const record = dataset.records[selectedRecordIds[0]];

        if (!record) {
            logger.warn('UniversalDatasetGridRoot', 'Record not found');
            return;
        }

        // Get file metadata from Dataverse record
        const documentId = record.getRecordId();
        const driveId = record.getFormattedValue(config.fieldMappings.graphDriveId);
        const itemId = record.getFormattedValue(config.fieldMappings.graphItemId);
        const fileName = record.getFormattedValue(config.fieldMappings.fileName);

        if (!documentId || !driveId || !itemId || !fileName) {
            logger.error('UniversalDatasetGridRoot', 'Missing required fields for delete', {
                hasDocumentId: !!documentId,
                hasDriveId: !!driveId,
                hasItemId: !!itemId,
                hasFileName: !!fileName
            });
            return;
        }

        // Store delete info and show confirmation dialog
        setFileToDelete({ documentId, driveId, itemId, fileName });
        setDeleteDialogOpen(true);

    } catch (error) {
        logger.error('UniversalDatasetGridRoot', 'Delete click handler error', error);
    }
}, [selectedRecordIds, dataset, config]);

/**
 * Handle delete confirmation
 */
const handleDeleteConfirm = React.useCallback(async () => {
    if (!fileToDelete) return;

    try {
        logger.info('UniversalDatasetGridRoot', `Confirming delete: ${fileToDelete.fileName}`);

        // Get SDAP API base URL from config
        const baseUrl = config.sdapConfig.baseUrl;

        // Create API client and delete service
        const apiClient = SdapApiClientFactory.create(context, baseUrl);
        const deleteService = new FileDeleteService(apiClient, context);

        // Execute delete
        const result = await deleteService.deleteFile(
            fileToDelete.documentId,
            fileToDelete.driveId,
            fileToDelete.itemId,
            fileToDelete.fileName
        );

        if (result.success) {
            logger.info('UniversalDatasetGridRoot', 'File deleted successfully');

            // Refresh grid to show updated record (hasFile = false)
            dataset.refresh();
            notifyOutputChanged();

        } else {
            logger.error('UniversalDatasetGridRoot', `Delete failed: ${result.error}`);
        }

    } catch (error) {
        logger.error('UniversalDatasetGridRoot', 'Delete confirmation handler error', error);
    } finally {
        // Close dialog and clear state
        setDeleteDialogOpen(false);
        setFileToDelete(null);
    }
}, [fileToDelete, context, config, dataset, notifyOutputChanged]);

/**
 * Handle delete cancellation
 */
const handleDeleteCancel = React.useCallback(() => {
    logger.debug('UniversalDatasetGridRoot', 'Delete cancelled');
    setDeleteDialogOpen(false);
    setFileToDelete(null);
}, []);
```

**Update handleCommandExecute**:
```typescript
const handleCommandExecute = React.useCallback(async (commandId: string) => {
    logger.info('UniversalDatasetGridRoot', `Command executed: ${commandId}`);

    switch (commandId) {
        case 'addFile':
            logger.info('UniversalDatasetGridRoot', 'Add File - will implement in future task');
            break;
        case 'removeFile':
            await handleDeleteFile();  // <-- Add this
            break;
        case 'updateFile':
            logger.info('UniversalDatasetGridRoot', 'Update File - will implement in future task');
            break;
        case 'downloadFile':
            await handleDownloadFile();
            break;
        default:
            logger.warn('UniversalDatasetGridRoot', `Unknown command: ${commandId}`);
    }
}, [handleDownloadFile, handleDeleteFile]);  // <-- Add handleDeleteFile dependency
```

**Add ConfirmDialog to JSX** (inside return statement, after DatasetGrid):
```typescript
return (
    <div style={{ ... }}>
        <CommandBar ... />
        <DatasetGrid ... />

        {/* Delete Confirmation Dialog */}
        <ConfirmDialog
            open={deleteDialogOpen}
            title="Delete File"
            message={`Are you sure you want to delete "${fileToDelete?.fileName}"? This action cannot be undone.`}
            confirmLabel="Delete"
            cancelLabel="Cancel"
            onConfirm={handleDeleteConfirm}
            onCancel={handleDeleteCancel}
        />
    </div>
);
```

---

## Validation Criteria

Before marking this task complete, verify:

- [x] `ConfirmDialog.tsx` compiles without errors
- [x] Dialog renders with correct title and message
- [x] `FileDeleteService.ts` compiles without errors
- [x] `deleteFile()` deletes from SPE and updates Dataverse
- [x] Remove File button shows confirmation dialog
- [x] Cancel button closes dialog without deleting
- [x] Confirm button executes delete
- [x] Grid refreshes after successful delete
- [x] Record shows hasFile=false after delete
- [x] `notifyOutputChanged()` called after delete
- [x] Errors logged appropriately
- [x] Delete button disabled when no record selected
- [x] Delete button disabled when record has no file

---

## Testing Instructions

### Build and Deploy
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid
npm run build
npx tsc --noEmit
```

### Manual Testing
1. Deploy control to environment
2. Select a document record with file (hasFile = true)
3. Click "Remove File" button
4. Verify confirmation dialog appears with correct filename
5. Click Cancel → Verify dialog closes, record unchanged
6. Click "Remove File" again
7. Click Confirm → Verify:
   - File deleted from SharePoint
   - Record updated (hasFile = false)
   - Grid refreshes (record still visible but marked as no file)
   - No console errors

### Error Testing
1. Delete file that doesn't exist in SPE → Should handle gracefully
2. Delete with network disconnected → Should show error
3. Delete with insufficient permissions → Should show error

---

## Expected Outcomes

After completing this task:

✅ **File delete working** with confirmation
✅ **File removed from SPE**, record marked as no file
✅ **User safety** with confirmation dialog
✅ **Grid auto-refreshes** after delete
✅ **Reusable ConfirmDialog** component for future use
✅ **Record preserved** in Dataverse with metadata cleared

---

## Next Steps

After Task 3 completion:
- **Task 4**: File Replace Integration
- **Task 5**: Field Mapping & SharePoint Links
- **Task 6**: Testing & Deployment

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - API endpoints and field mappings
- [TASK-1-API-CLIENT-SETUP-COMPLETE.md](TASK-1-API-CLIENT-SETUP-COMPLETE.md) - API client reference
- [TASK-2-FILE-DOWNLOAD-COMPLETE.md](TASK-2-FILE-DOWNLOAD-COMPLETE.md) - Similar handler pattern

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 1 day
**Status**: Ready to Begin
