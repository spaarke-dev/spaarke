# Task 3.2 Implementation Complete: Handle Token Errors in API Client

**Date:** 2025-10-06
**Sprint:** 8 - MSAL Integration
**Task:** 3.2 (Phase 3 - Integration)
**Status:** ✅ **COMPLETE**

---

## Implementation Summary

Successfully implemented automatic retry logic for 401 errors and user-friendly error messages for common HTTP failure scenarios in `SdapApiClient`. The client now gracefully handles token expiration race conditions by clearing MSAL cache and retrying with a fresh token.

---

## Changes Made

### 1. Added MSAL Import for Cache Management

**File:** [services/SdapApiClient.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)

**Change:**
```typescript
import { MsalAuthProvider } from './auth/MsalAuthProvider';
```

**Purpose:** Access MSAL cache clearing functionality for 401 retry logic

---

### 2. Enhanced fetchWithTimeout() with Automatic 401 Retry

**Previous Implementation:**
- Simple timeout wrapper around fetch()
- No retry logic
- Token expiration during request causes user-facing error

**New Implementation:**
```typescript
private async fetchWithTimeout(
    url: string,
    options: RequestInit
): Promise<Response> {
    let attempt = 0;
    const maxAttempts = 2;

    while (attempt < maxAttempts) {
        attempt++;

        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), this.timeout);

        try {
            const response = await fetch(url, {
                ...options,
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            // Success or last attempt - return response
            if (response.ok || attempt === maxAttempts) {
                return response;
            }

            // 401 Unauthorized - token may have expired during request
            if (response.status === 401 && attempt < maxAttempts) {
                logger.warn('SdapApiClient', '401 Unauthorized response - clearing token cache and retrying', {
                    url,
                    attempt,
                    maxAttempts
                });

                // Clear MSAL cache to force fresh token acquisition
                MsalAuthProvider.getInstance().clearCache();

                // Get fresh token for retry
                const newToken = await this.getAccessToken();

                // Update Authorization header with fresh token
                if (options.headers) {
                    (options.headers as Record<string, string>)['Authorization'] = `Bearer ${newToken}`;
                }

                logger.info('SdapApiClient', 'Retrying request with fresh token');

                // Continue to next iteration (retry)
                continue;
            }

            // Other errors (non-401) - return immediately
            return response;

        } catch (error) {
            clearTimeout(timeoutId);

            if (error instanceof Error && error.name === 'AbortError') {
                throw new Error(`Request timeout after ${this.timeout}ms`);
            }

            throw error;
        }
    }

    // Should never reach here, but TypeScript needs return
    throw new Error('Unexpected error in fetchWithTimeout retry logic');
}
```

**Key Features:**
- ✅ **Max 2 attempts:** First attempt + 1 retry (prevents infinite loops)
- ✅ **401-specific retry:** Only retries on Unauthorized errors
- ✅ **Cache clearing:** Forces MSAL to acquire fresh token
- ✅ **Token refresh:** Gets new token via `getAccessToken()` callback
- ✅ **Header update:** Updates Authorization header with fresh token
- ✅ **Logging:** Tracks retry attempts for debugging
- ✅ **Other errors handled:** Non-401 errors return immediately (no retry)

**Handles Race Condition:**
```
Scenario: Token expires between cache check and API call

Timeline:
1. T=0ms:    getToken() checks cache, token valid (expires in 4 min)
2. T=50ms:   API request starts
3. T=100ms:  Token expires (edge case timing)
4. T=200ms:  BFF validates token → 401 Unauthorized
5. T=250ms:  SdapApiClient detects 401
6. T=300ms:  Clear MSAL cache
7. T=350ms:  Get fresh token (acquireTokenSilent)
8. T=850ms:  Retry request with fresh token
9. T=1050ms: Success ✅

Without retry: User sees error ❌
With retry: Transparent recovery ✅
```

---

### 3. Added User-Friendly Error Messages

**New Method:** `getUserFriendlyErrorMessage()`

