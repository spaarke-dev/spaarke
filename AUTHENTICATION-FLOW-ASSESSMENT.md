# Authentication Flow Assessment - Current Configuration

**Date**: 2025-10-13
**Purpose**: Document actual authentication flow, identify 500 error root cause
**Status**: 🔴 OBO Flow Failing with AADSTS50013

---

## Executive Summary

The PCF control successfully acquires tokens and sends them to the API. The API successfully validates the JWT. However, **the API fails during the On-Behalf-Of (OBO) token exchange** when trying to obtain a Graph token to upload files to SharePoint Embedded.

**Error**: `AADSTS50013: Assertion failed signature validation`
**Root Cause**: TBD - Need to trace OBO configuration

---

## App Registrations - ACTUAL Configuration

You provided two app registrations:

### 1. SPE-BFF-API (Backend API)
- **Application (client) ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Purpose**: Receives user tokens from PCF, validates JWT, performs OBO to Graph
- **Scope Exposed**: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
- **Client Secret**: `CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy`
- **Secret ID**: `3d09b386-89f0-41e2-902c-ed38a6ab1646`
- **API Permissions**: ✅ Microsoft Graph permissions with admin consent (per your statement)

### 2. Spaarke DSM-SPE Dev 2 (SPE/Graph Operations)
- **Application (client) ID**: `170c98e1-d486-4355-bcbe-170454e0207c`
- **Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Purpose**: Used for SPE/Graph API operations, Dataverse authentication
- **Client Secret**: `~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj`
- **Secret ID**: `40fcc0c4-4d60-4526-b303-be592f11314e`
- **Supported Account Types**: Multiple organizations

---

## Complete Authentication Flow - Step by Step

### Step 1: PCF Control Token Acquisition ✅ WORKING

**Component**: `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts`

```typescript
// Line 30 - Client ID (PCF runs in Dataverse context)
const CLIENT_ID = "170c98e1-d486-4355-bcbe-170454e0207c";

// Line 218 - Requested scope (for BFF API)
scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"],
```

**Process**:
1. User opens Form Dialog with PCF control
2. MSAL.js initializes with `ClientId: 170c98e1...`
3. MSAL calls Azure AD to get token for scope: `api://1e40baad.../user_impersonation`
4. Azure AD issues **Token A** (JWT) with:
   - `aud` (audience): `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - `appid`: `170c98e1-d486-4355-bcbe-170454e0207c` (PCF app)
   - `scp`: `user_impersonation`

**Status**: ✅ **Working** - Console logs show token acquired successfully

---

### Step 2: API JWT Validation ✅ WORKING

**Component**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
// Lines 67-68 - JWT Bearer authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
```

**Azure App Service Configuration**:
```json
{
  "AzureAd__ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "AzureAd__Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "AzureAd__TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
}
```

**Process**:
1. PCF sends `PUT /api/obo/containers/.../files/...` with `Authorization: Bearer {Token A}`
2. ASP.NET Core authentication middleware validates Token A:
   - Checks `aud` matches `AzureAd__Audience` ✅
   - Verifies signature with Azure AD public keys ✅
   - Validates expiration, issuer, etc. ✅
3. Request proceeds to OBOEndpoints

**Status**: ✅ **Working** - API accepts the token (no 401 Unauthorized)

---

### Step 3: OBO Token Exchange ❌ FAILING HERE

**Component**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

```csharp
// Lines 38-60 - ConfidentialClientApplication setup
var tenantId = configuration["TENANT_ID"]; // a221a95e-6abc-4434-aecc-e48338a1b2f2
var apiAppId = configuration["API_APP_ID"]; // 1e40baad-e065-4aea-a8d4-4b7ab273458c
var clientSecret = configuration["API_CLIENT_SECRET"]; // CBi8Q~v52...

var builder = ConfidentialClientApplicationBuilder
    .Create(apiAppId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}");

if (!string.IsNullOrWhiteSpace(clientSecret))
    builder = builder.WithClientSecret(clientSecret);

_cca = builder.Build();
```

**Azure App Service Configuration**:
```json
{
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy"
}
```

**OBO Token Exchange** (Lines 139-149):
```csharp
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
{
    // userAccessToken = Token A (from PCF)

    var result = await _cca.AcquireTokenOnBehalfOf(
        new[] {
            "https://graph.microsoft.com/Sites.FullControl.All",
            "https://graph.microsoft.com/Files.ReadWrite.All"
        },
        new UserAssertion(userAccessToken)
    ).ExecuteAsync();

    // Should return Token B (Graph API token) but throws exception
}
```

