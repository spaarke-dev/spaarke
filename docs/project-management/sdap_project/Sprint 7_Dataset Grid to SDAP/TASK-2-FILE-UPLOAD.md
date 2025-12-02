# Task 2: File Upload Integration

**Estimated Time**: 1-2 days
**Status**: Pending
**Prerequisites**: Task 1 complete ✅

---

## AI Coding Prompt

> Implement file upload functionality in the Universal Dataset Grid that allows users to pick a file via browser dialog, upload it to SharePoint Embedded via SDAP API, create a Dataverse document record, and auto-populate SharePoint metadata. Integrate with the existing CommandBar component and refresh the grid after successful upload.

---

## Objective

Enable users to upload files directly from the grid interface with:
1. Browser file picker dialog
2. File upload to SharePoint Embedded
3. Dataverse document record creation
4. SharePoint metadata auto-population
5. Real-time grid refresh
6. Comprehensive error handling

---

## Context & Knowledge

### What You're Building
A complete file upload workflow that orchestrates:
- User interaction (file picker)
- API calls (upload file, create document, update metadata)
- Grid state management (refresh after upload)

### Why This Matters
- **Core Feature**: File upload is the primary user workflow
- **Data Sync**: Must keep Dataverse and SharePoint in sync
- **UX**: Immediate feedback via grid refresh
- **Reliability**: Handle errors gracefully without data loss

### Existing Components
- **CommandBar**: `components/CommandBar.tsx` - Has placeholder Upload button
- **SdapApiClient**: `services/SdapApiClient.ts` - From Task 1
- **Logger**: `utils/logger.ts` - Use for all operations

### Upload Workflow
1. User selects document record (has matter reference)
2. User clicks Upload button
3. Get container ID from parent matter record
4. Show browser file picker
5. User selects file
6. Upload file to SharePoint Embedded (SDAP API)
7. Create Dataverse document record (SDAP API)
8. Update document with SharePoint metadata (SDAP API)
9. Refresh grid
10. Show success/error message

---

## Implementation Steps

### Step 1: Create File Upload Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/FileUploadService.ts`

**Requirements**:
- Export `FileUploadService` class
- Accept `SdapApiClient` in constructor (dependency injection)
- Implement `uploadFile()` method - orchestrates full workflow
- Implement `pickAndUploadFile()` method - shows file picker + uploads
- Return `FileOperationResult` (from `types/index.ts`)
- Use logger throughout
- Handle all error scenarios

**Key Methods**:
```typescript
export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Upload file and create document record
     *
     * Workflow:
     * 1. Upload file to SharePoint Embedded
     * 2. Create Dataverse document record
     * 3. Update document with SharePoint metadata
     */
    async uploadFile(
        matterId: string,
        containerId: string,
        file: File,
        onProgress?: UploadProgressCallback
    ): Promise<FileOperationResult>

    /**
     * Show file picker and upload selected file
     *
     * Creates hidden <input type="file"> element,
     * triggers click, handles selection/cancellation
     */
    async pickAndUploadFile(
        matterId: string,
        containerId: string,
        onProgress?: UploadProgressCallback
    ): Promise<FileOperationResult>
}
```

**Upload Implementation Pattern**:
```typescript
async uploadFile(
    matterId: string,
    containerId: string,
    file: File,
    onProgress?: UploadProgressCallback
): Promise<FileOperationResult> {
    try {
        logger.info('FileUploadService', `Uploading file: ${file.name} (${file.size} bytes)`);

        // Step 1: Upload to SharePoint Embedded
        const uploadRequest: UploadFileRequest = {
            containerId,
            path: file.name,
            file
        };
        const uploadResponse = await this.apiClient.uploadFile(uploadRequest);
        logger.debug('FileUploadService', 'File uploaded to SPE', uploadResponse);

        // Step 2: Create Dataverse document record
        const createDocRequest: CreateDocumentRequest = {
            displayName: file.name,
            matterId,
            filePath: file.name,
            fileSize: file.size,
            mimeType: file.type || 'application/octet-stream'
        };
        const documentResponse = await this.apiClient.createDocument(createDocRequest);
        logger.info('FileUploadService', `Document created: ${documentResponse.id}`);

        // Step 3: Update with SharePoint metadata
        await this.apiClient.updateDocument(documentResponse.id, {
            webUrl: uploadResponse.webUrl,
            sharepointItemId: uploadResponse.sharepointIds.listItemId
        });
        logger.info('FileUploadService', 'Upload complete with metadata');

        return {
            success: true,
            documentId: documentResponse.id,
            filePath: file.name
        };

    } catch (error) {
        logger.error('FileUploadService', 'Upload failed', error);
        return {
            success: false,
            error: error instanceof Error ? error.message : 'Unknown error'
        };
    }
}
```

