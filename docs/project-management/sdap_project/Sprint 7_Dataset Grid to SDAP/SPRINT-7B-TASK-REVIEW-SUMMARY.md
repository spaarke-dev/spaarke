# Sprint 7B Task Review Summary

**Date:** October 6, 2025
**Purpose:** Document review and updates to Sprint 7B tasks for MSAL compliance and multi-file adaptive upload
**Status:** ✅ Task 1 Updated, Task 2-4 Pending

---

## Overview

Sprint 7B task documents have been reviewed in light of:
1. **Sprint 8 MSAL authentication patterns** (must use MSAL from day one)
2. **Multi-file adaptive upload strategy** (user requirement)
3. **Current codebase state** (Sprint 7A/8 complete)

---

## Task 1: Quick Create Setup & MSAL Integration

**Status:** ✅ UPDATED

### Changes Made

#### 1. Added MSAL Requirements Section

```markdown
### 🔴 CRITICAL: MSAL Authentication Required

**Sprint 7B MUST use MSAL from day one** (lesson learned from Sprint 8):

✅ **Use these patterns:**
- Initialize MSAL in `init()` method: `MsalAuthProvider.getInstance().initialize()`
- Create SDAP API client via factory: `SdapApiClientFactory.create(baseUrl)`
- Handle race conditions (user clicks Save before MSAL ready)
- Use same Azure AD app registration as Sprint 7A/8

❌ **DO NOT:**
- Use PCF context tokens (`context.userSettings.accessToken`)
- Create separate authentication logic
- Bypass `SdapApiClientFactory`
- Hardcode tokens
```

#### 2. Updated Project Structure

Added MSAL services:
```
services/
  ├── auth/
  │   └── MsalAuthProvider.ts      # 🔴 SHARED from Sprint 8
  ├── SdapApiClient.ts             # 🔴 SHARED from Sprint 7A (MSAL-enabled)
  ├── SdapApiClientFactory.ts      # 🔴 SHARED from Sprint 7A
  ├── FileUploadService.ts         # Single file upload
  ├── MultiFileUploadService.ts    # 🆕 Multi-file adaptive upload
  └── DefaultValueMapper.ts
```

#### 3. Updated PCF Wrapper Class

**Added:**
- `authProvider: MsalAuthProvider` property
- `initializeMsalAsync()` method in `init()`
- `showError()` method for MSAL failure display
- MSAL cache clearing in `destroy()`

**Example:**
```typescript
export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private authProvider: MsalAuthProvider; // 🔴 MSAL auth provider

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // 🔴 CRITICAL: Initialize MSAL authentication (Phase 1)
        this.initializeMsalAsync(this.container);

        // ... rest of init ...
    }

    private initializeMsalAsync(container: HTMLDivElement): void {
        (async () => {
            try {
                this.authProvider = MsalAuthProvider.getInstance();
                await this.authProvider.initialize();
                // ... logging ...
            } catch (error) {
                this.showError(container, 'Authentication initialization failed...');
            }
        })();
    }
}
```

#### 4. Updated Dependencies

Added MSAL package:
```bash
npm install @azure/msal-browser@4.24.1
```

#### 5. Updated Implementation Steps

**Step 5:** Copy/Link Shared Services (30 min)
- Copy MsalAuthProvider.ts from Sprint 8
- Copy SdapApiClient.ts + SdapApiClientFactory.ts from Sprint 7A
- Copy types (SpeFileMetadata, ServiceResult, etc.)

#### 6. Updated Testing Checklist

Added MSAL verification:
```
- [ ] MSAL initializes in background (check console logs)
- [ ] User authentication detected (console: "User authenticated: true")

Expected Console Output:
[UniversalQuickCreate] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[UniversalQuickCreate] MSAL authentication initialized successfully ✅
[UniversalQuickCreate] User authenticated: true
```

#### 7. Updated Success Metrics

- Bundle size updated: <400 KB → acceptable up to 500 KB (due to MSAL)
- Added MSAL authentication success criteria
- Added user authentication logging verification

---

## Task 2: File Upload & SPE Integration (Multi-File Adaptive Upload)

**Status:** 🔶 PARTIALLY UPDATED (Title + Success Criteria only)

### Changes Made

#### 1. Updated Title and Time Estimate

```markdown
# Sprint 7B - Task 2: File Upload with SPE Integration (Multi-File Adaptive Upload)

**Estimated Time**: 2-3 days (increased from 1-2 days due to multi-file complexity)
```

#### 2. Updated Task Overview