**Process**:
1. API receives Token A (audience: `api://1e40baad...`)
2. API creates ConfidentialClientApplication with:
   - ClientId: `1e40baad-e065-4aea-a8d4-4b7ab273458c` (SPE-BFF-API)
   - ClientSecret: `CBi8Q~v52...`
3. API calls Azure AD: "Exchange Token A for Token B (Graph scope)"
4. **Azure AD returns error: AADSTS50013**

**Error Details**:
```
AADSTS50013: Assertion failed signature validation.
[Reason - The key was not found., Thumbprint of key used by client: '1FD9E3E40392B30329860D52171EE3695FA507DC']
```

**Status**: ❌ **FAILING** - OBO exchange rejected by Azure AD

---

### Step 4: Graph API Call ⏸️ NEVER REACHED

**Component**: `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

```csharp
// Line 237 - Would be called if OBO succeeded
public async Task<SpeFileMetadata> UploadSmallAsUserAsync(
    string userToken, string containerId, string path, Stream content, CancellationToken ct)
{
    var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);
    // ... use Token B to upload file to SharePoint Embedded
}
```

**Process** (if OBO worked):
1. Use Token B (Graph token with user context) to call:
   ```
   PUT https://graph.microsoft.com/beta/storage/fileStorage/containers/{containerId}/drive/items/root:/{path}:/content
   ```
2. SharePoint Embedded validates Token B
3. File uploaded with user's permissions
4. Return file metadata

**Status**: ⏸️ **Never reached** due to Step 3 failure

---

## Error Analysis

### The AADSTS50013 Error

**Full Error**:
```
AADSTS50013: Assertion failed signature validation.
[Reason - The key was not found., Thumbprint of key used by client: '1FD9E3E40392B30329860D52171EE3695FA507DC']
```

### What This Means

Azure AD is saying:
1. ✅ It received the OBO request
2. ✅ Token A is valid
3. ❌ But when trying to validate **something**, the signing key wasn't found

### Possible Causes

**Theory 1: Token A signature issue**
- Token A was signed with a key Azure AD doesn't recognize
- But this doesn't make sense because Step 2 (JWT validation) succeeded

**Theory 2: Client assertion issue**
- The `ClientSecret` authentication creates a client assertion
- Azure AD can't validate the client assertion signature
- This could mean:
  - Wrong client secret for `1e40baad...`
  - Client secret expired
  - App registration misconfigured

**Theory 3: Multi-tenant vs Single-tenant mismatch**
- PCF app (`170c98e1...`) is "Multiple organizations"
- BFF API app (`1e40baad...`) might be "Single tenant"
- Token A was issued by multi-tenant endpoint
- OBO requires single-tenant token

**Theory 4: Missing knownClientApplications**
- The BFF API (`1e40baad...`) might need to declare the PCF app (`170c98e1...`) as a known client
- This is configured in the app registration manifest

---

## Configuration Verification Needed

### 1. SPE-BFF-API App Registration (`1e40baad...`)

**Need to verify in Azure Portal**:

- [ ] **API permissions**:
  - [ ] Microsoft Graph → Delegated → `Files.ReadWrite.All` ✓
  - [ ] Microsoft Graph → Delegated → `Sites.FullControl.All` ✓
  - [ ] Admin consent granted ✓

- [ ] **Expose an API**:
  - [ ] Application ID URI: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
  - [ ] Scope: `user_impersonation` defined and enabled

- [ ] **Authentication**:
  - [ ] Supported account types: **Single tenant only** (not multi-tenant)
  - [ ] No redirect URIs needed (API registration)

- [ ] **Certificates & secrets**:
  - [ ] Client secret `CBi8Q~v52...` exists and not expired
  - [ ] Secret ID: `3d09b386-89f0-41e2-902c-ed38a6ab1646`

- [ ] **Manifest** (Critical for OBO):
  - [ ] `"knownClientApplications": ["170c98e1-d486-4355-bcbe-170454e0207c"]`
  - [ ] This tells Azure AD that PCF app (`170c98e1...`) is allowed to get tokens for this API

### 2. Spaarke DSM-SPE Dev 2 App Registration (`170c98e1...`)

**Need to verify in Azure Portal**:

- [ ] **API permissions**:
  - [ ] SPE-BFF-API (`1e40baad...`) → Delegated → `user_impersonation` ✓
  - [ ] Admin consent granted ✓

- [ ] **Authentication**:
  - [ ] Supported account types: Currently "Multiple organizations"
  - [ ] Should this be **Single tenant** to match BFF API?
  - [ ] Redirect URI: `https://spaarkedev1.crm.dynamics.com` ✓

