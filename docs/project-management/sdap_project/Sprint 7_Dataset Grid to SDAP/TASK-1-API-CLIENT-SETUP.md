# Task 1: SDAP API Client Service Setup

**Estimated Time**: 1-2 days
**Status**: Pending
**Prerequisites**: All met ✅

---

## AI Coding Prompt

> Create a TypeScript API client for the SDAP BFF API that integrates with the Universal Dataset Grid PCF control. The client should provide type-safe access to all SDAP endpoints, handle authentication using PCF context, implement comprehensive error handling, and integrate with the existing logger utility.

---

## Objective

Create a production-ready TypeScript API client service that:
1. Provides type-safe methods for all SDAP BFF API endpoints
2. Retrieves user authentication tokens from PCF context
3. Handles HTTP requests/responses with proper error handling
4. Integrates with existing logger utility for debugging
5. Follows established code patterns from v2.0.7

---

## Context & Knowledge

### What You're Building
A TypeScript service layer that sits between the React components and the SDAP BFF API. This client will be used by all file operation services (upload, download, delete) to communicate with the backend.

### Why This Matters
- **Type Safety**: Catch API contract mismatches at compile time
- **Authentication**: Centralize token management from PCF context
- **Error Handling**: Consistent error handling across all API calls
- **Maintainability**: Single source of truth for API integration

### Existing Architecture (Reference)
- **Location**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/`
- **Logger**: `utils/logger.ts` - Use for all logging
- **PCF Context**: Available in all components via props
- **React Version**: 18.2.0 (do NOT use React features in services)
- **Build**: TypeScript strict mode enabled

### API Endpoints Summary
See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#sdap-api-endpoints-reference) for full endpoint specifications.

**Document CRUD**:
- `POST /api/v1/documents` - Create document record
- `GET /api/v1/documents/{id}` - Get document
- `PUT /api/v1/documents/{id}` - Update document
- `DELETE /api/v1/documents/{id}` - Delete document

**File Operations**:
- `PUT /api/containers/{containerId}/files/{path}` - Upload file
- `GET /api/containers/{containerId}/files/{path}` - Download file
- `DELETE /api/containers/{containerId}/files/{path}` - Delete file
- `POST /api/containers/{containerId}/upload` - Create upload session (large files)

---

## Implementation Steps

### Step 1: Create API Client Service

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts`

**Requirements**:
- Export TypeScript interfaces for all request/response models
- Export `SdapApiClient` class with methods for each endpoint
- Use `logger` for all operations (info, debug, error)
- Handle all HTTP status codes appropriately
- Return typed responses using TypeScript generics
- Support both JSON and binary (File/Blob) request bodies

**Key Methods to Implement**:
```typescript
class SdapApiClient {
    // Document CRUD
    async createDocument(request: CreateDocumentRequest): Promise<DocumentResponse>
    async getDocument(id: string): Promise<DocumentResponse>
    async updateDocument(id: string, request: UpdateDocumentRequest): Promise<void>
    async deleteDocument(id: string): Promise<void>

    // File Operations
    async uploadFile(request: UploadFileRequest): Promise<UploadFileResponse>
    async downloadFile(containerId: string, path: string): Promise<Blob>
    async deleteFile(containerId: string, path: string): Promise<void>
    async createUploadSession(containerId: string, path: string): Promise<{ uploadUrl: string }>
}
```

**Code Pattern**:
```typescript
import { logger } from '../utils/logger';

export class SdapApiClient {
    private baseUrl: string;
    private accessToken: string;

    constructor(baseUrl: string, accessToken: string) {
        this.baseUrl = baseUrl.replace(/\/$/, ''); // Remove trailing slash
        this.accessToken = accessToken;
        logger.info('SdapApiClient', `Initialized with base URL: ${this.baseUrl}`);
    }

    private async fetchApi<T>(endpoint: string, options: RequestInit): Promise<T> {
        const url = `${this.baseUrl}${endpoint}`;

        const headers = {
            'Authorization': `Bearer ${this.accessToken}`,
            'Content-Type': 'application/json',
            ...options.headers
        };

        try {
            const response = await fetch(url, { ...options, headers });

            if (!response.ok) {
                const error = await this.handleErrorResponse(response);
                throw error;
            }

            // Handle 204 No Content
            if (response.status === 204) {
                return undefined as T;
            }

            return await response.json();
        } catch (error) {
            logger.error('SdapApiClient', `API request failed: ${endpoint}`, error);
            throw error;
        }
    }

    private async handleErrorResponse(response: Response): Promise<ApiError> {
        // Parse error response and return structured error
    }
}
```

**Request/Response Models** (TypeScript interfaces):
- `CreateDocumentRequest`
- `UpdateDocumentRequest`
- `DocumentResponse`
- `UploadFileRequest`
- `UploadFileResponse`
- `ApiError`

See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#field-mappings-dataverse--sdap-api) for field details.

---

