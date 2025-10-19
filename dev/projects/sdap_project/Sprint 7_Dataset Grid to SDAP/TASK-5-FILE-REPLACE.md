# Task 5: File Replace Integration

**Estimated Time**: 0.5-1 day
**Status**: Pending
**Prerequisites**: Task 1 (API Client) + Task 2 (Upload) complete ✅

---

## AI Coding Prompt

> Implement file replacement functionality in the Universal Dataset Grid that allows users to replace an existing file with a new version. When a user selects a document and clicks Replace, show a file picker, delete the old file from SharePoint Embedded, upload the new file, update the Dataverse document record with new metadata, and refresh the grid.

---

## Objective

Enable users to replace files with:
1. Select document record in grid
2. Click Replace button
3. Show browser file picker
4. User selects new file
5. Delete old file from SharePoint Embedded
6. Upload new file to SharePoint Embedded
7. Update Dataverse document record with new metadata
8. Refresh grid to show updated record
9. Handle errors gracefully

---

## Context & Knowledge

### What You're Building
An extension to `FileUploadService` that combines:
- Delete old file (like Task 4)
- Upload new file (like Task 2)
- Update existing record (instead of creating new)

### Why This Matters
- **Version Control**: Users can update files without creating duplicates
- **Metadata Preservation**: Document ID stays the same, only file changes
- **Atomic Operation**: Delete + upload in single transaction
- **UX**: One-click file replacement

### Existing Components
- **CommandBar**: Has placeholder Replace/Update button
- **FileUploadService**: From Task 2 (will extend with new methods)
- **SdapApiClient**: Has all required methods (from Task 1)

### Replace Workflow
1. User selects document record
2. User clicks Replace button
3. Get existing file info (document ID, container ID, old file path)
4. Show browser file picker
5. User selects new file
6. Delete old file from SharePoint Embedded
7. Upload new file to SharePoint Embedded
8. Update existing Dataverse record with new metadata
9. Refresh grid
10. Notify output changed

---

## Implementation Steps

### Step 1: Extend File Upload Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/FileUploadService.ts`

