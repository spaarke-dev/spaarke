# SDAP (SharePoint Document Access Proxy) - Simplified Design Specification

## Overview

A streamlined implementation of SharePoint Embedded file storage integration for Dynamics 365 Model Driven Apps using PCF controls and a BFF API with OAuth 2.0 On-Behalf-Of (OBO) flow.

**Reference Guide**: Based on [Set up the SharePoint Embedded App.md](./Set%20up%20the%20SharePoint%20Embedded%20App.md)

---

## Architecture Goals

### Core Principle: Follow the Reference Pattern

The reference guide demonstrates a proven SharePoint Embedded implementation with:
- **Client**: React SPA using MSAL.js for authentication
- **Server**: Node.js/Restify API using MSAL Node for OBO flow
- **Storage**: SharePoint Embedded Containers via Microsoft Graph

### Our Adaptation

- **Client**: PCF Control (TypeScript/React) in Model Driven App
- **Server**: ASP.NET Core BFF API (already exists)
- **Storage**: SharePoint Embedded Containers via Microsoft Graph

---

## Component Breakdown

### 1. Entra ID Application Setup

**Status**: ✅ Already exists (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)

**Required Permissions** (from reference guide lines 32-68):

#### Microsoft Graph (`00000003-0000-0000-c000-000000000000`)
- `FileStorageContainer.Selected` (Delegated) - `085ca537-6565-41c2-aca7-db852babc212`
- `FileStorageContainer.Selected` (Application) - `40dc41bc-0f7e-42ff-89bd-d9516947e474`

#### SharePoint Online (`00000003-0000-0ff1-ce00-000000000000`)
- `FileStorageContainer.Selected` (Delegated) - `4d114b1a-3649-4764-9dfb-be1e236ff371`
- `FileStorageContainer.Selected` (Application) - `19766c1b-905b-43af-8756-06526ab42875`

**Action Required**: Verify these permissions are added to the BFF API app registration manifest.

---

### 2. Container Type Setup

**Status**: ✅ Already exists (`8a6ce34c-6055-4681-8f87-2f4f9f921c06`)

**Reference**: Lines 101-138 of guide

**Validation Needed**:
```powershell
# Verify Container Type exists and is properly configured
Import-Module "Microsoft.Online.SharePoint.PowerShell"
Connect-SPOService -Url "https://spaarkedev1-admin.sharepoint.com"
Get-SPOContainerType -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06
```

**Expected Properties**:
- `ContainerTypeName`: SpaarkeDocuments (or similar)
- `OwningApplicationId`: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- `Classification`: Trial or Standard

---

### 3. Container Type Registration in Consumer Tenant

**Status**: ❓ Unknown - needs verification

**Reference**: Lines 139-206 of guide

**Critical Step**: Container Type must be registered in consumer tenant using SharePoint REST API with certificate authentication.

**Action Required**:
1. Verify certificate exists in Key Vault: `spe-app-cert`
2. Verify certificate is added to Entra ID app registration
3. Execute registration via PowerShell script (similar to existing `Register-BffApi-WithCertificate.ps1`)

**Endpoint** (from line 186):
```
POST /_api/v2.1/storageContainerTypes/{ContainerTypeId}/applicationPermissions
```

---

### 4. PCF Control (Client-Side)

**Status**: ✅ Partially implemented - needs alignment with reference pattern

**Reference Pattern** (lines 376-484): React SPA with MSAL authentication

**Current PCF Implementation**:
- Uses custom `MsalAuthProvider` wrapper
- Acquires tokens via `ssoSilent` flow
- Calls BFF API with Bearer token

**Alignment Needed**:

#### 4.1 Authentication Configuration
**Reference** (lines 388-398): Configure MSAL provider with correct scopes

```typescript
// PCF Control should request these scopes:
const scopes = [
  'openid',
  'profile',
  'offline_access',
  'User.Read',
  'Files.ReadWrite.All',
  'Sites.Read.All',
  'FileStorageContainer.Selected'  // Critical for SPE
];
```

**Action**: Verify PCF control requests `FileStorageContainer.Selected` scope during authentication.

#### 4.2 API Token Acquisition
**Reference** (lines 691-724): Use custom scope for calling web API

