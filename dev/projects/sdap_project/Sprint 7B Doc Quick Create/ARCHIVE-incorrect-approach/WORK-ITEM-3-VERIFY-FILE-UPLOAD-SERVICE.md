# Work Item 3: Verify File Upload Service

**Estimated Time:** 30 minutes
**Prerequisites:** Work Item 2 complete (FileUploadPCF.ts created)
**Status:** Ready to Start

---

## Objective

Verify that the existing FileUploadService.ts works correctly for the new field-bound approach.

---

## Context

The FileUploadService.ts was created in Sprint 7B Task 2. It handles:
- File upload to SharePoint Embedded via SDAP API
- MSAL authentication token injection
- Error handling and retry logic
- SPE metadata extraction

**Good News:** This service doesn't need changes! It's already designed to work with any PCF control.

This work item is about **verification only** - confirming the service works as expected.

---

## File to Verify

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/FileUploadService.ts`

---

## Expected Service Interface

```typescript
export interface FileUploadRequest {
    file: File;
    containerId: string;
    fileName?: string;
}

export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}

    /**
     * Upload a file to SharePoint Embedded
     *
     * @param request - File upload request
     * @returns Service result with SPE metadata
     */
    async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
        // Implementation...
    }
}
```

---

## Verification Steps

### Step 1: Read Existing File

```bash
# Navigate to service folder
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/services

# Read file
cat FileUploadService.ts
```

---

### Step 2: Verify Service Method Signature

**Check that uploadFile() method exists and has correct signature:**

```typescript
async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>>
```

**Verify:**
- ✅ Takes `FileUploadRequest` (file, containerId, fileName)
- ✅ Returns `Promise<ServiceResult<SpeFileMetadata>>`
- ✅ Uses SdapApiClient for API calls
- ✅ Handles errors gracefully

---

### Step 3: Verify Request Format

**Check that uploadFile() calls SDAP API correctly:**

```typescript
// Expected API call format:
POST /api/spe/upload
Headers:
    Authorization: Bearer {token}
    Content-Type: multipart/form-data
Body:
    file: {file binary}
    containerId: {containerGuid}
    fileName: {fileName}
```

**Verify:**
- ✅ Uses FormData for file upload
- ✅ Includes containerId in request
- ✅ Includes fileName in request
- ✅ MSAL token automatically injected by SdapApiClient

---

### Step 4: Verify Response Handling

**Check that uploadFile() returns SPE metadata:**

```typescript
// Expected response format:
{
    success: true,
    data: {
        driveItemId: "01ABC...",
        fileName: "contract.pdf",
        fileSize: 2048576,
        sharePointUrl: "https://...",
        webUrl: "https://...",
        createdDateTime: "2025-10-07T12:00:00Z",
        lastModifiedDateTime: "2025-10-07T12:00:00Z"
    }
}
```

**Verify:**
- ✅ Returns ServiceResult wrapper
- ✅ Extracts all SPE metadata fields
- ✅ Handles both success and error cases
- ✅ Logs upload progress

---

### Step 5: Check Error Handling

**Verify error scenarios are handled:**

1. **Missing Container ID:**
   ```typescript
   if (!request.containerId) {
       return {
           success: false,
           error: 'Container ID is required'
       };
   }
   ```

2. **API Errors (401, 403, 404, 500):**
   ```typescript
   try {
       const response = await this.apiClient.post(...);
   } catch (error) {
       return {
           success: false,
           error: error.message
       };
   }
   ```

3. **Network Errors:**
   ```typescript
   catch (error) {
       logger.error('FileUploadService', 'Upload failed', error);
       return { success: false, error: 'Network error' };
   }
   ```

---

### Step 6: Verify Integration with FileUploadPCF

**Check that FileUploadPCF.ts uses service correctly:**

```typescript
// In FileUploadPCF.ts:
const result = await this.fileUploadService.uploadFile({
    file,
    containerId: this.state.containerId,
    fileName: file.name
});

if (result.success && result.data) {
    // SPE metadata available
    const metadata = result.data;
}
```

**Verify:**
- ✅ PCF passes correct parameters
- ✅ PCF handles success/error results
- ✅ PCF collects metadata for getOutputs()

---

## Expected File Content

The FileUploadService.ts should look similar to this:

```typescript
/**
 * File Upload Service
 *
 * Handles file uploads to SharePoint Embedded via SDAP API.
 *
 * @version 1.0.0
 */

import { SdapApiClient } from './SdapApiClient';
import { SpeFileMetadata, ServiceResult } from '../types';
import { logger } from '../utils/logger';

export interface FileUploadRequest {
    file: File;
    containerId: string;
    fileName?: string;
}

