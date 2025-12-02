# Sprint 7B - Task 2: File Upload with SPE Integration (Multi-File Adaptive Upload)

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 2 of 4
**Estimated Time**: 2-3 days (increased from 1-2 days due to multi-file complexity)
**Priority**: High
**Status**: Pending
**Depends On**: Sprint 7A Task 1 (SDAP API Client), Sprint 7B Task 1 (Quick Create PCF Setup)

---

## Task Overview

Implement **multi-file upload with adaptive strategy** to SharePoint Embedded via SDAP BFF API from the Universal Quick Create PCF control. Users can upload 1-10 files in a single operation, creating multiple Dataverse Document records with shared metadata. The system automatically selects the optimal upload strategy based on file size and count.

**Key Features:**
- Single file upload (baseline)
- Multi-file upload (1-10 files)
- Adaptive strategy selection (sync-parallel vs long-running)
- Progress indicators for both strategies
- Shared metadata across all documents
- One Document record per file

---

## Success Criteria

### Single File Upload (Baseline)
- ✅ Single file uploads to SharePoint Embedded via SDAP API
- ✅ SPE returns metadata (URL, item ID, size, created date, modified date)
- ✅ Dataverse record created with form data + SPE metadata
- ✅ Upload progress indicator shown during upload
- ✅ Error handling for upload failures
- ✅ Quick Create form closes automatically after save
- ✅ Grid refreshes automatically (Power Apps standard behavior)

### Multi-File Upload (Enhanced)
- ✅ User can select 1-10 files in a single Quick Create
- ✅ Adaptive strategy selection based on file size/count
- ✅ **Sync-parallel upload**: 1-3 files, <10MB each, <20MB total (fast, 3-4 seconds)
- ✅ **Long-running batched upload**: >3 files OR >10MB OR >20MB total (batched with progress)
- ✅ Progress indicators for both strategies
- ✅ One Document record created per file
- ✅ All documents share the same metadata (description, category, etc.)
- ✅ Partial success handling (if 4 of 5 files upload, show summary)
- ✅ All operations logged appropriately
- ✅ Zero breaking changes to Sprint 7B Task 1

---

## Context & Background

### What We're Building

Extend the Universal Quick Create PCF (from Task 1) to:

1. **Upload file to SPE** when user clicks Save
2. **Receive SPE metadata** (URL, item ID, etc.) from SDAP API
3. **Create Dataverse record** with:
   - User-entered form data (title, description, etc.)
   - SPE metadata (URL, item ID, size, dates)
   - Parent entity relationship (Matter → Document)
4. **Close form** and trigger grid refresh

### User Flow

```
1. User fills Quick Create form, selects file
2. User clicks Save
3. Quick Create PCF:
   a. Shows upload progress indicator
   b. Uploads file to SPE via SDAP API
      POST /api/spe/upload
      Body: { file, containerId, fileName }
   c. Receives SPE metadata:
      {
        sharePointUrl: "https://...",
        driveItemId: "01ABC...",
        fileName: "Document.pdf",
        fileSize: 1234567,
        createdDateTime: "2025-10-05T10:00:00Z",
        lastModifiedDateTime: "2025-10-05T10:00:00Z"
      }
   d. Creates Dataverse record:
      POST /api/data/v9.2/sprk_documents
      Body: {
        sprk_name: "Document Title",
        sprk_description: "...",
        sprk_sharepointurl: "https://...",
        sprk_driveitemid: "01ABC...",
        sprk_filename: "Document.pdf",
        sprk_filesize: 1234567,
        sprk_createddate: "2025-10-05T10:00:00Z",
        sprk_modifieddate: "2025-10-05T10:00:00Z",
        "sprk_matter@odata.bind": "/sprk_matters(MATTER_GUID)"
      }
   e. Closes Quick Create form
4. Power Apps refreshes grid automatically
5. User sees new document in grid with clickable URL
```

### SDAP API Integration

**Reuse SDAP API Client from Sprint 7A Task 1**:

```typescript
// services/SdapApiClient.ts (from Sprint 7A)
export class SdapApiClient {
    async uploadFile(
        file: File,
        containerId: string,
        fileName?: string
    ): Promise<SpeFileMetadata> {
        // Implementation from Sprint 7A
    }
}
```

We'll create a **FileUploadService** that orchestrates:
1. File upload via SDAP API client
2. Dataverse record creation
3. Error handling and retry logic

---

## SDAP API Endpoints

