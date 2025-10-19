# Task 3.1: Integrate MSAL with SdapApiClient

**Sprint:** 8 - MSAL Integration
**Phase:** 3 - HTTP Client Integration
**Task:** 3.1 of 2 (Phase 3)
**Duration:** 45 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Update SdapApiClient initialization to use MsalAuthProvider.getToken() for Authorization headers.**

**Success Criteria:**
- ✅ `SdapApiClientFactory` updated to pass MSAL token callback
- ✅ All API calls include `Authorization: Bearer <token>` from MSAL
- ✅ API endpoint URLs point to Spe.Bff.Api (not Custom APIs)
- ✅ Token acquisition errors handled gracefully
- ✅ TypeScript compiles without errors
- ✅ Test API call succeeds with MSAL token

---

## Context

### Why This Task Matters

**Current state** (without MSAL):
- `SdapApiClient` has `getAccessToken()` callback
- Callback currently returns mock/placeholder token
- API calls fail with 401 Unauthorized

**After this task**:
- `getAccessToken()` calls `MsalAuthProvider.getToken()`
- Real user tokens acquired via SSO silent
- API calls succeed with proper authentication

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ Token acquisition happens client-side (MSAL.js)
- ✅ No Dataverse plugins involved

**ADR-007 (Storage Seam Minimalism):**
- ✅ Uses existing `SdapApiClient` pattern
- ✅ Calls Sprint 4 `SpeFileStore` OBO endpoints

### Sprint 4 Integration

From [ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md](c:\code_files\spaarke\docs\architecture\ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md):

**OBO Endpoints** (lines 505-522):
```typescript
app.MapGet("/api/obo/containers/{id}/children", async (
    string id,
    HttpContext ctx,
    [FromServices] SpeFileStore speFileStore) =>
{
    var userToken = TokenHelper.ExtractBearerToken(ctx); // ← Expects Authorization: Bearer <token>
    var result = await speFileStore.ListChildrenAsUserAsync(userToken, id, ...);
    return TypedResults.Ok(result);
});
```

**This task:** Make `SdapApiClient` send `Authorization: Bearer <token>` acquired from MSAL.

---

## Prerequisites

**Before starting:**
- ✅ Phase 1 complete (MSAL initialized in PCF control)
- ✅ Phase 2 complete (Token acquisition working)
- ✅ Existing `SdapApiClient` with `getAccessToken()` callback

**Expected files:**
- `services/SdapApiClient.ts` - API client with token callback
- `services/SdapApiClientFactory.ts` - Factory for creating client
- `services/auth/MsalAuthProvider.ts` - Token provider (Phase 2)

---

## Step-by-Step Instructions

### Step 1: Review Current SdapApiClientFactory

**File:** `services/SdapApiClientFactory.ts`

**Read current implementation:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
cat services/SdapApiClientFactory.ts
```

**Expected pattern:**
```typescript
export class SdapApiClientFactory {
  static create(baseUrl: string): SdapApiClient {
    const getAccessToken = async () => {
      // TODO: Implement real token acquisition
      return "placeholder-token";
    };

    return new SdapApiClient(baseUrl, getAccessToken);
  }
}
```

---

### Step 2: Update SdapApiClientFactory to Use MSAL

**Replace factory implementation:**

```typescript
import { SdapApiClient } from './SdapApiClient';
import { MsalAuthProvider } from './auth/MsalAuthProvider';
import { logger } from '../utils/logger';

/**
 * Factory for creating SdapApiClient instances with MSAL authentication
 *
 * ADR Compliance:
 * - ADR-002: Client-side token acquisition (no plugins)
 * - ADR-007: Uses existing SpeFileStore OBO endpoints (Sprint 4)
 *
 * Sprint 4 Integration:
 * - Acquires tokens via MSAL.js (ssoSilent)
 * - Sends Authorization: Bearer header to Spe.Bff.Api
 * - TokenHelper.ExtractBearerToken() validates header format
 */
export class SdapApiClientFactory {
  /**
   * OAuth scopes for Spe.Bff.Api access
   *
   * Matches Sprint 4 OBO endpoints permission requirement.
   */
  private static readonly SPE_BFF_API_SCOPES = ["api://spe-bff-api/user_impersonation"];