export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}

    async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>> {
        try {
            logger.info('FileUploadService', 'Starting file upload', {
                fileName: request.fileName || request.file.name,
                fileSize: request.file.size,
                containerId: request.containerId
            });

            // Validate request
            if (!request.containerId) {
                return {
                    success: false,
                    error: 'Container ID is required'
                };
            }

            // Build FormData
            const formData = new FormData();
            formData.append('file', request.file);
            formData.append('containerId', request.containerId);
            formData.append('fileName', request.fileName || request.file.name);

            // Upload to SDAP API
            const response = await this.apiClient.post<SpeFileMetadata>(
                '/spe/upload',
                formData
            );

            logger.info('FileUploadService', 'File uploaded successfully', {
                fileName: request.fileName,
                driveItemId: response.driveItemId
            });

            return {
                success: true,
                data: response
            };
        } catch (error) {
            logger.error('FileUploadService', 'File upload failed', error);

            return {
                success: false,
                error: error instanceof Error ? error.message : 'File upload failed'
            };
        }
    }
}
```

---

## Manual Testing (Optional)

If you want to manually test the service:

### Test 1: Single File Upload

```typescript
// In browser console (after control loads):
const testFile = new File(['test content'], 'test.txt', { type: 'text/plain' });

const result = await fileUploadService.uploadFile({
    file: testFile,
    containerId: 'your-container-guid',
    fileName: 'test.txt'
});

console.log('Upload result:', result);
// Expected: { success: true, data: { driveItemId: '...', ... } }
```

### Test 2: Multiple Files Upload

```typescript
const files = [
    new File(['content 1'], 'file1.txt', { type: 'text/plain' }),
    new File(['content 2'], 'file2.txt', { type: 'text/plain' })
];

const results = [];
for (const file of files) {
    const result = await fileUploadService.uploadFile({
        file,
        containerId: 'your-container-guid',
        fileName: file.name
    });
    results.push(result);
}

console.log('Upload results:', results);
// Expected: [{ success: true, ... }, { success: true, ... }]
```

### Test 3: Error Handling

```typescript
// Test missing Container ID
const result = await fileUploadService.uploadFile({
    file: testFile,
    containerId: '',  // ❌ Empty
    fileName: 'test.txt'
});

console.log('Error result:', result);
// Expected: { success: false, error: 'Container ID is required' }
```

---

## Verification Checklist

After reviewing FileUploadService.ts, confirm:

- ✅ uploadFile() method exists with correct signature
- ✅ Takes FileUploadRequest (file, containerId, fileName)
- ✅ Returns Promise<ServiceResult<SpeFileMetadata>>
- ✅ Uses SdapApiClient for API calls
- ✅ Builds FormData with file, containerId, fileName
- ✅ Calls POST /api/spe/upload endpoint
- ✅ Extracts SPE metadata from response
- ✅ Handles success and error cases
- ✅ Logs upload progress
- ✅ Returns ServiceResult wrapper

---

## Common Issues

### Issue 1: SdapApiClient Not Injecting Token

**Symptoms:** API returns 401 Unauthorized

**Fix:** Verify SdapApiClient uses MsalAuthProvider:

```typescript
// In SdapApiClient.ts:
const token = await this.authProvider.getAccessToken();
headers.set('Authorization', `Bearer ${token}`);
```

---

### Issue 2: FormData Not Built Correctly

**Symptoms:** API returns 400 Bad Request

**Fix:** Verify FormData includes all fields:

```typescript
const formData = new FormData();
formData.append('file', request.file);  // ✅ File binary
formData.append('containerId', request.containerId);  // ✅ GUID
formData.append('fileName', request.fileName);  // ✅ String
```

---

### Issue 3: SPE Metadata Fields Missing

**Symptoms:** Metadata incomplete or null

**Fix:** Verify SDAP API response includes all fields:

```json
{
    "driveItemId": "required",
    "fileName": "required",
    "fileSize": "required",
    "sharePointUrl": "optional",
    "webUrl": "optional",
    "createdDateTime": "optional",
    "lastModifiedDateTime": "optional"
}
```

---

## Troubleshooting

### Error: Cannot find module './SdapApiClient'

**Cause:** SdapApiClient.ts missing or path incorrect

**Fix:** Verify file exists:
```bash
ls -la /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/services/SdapApiClient.ts
```

---

### Error: SpeFileMetadata type not found

**Cause:** Type definition missing

**Fix:** Verify types/index.ts exports SpeFileMetadata:
```typescript
export interface SpeFileMetadata {
    driveItemId: string;
    fileName: string;
    fileSize: number;
    sharePointUrl?: string;
    webUrl?: string;
    createdDateTime?: string;
    lastModifiedDateTime?: string;
}
```

---

## Expected Result

After verification:

✅ FileUploadService.ts exists and is correct
✅ Service interface matches PCF requirements
✅ No code changes needed (service already works!)
✅ Ready to proceed to Work Item 4 (Multi-File Upload)

---

## Next Steps

After completing this work item:

1. ✅ Verify FileUploadService.ts is correct
2. ✅ No code changes needed
3. ⏳ Move to Work Item 4: Implement Multi-File Upload Logic

---

**Status:** Ready for implementation
**Estimated Time:** 30 minutes
**Next:** Work Item 4 - Multi-File Upload Logic