```typescript
// PCF should acquire token for BFF API with custom scope:
const apiScopes = [`api://${clientId}/Container.Manage`];
```

**Current Issue**: PCF may not be requesting the custom `Container.Manage` scope.

**Action**: Add custom scope acquisition in PCF control before calling upload endpoint.

---

### 5. BFF API (Server-Side)

**Status**: ✅ Exists - needs OBO flow verification

**Reference Pattern** (lines 488-537): Node.js API using MSAL ConfidentialClientApplication with OBO

**Current ASP.NET Implementation**: Uses `GraphClientFactory` and `UploadSessionManager`

**Critical OBO Implementation** (from reference lines 500-527):

#### 5.1 Extract User Token from Request
```typescript
// Reference pattern (line 513):
const [bearer, token] = (req.headers.authorization || '').split(' ');
```

**ASP.NET Equivalent**:
```csharp
// Should exist in OBOEndpoints.cs
var userToken = await HttpContext.GetTokenAsync("access_token");
```

**Action**: Verify BFF API extracts user's access token from Authorization header.

#### 5.2 Exchange for OBO Token
```typescript
// Reference pattern (lines 517-526):
const graphTokenRequest = {
  oboAssertion: token,
  scopes: [
    'Sites.Read.All',
    'FileStorageContainer.Selected'  // Must include this
  ]
};
const oboResponse = await confidentialClient.acquireTokenOnBehalfOf(graphTokenRequest);
```

**ASP.NET Equivalent**:
```csharp
// GraphClientFactory should use:
var scopes = new[] {
  "https://graph.microsoft.com/Sites.Read.All",
  "https://graph.microsoft.com/FileStorageContainer.Selected"
};
var oboToken = await _confidentialClient.AcquireTokenOnBehalfOf(scopes, userAssertion);
```

**Action**: Verify `GraphClientFactory` requests `FileStorageContainer.Selected` scope during OBO exchange.

#### 5.3 Call Microsoft Graph
```typescript
// Reference pattern (lines 530-537):
const authProvider = (callback) => callback(null, oboGraphToken);
const graphClient = MSGraph.Client.init({
  authProvider: authProvider,
  defaultVersion: 'beta'  // Important: SPE uses beta endpoint
});
```

**ASP.NET Equivalent**:
```csharp
// GraphClientFactory should create client with:
var graphClient = new GraphServiceClient(
  new DelegateAuthenticationProvider(async (request) => {
    request.Headers.Authorization =
      new AuthenticationHeaderValue("Bearer", oboToken);
  })
);
```

**Action**: Verify Graph client uses beta endpoint for SharePoint Embedded operations.

---

### 6. File Upload Flow

**Status**: ❌ Currently failing with HTTP 500

**Reference Pattern** (lines 987-1005): Upload file to Container using Graph client

#### 6.1 Upload Endpoint Pattern
```typescript
// Reference (line 993):
const endpoint = `/drives/${containerId}/items/${folderId || 'root'}:/${fileName}:/content`;
await graphClient.api(endpoint).putStream(fileContent);
```

**BFF API Implementation**:
```csharp
// OBOEndpoints.cs should use:
var endpoint = $"/drives/{containerId}/items/{folderId ?? "root"}:/{fileName}:/content";
await graphClient.Request(endpoint).PutAsync<DriveItem>(fileStream);
```

**Current Issue**: API crashes before reaching this code - authentication middleware failure.

#### 6.2 Upload Session for Large Files
**Reference Pattern**: Uses direct PUT for small files, upload sessions for large files

**Current Implementation**: Has `UploadSessionManager` for chunked uploads

**Action**: Verify upload endpoint logic matches reference pattern.

---

## Simplified Implementation Checklist

### Phase 1: Verify Foundation (No Code Changes)

- [ ] **Task 1.1**: Verify Entra ID app has all 4 SharePoint Embedded permissions
  - Microsoft Graph: `FileStorageContainer.Selected` (Delegated + Application)
  - SharePoint Online: `FileStorageContainer.Selected` (Delegated + Application)

- [ ] **Task 1.2**: Grant admin consent for new permissions
  - Navigate to API Permissions in Azure Portal
  - Click "Grant admin consent for [Tenant]"

- [ ] **Task 1.3**: Verify Container Type exists and matches app ID
  ```powershell
  Get-SPOContainerType -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06
  ```
  - Confirm `OwningApplicationId` = `1e40baad-e065-4aea-a8d4-4b7ab273458c`

- [ ] **Task 1.4**: Verify Container Type is registered in consumer tenant
  - Use Postman collection from SharePoint Embedded samples
  - Execute "Register ContainerType" request
  - Verify no "access denied" errors

### Phase 2: Align PCF Control (Client-Side Changes)

- [ ] **Task 2.1**: Add `FileStorageContainer.Selected` to PCF auth scopes
  ```typescript
  // In UniversalQuickCreate PCF control
  const scopes = [
    'User.Read',
    'Files.ReadWrite.All',
    'Sites.Read.All',
    'https://graph.microsoft.com/FileStorageContainer.Selected'  // Add this
  ];
  ```

- [ ] **Task 2.2**: Add custom `Container.Manage` scope for BFF API calls
  ```typescript
  // When calling BFF API, acquire token with:
  const apiToken = await msalInstance.acquireTokenSilent({
    scopes: [`api://1e40baad-e065-4aea-a8d4-4b7ab273458c/Container.Manage`]
  });
  ```

- [ ] **Task 2.3**: Update PCF to send correct token to BFF API
  - Ensure `Authorization: Bearer {apiToken}` header is set
  - Verify token contains `aud: api://1e40baad...`

