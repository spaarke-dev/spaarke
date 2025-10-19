# Work Item 4: Implement Multi-File Upload Logic

**Estimated Time:** 1 hour
**Prerequisites:** Work Item 3 complete (FileUploadService verified)
**Status:** Ready to Start

---

## Objective

Implement logic in FileUploadPCF.ts to handle multiple file uploads simultaneously.

---

## Context

Users need to upload multiple files at once from Quick Create forms. Each file:
- Uploads to SharePoint Embedded
- Generates separate SPE metadata
- All metadata stored in single `sprk_fileuploadmetadata` field as JSON array

**Important:** Multiple files → Single Document record with array of metadata (NOT multiple Document records - that's handled by Quick Create form submission).

---

## Implementation Overview

```
User selects 3 files:
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ contract.pdf│  │ invoice.pdf │  │ receipt.pdf │
└─────────────┘  └─────────────┘  └─────────────┘
       ↓                ↓                ↓
   Upload to       Upload to        Upload to
      SPE              SPE              SPE
       ↓                ↓                ↓
  ┌──────────┐    ┌──────────┐    ┌──────────┐
  │ Metadata │    │ Metadata │    │ Metadata │
  │ driveId: │    │ driveId: │    │ driveId: │
  │   ABC    │    │   DEF    │    │   GHI    │
  └──────────┘    └──────────┘    └──────────┘
       ↓                ↓                ↓
       └────────────────┴────────────────┘
                       ↓
         speMetadata field = JSON array:
         [
             { driveItemId: "ABC", ... },
             { driveItemId: "DEF", ... },
             { driveItemId: "GHI", ... }
         ]
```

---

## Current Implementation (Single File)

In FileUploadPCF.ts, the `handleFilesSelected()` method currently handles multiple files:

```typescript
private handleFilesSelected = async (files: File[]): Promise<void> => {
    const uploadedMetadata: SpeFileMetadata[] = [];

    // Upload each file sequentially
    for (const file of files) {
        const result = await this.fileUploadService.uploadFile({
            file,
            containerId: this.state.containerId,
            fileName: file.name
        });

        if (result.success && result.data) {
            uploadedMetadata.push(result.data);
        } else {
            throw new Error(result.error || 'File upload failed');
        }
    }

    // Store all metadata
    this.setState({ speMetadata: uploadedMetadata });
};
```

**Status:** ✅ Already implemented! Multi-file support is working.

---

## Verification Steps

### Step 1: Review Current Implementation

Open FileUploadPCF.ts and verify `handleFilesSelected()` method:

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts`

**Check for:**
1. ✅ Method accepts `files: File[]` (array)
2. ✅ Loops through files with `for...of`
3. ✅ Uploads each file sequentially
4. ✅ Collects metadata in array
5. ✅ Stores array in state

---

### Step 2: Verify Sequential vs Parallel Upload

**Current Approach:** Sequential (one at a time)

```typescript
for (const file of files) {
    await this.fileUploadService.uploadFile(...);  // Wait for each
}
```

**Benefits:**
- Simpler error handling
- Predictable progress updates
- Easier to debug
- Avoids rate limiting

**Alternative:** Parallel (all at once)

```typescript
const promises = files.map(file =>
    this.fileUploadService.uploadFile(...)
);
const results = await Promise.all(promises);
```

**Trade-offs:**
- Faster (if API allows)
- More complex error handling
- Risk of rate limiting
- Harder to show progress

**Decision:** Keep sequential for now (simpler, more reliable).

---

### Step 3: Enhance Error Handling

**Current Issue:** If one file fails, entire upload stops.

**Improvement:** Continue uploading remaining files, collect errors.

**Updated Implementation:**

```typescript
private handleFilesSelected = async (files: File[]): Promise<void> => {
    if (!this.fileUploadService) {
        this.setState({ error: 'File upload service not initialized' });
        return;
    }

    if (!this.state.containerId) {
        this.setState({ error: 'Container ID not found. Cannot upload files.' });
        return;
    }

    logger.info('FileUploadPCF', 'Files selected for upload', {
        fileCount: files.length,
        containerId: this.state.containerId
    });

    this.setState({ isLoading: true, error: null });

    const uploadedMetadata: SpeFileMetadata[] = [];
    const errors: string[] = [];

    try {
        // Upload each file
        for (let i = 0; i < files.length; i++) {
            const file = files[i];

            logger.info('FileUploadPCF', `Uploading file ${i + 1}/${files.length}`, {
                fileName: file.name
            });

            try {
                const result = await this.fileUploadService.uploadFile({
                    file,
                    containerId: this.state.containerId,
                    fileName: file.name
                });

                if (result.success && result.data) {
                    uploadedMetadata.push(result.data);
                    logger.info('FileUploadPCF', 'File uploaded successfully', {
                        fileName: file.name,
                        driveItemId: result.data.driveItemId
                    });
                } else {
                    // File upload failed, collect error but continue
                    const errorMsg = `${file.name}: ${result.error || 'Upload failed'}`;
                    errors.push(errorMsg);
                    logger.error('FileUploadPCF', 'File upload failed', { fileName: file.name, error: result.error });
                }
            } catch (error) {
                // Unexpected error, collect and continue
                const errorMsg = `${file.name}: ${error instanceof Error ? error.message : 'Unknown error'}`;
                errors.push(errorMsg);
                logger.error('FileUploadPCF', 'File upload error', { fileName: file.name, error });
            }
        }

        // Update state with uploaded metadata
        if (uploadedMetadata.length > 0) {
            this.setState({
                speMetadata: uploadedMetadata,
                isLoading: false,
                error: errors.length > 0 ? `Some files failed: ${errors.join(', ')}` : null
            });

            // Notify framework that outputs changed
            this.context.parameters.speMetadata.notifyOutputChanged?.();

            logger.info('FileUploadPCF', 'Upload complete', {
                successCount: uploadedMetadata.length,
                failCount: errors.length
            });
        } else {
            // All files failed
            this.setState({
                isLoading: false,
                error: `All files failed to upload: ${errors.join(', ')}`
            });
        }
    } catch (error) {
        logger.error('FileUploadPCF', 'Upload process failed', error);
        this.setState({
            isLoading: false,
            error: error instanceof Error ? error.message : 'File upload failed'
        });
    }
};
```

**Changes:**
1. ✅ Wrapped each file upload in try-catch
2. ✅ Collect errors but continue with remaining files
3. ✅ Show success count + error count
4. ✅ Progress logging (1/3, 2/3, 3/3)
5. ✅ Partial success supported (some files succeed, some fail)

---

### Step 4: Add Progress Tracking (Optional)

For better UX, track upload progress:

**Add to ControlState:**

```typescript
interface ControlState {
    speMetadata: SpeFileMetadata[];
    containerId: string | null;
    isLoading: boolean;
    error: string | null;
    uploadProgress?: {  // ✨ NEW
        current: number;
        total: number;
    };
}
```

**Update handleFilesSelected():**

```typescript
for (let i = 0; i < files.length; i++) {
    const file = files[i];

    // Update progress
    this.setState({
        uploadProgress: { current: i + 1, total: files.length }
    });

    // Upload file...
}

// Clear progress when done
this.setState({ uploadProgress: undefined });
```

**Display in UI (FileUploadField.tsx):**

```tsx
{uploadProgress && (
    <div>Uploading {uploadProgress.current} of {uploadProgress.total}...</div>
)}
```

---

### Step 5: Verify Metadata Array Format

After upload, verify `speMetadata` array is correct:

**Expected Format:**

```json
[
    {
        "driveItemId": "01ABCDEF...",
        "fileName": "contract.pdf",
        "fileSize": 2048576,
        "sharePointUrl": "https://...",
        "webUrl": "https://...",
        "createdDateTime": "2025-10-07T12:00:00Z",
        "lastModifiedDateTime": "2025-10-07T12:00:00Z"
    },
    {
        "driveItemId": "01GHIJKL...",
        "fileName": "invoice.pdf",
        "fileSize": 1024000,
        "sharePointUrl": "https://...",
        "webUrl": "https://...",
        "createdDateTime": "2025-10-07T12:01:00Z",
        "lastModifiedDateTime": "2025-10-07T12:01:00Z"
    }
]
```

**Verify:**
- ✅ Each object has required fields (driveItemId, fileName, fileSize)
- ✅ Array serializes to valid JSON string
- ✅ Total size < 10,000 characters (field limit)

---

### Step 6: Handle Field Size Limit

The `sprk_fileuploadmetadata` field has a 10,000 character limit.

**Calculate average metadata size:**

```
Single file metadata: ~250 characters
10,000 / 250 = ~40 files max
```

**Add size check:**

```typescript
const metadataJson = JSON.stringify(uploadedMetadata);

if (metadataJson.length > 10000) {
    logger.error('FileUploadPCF', 'Metadata exceeds field size limit', {
        actualSize: metadataJson.length,
        maxSize: 10000,
        fileCount: uploadedMetadata.length
    });

    this.setState({
        error: `Too many files uploaded (${uploadedMetadata.length}). Metadata exceeds 10,000 character limit.`
    });
    return;
}
```

**Recommendation:** Limit UI to max 20 files to be safe.

---

## Testing Multi-File Upload

### Test Case 1: Upload 3 Files Successfully

**Steps:**
1. Open Quick Create form for Document
2. Select 3 files (contract.pdf, invoice.pdf, receipt.pdf)
3. Click Save

**Expected:**
- All 3 files upload to SPE
- speMetadata field contains JSON array with 3 objects
- Form closes successfully

**Browser Console:**
```
[FileUploadPCF] Files selected for upload: { fileCount: 3, containerId: "..." }
[FileUploadPCF] Uploading file 1/3: { fileName: "contract.pdf" }
[FileUploadService] File uploaded successfully: { driveItemId: "01ABC..." }
[FileUploadPCF] Uploading file 2/3: { fileName: "invoice.pdf" }
[FileUploadService] File uploaded successfully: { driveItemId: "01DEF..." }
[FileUploadPCF] Uploading file 3/3: { fileName: "receipt.pdf" }
[FileUploadService] File uploaded successfully: { driveItemId: "01GHI..." }
[FileUploadPCF] Upload complete: { successCount: 3, failCount: 0 }
```

---

### Test Case 2: Upload 5 Files, 2 Fail

**Steps:**
1. Select 5 files
2. Simulate 2 failures (disconnect network during upload)
3. Verify partial success

**Expected:**
- 3 files succeed, 2 fail
- Error message shows which files failed
- speMetadata field contains 3 successful uploads

**Browser Console:**
```
[FileUploadPCF] Files selected for upload: { fileCount: 5 }
[FileUploadPCF] Uploading file 1/5: { fileName: "file1.pdf" }
[FileUploadService] File uploaded successfully
[FileUploadPCF] Uploading file 2/5: { fileName: "file2.pdf" }
[FileUploadService] File upload failed: Network error
[FileUploadPCF] File upload failed: { fileName: "file2.pdf", error: "Network error" }
[FileUploadPCF] Uploading file 3/5: { fileName: "file3.pdf" }
[FileUploadService] File uploaded successfully
[FileUploadPCF] Uploading file 4/5: { fileName: "file4.pdf" }
[FileUploadService] File upload failed: Network error
[FileUploadPCF] File upload failed: { fileName: "file4.pdf", error: "Network error" }
[FileUploadPCF] Uploading file 5/5: { fileName: "file5.pdf" }
[FileUploadService] File uploaded successfully
[FileUploadPCF] Upload complete: { successCount: 3, failCount: 2 }
```

---

### Test Case 3: Upload 30 Files (Field Size Limit)

**Steps:**
1. Select 30 small files (each ~500 bytes)
2. Upload all
3. Verify metadata JSON size

**Expected:**
- All files upload successfully
- Metadata JSON size < 10,000 characters
- Warning if approaching limit

**Check metadata size:**
```typescript
const metadataJson = JSON.stringify(uploadedMetadata);
console.log('Metadata size:', metadataJson.length, 'characters');
// Expected: ~7,500 characters (30 files × 250 chars)
```

---

## Implementation Checklist

After implementing multi-file upload logic:

- ✅ handleFilesSelected() accepts File[] array
- ✅ Loops through files sequentially
- ✅ Uploads each file via FileUploadService
- ✅ Collects metadata in array
- ✅ Handles partial failures (some succeed, some fail)
- ✅ Shows progress (1/3, 2/3, 3/3)
- ✅ Logs upload activity
- ✅ Checks metadata JSON size < 10,000 characters
- ✅ Updates state with uploadedMetadata array
- ✅ Notifies framework via notifyOutputChanged()
- ✅ Returns JSON array from getOutputs()

---

## Code Changes Summary

### FileUploadPCF.ts Changes:

1. **Add progress tracking to ControlState:**
   ```typescript
   uploadProgress?: { current: number; total: number };
   ```

2. **Enhance handleFilesSelected() error handling:**
   - Wrap each upload in try-catch
   - Collect errors array
   - Continue with remaining files on failure
   - Show partial success message

3. **Add metadata size check:**
   - Check JSON.stringify(metadata).length < 10,000
   - Show error if exceeded

4. **Add progress logging:**
   - Log "Uploading 1/3", "Uploading 2/3", etc.

---

## Next Steps

After completing this work item:

1. ✅ Update FileUploadPCF.ts with enhanced error handling
2. ✅ Add progress tracking (optional)
3. ✅ Verify metadata array format
4. ⏳ Move to Work Item 5: Create FileUploadField.tsx UI

---

**Status:** Ready for implementation
**Estimated Time:** 1 hour
**Next:** Work Item 5 - Create File Upload UI