```markdown
Implement **multi-file upload with adaptive strategy** to SharePoint Embedded via SDAP BFF API.
Users can upload 1-10 files in a single operation, creating multiple Dataverse Document records
with shared metadata.

**Key Features:**
- Single file upload (baseline)
- Multi-file upload (1-10 files)
- Adaptive strategy selection (sync-parallel vs long-running)
- Progress indicators for both strategies
- Shared metadata across all documents
- One Document record per file
```

#### 3. Expanded Success Criteria

**Added Multi-File Upload criteria:**
```markdown
### Multi-File Upload (Enhanced)
- ✅ User can select 1-10 files in a single Quick Create
- ✅ Adaptive strategy selection based on file size/count
- ✅ Sync-parallel upload: 1-3 files, <10MB each, <20MB total (fast, 3-4 seconds)
- ✅ Long-running batched upload: >3 files OR >10MB OR >20MB total (batched with progress)
- ✅ Progress indicators for both strategies
- ✅ One Document record created per file
- ✅ All documents share the same metadata
- ✅ Partial success handling (if 4 of 5 files upload, show summary)
```

### Required Updates (Not Yet Done)

#### 1. Add Multi-File Upload Service Implementation

**File:** `services/MultiFileUploadService.ts`

**Key Methods:**
```typescript
class MultiFileUploadService {
    // Determine strategy based on file count and size
    determineUploadStrategy(files: File[]): 'sync-parallel' | 'long-running'

    // Handle sync-parallel upload (1-3 small files)
    async handleSyncParallelUpload(
        files: File[],
        driveId: string,
        metadata: SharedMetadata
    ): Promise<ServiceResult<DocumentRecord[]>>

    // Handle long-running batched upload (large/many files)
    async handleLongRunningUpload(
        files: File[],
        driveId: string,
        metadata: SharedMetadata,
        onProgress?: (current: number, total: number) => void
    ): Promise<ServiceResult<DocumentRecord[]>>

    // Calculate adaptive batch size
    private calculateBatchSize(files: File[]): number
}
```

**Strategy Logic:**
```typescript
function determineUploadStrategy(files: File[]): 'sync-parallel' | 'long-running' {
    const totalFiles = files.length;
    const largestFile = Math.max(...files.map(f => f.size));
    const totalSize = files.reduce((sum, f) => sum + f.size, 0);

    // Sync Parallel: 1-3 files AND all <10MB AND total <20MB
    if (
        totalFiles <= 3 &&
        largestFile < 10 * 1024 * 1024 &&      // 10MB
        totalSize < 20 * 1024 * 1024            // 20MB
    ) {
        return 'sync-parallel';
    }

    // Long-running for everything else
    return 'long-running';
}
```

**Batch Size Calculation:**
```typescript
function calculateBatchSize(files: File[]): number {
    const avgSize = files.reduce((sum, f) => sum + f.size, 0) / files.length;

    if (avgSize < 1_000_000) return 5;       // <1MB: batch of 5
    if (avgSize < 5_000_000) return 3;       // 1-5MB: batch of 3
    return 2;                                 // >5MB: batch of 2
}
```

#### 2. Update FilePickerField Component

**File:** `components/FilePickerField.tsx`

**Add multi-file support:**
```typescript
export const FilePickerField: React.FC<FilePickerFieldProps> = ({
    value,      // Change to File[] instead of File
    onChange,   // Change to (files: File[]) => void
    required = false,
    multiple = true,  // 🆕 Allow multiple files
    maxFiles = 10     // 🆕 Max 10 files
}) => {
    const handleFileChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const files = Array.from(e.target.files || []);

        if (files.length > maxFiles) {
            // Show error: "Maximum 10 files allowed"
            return;
        }

        onChange(files);
    }, [onChange, maxFiles]);

    return (
        <Field label="Select File(s)" required={required}>
            <Input
                type="file"
                multiple={multiple}  // 🆕 HTML5 multiple attribute
                onChange={handleFileChange}
            />
            {value && value.length > 0 && (
                <div className={styles.fileInfo}>
                    {value.map((file, i) => (
                        <div key={i}>
                            {file.name} ({formatFileSize(file.size)})
                        </div>
                    ))}
                    <div>Total: {value.length} files ({formatFileSize(totalSize)})</div>
                </div>
            )}
        </Field>
    );
};
```

#### 3. Add Progress Component for Long-Running Uploads

**File:** `components/UploadProgress.tsx`

**Purpose:** Show detailed progress for long-running uploads

