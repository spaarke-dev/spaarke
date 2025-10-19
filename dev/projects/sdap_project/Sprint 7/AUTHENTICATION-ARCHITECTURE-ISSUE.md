# Sprint 7A - Authentication Architecture Issue

**Date**: 2025-10-06
**Status**: 🔴 BLOCKED - Authentication Issue
**Severity**: High - Prevents file operations from working

---

## Executive Summary

The Universal Dataset Grid PCF control successfully deploys and displays buttons, but **file operations fail** because the PCF control **cannot obtain user authentication tokens** to send to the Spe.Bff.Api.

**Current Status:**
- ✅ PCF control deployed (v2.0.9)
- ✅ UI renders correctly with command bar
- ✅ Buttons respond to clicks
- ✅ File picker dialogs work
- ❌ **Actual file operations fail with authentication error**

**Root Cause**: PCF dataset controls in model-driven apps don't have built-in access to user Azure AD tokens needed to call external authenticated APIs.

---

## The Architecture - How It's SUPPOSED to Work

```
┌─────────────────────────────────────────────────────────────────┐
│                      AUTHENTICATION FLOW                         │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Download" button in Power Apps
   │
   ├─> PCF Control needs to call Spe.Bff.Api
   │
   ├─> Spe.Bff.Api requires: Authorization: Bearer <USER_TOKEN>
   │
   ├─> PCF Control must obtain USER_TOKEN somehow ❌ THIS IS THE PROBLEM
   │
   └─> Send: GET /api/drives/{id}/items/{id}/content
       Headers: Authorization: Bearer <USER_TOKEN>

2. Spe.Bff.Api receives request
   │
   ├─> Validates USER_TOKEN (Azure AD JWT)
   │
   ├─> Uses On-Behalf-Of (OBO) flow to get APP_TOKEN
   │   - Exchanges USER_TOKEN for APP_TOKEN with delegated permissions
   │   - APP_TOKEN has permissions to call SharePoint Embedded
   │
   └─> Calls SharePoint Embedded API
       Headers: Authorization: Bearer <APP_TOKEN>

3. SharePoint Embedded returns file
   │
   └─> Spe.Bff.Api streams back to PCF control → User downloads file
```

**Why This Architecture?**

1. **Browser Security**: Client-side JavaScript cannot have app secrets
2. **SharePoint Embedded Requirements**: Requires app-level permissions (not user permissions)
3. **On-Behalf-Of Pattern**: Allows API to act on behalf of the user with app permissions
4. **Audit Trail**: User identity is preserved through the chain

---

## The Problem - What's Actually Happening

```
┌─────────────────────────────────────────────────────────────────┐
│                    ACTUAL BEHAVIOR (FAILING)                     │
└─────────────────────────────────────────────────────────────────┘

1. User clicks "Download" button
   │
   ├─> PCF Control tries to get user token:
   │
   ├─> Method 1: context.utils.getAccessToken()
   │   └─> ❌ Returns: undefined (not available in model-driven apps)
   │
   ├─> Method 2: context.page.getAccessToken()
   │   └─> ❌ Returns: undefined (not available in model-driven apps)
   │
   ├─> Method 3: context.accessToken or context.token
   │   └─> ❌ Returns: undefined (not exposed in PCF API)
   │
   ├─> Method 4: Call WhoAmI API to extract token
   │   └─> ❌ WhoAmI returns 200 OK but NO Authorization header
   │
   └─> RESULT: Error: "Unable to retrieve access token from PCF context"

2. File operation fails before even calling Spe.Bff.Api
```

**Console Error:**
```
[UniversalDatasetGrid][SdapApiClientFactory] No Authorization header found in WhoAmI response
[UniversalDatasetGrid][SdapApiClientFactory] Failed to retrieve access token
Error: Unable to retrieve access token from PCF context
```

---

## Why PCF Controls Can't Get Tokens (By Design)

### Power Apps Security Model

**Model-Driven Apps run in a sandboxed environment:**

1. **Implicit Authentication**: Users are already authenticated to Power Apps/Dataverse
2. **Cookie-Based Sessions**: Authentication uses httpOnly cookies (not accessible to JavaScript)
3. **No Token Exposure**: For security, tokens are NOT exposed to client-side JavaScript
4. **Controlled API Access**: PCF controls can only call Dataverse APIs via `context.webAPI`

### What PCF Controls CAN Do

✅ **Call Dataverse Web API**:
```typescript
context.webAPI.retrieveRecord("account", id)
// This works - uses implicit authentication (cookies)
// No token needed - framework handles it
```

✅ **Call Dataverse Custom APIs**:
```typescript
context.webAPI.execute({
    name: "new_MyCustomApi",
    parameters: { ... }
})
// This works - server-side API can do authenticated calls
```