- [ ] **Certificates & secrets**:
  - [ ] Client secret `~Ac8Q~JGns...` exists (for Dataverse/Graph operations)

---

## Most Likely Root Cause

Based on the error and OBO flow requirements, the most likely issue is:

### Missing `knownClientApplications` in SPE-BFF-API Manifest

**The Problem**:
When the PCF app (`170c98e1...`) requests a token for the BFF API (`1e40baad...`), Azure AD issues Token A. When the BFF API tries to use Token A in an OBO flow, Azure AD checks: "Is this client app (`170c98e1...`) authorized to act on behalf of users for this API?"

If the BFF API manifest doesn't list `170c98e1...` in `knownClientApplications`, the OBO flow fails.

**The Fix**:
1. Go to Azure Portal → App Registrations → **SPE-BFF-API** (`1e40baad...`)
2. Click **Manifest**
3. Find the `knownClientApplications` array (should be near top)
4. Add the PCF app ID:
   ```json
   "knownClientApplications": [
     "170c98e1-d486-4355-bcbe-170454e0207c"
   ],
   ```
5. Click **Save**
6. Wait 5-10 minutes for propagation
7. Test upload again

### Alternative Causes (Less Likely)

1. **Multi-tenant vs Single-tenant mismatch**
   - PCF app is multi-tenant, BFF API is single-tenant
   - Token issued by `https://login.microsoftonline.com/organizations`
   - OBO expects token from `https://login.microsoftonline.com/{tenantId}`

2. **Client secret mismatch**
   - The secret `CBi8Q~v52...` doesn't actually belong to `1e40baad...`
   - But you confirmed this is correct

3. **Admin consent not actually granted**
   - Graph permissions show green checkmark but consent not effective
   - Need to re-grant admin consent

---

## Next Steps - Verification Plan

### Phase 1: Verify Configuration (No Changes)

1. **Check SPE-BFF-API Manifest**:
   ```bash
   az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "{knownClientApplications:knownClientApplications, signInAudience:signInAudience}"
   ```

   Expected:
   ```json
   {
     "knownClientApplications": ["170c98e1-d486-4355-bcbe-170454e0207c"],
     "signInAudience": "AzureADMyOrg"
   }
   ```

2. **Check API Permissions**:
   ```bash
   az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
   ```

   Should show Graph permissions with admin consent.

3. **Check Client Secret Validity**:
   ```bash
   az ad app credential list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "[?keyId=='3d09b386-89f0-41e2-902c-ed38a6ab1646']"
   ```

   Verify `endDateTime` is in the future.

### Phase 2: Fix Based on Findings

Based on Phase 1 results, we'll know which of these needs to be fixed:
- Add `knownClientApplications` to manifest
- Change `signInAudience` to single-tenant
- Re-grant admin consent to Graph permissions
- Update client secret if expired

---

## Current Code References

### PCF Control Configuration
- **File**: `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts`
- **ClientId**: Line 30 → `170c98e1-d486-4355-bcbe-170454e0207c`
- **Scope**: Line 218 → `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`

### API JWT Validation
- **File**: `src/api/Spe.Bff.Api/Program.cs`
- **Configuration**: Lines 67-68
- **Azure Settings**: `AzureAd__ClientId`, `AzureAd__Audience`, `AzureAd__TenantId`

### API OBO Implementation
- **File**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- **ConfidentialClient Setup**: Lines 38-60
- **OBO Token Exchange**: Lines 139-149
- **Azure Settings**: `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET`

### Upload Implementation
- **File**: `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`
- **Upload Method**: Line 237 (UploadSmallAsUserAsync)
- **Calls**: GraphClientFactory.CreateOnBehalfOfClientAsync

---

## Summary

**What's Working**: ✅
1. PCF token acquisition
2. API JWT validation
3. Request routing to OBO endpoint

**What's Failing**: ❌
1. OBO token exchange (AADSTS50013)

**Most Likely Fix**:
Add `"knownClientApplications": ["170c98e1-d486-4355-bcbe-170454e0207c"]` to SPE-BFF-API manifest.

**Next Action**:
Run verification commands to confirm configuration before making any changes.
