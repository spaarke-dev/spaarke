# OBO 403 Forbidden Root Cause Analysis

**Date:** 2025-10-09
**Status:** 🔴 BLOCKER IDENTIFIED
**Issue:** HTTP 403 Forbidden when uploading files to SharePoint Embedded via OBO flow

---

## Executive Summary

The 403 Forbidden error is caused by **missing container type application permissions** for the BFF API application. SharePoint Embedded validates the `appid` claim in Token B to determine which application is making the request, and that application must be explicitly granted permissions on the container type.

### Root Cause

**Token B has `appid: 1e40baad-e065-4aea-a8d4-4b7ab273458c` (BFF API)**

This is **correct and expected behavior** in OAuth2 OBO flow. However, SharePoint Embedded requires that the BFF API application be registered with the container type and granted appropriate permissions.

### Solution Required

Register the BFF API application (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) with the SPE container type (`8a6ce34c-6055-4681-8f87-2f4f9f921c06`) and grant delegated permissions for `WriteContent`.

---

## Detailed Analysis

### 1. OAuth2 OBO Flow Behavior

#### Expected Behavior (Confirmed)

When using OAuth2 On-Behalf-Of flow:

1. **Token A** contains:
   - `aud`: BFF API application ID (`api://1e40baad-e065-4aea-a8d4-4b7ab273458c`)
   - `appid`: PCF Control application ID (`170c98e1-d486-4355-bcbe-170454e0207c`)
   - User claims: `upn`, `oid`, etc.

2. **Token B** (OBO token) contains:
   - `aud`: Microsoft Graph (`https://graph.microsoft.com`)
   - **`appid`: BFF API application ID (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)** ← This is correct!
   - User claims: `upn`, `oid`, etc. (preserved from Token A)
   - `scp`: Delegated permissions requested by BFF API

**Key Finding:** Token B correctly shows the BFF API's `appid` because the BFF API is the confidential client making the Graph API call on behalf of the user.

#### Microsoft Documentation Confirmation

From [Microsoft Learn - OBO Flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow):

> "The middle-tier service authenticates to the Microsoft identity platform token issuance endpoint and requests a token to access API B."

The middle-tier service (BFF API) is the authenticated party making the downstream call, so its application ID appears in the token.

From [Access Token Claims Reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference):

> `appid` (v1.0) / `azp` (v2.0): "The application ID of the client using the token"

In OBO flow, the "client using the token" is the middle-tier API (BFF API), not the original client (PCF Control).

### 2. SharePoint Embedded Permission Model

#### Container Type Application Permissions

From [Microsoft Learn - SPE Application Permissions](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/register-api-documentation):

> "Applications need container type application permissions to access containers of that container type."

> "SharePoint Embedded applications need to be granted container type application permissions by the owner application before they can access containers of the given container type."

**Critical Requirement:** Any application that calls Graph API to access SPE containers must be registered with the container type, even in delegated (OBO) scenarios.

#### Which Application Needs Registration?

When using OBO flow:
- ✅ **BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)** - Must be registered (it's the `appid` in Token B)
- ❌ **PCF Control (`170c98e1-d486-4355-bcbe-170454e0207c`)** - NOT required (never calls Graph directly)

**Why:** SharePoint Embedded validates the `appid` claim in the token making the Graph API call. Since Token B has the BFF API's `appid`, SPE checks if the BFF API has container type permissions.

#### Permission Levels

From the registration API documentation, available delegated permission levels:
- `ReadContent` - Read files
- `WriteContent` - Create/update files
- `Delete` - Delete files
- `Full` - All operations

For file upload, we need **`WriteContent`** delegated permission.

### 3. Current State Assessment

#### What's Working ✅

1. **PCF Control → BFF API authentication**
   - Token A correctly issued with BFF API as audience
   - Token A includes user identity and PCF app as requesting application
   - Authorization header correctly formatted

2. **OBO Token Exchange**
   - MSAL successfully exchanges Token A for Token B
   - Token B has all required Graph scopes:
     - `FileStorageContainer.Selected`
     - `Sites.FullControl.All`
     - `Files.ReadWrite.All`
   - User identity preserved in Token B (`upn`, `oid`)
   - Token is delegated (`idtyp: user`)

3. **User Permissions**
   - User (ralph.schroeder@spaarke.com) has `owner` role on the container
   - Confirmed via GET `/storage/fileStorage/containers/{id}/permissions`

#### What's Broken ❌

**Container Type Application Permissions**

The BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) is **not registered** with the SPE container type (`8a6ce34c-6055-4681-8f87-2f4f9f921c06`).