  /**
   * Create SdapApiClient with MSAL authentication
   *
   * @param baseUrl - Spe.Bff.Api base URL (e.g., "https://spe-bff-api.azurewebsites.net")
   * @param timeout - Request timeout in milliseconds (default: 5 minutes)
   * @returns Configured SdapApiClient instance
   */
  static create(baseUrl: string, timeout?: number): SdapApiClient {
    logger.info('SdapApiClientFactory', 'Creating API client', { baseUrl, timeout });

    /**
     * Token acquisition callback
     *
     * Called by SdapApiClient before each API request.
     * Acquires user token via MSAL (ssoSilent + popup fallback).
     *
     * Flow:
     * 1. getAccessToken() called by SdapApiClient
     * 2. MsalAuthProvider.getToken() checks cache
     * 3. If cached and valid, returns immediately (< 1ms)
     * 4. If not cached, acquires via ssoSilent() (~450ms)
     * 5. Returns token string
     * 6. SdapApiClient adds to Authorization: Bearer header
     */
    const getAccessToken = async (): Promise<string> => {
      try {
        // Get MSAL provider instance (singleton, already initialized in index.ts)
        const msalProvider = MsalAuthProvider.getInstance();

        // Acquire token for Spe.Bff.Api scopes
        // This uses cache if available (fast), or ssoSilent if needed
        const token = await msalProvider.getToken(SdapApiClientFactory.SPE_BFF_API_SCOPES);

        logger.debug('SdapApiClientFactory', 'Token acquired for API call');

        return token;

      } catch (error) {
        logger.error('SdapApiClientFactory', 'Failed to acquire token for API call', error);

        // Throw with user-friendly message
        throw new Error(
          'Failed to acquire authentication token. Please sign in and try again. ' +
          `Details: ${error instanceof Error ? error.message : 'Unknown error'}`
        );
      }
    };

    // Create and return configured client
    return new SdapApiClient(baseUrl, getAccessToken, timeout);
  }
}
```

---

### Step 3: Update API Client Initialization in PCF Control

**File:** `index.ts`

**Find where SdapApiClient is created:**
```typescript
// Look for existing initialization
const apiClient = SdapApiClientFactory.create("https://...");
```

**Update to use correct base URL:**
```typescript
/**
 * Initialize SDAP API client with MSAL authentication
 *
 * Points to Spe.Bff.Api OBO endpoints (Sprint 4).
 * Uses MsalAuthProvider for token acquisition.
 */
private initializeApiClient(): void {
  // TODO: Get base URL from environment config
  // For now, using placeholder (update with actual Spe.Bff.Api URL)
  const baseUrl = "https://spe-bff-api.azurewebsites.net";

  this.apiClient = SdapApiClientFactory.create(baseUrl);

  logger.info('[UniversalDatasetGrid] API client initialized', { baseUrl });
}
```

**Call in init() method after MSAL initialization:**
```typescript
public async init(...): Promise<void> {
  try {
    // Phase 1: Initialize MSAL
    this.authProvider = MsalAuthProvider.getInstance();
    await this.authProvider.initialize();

    // Phase 3: Initialize API client (uses MSAL for tokens)
    this.initializeApiClient();

    // ... rest of initialization
  } catch (error) {
    // ... error handling
  }
}
```

---

### Step 4: Verify API Endpoint URLs

**Check `SdapApiClient.ts` endpoints match Sprint 4 OBO format:**

**Expected OBO endpoint format** (from Sprint 4):
```
✅ /api/obo/drives/{driveId}/items/{itemId}
✅ /api/obo/containers/{containerId}/children
```

**NOT Custom API format:**
```
❌ /api/sprk_DownloadFile
❌ /api/sprk_DeleteFile
```

**If endpoints use Custom API format, update to OBO format in `SdapApiClient.ts`:**

```typescript
// ✅ Correct (OBO endpoint)
const url = `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`;

