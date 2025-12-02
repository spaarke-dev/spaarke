# Task 3: File Download Integration

**Estimated Time**: 0.5-1 day
**Status**: Pending
**Prerequisites**: Task 1 complete ✅

---

## AI Coding Prompt

> Implement file download functionality in the Universal Dataset Grid that allows users to download files from SharePoint Embedded via SDAP API. When a user selects a document record and clicks Download, retrieve the file as a blob and trigger a browser download with the correct filename.

---

## Objective

Enable users to download files with:
1. Select document record in grid
2. Click Download button
3. File downloads from SharePoint Embedded
4. Browser saves file with correct name
5. Error handling for missing files

---

## Context & Knowledge

### What You're Building
A simple file download service that:
- Retrieves file blob from SDAP API
- Triggers browser download dialog
- Handles cleanup of blob URLs

### Why This Matters
- **Core Feature**: Users need to access uploaded files
- **Browser Integration**: Proper download UX (Save As dialog)
- **Error Handling**: Graceful handling of missing/deleted files

### Existing Components
- **CommandBar**: Has placeholder Download button
- **SdapApiClient**: Has `downloadFile()` method (from Task 1)
- **Logger**: Use for all operations

### Download Workflow
1. User selects document record
2. User clicks Download button
3. Get container ID and file path from record
4. Download file blob from SDAP API
5. Create temporary blob URL
6. Trigger browser download
7. Clean up blob URL

---

## Implementation Steps

### Step 1: Create File Download Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/FileDownloadService.ts`

**Requirements**:
- Export `FileDownloadService` class
- Accept `SdapApiClient` in constructor
- Implement `downloadFile()` method
- Return `FileOperationResult`
- Use logger throughout
- Clean up blob URLs properly

**Implementation**:
```typescript
import { SdapApiClient } from './SdapApiClient';
import { ServiceResult } from '../types';
import { logger } from '../utils/logger';

export class FileDownloadService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Download file from SharePoint Embedded
     *
     * @param driveId - Graph API Drive ID (from sprk_graphdriveid)
     * @param itemId - Graph API Item ID (from sprk_graphitemid)
     * @param fileName - Display name for downloaded file
     */
    async downloadFile(
        driveId: string,
        itemId: string,
        fileName: string
    ): Promise<ServiceResult> {
        try {
            logger.info('FileDownloadService', `Downloading file: ${fileName}`, {
                driveId,
                itemId
            });

            // Step 1: Download file blob from SDAP API
            const blob = await this.apiClient.downloadFile({ driveId, itemId });

            // Step 2: Create blob URL
            const url = URL.createObjectURL(blob);

            // Step 3: Create temporary download link
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            link.style.display = 'none';

            // Step 4: Trigger download
            document.body.appendChild(link);
            link.click();

            // Step 5: Cleanup (after browser has read the URL)
            setTimeout(() => {
                URL.revokeObjectURL(url);
                document.body.removeChild(link);
            }, 100);

            logger.info('FileDownloadService', `Download complete: ${fileName}`);

            return {
                success: true
            };

        } catch (error) {
            logger.error('FileDownloadService', 'Download failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error'
            };
        }
    }
}
```

---

### Step 2: Update CommandBar Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**Requirements**:
- Add download button click handler
- Get container ID from parent matter record
- Get file path and name from selected record
- Create API client and download service
- Call `downloadFile()`
- Handle errors gracefully

