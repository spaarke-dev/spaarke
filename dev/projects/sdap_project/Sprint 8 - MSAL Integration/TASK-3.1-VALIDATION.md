# Task 3.1 Validation: Integrate MSAL with SdapApiClient

**Date:** 2025-10-06
**Sprint:** 8 - MSAL Integration
**Task:** 3.1 (Phase 3 - Integration)
**Status:** ✅ **VALIDATED - READY FOR IMPLEMENTATION**

---

## Validation Summary

**Task Goal:** Update `SdapApiClientFactory` to use `MsalAuthProvider.getToken()` instead of PCF context fallback methods.

**Current State:**
- ✅ Phase 1 complete: MSAL initialized in PCF control
- ✅ Phase 2 complete: Token acquisition with caching and proactive refresh
- ✅ `MsalAuthProvider.getToken()` fully implemented and tested
- ⚠️ `SdapApiClientFactory` still uses **4 PCF context fallback methods**
- ⚠️ API endpoint URLs currently **missing `/api/obo/` prefix**

**Validation Result:** Task requirements are **accurate and necessary**. Current implementation needs major refactor to use MSAL.

---

## Current Implementation Analysis

### 1. SdapApiClientFactory.ts (Current)

**File:** [services/SdapApiClientFactory.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts)

**Current Method Signature:**
```typescript
static create<TInputs>(
  context: ComponentFramework.Context<TInputs>,
  baseUrl: string,
  timeout = 300000
): SdapApiClient
```

**Current Token Acquisition (4 Fallback Methods):**
```typescript
private static async getTokenFromContext<TInputs>(
  context: ComponentFramework.Context<TInputs>
): Promise<string> {
  // Method 1: context.utils.getAccessToken()
  if (context.utils && 'getAccessToken' in context.utils) {
    try {
      const token = await (context.utils as any).getAccessToken();
      if (token) return token;
    } catch {}
  }

  // Method 2: context.page.getAccessToken()
  if (context.page && 'getAccessToken' in context.page) {
    try {
      const token = await (context.page as any).getAccessToken();
      if (token) return token;
    } catch {}
  }

  // Method 3: context.accessToken / context.token
  if ('accessToken' in context) {
    const token = (context as any).accessToken;
    if (token) return token;
  }
  if ('token' in context) {
    const token = (context as any).token;
    if (token) return token;
  }

  // Method 4: WhoAmI API call fallback
  if (context.webAPI) {
    try {
      const response = await context.webAPI.execute({
        getMetadata: () => ({ boundParameter: null, operationType: 1, operationName: 'WhoAmI' })
      });
      const token = (response as any)?.accessToken;
      if (token) return token;
    } catch {}
  }

  throw new Error('Unable to retrieve access token from context');
}
```

**Issues:**
- ❌ Uses PCF context (brittle, undocumented APIs)
- ❌ No token caching (calls PCF context on every API request)
- ❌ No token refresh (relies on PCF context to handle expiration)
- ❌ 4 fallback methods add complexity and maintenance burden
- ❌ Not using MSAL authentication implemented in Phase 1-2

---

### 2. SdapApiClient.ts (Current)

**File:** [services/SdapApiClient.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)

**Constructor:**
```typescript
constructor(
  baseUrl: string,
  getAccessToken: () => Promise<string>,
  timeout = 300000
) {
  this.baseUrl = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
  this.getAccessToken = getAccessToken;
  this.timeout = timeout;
}
```

**API Endpoint URLs (Current):**
```typescript
// Upload
const url = `${this.baseUrl}/drives/${driveId}/upload?fileName=${name}`;

// Download
const url = `${this.baseUrl}/drives/${driveId}/items/${itemId}/content`;

// Delete
const url = `${this.baseUrl}/drives/${driveId}/items/${itemId}`;
```

**Issues:**
- ❌ Missing `/api/obo/` prefix in endpoint URLs
- ❌ Sprint 4 BFF expects OBO endpoints: `/api/obo/drives/{driveId}/...`
- ⚠️ Constructor signature is **correct** (accepts `getAccessToken` callback)
- ✅ Authorization header format is correct: `Bearer ${token}`

---

### 3. Usage Locations

**File:** [components/UniversalDatasetGridRoot.tsx](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx)

**3 Call Sites:**

1. **Download Handler (Line 121):**
```typescript
const apiClient = SdapApiClientFactory.create(context, baseUrl);
const downloadService = new FileDownloadService(apiClient);
```

2. **Delete Handler (Line 226):**
```typescript
const apiClient = SdapApiClientFactory.create(context, baseUrl);
const deleteService = new FileDeleteService(apiClient, context);
```