### What PCF Controls CANNOT Do

❌ **Call External Authenticated APIs Directly**:
```typescript
fetch("https://my-api.com/data", {
    headers: {
        "Authorization": "Bearer ???" // No way to get this token
    }
})
// Fails - no access to Azure AD tokens
```

---

## Why This Worked in Sprint 6 But Not Now

### Sprint 6 (October 4, 2025)

**What we did:**
- Deployed the PCF control UI
- Added command bar with buttons
- Tested that buttons **appeared and were clickable**

**What we DIDN'T do:**
- Actually click the buttons to perform file operations
- Test authentication flow
- Call Spe.Bff.Api from the PCF control

**From Sprint 6 docs:**
> "Next phase (Sprint 6 Phase 3) will integrate SDAP API for actual file operations (upload, download, remove, update)."

**Sprint 6 Phase 3 was never completed** - we moved to other sprints.

### Sprint 7A (October 6, 2025) - TODAY

**What we did:**
- Implemented Download, Delete, Replace button handlers
- Added SdapApiClient to call Spe.Bff.Api
- Deployed the control

**What we discovered:**
- File operations don't work
- Authentication token retrieval fails
- **This is the FIRST TIME we tried to actually call Spe.Bff.Api from PCF**

---

## Solution Options - Detailed Analysis

### Option 1: MSAL.js in PCF Control (Client-Side Token Acquisition)

**How it works:**

```typescript
// Install: npm install @azure/msal-browser

import { PublicClientApplication } from '@azure/msal-browser';

// Initialize MSAL in PCF control
const msalConfig = {
    auth: {
        clientId: "YOUR_CLIENT_ID",
        authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
        redirectUri: window.location.origin
    }
};

const msalInstance = new PublicClientApplication(msalConfig);

// Get token
const token = await msalInstance.acquireTokenSilent({
    scopes: ["api://YOUR_BFF_API_CLIENT_ID/.default"]
});

// Call Spe.Bff.Api
fetch("https://spe-api-dev.../api/drives/...", {
    headers: {
        "Authorization": `Bearer ${token.accessToken}`
    }
});
```

**Pros:**
- ✅ Direct solution - PCF gets tokens independently
- ✅ No middle layer needed
- ✅ User identity preserved end-to-end
- ✅ Standard Microsoft pattern (MSAL is official library)
- ✅ Works for any external API, not just Spe.Bff.Api

**Cons:**
- ❌ Requires app registration configuration in Azure AD
- ❌ Adds ~100KB to bundle size
- ❌ User may see consent prompts (first time)
- ❌ More complex error handling (token expiration, refresh, etc.)
- ❌ Requires configuring redirect URIs for all Power Apps URLs
- ❌ May have issues with pop-up blockers
- ❌ Additional security review needed (exposing client ID in browser)

**Effort:**
- 4-6 hours implementation
- 2-3 hours testing and troubleshooting
- Moderate complexity

**Risk:**
- Medium - MSAL is well-documented but requires careful configuration
- Redirect URI configuration can be tricky in Power Apps

---

### Option 2: Dataverse Custom API Proxy (Server-Side)

**How it works:**

```
┌─────────────────────────────────────────────────────────────────┐
│                    CUSTOM API PROXY PATTERN                      │
└─────────────────────────────────────────────────────────────────┘

1. PCF Control
   └─> context.webAPI.execute({
           name: "sprk_DownloadFile",
           parameters: {
               driveId: "...",
               itemId: "..."
           }
       })
       // No token needed - Dataverse handles authentication

2. Custom API (C# Plugin running in Dataverse)
   ├─> Receives request from PCF (already authenticated)
   ├─> Gets user identity from Dataverse context
   ├─> Uses MSAL.NET to acquire token for user
   ├─> Calls Spe.Bff.Api with token:
   │   GET https://spe-api-dev.../api/drives/.../items/.../content
   │   Headers: Authorization: Bearer <TOKEN>
   ├─> Receives file stream from Spe.Bff.Api
   └─> Returns file data to PCF control

3. PCF Control receives file → triggers browser download
```

**Implementation:**

```csharp
// Custom API: sprk_DownloadFile
public class DownloadFileCustomApi : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        // Get parameters
        var driveId = context.InputParameters["driveId"] as string;
        var itemId = context.InputParameters["itemId"] as string;

        // Get user identity (already authenticated by Dataverse)
        var userId = context.InitiatingUserId;

        // Acquire token using MSAL (server-side)
        var app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .Build();

        var token = await app.AcquireTokenOnBehalfOf(
            scopes: new[] { "api://spe-bff-api/.default" },
            userAssertion: new UserAssertion(userToken)
        ).ExecuteAsync();

        // Call Spe.Bff.Api
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var response = await httpClient.GetAsync(
            $"https://spe-api-dev.../api/drives/{driveId}/items/{itemId}/content"
        );

        // Return file data
        var fileBytes = await response.Content.ReadAsByteArrayAsync();
        context.OutputParameters["fileData"] = Convert.ToBase64String(fileBytes);
    }
}
```