### Upload File Endpoint

**Endpoint**: `POST /api/spe/upload`

**Request**:
```typescript
// multipart/form-data
{
    file: File,                    // The file to upload
    containerId: string,           // SPE container ID (from Matter)
    fileName?: string,             // Optional custom file name
    folderPath?: string            // Optional folder path
}
```

**Response** (200 OK):
```typescript
{
    sharePointUrl: string;         // https://...
    driveItemId: string;           // 01ABC123...
    fileName: string;              // "Document.pdf"
    fileSize: number;              // 1234567 (bytes)
    createdDateTime: string;       // ISO 8601
    lastModifiedDateTime: string;  // ISO 8601
}
```

**Error Response** (400/500):
```typescript
{
    error: string;                 // Error message
    details?: string;              // Optional details
}
```

---

## Deliverables

### 1. File Upload Service (services/FileUploadService.ts)

```typescript
import { SdapApiClient } from './SdapApiClient';
import { logger } from '../utils/logger';

export interface FileUploadRequest {
    file: File;
    containerId: string;
    fileName?: string;
    folderPath?: string;
}

export interface FileUploadResult {
    success: boolean;
    speMetadata?: SpeFileMetadata;
    error?: string;
}

export interface SpeFileMetadata {
    sharePointUrl: string;
    driveItemId: string;
    fileName: string;
    fileSize: number;
    createdDateTime: string;
    lastModifiedDateTime: string;
}

export class FileUploadService {
    constructor(private sdapApiClient: SdapApiClient) {}

    async uploadFile(request: FileUploadRequest): Promise<FileUploadResult> {
        try {
            logger.info('FileUploadService', 'Starting file upload', {
                fileName: request.file.name,
                fileSize: request.file.size,
                containerId: request.containerId
            });

            // Upload file to SPE via SDAP API
            const speMetadata = await this.sdapApiClient.uploadFile(
                request.file,
                request.containerId,
                request.fileName
            );

            logger.info('FileUploadService', 'File uploaded successfully', speMetadata);

            return {
                success: true,
                speMetadata
            };

        } catch (error) {
            logger.error('FileUploadService', 'File upload failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error occurred'
            };
        }
    }

    async uploadFileWithProgress(
        request: FileUploadRequest,
        onProgress?: (progress: number) => void
    ): Promise<FileUploadResult> {
        try {
            logger.info('FileUploadService', 'Starting file upload with progress', {
                fileName: request.file.name,
                fileSize: request.file.size,
                containerId: request.containerId
            });

            // Note: Progress tracking requires XMLHttpRequest or Fetch with streams
            // For now, we'll simulate progress in chunks

            onProgress?.(0);

            const speMetadata = await this.sdapApiClient.uploadFile(
                request.file,
                request.containerId,
                request.fileName
            );

            onProgress?.(100);

            logger.info('FileUploadService', 'File uploaded successfully', speMetadata);

            return {
                success: true,
                speMetadata
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

### 2. Dataverse Record Creation Service (services/DataverseRecordService.ts)

```typescript
import { logger } from '../utils/logger';
import { SpeFileMetadata } from './FileUploadService';

export interface CreateRecordRequest {
    entityName: string;
    formData: Record<string, any>;
    speMetadata?: SpeFileMetadata;
    parentEntityName?: string;
    parentRecordId?: string;
}

export interface CreateRecordResult {
    success: boolean;
    recordId?: string;
    error?: string;
}

export class DataverseRecordService {
    constructor(private context: ComponentFramework.Context<any>) {}

    async createRecord(request: CreateRecordRequest): Promise<CreateRecordResult> {
        try {
            logger.info('DataverseRecordService', 'Creating Dataverse record', {
                entityName: request.entityName,
                hasFormData: !!request.formData,
                hasSpeMetadata: !!request.speMetadata
            });

            // Build record data
            const recordData = this.buildRecordData(request);

            // Create record via Dataverse Web API
            const result = await this.context.webAPI.createRecord(
                request.entityName,
                recordData
            );

            logger.info('DataverseRecordService', 'Record created successfully', {
                recordId: result.id
            });

            return {
                success: true,
                recordId: result.id
            };

        } catch (error) {
            logger.error('DataverseRecordService', 'Record creation failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error occurred'
            };
        }
    }

