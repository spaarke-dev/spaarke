# Task 1.2: Add BffClient.getOfficeUrl() Method

**Phase**: 1 - Core Functionality
**Priority**: High (Blocking)
**Estimated Time**: 30 minutes
**Depends On**: Task 1.1 (TypeScript Interfaces)
**Blocks**: Task 1.3 (FilePreview State)

---

## Objective

Implement `getOfficeUrl()` method in BffClient to call the `/api/documents/{id}/office` BFF endpoint and return Office Online editor URL with user permissions.

## Context & Knowledge Required

### What You Need to Know
1. **Fetch API**: HTTP GET requests with bearer token authentication
2. **Error Handling**: Mapping BFF error codes to user-friendly messages
3. **CORS Configuration**: Cross-origin requests require proper headers
4. **Correlation IDs**: Used for distributed tracing across BFF → Graph API
5. **TypeScript Async/Await**: Promise-based async method patterns

### Files to Review Before Starting
- **BffClient.ts (existing)**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts)
  - Review `getPreviewUrl()` method (~lines 37-81) as a template
  - Review `handleErrorResponse()` method (~lines 93-155) for error mapping
- **types.ts**: [C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts](C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts)
  - Confirm `OfficeUrlResponse` interface (added in Task 1.1)
- **FileAccessEndpoints.cs**: [c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L323-L388)
  - Understand `/office` endpoint response structure

### API Endpoint Details
- **URL**: `GET /api/documents/{documentId}/office`
- **Headers**:
  - `Authorization: Bearer {accessToken}`
  - `X-Correlation-Id: {correlationId}`
  - `Accept: application/json`
- **Response** (200 OK):
  ```json
  {
    "officeUrl": "https://tenant.sharepoint.com/_layouts/15/Doc.aspx?...",
    "permissions": {
      "canEdit": true,
      "canView": true,
      "role": "editor"
    },
    "correlationId": "abc-123-def"
  }
  ```
- **Errors**: Same error codes as `/preview-url` (see `handleErrorResponse()`)

---

## Implementation Prompt

### Step 1: Add getOfficeUrl() Method

**Location**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts`
**Insert After**: `getPreviewUrl()` method (~line 195, after closing brace)

```typescript
/**
 * Get Office Online editor URL for a document
 *
 * Calls: GET /api/documents/{documentId}/office
 *
 * This method requests the Office Online editor URL from the BFF API.
 * The BFF uses On-Behalf-Of (OBO) flow to call Microsoft Graph API
 * with the user's permissions, ensuring security.
 *
 * @param documentId Document GUID (e.g., "550e8400-e29b-41d4-a716-446655440000")
 * @param accessToken Bearer token from MSAL (user's access token)
 * @param correlationId Correlation ID for distributed tracing
 * @returns Promise<OfficeUrlResponse> with editor URL and permissions
 * @throws Error if API call fails (network error, 4xx/5xx response)
 *
 * @example
 * ```typescript
 * const response = await bffClient.getOfficeUrl(
 *     "550e8400-e29b-41d4-a716-446655440000",
 *     accessToken,
 *     "trace-abc-123"
 * );
 * console.log(response.officeUrl); // Office Online URL
 * console.log(response.permissions.canEdit); // true or false
 * ```
 */