**Requirements**:
- Add `replaceFile()` method to existing class
- Add `pickAndReplaceFile()` method to existing class
- Use same error handling pattern as `uploadFile()`
- Update existing document (don't create new)

**New Methods to Add**:
```typescript
// Add these methods to existing FileUploadService class

/**
 * Replace existing file with new version
 *
 * Workflow:
 * 1. Delete old file from SharePoint Embedded
 * 2. Upload new file to SharePoint Embedded
 * 3. Update existing Dataverse document record with new metadata
 *
 * @param documentId - Existing document record ID (stays same)
 * @param containerId - SharePoint Embedded container ID
 * @param oldFilePath - Current file path (for deletion)
 * @param newFile - New file to upload
 */
async replaceFile(
    documentId: string,
    containerId: string,
    oldFilePath: string,
    newFile: File
): Promise<FileOperationResult> {
    try {
        logger.info('FileUploadService', `Replacing file: ${oldFilePath} with ${newFile.name}`);

        // Step 1: Delete old file from SharePoint Embedded
        await this.apiClient.deleteFile(containerId, oldFilePath);
        logger.debug('FileUploadService', 'Old file deleted from SPE');

        // Step 2: Upload new file
        const uploadRequest: UploadFileRequest = {
            containerId,
            path: newFile.name,
            file: newFile
        };

        const uploadResponse = await this.apiClient.uploadFile(uploadRequest);
        logger.debug('FileUploadService', 'New file uploaded to SPE', uploadResponse);

        // Step 3: Update existing Dataverse document record
        await this.apiClient.updateDocument(documentId, {
            displayName: newFile.name,
            filePath: newFile.name,
            fileSize: newFile.size,
            mimeType: newFile.type || 'application/octet-stream',
            webUrl: uploadResponse.webUrl,
            sharepointItemId: uploadResponse.sharepointIds.listItemId
        });

        logger.info('FileUploadService', 'File replaced successfully');

        return {
            success: true,
            documentId,
            filePath: newFile.name
        };

    } catch (error) {
        logger.error('FileUploadService', 'Replace failed', error);

        return {
            success: false,
            error: error instanceof Error ? error.message : 'Unknown error'
        };
    }
}

/**
 * Show file picker and replace existing file
 *
 * @param documentId - Existing document record ID
 * @param containerId - SharePoint Embedded container ID
 * @param oldFilePath - Current file path
 */
async pickAndReplaceFile(
    documentId: string,
    containerId: string,
    oldFilePath: string
): Promise<FileOperationResult> {
    return new Promise((resolve) => {
        // Create hidden file input
        const input = document.createElement('input');
        input.type = 'file';
        input.style.display = 'none';

        input.onchange = async () => {
            const file = input.files?.[0];

            if (!file) {
                resolve({
                    success: false,
                    error: 'No file selected'
                });
                return;
            }

            const result = await this.replaceFile(documentId, containerId, oldFilePath, file);
            resolve(result);

            // Cleanup
            document.body.removeChild(input);
        };

        input.oncancel = () => {
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
```

---

### Step 2: Update CommandBar Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**Requirements**:
- Add replace button click handler
- Get existing document ID, container ID, and file path
- Create upload service and call `pickAndReplaceFile()`
- Refresh grid on success
- Handle errors gracefully

**Replace Button Handler**:
```typescript
// Inside CommandBarComponent

const handleReplaceClick = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('CommandBar', 'Replace requires exactly one selected record');
            return;
        }

        const record = dataset.records[selectedRecordIds[0]];

        // Get existing document info
        const documentId = record.getValue('sprk_documentid') as string;
        const oldFilePath = record.getFormattedValue('sprk_filepath');

        if (!documentId || !oldFilePath) {
            logger.error('CommandBar', 'Missing document ID or file path');
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

        // Create upload service and replace file
        const apiClient = createSdapApiClient(context);
        const uploadService = new FileUploadService(apiClient);

        const result = await uploadService.pickAndReplaceFile(
            documentId,
            containerId,
            oldFilePath
        );

        if (result.success) {
            logger.info('CommandBar', 'File replaced successfully');

            // Refresh grid to show updated record
            dataset.refresh();
            notifyOutputChanged();

            // TODO: Show success notification (optional)
        } else {
            logger.error('CommandBar', `Replace failed: ${result.error}`);
            // TODO: Show error notification to user
        }

    } catch (error) {
        logger.error('CommandBar', 'Replace handler error', error);
        // TODO: Show error notification to user
    }
}, [context, dataset, selectedRecordIds, notifyOutputChanged]);
```

**Update/Add Replace Button**:
```typescript
import { ArrowSync24Regular } from '@fluentui/react-icons';

// In Toolbar JSX
<Button
    icon={<ArrowSync24Regular />}
    onClick={handleReplaceClick}
    disabled={selectedRecordIds.length !== 1}
>
    Replace
</Button>
```

**Import Statements** (may already exist from Task 2):
```typescript
import { FileUploadService } from '../services/FileUploadService';
import { createSdapApiClient } from '../services/SdapApiClientFactory';
import { ArrowSync24Regular } from '@fluentui/react-icons';
```

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `replaceFile()` method added to `FileUploadService.ts`
- [ ] `pickAndReplaceFile()` method added to `FileUploadService.ts`
- [ ] No TypeScript compilation errors
- [ ] Replace button shows file picker
- [ ] Old file deleted from SharePoint Embedded
- [ ] New file uploaded to SharePoint Embedded
- [ ] Existing Dataverse record updated (not new record created)
- [ ] Updated record has new filename, size, mimeType
- [ ] Updated record has new webUrl and sharepointItemId
- [ ] Grid refreshes to show updated record
- [ ] `notifyOutputChanged()` called after replace
- [ ] Replace button disabled when no record selected
- [ ] Replace button disabled when multiple records selected
- [ ] Errors logged appropriately
- [ ] Cancellation handled gracefully

---

## Testing Instructions

### Build and Deploy
```bash
npm run build
npx tsc --noEmit
```

### Manual Testing
1. Deploy control to environment
2. Select a document record with existing file
3. Note original filename, size, and SharePoint URL
4. Click Replace button
5. Verify file picker opens
6. Select a different file (different name and size)
7. Wait for replacement to complete
8. Verify in grid:
   - Same document record (ID unchanged)
   - New filename displayed
   - New file size displayed
   - New SharePoint URL (different from original)
9. Click SharePoint URL to verify new file accessible

### Error Testing
1. Cancel file picker → Should handle gracefully
2. Select invalid file type (if validation added) → Should show error
3. Network failure during upload → Should show error, old file may be deleted (orphan)

### Edge Cases
1. Replace with file having same name → Should work (overwrites)
2. Replace with much larger file → Should work (update size metadata)
3. Replace with file having different extension → Should work (update mimeType)

---

## Expected Outcomes

After completing this task:

✅ **File replace working** end-to-end
✅ **Atomic operation** (delete old + upload new)
✅ **Metadata updated** correctly
✅ **Grid shows updated record** immediately
✅ **No duplicate records** created

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#51-extend-upload-service-for-replace) lines 1110-1264 for complete code.

**Key sections**:
- replaceFile method: Lines 1121-1170
- pickAndReplaceFile method: Lines 1175-1213
- CommandBar integration: Lines 1219-1264

---

## Troubleshooting

### Issue: New record created instead of updating existing
**Solution**: Verify `updateDocument()` called with correct document ID (not `createDocument()`).

### Issue: Old file not deleted, both files exist
**Solution**: Ensure `deleteFile()` completes before `uploadFile()`. Check await statements.

### Issue: Metadata not updated after replace
**Solution**: Verify `updateDocument()` includes all fields: displayName, filePath, fileSize, mimeType, webUrl, sharepointItemId.

### Issue: Grid shows old filename after replace
**Solution**: Ensure `dataset.refresh()` AND `notifyOutputChanged()` both called after successful replace.

---

## Advanced: Confirmation Dialog (Optional)

To add confirmation before replace (prevent accidental overwrites):

```typescript
// Add confirmation dialog similar to Task 4
const [replaceDialogOpen, setReplaceDialogOpen] = React.useState(false);

// Show confirmation before file picker
const handleReplaceClick = async () => {
    // ... get document info ...
    setReplaceDialogOpen(true); // Show confirmation first
};

const handleReplaceConfirm = async () => {
    setReplaceDialogOpen(false);
    // Then show file picker and execute replace
};
```

**Note**: This adds extra step but improves user safety. Defer if time-constrained.

---

## Known Limitations

### Orphaned Files on Partial Failure
If delete succeeds but upload fails:
- Old file deleted from SharePoint
- New file not uploaded
- Dataverse record points to non-existent file

**Mitigation**: Log error clearly. User must retry upload (Task 2) to restore file.

**Future Enhancement**: Implement transaction rollback or backup old file before delete.

---

## Next Steps

After Task 5 completion:
- **Task 6**: Field Mapping & SharePoint Links (make URLs clickable)
- **Task 7**: Testing & Deployment (comprehensive testing, bundle size validation)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#common-workflows) - Replace workflow details
- [TASK-2-FILE-UPLOAD.md](TASK-2-FILE-UPLOAD.md) - Upload service reference
- [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - API client reference

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 0.5-1 day
**Status**: Ready to Begin (after Tasks 1 & 2)