```typescript
private getUserFriendlyErrorMessage(status: number, originalMessage: string): string {
    switch (status) {
        case 401:
            return 'Authentication failed. Your session may have expired. Please refresh the page and try again.';

        case 403:
            return 'Access denied. You do not have permission to perform this operation. Please contact your administrator.';

        case 404:
            return 'The requested file was not found. It may have been deleted or moved.';

        case 408:
        case 504:
            return 'Request timeout. The server took too long to respond. Please try again.';

        case 429:
            return 'Too many requests. Please wait a moment and try again.';

        case 500:
            return 'Server error occurred. Please try again later. If the problem persists, contact your administrator.';

        case 502:
        case 503:
            return 'The service is temporarily unavailable. Please try again in a few minutes.';

        default:
            // For other errors, return original message
            return originalMessage;
    }
}
```

**Error Messages Coverage:**

| Status Code | User-Friendly Message | Technical Meaning |
|-------------|----------------------|-------------------|
| **401** | "Authentication failed. Your session may have expired. Please refresh the page and try again." | Unauthorized - token invalid/expired |
| **403** | "Access denied. You do not have permission to perform this operation. Please contact your administrator." | Forbidden - user lacks permissions |
| **404** | "The requested file was not found. It may have been deleted or moved." | Not Found - resource doesn't exist |
| **408/504** | "Request timeout. The server took too long to respond. Please try again." | Timeout errors |
| **429** | "Too many requests. Please wait a moment and try again." | Rate limiting |
| **500** | "Server error occurred. Please try again later. If the problem persists, contact your administrator." | Internal Server Error |
| **502/503** | "The service is temporarily unavailable. Please try again in a few minutes." | Bad Gateway / Service Unavailable |
| **Other** | *(Original error message)* | Unhandled status codes |

---

### 4. Enhanced handleResponse() Method

**Before:**
```typescript
const error = new Error(errorMessage) as Error & { details?: string; status?: number };
error.details = errorDetails;
error.status = response.status;
throw error;
```

**After:**
```typescript
// Create user-friendly error messages for common scenarios
const userFriendlyMessage = this.getUserFriendlyErrorMessage(response.status, errorMessage);

const error = new Error(userFriendlyMessage) as Error & {
    details?: string;
    status?: number;
    originalMessage?: string;
};
error.details = errorDetails;
error.status = response.status;
error.originalMessage = errorMessage;  // Preserve original for logging

throw error;
```

**Benefits:**
- ✅ Error message shown to user is friendly and actionable
- ✅ Original technical error preserved in `originalMessage` property
- ✅ Status code and details available for debugging
- ✅ Consistent error handling across all API methods

---

## Integration with Existing Features

### Works with Phase 2 Token Caching
```
Happy Path (Token in Cache):
1. API call → getAccessToken()
2. Check cache → token valid → return immediately (5-50ms)
3. API request with cached token
4. Success ✅

Edge Case (Token Expired During Request):
1. API call → getAccessToken()
2. Check cache → token valid (expires in 4 min)
3. API request starts
4. Token expires before request reaches server
5. Server returns 401
6. fetchWithTimeout() detects 401
7. Clear MSAL cache
8. Get fresh token (acquireTokenSilent - 500ms)
9. Retry request with fresh token
10. Success ✅
```

### Works with Phase 2 Proactive Refresh
- **Normal operation:** Proactive refresh prevents token expiration (Task 2.3)
- **Fallback:** If proactive refresh misses edge case, 401 retry handles it
- **Defense in depth:** Multiple layers prevent user-facing auth errors

---

## Error Flow Examples