When SharePoint Embedded receives the Graph API call:
1. Validates Token B signature ✅
2. Checks user permissions on container ✅
3. **Checks if application (`appid` claim) has container type permissions** ❌ ← **FAILS HERE**
4. Returns 403 Forbidden

### 4. Evidence Supporting This Diagnosis

#### Token B Analysis

Captured from Application Insights logs:

```json
{
  "aud": "https://graph.microsoft.com",
  "iss": "https://sts.windows.net/a221a95e-6abc-4434-aecc-e48338a1b2f2/",
  "appid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",  // BFF API
  "app_displayname": "SPE-BFF-API",
  "upn": "ralph.schroeder@spaarke.com",             // User preserved
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",    // User preserved
  "scp": "email FileStorageContainer.Selected Files.ReadWrite.All Mail.Send openid profile Sites.FullControl.All User.Read",
  "idtyp": "user"                                    // Delegated token
}
```

**Analysis:**
- ✅ `appid` correctly shows BFF API (expected in OBO)
- ✅ User identity preserved
- ✅ All required scopes present
- ✅ Delegated token (not app-only)
- ❌ BFF API not registered with container type

#### Error Pattern

Graph API response:
```
HTTP 403 Forbidden
{
  "error": {
    "code": "accessDenied",
    "message": "Access denied"
  }
}
```

This matches the SPE documentation pattern for "application lacks container type permissions."

### 5. Comparison with Working Postman Test

When the user successfully accesses the container via Postman:

**Postman Token:**
- Acquired using PCF app credentials (`170c98e1-d486-4355-bcbe-170454e0207c`)
- `appid`: PCF app
- Postman calls Graph API directly (no middle tier)
- PCF app IS registered with container type (user granted permissions to it)

**OBO Token B:**
- Acquired using BFF API credentials (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)
- `appid`: BFF API
- BFF API calls Graph API on behalf of user
- BFF API is NOT registered with container type ❌

---

## Solution

### Step 1: Register BFF API with Container Type

Use the SharePoint Container Type Registration API to grant the BFF API application permissions.

**Prerequisites:**
- Owning application service principal must exist in tenant
- Must have `Container.Selected` app-only permission
- Must use certificate or client secret authentication

**API Call:**

```http
PUT https://[tenant].sharepoint.com/_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
Authorization: Bearer [token-with-Container.Selected]
Content-Type: application/json

{
  "value": [
    {
      "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
      "delegated": ["WriteContent"],
      "appOnly": []
    }
  ]
}
```

**Parameters:**
- `appId`: BFF API application ID
- `delegated`: Permissions for delegated (user) calls - need `WriteContent` for file upload
- `appOnly`: Permissions for app-only calls - empty for now (BFF only does OBO)

**Permission Levels:**
- `WriteContent` - Sufficient for file upload
- `ReadContent` - If we also need read operations
- `Full` - If we need all operations

### Step 2: Verify Registration

After registering, verify the BFF API appears in the container type's application permissions:

```http
GET https://[tenant].sharepoint.com/_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
Authorization: Bearer [token-with-Container.Selected]
```

Expected response should include:
```json
{
  "value": [
    {
      "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
      "delegated": ["WriteContent"],
      "appOnly": []
    }
  ]
}
```

### Step 3: Test File Upload

After registration, the OBO file upload should work immediately (no code changes needed):

```http
PUT /api/obo/containers/{containerId}/files/test.txt
Authorization: Bearer [Token A from PCF]
Content-Type: text/plain

Test file content
```

Expected result: HTTP 200 OK with file metadata.

