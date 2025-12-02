# Phase 4: PCF Control Integration

**Status**: 🔲 Not Started
**Duration**: 1 day
**Prerequisites**: Phase 3 complete, Custom APIs deployed and tested

---

## Phase Objectives

Update Universal Dataset Grid PCF control to use Custom API Proxy instead of direct Spe.Bff.Api calls:
- Replace SdapApiClientFactory with Custom API-based client
- Update command handlers to use new API client
- Add proper error handling and user feedback
- Update TypeScript type definitions
- Test all file operations end-to-end

---

## Context for AI Vibe Coding

### What We're Changing

**BEFORE (Sprint 7A)**:
```typescript
// PCF tries to get user token (fails)
const token = await getTokenFromContext(context);

// PCF calls Spe.Bff.Api directly (blocked by auth)
const response = await fetch('https://spe-api.../api/files/download', {
    headers: { 'Authorization': `Bearer ${token}` }
});
```

**AFTER (Sprint 8)**:
```typescript
// PCF calls Dataverse Custom API (implicit auth)
const response = await context.webAPI.execute({
    name: 'sprk_ProxyDownloadFile',
    parameters: { DocumentId: '...', FileId: '...' }
});

// Response contains Base64 file content
const blob = base64ToBlob(response.FileContent);
```

### Key Changes
1. **Remove token acquisition logic** - No longer needed
2. **Use context.webAPI.execute()** - Dataverse handles auth
3. **Handle Base64 encoding/decoding** - Files transferred as Base64
4. **Better error messages** - Map Dataverse errors to user-friendly messages

---

## Task Breakdown

### Task 4.1: Create Custom API Type Definitions

**Objective**: Define TypeScript types for Custom API requests and responses.

**AI Instructions**:

Create file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/customApi.ts`

```typescript
/**
 * Type definitions for Dataverse Custom API Proxy operations.
 */

/**
 * Base response interface for all Custom API operations.
 */
export interface CustomApiResponse {
    StatusCode: number;
    ErrorMessage?: string;
}

/**
 * Request for downloading a file.
 */
export interface ProxyDownloadFileRequest {
    DocumentId: string;
    FileId?: string;
    DownloadUrl?: string;
}

/**
 * Response from download file operation.
 */
export interface ProxyDownloadFileResponse extends CustomApiResponse {
    FileContent: string; // Base64 encoded
    FileName: string;
    ContentType: string;
}

/**
 * Request for deleting a file.
 */
export interface ProxyDeleteFileRequest {
    DocumentId: string;
    FileId: string;
}

/**
 * Response from delete file operation.
 */
export interface ProxyDeleteFileResponse extends CustomApiResponse {
    Success: boolean;
}

/**
 * Request for replacing a file.
 */
export interface ProxyReplaceFileRequest {
    DocumentId: string;
    FileId: string;
    FileContent: string; // Base64 encoded
    FileName: string;
    ContentType?: string;
}

/**
 * Response from replace file operation.
 */
export interface ProxyReplaceFileResponse extends CustomApiResponse {
    Success: boolean;
    NewFileId?: string;
}

/**
 * Request for uploading a file.
 */
export interface ProxyUploadFileRequest {
    DocumentId: string;
    FileContent: string; // Base64 encoded
    FileName: string;
    ContentType?: string;
}

/**
 * Response from upload file operation.
 */
export interface ProxyUploadFileResponse extends CustomApiResponse {
    FileId: string;
    DownloadUrl: string;
}

/**
 * Error response from Custom API.
 */
export interface CustomApiError {
    message: string;
    statusCode?: number;
    innerError?: string;
}
```

**Validation**:
- Types compile without errors
- All request/response interfaces defined
- Matches Custom API parameter names exactly

---

### Task 4.2: Create Custom API Client

**Objective**: Create new client class that uses context.webAPI instead of direct HTTP calls.

**AI Instructions**:

Create file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/CustomApiClient.ts`