    private buildRecordData(request: CreateRecordRequest): any {
        const recordData: any = { ...request.formData };

        // Add SPE metadata fields (if provided)
        if (request.speMetadata) {
            recordData.sprk_sharepointurl = request.speMetadata.sharePointUrl;
            recordData.sprk_driveitemid = request.speMetadata.driveItemId;
            recordData.sprk_filename = request.speMetadata.fileName;
            recordData.sprk_filesize = request.speMetadata.fileSize;
            recordData.sprk_createddate = request.speMetadata.createdDateTime;
            recordData.sprk_modifieddate = request.speMetadata.lastModifiedDateTime;
        }

        // Add parent entity relationship (if provided)
        if (request.parentEntityName && request.parentRecordId) {
            const relationshipField = this.getRelationshipField(
                request.entityName,
                request.parentEntityName
            );

            if (relationshipField) {
                // Use OData bind syntax for relationship
                recordData[`${relationshipField}@odata.bind`] =
                    `/${this.getEntitySetName(request.parentEntityName)}(${request.parentRecordId})`;

                logger.debug('DataverseRecordService', 'Added parent relationship', {
                    relationshipField,
                    parentEntityName: request.parentEntityName,
                    parentRecordId: request.parentRecordId
                });
            }
        }

        return recordData;
    }

    private getRelationshipField(childEntity: string, parentEntity: string): string | null {
        // Map of child entity -> parent entity -> relationship field
        const relationshipMappings: Record<string, Record<string, string>> = {
            'sprk_document': {
                'sprk_matter': 'sprk_matter'
            },
            'task': {
                'sprk_matter': 'regardingobjectid'
            },
            'contact': {
                'account': 'parentcustomerid'
            }
        };

        return relationshipMappings[childEntity]?.[parentEntity] || null;
    }

    private getEntitySetName(entityName: string): string {
        // Map logical names to entity set names
        const entitySetMappings: Record<string, string> = {
            'sprk_matter': 'sprk_matters',
            'sprk_document': 'sprk_documents',
            'account': 'accounts',
            'contact': 'contacts',
            'task': 'tasks'
        };

        return entitySetMappings[entityName] || `${entityName}s`;
    }
}
```

### 3. Update UniversalQuickCreatePCF.ts (handleSave)

```typescript
// In UniversalQuickCreatePCF.ts

import { FileUploadService } from './services/FileUploadService';
import { DataverseRecordService } from './services/DataverseRecordService';
import { SdapApiClientFactory } from './services/SdapApiClientFactory';

export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // ... existing properties ...

    private fileUploadService: FileUploadService | null = null;
    private dataverseRecordService: DataverseRecordService | null = null;

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // ... existing init code ...

        // Initialize services
        const sdapApiClient = SdapApiClientFactory.create(context, this.sdapApiBaseUrl);
        this.fileUploadService = new FileUploadService(sdapApiClient);
        this.dataverseRecordService = new DataverseRecordService(context);

        logger.info('UniversalQuickCreate', 'Services initialized');
    }

    private async handleSave(
        formData: Record<string, any>,
        file?: File,
        onProgress?: (progress: number) => void
    ): Promise<void> {
        logger.info('UniversalQuickCreate', 'Save requested', {
            formData,
            hasFile: !!file
        });

        try {
            let speMetadata = undefined;

            // Step 1: Upload file to SPE (if file provided)
            if (file && this.fileUploadService) {
                // Get container ID from form data or parent record
                const containerId = formData.sprk_containerid ||
                                   this.parentRecordData?.sprk_containerid;

                if (!containerId) {
                    throw new Error('Container ID not found - cannot upload file to SharePoint');
                }

                logger.info('UniversalQuickCreate', 'Uploading file to SPE', {
                    fileName: file.name,
                    containerId
                });

                const uploadResult = await this.fileUploadService.uploadFileWithProgress(
                    {
                        file,
                        containerId,
                        fileName: file.name
                    },
                    onProgress
                );

                if (!uploadResult.success) {
                    throw new Error(`File upload failed: ${uploadResult.error}`);
                }

                speMetadata = uploadResult.speMetadata;

                logger.info('UniversalQuickCreate', 'File uploaded to SPE', speMetadata);
            }

            // Step 2: Create Dataverse record
            if (this.dataverseRecordService) {
                logger.info('UniversalQuickCreate', 'Creating Dataverse record');

                const createResult = await this.dataverseRecordService.createRecord({
                    entityName: this.entityName,
                    formData: formData,
                    speMetadata: speMetadata,
                    parentEntityName: this.parentEntityName,
                    parentRecordId: this.parentRecordId
                });

                if (!createResult.success) {
                    throw new Error(`Record creation failed: ${createResult.error}`);
                }

                logger.info('UniversalQuickCreate', 'Record created successfully', {
                    recordId: createResult.recordId
                });
            }

            // Step 3: Close Quick Create form (Power Apps handles this automatically)
            logger.info('UniversalQuickCreate', 'Save complete - closing form');

            // Note: Power Apps will automatically close the Quick Create form
            // and refresh the parent grid when the save operation completes

        } catch (error) {
            logger.error('UniversalQuickCreate', 'Save failed', error);
            throw error; // Re-throw to show error in UI
        }
    }
}
```

### 4. Update QuickCreateForm.tsx (Progress Indicator)

```typescript
// In QuickCreateForm.tsx

