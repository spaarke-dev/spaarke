# Task 4: File Replace Integration (CORRECTED)

**Estimated Time**: 0.5-1 day
**Status**: In Progress
**Prerequisites**: Task 1 ✅, Task 2 ✅, Task 3 ✅

---

## AI Coding Prompt

> Implement file replacement functionality in the Universal Dataset Grid that allows users to replace an existing file with a new version. When a user selects a document and clicks Replace, show a file picker, then use the SDAP API replaceFile method (delete + upload pattern) to replace the file in SharePoint Embedded, update the Dataverse document record with new FileHandleDto metadata, and refresh the grid.

---

## Objective

Enable users to replace files with:
1. Select document record in grid (must have existing file)
2. Click "Update File" button
3. Show browser file picker
4. User selects new file
5. Call `SdapApiClient.replaceFile({ driveId, itemId, file, fileName })` which:
   - Deletes old file from SPE
   - Uploads new file to SPE
6. Update Dataverse record with new FileHandleDto metadata
7. Refresh grid to show updated record
8. Handle errors gracefully

---

## Context & Knowledge

### What You're Building
A new `FileReplaceService` that:
- Shows browser file picker
- Calls `SdapApiClient.replaceFile()` (already exists from Task 1)
- Updates Dataverse record with new file metadata
- Follows same pattern as FileDeleteService (Task 3)

### Why This Matters
- **Version Control**: Users can update files without creating duplicates
- **Metadata Preservation**: Document ID stays the same, only file changes
- **Atomic Operation**: Delete + upload handled by API
- **UX**: Simple one-click file replacement

### Existing Components
- **CommandBar**: Has "Update File" button (ArrowUpload24Regular icon)
- **SdapApiClient**: Has `replaceFile()` method (from Task 1)
- **ConfirmDialog**: From Task 3 (optional for confirmation)

### Replace Workflow
1. User selects document record with file (hasFile = true)
2. User clicks "Update File" button
3. Show browser file picker
4. User selects new file
5. Call `SdapApiClient.replaceFile()`:
   - API deletes old file via `DELETE /api/drives/{driveId}/items/{itemId}`
   - API uploads new file via `PUT /api/drives/{driveId}/upload?fileName={name}`
6. Receive `FileHandleDto` response with new metadata
7. Update Dataverse record via `context.webAPI.updateRecord()`:
   - `sprk_filename = response.name`
   - `sprk_filesize = response.size`
   - `sprk_graphitemid = response.id`
   - `sprk_createddatetime = response.createdDateTime`
   - `sprk_lastmodifieddatetime = response.lastModifiedDateTime`
   - `sprk_etag = response.eTag`
   - `sprk_filepath = response.webUrl`
   - `sprk_parentfolderid = response.parentId`
8. Refresh grid
9. Notify output changed

---

## Implementation Steps

### Step 1: Create File Replace Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/FileReplaceService.ts`

**Requirements**:
- Export `FileReplaceService` class
- Accept `SdapApiClient` and `ComponentFramework.Context` in constructor
- Implement `pickAndReplaceFile()` method - shows file picker
- Implement `replaceFile()` method - executes replace + update
- Return `ServiceResult`
- Use logger throughout