```typescript
import { IInputs } from '../generated/ManifestTypes';
import {
    ProxyDownloadFileRequest,
    ProxyDownloadFileResponse,
    ProxyDeleteFileRequest,
    ProxyDeleteFileResponse,
    ProxyReplaceFileRequest,
    ProxyReplaceFileResponse,
    ProxyUploadFileRequest,
    ProxyUploadFileResponse,
    CustomApiError
} from '../types/customApi';

/**
 * Client for calling Dataverse Custom API Proxy operations.
 * Uses context.webAPI.execute() for implicit authentication.
 */
export class CustomApiClient {
    private context: ComponentFramework.Context<IInputs>;

    constructor(context: ComponentFramework.Context<IInputs>) {
        this.context = context;
    }

    /**
     * Download file from SharePoint Embedded via Custom API Proxy.
     * @param request Download file request
     * @returns Blob containing file content
     */
    async downloadFile(request: ProxyDownloadFileRequest): Promise<Blob> {
        console.log('[CustomApiClient] Downloading file', request);

        try {
            // Call Custom API via Dataverse
            const response = await this.executeCustomApi<ProxyDownloadFileResponse>(
                'sprk_ProxyDownloadFile',
                request
            );

            console.log('[CustomApiClient] Download successful', {
                fileName: response.FileName,
                contentType: response.ContentType,
                statusCode: response.StatusCode
            });

            // Convert Base64 to Blob
            const blob = this.base64ToBlob(response.FileContent, response.ContentType);

            return blob;
        } catch (error) {
            console.error('[CustomApiClient] Download failed', error);
            throw this.mapError(error, 'Failed to download file');
        }
    }

    /**
     * Delete file from SharePoint Embedded via Custom API Proxy.
     * @param request Delete file request
     */
    async deleteFile(request: ProxyDeleteFileRequest): Promise<void> {
        console.log('[CustomApiClient] Deleting file', request);

        try {
            const response = await this.executeCustomApi<ProxyDeleteFileResponse>(
                'sprk_ProxyDeleteFile',
                request
            );

            if (!response.Success) {
                throw new Error(response.ErrorMessage || 'Delete operation failed');
            }

            console.log('[CustomApiClient] Delete successful');
        } catch (error) {
            console.error('[CustomApiClient] Delete failed', error);
            throw this.mapError(error, 'Failed to delete file');
        }
    }

    /**
     * Replace file in SharePoint Embedded via Custom API Proxy.
     * @param request Replace file request
     */
    async replaceFile(request: ProxyReplaceFileRequest): Promise<void> {
        console.log('[CustomApiClient] Replacing file', {
            documentId: request.DocumentId,
            fileId: request.FileId,
            fileName: request.FileName
        });

        try {
            const response = await this.executeCustomApi<ProxyReplaceFileResponse>(
                'sprk_ProxyReplaceFile',
                request
            );

            if (!response.Success) {
                throw new Error(response.ErrorMessage || 'Replace operation failed');
            }

            console.log('[CustomApiClient] Replace successful');
        } catch (error) {
            console.error('[CustomApiClient] Replace failed', error);
            throw this.mapError(error, 'Failed to replace file');
        }
    }

    /**
     * Upload file to SharePoint Embedded via Custom API Proxy.
     * @param request Upload file request
     * @returns File ID and download URL
     */
    async uploadFile(request: ProxyUploadFileRequest): Promise<{ fileId: string; downloadUrl: string }> {
        console.log('[CustomApiClient] Uploading file', {
            documentId: request.DocumentId,
            fileName: request.FileName
        });

        try {
            const response = await this.executeCustomApi<ProxyUploadFileResponse>(
                'sprk_ProxyUploadFile',
                request
            );

            console.log('[CustomApiClient] Upload successful', {
                fileId: response.FileId,
                downloadUrl: response.DownloadUrl
            });

            return {
                fileId: response.FileId,
                downloadUrl: response.DownloadUrl
            };
        } catch (error) {
            console.error('[CustomApiClient] Upload failed', error);
            throw this.mapError(error, 'Failed to upload file');
        }
    }

    /**
     * Execute Custom API via context.webAPI.execute().
     * @param customApiName Name of Custom API (e.g., 'sprk_ProxyDownloadFile')
     * @param parameters Request parameters
     * @returns Response from Custom API
     */
    private async executeCustomApi<TResponse>(
        customApiName: string,
        parameters: Record<string, any>
    ): Promise<TResponse> {
        try {
            // Call Custom API using PCF framework method
            const executeRequest = {
                getMetadata: () => ({
                    boundParameter: null,
                    operationType: 0, // This is an Action
                    operationName: customApiName,
                    parameterTypes: {}
                }),
                ...parameters
            };

            const response = await this.context.webAPI.execute(executeRequest);

            return response as TResponse;
        } catch (error: any) {
            console.error('[CustomApiClient] Custom API execution failed', {
                customApiName,
                error
            });

            // Enhance error with more context
            throw {
                message: error.message || 'Custom API execution failed',
                statusCode: error.statusCode,
                innerError: error.innerError || error.toString()
            } as CustomApiError;
        }
    }

    /**
     * Convert Base64 string to Blob.
     * @param base64 Base64 encoded string
     * @param contentType MIME type
     * @returns Blob
     */
    private base64ToBlob(base64: string, contentType: string): Blob {
        try {
            const byteCharacters = atob(base64);
            const byteNumbers = new Array(byteCharacters.length);

            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }

            const byteArray = new Uint8Array(byteNumbers);
            return new Blob([byteArray], { type: contentType });
        } catch (error) {
            console.error('[CustomApiClient] Failed to convert Base64 to Blob', error);
            throw new Error('Failed to decode file content');
        }
    }

    /**
     * Convert File to Base64 string.
     * @param file File object
     * @returns Promise<string> Base64 encoded string
     */
    static async fileToBase64(file: File): Promise<string> {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();

            reader.onload = () => {
                const dataUrl = reader.result as string;
                // Remove data URL prefix (e.g., "data:application/pdf;base64,")
                const base64 = dataUrl.split(',')[1];
                resolve(base64);
            };

            reader.onerror = () => {
                reject(new Error('Failed to read file'));
            };

            reader.readAsDataURL(file);
        });
    }

    /**
     * Map error to user-friendly message.
     * @param error Error object
     * @param defaultMessage Default error message
     * @returns Error
     */
    private mapError(error: any, defaultMessage: string): Error {
        if (error && typeof error === 'object') {
            const apiError = error as CustomApiError;

            // Map common error codes to user-friendly messages
            if (apiError.statusCode === 404) {
                return new Error('File not found');
            } else if (apiError.statusCode === 403) {
                return new Error('You do not have permission to access this file');
            } else if (apiError.statusCode === 500) {
                return new Error('Server error occurred. Please try again later.');
            } else if (apiError.message) {
                return new Error(apiError.message);
            }
        }

        return new Error(defaultMessage);
    }
}
```