// ❌ Wrong (Custom API - don't use)
const url = `${this.baseUrl}/api/sprk_DownloadFile`;
```

---

### Step 5: Build and Test

**Build:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Test API call:**

Start test harness:
```bash
npm start watch
```

**In browser console, test file operation:**
```javascript
// Assuming control loaded
(async () => {
  try {
    // Test download (adjust IDs to match your test data)
    const request = {
      driveId: "b!...",
      itemId: "01ABC123..."
    };

    const blob = await window.apiClient.downloadFile(request);
    console.log("✅ Download succeeded:", blob.size, "bytes");

  } catch (error) {
    console.error("❌ Download failed:", error);
  }
})();
```

**Expected console logs:**
```
[MsalAuthProvider] Acquiring token for scopes: api://spe-bff-api/user_impersonation
[MsalAuthProvider] Token acquired successfully via silent flow ✅
[SdapApiClientFactory] Token acquired for API call
[SdapApiClient] Downloading file...
[SdapApiClient] File downloaded successfully
✅ Download succeeded: 12345 bytes
```

---

## Verification Checklist

**Task 3.1 complete when:**

- ✅ `SdapApiClientFactory.create()` updated to use `MsalAuthProvider.getToken()`
- ✅ Token acquisition callback properly wrapped with error handling
- ✅ Scopes constant defined: `SPE_BFF_API_SCOPES`
- ✅ `index.ts` calls `initializeApiClient()` after MSAL initialization
- ✅ API endpoint URLs verified to use OBO format (not Custom API)
- ✅ `npm run build` compiles without errors
- ✅ Test API call succeeds with MSAL token
- ✅ Authorization header includes Bearer token in network tab

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npx tsc --noEmit && \
npm run build && \
echo "✅ Task 3.1 Complete"
```

---

## Troubleshooting

### Issue 1: "MSAL not initialized" error

**Cause:** `SdapApiClientFactory.create()` called before MSAL initialization.

**Fix:** Ensure call order in `index.ts`:
```typescript
public async init(...): Promise<void> {
  // 1. Initialize MSAL FIRST
  await this.authProvider.initialize();

  // 2. THEN initialize API client (uses MSAL)
  this.initializeApiClient();
}
```

---

### Issue 2: API returns 401 Unauthorized

**Possible causes:**

1. **Token not in Authorization header**
   - Check network tab: Request → Headers → Authorization
   - Should see: `Authorization: Bearer eyJ0eXAiOiJ...`
   - **Fix:** Verify `SdapApiClient` adds header correctly

2. **Token for wrong audience**
   - Token audience must match Spe.Bff.Api app ID
   - **Fix:** Verify scopes in `msalConfig.ts`: `["api://spe-bff-api/user_impersonation"]`

3. **Token expired**
   - Check token expiration (decode JWT at jwt.ms)
   - **Fix:** Clear cache: `MsalAuthProvider.getInstance().clearCache()`

---

### Issue 3: API returns 404 Not Found

**Cause:** Endpoint URL format incorrect.

**Fix:** Verify OBO endpoint format:
```typescript
// ✅ Correct
`${baseUrl}/api/obo/drives/${driveId}/items/${itemId}`

// ❌ Wrong
`${baseUrl}/api/drives/${driveId}/items/${itemId}` // Missing /obo
`${baseUrl}/api/sprk_DownloadFile` // Custom API format
```

---

## Next Steps

**After Task 3.1 completion:**

✅ **Task 3.1 Complete** - MSAL integrated with SdapApiClient

➡️ **Task 3.2: Handle Token Errors in API Client**
- Add 401 retry logic (refresh token and retry request)
- Handle MSAL errors gracefully
- User-friendly error messages for auth failures

**See:** `TASK-3.2-HANDLE-TOKEN-ERRORS.md`

---

## Files Modified

**Modified:**
- `services/SdapApiClientFactory.ts` - Added MSAL integration
- `index.ts` - Added API client initialization

---

## Related Documentation

- [Sprint 4 OBO Endpoints](../../../docs/architecture/ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md#flow-2-on-behalf-of-user-context)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)
- [ADR-007: Storage Seam Minimalism](../../../docs/adr/ADR-007-spe-storage-seam-minimalism.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 3.2 - Handle Token Errors
**Estimated Duration:** 45 minutes

---
