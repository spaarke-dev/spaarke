# Sprint 7B - Task 2: File Upload & SPE Integration - IMPLEMENTATION GUIDE

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 2 of 4 (Single-File Baseline)
**Estimated Time**: 1-2 days
**Priority**: High
**Status**: Ready to Start
**Depends On**: ✅ Sprint 7B Task 1 (COMPLETE)

---

## ⚠️ IMPORTANT: Task Scope

This guide covers **Task 2 BASELINE: Single-File Upload Only**

**What's in THIS task:**
- ✅ Single file upload to SharePoint Embedded
- ✅ MSAL token acquisition for SDAP API calls
- ✅ Dataverse record creation with SPE metadata
- ✅ Upload progress indicator
- ✅ Error handling

**What's in Task 2A (separate task):**
- ⏳ Multi-file upload (1-10 files)
- ⏳ Adaptive strategy selection
- ⏳ Batched uploads with progress

**Reference:** [TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md](TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md) contains full multi-file implementation.

---

## Current Status (After Task 1)

✅ **What's Already Done:**
- PCF project structure created
- MSAL authentication integrated and initialized
- React components (QuickCreateForm, FilePickerField) implemented
- Parent context retrieval working
- Default value mapping working
- Save handler exists (placeholder only)

❌ **What's Missing (Task 2 will add):**
- File upload to SPE via SDAP API
- MSAL token acquisition for API calls
- Dataverse record creation
- Upload progress indicator
- Error handling for upload failures
- Form close after successful save

---

## Success Criteria

### Core Functionality
- ✅ Single file uploads to SharePoint Embedded via SDAP API
- ✅ MSAL token used for authentication (Sprint 8 pattern)
- ✅ SPE returns metadata (URL, item ID, size, created date, modified date)
- ✅ Dataverse record created with form data + SPE metadata
- ✅ Upload progress indicator shown during upload
- ✅ Error handling for upload failures
- ✅ Quick Create form closes automatically after save
- ✅ Grid refreshes automatically (Power Apps standard behavior)
- ✅ All operations logged appropriately
- ✅ Zero breaking changes to Task 1

---

## Implementation Steps

### Step 1: Create FileUploadService (30 min)

**File:** `services/FileUploadService.ts`

**Purpose:** Orchestrates file upload to SPE via SDAP API

**Implementation:**

```typescript
import { SdapApiClient } from './SdapApiClient';
import { SpeFileMetadata, ServiceResult } from '../types';
import { logger } from '../utils/logger';

export interface FileUploadRequest {
    file: File;
    driveId: string;
    fileName?: string;
}

export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}

    async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
        try {
            logger.info('FileUploadService', 'Starting file upload', {
                fileName: request.file.name,
                fileSize: request.file.size,
                driveId: request.driveId
            });

            // Upload file to SPE via SDAP API
            // apiClient already uses MSAL for authentication (from Sprint 8)
            const speMetadata = await this.apiClient.uploadFile({
                file: request.file,
                driveId: request.driveId,
                fileName: request.fileName || request.file.name
            });

            logger.info('FileUploadService', 'File uploaded successfully', {
                fileName: speMetadata.fileName,
                driveItemId: speMetadata.driveItemId
            });

            return {
                success: true,
                data: speMetadata
            };

        } catch (error) {
            logger.error('FileUploadService', 'File upload failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error occurred'
            };
        }
    }
}
```

**Key Points:**
- Uses `SdapApiClient` (already MSAL-enabled from Sprint 7A/8)
- Returns `ServiceResult` pattern (success/error)
- Comprehensive logging for debugging

---

### Step 2: Create DataverseRecordService (45 min)

**File:** `services/DataverseRecordService.ts`

**Purpose:** Creates Dataverse Document records with SPE metadata

**Implementation:**