### Example 1: 401 Retry Success
```
User: Clicks "Download File"
↓
SdapApiClient.downloadFile()
↓
getAccessToken() → returns token from cache (expires in 4 min)
↓
HTTP GET /api/obo/drives/{id}/items/{id}/content
Authorization: Bearer <token>
↓
[Token expires during request]
↓
BFF validates token → 401 Unauthorized
↓
fetchWithTimeout() detects 401 (attempt 1 of 2)
↓
MsalAuthProvider.clearCache()
↓
getAccessToken() → acquireTokenSilent() → fresh token
↓
HTTP GET /api/obo/drives/{id}/items/{id}/content (retry)
Authorization: Bearer <fresh_token>
↓
BFF validates token → 200 OK
↓
File downloaded successfully ✅
↓
User: File downloads (no error visible)
```

### Example 2: 403 Permission Denied
```
User: Clicks "Delete File"
↓
SdapApiClient.deleteFile()
↓
getAccessToken() → returns token from cache
↓
HTTP DELETE /api/obo/drives/{id}/items/{id}
Authorization: Bearer <token>
↓
BFF validates token → User lacks delete permission → 403 Forbidden
↓
handleResponse() → status 403
↓
getUserFriendlyErrorMessage(403)
↓
Error thrown: "Access denied. You do not have permission to perform this operation. Please contact your administrator."
↓
User: Sees friendly error message (not "HTTP 403: Forbidden")
```

### Example 3: 401 After Retry (Session Truly Expired)
```
User: Clicks "Upload File" (after 8 hours inactive)
↓
SdapApiClient.uploadFile()
↓
getAccessToken() → cache empty → acquireTokenSilent()
↓
MSAL: No valid session → InteractionRequiredAuthError
↓
acquireTokenPopup() → User sees login popup
↓
User: Closes popup without logging in
↓
Error thrown: "Failed to retrieve user access token via MSAL"
↓
OR
↓
User: Logs in via popup → Fresh token acquired
↓
HTTP PUT /api/obo/drives/{id}/upload
Authorization: Bearer <fresh_token>
↓
[Token somehow still invalid - edge case]
↓
BFF returns 401 Unauthorized
↓
fetchWithTimeout() detects 401 (attempt 1 of 2)
↓
MsalAuthProvider.clearCache()
↓
getAccessToken() → acquireTokenSilent() → Still fails
↓
HTTP PUT /api/obo/drives/{id}/upload (retry)
Authorization: Bearer <still_invalid_token>
↓
BFF returns 401 Unauthorized again
↓
fetchWithTimeout() → attempt 2 of 2 (max attempts reached)
↓
handleResponse() → status 401
↓
getUserFriendlyErrorMessage(401)
↓
Error thrown: "Authentication failed. Your session may have expired. Please refresh the page and try again."
↓
User: Sees actionable error message
```

---

## Success Criteria Met

**From Task 3.2 Requirements:**

✅ **401 errors trigger token refresh + retry**
- Implemented in `fetchWithTimeout()` with automatic cache clear and retry

✅ **Second 401 throws error (no infinite loop)**
- `maxAttempts = 2` enforces single retry
- Second 401 returns response to `handleResponse()` for user-friendly error

✅ **User sees friendly message: "Session expired. Please refresh the page."**
- `getUserFriendlyErrorMessage(401)` returns: "Authentication failed. Your session may have expired. Please refresh the page and try again."

✅ **No changes to API method signatures**
- All changes internal to `SdapApiClient`
- `fetchWithTimeout()` still returns `Promise<Response>`
- All 4 API methods unchanged (upload, download, delete, replace)

✅ **Prevents infinite retry loops**
- Max 2 attempts (first + 1 retry)
- Only retries on 401
- Other errors return immediately

---

## Build Verification