**Download Button Handler**:
```typescript
import { FileDownloadService } from '../services/FileDownloadService';
import { SdapApiClientFactory } from '../services/SdapApiClientFactory';

// Inside CommandBarComponent

const handleDownloadClick = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('CommandBar', 'Download requires exactly one selected record');
            return;
        }

        const record = dataset.records[selectedRecordIds[0]];

        // Get file information from Dataverse record
        // These fields are populated during file upload (see Task 2)
        const driveId = record.getFormattedValue('sprk_graphdriveid');
        const itemId = record.getFormattedValue('sprk_graphitemid');
        const fileName = record.getFormattedValue('sprk_filename');

        if (!driveId || !itemId || !fileName) {
            logger.error('CommandBar', 'Missing required fields for download', {
                hasDriveId: !!driveId,
                hasItemId: !!itemId,
                hasFileName: !!fileName
            });
            return;
        }

        // Create download service and download file
        const apiClient = SdapApiClientFactory.create(
            context,
            'https://spe-bff-api.azurewebsites.net/api'  // TODO: Get from config
        );
        const downloadService = new FileDownloadService(apiClient);

        const result = await downloadService.downloadFile(driveId, itemId, fileName);

        if (result.success) {
            logger.info('CommandBar', 'File downloaded successfully');
            // TODO: Show success notification (optional)
        } else {
            logger.error('CommandBar', `Download failed: ${result.error}`);
            // TODO: Show error notification to user
        }

    } catch (error) {
        logger.error('CommandBar', 'Download handler error', error);
        // TODO: Show error notification to user
    }
}, [context, dataset, selectedRecordIds]);
```

**Update Existing Download Button**:
```typescript
<Button
    icon={<ArrowDownload24Regular />}
    onClick={handleDownloadClick}
    disabled={selectedRecordIds.length !== 1}
>
    Download
</Button>
```

**Import Statements to Add**:
```typescript
import { FileDownloadService } from '../services/FileDownloadService';
```

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `FileDownloadService.ts` compiles without errors
- [ ] `downloadFile()` method retrieves blob from API
- [ ] Browser download dialog appears
- [ ] Downloaded file has correct filename
- [ ] Downloaded file content matches original
- [ ] Download button disabled when no record selected
- [ ] Download button disabled when multiple records selected
- [ ] Blob URL cleaned up after download
- [ ] Errors logged appropriately
- [ ] Missing file handled gracefully (404 error)

---

## Testing Instructions

### Build and Deploy
```bash
npm run build
npx tsc --noEmit
```

### Manual Testing
1. Deploy control to environment
2. Select a document record that has a file uploaded
3. Click Download button
4. Verify browser download dialog appears
5. Verify filename matches document name
6. Open downloaded file and verify content matches original

### Error Testing
1. Select record with invalid file path → Should show error
2. Select record where file was deleted from SharePoint → Should handle 404
3. Disconnect network during download → Should show error

### Cross-Browser Testing
- [ ] Edge (primary)
- [ ] Chrome
- [ ] Firefox (if supported)

---

## Expected Outcomes

After completing this task:

✅ **File download working** from grid
✅ **Browser download dialog** with correct filename
✅ **Blob URL cleanup** prevents memory leaks
✅ **Error handling** for missing files
✅ **Simple, reliable** download UX

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#31-create-download-service) lines 728-849 for complete code.

---

## Troubleshooting

### Issue: Download doesn't start
**Solution**: Check browser console for CORS errors. Verify SDAP API CORS configuration.

### Issue: Downloaded file is empty
**Solution**: Verify blob response from API. Check Content-Type header in API response.

### Issue: Filename incorrect
**Solution**: Ensure `sprk_name` field populated correctly during upload (Task 2).

### Issue: Multiple downloads triggered
**Solution**: Verify `setTimeout` cleanup executes. Check for duplicate event handlers.

---

## Advanced: Download Multiple Files (Optional)

To support multiple file downloads:

```typescript
// Modify handler to loop through selected records
for (const recordId of selectedRecordIds) {
    const record = dataset.records[recordId];
    // Download each file with delay to avoid browser blocking
    await downloadService.downloadFile(...);
    await new Promise(resolve => setTimeout(resolve, 500)); // 500ms delay
}
```

**Note**: Browser may block multiple simultaneous downloads. Consider ZIP download for multiple files (future sprint).

---

## Next Steps

After Task 3 completion:
- **Task 4**: File Delete Integration (with confirmation)
- **Task 5**: File Replace Integration (delete + upload)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#common-workflows) - Download workflow
- [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - API client reference

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 0.5-1 day
**Status**: Ready to Begin (after Task 1)