### Step 2: Create API Client Factory

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts`

**Purpose**: Factory function to create `SdapApiClient` instances from PCF context

**Requirements**:
- Export `createSdapApiClient(context)` function
- Retrieve API base URL from environment or config
- Retrieve user access token from PCF context
- Handle missing token gracefully with clear error message
- Use logger for diagnostics

**Authentication Token Retrieval** (try multiple locations):
```typescript
function getUserAccessToken(context: ComponentFramework.Context<IInputs>): string {
    // Attempt 1: Standard location
    const token = (context as any).userSettings?.accessToken;
    if (token) {
        logger.debug('SdapApiClientFactory', 'Retrieved token from userSettings');
        return token;
    }

    // Attempt 2: Alternative location
    const altToken = (context as any).page?.accessToken;
    if (altToken) {
        logger.debug('SdapApiClientFactory', 'Retrieved token from page');
        return altToken;
    }

    // Fallback: Error
    logger.error('SdapApiClientFactory', 'Could not retrieve access token');
    throw new Error('Unable to retrieve user access token from PCF context');
}
```

**API Base URL Configuration**:
```typescript
function getApiBaseUrl(context: ComponentFramework.Context<IInputs>): string {
    // Option 1: Environment variable (production)
    const envUrl = process.env.SDAP_API_URL;
    if (envUrl) {
        logger.debug('SdapApiClientFactory', `Using API URL from env: ${envUrl}`);
        return envUrl;
    }

    // Option 2: Hardcoded for development
    const defaultUrl = 'https://localhost:7071';
    logger.warn('SdapApiClientFactory', `No SDAP_API_URL env var, using default: ${defaultUrl}`);
    return defaultUrl;
}
```

---

### Step 3: Update Type Definitions

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts`

**Requirements**: Add SDAP-related types to existing file

**Types to Add**:
```typescript
// ==================== SDAP Integration ====================

/**
 * File operation types
 */
export type FileOperationType = 'upload' | 'download' | 'delete' | 'replace';

/**
 * File operation result (used by services)
 */
export interface FileOperationResult {
    success: boolean;
    documentId?: string;
    filePath?: string;
    error?: string;
}

/**
 * Upload progress tracking
 */
export interface UploadProgress {
    loaded: number;
    total: number;
    percentage: number;
}

export type UploadProgressCallback = (progress: UploadProgress) => void;
```

---

## Validation Criteria

Before marking this task complete, verify:

- [ ] `SdapApiClient.ts` compiles without TypeScript errors
- [ ] All 8 API methods implemented (4 document + 4 file operations)
- [ ] All request/response interfaces exported
- [ ] `SdapApiClientFactory.ts` correctly retrieves token from context
- [ ] Logger integrated throughout (info, debug, error)
- [ ] Error handling returns structured `ApiError` objects
- [ ] Types added to `types/index.ts`
- [ ] No React dependencies in service files (pure TypeScript)
- [ ] Code follows existing patterns (see logger usage in `index.ts`)

---

## Testing Instructions

### Quick Test (TypeScript Compilation)
```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npx tsc --noEmit
```

Expected: Zero errors

### Manual Test (After Integration)
1. Import `createSdapApiClient` in `CommandBar.tsx`
2. Call `createSdapApiClient(context)` in a button handler
3. Check browser console for logger output
4. Verify token retrieval works (or fails gracefully)

---

## Expected Outcomes

After completing this task:

✅ **Type-safe API client ready** for all SDAP endpoints
✅ **Authentication configured** via PCF context
✅ **Error handling in place** for all API calls
✅ **Logger integration** for debugging
✅ **Foundation ready** for Tasks 2-5 (file operation services)

---

## Code Reference

### Full Implementation Example

See [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md#11-create-api-client-service) lines 91-446 for complete code.

**Key sections**:
- Request/Response Models: Lines 104-159
- SdapApiClient class: Lines 163-363
- Document CRUD methods: Lines 176-228
- File operation methods: Lines 235-289
- HTTP helpers: Lines 310-362

### Import Statements
```typescript
import { logger } from '../utils/logger';
import { IInputs } from '../generated/ManifestTypes';
```

---

## Troubleshooting

### Issue: "Cannot find module '../utils/logger'"
**Solution**: Verify you're in correct directory:
```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/
```

### Issue: TypeScript error on `context as any`
**Solution**: This is expected - PCF context type definitions don't include all runtime properties

### Issue: "Property 'accessToken' does not exist"
**Solution**: Correct - use type assertion `(context as any).userSettings?.accessToken`

---

## Next Steps

After Task 1 completion:
- **Task 2**: File Upload Integration (uses `SdapApiClient`)
- **Task 3**: File Download Integration (uses `SdapApiClient`)
- **Task 4**: File Delete Integration (uses `SdapApiClient`)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - Full reference
- [SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md) - Complete sprint plan

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 1-2 days
**Status**: Ready to Begin