**Validation**:
- Code compiles without errors
- All methods properly typed
- Error handling comprehensive
- Base64 conversion methods tested

---

### Task 4.3: Update Command Handlers

**Objective**: Update command handlers in UniversalDatasetGridRoot to use CustomApiClient.

**AI Instructions**:

Update file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**Changes needed**:

1. **Import CustomApiClient**:
```typescript
import { CustomApiClient } from '../services/CustomApiClient';
```

2. **Create CustomApiClient instance** in component:
```typescript
const [apiClient] = useState(() => new CustomApiClient(props.context));
```

3. **Update handleDownloadFile**:
```typescript
const handleDownloadFile = async () => {
    if (selectedRecordIds.length === 0) {
        setErrorMessage('Please select a file to download');
        return;
    }

    const selectedRecord = selectedRecords[0];
    const documentId = selectedRecord.sprk_documentid;
    const fileId = selectedRecord.sprk_fileid;
    const downloadUrl = selectedRecord.sprk_downloadurl;
    const fileName = selectedRecord.sprk_filename;

    setIsLoading(true);
    setErrorMessage(null);

    try {
        console.log('[UniversalDatasetGridRoot] Downloading file', { documentId, fileId });

        // Call Custom API Proxy
        const blob = await apiClient.downloadFile({
            DocumentId: documentId,
            FileId: fileId,
            DownloadUrl: downloadUrl
        });

        // Trigger browser download
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName || 'download';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);

        console.log('[UniversalDatasetGridRoot] Download successful');
    } catch (error: any) {
        console.error('[UniversalDatasetGridRoot] Download failed', error);
        setErrorMessage(error.message || 'Failed to download file');
    } finally {
        setIsLoading(false);
    }
};
```

4. **Update handleDeleteFile**:
```typescript
const handleDeleteFile = async () => {
    if (selectedRecordIds.length === 0) {
        setErrorMessage('Please select a file to delete');
        return;
    }

    const selectedRecord = selectedRecords[0];
    const documentId = selectedRecord.sprk_documentid;
    const fileId = selectedRecord.sprk_fileid;

    if (!confirm(`Are you sure you want to delete ${selectedRecord.sprk_filename}?`)) {
        return;
    }

    setIsLoading(true);
    setErrorMessage(null);

    try {
        console.log('[UniversalDatasetGridRoot] Deleting file', { documentId, fileId });

        await apiClient.deleteFile({
            DocumentId: documentId,
            FileId: fileId
        });

        console.log('[UniversalDatasetGridRoot] Delete successful');

        // Refresh grid
        props.onRefresh?.();
    } catch (error: any) {
        console.error('[UniversalDatasetGridRoot] Delete failed', error);
        setErrorMessage(error.message || 'Failed to delete file');
    } finally {
        setIsLoading(false);
    }
};
```