public async getOfficeUrl(
    documentId: string,
    accessToken: string,
    correlationId: string
): Promise<OfficeUrlResponse> {
    const url = `${this.baseUrl}/api/documents/${documentId}/office`;

    console.log(`[BffClient] GET ${url} (Office Editor)`);
    console.log(`[BffClient] Correlation ID: ${correlationId}`);

    try {
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${accessToken}`,
                'X-Correlation-Id': correlationId,
                'Accept': 'application/json'
            },
            mode: 'cors', // Required for cross-origin BFF calls
            credentials: 'omit' // Don't send cookies (JWT only)
        });

        // Handle non-2xx responses
        if (!response.ok) {
            await this.handleErrorResponse(response, correlationId);
        }

        // Parse successful response
        const data = await response.json() as OfficeUrlResponse;

        console.log(`[BffClient] Office URL acquired for document`);
        console.log(`[BffClient] Can Edit: ${data.permissions.canEdit}`);
        console.log(`[BffClient] User Role: ${data.permissions.role}`);

        // Verify correlation ID round-trip
        if (data.correlationId !== correlationId) {
            console.warn(
                `[BffClient] Correlation ID mismatch! Sent: ${correlationId}, Received: ${data.correlationId}`
            );
        }

        return data;

    } catch (error) {
        // Network errors, JSON parse errors, etc.
        console.error('[BffClient] Request failed:', error);
        throw new Error(
            `Failed to get Office URL: ${error instanceof Error ? error.message : String(error)}`
        );
    }
}
```

### Step 2: Verify Import Statement

**Location**: Top of BffClient.ts (~line 10)

**Ensure this import exists**:
```typescript
import { FilePreviewResponse, BffErrorResponse, OfficeUrlResponse } from './types';
```

If `OfficeUrlResponse` is not imported, add it to the existing import statement.

---

## Validation & Review

### Pre-Commit Checklist

1. **TypeScript Compilation**:
   ```bash
   cd C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer
   npm run build
   ```
   - [ ] No TypeScript errors
   - [ ] No type checking warnings
   - [ ] `OfficeUrlResponse` type is recognized

2. **Code Review**:
   ```bash
   # Review the changes
   git diff C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/BffClient.ts
   ```
   - [ ] Method signature matches `getPreviewUrl()` pattern
   - [ ] Error handling uses `handleErrorResponse()` (reused)
   - [ ] Console logging includes correlation ID
   - [ ] JSDoc comments are complete

3. **Method Alignment**:
   - [ ] Method is `public async`
   - [ ] Returns `Promise<OfficeUrlResponse>`
   - [ ] Parameters match call site expectations (documentId, accessToken, correlationId)
   - [ ] Follows existing code style (2-space indent, single quotes)

4. **Error Handling**:
   - [ ] Network errors caught and re-thrown with context
   - [ ] Non-200 responses call `handleErrorResponse()`
   - [ ] JSON parse errors handled in catch block

---

## Testing

### Manual API Test (Optional)

Test the endpoint directly before integrating with UI:

```typescript
// Add to FilePreview.tsx componentDidMount temporarily for testing
async testOfficeUrl() {
    const { documentId, accessToken, correlationId } = this.props;

    try {
        const response = await this.bffClient.getOfficeUrl(
            documentId,
            accessToken,
            correlationId
        );

        console.log('Office URL Test Result:', response);
        console.log('Can Edit:', response.permissions.canEdit);
    } catch (error) {
        console.error('Office URL Test Failed:', error);
    }
}
```

**Remove this test code after verification!**

### Verification Steps

1. **Build succeeds**:
   ```bash
   npm run build
   ```

2. **Method exists in bundle**:
   ```bash
   grep -n "getOfficeUrl" C:/code_files/spaarke/src/controls/SpeFileViewer/out/bundle.js
   ```
   - [ ] Method name found in bundle

3. **Types are correct**:
   ```bash
   # Check TypeScript declaration file
   cat C:/code_files/spaarke/src/controls/SpeFileViewer/out/BffClient.d.ts | grep -A 5 "getOfficeUrl"
   ```

---

## Acceptance Criteria

- [x] `getOfficeUrl()` method added to BffClient class
- [x] Method signature: `async getOfficeUrl(documentId, accessToken, correlationId): Promise<OfficeUrlResponse>`
- [x] HTTP request uses `fetch()` with proper headers (Authorization, X-Correlation-Id, Accept)
- [x] Error handling reuses `handleErrorResponse()` method
- [x] Console logging includes correlation ID and permission details
- [x] TypeScript compiles without errors
- [x] JSDoc comments explain parameters, return value, and example usage
- [x] Import statement includes `OfficeUrlResponse` type

---

## Common Issues & Solutions

### Issue 1: TypeScript Error - "Cannot find name 'OfficeUrlResponse'"
**Symptom**: Build fails with type error

**Root Cause**: Task 1.1 not completed or import statement missing

**Solution**:
```bash
# Verify Task 1.1 is complete
grep "OfficeUrlResponse" C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/types.ts

# Verify import statement
grep "import.*OfficeUrlResponse" C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer/BffClient.ts
```

### Issue 2: Runtime Error - "handleErrorResponse is not a function"
**Symptom**: Error when API call fails

**Root Cause**: Method call missing `await` or `this` keyword

**Solution**: Ensure line reads:
```typescript
await this.handleErrorResponse(response, correlationId);
```

### Issue 3: CORS Error in Browser Console
**Symptom**: "Access to fetch... has been blocked by CORS policy"

**Root Cause**: BFF API CORS configuration may need updating (unlikely)

**Solution**: Verify BFF API allows cross-origin requests from Dataverse. Check [Program.cs CORS configuration](c:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs).

---

## Dependencies

### Required Before This Task
- ✅ Task 1.1: TypeScript Interfaces (OfficeUrlResponse must exist)

### Required After This Task
- Task 1.3: FilePreview state will call this method

---

## Files Modified

- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts`

---

## Estimated Effort Breakdown

| Activity | Time |
|----------|------|
| Review existing getPreviewUrl() method | 5 min |
| Write getOfficeUrl() method | 10 min |
| Add JSDoc comments | 5 min |
| Update import statement | 2 min |
| Verify TypeScript compilation | 3 min |
| Test with manual API call (optional) | 5 min |
| **Total** | **30 min** |

---

## Error Code Reference

The `handleErrorResponse()` method maps these BFF error codes to user-friendly messages:

| Error Code | HTTP Status | User Message |
|------------|-------------|--------------|
| `invalid_id` | 400 | "Invalid document ID format. Please contact support." |
| `document_not_found` | 404 | "Document not found. It may have been deleted." |
| `mapping_missing_drive` | 409 | "This file is still initializing. Please try again in a moment." |
| `mapping_missing_item` | 409 | "This file is still initializing. Please try again in a moment." |
| `storage_not_found` | 404 | "File has been removed from storage. Contact your administrator." |
| `throttled_retry` | 429 | "Service is temporarily busy. Please try again in a few seconds." |
| Generic 401 | 401 | "Authentication failed. Please refresh the page." |
| Generic 403 | 403 | "You do not have permission to access this file." |
| Generic 5xx | 500+ | "Server error ({status}). Please try again later. Correlation ID: {id}" |

**Note**: These error mappings are already implemented in `handleErrorResponse()` - no changes needed.

---

## Next Task

**Task 1.3**: Add FilePreview State & Methods
- Uses the `getOfficeUrl()` method created in this task
- Implements editor mode toggle logic
