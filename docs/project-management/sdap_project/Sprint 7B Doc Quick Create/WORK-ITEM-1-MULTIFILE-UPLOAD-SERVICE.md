# Work Item 1: Create MultiFileUploadService

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 4 hours
**Prerequisites:** None
**Status:** Ready to Start
**Reference:** TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md (lines 163-755)

---

## Objective

Create the `MultiFileUploadService.ts` that orchestrates multi-file uploads with adaptive strategy selection.

---

## Context

This service is the brain of the multi-file upload system. It:
- **Analyzes** files to choose optimal upload strategy (fast vs safe)
- **Orchestrates** uploads via existing FileUploadService
- **Creates** Dataverse Document records (one per file)
- **Reports** progress to UI for real-time feedback
- **Handles** partial failures gracefully

**Key Innovation:** Automatically switches between sync-parallel (fast path) and long-running (safe path) based on file characteristics.

---

## File Location

**Create:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MultiFileUploadService.ts`

**Alongside:**
- FileUploadService.ts (existing - reuse)
- SdapApiClient.ts (existing - reuse)
- auth/MsalAuthProvider.ts (existing)

---

## Key Interfaces

Define these TypeScript interfaces:

```typescript
export interface UploadFilesRequest {
    files: File[];
    driveId: string;
    sharedMetadata: Record<string, any>;  // Description, etc.
}

export interface UploadFilesResult {
    success: boolean;
    totalFiles: number;
    successCount: number;
    failureCount: number;
    documentRecords: DocumentRecord[];  // Created records
    errors: UploadError[];              // Failed files
}

export interface UploadProgress {
    current: number;        // 1-based
    total: number;
    fileName: string;
    status: 'uploading' | 'creating-record' | 'complete' | 'failed';
}

export type UploadStrategy = 'sync-parallel' | 'long-running';
```

---

## Core Methods to Implement

### 1. Main Entry Point

```typescript
async uploadFiles(
    request: UploadFilesRequest,
    onProgress?: (progress: UploadProgress) => void
): Promise<UploadFilesResult>
```

**Logic:**
1. Log upload start (file count, total size)
2. Determine strategy → sync-parallel or long-running
3. Execute chosen strategy
4. Return result with success/failure details

---

### 2. Strategy Decision

```typescript
determineUploadStrategy(files: File[]): UploadStrategy
```

**Decision Logic:**
```typescript
const MAX_FILES_SYNC = 3;
const MAX_FILE_SIZE_SYNC = 10 * 1024 * 1024;   // 10MB
const MAX_TOTAL_SIZE_SYNC = 20 * 1024 * 1024;  // 20MB

if (files.length <= 3 && largestFile < 10MB && totalSize < 20MB) {
    return 'sync-parallel';  // Fast path
} else {
    return 'long-running';   // Safe path
}
```

**Test Cases:**
- 3 files × 2MB = sync-parallel ✓
- 4 files × 2MB = long-running (count > 3)
- 3 files × 12MB = long-running (size > 10MB)
- 3 files × 8MB (25MB total) = long-running (total > 20MB)

---

### 3. Sync-Parallel Upload (Fast Path)

```typescript
private async handleSyncParallelUpload(
    request: UploadFilesRequest,
    onProgress?: (progress: UploadProgress) => void
): Promise<UploadFilesResult>
```

**Algorithm:**
```
PHASE 1: Upload all files in parallel
    for each file:
        Promise.all(uploadFile) → get SPE metadata
    Wait for all uploads

PHASE 2: Create records sequentially
    for each uploaded file:
        createDocumentRecord(metadata) → get record ID
        Fire onProgress callback
```

**Why sequential record creation?**
- Avoid Dataverse throttling
- Predictable behavior
- Fast enough (~500ms per record)

**Performance:** 3 files × 2MB = ~3-4 seconds total

---

### 4. Long-Running Upload (Safe Path)

```typescript
private async handleLongRunningUpload(
    request: UploadFilesRequest,
    onProgress?: (progress: UploadProgress) => void
): Promise<UploadFilesResult>
```

**Algorithm:**
```
Calculate batch size (2-5 based on avg file size)
Split files into batches

for each batch:
    Upload files in batch (parallel within batch)
    Create records for batch (sequential)
    Fire progress callbacks