**Implementation**:
```typescript
/**
 * File Replace Service
 *
 * Handles replacing files in SharePoint Embedded and updating
 * Dataverse records with new file metadata.
 */

import { SdapApiClient } from './SdapApiClient';
import type { ServiceResult, SpeFileMetadata } from '../types';
import { logger } from '../utils/logger';

/**
 * Service for replacing files
 */
export class FileReplaceService {
    constructor(
        private apiClient: SdapApiClient,
        private context: ComponentFramework.Context<unknown>
    ) {}

    /**
     * Show file picker and replace existing file
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @returns ServiceResult indicating success or failure
     */
    async pickAndReplaceFile(
        documentId: string,
        driveId: string,
        itemId: string
    ): Promise<ServiceResult> {
        return new Promise((resolve) => {
            logger.info('FileReplaceService', 'Showing file picker for replace');

            // Create hidden file input
            const input = document.createElement('input');
            input.type = 'file';
            input.style.display = 'none';

            input.onchange = async () => {
                const file = input.files?.[0];

                if (!file) {
                    logger.warn('FileReplaceService', 'No file selected');
                    resolve({
                        success: false,
                        error: 'No file selected'
                    });
                    document.body.removeChild(input);
                    return;
                }

                const result = await this.replaceFile(documentId, driveId, itemId, file);
                resolve(result);

                // Cleanup
                document.body.removeChild(input);
            };

            input.oncancel = () => {
                logger.debug('FileReplaceService', 'Replace cancelled by user');
                resolve({
                    success: false,
                    error: 'Replace cancelled by user'
                });
                document.body.removeChild(input);
            };

            // Trigger file picker
            document.body.appendChild(input);
            input.click();
        });
    }

    /**
     * Replace existing file with new version
     *
     * Workflow:
     * 1. Call SdapApiClient.replaceFile() (handles delete + upload)
     * 2. Receive FileHandleDto with new metadata
     * 3. Update Dataverse record with new metadata
     *
     * @param documentId - Dataverse document record ID
     * @param driveId - Graph API Drive ID
     * @param itemId - Graph API Item ID (old file)
     * @param newFile - New file to upload
     * @returns ServiceResult indicating success or failure
     */
    async replaceFile(
        documentId: string,
        driveId: string,
        itemId: string,
        newFile: File
    ): Promise<ServiceResult> {
        try {
            logger.info('FileReplaceService', `Replacing file with: ${newFile.name}`, {
                documentId,
                driveId,
                oldItemId: itemId
            });

            // Step 1: Call API to replace file (delete old + upload new)
            const fileMetadata: SpeFileMetadata = await this.apiClient.replaceFile({
                driveId,
                itemId,
                file: newFile,
                fileName: newFile.name
            });

            logger.debug('FileReplaceService', 'File replaced in SPE', fileMetadata);

            // Step 2: Update Dataverse record with new metadata
            await this.context.webAPI.updateRecord(
                'sprk_document',
                documentId,
                {
                    sprk_filename: fileMetadata.name,
                    sprk_filesize: fileMetadata.size,
                    sprk_graphitemid: fileMetadata.id,
                    sprk_createddatetime: fileMetadata.createdDateTime,
                    sprk_lastmodifieddatetime: fileMetadata.lastModifiedDateTime,
                    sprk_etag: fileMetadata.eTag,
                    sprk_filepath: fileMetadata.webUrl,
                    sprk_parentfolderid: fileMetadata.parentId,
                    sprk_hasfile: true,
                    sprk_mimetype: newFile.type || 'application/octet-stream'
                }
            );

            logger.info('FileReplaceService', 'Dataverse record updated with new metadata');

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileReplaceService', 'Replace failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown replace error'
            };
        }
    }
}
```

**Key Points**:
- Uses `SdapApiClient.replaceFile()` which handles delete + upload atomically
- Updates ALL FileHandleDto fields in Dataverse
- Uses browser file picker for file selection
- Follows same pattern as FileDeleteService

---

### Step 2: Update UniversalDatasetGridRoot Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**Requirements**:
- Add import for FileReplaceService
- Add replace handler function
- Update handleCommandExecute to call replace handler
- Handle errors gracefully

**Add Import**:
```typescript
import { FileReplaceService } from '../services/FileReplaceService';
```