---

## Implementation Notes

### Required Information

- **Container Type ID:** `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- **BFF API App ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Tenant SharePoint URL:** (Need to obtain)
- **Owning Application:** (Need to identify - which app owns this container type?)

### Who Can Perform Registration?

The registration API requires:
1. **Owning application credentials** - The application that originally created the container type
2. **`Container.Selected` app-only permission** on SharePoint resource (not Graph)
3. **Authentication via certificate or client secret**

### Automation Considerations

This registration should be done as part of environment setup:
1. Manual registration in Dev environment (first time)
2. Document process for Prod environment
3. Consider adding to deployment scripts or ARM templates

### Alternative Approaches (Not Recommended)

If container type registration is not feasible:

**Option A: Direct Graph Call from PCF**
- Remove BFF API from the flow
- PCF calls Graph API directly with Token A
- Pros: PCF app already has container permissions
- Cons: Loses BFF benefits (retry logic, monitoring, security)

**Option B: App-Only Graph Call**
- BFF uses Managed Identity (app-only) instead of OBO
- Pros: No container type registration needed (MI has all permissions)
- Cons: Loses user context, can't enforce user-level permissions

**Recommendation:** Proceed with container type registration (Option in Solution section). This maintains the security and architectural benefits of the OBO pattern.

---

## Testing Plan

### 1. Pre-Registration Test (Current State)

Confirm 403 error still occurs:

```bash
# Test OBO upload endpoint
curl -X PUT "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/test.txt" \
  -H "Authorization: Bearer [Token A]" \
  -H "Content-Type: text/plain" \
  -d "Test content"

# Expected: HTTP 403
```

### 2. Perform Registration

Execute PUT request to container type registration API (Step 1 in Solution).

### 3. Post-Registration Test

Retry same upload request:

```bash
# Same test as above
curl -X PUT "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/test.txt" \
  -H "Authorization: Bearer [Token A]" \
  -H "Content-Type: text/plain" \
  -d "Test content"

# Expected: HTTP 200 OK with file metadata
```

### 4. PCF Control Test

Test from actual UniversalQuickCreate control in Dataverse:
1. Open Quick Create form with control
2. Select file(s)
3. Click Save
4. Verify files uploaded successfully
5. Check Application Insights for success logs

---

## Lessons Learned

### OAuth2 OBO Flow

1. **Token B has middle-tier's appid** - This is correct and expected
2. **User identity is preserved** - `upn`, `oid` flow through the chain
3. **Delegated permissions are cumulative** - Token B has intersection of app permissions and user permissions

### SharePoint Embedded

1. **Application-level permissions required** - Not just user permissions
2. **Container type registration is mandatory** - Even for delegated scenarios
3. **Registration is per-application** - Each app in the call chain may need registration

### Architecture Implications

1. **BFF pattern requires extra setup** - Container type registration for middle tier
2. **Environment parity is critical** - Registration must occur in each environment
3. **Documentation must include deployment steps** - Not just code

---

## References

### Microsoft Documentation

1. [OAuth2 On-Behalf-Of Flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
2. [Access Token Claims Reference](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference)
3. [SharePoint Embedded Authentication](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/development/auth)
4. [Container Type Registration API](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/register-api-documentation)

### Related Files

- [c:\code_files\spaarke\docs\KM-V2-OAUTH2-OBO-FLOW.md](../../../docs/KM-V2-OAUTH2-OBO-FLOW.md)
- [c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs)
- [c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreate\services\auth\msalConfig.ts](../../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts)

---

## Next Steps

1. ✅ **Root cause identified** - Missing container type registration
2. ⏭️ **Obtain required information:**
   - Tenant SharePoint URL
   - Owning application credentials
   - Access to container type registration API
3. ⏭️ **Perform registration** - Register BFF API with container type
4. ⏭️ **Verify and test** - Confirm 403 error resolved
5. ⏭️ **Document deployment process** - Add to environment setup guides
6. ⏭️ **Plan for other environments** - Test, Staging, Production

**Status:** Ready to proceed with container type registration.