```typescript
import { logger } from '../utils/logger';
import { SpeFileMetadata, ServiceResult } from '../types';

export interface CreateDocumentRequest {
    formData: Record<string, unknown>;
    speMetadata: SpeFileMetadata;
    parentEntityName: string;
    parentRecordId: string;
}

export class DataverseRecordService {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    constructor(private context: ComponentFramework.Context<any>) {}

    async createDocument(request: CreateDocumentRequest): Promise<ServiceResult<string>> {
        try {
            logger.info('DataverseRecordService', 'Creating Document record', {
                fileName: request.speMetadata.fileName,
                parentEntity: request.parentEntityName
            });

            // Build record data
            const recordData: Record<string, unknown> = {
                // Form data (user-entered)
                ...request.formData,

                // SPE metadata (from upload)
                sprk_sharepointurl: request.speMetadata.sharePointUrl,
                sprk_driveitemid: request.speMetadata.driveItemId,
                sprk_filename: request.speMetadata.fileName,
                sprk_filesize: request.speMetadata.fileSize,
                sprk_createddate: request.speMetadata.createdDateTime,
                sprk_modifieddate: request.speMetadata.lastModifiedDateTime
            };

            // Add parent entity relationship
            if (request.parentEntityName && request.parentRecordId) {
                const relationshipField = this.getRelationshipField(request.parentEntityName);
                if (relationshipField) {
                    const entitySetName = this.getEntitySetName(request.parentEntityName);
                    recordData[`${relationshipField}@odata.bind`] =
                        `/${entitySetName}(${request.parentRecordId})`;
                }
            }

            // Create record via Dataverse Web API
            const result = await this.context.webAPI.createRecord('sprk_document', recordData);

            logger.info('DataverseRecordService', 'Document record created', {
                recordId: result.id,
                fileName: request.speMetadata.fileName
            });

            return {
                success: true,
                data: result.id
            };

        } catch (error) {
            logger.error('DataverseRecordService', 'Record creation failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Failed to create record'
            };
        }
    }

    private getRelationshipField(parentEntityName: string): string | null {
        const mappings: Record<string, string> = {
            'sprk_matter': 'sprk_matter'
        };
        return mappings[parentEntityName] || null;
    }

    private getEntitySetName(entityName: string): string {
        const mappings: Record<string, string> = {
            'sprk_matter': 'sprk_matters',
            'sprk_document': 'sprk_documents'
        };
        return mappings[entityName] || `${entityName}s`;
    }
}
```

**Key Points:**
- Uses PCF WebAPI for Dataverse operations
- Combines form data + SPE metadata
- Uses OData bind syntax for parent relationship
- Returns record ID on success

---

### Step 3: Update UniversalQuickCreatePCF.ts (1 hour)

**File:** `UniversalQuickCreatePCF.ts`

**Changes Required:**

#### A. Add Service Properties

```typescript
export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // ... existing properties ...

    // 🆕 Services (Task 2)
    private fileUploadService: FileUploadService | null = null;
    private dataverseRecordService: DataverseRecordService | null = null;
```

#### B. Initialize Services in init()

```typescript
public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
    // ... existing init code ...

    // 🆕 Initialize services (Task 2)
    const sdapApiClient = SdapApiClientFactory.create(this.sdapApiBaseUrl);
    this.fileUploadService = new FileUploadService(sdapApiClient);
    this.dataverseRecordService = new DataverseRecordService(context);

    logger.info('UniversalQuickCreatePCF', 'Services initialized (Task 2)');
}
```

#### C. Implement handleSave() - Replace Placeholder

**Find this placeholder:**
```typescript
private async handleSave(formData: Record<string, unknown>, file?: File): Promise<void> {
    logger.info('UniversalQuickCreatePCF', 'Save requested', { formData, hasFile: !!file });

    try {
        // 🔴 Task 2 will implement:
        alert(`Save clicked!\n\nFile: ${file?.name || 'None'}\n\nThis will be implemented in Task 2.`);
    } catch (error) {
        logger.error('UniversalQuickCreatePCF', 'Save failed', error);
        throw error;
    }
}
```

**Replace with:**
```typescript
private async handleSave(formData: Record<string, unknown>, file?: File): Promise<void> {
    logger.info('UniversalQuickCreatePCF', 'Save requested', {
        formData,
        hasFile: !!file,
        fileName: file?.name
    });

    try {
        // Validate file is provided
        if (!file) {
            throw new Error('No file selected');
        }

        // Get container ID from form data or parent record
        const containerId = (formData.sprk_containerid as string) ||
                           (this.parentRecordData?.sprk_containerid as string);

        if (!containerId) {
            throw new Error('Container ID not found - cannot upload file to SharePoint');
        }

        logger.info('UniversalQuickCreatePCF', 'Container ID found', { containerId });

        // Step 1: Upload file to SPE
        if (!this.fileUploadService) {
            throw new Error('FileUploadService not initialized');
        }

        logger.info('UniversalQuickCreatePCF', 'Uploading file to SPE...');

        const uploadResult = await this.fileUploadService.uploadFile({
            file,
            driveId: containerId,
            fileName: file.name
        });

        if (!uploadResult.success || !uploadResult.data) {
            throw new Error(`File upload failed: ${uploadResult.error || 'Unknown error'}`);
        }

        const speMetadata = uploadResult.data;

        logger.info('UniversalQuickCreatePCF', 'File uploaded to SPE successfully', {
            driveItemId: speMetadata.driveItemId,
            sharePointUrl: speMetadata.sharePointUrl
        });

        // Step 2: Create Dataverse record
        if (!this.dataverseRecordService) {
            throw new Error('DataverseRecordService not initialized');
        }

        logger.info('UniversalQuickCreatePCF', 'Creating Dataverse record...');

        const createResult = await this.dataverseRecordService.createDocument({
            formData,
            speMetadata,
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId
        });

        if (!createResult.success) {
            throw new Error(`Record creation failed: ${createResult.error || 'Unknown error'}`);
        }

        logger.info('UniversalQuickCreatePCF', 'Dataverse record created successfully', {
            recordId: createResult.data
        });

        // Step 3: Success - Form will close automatically
        logger.info('UniversalQuickCreatePCF', 'Save complete - form will close');

        // Power Apps will automatically close the Quick Create form
        // and refresh the parent grid

    } catch (error) {
        logger.error('UniversalQuickCreatePCF', 'Save failed', error);
        throw error; // Re-throw to show error in React UI
    }
}
```