import { ProgressBar } from '@fluentui/react-components';

export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    // ... existing props ...
}) => {
    const styles = useStyles();

    // ... existing state ...
    const [uploadProgress, setUploadProgress] = React.useState<number>(0);

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

        try {
            logger.info('QuickCreateForm', 'Submitting form', {
                formData,
                hasFile: !!selectedFile
            });

            // Call onSave with progress callback
            await onSave(
                formData,
                selectedFile,
                (progress: number) => {
                    setUploadProgress(progress);
                    logger.debug('QuickCreateForm', 'Upload progress', { progress });
                }
            );

            logger.info('QuickCreateForm', 'Form submitted successfully');

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
            logger.error('QuickCreateForm', 'Form submission failed', err);
            setError(errorMessage);
        } finally {
            setIsSaving(false);
            setUploadProgress(0);
        }
    }, [formData, selectedFile, enableFileUpload, onSave]);

    return (
        <FluentProvider theme={webLightTheme}>
            <div className={styles.container}>
                <form onSubmit={handleSubmit} className={styles.form}>
                    {/* ... existing fields ... */}

                    {/* Upload progress indicator */}
                    {isSaving && uploadProgress > 0 && uploadProgress < 100 && (
                        <Field label="Upload Progress">
                            <ProgressBar value={uploadProgress / 100} />
                            <div style={{ fontSize: '12px', marginTop: '4px' }}>
                                {uploadProgress}%
                            </div>
                        </Field>
                    )}

                    {/* Error message */}
                    {error && (
                        <div style={{ color: 'red', fontSize: '14px' }}>
                            {error}
                        </div>
                    )}

                    {/* ... existing actions ... */}
                </form>

                {/* Loading overlay */}
                {isSaving && (
                    <div className={styles.loadingOverlay}>
                        <Spinner
                            label={uploadProgress > 0 ? `Uploading... ${uploadProgress}%` : 'Saving...'}
                            size="large"
                        />
                    </div>
                )}
            </div>
        </FluentProvider>
    );
};
```

---

## Implementation Steps

### Step 1: Copy SDAP API Client from Sprint 7A (30 min)

```bash
# Copy SDAP API Client from Universal Dataset Grid
cp ../UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts services/
cp ../UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts services/