```

**Batch Size Logic:**
```typescript
calculateBatchSize(files: File[]): number {
    avgSize = total size / file count

    if (avgSize < 1MB) return 5;      // Small files: more parallel
    if (avgSize < 5MB) return 3;      // Medium files: balanced
    return 2;                         // Large files: safe
}
```

**Performance:** 5 files × 15MB = ~17-25 seconds total

---

### 5. Create Document Record

```typescript
private async createDocumentRecord(
    file: File,
    speMetadata: SpeFileMetadata,
    driveId: string,
    sharedMetadata: Record<string, any>
): Promise<string>  // Returns record ID
```

**Data Mapping:**
```typescript
const recordData = {
    ...sharedMetadata,                           // Description, etc.
    sprk_documenttitle: file.name,               // From file
    sprk_sharepointurl: speMetadata.sharePointUrl,
    sprk_driveitemid: speMetadata.driveItemId,
    sprk_filename: speMetadata.fileName,
    sprk_filesize: speMetadata.fileSize,
    sprk_createddate: speMetadata.createdDateTime,
    sprk_modifieddate: speMetadata.lastModifiedDateTime,
    sprk_containerid: driveId
};

const result = await this.context.webAPI.createRecord('sprk_document', recordData);
return result.id;
```

---

## Error Handling Pattern

**Partial Success Support:**

```typescript
const errors: UploadError[] = [];
const documentRecords: DocumentRecord[] = [];

for (const file of files) {
    try {
        // Upload and create record
        documentRecords.push(record);
    } catch (error) {
        // Collect error but CONTINUE
        errors.push({ fileName: file.name, error: error.message });
    }
}

return {
    success: errors.length === 0,
    successCount: documentRecords.length,
    failureCount: errors.length,
    documentRecords,
    errors
};
```

**Why continue on error?**
- Better UX: 4 of 5 succeeded > none succeeded
- User sees which files failed
- Can retry failed files only

---

## Code Patterns to Follow

### Logging

```typescript
logger.info('MultiFileUploadService', 'Starting upload', {
    fileCount: files.length,
    strategy: 'sync-parallel'
});

logger.error('MultiFileUploadService', 'Upload failed', { fileName, error });
```

### Progress Callbacks

```typescript
onProgress?.({
    current: 2,
    total: 5,
    fileName: 'invoice.pdf',
    status: 'uploading'
});
```

### Utility Functions

```typescript
// Array chunking
private chunkArray<T>(array: T[], size: number): T[][] {
    const chunks: T[][] = [];
    for (let i = 0; i < array.length; i += size) {
        chunks.push(array.slice(i, i + size));
    }
    return chunks;
}
```

---

## Testing Checklist

After implementation:

- [ ] **Compile:** `npm run build` succeeds
- [ ] **Strategy:** 3 small files → sync-parallel
- [ ] **Strategy:** 5 large files → long-running
- [ ] **Batch size:** Calculates 2, 3, or 5 correctly
- [ ] **Parallel upload:** Promise.all used in sync-parallel
- [ ] **Sequential records:** No parallel createRecord calls
- [ ] **Partial success:** 4 of 5 succeed → returns 4 records + 1 error
- [ ] **Progress:** Callbacks fire for each file
- [ ] **Logging:** Info logs for start/complete, error logs for failures

---

## Implementation Tips

1. **Reuse existing code:** Copy structure from TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md lines 163-755
2. **Don't reinvent:** Use existing FileUploadService for single file uploads
3. **Type safety:** Import types from `../types` (SpeFileMetadata, ServiceResult)
4. **Context:** Passed in constructor, used for `context.webAPI.createRecord()`
5. **MSAL:** Automatically handled by SdapApiClient (no extra work needed)

---

## Verification

**Before moving to next work item:**

```bash
# Should compile
npm run build

# Check file exists
ls -la services/MultiFileUploadService.ts

# Check exports
grep "export class MultiFileUploadService" services/MultiFileUploadService.ts
grep "export interface UploadFilesRequest" services/MultiFileUploadService.ts
```

---

## Reference Implementation

**Full code available at:** TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md lines 163-755

**Use as reference, but adapt for:**
- ✅ Correct imports for this project structure
- ✅ Correct field names (sprk_* prefix)
- ✅ Correct entity name (sprk_document)
- ✅ Logging via logger utility (not console.log)

---

**Status:** Ready for implementation
**Time:** 4 hours
**Next:** Work Item 2 - Button Management