**Key Points:**
- Validates file and container ID
- Uploads file using FileUploadService (MSAL token acquired automatically)
- Creates Dataverse record with SPE metadata
- Comprehensive logging at each step
- Error handling with re-throw to UI

---

### Step 4: Update QuickCreateForm.tsx - Add Progress Indicator (30 min)

**File:** `components/QuickCreateForm.tsx`

**Changes Required:**

#### A. Add Upload Progress State

```typescript
export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    // ... existing props ...
}) => {
    // ... existing state ...
    const [uploadProgress, setUploadProgress] = React.useState<number>(0);
```

#### B. Update handleSubmit to Track Progress

```typescript
const handleSubmit = React.useCallback(async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setUploadProgress(0);

    // Validate file upload if required
    if (enableFileUpload && !selectedFile) {
        setError('Please select a file to upload.');
        return;
    }

    setIsSaving(true);
    setUploadProgress(10); // Starting

    try {
        logger.info('QuickCreateForm', 'Submitting form', { formData, hasFile: !!selectedFile });

        setUploadProgress(30); // Uploading to SPE

        await onSave(formData, selectedFile);

        setUploadProgress(100); // Complete

        logger.info('QuickCreateForm', 'Form submitted successfully');

        // Form will close automatically

    } catch (err) {
        const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
        logger.error('QuickCreateForm', 'Form submission failed', err);
        setError(errorMessage);
        setUploadProgress(0);
    } finally {
        setIsSaving(false);
    }
}, [formData, selectedFile, enableFileUpload, onSave]);
```

#### C. Add Progress Bar in JSX

```typescript
{/* Loading overlay */}
{isSaving && (
    <div className={styles.loadingOverlay}>
        <Spinner label={`Uploading... ${uploadProgress}%`} size="large" />
        {uploadProgress > 0 && uploadProgress < 100 && (
            <div style={{ marginTop: '16px', fontSize: '14px' }}>
                {uploadProgress < 30 && 'Starting upload...'}
                {uploadProgress >= 30 && uploadProgress < 100 && 'Uploading file to SharePoint...'}
            </div>
        )}
    </div>
)}
```

---

### Step 5: Update Types (15 min)

**File:** `types/index.ts`

**Ensure these types exist (they should already be copied from Sprint 7A):**

```typescript
export interface SpeFileMetadata {
    sharePointUrl: string;
    driveItemId: string;
    fileName: string;
    fileSize: number;
    createdDateTime: string;
    lastModifiedDateTime: string;
}

export interface ServiceResult<T> {
    success: boolean;
    data?: T;
    error?: string;
}
```

**If missing, add them.**

---

### Step 6: Build and Test (1 hour)

#### A. Build

```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build:prod
```

**Expected:** ✅ Build succeeds, bundle ~600-650 KB

#### B. Test Checklist

**Manual Testing in Dataverse:**

1. **MSAL Authentication**
   - [ ] Console shows "MSAL authentication initialized successfully ✅"
   - [ ] Console shows "User authenticated: true"

2. **Parent Context**
   - [ ] Open Matter form
   - [ ] Click "+ New Document" in Documents subgrid
   - [ ] Quick Create launches
   - [ ] Console shows parent context retrieved
   - [ ] Default values populated (Document Title = Matter Name)

3. **File Upload**
   - [ ] Select a test file (e.g., 2MB PDF)
   - [ ] File name displayed below file picker
   - [ ] Click Save
   - [ ] Console shows "Uploading file to SPE..."
   - [ ] Console shows "File uploaded to SPE successfully"
   - [ ] Console shows drive item ID and SharePoint URL

4. **Dataverse Record Creation**
   - [ ] Console shows "Creating Dataverse record..."
   - [ ] Console shows "Dataverse record created successfully"
   - [ ] Console shows record ID (GUID)

5. **Form Close & Grid Refresh**
   - [ ] Quick Create form closes automatically
   - [ ] Grid refreshes
   - [ ] New Document appears in grid
   - [ ] Document has correct title and file info

6. **Error Handling**
   - [ ] Try save without file → Error: "Please select a file to upload."
   - [ ] Try with invalid container ID → Error displayed
   - [ ] Check console for error logs

