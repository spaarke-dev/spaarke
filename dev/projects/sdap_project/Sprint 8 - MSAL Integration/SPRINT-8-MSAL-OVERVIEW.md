# Sprint 8: MSAL.js Integration in Universal Dataset Grid

**Date:** October 6, 2025
**Status:** 🚀 **ACTIVE SPRINT**
**Approach:** MSAL.js client-side authentication (ADR-002 compliant)

---

## Executive Summary

**Goal:** Enable Universal Dataset Grid PCF control to authenticate users and call Spe.Bff.Api OBO endpoints using MSAL.js for token acquisition.

**Why MSAL.js?**
- ✅ ADR-002 compliant (no plugins with HTTP calls)
- ✅ Leverages existing Sprint 4 OBO endpoints (no backend changes)
- ✅ Client-side token acquisition (proven to work in PCF)
- ✅ 75% less effort than Custom API Proxy approach
- ✅ Better performance, scalability, and maintainability

---

## Sprint 8 Goal

> **Integrate MSAL.js (@azure/msal-browser) in Universal Dataset Grid PCF control to acquire user authentication tokens via SSO silent flow, then call existing Spe.Bff.Api OBO endpoints for SharePoint Embedded file operations.**

**Success Criteria:**
1. ✅ MSAL.js initialized in PCF control without errors
2. ✅ User tokens acquired via `PublicClientApplication.ssoSilent()`
3. ✅ Tokens cached in sessionStorage for performance
4. ✅ File operations (download, delete, replace, upload) use OBO endpoints
5. ✅ Authorization header includes bearer token: `Authorization: Bearer <token>`
6. ✅ Token refresh logic handles expired tokens
7. ✅ Fallback to interactive login if SSO fails
8. ✅ End-to-end testing confirms OBO flow works
9. ✅ No ADR-002 violations (no plugins, no server-side proxy)
10. ✅ Production deployment successful

---

## Architecture Overview

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────┐
│ Universal Dataset Grid PCF Control (React + TypeScript)      │
│                                                              │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 1. MSAL.js Initialization                           │    │
│  │    - PublicClientApplication                        │    │
│  │    - Client ID from Azure App Registration          │    │
│  │    - Redirect URI: https://dataverse.dynamics.com   │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 2. Token Acquisition (ssoSilent)                    │    │
│  │    - publicClientApplication.ssoSilent()            │    │
│  │    - Scopes: ["api://spe-bff-api/.default"]        │    │
│  │    - loginHint: user.email                          │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 3. Token Cache (sessionStorage)                     │    │
│  │    - sessionStorage.setItem("accessToken", token)   │    │
│  │    - Check expiration before reuse                  │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 4. HTTP Client with Auth Header                     │    │
│  │    - Authorization: Bearer <token>                  │    │
│  │    - POST /api/files/download (OBO endpoint)        │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Spe.Bff.Api (Sprint 4 OBO Endpoints - Already Exists)       │
│                                                              │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 5. TokenHelper.ExtractBearerToken()                 │    │
│  │    - Validates Authorization header format          │    │
│  │    - Extracts token from "Bearer <token>"           │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 6. OBO Flow (On-Behalf-Of)                          │    │
│  │    - Exchange user token for Graph API token        │    │
│  │    - ClientSecretCredential + user assertion        │    │
│  │    - Scopes: https://graph.microsoft.com/.default   │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │ 7. SpeFileStore Operations (User Context)           │    │
│  │    - DriveItemOps.DownloadDriveItemAsUserAsync()    │    │
│  │    - Uses Graph SDK with OBO token                  │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ Microsoft Graph API / SharePoint Embedded                    │
│                                                              │
│  - User-context file operations                             │
│  - Permissions: User's SharePoint permissions               │
│  - Audit logs: User identity preserved                      │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

**1. MsalAuthProvider.ts (NEW)**
- Initializes `PublicClientApplication`
- Implements `ssoSilent()` token acquisition
- Manages token cache in sessionStorage
- Handles token refresh logic
- Fallback to interactive login

**2. fileService.ts (UPDATED)**
- Adds `Authorization: Bearer <token>` header
- Calls Spe.Bff.Api OBO endpoints directly
- Handles 401 Unauthorized (refresh token)
- No Custom API calls (direct HTTP to BFF)