**Pros:**
- ✅ Clean separation - auth logic on server
- ✅ No client-side token management
- ✅ No bundle size increase in PCF
- ✅ No user consent prompts
- ✅ Uses existing Dataverse security model
- ✅ Server-side logging and monitoring
- ✅ Easier to update (no PCF redeployment for auth changes)

**Cons:**
- ❌ Requires creating 4 Custom APIs (Download, Delete, Replace, Upload)
- ❌ More code to maintain (plugins + PCF)
- ❌ Custom API development and deployment complexity
- ❌ Potential performance overhead (extra hop through Dataverse)
- ❌ Limited by Custom API size limits (32MB response)
- ❌ Requires plugin registration and solution deployment

**Effort:**
- 8-12 hours implementation (4 Custom APIs + plugin code)
- 4-6 hours testing
- High complexity

**Risk:**
- Medium-High - Custom APIs and plugins require careful testing
- Deployment complexity increases

---

### Option 3: Hybrid - Use Dataverse Token Endpoint (If Available)

**Research needed**: Check if Power Apps provides a token endpoint that PCF controls can call

**How it might work:**

```typescript
// Hypothetical - need to verify if this exists
const tokenResponse = await fetch(
    `${context.page.getClientUrl()}/api/oauth/token`,
    {
        method: 'POST',
        credentials: 'include' // Use existing session
    }
);

const { access_token } = await tokenResponse.json();
```

**Pros:**
- ✅ Simpler than MSAL if available
- ✅ Uses existing Power Apps session
- ✅ No additional app registration

**Cons:**
- ❌ Might not exist (need to research)
- ❌ Undocumented = unsupported

**Effort:**
- 1-2 hours research
- 2-4 hours implementation if it exists
- Unknown complexity

**Risk:**
- High - May not exist or be unsupported

---

### Option 4: Use Dataverse OAuth Flow with Hidden iFrame

**How it works:**

```typescript
// Create hidden iframe pointing to Dataverse OAuth endpoint
const iframe = document.createElement('iframe');
iframe.style.display = 'none';
iframe.src = `${context.page.getClientUrl()}/api/data/v9.2/WhoAmI`;

// Listen for message with token
window.addEventListener('message', (event) => {
    if (event.origin === context.page.getClientUrl()) {
        const token = event.data.token;
        // Use token...
    }
});
```

**Pros:**
- ✅ Uses existing Dataverse session
- ✅ No separate MSAL configuration

**Cons:**
- ❌ Hacky approach
- ❌ May violate Power Apps terms
- ❌ Fragile - could break with updates
- ❌ Security concerns

**Effort:**
- 4-6 hours experimentation
- High complexity

**Risk:**
- Very High - Unsupported pattern, may break

---

### Option 5: Temporary Test Endpoint (Not for Production)

**For testing purposes only** - create an unauthenticated endpoint in Spe.Bff.Api

**How it works:**

```csharp
// In Spe.Bff.Api/Program.cs
app.MapGet("/api/test/drives/{driveId}/items/{itemId}/content",
    [AllowAnonymous] // NO AUTHENTICATION
    async (string driveId, string itemId, ISpeService speService) =>
    {
        // Use service account credentials (not user)
        var file = await speService.DownloadFileAsync(driveId, itemId);
        return Results.File(file, "application/octet-stream");
    }
);
```

**Pros:**
- ✅ Immediate testing capability
- ✅ Zero PCF changes needed
- ✅ Proves the rest of the architecture works

**Cons:**
- ❌ **SECURITY RISK** - No authentication
- ❌ Cannot use for production
- ❌ Loses user identity (audit trail)
- ❌ Bypasses OBO flow

**Effort:**
- 30 minutes implementation
- Low complexity

**Risk:**
- High - Must be removed before production

---

## Recommended Solution

### **RECOMMENDATION: Option 2 - Dataverse Custom API Proxy**

**Rationale:**

1. **Best Security**: Token management stays server-side
2. **Best User Experience**: No consent prompts, uses existing Dataverse auth
3. **Best Maintainability**: Auth logic separated from UI, easier to update
4. **Enterprise Pattern**: Follows Microsoft's recommended architecture for LOB apps
5. **Audit Trail**: Full user identity preserved through Dataverse → Custom API → Spe.Bff.Api → SPE