---

## Implementation Checklist

**Services:**
- [ ] Create `services/FileUploadService.ts`
- [ ] Create `services/DataverseRecordService.ts`

**PCF Updates:**
- [ ] Add service properties to `UniversalQuickCreatePCF.ts`
- [ ] Initialize services in `init()`
- [ ] Replace `handleSave()` placeholder with full implementation
- [ ] Import new services

**React Updates:**
- [ ] Add `uploadProgress` state to `QuickCreateForm.tsx`
- [ ] Update `handleSubmit` to track progress
- [ ] Update loading overlay with progress percentage

**Build:**
- [ ] Run `npm run build` - verify no errors
- [ ] Run `npm run build:prod` - verify bundle size
- [ ] Check console for any warnings

**Testing:**
- [ ] Deploy to Dataverse test environment
- [ ] Test MSAL authentication
- [ ] Test file upload (2MB PDF)
- [ ] Test Dataverse record creation
- [ ] Test form close and grid refresh
- [ ] Test error handling

---

## Expected Console Output

**Successful Upload:**
```
[UniversalQuickCreatePCF] Save requested: { formData: {...}, hasFile: true, fileName: "test.pdf" }
[UniversalQuickCreatePCF] Container ID found: { containerId: "b!abc123..." }
[UniversalQuickCreatePCF] Uploading file to SPE...
[FileUploadService] Starting file upload: { fileName: "test.pdf", fileSize: 2048576, driveId: "b!abc123..." }
[SdapApiClient] Uploading file via SDAP API...
[MsalAuthProvider] Token retrieved from cache (5ms)
[FileUploadService] File uploaded successfully: { fileName: "test.pdf", driveItemId: "01ABC..." }
[UniversalQuickCreatePCF] File uploaded to SPE successfully: { driveItemId: "01ABC...", sharePointUrl: "https://..." }
[UniversalQuickCreatePCF] Creating Dataverse record...
[DataverseRecordService] Creating Document record: { fileName: "test.pdf", parentEntity: "sprk_matter" }
[DataverseRecordService] Document record created: { recordId: "12345678-...", fileName: "test.pdf" }
[UniversalQuickCreatePCF] Dataverse record created successfully: { recordId: "12345678-..." }
[UniversalQuickCreatePCF] Save complete - form will close
```

---

## Common Issues & Solutions

### Issue 1: "Container ID not found"

**Symptom:** Error when clicking Save

**Cause:** Parent Matter doesn't have `sprk_containerid` field populated

**Solution:**
1. Check parent Matter record has container ID
2. Verify default value mapping in manifest
3. Check console for parent record data

---

### Issue 2: "401 Unauthorized" from SDAP API

**Symptom:** File upload fails with 401 error

**Cause:** MSAL token not acquired or expired

**Solution:**
1. Check MSAL initialized successfully (console logs)
2. Verify Azure AD app registration configured correctly
3. Check SDAP API base URL in manifest parameter
4. Try clearing browser cache and reload

---

### Issue 3: File uploads but record creation fails

**Symptom:** File in SPE but no Dataverse record

**Cause:** Field mapping issue or permissions

**Solution:**
1. Check user has create permissions on sprk_document entity
2. Verify field names match Dataverse schema
3. Check console for detailed error message
4. Verify parent relationship field exists

---

## Files to Modify

**New Files:**
1. `services/FileUploadService.ts` (~80 lines)
2. `services/DataverseRecordService.ts` (~100 lines)

**Modified Files:**
3. `UniversalQuickCreatePCF.ts` (replace `handleSave` method, add service initialization)
4. `components/QuickCreateForm.tsx` (add progress tracking)

**Total:** 2 new files, 2 modified files (~200 lines of new code)

---

## Next Steps After Task 2

Once Task 2 is complete and tested:

**Option 1:** Proceed to Task 2A (Multi-File Upload Enhancement)
- Reference: [TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md](TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md)
- Adds multi-file support with adaptive strategy

**Option 2:** Proceed to Task 3 (Default Value Mappings)
- Make field rendering fully configurable
- Support multiple entity types
- Document configuration patterns

---

## References

- **Task 1 Completion:** [TASK-7B-1-COMPLETION-SUMMARY.md](TASK-7B-1-COMPLETION-SUMMARY.md)
- **Task 2A (Multi-File):** [TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md](TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md)
- **Sprint 7B Overview:** [SPRINT-7B-OVERVIEW.md](SPRINT-7B-OVERVIEW.md)
- **Sprint 8 MSAL:** Sprint 8 completion review
- **SDAP API Client:** `services/SdapApiClient.ts` (already MSAL-enabled)

---

**Ready to implement Task 2!** 🚀

This task should take **1-2 days** and will complete the core file upload functionality.