### Phase 3: Align BFF API (Server-Side Changes)

- [ ] **Task 3.1**: Expose custom scope in Entra ID app
  - Add scope `Container.Manage` via "Expose an API"
  - Set admin consent required
  - Description: "Manage SharePoint Embedded Containers"

- [ ] **Task 3.2**: Update `GraphClientFactory` OBO scopes
  ```csharp
  // In GraphClientFactory.cs
  var scopes = new[] {
    "https://graph.microsoft.com/Sites.Read.All",
    "https://graph.microsoft.com/Files.ReadWrite.All",
    "https://graph.microsoft.com/FileStorageContainer.Selected"  // Add this
  };
  ```

- [ ] **Task 3.3**: Ensure Graph client uses beta endpoint
  ```csharp
  // Microsoft.Graph.GraphServiceClient should target:
  baseUrl = "https://graph.microsoft.com/beta"
  ```

- [ ] **Task 3.4**: Update upload endpoint path
  ```csharp
  // In OBOEndpoints.cs - verify endpoint format:
  var uploadEndpoint = $"/drives/{containerId}/items/root:/{fileName}:/content";
  // NOT: /storage/fileStorage/containers/{containerId}/files/{fileName}
  ```

### Phase 4: Test End-to-End

- [ ] **Task 4.1**: Test authentication flow
  - PCF acquires user token with FileStorageContainer.Selected scope
  - PCF acquires API token with Container.Manage scope
  - Verify tokens in browser console

- [ ] **Task 4.2**: Test BFF API OBO exchange
  - Add detailed logging to GraphClientFactory
  - Verify OBO token includes FileStorageContainer.Selected scope
  - Check token audience is `https://graph.microsoft.com`

- [ ] **Task 4.3**: Test upload with small file (<4MB)
  - Use single PUT request (no upload session)
  - Verify file appears in SharePoint Embedded Container
  - Check file metadata (name, size, modified date)

- [ ] **Task 4.4**: Test upload with large file (>10MB)
  - Use upload session with chunked upload
  - Verify progress tracking works
  - Confirm file integrity after upload

---

## Key Differences from Reference Guide

### What We Keep the Same

1. **Authentication Pattern**: MSAL.js (PCF) → MSAL Node/C# (BFF) → Graph
2. **OBO Flow**: User token → API token → OBO Graph token
3. **Scopes**: FileStorageContainer.Selected for both Graph and SharePoint
4. **Endpoints**: `/drives/{containerId}/items/...` for file operations

### What We Change

1. **Client Technology**: React SPA → PCF Control (still React, but in Dataverse context)
2. **Server Technology**: Node.js/Restify → ASP.NET Core
3. **Authentication Library**: @azure/msal-browser → Power Apps MSAL wrapper + custom MsalAuthProvider
4. **Deployment**: Standalone web app → Embedded in Model Driven App

### What We Already Have

1. ✅ BFF API with OBO flow (`Spe.Bff.Api`)
2. ✅ GraphClientFactory for creating Graph clients
3. ✅ UploadSessionManager for chunked uploads
4. ✅ Certificate authentication for app-only scenarios
5. ✅ Container Type and Entra ID app registration

---