**3. Spe.Bff.Api OBO Endpoints (EXISTING - Sprint 4)**
- POST `/api/files/download` - Download file as user
- DELETE `/api/files/{id}` - Delete file as user
- PUT `/api/files/{id}` - Replace file as user
- POST `/api/files/upload` - Upload file as user

**4. TokenHelper.cs (EXISTING - Sprint 4)**
- Extracts bearer token from Authorization header
- Validates token format
- Returns token for OBO flow

---

## Implementation Phases

### Phase 1: MSAL.js Setup and Configuration
- Add `@azure/msal-browser` NPM package
- Create `MsalAuthProvider.ts` with `PublicClientApplication`
- Configure Azure App Registration (Client ID, Redirect URI)
- Test MSAL initialization in PCF control

### Phase 2: Token Acquisition Implementation
- Implement `ssoSilent()` for silent token acquisition
- Add sessionStorage caching logic
- Implement token expiration checking
- Add error handling for SSO failures

### Phase 3: HTTP Client Integration
- Update `fileService.ts` to use `MsalAuthProvider`
- Add `Authorization: Bearer <token>` header to all requests
- Update API endpoint URLs to point to Spe.Bff.Api OBO endpoints
- Remove any Custom API references

### Phase 4: Error Handling and Refresh Logic
- Handle 401 Unauthorized responses (refresh token)
- Fallback to interactive login if SSO fails
- Add retry logic for transient failures
- Implement comprehensive error messages

### Phase 5: Testing and Deployment
- Unit tests for `MsalAuthProvider`
- Integration tests for token acquisition
- E2E tests for file operations with OBO
- Deploy to dev → test → prod environments

---

## Technical Requirements

### NPM Package

**Package:** `@azure/msal-browser`
**Version:** Latest stable (2.x)
**Installation:**
```bash
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm install @azure/msal-browser --save
```

### Azure App Registration

**Required Configuration:**
- **Client ID**: From Azure App Registration for SDAP
- **Tenant ID**: Your Azure AD tenant
- **Redirect URI**: `https://<your-dataverse-env>.dynamics.com`
- **API Permissions**:
  - `api://spe-bff-api/user_impersonation` (delegated)
  - `User.Read` (Microsoft Graph, delegated)
- **Supported Account Types**: Single tenant
- **Public Client Flow**: Enabled

### MSAL Configuration

```typescript
// msalConfig.ts (NEW)
import { Configuration } from "@azure/msal-browser";

export const msalConfig: Configuration = {
  auth: {
    clientId: "<YOUR_CLIENT_ID>", // From Azure App Registration
    authority: "https://login.microsoftonline.com/<YOUR_TENANT_ID>",
    redirectUri: "https://<your-dataverse-env>.dynamics.com",
  },
  cache: {
    cacheLocation: "sessionStorage", // Store tokens in sessionStorage
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) return;
        console.log(`[MSAL] ${message}`);
      },
    },
  },
};

export const loginRequest = {
  scopes: ["api://spe-bff-api/user_impersonation"],
};
```

### TypeScript Interfaces

```typescript
// types/auth.ts (NEW)
export interface AuthToken {
  accessToken: string;
  expiresOn: Date;
  scopes: string[];
}

export interface AuthError {
  errorCode: string;
  errorMessage: string;
  requiresInteraction: boolean;
}

export interface IAuthProvider {
  initialize(): Promise<void>;
  getToken(scopes: string[]): Promise<string>;
  clearCache(): void;
  isAuthenticated(): boolean;
}
```

---

## File Changes

### New Files

1. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts`**
   - MSAL configuration (client ID, tenant, redirect URI)
   - Login request scopes

2. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts`**
   - `PublicClientApplication` wrapper
   - `ssoSilent()` implementation
   - Token caching and refresh logic
   - Error handling

3. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/auth.ts`**
   - TypeScript interfaces for auth types
   - `AuthToken`, `AuthError`, `IAuthProvider`

### Modified Files

1. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/fileService.ts`**
   - Add `Authorization: Bearer <token>` header
   - Update API endpoint URLs to Spe.Bff.Api
   - Add token refresh on 401 responses

2. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/package.json`**
   - Add `@azure/msal-browser` dependency

3. **`src/controls/UniversalDatasetGrid/UniversalDatasetGrid/UniversalDatasetGrid/index.ts`**
   - Initialize `MsalAuthProvider` on control init
   - Handle MSAL errors gracefully

---

## Integration with Existing Spe.Bff.Api

### OBO Endpoints (Sprint 4 - Already Exist)

**Download File:**
```http
POST https://spe-bff-api.azurewebsites.net/api/files/download
Authorization: Bearer <user-token>
Content-Type: application/json

{
  "containerId": "b!abc123...",
  "driveId": "b!xyz789...",
  "itemId": "01ABC123..."
}

Response: 200 OK
{
  "downloadUrl": "https://sharepoint.com/...",
  "expiresIn": 3600
}
```

**Delete File:**
```http
DELETE https://spe-bff-api.azurewebsites.net/api/files/{itemId}?driveId={driveId}
Authorization: Bearer <user-token>

Response: 204 No Content
```

**Replace File:**
```http
PUT https://spe-bff-api.azurewebsites.net/api/files/{itemId}?driveId={driveId}
Authorization: Bearer <user-token>
Content-Type: multipart/form-data

{file upload}

Response: 200 OK
{
  "id": "01ABC123...",
  "name": "document.pdf",
  "size": 1024000
}
```

**Upload File:**
```http
POST https://spe-bff-api.azurewebsites.net/api/files/upload
Authorization: Bearer <user-token>
Content-Type: multipart/form-data

{file upload + metadata}

Response: 201 Created
{
  "id": "01ABC123...",
  "name": "document.pdf",
  "size": 1024000
}
```

### TokenHelper Usage (Sprint 4 - Already Exists)

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs
public static string ExtractBearerToken(HttpContext httpContext)
{
    var authHeader = httpContext.Request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(authHeader))
    {
        throw new UnauthorizedAccessException("Missing Authorization header");
    }

    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        throw new UnauthorizedAccessException("Invalid Authorization header format");
    }

    return authHeader["Bearer ".Length..].Trim();
}
```

**Usage in OBO Endpoints:**
```csharp
[HttpPost("download")]
public async Task<IActionResult> DownloadFile([FromBody] DownloadRequest request)
{
    // Extract user token from Authorization header
    var userToken = TokenHelper.ExtractBearerToken(HttpContext);

    // Call SpeFileStore with user token (OBO flow)
    var downloadUrl = await _speFileStore.GetDownloadUrlAsUserAsync(
        userToken,
        request.ContainerId,
        request.DriveId,
        request.ItemId
    );

    return Ok(new { downloadUrl });
}
```

---

## Success Metrics

### Performance
- ✅ Token acquisition < 500ms (p95)
- ✅ Token cache hit rate > 90%
- ✅ File operation latency unchanged (no plugin overhead)
- ✅ No Dataverse plugin execution limits

### Reliability
- ✅ SSO silent flow success rate > 95%
- ✅ Token refresh success rate > 99%
- ✅ Fallback to interactive login < 5% of attempts
- ✅ Zero 401 errors after token refresh

### Security
- ✅ No credentials stored in code or Dataverse
- ✅ Tokens stored securely in sessionStorage
- ✅ Token expiration enforced
- ✅ OBO flow preserves user identity in audit logs

### Compliance
- ✅ Zero ADR-002 violations (no plugins)
- ✅ All operations use Sprint 4 OBO endpoints
- ✅ No Custom APIs created
- ✅ No Dataverse entities created

---

## Timeline

### Week 1: Setup and Configuration
**Days 1-2:**
- Add `@azure/msal-browser` package
- Create `msalConfig.ts` with Azure App Registration details
- Create `MsalAuthProvider.ts` skeleton

**Days 3-5:**
- Implement `PublicClientApplication` initialization
- Test MSAL initialization in dev environment
- Verify no errors in browser console

### Week 2: Token Acquisition
**Days 1-2:**
- Implement `ssoSilent()` token acquisition
- Add sessionStorage caching logic

**Days 3-4:**
- Implement token expiration checking
- Add token refresh logic

**Day 5:**
- Add fallback to interactive login
- Test all token acquisition paths

### Week 3: Integration and Testing
**Days 1-2:**
- Update `fileService.ts` with Authorization header
- Update API endpoint URLs to Spe.Bff.Api

**Days 3-4:**
- Unit tests for `MsalAuthProvider`
- Integration tests for token acquisition