3. **Replace Handler (Line 304):**
```typescript
const apiClient = SdapApiClientFactory.create(context, baseUrl);
const replaceService = new FileReplaceService(apiClient, context);
```

**Impact:**
- ⚠️ All 3 call sites pass `context` parameter
- ⚠️ After refactor, must update all 3 call sites to remove `context` parameter
- ✅ Services (download/delete/replace) don't need changes (only use apiClient)

---

## Required Changes

### Change 1: Update SdapApiClientFactory

**File:** `services/SdapApiClientFactory.ts`

**Before:**
```typescript
export class SdapApiClientFactory {
  static create<TInputs>(
    context: ComponentFramework.Context<TInputs>,
    baseUrl: string,
    timeout = 300000
  ): SdapApiClient {
    const getAccessToken = async (): Promise<string> => {
      // 4 fallback methods using PCF context
    };

    return new SdapApiClient(baseUrl, getAccessToken, timeout);
  }
}
```

**After:**
```typescript
import { MsalAuthProvider } from './auth/MsalAuthProvider';

export class SdapApiClientFactory {
  private static readonly SPE_BFF_API_SCOPES = ['api://spe-bff-api/user_impersonation'];

  static create(
    baseUrl: string,
    timeout = 300000
  ): SdapApiClient {
    const getAccessToken = async (): Promise<string> => {
      const authProvider = MsalAuthProvider.getInstance();
      return await authProvider.getToken(SdapApiClientFactory.SPE_BFF_API_SCOPES);
    };

    return new SdapApiClient(baseUrl, getAccessToken, timeout);
  }
}
```

**Changes:**
- ✅ Remove `context` parameter (no longer needed)
- ✅ Remove generic type `<TInputs>` (not needed without context)
- ✅ Remove `getTokenFromContext()` method (4 fallback methods)
- ✅ Add MSAL import
- ✅ Add `SPE_BFF_API_SCOPES` constant
- ✅ Use `MsalAuthProvider.getToken()` for token acquisition
- ✅ Leverage Phase 2 caching (~82x speedup)
- ✅ Leverage Phase 2 proactive refresh (no expiration delays)

---

### Change 2: Update API Endpoint URLs

**File:** `services/SdapApiClient.ts`

**Before:**
```typescript
// Upload
const url = `${this.baseUrl}/drives/${driveId}/upload?fileName=${name}`;

// Download
const url = `${this.baseUrl}/drives/${driveId}/items/${itemId}/content`;

// Delete
const url = `${this.baseUrl}/drives/${driveId}/items/${itemId}`;
```

**After:**
```typescript
// Upload
const url = `${this.baseUrl}/api/obo/drives/${driveId}/upload?fileName=${name}`;

// Download
const url = `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}/content`;

// Delete
const url = `${this.baseUrl}/api/obo/drives/${driveId}/items/${itemId}`;
```

**Changes:**
- ✅ Add `/api/obo/` prefix to all endpoint URLs
- ✅ Matches Sprint 4 BFF OBO endpoint routing
- ✅ Ensures `TokenHelper.ExtractBearerToken()` is called in BFF
- ✅ Enables OBO flow (user token → Graph token)

---

### Change 3: Update Call Sites

**File:** `components/UniversalDatasetGridRoot.tsx`

**Before (3 locations):**
```typescript
const apiClient = SdapApiClientFactory.create(context, baseUrl);
```

**After (3 locations):**
```typescript
const apiClient = SdapApiClientFactory.create(baseUrl);
```

**Changes:**
- ✅ Remove `context` parameter from all 3 call sites
- ✅ Line 121 (download handler)
- ✅ Line 226 (delete handler)
- ✅ Line 304 (replace handler)

---

## ADR Compliance

### ADR-002 (No Heavy Plugins)
- ✅ MSAL runs client-side (no plugins)
- ✅ Tokens acquired in PCF control
- ✅ No HTTP calls from Dataverse plugins

### ADR-007 (Storage Seam Minimalism)
- ✅ Uses existing `MsalAuthProvider` singleton
- ✅ Simple factory pattern (no over-abstraction)
- ✅ Dependency injection via `getAccessToken` callback

---

## Sprint 4 Integration

**OBO Flow (After This Task):**