**Command:** `npm run build`
**Working Directory:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid`

**Result:** ✅ **SUCCESS**

**Output:**
```
[4:22:36 PM] [build] Succeeded
webpack 5.102.0 compiled successfully in 30645 ms
```

**Warnings:**
- 1 ESLint warning (react-hooks/exhaustive-deps - unrelated to this task)
- No TypeScript compilation errors
- No build errors

**Bundle Size:** 9.75 MiB (no significant change from Task 3.1)

---

## ADR Compliance

### ADR-002: No Heavy Plugins
- ✅ Retry logic runs client-side in PCF control
- ✅ No changes to Dataverse plugins
- ✅ MSAL cache management client-side only

### ADR-007: Storage Seam Minimalism
- ✅ Uses existing `MsalAuthProvider` singleton
- ✅ No new abstractions introduced
- ✅ Simple retry pattern within existing method

---

## Testing Recommendations

### Unit Tests (Future - Task 4.1)
- Test 401 triggers cache clear + retry
- Test 401 after retry throws user-friendly error
- Test 403/404/500 show correct messages
- Test other errors return original message
- Mock `MsalAuthProvider.clearCache()` and `getAccessToken()`

### Integration Tests (Future - Task 4.2)
- Test token expiration during long upload
- Test retry success on 401
- Test 401 after retry shows error
- Test permission denied (403) error message
- Test 404 error message
- Test server error (500) message

### Manual Testing
1. **401 Retry Test:**
   - Use Chrome DevTools → Network tab
   - Set breakpoint in BFF to force 401 response
   - Trigger API call
   - Verify retry with fresh token
   - Verify success message

2. **User-Friendly Errors Test:**
   - Force 403 response (remove user permissions)
   - Verify message: "Access denied..."
   - Force 404 response (delete file)
   - Verify message: "The requested file was not found..."

---

## Performance Impact

**No Negative Performance Impact:**
- ✅ Happy path unchanged (no retry needed)
- ✅ Retry only on 401 (rare edge case)
- ✅ Single retry adds ~500ms (acquireTokenSilent)
- ✅ Proactive refresh (Task 2.3) prevents most 401s

**Typical Performance:**
```
Without 401:
- API call with cached token: 100-300ms (network time)

With 401 (rare):
- First attempt: 100-300ms (fails with 401)
- Cache clear: <1ms
- Get fresh token: 500ms (acquireTokenSilent)
- Retry attempt: 100-300ms (succeeds)
- Total: ~700-1100ms

User experience: Slightly slower, but no error ✅
```

---

## Files Modified

**Modified:**
1. [services/SdapApiClient.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)
   - Added `MsalAuthProvider` import
   - Enhanced `fetchWithTimeout()` with 401 retry logic (73 lines, was 28 lines)
   - Enhanced `handleResponse()` with user-friendly messages
   - Added `getUserFriendlyErrorMessage()` helper method (32 lines)
   - Updated JSDoc comments

**Lines Changed:** ~110 lines added/modified
**Total File Size:** 373 lines (was 282 lines)

---

## Phase 3 Complete

**Task 3.1:** ✅ Integrate MSAL with SdapApiClient
**Task 3.2:** ✅ Handle Token Errors in API Client

**Phase 3 Status:** ✅ **COMPLETE** (HTTP Client Integration)

---

## Next Steps

**Phase 4: Testing and Validation**

**Recommended Next Task:** Task 4.2 - End-to-End Testing (45 min)
- Test in actual Dataverse environment
- Validate happy path (SSO silent → API success)
- Validate error scenarios (401, 403, 404, 500)
- Validate token refresh during long operations
- Validate performance (< 500ms for cached tokens)

**Alternative:** Task 4.1 - Create Unit Tests (60 min)
- Unit test coverage before E2E testing
- Jest + @testing-library/react
- Mock MSAL dependencies

**See:** [TASKS-REMAINING-SUMMARY.md](TASKS-REMAINING-SUMMARY.md) for full task list

---

## Completion Metrics

**Duration:** ~20 minutes
**Lines Added:** ~110 lines
**Build Status:** ✅ Success
**ADR Compliance:** ✅ ADR-002, ADR-007
**Success Criteria:** ✅ All met
**Infinite Loop Prevention:** ✅ Max 2 attempts
**User Experience:** ✅ Friendly error messages

---

**Task Status:** ✅ **COMPLETE**
**Phase 3 Status:** ✅ **COMPLETE**
**Ready for Phase 4:** ✅ **YES**

---