**Day 5:**
- E2E tests for file operations with OBO
- Fix any integration issues

### Week 4: Deployment
**Days 1-2:**
- Deploy to dev environment
- Dev testing and validation

**Days 3-4:**
- Deploy to test environment
- User acceptance testing

**Day 5:**
- Production deployment
- Production validation and monitoring

**Total Timeline:** 4 weeks

---

## Risk Mitigation

### Risk 1: SSO Silent Fails
**Mitigation:**
- Implement fallback to interactive login
- Log SSO failures for monitoring
- Test in all supported browsers

### Risk 2: Token Expiration During Operation
**Mitigation:**
- Check token expiration before each request
- Refresh token proactively (5 min before expiration)
- Retry request after token refresh

### Risk 3: CORS Issues
**Mitigation:**
- Verify Spe.Bff.Api CORS configuration allows Dataverse origin
- Test in actual Dataverse environment (not localhost)
- Add wildcard CORS for dev/test environments only

### Risk 4: Azure App Registration Misconfiguration
**Mitigation:**
- Document required permissions explicitly
- Test with minimal permissions first
- Admin consent required for delegated permissions

---

## Rollback Plan

**If MSAL.js integration fails in production:**

1. **Immediate Rollback:**
   - Revert PCF bundle to previous version
   - No backend changes to rollback (Sprint 4 OBO endpoints unchanged)

2. **Investigation:**
   - Review browser console logs for MSAL errors
   - Check Azure App Registration configuration
   - Verify token acquisition logs

3. **Alternative Approach:**
   - If MSAL.js fundamentally doesn't work: Consider ADR-002 exception for Custom API Proxy
   - If only SSO silent fails: Force interactive login for all users

**Rollback Time:** < 1 hour (PCF bundle revert only)

---

## Documentation

### Developer Documentation
- MSAL.js integration guide (this document)
- Phase-by-phase implementation guides (Phase 1-5)
- Troubleshooting guide
- Configuration reference

### End-User Documentation
- No user-facing changes (authentication is transparent)
- If interactive login required: Short guide on granting consent

### Operations Documentation
- Deployment runbook
- Monitoring and alerting guide
- Incident response procedures

---

## Related Documents

### Sprint 8 Documentation
- `SPRINT-8-PIVOT-TO-MSAL.md` - Why we abandoned Custom API Proxy
- `PHASE-1-MSAL-SETUP.md` - Detailed Phase 1 implementation
- `PHASE-2-TOKEN-ACQUISITION.md` - Detailed Phase 2 implementation
- `PHASE-3-HTTP-CLIENT-INTEGRATION.md` - Detailed Phase 3 implementation
- `PHASE-4-ERROR-HANDLING.md` - Detailed Phase 4 implementation
- `PHASE-5-TESTING-DEPLOYMENT.md` - Detailed Phase 5 implementation

### Sprint 4 Documentation (Reference)
- `docs/architecture/ARCHITECTURE-DUAL-AUTH-GRAPH-INTEGRATION.md` - Dual auth architecture
- `dev/projects/sdap_project/Sprint 4/TASK-4.4-FINAL-SUMMARY.md` - Sprint 4 completion summary
- `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs` - Token extraction helper

### ADRs
- `docs/adr/ADR-002-no-heavy-plugins.md` - Why no plugins with HTTP calls
- `docs/adr/ADR-007-spe-storage-seam-minimalism.md` - DTO mapping pattern

---

## Conclusion

Sprint 8 will integrate MSAL.js in Universal Dataset Grid PCF control to enable user authentication via SSO silent flow, leveraging existing Sprint 4 OBO endpoints for SharePoint Embedded file operations.

**Key Benefits:**
- ✅ ADR-002 compliant (no plugins)
- ✅ 75% less effort than Custom API Proxy
- ✅ Leverages Sprint 4 work (no backend changes)
- ✅ Proven approach (research confirms MSAL.js works in PCF)
- ✅ Better performance and scalability

**Next Steps:**
1. Review this overview with team
2. Begin Phase 1: MSAL.js Setup and Configuration
3. Track progress with phase-specific documentation

---

**Sprint 8 Status:** 🚀 **READY TO START**
**Approach:** MSAL.js Integration (ADR-002 Compliant)
**Timeline:** 4 weeks
**Effort Reduction:** 75% vs Custom API Proxy

---