# Copy types
cp ../UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts types/
```

Verify the API client has the `uploadFile` method implemented.

### Step 2: Create FileUploadService (1 hour)

1. Create `services/FileUploadService.ts`
2. Implement `uploadFile()` and `uploadFileWithProgress()`
3. Add comprehensive error handling
4. Add logging for all operations

### Step 3: Create DataverseRecordService (1-2 hours)

1. Create `services/DataverseRecordService.ts`
2. Implement `createRecord()` method
3. Implement `buildRecordData()` helper
4. Add relationship field mappings
5. Add entity set name mappings
6. Test with different entity types

### Step 4: Update UniversalQuickCreatePCF (1 hour)

1. Import new services
2. Initialize services in `init()`
3. Update `handleSave()` method with:
   - File upload orchestration
   - Progress callback
   - Dataverse record creation
   - Error handling

### Step 5: Update QuickCreateForm Component (1 hour)

1. Add `uploadProgress` state
2. Add progress callback to `handleSubmit`
3. Add `ProgressBar` component
4. Update loading overlay with progress
5. Update `onSave` prop to include progress callback

### Step 6: Test Locally (1-2 hours)

```bash
npm run build
npm start watch
```

Test scenarios:
1. Upload small file (< 1 MB)
2. Upload large file (> 10 MB) - verify progress
3. Upload fails (invalid container ID) - verify error handling
4. Record creation fails - verify error handling

### Step 7: Integration Testing (2-3 hours)

Deploy to Dataverse and test:

1. **Happy Path**:
   - Open Matter form
   - Click "+ New Document"
   - Select file
   - Fill form fields
   - Click Save
   - Verify file uploads to SPE
   - Verify record created in Dataverse
   - Verify form closes
   - Verify grid refreshes
   - Verify URL is clickable

2. **Error Cases**:
   - Container ID missing → error message
   - File upload fails → error message
   - Record creation fails → error message (but file already uploaded)
   - Network timeout → error message

3. **Edge Cases**:
   - Very large file (> 100 MB)
   - Special characters in file name
   - Duplicate file name
   - No file selected (should show validation error)

---

## Field Mappings (Document Entity)

### User-Entered Fields (from Form Data)

| Form Field | Dataverse Field | Type | Required |
|------------|----------------|------|----------|
| Document Title | `sprk_documenttitle` or `sprk_name` | String | Yes |
| Description | `sprk_description` | String | No |

### Auto-Populated Fields (from SPE Metadata)

| SPE Metadata | Dataverse Field | Type | Source |
|--------------|----------------|------|--------|
| sharePointUrl | `sprk_sharepointurl` | String | SDAP API response |
| driveItemId | `sprk_driveitemid` | String | SDAP API response |
| fileName | `sprk_filename` | String | SDAP API response |
| fileSize | `sprk_filesize` | Number | SDAP API response |
| createdDateTime | `sprk_createddate` | DateTime | SDAP API response |
| lastModifiedDateTime | `sprk_modifieddate` | DateTime | SDAP API response |

### Auto-Populated Fields (from Parent Matter)

| Parent Field | Dataverse Field | Type | Source |
|--------------|----------------|------|--------|
| Container ID | `sprk_containerid` | String | Parent Matter record |
| Owner | `ownerid` | Lookup | Parent Matter record |
| Matter | `sprk_matter` | Lookup | Parent relationship (OData bind) |

---

## Error Handling

### Upload Errors

```typescript
try {
    const uploadResult = await this.fileUploadService.uploadFileWithProgress(...);

    if (!uploadResult.success) {
        throw new Error(`File upload failed: ${uploadResult.error}`);
    }

} catch (error) {
    logger.error('QuickCreate', 'Upload failed', error);

    // Show user-friendly error
    setError('File upload failed. Please check your connection and try again.');
}
```

### Record Creation Errors

```typescript
try {
    const createResult = await this.dataverseRecordService.createRecord(...);

    if (!createResult.success) {
        throw new Error(`Record creation failed: ${createResult.error}`);
    }

} catch (error) {
    logger.error('QuickCreate', 'Record creation failed', error);

    // Show user-friendly error
    setError('Failed to create document record. The file was uploaded but the record was not created.');
}
```

### Rollback Considerations

**Question**: If file uploads successfully but record creation fails, what should we do?

**Options**:
1. **Leave file in SPE** (recommended) - User can retry record creation later
2. **Delete file from SPE** - Requires calling delete API, adds complexity
3. **Show error with retry option** - Best UX, let user retry

**Recommendation**: Option 3 - Leave file in SPE, show error, allow retry.

---

## Testing Checklist

### File Upload

- [ ] File <1 MB uploads successfully
- [ ] File >10 MB uploads successfully
- [ ] Upload progress shows 0% → 100%
- [ ] Progress indicator displays during upload
- [ ] File name preserved (no special character issues)
- [ ] SPE metadata returned correctly
- [ ] Logger outputs file upload start/complete

### Record Creation

- [ ] Dataverse record created with all fields
- [ ] SPE metadata fields populated correctly
- [ ] Parent relationship (Matter → Document) created
- [ ] Auto-populated fields from Matter correct
- [ ] User-entered fields saved correctly
- [ ] Record ID returned from API

### Integration

- [ ] Quick Create form closes after save
- [ ] Grid refreshes automatically
- [ ] New document appears in grid
- [ ] SharePoint URL is clickable
- [ ] Click URL opens file in browser

### Error Handling

- [ ] Missing container ID → error message
- [ ] Upload fails → error message shown
- [ ] Record creation fails → error message shown
- [ ] Network timeout → error message shown
- [ ] Invalid file type → error message shown
- [ ] File too large (>250 MB) → error message shown

---

## Common Issues & Solutions

### Issue 1: Container ID Not Found

**Symptom**: Error "Container ID not found - cannot upload file to SharePoint"

**Causes**:
- Matter doesn't have container ID (not created in SPE yet)
- Container ID field not in default value mappings
- Parent record data didn't load

**Solution**:
```typescript
// Check container ID before upload
const containerId = formData.sprk_containerid ||
                   this.parentRecordData?.sprk_containerid;