## Current Blocker Analysis

### Why Upload Fails (HTTP 500)

**Hypothesis**: BFF API crashes during authentication middleware processing because:

1. **Missing Scope**: OBO token exchange doesn't request `FileStorageContainer.Selected`
   - Graph API returns 401/403 when trying to access `/drives/{containerId}`
   - ASP.NET Core middleware intercepts this and crashes the app

2. **Wrong Endpoint Version**: Using v1.0 instead of beta
   - SharePoint Embedded requires `/beta/drives/...` endpoints
   - v1.0 endpoints may not recognize Container drive IDs

3. **Unregistered Container Type**: Container Type not registered in consumer tenant
   - Graph returns "access denied" error
   - Error occurs before file upload logic executes

**Next Diagnostic Step**: Enable detailed logging (already done) and attempt upload to capture actual exception.

---

## Success Criteria

### Minimal Viable Product

1. PCF control authenticates user with correct scopes
2. BFF API successfully exchanges user token for OBO token with FileStorageContainer.Selected
3. BFF API can create/access SharePoint Embedded Containers
4. Small file (<4MB) uploads successfully to Container
5. Uploaded file is visible in SharePoint Embedded Container

### Full Feature Parity

1. All MVP criteria met
2. Large file (>10MB) uploads with progress tracking
3. Error handling shows meaningful messages to user
4. File overwrites work correctly
5. Metadata (Dataverse entity context) is preserved

---

## Implementation Approach

### Recommended Strategy: "Follow the Reference Exactly"

1. **Step 1**: Verify all infrastructure (Entra ID, Container Type, Registration)
2. **Step 2**: Align PCF control authentication to match reference React SPA
3. **Step 3**: Align BFF API OBO flow to match reference Node.js API
4. **Step 4**: Test upload flow step-by-step with detailed logging
5. **Step 5**: Refine error handling and user experience

### Anti-Patterns to Avoid

❌ **Don't**: Try to "improve" the reference pattern before it works
✅ **Do**: Copy the reference pattern exactly, then optimize

❌ **Don't**: Assume existing code already implements OBO correctly
✅ **Do**: Verify each step of OBO flow matches reference guide

❌ **Don't**: Skip Container Type registration thinking it's optional
✅ **Do**: Follow all setup steps in order, even if they seem redundant

---

## File References

### Existing Codebase
- BFF API: `src/api/Spe.Bff.Api/`
  - OBO Endpoints: `Api/OBOEndpoints.cs`
  - Graph Factory: `Infrastructure/Graph/GraphClientFactory.cs`
  - Upload Manager: `Infrastructure/Graph/UploadSessionManager.cs`
  - Configuration: `appsettings.json`, `Program.cs`

- PCF Control: `src/controls/UniversalQuickCreate/`
  - Main Control: `UniversalQuickCreate/index.ts`
  - Auth Provider: (needs to be identified)
  - API Client: (needs to be identified)

### Reference Guide
- Source: `Set up the SharePoint Embedded App.md`
- Key Sections:
  - Lines 12-100: Entra ID setup
  - Lines 101-138: Container Type creation
  - Lines 139-206: Container Type registration
  - Lines 376-484: React SPA authentication
  - Lines 488-537: OBO flow implementation
  - Lines 550-654: List containers endpoint
  - Lines 866-964: Create container endpoint
  - Lines 966-1006: Upload file implementation

---

## Notes

- **Token Scopes Are Critical**: Every permission must be explicitly requested at each step
- **Beta Endpoint Required**: SharePoint Embedded is preview feature, requires `/beta` Graph endpoint
- **Certificate for Registration**: Container Type registration requires certificate auth, not client secret
- **OBO Flow is Stateless**: Each API request must perform OBO exchange (no caching shown in reference)
- **Container ID = Drive ID**: SharePoint Embedded Containers are accessed as Graph drives

---

## Next Steps

1. **Immediate**: Check log stream for actual .NET exception (logging already enabled)
2. **Phase 1**: Complete infrastructure verification checklist
3. **Phase 2**: Align PCF control scopes with reference guide
4. **Phase 3**: Align BFF API OBO scopes with reference guide
5. **Phase 4**: Test upload flow end-to-end

---

**Document Version**: 1.0
**Created**: 2025-10-16
**Based On**: Set up the SharePoint Embedded App.md
**Status**: Design specification - no code changes made