**File Picker Pattern**:
```typescript
async pickAndUploadFile(
    matterId: string,
    containerId: string,
    onProgress?: UploadProgressCallback
): Promise<FileOperationResult> {
    return new Promise((resolve) => {
        // Create hidden file input
        const input = document.createElement('input');
        input.type = 'file';
        input.style.display = 'none';

        input.onchange = async () => {
            const file = input.files?.[0];
            if (!file) {
                resolve({ success: false, error: 'No file selected' });
                return;
            }

            const result = await this.uploadFile(matterId, containerId, file, onProgress);
            resolve(result);

            // Cleanup
            document.body.removeChild(input);
        };

        input.oncancel = () => {
            resolve({ success: false, error: 'Upload cancelled by user' });
            document.body.removeChild(input);
        };

        // Trigger file picker
        document.body.appendChild(input);
        input.click();
    });
}
```

**Import Statements**:
```typescript
import { SdapApiClient, CreateDocumentRequest, UploadFileRequest } from './SdapApiClient';
import { FileOperationResult, UploadProgressCallback } from '../types';
import { logger } from '../utils/logger';
```

---

### Step 2: Update CommandBar Component

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/CommandBar.tsx`

**Requirements**:
- Add upload button click handler
- Retrieve matter ID from selected record
- Retrieve container ID from matter record via `context.webAPI`
- Create API client and upload service
- Call `pickAndUploadFile()`
- Refresh grid on success
- Call `notifyOutputChanged()` to trigger re-render
- Handle errors gracefully

**Upload Button Handler Pattern**:
```typescript
import { FileUploadService } from '../services/FileUploadService';
import { createSdapApiClient } from '../services/SdapApiClientFactory';

// Inside CommandBarComponent

const handleUploadClick = React.useCallback(async () => {
    try {
        // Validate selection
        if (selectedRecordIds.length !== 1) {
            logger.warn('CommandBar', 'Upload requires exactly one selected record');
            return;
        }

        // Get matter ID from selected record
        const selectedRecord = dataset.records[selectedRecordIds[0]];
        const matterId = selectedRecord?.getValue('sprk_regardingid') as string;

        if (!matterId) {
            logger.error('CommandBar', 'No matter ID found on selected record');
            return;
        }

        // Get container ID from matter record
        const matterRecord = await context.webAPI.retrieveRecord(
            'sprk_matter',
            matterId,
            '?$select=sprk_containerid'
        );
        const containerId = matterRecord.sprk_containerid;

        if (!containerId) {
            logger.error('CommandBar', 'No container ID found for matter');
            // TODO: Show error notification to user
            return;
        }

        // Create API client and upload service
        const apiClient = createSdapApiClient(context);
        const uploadService = new FileUploadService(apiClient);

        // Pick and upload file
        const result = await uploadService.pickAndUploadFile(matterId, containerId);

        if (result.success) {
            logger.info('CommandBar', `File uploaded successfully: ${result.documentId}`);

            // Refresh grid to show new record
            dataset.refresh();
            notifyOutputChanged();

            // TODO: Show success notification to user
        } else {
            logger.error('CommandBar', `Upload failed: ${result.error}`);
            // TODO: Show error notification to user
        }

    } catch (error) {
        logger.error('CommandBar', 'Upload handler error', error);
        // TODO: Show error notification to user
    }
}, [context, dataset, selectedRecordIds, notifyOutputChanged]);
```

**Update Existing Upload Button**:
```typescript
// Find existing Upload button and update props
<Button
    icon={<ArrowUpload24Regular />}
    onClick={handleUploadClick}
    disabled={selectedRecordIds.length !== 1}  // Enable only when 1 record selected