```typescript
interface UploadProgressProps {
    files: File[];
    currentFile: number;
    totalFiles: number;
    uploadedFiles: string[];  // File names that completed
    failedFiles: string[];    // File names that failed
}

export const UploadProgress: React.FC<UploadProgressProps> = ({
    files,
    currentFile,
    totalFiles,
    uploadedFiles,
    failedFiles
}) => {
    const progressPercent = (currentFile / totalFiles) * 100;

    return (
        <div>
            <h3>Uploading {totalFiles} files...</h3>

            <ProgressBar value={progressPercent / 100} />
            <div>{Math.round(progressPercent)}% ({currentFile} of {totalFiles})</div>

            <ul>
                {files.map((file, i) => (
                    <li key={i}>
                        {uploadedFiles.includes(file.name) && '✓ '}
                        {failedFiles.includes(file.name) && '✗ '}
                        {!uploadedFiles.includes(file.name) && !failedFiles.includes(file.name) &&
                         i === currentFile && '↻ '}
                        {file.name}
                    </li>
                ))}
            </ul>

            <p>Please keep this window open</p>
        </div>
    );
};
```

#### 4. Update handleSave Implementation

**File:** `UniversalQuickCreatePCF.ts`

**Support both single and multi-file:**
```typescript
private async handleSave(
    formData: Record<string, any>,
    files?: File[],  // 🔄 Changed from single File to File[]
    onProgress?: (current: number, total: number) => void
): Promise<void> {
    try {
        const containerId = formData.sprk_containerid || this.parentRecordData?.sprk_containerid;

        if (!containerId) {
            throw new Error('Container ID not found');
        }

        let documentRecords: DocumentRecord[] = [];

        if (files && files.length > 0) {
            // Determine strategy
            const strategy = this.multiFileUploadService.determineUploadStrategy(files);

            logger.info('UniversalQuickCreate', `Using ${strategy} upload strategy`, {
                fileCount: files.length,
                totalSize: files.reduce((sum, f) => sum + f.size, 0)
            });

            if (strategy === 'sync-parallel') {
                // Fast path: 1-3 small files
                documentRecords = await this.multiFileUploadService.handleSyncParallelUpload(
                    files,
                    containerId,
                    formData
                );
            } else {
                // Safe path: Large/many files
                documentRecords = await this.multiFileUploadService.handleLongRunningUpload(
                    files,
                    containerId,
                    formData,
                    onProgress
                );
            }
        }

        logger.info('UniversalQuickCreate', 'All files uploaded', {
            successCount: documentRecords.length,
            totalFiles: files?.length || 0
        });

    } catch (error) {
        logger.error('UniversalQuickCreate', 'Save failed', error);
        throw error;
    }
}
```

#### 5. Update Testing Scenarios

**Add test scenarios:**

**Scenario 1: Single File (Baseline)**
```
Input: 1 file (5MB PDF)
Expected: Sync-parallel, ~2 seconds
```

**Scenario 2: Few Small Files (Sync-Parallel)**
```
Input: 3 files (2MB each)
Expected: Sync-parallel, ~3-4 seconds
```

**Scenario 3: Threshold Test (Boundary)**
```
Input: 3 files (10MB each, exactly 30MB total)
Expected: Long-running (exceeds 20MB total threshold)
```

**Scenario 4: Many Files (Long-Running)**
```
Input: 5 files (3MB each, 15MB total)
Expected: Long-running (exceeds 3 file threshold)
```

**Scenario 5: Large Files (Long-Running)**
```
Input: 2 files (20MB each, 40MB total)
Expected: Long-running (exceeds 10MB per file threshold)
```

**Scenario 6: Maximum Files (Stress Test)**
```
Input: 10 files (5MB each, 50MB total)
Expected: Long-running with batching, ~35 seconds
```

#### 6. MSAL Integration

**Critical:** Ensure MSAL token acquisition is used for ALL file uploads.

**Pattern:**
```typescript
// In MultiFileUploadService.ts
import { SdapApiClientFactory } from './SdapApiClientFactory';

class MultiFileUploadService {
    private apiClient: SdapApiClient;

    constructor() {
        // 🔴 CRITICAL: Use SdapApiClientFactory (MSAL-enabled)
        this.apiClient = SdapApiClientFactory.create(baseUrl);
    }

    async uploadSingleFile(file: File, driveId: string): Promise<SpeFileMetadata> {
        // apiClient.uploadFile() automatically uses MSAL token via SdapApiClientFactory
        return await this.apiClient.uploadFile({
            file,
            driveId,
            fileName: file.name
        });
    }
}
```

**Token Caching Benefits:**
- First file upload: ~420ms (token acquisition)
- Subsequent files: ~5ms (cached token)
- **Result:** Multi-file upload is FAST with MSAL caching!

---

## Task 3: Default Value Mappings & Configuration

**Status:** ⏳ PENDING REVIEW

### Expected Changes

Minimal changes expected. This task focuses on configuration, which is orthogonal to MSAL and multi-file upload.

**Possible updates:**
- Ensure default value mappings work for multi-file scenarios (same metadata applied to all documents)
- No MSAL-specific changes required