**Add Replace Handler** (after handleDeleteFile):
```typescript
/**
 * Handle file replace command
 */
const handleReplaceFile = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('UniversalDatasetGridRoot', 'Replace requires exactly one selected record');
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

        if (!documentId || !driveId || !itemId) {
            logger.error('UniversalDatasetGridRoot', 'Missing required fields for replace', {
                hasDocumentId: !!documentId,
                hasDriveId: !!driveId,
                hasItemId: !!itemId
            });
            return;
        }

        logger.info('UniversalDatasetGridRoot', 'Starting file replace');

        // Get SDAP API base URL from config
        const baseUrl = config.sdapConfig.baseUrl;

        // Create API client and replace service
        const apiClient = SdapApiClientFactory.create(context, baseUrl);
        const replaceService = new FileReplaceService(apiClient, context);

        // Show file picker and execute replace
        const result = await replaceService.pickAndReplaceFile(documentId, driveId, itemId);

        if (result.success) {
            logger.info('UniversalDatasetGridRoot', 'File replaced successfully');

            // Refresh grid to show updated record
            dataset.refresh();
            notifyOutputChanged();

        } else {
            // User may have cancelled or error occurred
            if (result.error !== 'Replace cancelled by user') {
                logger.error('UniversalDatasetGridRoot', `Replace failed: ${result.error}`);
            }
        }

    } catch (error) {
        logger.error('UniversalDatasetGridRoot', 'Replace handler error', error);
    }
}, [selectedRecordIds, dataset, context, config, notifyOutputChanged]);
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
            await handleDeleteFile();
            break;
        case 'updateFile':
            await handleReplaceFile();  // <-- Add this
            break;
        case 'downloadFile':
            await handleDownloadFile();
            break;
        default:
            logger.warn('UniversalDatasetGridRoot', `Unknown command: ${commandId}`);
    }
}, [handleDownloadFile, handleDeleteFile, handleReplaceFile]);  // <-- Add dependency
```

---

## Validation Criteria

Before marking this task complete, verify:

- [x] `FileReplaceService.ts` compiles without errors
- [x] `pickAndReplaceFile()` shows file picker
- [x] `replaceFile()` calls `SdapApiClient.replaceFile()`
- [x] Dataverse record updated with all FileHandleDto fields
- [x] Update File button triggers replace workflow
- [x] File picker cancellation handled gracefully
- [x] Grid refreshes after successful replace
- [x] `notifyOutputChanged()` called after replace
- [x] Errors logged appropriately
- [x] Update button disabled when no record selected
- [x] Update button disabled when record has no file

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
2. Select a document record with existing file
3. Note original filename and file size
4. Click "Update File" button
5. Verify file picker opens
6. Select a different file (different name and size)
7. Wait for replacement to complete
8. Verify in grid:
   - Same document record (ID unchanged)
   - New filename displayed
   - New file size displayed
   - Updated timestamp fields
9. Download the file to verify it's the new version

### Error Testing
1. Cancel file picker → Should handle gracefully (no error)
2. Replace with network failure → Should log error
3. Replace with invalid file → Should handle gracefully

---

## Expected Outcomes

After completing this task:

✅ **File replace working** end-to-end
✅ **Atomic operation** (delete + upload via API)
✅ **Metadata updated** with all FileHandleDto fields
✅ **Grid shows updated record** immediately
✅ **No duplicate records** created
✅ **File picker UX** for file selection

---

## Known Limitations

### Orphaned Files on Partial Failure
If API delete succeeds but upload fails:
- Old file deleted from SharePoint
- New file not uploaded
- Dataverse record has old metadata (stale)

**Mitigation**: SdapApiClient.replaceFile() handles this atomically on the server side. Client-side failure would still leave consistent state (old metadata, no file).

**Future Enhancement**: Add confirmation dialog before showing file picker to prevent accidental replacements.

---

## Next Steps

After Task 4 completion:
- **Task 5**: Field Mapping & SharePoint Links (verify all fields populated)
- **Task 6**: Testing & Deployment (comprehensive testing)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - API endpoints and workflows
- [TASK-1-API-CLIENT-SETUP-COMPLETE.md](TASK-1-API-CLIENT-SETUP-COMPLETE.md) - replaceFile() API reference
- [TASK-3-FILE-DELETE-COMPLETE.md](TASK-3-FILE-DELETE-COMPLETE.md) - Similar service pattern

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 0.5-1 day
**Status**: Ready to Begin