>
    Upload
</Button>
```

**Import Statements to Add**:
```typescript
import { FileUploadService } from '../services/FileUploadService';
import { createSdapApiClient } from '../services/SdapApiClientFactory';
```

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `FileUploadService.ts` compiles without errors
- [ ] `uploadFile()` method orchestrates all 3 API calls
- [ ] `pickAndUploadFile()` shows browser file picker
- [ ] CommandBar upload button calls new handler
- [ ] Upload button disabled when no record selected
- [ ] Upload button disabled when multiple records selected
- [ ] Matter ID retrieved from selected record
- [ ] Container ID retrieved from matter record
- [ ] File uploads to SharePoint Embedded
- [ ] Document record created in Dataverse
- [ ] SharePoint metadata populated (webUrl, sharepointItemId)
- [ ] Grid refreshes after successful upload
- [ ] `notifyOutputChanged()` called after upload
- [ ] Errors logged with logger.error()
- [ ] Upload cancellation handled gracefully

---

## Testing Instructions

### Build and Deploy
```bash
# Build production bundle
npm run build

# Verify no TypeScript errors
npx tsc --noEmit

# Check bundle size (should be <550 KB)
ls -lh out/bundle.js
```

### Manual Testing
1. Deploy control to environment
2. Open model-driven app with grid
3. Select a document record (with matter reference)
4. Click Upload button
5. Verify file picker opens
6. Select a small file (<1 MB)
7. Wait for upload to complete
8. Verify grid refreshes and shows new record
9. Verify new record has:
   - Correct file name
   - File size populated
   - SharePoint URL populated
   - SharePoint item ID populated

### Error Testing
1. Select record with no matter → Should show error
2. Cancel file picker → Should handle gracefully
3. Upload large file (>100 MB) → Should handle (or fail gracefully if >250 MB)
4. Disconnect network during upload → Should show error

---

## Expected Outcomes

After completing this task:

✅ **File upload working end-to-end** from grid
✅ **Browser file picker** integrated
✅ **Dataverse + SharePoint** synchronized
✅ **Grid auto-refreshes** after upload
✅ **Error handling** for all failure scenarios
✅ **Foundation ready** for Tasks 3-5 (download, delete, replace)

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#21-create-upload-service) lines 508-701 for complete code.

**Key sections**:
- FileUploadService class: Lines 525-640
- uploadFile method: Lines 536-593
- pickAndUploadFile method: Lines 598-638
- CommandBar integration: Lines 648-701

---

## Troubleshooting

### Issue: "Cannot read property 'sprk_regardingid'"
**Solution**: Verify field name matches your Dataverse schema. Use `record.getValue('field_name')`.

### Issue: "Container ID is null"
**Solution**: Ensure matter record has `sprk_containerid` populated. Check Sprint 2 docs for container setup.

### Issue: File picker doesn't appear
**Solution**: Check browser console for errors. Verify `document.body.appendChild()` executed.

### Issue: Upload succeeds but grid doesn't refresh
**Solution**: Ensure both `dataset.refresh()` AND `notifyOutputChanged()` are called.

---

## Advanced: Progress Tracking (Optional)

For large files, implement progress tracking:

```typescript
// In FileUploadService.uploadFile()

// Track upload progress (requires XMLHttpRequest instead of fetch)
const xhr = new XMLHttpRequest();

xhr.upload.addEventListener('progress', (event) => {
    if (event.lengthComputable && onProgress) {
        const progress: UploadProgress = {
            loaded: event.loaded,
            total: event.total,
            percentage: Math.round((event.loaded / event.total) * 100)
        };
        onProgress(progress);
    }
});
```

**Note**: This requires refactoring `fetchApi()` in `SdapApiClient` to support XMLHttpRequest. Defer to later sprint if time-constrained.

---

## Next Steps

After Task 2 completion:
- **Task 3**: File Download Integration (similar pattern)
- **Task 4**: File Delete Integration (uses confirmation dialog)
- **Task 5**: File Replace Integration (combines delete + upload)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#common-workflows) - Upload workflow details
- [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - API client reference

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 1-2 days
**Status**: Ready to Begin (after Task 1)