if (!containerId) {
    // Show user-friendly error
    throw new Error('This Matter does not have a SharePoint container. Please create a container first.');
}
```

### Issue 2: File Upload Succeeds but Record Creation Fails

**Symptom**: File in SPE but no Dataverse record

**Causes**:
- Permission error creating Dataverse record
- Invalid field values
- Network timeout

**Solution**:
```typescript
// Store SPE metadata for retry
if (uploadResult.success) {
    this.lastUploadMetadata = uploadResult.speMetadata;
}

// On retry, skip upload if we have metadata
if (this.lastUploadMetadata) {
    speMetadata = this.lastUploadMetadata;
} else {
    // Upload file
}
```

### Issue 3: Form Doesn't Close After Save

**Symptom**: Form stays open after successful save

**Cause**: Power Apps expects specific return value or event

**Solution**:
```typescript
// Ensure Promise resolves (don't throw error)
await onSave(formData, file);

// Power Apps should close form automatically
// If not, may need to call context API to close
```

---

## Performance Considerations

### Large File Uploads

For files >10 MB, consider:

1. **Chunked Upload**: Split file into chunks, upload sequentially
2. **Resume on Failure**: Save progress, allow resume
3. **Background Upload**: Use Web Workers (future enhancement)

**Current Approach**: Simple upload with progress indicator (acceptable for <100 MB files)

### Bundle Size Impact

New dependencies:
- FileUploadService: ~5 KB
- DataverseRecordService: ~3 KB
- SDAP API Client: Already counted from Sprint 7A

**Total Bundle Size**: Still <400 KB target

---

## Success Metrics

- ✅ File upload success rate >99%
- ✅ Upload time <2s for <1 MB files
- ✅ Upload time <10s for <10 MB files
- ✅ Record creation time <1s
- ✅ Total save time (upload + create) <15s for typical files
- ✅ Error messages clear and actionable
- ✅ Zero data loss (file orphaned without record)

---

## Next Steps

After completing this task:

1. **Task 3**: Configurable Default Value Mappings
   - Make field rendering fully dynamic
   - Support multiple entity types
   - Document configuration patterns

2. **Task 4**: Testing, Bundle Size & Deployment
   - Integration tests
   - Manual testing checklist
   - Production deployment

---

## References

- **Master Resource**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)
- **Sprint 7B Task 1**: [TASK-7B-1-QUICK-CREATE-SETUP.md](TASK-7B-1-QUICK-CREATE-SETUP.md)
- **Sprint 7A Task 1**: [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - SDAP API Client
- **SDAP API Docs**: [../../Spe.Bff.Api/SPRINT-4-BFF-API-SPEC.md](../../Spe.Bff.Api/SPRINT-4-BFF-API-SPEC.md)

---

**AI Coding Prompt**:

```
Implement file upload to SharePoint Embedded in the Universal Quick Create PCF control:

1. Create FileUploadService that:
   - Uses SDAP API client from Sprint 7A
   - Uploads file with progress tracking
   - Returns SPE metadata (URL, item ID, size, dates)
   - Handles errors gracefully

2. Create DataverseRecordService that:
   - Creates Dataverse record via context.webAPI.createRecord()
   - Combines form data + SPE metadata
   - Adds parent relationship using OData bind syntax
   - Handles different entity types

3. Update UniversalQuickCreatePCF.handleSave() to:
   - Upload file to SPE (if provided)
   - Create Dataverse record with all metadata
   - Show progress indicator
   - Handle errors with user-friendly messages
   - Close form on success

4. Update QuickCreateForm.tsx to:
   - Add upload progress state
   - Show ProgressBar during upload
   - Display upload percentage in loading overlay
   - Show errors in red text

Key technical requirements:
- Reuse SDAP API client from Sprint 7A Task 1
- Use logger for all operations
- Handle errors at each step (upload, create)
- Progress callback for upload status
- Auto-close form on success (Power Apps handles this)

Refer to SPRINT-7-MASTER-RESOURCE.md for field mappings and SPE metadata structure.
```