**Trade-offs Accepted:**
- More initial development time (8-12 hours vs 4-6 hours)
- Slightly higher complexity
- **But**: Better long-term maintenance and security

**Implementation Plan:**

### Phase 1: Create Custom APIs (Week 1)

1. **sprk_DownloadFile**
   - Input: driveId, itemId, fileName
   - Output: fileData (base64), mimeType
   - Calls: GET /api/drives/{driveId}/items/{itemId}/content

2. **sprk_DeleteFile**
   - Input: documentId, driveId, itemId
   - Output: success (boolean)
   - Calls: DELETE /api/drives/{driveId}/items/{itemId}

3. **sprk_ReplaceFile**
   - Input: documentId, driveId, itemId, fileData (base64), fileName
   - Output: newMetadata (FileHandleDto)
   - Calls: DELETE + PUT /api/drives/{driveId}/upload

4. **sprk_UploadFile** (future)
   - Input: driveId, fileData, fileName
   - Output: metadata (FileHandleDto)
   - Calls: PUT /api/drives/{driveId}/upload

### Phase 2: Update PCF Control (Week 1)

1. Replace SdapApiClient with DataverseCustomApiClient
2. Update button handlers to call Custom APIs via context.webAPI
3. Remove token acquisition logic (not needed)
4. Update error handling

### Phase 3: Testing (Week 1)

1. Unit tests for Custom APIs
2. Integration tests PCF → Custom API → Spe.Bff.Api
3. End-to-end user testing in Power Apps
4. Security review

---

## Alternative: Quick Win with Option 1 (MSAL)

**If you need faster results:**

Option 1 (MSAL) can be implemented faster (4-6 hours) and gets us working file operations sooner.

**Then migrate to Option 2 later** for better security and maintainability.

---

## Questions for Decision

1. **Timeline**: Do we need file operations working this week, or can we wait for proper Custom API implementation?

2. **Security Requirements**: Is it acceptable to have MSAL running in the browser (client-side token acquisition)?

3. **Maintenance Preference**: Do you prefer simpler client-side code (MSAL) or server-side security (Custom API)?

4. **Future Scope**: Will we need other file operations beyond Download/Delete/Replace? (This affects architecture choice)

5. **Testing Approach**: Should we implement Option 5 (temporary test endpoint) first to validate the rest of the architecture works?

---

## Next Steps Based on Your Decision

**If Option 1 (MSAL):**
1. Create Azure AD app registration for PCF control
2. Install @azure/msal-browser package
3. Implement token acquisition in SdapApiClientFactory
4. Configure scopes and permissions
5. Test authentication flow
6. Deploy and test file operations

**If Option 2 (Custom API):**
1. Design Custom API schemas
2. Create plugin project
3. Implement 4 Custom APIs (Download, Delete, Replace, Upload)
4. Register plugins in Dataverse
5. Update PCF control to call Custom APIs
6. Deploy and test end-to-end

**If Option 5 (Test endpoint first):**
1. Add unauthenticated test endpoint to Spe.Bff.Api
2. Update PCF control to call test endpoint
3. Verify file operations work
4. Then implement proper auth (Option 1 or 2)
5. Remove test endpoint

---

## Impact on Sprint 7A

**Current Sprint 7A Status:**
- ✅ UI Complete (buttons, grid, dialogs)
- ❌ File Operations Blocked (authentication issue)

**Options:**

**A) Close Sprint 7A as "UI Complete, Auth Pending"**
- Document the authentication limitation
- Create Sprint 7B for authentication implementation
- Deliverable: Working UI, buttons clickable, architecture documented

**B) Extend Sprint 7A to include authentication**
- Implement chosen auth solution
- Complete end-to-end file operations
- Deliverable: Fully working Download, Delete, Replace

**C) Split into phases**
- Sprint 7A: UI + temporary test endpoint (proves it works)
- Sprint 7B: Production authentication (Custom API or MSAL)

---

## My Recommendation

**Short Term (This Week):**
1. Implement **Option 5** (test endpoint) - 30 minutes
2. Verify all file operations work end-to-end
3. Close Sprint 7A as complete with working demo
4. Document authentication as "known limitation, production solution pending"

**Medium Term (Next Week):**
5. Start Sprint 7B: Implement **Option 2** (Custom API proxy)
6. Replace test endpoint with production Custom APIs
7. Full security review and testing

**Why This Approach:**
- ✅ Quick validation that architecture works
- ✅ Sprint 7A delivers working demo
- ✅ Proper production solution next sprint
- ✅ Separates UI work from auth infrastructure work
- ✅ Lower risk - incremental progress

---

**What would you like to do?**