5. **Update handleReplaceFile**:
```typescript
const handleReplaceFile = async () => {
    if (selectedRecordIds.length === 0) {
        setErrorMessage('Please select a file to replace');
        return;
    }

    const selectedRecord = selectedRecords[0];
    const documentId = selectedRecord.sprk_documentid;
    const fileId = selectedRecord.sprk_fileid;

    // Open file picker
    const input = document.createElement('input');
    input.type = 'file';
    input.onchange = async (e: Event) => {
        const target = e.target as HTMLInputElement;
        const file = target.files?.[0];

        if (!file) return;

        setIsLoading(true);
        setErrorMessage(null);

        try {
            console.log('[UniversalDatasetGridRoot] Replacing file', { documentId, fileId, newFileName: file.name });

            // Convert file to Base64
            const base64Content = await CustomApiClient.fileToBase64(file);

            await apiClient.replaceFile({
                DocumentId: documentId,
                FileId: fileId,
                FileContent: base64Content,
                FileName: file.name,
                ContentType: file.type
            });

            console.log('[UniversalDatasetGridRoot] Replace successful');

            // Refresh grid
            props.onRefresh?.();
        } catch (error: any) {
            console.error('[UniversalDatasetGridRoot] Replace failed', error);
            setErrorMessage(error.message || 'Failed to replace file');
        } finally {
            setIsLoading(false);
        }
    };

    input.click();
};
```

6. **Update handleUploadFile**:
```typescript
const handleUploadFile = async () => {
    if (selectedRecordIds.length === 0) {
        setErrorMessage('Please select a document to attach a file');
        return;
    }

    const selectedRecord = selectedRecords[0];
    const documentId = selectedRecord.sprk_documentid;

    // Open file picker
    const input = document.createElement('input');
    input.type = 'file';
    input.onchange = async (e: Event) => {
        const target = e.target as HTMLInputElement;
        const file = target.files?.[0];

        if (!file) return;

        setIsLoading(true);
        setErrorMessage(null);

        try {
            console.log('[UniversalDatasetGridRoot] Uploading file', { documentId, fileName: file.name });

            // Convert file to Base64
            const base64Content = await CustomApiClient.fileToBase64(file);

            const result = await apiClient.uploadFile({
                DocumentId: documentId,
                FileContent: base64Content,
                FileName: file.name,
                ContentType: file.type
            });

            console.log('[UniversalDatasetGridRoot] Upload successful', result);

            // Refresh grid
            props.onRefresh?.();
        } catch (error: any) {
            console.error('[UniversalDatasetGridRoot] Upload failed', error);
            setErrorMessage(error.message || 'Failed to upload file');
        } finally {
            setIsLoading(false);
        }
    };

    input.click();
};
```

**Validation**:
- All command handlers updated
- Error messages user-friendly
- Loading states handled
- Grid refreshes after mutations

---

### Task 4.4: Remove Old API Client

**Objective**: Remove or deprecate old SdapApiClientFactory that used direct HTTP calls.

**AI Instructions**:

1. **Archive old client**:
```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services
mv SdapApiClientFactory.ts SdapApiClientFactory.ts.deprecated
```

2. **Remove imports** from components that used old client

3. **Update index.ts** to export new client:
```typescript
export { CustomApiClient } from './CustomApiClient';
```

**Validation**:
- Old client not imported anywhere
- Code compiles without errors
- No references to token acquisition

---

### Task 4.5: Update Configuration

**Objective**: Remove SDAP API configuration from types/index.ts since we no longer call API directly.

**AI Instructions**:

Update file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts`

**Remove**:
```typescript
sdapConfig: {
    baseUrl: string;
    timeout: number;
}
```

This is no longer needed because Custom API Proxy handles the external API connection.

**Validation**:
- Configuration removed
- No references to sdapConfig in code

---

### Task 4.6: Add Error Notification Component

**Objective**: Add user-friendly error notification banner.

**AI Instructions**:

Create file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/ErrorNotification.tsx`

```typescript
import * as React from 'react';
import {
    MessageBar,
    MessageBarType,
    MessageBarBody,
    Button
} from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';

export interface ErrorNotificationProps {
    message: string | null;
    onDismiss: () => void;
}

export const ErrorNotification: React.FC<ErrorNotificationProps> = ({ message, onDismiss }) => {
    if (!message) return null;

    return (
        <MessageBar
            intent="error"
            style={{ marginBottom: '16px' }}
        >
            <MessageBarBody>
                {message}
            </MessageBarBody>
            <Button
                appearance="transparent"
                icon={<Dismiss24Regular />}
                onClick={onDismiss}
                aria-label="Dismiss error"
            />
        </MessageBar>
    );
};
```

**Integrate into UniversalDatasetGridRoot**:

```typescript
import { ErrorNotification } from './ErrorNotification';

// In render:
<FluentProvider theme={theme}>
    <ErrorNotification
        message={errorMessage}
        onDismiss={() => setErrorMessage(null)}
    />
    {/* Rest of grid UI */}
</FluentProvider>
```

**Validation**:
- Error notification displays when error occurs
- User can dismiss notification
- Styling matches Fluent UI theme

---

### Task 4.7: Update ControlManifest Version

**Objective**: Increment version to track Custom API integration.

**AI Instructions**:

Update file: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/ControlManifest.Input.xml`

```xml
<control namespace="Spaarke.UI.Components"
         constructor="UniversalDatasetGrid"
         version="2.1.0"
         display-name-key="Universal Dataset Grid"
         description-key="Document management grid with Custom API Proxy integration"
         control-type="standard">
```

**Also update version indicator in component**:

Update `UniversalDatasetGridRoot.tsx`:
```typescript
<div style={{
    position: 'absolute',
    bottom: '2px',
    right: '5px',
    fontSize: '8px',
    color: '#666',
    userSelect: 'none',
    pointerEvents: 'none',
    zIndex: 1000
}}>
    v2.1.0
</div>
```

**Validation**:
- Version updated in manifest
- Version indicator updated in UI

---

### Task 4.8: Build and Test

**Objective**: Build PCF control and verify it compiles without errors.

**AI Instructions**:

1. **Clean build**:
```bash
cd src/controls/UniversalDatasetGrid
npm run clean
```

2. **Install dependencies** (if any new):
```bash
npm install
```

3. **Build control**:
```bash
npm run build
```

4. **Build production** (for deployment):
```bash
npm run build:prod
```

5. **Run tests** (if available):
```bash
npm test
```

**Expected Output**:
- Build succeeds without errors
- Bundle size reasonable (< 600 KB)
- No TypeScript compilation errors
- No ESLint errors

**Validation**:
- Control builds successfully
- No console errors during build
- Bundle contains all necessary files

---

## Deliverables

✅ CustomApiClient implemented with all file operations
✅ TypeScript type definitions for Custom API requests/responses
✅ Command handlers updated to use CustomApiClient
✅ Old SdapApiClientFactory removed
✅ Configuration cleaned up
✅ Error notification component added
✅ Control version updated to 2.1.0
✅ Control builds without errors

---

## Validation Checklist

- [ ] CustomApiClient compiles without errors
- [ ] All command handlers updated
- [ ] No references to old token acquisition logic
- [ ] Error handling comprehensive
- [ ] Error messages user-friendly
- [ ] Loading states work correctly
- [ ] Control builds successfully
- [ ] Bundle size < 600 KB
- [ ] Version indicator shows v2.1.0

---

## Next Steps

Proceed to **Phase 5: Deployment and Testing**

**Phase 5 will**:
- Deploy updated PCF control to spaarkedev1
- Deploy Custom API Proxy solution
- Configure external service
- Test all file operations end-to-end
- Verify error handling and user feedback

---

## Knowledge Resources

### Internal Documentation
- [Phase 3 Proxy Implementation](./PHASE-3-PROXY-IMPLEMENTATION.md)
- [PCF Control Standards](../../../docs/KM-PCF-CONTROL-STANDARDS.md)
- [Sprint 6 PCF Deployment](../Sprint%206/TASK-2-DEPLOYMENT-COMPLETE.md)

### External Resources
- [PCF WebAPI Reference](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/webapi)
- [PCF Execute Method](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/webapi/execute)
- [React Hooks Best Practices](https://react.dev/reference/react)
- [Fluent UI React v9](https://react.fluentui.dev/)

---

## Notes for AI Vibe Coding

**Key Implementation Details**:

1. **context.webAPI.execute() Usage**:
   - Requires `getMetadata()` function
   - `operationType: 0` for Actions
   - Parameter names must match Custom API exactly (case-sensitive)

2. **Base64 Encoding/Decoding**:
   - Use `atob()` to decode Base64
   - Use `btoa()` to encode (or FileReader for files)
   - Watch for special characters and binary data

3. **Error Handling**:
   - Custom API errors have specific structure
   - Map HTTP status codes to user-friendly messages
   - Always show error to user (don't fail silently)

4. **File Operations**:
   - Dispose Blob URLs after use (`URL.revokeObjectURL()`)
   - Handle large files gracefully (show progress if possible)
   - Validate file types if needed

5. **State Management**:
   - Use useState for loading and error states
   - Clear errors when operation succeeds
   - Refresh grid after mutations (delete, replace, upload)