---

## Task 4: Testing, Deployment & Sprint 7A Validation

**Status:** ⏳ PENDING REVIEW

### Required Updates

#### 1. Add Multi-File Test Scenarios

**Test Plan:**
```markdown
### Multi-File Upload Testing

**Test 1: Sync-Parallel (Small Files)**
- Upload: 3 files × 2MB each
- Expected: Sync-parallel strategy, ~3 seconds total
- Verify: All 3 Document records created with same metadata

**Test 2: Long-Running (Large Files)**
- Upload: 5 files × 15MB each
- Expected: Long-running strategy, ~25 seconds total
- Verify: Progress indicator shows file-by-file status
- Verify: All 5 Document records created

**Test 3: Partial Failure**
- Upload: 5 files (with 1 invalid file type)
- Expected: 4 succeed, 1 fails
- Verify: Summary shows "4 of 5 files uploaded successfully"
- Verify: 4 Document records created

**Test 4: MSAL Token Caching**
- Upload: 10 files × 5MB each
- Verify console logs:
  - File 1: "Token acquired in 420ms"
  - File 2-10: "Token retrieved from cache (5ms)"
```

#### 2. Add MSAL Verification Tests

**Test Plan:**
```markdown
### MSAL Authentication Testing

**Test 1: Initial Authentication**
- Launch Quick Create
- Verify console: "MSAL authentication initialized successfully ✅"
- Verify console: "User authenticated: true"

**Test 2: Token Acquisition During Upload**
- Upload single file
- Verify console: "Token acquired in ~420ms" (first call)
- Upload another file
- Verify console: "Token retrieved from cache (5ms)" (cached)

**Test 3: 401 Auto-Retry**
- Simulate expired token scenario (manual test)
- Verify: Automatic retry with fresh token
- Verify: Upload succeeds on second attempt
```

#### 3. Add Sprint 7A Validation Reminder

**Important:**
```markdown
## 🔴 REMINDER: Return to Sprint 7A Remedial Tasks

After Sprint 7B Task 4 completes:

1. Quick Create will have created REAL test files in SharePoint Embedded
2. Return to Sprint 7A Task 3 (Manual Testing)
3. Use real Document records to test:
   - Download file
   - Replace file
   - Delete file
4. Validate MSAL authentication works end-to-end

**Reference:** [SPRINT-7A-REMEDIAL-TASKS.md](../../Sprint 7_Dataset Grid to SDAP/SPRINT-7A-REMEDIAL-TASKS.md)
```

---

## Summary of Changes by Task

| Task | Status | MSAL Updates | Multi-File Updates | Time Estimate Change |
|------|--------|--------------|--------------------|--------------------|
| Task 1 | ✅ Complete | ✅ Added MSAL init, error handling, cache clearing | ✅ Added MultiFileUploadService to structure | 1-2 days (unchanged) |
| Task 2 | 🔶 Partial | ⏳ Pending | 🔶 Title + criteria updated, implementation pending | 1-2 → 2-3 days |
| Task 3 | ⏳ Pending | ✅ No changes needed | ⏳ Review for multi-file metadata | 1 day (unchanged) |
| Task 4 | ⏳ Pending | ⏳ Add MSAL tests | ⏳ Add multi-file tests | 1 day (unchanged) |

**Overall Sprint:** 4-6 days → 5-7 days (increased due to multi-file complexity)

---

## Next Steps

1. **✅ DONE:** Review and update Task 1 (Quick Create Setup) for MSAL
2. **🔶 IN PROGRESS:** Update Task 2 (File Upload) with multi-file adaptive strategy
3. **⏳ TODO:** Review Task 3 (Default Value Mappings) for alignment
4. **⏳ TODO:** Update Task 4 (Testing) with multi-file and MSAL test scenarios
5. **⏳ TODO:** Begin Task 1 implementation

---

## References

- **Sprint 7B Overview:** [SPRINT-7B-OVERVIEW.md](SPRINT-7B-OVERVIEW.md)
- **Sprint 7B Updates Summary:** [SPRINT-7B-UPDATES-SUMMARY.md](SPRINT-7B-UPDATES-SUMMARY.md)
- **Sprint 8 MSAL Implementation:** [../Sprint 8_MSAL/SPRINT-8-COMPLETION-REVIEW.md](../../Sprint 8_MSAL/SPRINT-8-COMPLETION-REVIEW.md)
- **Universal Dataset Grid (Reference):** [index.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts)

---

**Document Owner:** Sprint 7B Planning
**Created:** October 6, 2025
**Status:** ✅ Task 1 Complete, Task 2-4 In Progress
**Next Action:** Complete Task 2-4 updates, then begin implementation