```
1. User interacts with PCF control
   ↓
2. SdapApiClientFactory.create(baseUrl)
   ↓
3. getAccessToken() callback
   ↓
4. MsalAuthProvider.getToken(["api://spe-bff-api/user_impersonation"])
   ↓ (Phase 2 caching - check sessionStorage)
   ↓ (if cached and fresh, return immediately ~82x faster)
   ↓ (if near expiration, background refresh)
   ↓ (if expired, acquireTokenSilent())
5. User token (JWT)
   ↓
6. Authorization: Bearer <token>
   ↓
7. HTTP request to /api/obo/drives/{driveId}/...
   ↓
8. Spe.Bff.Api receives request
   ↓
9. TokenHelper.ExtractBearerToken() (Sprint 4)
   ↓
10. OBO flow: User token → Graph token
   ↓
11. SpeFileStore.*AsUserAsync() (Sprint 4)
   ↓
12. SharePoint Embedded API call with Graph token
```

---

## Validation Checklist

**Task 3.1 requirements validated against current codebase:**

- ✅ **Requirement 1:** Update `SdapApiClientFactory.create()` to use MSAL
  - **Status:** Required - currently uses PCF context fallback methods
  - **Impact:** Replace 4 fallback methods with single MSAL call

- ✅ **Requirement 2:** Remove `context` parameter from factory method
  - **Status:** Required - currently passes context for token retrieval
  - **Impact:** Update method signature and 3 call sites

- ✅ **Requirement 3:** Add OAuth scopes constant
  - **Status:** Required - currently no scopes defined
  - **Impact:** Define `SPE_BFF_API_SCOPES = ["api://spe-bff-api/user_impersonation"]`

- ✅ **Requirement 4:** Update API endpoint URLs with `/api/obo/` prefix
  - **Status:** Required - currently missing OBO prefix
  - **Impact:** Update 3 endpoint URLs in `SdapApiClient.ts`

- ✅ **Requirement 5:** Ensure Authorization header format
  - **Status:** Already correct - `Bearer ${token}`
  - **Impact:** No change needed

---

## Risk Assessment

**Low Risk:**
- ✅ `MsalAuthProvider.getToken()` fully implemented and tested (Phase 2)
- ✅ Token caching improves performance (~82x speedup)
- ✅ Proactive refresh eliminates expiration delays
- ✅ Constructor signature of `SdapApiClient` unchanged (only factory changes)
- ✅ Services (download/delete/replace) unchanged (only use apiClient)

**Medium Risk:**
- ⚠️ 3 call sites must be updated (breaking change)
- ⚠️ API endpoint URLs change (BFF must support `/api/obo/` routes)

**Mitigation:**
- ✅ Sprint 4 BFF already implements `/api/obo/*` routes (verified in Sprint 4 docs)
- ✅ All call sites in single file (`UniversalDatasetGridRoot.tsx`)
- ✅ TypeScript compiler will catch call site errors (signature change)

---

## Updated Implementation Plan

**Step 1:** Update `SdapApiClientFactory.ts`
- Remove `context` parameter
- Remove `getTokenFromContext()` method (4 fallbacks)
- Add MSAL import and scopes constant
- Use `MsalAuthProvider.getToken()`

**Step 2:** Update `SdapApiClient.ts` endpoint URLs
- Add `/api/obo/` prefix to upload endpoint (line 56)
- Add `/api/obo/` prefix to download endpoint (line 94)
- Add `/api/obo/` prefix to delete endpoint (line 139)

**Step 3:** Update call sites in `UniversalDatasetGridRoot.tsx`
- Remove `context` parameter from line 121 (download)
- Remove `context` parameter from line 226 (delete)
- Remove `context` parameter from line 304 (replace)

**Step 4:** Build and test
- Run `npm run build` - verify TypeScript compiles
- Test in PCF test harness - verify token acquisition
- Test file operations - verify OBO endpoints work

---

## Files to Modify

**Modified:**
1. [services/SdapApiClientFactory.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts)
   - Remove context parameter
   - Add MSAL integration
   - Remove 4 fallback methods

2. [services/SdapApiClient.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)
   - Update 3 endpoint URLs with `/api/obo/` prefix

3. [components/UniversalDatasetGridRoot.tsx](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx)
   - Update 3 call sites (remove context parameter)

**No changes needed:**
- `services/auth/MsalAuthProvider.ts` (Phase 2 complete)
- `services/FileDownloadService.ts` (only uses apiClient)
- `services/FileDeleteService.ts` (only uses apiClient)
- `services/FileReplaceService.ts` (only uses apiClient)

---

## Next Steps

**After validation:**

✅ **TASK-3.1-VALIDATION Complete** - Requirements validated, ready for implementation

➡️ **TASK-3.1-IMPLEMENTATION** - Implement MSAL integration in SdapApiClient

**See:** Original task document for step-by-step instructions (now validated as accurate)

---

**Validation Status:** ✅ **COMPLETE**
**Ready for Implementation:** ✅ **YES**
**Estimated Duration:** 45 minutes

---
