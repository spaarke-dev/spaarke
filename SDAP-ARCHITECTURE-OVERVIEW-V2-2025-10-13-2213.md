# SDAP Architecture Overview V2

**Document Version**: V2
**Created**: 2025-10-13 22:13:10
**Status**: Current Architecture Reference
**Purpose**: Comprehensive guide to Spaarke Document Access Platform (SDAP) architecture using SharePoint Embedded

---

## TABLE OF CONTENTS

1. [System Overview](#1-system-overview)
2. [Architecture Principles](#2-architecture-principles)
3. [Complete Authentication Flow](#3-complete-authentication-flow)
4. [App Registrations & Identity](#4-app-registrations--identity)
5. [Azure Resources & Services](#5-azure-resources--services)
6. [Component Inventory](#6-component-inventory)
7. [PCF Control Architecture](#7-pcf-control-architecture)
8. [Configuration Reference](#8-configuration-reference)
9. [Data Flow Examples](#9-data-flow-examples)
10. [Security Model](#10-security-model)
11. [Deployment Architecture](#11-deployment-architecture)

---

## 1. SYSTEM OVERVIEW

### 1.1 What is SDAP?

**Spaarke Document Access Platform (SDAP)** is a secure, enterprise-grade document management system that combines:

- **SharePoint Embedded (SPE)**: Cloud-native file storage with enterprise-grade security
- **Microsoft Dataverse**: Metadata storage and authorization management
- **Power Apps PCF Control**: User interface embedded in Model-Driven Apps
- **Backend For Frontend (BFF) API**: Secure middleware orchestrating operations

### 1.2 Key Capabilities

| Capability | Description |
|------------|-------------|
| **Secure File Storage** | Files stored in SharePoint Embedded containers with Microsoft 365 security |
| **User Authorization** | Row-level security enforced through Dataverse access rights |
| **Seamless Integration** | Embedded directly in Power Apps Model-Driven Apps |
| **Enterprise Features** | Versioning, audit trails, co-authoring, Office Online integration |
| **Scalable Architecture** | Supports millions of documents across thousands of users |

### 1.3 High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                           USER LAYER                                │
├─────────────────────────────────────────────────────────────────────┤
│  Power Apps Model-Driven App (Browser)                              │
│    ↓ embeds                                                         │
│  PCF Control (React + MSAL.js)                                      │
│    - Document list/upload UI                                        │
│    - User authentication (MSAL.js)                                  │
│    - Calls BFF API with user token                                  │
└────────────────────────────┬────────────────────────────────────────┘
                             │ HTTPS + JWT Token
                             ↓
┌─────────────────────────────────────────────────────────────────────┐
│                       BACKEND LAYER                                  │
├─────────────────────────────────────────────────────────────────────┤
│  Spe.Bff.Api (Azure App Service)                                    │
│    - JWT token validation                                           │
│    - Authorization enforcement                                      │
│    - OBO token exchange                                             │
│    - File operations orchestration                                  │
└────────┬──────────────────┬──────────────────┬───────────────────────┘
         │                  │                  │
         │ OBO Token        │ Client Secret    │ Client Secret
         ↓                  ↓                  ↓
┌────────────────┐  ┌──────────────────┐  ┌─────────────────────┐
│ Microsoft      │  │ Microsoft        │  │ Azure Service Bus   │
│ Graph API      │  │ Dataverse        │  │ (Background Jobs)   │
│                │  │                  │  │                     │
│ - SPE Ops      │  │ - Metadata       │  │ - Document Events   │
│ - File CRUD    │  │ - Authorization  │  │ - Processing Queue  │
│ - Containers   │  │ - Audit Logs     │  │                     │
└────────┬───────┘  └──────────────────┘  └─────────────────────┘
         │
         ↓
┌──────────────────────────────┐
│ SharePoint Embedded          │
│ - Container storage          │
│ - File versioning            │
│ - Office Online integration  │
└──────────────────────────────┘
```

### 1.4 Technology Stack

| Layer | Technologies |
|-------|-------------|
| **Frontend** | React, TypeScript, Fluent UI v9, MSAL.js 2.x, Power Apps PCF Framework |
| **Backend API** | .NET 8.0, ASP.NET Core Minimal APIs, Microsoft.Identity.Web |
| **Authentication** | Azure AD (Entra ID), OAuth 2.0 + OpenID Connect, On-Behalf-Of (OBO) Flow |
| **Storage** | SharePoint Embedded (SPE), Microsoft Graph SDK v5 |
| **Metadata** | Microsoft Dataverse, PowerPlatform.Dataverse.Client |
| **Caching** | Redis (StackExchange.Redis), IDistributedCache |
| **Background Processing** | Azure Service Bus, .NET Hosted Services |
| **Infrastructure** | Azure App Service, Azure Key Vault, System-Assigned Managed Identity |
| **Monitoring** | Application Insights, Azure Monitor, Distributed Tracing |

---

## 2. ARCHITECTURE PRINCIPLES

### 2.1 Design Principles (ADRs)

#### ADR-007: SPE Storage Seam Minimalism
- **Principle**: Single, focused SPE storage facade
- **Implementation**: `SpeFileStore` concrete class (no generic interfaces)
- **Benefit**: 50% fewer abstraction layers, simpler debugging

#### ADR-009: Redis-First Caching
- **Principle**: Distributed cache only, no hybrid L1/L2
- **Implementation**: `GraphTokenCache` with Redis backend
- **Benefit**: 95% cache hit rate, 97% reduction in auth latency

#### ADR-010: DI Minimalism and Feature Modules
- **Principle**: Register concretes, feature-module organization
- **Implementation**: 3 feature modules, ~20 lines DI code in Program.cs
- **Benefit**: 75% reduction in Program.cs complexity

#### ADR-011: Graph API Drives Endpoint for SPE
- **Principle**: Use Graph API `/drives/` endpoint instead of SPE-specific `/storage/fileStorage/containers/`
- **Rationale**: In SharePoint Embedded, Container ID equals Drive ID (documented SPE behavior)
- **Implementation**: `graphClient.Drives[containerId]` instead of `graphClient.Storage.FileStorage.Containers[containerId].Drive`
- **Benefit**: Simpler SDK path, avoids "service unavailable" errors, standard Graph API semantics
- **Example**:
  ```
  BFF API Route:     PUT /api/obo/containers/{containerId}/files/{path}
  Graph SDK Call:    graphClient.Drives[containerId].Root.ItemWithPath(path).Content.PutAsync()
  Graph API HTTP:    PUT /v1.0/drives/{containerId}/root:/{path}:/content
  ```

### 2.2 Security Principles

1. **Defense in Depth**: Multiple security layers (Azure AD, Dataverse, SPE)
2. **Least Privilege**: Users get minimum required permissions
3. **Zero Trust**: Every request validated, no implicit trust
4. **Audit Everything**: Comprehensive logging and audit trails
5. **Secrets in Vault**: All secrets stored in Azure Key Vault

### 2.3 Scalability Principles

1. **Stateless API**: No server-side sessions, scales horizontally
2. **Connection Pooling**: Singleton Dataverse client, connection reuse
3. **Token Caching**: 95% reduction in authentication overhead
4. **Async Operations**: Non-blocking I/O for all external calls
5. **Background Processing**: Heavy operations offloaded to Service Bus

---

## 3. COMPLETE AUTHENTICATION FLOW

### 3.1 Authentication Sequence Diagram

```
User Browser          PCF Control          Azure AD          BFF API          Microsoft Graph          SharePoint Embedded
     |                     |                    |                |                     |                         |
     |--1. Navigate to---->|                    |                |                     |                         |
     |    Power App        |                    |                |                     |                         |
     |                     |                    |                |                     |                         |
     |                     |--2. Login Popup--->|                |                     |                         |
     |                     |    (MSAL.js)       |                |                     |                         |
     |                     |                    |                |                     |                         |
     |                     |                    |--3. Auth user--|                     |                         |
     |                     |                    |   credentials  |                     |                         |
     |                     |                    |                |                     |                         |
     |                     |<----4. Token A-----|                |                     |                         |
     |                     | aud: api://1e40baad...              |                     |                         |
     |                     | appid: 170c98e1...                  |                     |                         |
     |                     | sub: user-id                        |                     |                         |
     |                     |                    |                |                     |                         |
     |                     |----5. API Call with Token A-------->|                     |                         |
     |                     | Authorization: Bearer {Token A}     |                     |                         |
     |                     |                    |                |                     |                         |
     |                     |                    |                |--6. Validate Token A|                         |
     |                     |                    |                |   (JWT middleware)  |                         |
     |                     |                    |                |                     |                         |
     |                     |                    |<--7. OBO Exchange---                 |                         |
     |                     |                    |   (ConfidentialClientApp)            |                         |
     |                     |                    |   UserAssertion: Token A             |                         |
     |                     |                    |   ClientId: 1e40baad...              |                         |
     |                     |                    |   ClientSecret: {from KeyVault}      |                         |
     |                     |                    |                |                     |                         |
     |                     |                    |----8. Token B--->                    |                         |
     |                     |                    | aud: https://graph.microsoft.com     |                         |
     |                     |                    | appid: 1e40baad... (BFF API)         |                         |
     |                     |                    | sub: user-id (preserved)             |                         |
     |                     |                    |                |                     |                         |
     |                     |                    |                |----9. Graph API Call with Token B------------>|
     |                     |                    |                |                     |                         |
     |                     |                    |                |                     |<---10. Validate---------|
     |                     |                    |                |                     |    Token B              |
     |                     |                    |                |                     |    Check Container      |
     |                     |                    |                |                     |    Registration         |
     |                     |                    |                |                     |                         |
     |                     |                    |                |                     |----11. Enforce User---->|
     |                     |                    |                |                     |         Permissions     |
     |                     |                    |                |                     |                         |
     |                     |                    |                |<---------12. File Operation Result------------|
     |                     |                    |                |                     |                         |
     |                     |<-----------13. API Response---------|                     |                         |
     |                     |                    |                |                     |                         |
     |<--14. Update UI-----|                    |                |                     |                         |
     |                     |                    |                |                     |                         |
```

### 3.2 Step-by-Step Authentication Breakdown

#### Step 1-4: User Authentication (MSAL.js)

**Location**: Browser (PCF Control)

```javascript
// PCF Control: MSAL.js initialization
const msalConfig = {
    auth: {
        clientId: "170c98e1-d486-4355-bcbe-170454e0207c",  // SDAP-PCF-CLIENT
        authority: "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2",
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    }
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

// Login and get token for BFF API
const loginRequest = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"]
};

const authResult = await msalInstance.acquireTokenPopup(loginRequest);
const tokenA = authResult.accessToken;  // JWT Token A
```

**Token A Contents**:
```json
{
  "aud": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",  // BFF API
  "iss": "https://sts.windows.net/a221a95e-6abc-4434-aecc-e48338a1b2f2/",
  "iat": 1697200000,
  "nbf": 1697200000,
  "exp": 1697203600,
  "appid": "170c98e1-d486-4355-bcbe-170454e0207c",      // PCF Client
  "appidacr": "0",                                       // Public client
  "family_name": "Schroeder",
  "given_name": "Ralph",
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",        // User Object ID
  "sub": "...",                                          // Subject
  "tid": "a221a95e-6abc-4434-aecc-e48338a1b2f2",        // Tenant ID
  "unique_name": "ralph.schroeder@spaarke.com",
  "upn": "ralph.schroeder@spaarke.com",
  "ver": "1.0"
}
```

**Key Points**:
- ✅ Token represents the USER, not the PCF app
- ✅ Audience is BFF API (`1e40baad...`)
- ✅ PCF app ID is in `appid` claim (`170c98e1...`)
- ✅ User identity in `oid`, `upn`, `unique_name` claims

---

#### Step 5-6: BFF API Token Validation

**Location**: Spe.Bff.Api (Azure App Service)

```csharp
// Program.cs: JWT Bearer authentication configuration
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// appsettings.json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  }
}
```

**Validation Checks**:
1. ✅ **Signature**: Token signed by Azure AD with valid certificate
2. ✅ **Audience**: Token audience matches BFF API (`1e40baad...`)
3. ✅ **Issuer**: Token issued by correct tenant Azure AD
4. ✅ **Expiration**: Token not expired (`exp` claim)
5. ✅ **Not Before**: Token valid time reached (`nbf` claim)

**If Validation Fails**: 401 Unauthorized response

---

#### Step 7-8: On-Behalf-Of (OBO) Token Exchange

**Location**: Spe.Bff.Api → GraphClientFactory

```csharp
// GraphClientFactory.cs: OBO token exchange
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
{
    // Check cache first (Phase 4)
    var tokenHash = ComputeTokenHash(userAccessToken);
    var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);
    if (cachedToken != null)
    {
        return CreateGraphClientWithToken(cachedToken);  // Cache hit: ~5ms
    }

    // Cache miss: Perform OBO exchange (~200ms)
    var result = await _confidentialClientApp.AcquireTokenOnBehalfOf(
        scopes: new[] {
            "https://graph.microsoft.com/Sites.FullControl.All",
            "https://graph.microsoft.com/Files.ReadWrite.All"
        },
        userAssertion: new UserAssertion(userAccessToken)  // Token A
    ).ExecuteAsync();

    // Cache for 55 minutes (5-minute buffer before 1-hour expiration)
    await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

    return CreateGraphClientWithToken(result.AccessToken);  // Token B
}
```

**OBO Exchange Details**:
- **Input**: Token A (user token for BFF API)
- **Client**: BFF API (`1e40baad...`) with client secret
- **Output**: Token B (user token for Graph API)
- **User Context**: Preserved (same `oid`, `upn`)
- **App Context**: Changed (appid becomes `1e40baad...`)

**Token B Contents**:
```json
{
  "aud": "https://graph.microsoft.com",                 // Graph API
  "iss": "https://sts.windows.net/a221a95e-6abc-4434-aecc-e48338a1b2f2/",
  "iat": 1697200010,
  "nbf": 1697200010,
  "exp": 1697203610,
  "appid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",      // BFF API (changed!)
  "appidacr": "1",                                       // Confidential client
  "family_name": "Schroeder",
  "given_name": "Ralph",
  "oid": "c74ac1af-ff3b-46fb-83e7-3063616e959c",        // User Object ID (preserved)
  "scp": "Sites.FullControl.All Files.ReadWrite.All",   // Delegated permissions
  "sub": "...",                                          // Subject (preserved)
  "tid": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "unique_name": "ralph.schroeder@spaarke.com",          // User (preserved)
  "upn": "ralph.schroeder@spaarke.com",                  // User (preserved)
  "ver": "1.0"
}
```

**Critical Changes in Token B**:
- ✅ **Audience**: Changed to Graph API
- ✅ **App ID**: Changed to BFF API (`1e40baad...`)
- ✅ **App ID ACR**: Changed to "1" (confidential client)
- ✅ **User Identity**: PRESERVED (oid, upn, sub unchanged)
- ✅ **Scopes**: Delegated permissions for Graph API

**Why This Matters for SPE**:
- SPE validates the `appid` claim in Token B
- SPE checks if `1e40baad...` is registered in the Container Type
- SPE enforces user permissions based on preserved user identity

---

#### Step 9-12: Graph API & SharePoint Embedded

**Location**: Microsoft Graph API → SharePoint Embedded

```csharp
// UploadSessionManager.cs: Upload file to SPE container
public async Task<FileHandleDto?> UploadSmallAsUserAsync(
    string userToken,
    string containerId,
    string path,
    Stream content,
    CancellationToken ct = default)
{
    // Get Graph client with OBO token (Token B)
    var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

    // CRITICAL: For SharePoint Embedded, Container ID IS the Drive ID
    // This is a documented SPE behavior - no container->drive lookup needed
    // Direct endpoint: PUT /drives/{containerId}/root:/{path}:/content
    var uploadedItem = await graphClient.Drives[containerId].Root
        .ItemWithPath(path)
        .Content
        .PutAsync(content, cancellationToken: ct);

    // Map Graph SDK DriveItem to SDAP DTO (ADR-007 compliance)
    return new FileHandleDto(
        uploadedItem.Id!,
        uploadedItem.Name!,
        uploadedItem.ParentReference?.Id,
        uploadedItem.Size,
        uploadedItem.CreatedDateTime ?? DateTimeOffset.UtcNow,
        uploadedItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
        uploadedItem.ETag,
        uploadedItem.Folder != null);
}
```

**Key Architectural Point**:
- ✅ **Container ID = Drive ID** in SharePoint Embedded
- ✅ **Uses `/drives/` endpoint** (not `/storage/fileStorage/containers/`)
- ✅ **Why**: Simplified Graph SDK path, avoids "service unavailable" errors
- ✅ **Result**: Returns DTO (FileHandleDto), not Graph SDK type (ADR-007 compliance)

**Graph API Validation** (Token B):
1. ✅ **Signature**: Valid Azure AD signature
2. ✅ **Audience**: `https://graph.microsoft.com`
3. ✅ **Scopes**: Contains required permissions (`Sites.FullControl.All`)
4. ✅ **App ID**: `1e40baad...` registered in Container Type

**SharePoint Embedded Validation**:
1. ✅ **Container Type Registration**: BFF API (`1e40baad...`) is registered
2. ✅ **User Permissions**: User (ralph.schroeder@spaarke.com) has access to container
3. ✅ **Resource Ownership**: Container owned by registered app

**If Validation Fails**:
- 403 Forbidden: App not registered or user lacks permissions
- 404 Not Found: Container doesn't exist
- 401 Unauthorized: Invalid token

---

### 3.3 Parallel Flow: Dataverse Authorization

**Purpose**: Check user's Dataverse permissions before allowing operations

```csharp
// ResourceAccessHandler.cs: Authorization handler
protected override async Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    ResourceAccessRequirement requirement,
    HttpContext httpContext)
{
    // Extract document ID from request
    var documentId = httpContext.GetRouteValue("documentId")?.ToString();
    if (string.IsNullOrEmpty(documentId))
    {
        context.Fail();
        return;
    }

    // Get user ID from token
    var userId = context.User.FindFirst("oid")?.Value;

    // Query Dataverse for user's access rights
    var accessLevel = await _authorizationService.GetUserAccessAsync(
        userId: userId,
        documentId: documentId
    );

    // Map operation to required access level
    var requiredAccess = requirement.Operation switch
    {
        "driveitem.content.download" => AccessRights.ReadAccess,
        "driveitem.content.upload" => AccessRights.WriteAccess,
        "driveitem.delete" => AccessRights.DeleteAccess,
        _ => AccessRights.None
    };

    // Authorize or deny
    if (accessLevel.HasFlag(requiredAccess))
    {
        context.Succeed(requirement);
    }
    else
    {
        context.Fail();
    }
}
```

**Dataverse Authentication**:
```csharp
// DataverseServiceClientImpl.cs: Client secret authentication
var connectionString =
    $"AuthType=ClientSecret;" +
    $"Url=https://spaarkedev1.crm.dynamics.com;" +
    $"ClientId=1e40baad-e065-4aea-a8d4-4b7ab273458c;" +  // BFF API
    $"ClientSecret={secretFromKeyVault};" +
    $"RequireNewInstance=false;";

var serviceClient = new ServiceClient(connectionString);

// Query user access
var query = new QueryExpression("sprk_documentaccess")
{
    ColumnSet = new ColumnSet("sprk_accessrights"),
    Criteria = new FilterExpression
    {
        Conditions =
        {
            new ConditionExpression("sprk_documentid", ConditionOperator.Equal, documentId),
            new ConditionExpression("sprk_userid", ConditionOperator.Equal, userId)
        }
    }
};

var results = await serviceClient.RetrieveMultipleAsync(query);
```

**Key Points**:
- ✅ BFF API uses **client secret** to query Dataverse (S2S authentication)
- ✅ Queries user's access rights for specific document
- ✅ Enforces row-level security (RLS) before allowing SPE operations
- ✅ Same app (`1e40baad...`) used for both OBO and Dataverse

---

## 4. APP REGISTRATIONS & IDENTITY

### 4.1 App Registration Overview

| App Name | App ID | Type | Purpose | Has Secrets? | Has Dataverse User? |
|----------|--------|------|---------|--------------|---------------------|
| **SDAP-BFF-SPE-API** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Confidential Client | Backend API | ✅ Yes | ✅ Yes (System Admin) |
| **SDAP-PCF-CLIENT** | `170c98e1-d486-4355-bcbe-170454e0207c` | Public Client | PCF Control | ⚠️ Has Secrets (Not Needed) | ❌ No |

### 4.2 SDAP-BFF-SPE-API (Confidential Client)

**Display Name**: `SDAP-BFF-SPE-API` (formerly `SPE-BFF-API`)

**App ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Object ID**: `c2aab303-50f8-4279-9934-503ab3a4b357`

**Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

**Type**: Confidential Client (server-side, has secrets)

**Purpose**: Backend For Frontend API orchestrating all operations

#### Configuration

**Application ID URI**:
```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c
```

**Redirect URIs**: None (confidential client, no interactive login)

**Client Credentials**:
```json
{
  "secretDescription": "SPE BFF API",
  "secretId": "3d09b386-89f0-41e2-902c-ed38a6ab1646",
  "secretValue": "CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy",  // Stored in KeyVault
  "expiresOn": "2026-10-06"
}
```

**API Permissions Exposed**:
```json
{
  "oauth2PermissionScopes": [
    {
      "id": "{generated-guid}",
      "adminConsentDescription": "Allow the application to access SDAP BFF API on behalf of the signed-in user",
      "adminConsentDisplayName": "Access SDAP BFF API",
      "isEnabled": true,
      "type": "User",
      "userConsentDescription": "Allow the application to access SDAP BFF API on your behalf",
      "userConsentDisplayName": "Access SDAP BFF API",
      "value": "user_impersonation"
    }
  ]
}
```

**API Permissions Required** (Delegated):
- **Microsoft Graph**:
  - `Files.ReadWrite.All` - Read and write files
  - `Sites.FullControl.All` - Full control of SharePoint sites

**Pre-Authorized Applications** (`knownClientApplications`):
```json
{
  "knownClientApplications": [
    "170c98e1-d486-4355-bcbe-170454e0207c"  // SDAP-PCF-CLIENT
  ]
}
```

**Dataverse Application User**:
- ✅ **Has Application User** with System Administrator security role
- **Purpose**: Server-to-Server (S2S) authentication for Dataverse operations
- **Used For**: Authorization queries, document metadata CRUD, audit logging

**SharePoint Embedded Registration**:
- ✅ **Registered in Container Type** `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- **Purpose**: Allows BFF API to perform file operations on SPE containers
- **Validates**: Token B `appid` claim during Graph API calls

---

### 4.3 SDAP-PCF-CLIENT (Public Client)

**Display Name**: `SDAP-PCF-CLIENT` (formerly `Spaarke DSM-SPE Dev 2`)

**App ID**: `170c98e1-d486-4355-bcbe-170454e0207c`

**Object ID**: `f21aa14d-0f0b-46f9-9045-9d5dfef58cf7`

**Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

**Type**: Public Client (browser-based, should not have secrets)

**Purpose**: User authentication in PCF control

#### Configuration

**Platform**: Single-Page Application (SPA)

**Redirect URIs**:
```json
{
  "redirectUris": [
    "https://spaarkedev1.crm.dynamics.com",
    "https://spaarkedev1.api.crm.dynamics.com",
    "http://localhost:8181"  // Local development
  ]
}
```

**Supported Account Types**: Single tenant (this organization only)

**Client Credentials**: ⚠️ Has secrets associated (but should not be needed - public client best practice is to have no secrets)

**API Permissions Required** (Delegated):
- **SDAP-BFF-SPE-API** (`1e40baad...`):
  - `user_impersonation` - Access SDAP BFF API on behalf of user

**Implicit Grant Settings**:
```json
{
  "enableIdTokenIssuance": true,
  "enableAccessTokenIssuance": true
}
```

**Dataverse Application User**:
- ❌ **Does NOT have Application User**
- **Reason**: Public client, no S2S operations, user context only
- **User Access**: User (ralph.schroeder@spaarke.com) already has Dataverse access

**SharePoint Embedded**:
- **Container Type Ownership**: Owns Container Type `8a6ce34c...`
- **Note**: Ownership != File operations (only BFF API performs file ops)

**PCF Control Architecture**:
- See detailed PCF control architecture documentation: [dev/projects/quickcreate_pcf_component/ARCHITECTURE.md](dev/projects/quickcreate_pcf_component/ARCHITECTURE.md)
- Includes: File upload UI, document metadata management, multi-file upload, parent entity integration

---

### 4.4 System-Assigned Managed Identity

**Display Name**: `spe-api-dev-67e2xz` (App Service)

**App ID (Client ID)**: `6bbcfa82-14a0-40b5-8695-a271f4bac521`

**Principal ID (Object ID)**: `56ae2188-c978-4734-ad16-0bc288973f20`

**Type**: System-Assigned Managed Identity (Azure resource identity)

**Purpose**: Azure resource access without secrets

#### Configuration

**Azure RBAC Roles**:
- ✅ **Key Vault Secrets User** (on `spaarke-spekvcert`)
  - **Purpose**: Read configuration secrets (Dataverse URL, client secrets, connection strings)
  - **Scope**: Key Vault `spaarke-spekvcert`

- ✅ **Azure Service Bus Data Receiver** (on Service Bus namespace)
  - **Purpose**: Receive messages from queues for background processing
  - **Scope**: Service Bus namespace (to be configured)

**Dataverse Application User**:
- ❌ **Does NOT have Application User** (recommended configuration)
- **Reason**: BFF API app (`1e40baad...`) handles all Dataverse operations
- **Simplicity**: Single security principal for all Dataverse S2S operations

**NOT Used For**:
- ❌ NOT used for Dataverse authentication (uses client secret instead)
- ❌ NOT used for Graph API calls (uses OBO with client secret)
- ✅ ONLY used for Azure resource access (KeyVault, Service Bus)

---

## 5. AZURE RESOURCES & SERVICES

### 5.1 Resource Group: SharePointEmbedded

**Subscription**: Spaarke SPE Subscription 1
**Subscription ID**: `484bc857-3802-427f-9ea5-ca47b43db0f0`
**Region**: East US

| Resource Type | Resource Name | Purpose |
|---------------|---------------|---------|
| App Service | `spe-api-dev-67e2xz` | BFF API hosting |
| Key Vault | `spaarke-spekvcert` | Secrets management |
| Service Bus Namespace | (TBD) | Background job processing |

---

### 5.2 App Service: spe-api-dev-67e2xz

**URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`

**Runtime**: .NET 8.0

**Plan**: (Standard/Premium tier for production)

**Managed Identity**: System-Assigned enabled

#### Application Settings

**Core Settings**:
```json
{
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)",
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
}
```

**Azure AD Settings**:
```json
{
  "AzureAd__Instance": "https://login.microsoftonline.com/",
  "AzureAd__TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "AzureAd__ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "AzureAd__Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
}
```

**Dataverse Settings**:
```json
{
  "Dataverse__ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
  "Dataverse__ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "Dataverse__ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
}
```

**Service Bus Settings**:
```json
{
  "ConnectionStrings__ServiceBus": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)"
}
```

**Redis Settings** (Optional):
```json
{
  "Redis__Enabled": "false",  // Use "true" for production
  "ConnectionStrings__Redis": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/Redis-ConnectionString)"
}
```

#### Deployment

**Source Control**: Manual deployment via Azure CLI

**Deployment Command**:
```bash
az webapp deploy \
  --resource-group SharePointEmbedded \
  --name spe-api-dev-67e2xz \
  --src-path deployment.zip \
  --type zip
```

**Health Endpoints**:
- `/healthz` - Overall health check
- `/healthz/dataverse` - Dataverse connectivity test
- `/healthz/dataverse/crud` - Dataverse CRUD operations test
- `/ping` - Basic availability check

---

### 5.3 Key Vault: spaarke-spekvcert

**Vault URI**: `https://spaarke-spekvcert.vault.azure.net/`

**Resource ID**: `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert`

**Access Policy**: RBAC-based (not classic access policies)

#### Secrets Stored

| Secret Name | Description | Used By |
|-------------|-------------|---------|
| `BFF-API-ClientSecret` | SDAP-BFF-SPE-API client secret | OBO token exchange, Dataverse auth |
| `SPRK-DEV-DATAVERSE-URL` | Dataverse environment URL | Dataverse ServiceClient connection |
| `ServiceBus-ConnectionString` | Azure Service Bus connection string | Background job processing |
| `Redis-ConnectionString` | Redis cache connection string | Distributed caching (optional) |
| `spe-app-cert` | PFX certificate (Base64) | Certificate-based auth (alternative) |
| `spe-app-cert-pass` | PFX certificate password | Certificate decryption |

#### RBAC Assignments

| Principal | Role | Purpose |
|-----------|------|---------|
| `spe-api-dev-67e2xz` MI<br/>`56ae2188...` | Key Vault Secrets User | Read secrets at runtime |
| Deployment Service Principal | Key Vault Administrator | Manage secrets during deployment |

---

### 5.4 Microsoft Dataverse

**Environment Name**: `spaarkedev1`

**Environment URL**: `https://spaarkedev1.crm.dynamics.com`

**API URL**: `https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/`

**Environment ID**: `b5a401dd-b42b-e84a-8cab-2aef8471220d`

**Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

**Object ID**: `c74ac1af-ff3b-46fb-83e7-3063616e959c`

#### Application Users

| Application | App ID | Security Role | Purpose |
|-------------|--------|---------------|---------|
| **SDAP-BFF-SPE-API** | `1e40baad...` | System Administrator | S2S authentication for authorization queries, metadata CRUD |

#### Custom Tables (Entities)

| Table Name | Logical Name | Purpose |
|------------|--------------|---------|
| **Document** | `sprk_document` | Document metadata and properties |
| **Document Access** | `sprk_documentaccess` | User access rights (RLS enforcement) |
| **Container** | `sprk_container` | SPE container metadata |
| **Audit Log** | `sprk_auditlog` | Audit trail for operations |

#### Document Table Schema

| Field | Type | Description |
|-------|------|-------------|
| `sprk_documentid` | Primary Key (GUID) | Unique document identifier |
| `sprk_documentname` | Single Line Text | File name |
| `sprk_containerid` | Single Line Text | SPE container ID |
| `sprk_driveitemid` | Single Line Text | SPE drive item ID |
| `sprk_description` | Multiple Lines Text | Document description |
| `sprk_filesize` | Whole Number | File size in bytes |
| `sprk_mimetype` | Single Line Text | MIME type |
| `sprk_createdby` | Lookup (User) | Creator |
| `sprk_modifiedby` | Lookup (User) | Last modifier |
| `statecode` | State | Active/Inactive |
| `statuscode` | Status | Draft/Published/Archived |

#### Document Access Table Schema

| Field | Type | Description |
|-------|------|-------------|
| `sprk_documentaccessid` | Primary Key (GUID) | Access record identifier |
| `sprk_documentid` | Lookup (Document) | Related document |
| `sprk_userid` | Lookup (User) | User with access |
| `sprk_accessrights` | Choice | Read/Write/Delete/Share |
| `sprk_grantedby` | Lookup (User) | Who granted access |
| `sprk_grantedon` | Date Time | When access granted |

---

### 5.5 SharePoint Embedded

#### Container Type

**Container Type Name**: `Spaarke PAYGO 1`

**Container Type ID**: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`

**Owning Application ID**: `170c98e1-d486-4355-bcbe-170454e0207c` (Spaarke DSM-SPE Dev 2)

**Azure Subscription ID**: `484bc857-3802-427f-9ea5-ca47b43db0f0`

**Resource Group**: `SharePointEmbedded`

**Region**: `eastus`

**Classification**: `Standard`

**Created**: 2025-09-22

**Registered Applications**:
- ✅ `1e40baad-e065-4aea-a8d4-4b7ab273458c` (SDAP-BFF-SPE-API) - **Performs file operations**
- ✅ `170c98e1-d486-4355-bcbe-170454e0207c` (Spaarke DSM-SPE Dev 2) - **Owner only**

#### Test Container

**Container ID**: `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`

**Permissions**:
- **User**: Ralph Schroeder (ralph.schroeder@spaarke.com)
- **User ID**: `c74ac1af-ff3b-46fb-83e7-3063616e959c`
- **Role**: Owner (full control)

---

### 5.6 Azure Service Bus

**Namespace**: `spaarke-servicebus-dev`

**Connection String**: Stored in Key Vault secret `ServiceBus-ConnectionString`

#### Queues

| Queue Name | Purpose | Max Delivery Count | Lock Duration |
|------------|---------|-------------------|---------------|
| `sdap-jobs` | General background job processing | 10 | 5 minutes |
| `document-events` | Document lifecycle event processing | 3 | 5 minutes |

#### Message Handlers

| Handler | Queue | Processing |
|---------|-------|------------|
| `ServiceBusJobProcessor` | `sdap-jobs` | Job orchestration and execution |
| `DocumentEventProcessor` | `document-events` | Document event handling (upload complete, access changed, etc.) |

---

## 6. COMPONENT INVENTORY

### 6.1 Solution Structure

```
spaarke/
├── src/
│   ├── api/
│   │   └── Spe.Bff.Api/                    ← Backend API
│   │       ├── Endpoints/                   ← API endpoint definitions
│   │       ├── Extensions/                  ← Feature modules (DI)
│   │       ├── Storage/                     ← SpeFileStore (SPE facade)
│   │       ├── Services/                    ← Application services
│   │       ├── Infrastructure/              ← Cross-cutting concerns
│   │       ├── Models/                      ← DTOs and request/response models
│   │       ├── Configuration/               ← Strongly-typed configuration
│   │       └── Program.cs                   ← Application entry point
│   │
│   ├── shared/
│   │   ├── Spaarke.Core/                   ← Authorization and caching
│   │   │   ├── Auth/                       ← Authorization service and rules
│   │   │   └── Cache/                      ← Distributed cache extensions
│   │   │
│   │   └── Spaarke.Dataverse/              ← Dataverse integration
│   │       ├── DataverseServiceClientImpl.cs
│   │       ├── DataverseAccessDataSource.cs
│   │       └── Models.cs
│   │
│   └── controls/                           ← PCF controls (separate repository)
│       └── UniversalDatasetGrid/           ← Document list/upload UI
│
├── dev/
│   ├── SDAP-AZURE-ENVIRONMENT-SETTINGS.md  ← Azure resource reference
│   └── projects/
│       └── sdap_V2/                        ← V2 refactoring plan
│
├── docs/                                   ← Architecture documentation
│   ├── SDAP-ARCHITECTURE-OVERVIEW-V2.md    ← This document
│   ├── SPE-BFF-API-COMPONENT-INVENTORY.md  ← Component catalog
│   └── DATAVERSE-APPLICATION-USER-CLEANUP.md
│
└── tests/
    └── Spe.Bff.Api.Tests/                  ← Unit and integration tests
```

---

### 6.2 API Endpoints (Minimal API)

#### 6.2.1 OBOEndpoints.cs

**Purpose**: User-context file operations using OBO flow

**Routes**:
```csharp
// File upload (OBO)
PUT /api/obo/containers/{containerId}/files/{fileName}
[Authorize(Policy = "canuploadfiles")]
[RequireRateLimiting("upload-heavy")]

// File download (OBO)
GET /api/obo/containers/{containerId}/files/{fileId}
[Authorize(Policy = "candownloadfiles")]
[RequireRateLimiting("graph-read")]

// Initiate upload session (large files)
POST /api/obo/containers/{containerId}/upload
[Authorize(Policy = "canuploadfiles")]
[RequireRateLimiting("graph-write")]
```

**Authentication**: Requires JWT token, performs OBO exchange

**Dependencies**:
- `SpeFileStore` (concrete class)
- `IGraphClientFactory` (for OBO token exchange)

---

#### 6.2.2 DocumentsEndpoints.cs

**Purpose**: Container and file management operations

**Routes**:
```csharp
// List containers
GET /api/documents/containers
[Authorize(Policy = "canlistcontainers")]

// Create container
POST /api/documents/containers
[Authorize(Policy = "cancreatecontainers")]

// List files in container
GET /api/documents/containers/{id}/items
[Authorize(Policy = "canlistchildren")]

// Get file metadata
GET /api/documents/items/{id}
[Authorize(Policy = "canreadmetadata")]

// Delete file
DELETE /api/documents/items/{id}
[Authorize(Policy = "candeletefiles")]
```

**Dependencies**:
- `SpeFileStore`
- `ContainerOperations`
- `DriveItemOperations`

---

#### 6.2.3 DataverseDocumentsEndpoints.cs

**Purpose**: Document metadata CRUD in Dataverse

**Routes**:
```csharp
// Create document record
POST /api/dataverse/documents
[Authorize]

// Get document metadata
GET /api/dataverse/documents/{id}
[Authorize]

// Update document
PUT /api/dataverse/documents/{id}
[Authorize]

// Delete document
DELETE /api/dataverse/documents/{id}
[Authorize(Policy = "candeletefiles")]

// List documents by container
GET /api/dataverse/documents/container/{containerId}
[Authorize]
```

**Dependencies**:
- `IDataverseService` → `DataverseServiceClientImpl`

---

#### 6.2.4 UploadEndpoints.cs

**Purpose**: Large file upload management (chunked uploads)

**Routes**:
```csharp
// Create upload session
POST /api/upload/session
[Authorize(Policy = "canuploadfiles")]

// Upload chunk
PUT /api/upload/session/{id}/chunk
[Authorize(Policy = "canuploadfiles")]

// Complete upload
POST /api/upload/session/{id}/complete
[Authorize(Policy = "canuploadfiles")]

// Cancel upload
DELETE /api/upload/session/{id}
[Authorize]
```

**Dependencies**:
- `UploadSessionManager`
- `IGraphClientFactory`

---

#### 6.2.5 PermissionsEndpoints.cs

**Purpose**: User permission queries for UI

**Routes**:
```csharp
// Get current user permissions
GET /api/permissions/user
[Authorize]

// Get document-specific permissions
GET /api/permissions/document/{id}
[Authorize]

// Get container permissions
GET /api/permissions/container/{id}
[Authorize]
```

**Dependencies**:
- `AuthorizationService` (Spaarke.Core)
- `IAccessDataSource`

---

#### 6.2.6 UserEndpoints.cs

**Purpose**: User identity and profile information

**Routes**:
```csharp
// Get current user info
GET /api/user/me
[Authorize]

// Get user capabilities
GET /api/user/capabilities
[Authorize]
```

**Dependencies**: JWT claims extraction

---

#### 6.2.7 Health Check Endpoints

**Routes**:
```csharp
// Overall health
GET /healthz
[AllowAnonymous]

// Dataverse connectivity
GET /healthz/dataverse
[AllowAnonymous]

// Dataverse CRUD test
GET /healthz/dataverse/crud
[AllowAnonymous]

// Basic availability
GET /ping
[AllowAnonymous]
```

---

### 6.3 Core Services

#### 6.3.1 SpeFileStore.cs

**Location**: `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs`

**Purpose**: Single SPE storage facade (ADR-007 compliant)

**Lifetime**: Scoped

**Key Methods**:
```csharp
// File operations
Task<FileUploadResult> UploadFileAsync(string containerId, string fileName, Stream content, string userToken);
Task<Stream> DownloadFileAsync(string containerId, string fileId, string userToken);
Task DeleteFileAsync(string containerId, string fileId, string userToken);

// Container operations
Task<ContainerDto> CreateContainerAsync(string containerName, string userToken);
Task<IEnumerable<ContainerDto>> ListContainersAsync(string userToken);

// Metadata operations
Task<FileMetadataDto> GetFileMetadataAsync(string containerId, string fileId, string userToken);
Task UpdateFileMetadataAsync(string containerId, string fileId, FileMetadataDto metadata, string userToken);
```

**Dependencies**:
- `IGraphClientFactory` (for OBO token exchange)

**Note**: Concrete class, no interface (ADR-007)

---

#### 6.3.2 GraphClientFactory.cs

**Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

**Purpose**: Creates Graph clients with appropriate authentication

**Lifetime**: Singleton

**Interface**: `IGraphClientFactory` (factory pattern justified)

**Key Methods**:
```csharp
// OBO flow for user operations (with caching)
Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);

// App-only for service operations (if needed)
GraphServiceClient CreateAppOnlyClient();
```

**Configuration**:
```csharp
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(...)",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
}
```

**Token Caching**: Integrated with `GraphTokenCache` (Phase 4)

---

#### 6.3.3 GraphTokenCache.cs

**Location**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

**Purpose**: Cache OBO tokens to reduce Azure AD load (ADR-009)

**Lifetime**: Singleton

**Key Methods**:
```csharp
// Compute hash of user token (for cache key)
string ComputeTokenHash(string userToken);

// Get cached Graph token
Task<string?> GetTokenAsync(string tokenHash);

// Set Graph token with TTL
Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry);
```

**Cache Key Pattern**:
```
sdap:graph:token:{SHA256(userToken)}
```

**TTL**: 55 minutes (5-minute buffer before 1-hour expiration)

**Storage**: Redis (IDistributedCache)

**Performance Impact**:
- Cache Hit Rate: 95%
- Cache Hit Latency: ~5ms
- Cache Miss Latency: ~200ms (OBO exchange)
- Average Latency: ~15ms (95% × 5ms + 5% × 200ms)

---

#### 6.3.4 DataverseServiceClientImpl.cs

**Location**: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

**Purpose**: Dataverse connection and operations

**Lifetime**: Singleton (connection pooling)

**Authentication**: Client Secret (S2S)

**Configuration**:
```csharp
var connectionString =
    $"AuthType=ClientSecret;" +
    $"Url=https://spaarkedev1.crm.dynamics.com;" +
    $"ClientId=1e40baad-e065-4aea-a8d4-4b7ab273458c;" +
    $"ClientSecret={secretFromKeyVault};" +
    $"RequireNewInstance=false;";  // Enable connection pooling

_serviceClient = new ServiceClient(connectionString);
```

**Key Methods**:
```csharp
// Document CRUD
Task<string> CreateDocumentAsync(CreateDocumentRequest request);
Task<DocumentEntity?> GetDocumentAsync(string id);
Task UpdateDocumentAsync(string id, UpdateDocumentRequest request);
Task DeleteDocumentAsync(string id);

// Query operations
Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId);

// Health checks
Task<bool> TestConnectionAsync();
Task<bool> TestDocumentOperationsAsync();
```

**Performance**:
- Connection created once at startup
- Reused across all requests
- 100% elimination of 500ms initialization overhead per request

---

#### 6.3.5 AuthorizationService.cs

**Location**: `src/shared/Spaarke.Core/Auth/AuthorizationService.cs`

**Purpose**: Dataverse-based authorization logic

**Lifetime**: Singleton

**Key Methods**:
```csharp
// Get user's access level for document
Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId);

// Check if user can perform operation
Task<bool> CanUserPerformOperationAsync(string userId, string documentId, string operation);

// Evaluate authorization rules
Task<AuthorizationResult> EvaluateAsync(AuthorizationContext context);
```

**Rule Chain**:
1. `ExplicitDenyRule` - Check explicit denies (highest priority)
2. `ExplicitGrantRule` - Check explicit grants
3. `TeamMembershipRule` - Check team-based access
4. `OperationAccessRule` - Check operation-specific access

**Dependencies**:
- `IAccessDataSource` → `DataverseAccessDataSource`

---

#### 6.3.6 UploadSessionManager.cs

**Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

**Purpose**: Manage large file upload sessions

**Lifetime**: Scoped

**Key Methods**:
```csharp
// Create upload session
Task<UploadSession> CreateSessionAsync(string containerId, string fileName, long fileSize, string userToken);

// Upload chunk
Task<UploadChunkResult> UploadChunkAsync(string sessionUrl, byte[] chunk, long rangeStart, long rangeEnd);

// Complete upload
Task<DriveItem> CompleteUploadAsync(string sessionUrl);

// Cancel upload
Task CancelUploadAsync(string sessionUrl);
```

**Features**:
- Chunked upload (10MB chunks recommended)
- Resume support (track uploaded ranges)
- Progress tracking
- Automatic retry on transient failures

---

### 6.4 Background Services

#### 6.4.1 DocumentEventProcessor.cs

**Location**: `src/api/Spe.Bff.Api/Services/Jobs/DocumentEventProcessor.cs`

**Purpose**: Process document lifecycle events from Service Bus

**Lifetime**: Hosted Service (Singleton)

**Queue**: `document-events`

**Events Handled**:
- `DocumentUploaded` - File upload completed
- `DocumentDeleted` - File deleted
- `DocumentAccessChanged` - Permissions updated
- `DocumentMetadataUpdated` - Metadata changed

**Configuration**:
```json
{
  "DocumentEventProcessor": {
    "QueueName": "document-events",
    "MaxConcurrentCalls": 5,
    "MaxRetryAttempts": 3,
    "MessageLockDuration": "00:05:00",
    "EnableDeadLettering": true,
    "EnableDetailedLogging": true
  }
}
```

**Processing Flow**:
1. Receive message from queue
2. Deserialize event payload
3. Execute event handler
4. Update Dataverse if needed
5. Complete message (or dead letter on failure)

---

#### 6.4.2 ServiceBusJobProcessor.cs

**Location**: `src/api/Spe.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs`

**Purpose**: General-purpose background job processing

**Lifetime**: Hosted Service (Singleton)

**Queue**: `sdap-jobs`

**Job Types**:
- `DocumentProcessingJob` - Post-upload processing
- `BulkOperationJob` - Batch operations
- `MaintenanceJob` - Cleanup and maintenance
- `ReportGenerationJob` - Generate reports

**Configuration**:
```json
{
  "Jobs": {
    "ServiceBus": {
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 5
    }
  }
}
```

---

#### 6.4.3 IdempotencyService.cs

**Location**: `src/api/Spe.Bff.Api/Services/Jobs/IdempotencyService.cs`

**Purpose**: Prevent duplicate job processing

**Lifetime**: Singleton

**Key Methods**:
```csharp
// Check if job already processed
Task<bool> IsJobProcessedAsync(string jobId);

// Mark job as processed
Task MarkJobAsProcessedAsync(string jobId, TimeSpan ttl);
```

**Storage**: Redis (distributed cache)

**Cache Key Pattern**:
```
sdap:job:idempotency:{jobId}
```

**TTL**: 24 hours (configurable)

---

### 6.5 Infrastructure Components

#### 6.5.1 SecurityHeadersMiddleware.cs

**Purpose**: Add security headers to all responses

**Headers Added**:
```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Strict-Transport-Security: max-age=31536000; includeSubDomains
Content-Security-Policy: default-src 'self'
```

---

#### 6.5.2 GraphHttpMessageHandler.cs

**Purpose**: Resilience patterns for Graph API calls (ADR-009)

**Features**:
- **Retry Policy**: 3 retries with exponential backoff
- **Circuit Breaker**: 5 failures = 30-second break
- **Timeout**: 30 seconds per request
- **Honors Retry-After**: Respects 429 throttling headers

**Configuration**:
```json
{
  "GraphResilience": {
    "RetryCount": 3,
    "RetryBackoffSeconds": 2,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDurationSeconds": 30,
    "TimeoutSeconds": 30,
    "HonorRetryAfterHeader": true
  }
}
```

---

#### 6.5.3 ResourceAccessHandler.cs

**Purpose**: Custom authorization handler for resource-based policies

**Flow**:
1. Extract document ID from route
2. Get user ID from JWT token
3. Query Dataverse for user access rights
4. Map operation to required access level
5. Authorize or deny request

**Policies**:
```csharp
// File operations
"canuploadfiles", "candownloadfiles", "candeletefiles"

// Metadata operations
"canreadmetadata", "canupdatemetadata"

// Container operations
"canlistcontainers", "cancreatecontainers", "candeletecontainers"

// Sharing operations
"cansharefiles", "canmanagefilepermissions"
```

---

## 7. PCF CONTROL ARCHITECTURE

### 7.1 Overview

The Universal Document Upload PCF control provides the user interface for file upload and document management in Power Apps Model-Driven Apps. It's designed to be parent-entity agnostic, working with Matter, Project, Invoice, Account, Contact, and future entity types through configuration.

**Key Features**:
- Multi-file upload (up to 10 files simultaneously)
- Real-time progress tracking
- File validation (size, type, count)
- Document metadata management
- Parent entity integration (dynamic lookup binding)
- Fluent UI v9 design system

### 7.2 User Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│  Parent Entity Form (e.g., Matter)                              │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Documents Subgrid                                         │ │
│  │  [+ New Document] ← Opens dialog                           │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                          ↓
         Xrm.Navigation.navigateTo(customPage, dialogOptions)
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│  Custom Page Dialog: sprk_DocumentUploadDialog                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  PCF Control: UniversalDocumentUploadPCF                   │ │
│  │  • [Add File ↑]                                            │ │
│  │  • Document Description: [_______________]                 │ │
│  │  • Progress: 3 of 10 files ████████░░░░ 30%                │ │
│  │  • [Upload & Create]  [Cancel]                             │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                          ↓
                   File Upload Flow
```

### 7.3 Two-Phase Processing

**Phase 1: File Upload to SharePoint Embedded**
- Parallel upload of up to 10 files
- Uses BFF API (`PUT /api/obo/containers/{containerId}/files/{fileName}`)
- OAuth 2.0 OBO flow preserves user identity
- Real-time progress tracking
- Expected time: ~5-10 seconds for 100MB total

**Phase 2: Create Dataverse Records**
- Sequential creation (one record at a time)
- Uses `Xrm.WebApi.createRecord()` (no Quick Create limitations)
- Dynamic lookup binding based on parent entity type
- SPE metadata stored (driveId, itemId, size)
- Expected time: ~1-2 seconds per record

### 7.4 Entity Configuration

The control uses configuration to support multiple parent entity types:

```typescript
export interface EntityDocumentConfig {
    entityName: string;           // 'sprk_matter'
    lookupFieldName: string;      // 'sprk_matter' (on Document)
    containerIdField: string;     // 'sprk_containerid' (on parent)
    displayNameField: string;     // 'sprk_matternumber' (on parent)
    entitySetName: string;        // 'sprk_matters' (OData)
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': { /* ... */ },
    'sprk_project': { /* ... */ },
    'sprk_invoice': { /* ... */ },
    'account': { /* ... */ },
    'contact': { /* ... */ }
};
```

### 7.5 Component Architecture

**Layer 1: Presentation (UI)**
- `DocumentUploadForm.tsx` (Main container)
- `FileSelectionField.tsx` (File input + validation)
- `DocumentMetadataFields.tsx` (Description, etc.)
- `UploadProgressBar.tsx` (Progress display)
- `ErrorMessageList.tsx` (Error handling)

**Layer 2: Control Logic (PCF Framework)**
- `UniversalDocumentUploadPCF.ts` (Main control class)
- Receives parameters from Custom Page navigation
- Manages component state and lifecycle

**Layer 3: Business Logic (Services)**
- `FileUploadService.ts` - Upload single file to SPE via BFF API
- `DocumentRecordService.ts` - Create Document records using Xrm.WebApi
- `SdapApiClient.ts` - HTTP client for BFF API

### 7.6 Authentication in PCF Control

The PCF control uses **SDAP-PCF-CLIENT** (`170c98e1-d486-4355-bcbe-170454e0207c`) for user authentication:

```javascript
const msalConfig = {
    auth: {
        clientId: "170c98e1-d486-4355-bcbe-170454e0207c",  // SDAP-PCF-CLIENT
        authority: "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2",
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    }
};

// Request token for BFF API
const loginRequest = {
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"]
};
```

**Security Flow**:
1. User opens dialog from Parent form
2. MSAL.js acquires token silently (or prompts for login)
3. Token sent to BFF API for file upload (OBO exchange happens in BFF)
4. Xrm.WebApi.createRecord() uses current user's Dataverse session

### 7.7 File Validation Rules

**Pre-Upload Validation**:
- Maximum files: 10
- Maximum file size: 10MB per file
- Maximum total size: 100MB
- Blocked extensions: `.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.jar`

**Error Handling**:
- Validation errors: Displayed immediately, upload blocked
- Upload failures: Partial success handling (continue with successful uploads)
- Record creation errors: Individual error tracking per file

### 7.8 Deployment

**Solution Package**:
```
SpaarkeDocumentUpload_2_0_0_0.zip
├── Controls/
│   └── sprk_UniversalDocumentUploadPCF.xml (version 2.0.0.0)
├── CustomPages/
│   └── sprk_DocumentUploadDialog.json
├── WebResources/
│   ├── sprk_subgrid_commands.js
│   └── sprk_entity_document_config.json
└── RibbonCustomizations/
    └── sprk_document_upload_button.xml
```

**Deployment Steps**:
1. Import solution into target environment
2. Publish all customizations
3. For each entity: Add `sprk_containerid` field, Documents subgrid, command button
4. Verify version in dialog footer

### 7.9 Related Documentation

For complete PCF control architecture details, see:
- **[dev/projects/quickcreate_pcf_component/ARCHITECTURE.md](dev/projects/quickcreate_pcf_component/ARCHITECTURE.md)**
  - Detailed component architecture
  - Data flow diagrams
  - Entity relationship diagrams
  - Error handling strategy
  - Testing strategy
  - Future enhancements

---

## 8. CONFIGURATION REFERENCE

### 8.1 appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Graph": "Information"
    }
  },
  "AllowedHosts": "*",

  // Core Identity Configuration
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)",
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",

  // Connection Strings
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)",
    "Redis": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/Redis-ConnectionString)"
  },

  // Redis Configuration
  "Redis": {
    "Enabled": false,  // Set to true for production
    "InstanceName": "sdap-dev:",
    "DefaultExpirationMinutes": 60,
    "AbsoluteExpirationMinutes": 1440
  },

  // Azure AD Authentication
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },

  // CORS Origins
  "Cors": {
    "AllowedOrigins": [
      "https://spaarkedev1.crm.dynamics.com",
      "https://spaarkedev1.api.crm.dynamics.com",
      "http://localhost:3000",
      "http://127.0.0.1:3000"
    ]
  },

  // Dataverse Configuration
  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "ClientSecret": "@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)"
  },

  // Background Jobs
  "Jobs": {
    "ServiceBus": {
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 5
    }
  },

  // Document Event Processor
  "DocumentEventProcessor": {
    "QueueName": "document-events",
    "MaxConcurrentCalls": 5,
    "MaxRetryAttempts": 3,
    "MessageLockDuration": "00:05:00",
    "EnableDeadLettering": true,
    "EnableDetailedLogging": true
  },

  // Graph API Resilience
  "GraphResilience": {
    "RetryCount": 3,
    "RetryBackoffSeconds": 2,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDurationSeconds": 30,
    "TimeoutSeconds": 30,
    "HonorRetryAfterHeader": true
  }
}
```

---

### 8.2 Azure App Service Configuration

#### Environment Variables

```bash
# Core Identity
TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
API_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c
DEFAULT_CT_ID=8a6ce34c-6055-4681-8f87-2f4f9f921c06

# Azure AD
AzureAd__Instance=https://login.microsoftonline.com/
AzureAd__TenantId=a221a95e-6abc-4434-aecc-e48338a1b2f2
AzureAd__ClientId=1e40baad-e065-4aea-a8d4-4b7ab273458c
AzureAd__Audience=api://1e40baad-e065-4aea-a8d4-4b7ab273458c

# Dataverse
Dataverse__ServiceUrl=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)
Dataverse__ClientId=1e40baad-e065-4aea-a8d4-4b7ab273458c
Dataverse__ClientSecret=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)

# Secrets (via KeyVault references)
API_CLIENT_SECRET=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)
ConnectionStrings__ServiceBus=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ServiceBus-ConnectionString)
ConnectionStrings__Redis=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/Redis-ConnectionString)

# Redis
Redis__Enabled=true
Redis__InstanceName=sdap-prod:

# Logging
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_DETAILEDERRORS=false
```

---

## 9. DATA FLOW EXAMPLES

### 9.1 File Upload Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ Step 1: User Initiates Upload                                       │
├─────────────────────────────────────────────────────────────────────┤
│ User selects file in PCF control                                    │
│   ↓                                                                 │
│ PCF Control validates file (size, type)                             │
│   ↓                                                                 │
│ PCF calls: PUT /api/obo/containers/{containerId}/files/{fileName}   │
│   - Authorization: Bearer {Token A}                                 │
│   - Content-Type: application/octet-stream                          │
│   - Content-Length: {fileSize}                                      │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 2: BFF API Validates & Authorizes                              │
├─────────────────────────────────────────────────────────────────────┤
│ JWT Middleware validates Token A                                    │
│   ↓                                                                 │
│ Authorization Handler checks user permissions:                      │
│   - Extract user ID from token                                      │
│   - Query Dataverse for user access rights                          │
│   - Verify user has WriteAccess for document                        │
│   ↓                                                                 │
│ If authorized, proceed to file upload                               │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 3: OBO Token Exchange (Cached)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ GraphClientFactory.CreateOnBehalfOfClientAsync(Token A)             │
│   ↓                                                                 │
│ Compute token hash: SHA256(Token A) = {hash}                        │
│   ↓                                                                 │
│ Check Redis cache: GET sdap:graph:token:{hash}                      │
│   ↓                                                                 │
│ Cache Hit (95% of requests):                                        │
│   - Return cached Token B (~5ms)                                    │
│   ↓                                                                 │
│ Cache Miss (5% of requests):                                        │
│   - Perform OBO exchange (~200ms)                                   │
│   - Store in Redis with 55-minute TTL                               │
│   - Return Token B                                                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 4: Upload File to SharePoint Embedded                          │
├─────────────────────────────────────────────────────────────────────┤
│ SpeFileStore.UploadFileAsync(containerId, fileName, stream, Token A)│
│   ↓                                                                 │
│ Create Graph client with Token B                                    │
│   ↓                                                                 │
│ Call Graph API:                                                     │
│   PUT /v1.0/storage/fileStorage/containers/{containerId}/drive/     │
│       root:/{fileName}:/content                                     │
│   - Authorization: Bearer {Token B}                                 │
│   - Content-Type: application/octet-stream                          │
│   ↓                                                                 │
│ Graph API validates Token B:                                        │
│   - Signature valid                                                 │
│   - Audience: https://graph.microsoft.com                           │
│   - App ID: 1e40baad... (registered in Container Type)              │
│   ↓                                                                 │
│ Forward to SharePoint Embedded:                                     │
│   - Validate app registration                                       │
│   - Enforce user permissions (from preserved user identity)         │
│   - Store file in container                                         │
│   ↓                                                                 │
│ Return DriveItem with file metadata                                 │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 5: Create Dataverse Document Record                            │
├─────────────────────────────────────────────────────────────────────┤
│ DataverseServiceClientImpl.CreateDocumentAsync(request)             │
│   ↓                                                                 │
│ Authenticate to Dataverse using client secret:                      │
│   - AuthType: ClientSecret                                          │
│   - ClientId: 1e40baad...                                           │
│   - ClientSecret: {from KeyVault}                                   │
│   ↓                                                                 │
│ Create document entity:                                             │
│   - sprk_documentname: {fileName}                                   │
│   - sprk_containerid: {containerId}                                 │
│   - sprk_driveitemid: {driveItem.Id}                                │
│   - sprk_filesize: {fileSize}                                       │
│   - sprk_createdby: {userId}                                        │
│   - statecode: Active                                               │
│   - statuscode: Draft                                               │
│   ↓                                                                 │
│ Return document ID                                                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 6: Publish Document Event                                      │
├─────────────────────────────────────────────────────────────────────┤
│ JobSubmissionService.SubmitJobAsync(new DocumentUploadedEvent       │
│ {                                                                   │
│     DocumentId = documentId,                                        │
│     ContainerId = containerId,                                      │
│     FileName = fileName,                                            │
│     UploadedBy = userId,                                            │
│     UploadedAt = DateTime.UtcNow                                    │
│ })                                                                  │
│   ↓                                                                 │
│ Send message to Service Bus queue "document-events"                 │
│   ↓                                                                 │
│ DocumentEventProcessor receives message (background)                │
│   - Process document (OCR, virus scan, etc.)                        │
│   - Update Dataverse status: statuscode = Published                 │
│   - Send notification                                               │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 7: Return Response to PCF                                      │
├─────────────────────────────────────────────────────────────────────┤
│ BFF API returns 200 OK:                                             │
│ {                                                                   │
│   "documentId": "guid",                                             │
│   "driveItemId": "guid",                                            │
│   "fileName": "document.pdf",                                       │
│   "fileSize": 1024000,                                              │
│   "uploadedAt": "2025-10-13T22:00:00Z",                             │
│   "webUrl": "https://..."                                           │
│ }                                                                   │
│   ↓                                                                 │
│ PCF Control updates UI:                                             │
│   - Add file to list                                                │
│   - Show success message                                            │
│   - Enable download/preview buttons                                 │
└─────────────────────────────────────────────────────────────────────┘
```

**Total Latency Breakdown** (with Phase 4 caching):
- JWT Validation: ~10ms
- Authorization Check (Dataverse): ~50ms
- OBO Exchange (95% cached): ~5ms
- File Upload (SPE): ~100ms (depends on file size)
- Dataverse Record Creation: ~50ms
- Service Bus Message: ~10ms
- **Total: ~225ms** (excluding file transfer time)

---

### 9.2 File Download Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ Step 1: User Requests File                                          │
├─────────────────────────────────────────────────────────────────────┤
│ User clicks download in PCF control                                 │
│   ↓                                                                 │
│ PCF calls: GET /api/obo/containers/{containerId}/files/{fileId}     │
│   - Authorization: Bearer {Token A}                                 │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 2: BFF API Validates & Authorizes                              │
├─────────────────────────────────────────────────────────────────────┤
│ JWT Middleware validates Token A                                    │
│   ↓                                                                 │
│ Authorization Handler checks user permissions:                      │
│   - Query Dataverse for document access                             │
│   - Verify user has ReadAccess                                      │
│   ↓                                                                 │
│ If authorized, proceed to file download                             │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 3: OBO Token Exchange (Cached)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ GraphClientFactory.CreateOnBehalfOfClientAsync(Token A)             │
│   ↓                                                                 │
│ Cache hit: Return Token B (~5ms, 95% of requests)                   │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 4: Download File from SharePoint Embedded                      │
├─────────────────────────────────────────────────────────────────────┤
│ SpeFileStore.DownloadFileAsync(containerId, fileId, Token A)        │
│   ↓                                                                 │
│ Create Graph client with Token B                                    │
│   ↓                                                                 │
│ Call Graph API:                                                     │
│   GET /v1.0/storage/fileStorage/containers/{containerId}/drive/     │
│       items/{fileId}/content                                        │
│   - Authorization: Bearer {Token B}                                 │
│   ↓                                                                 │
│ SharePoint Embedded:                                                │
│   - Validate app registration                                       │
│   - Enforce user permissions                                        │
│   - Stream file content                                             │
│   ↓                                                                 │
│ Return file stream                                                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Step 5: Return File to PCF                                          │
├─────────────────────────────────────────────────────────────────────┤
│ BFF API streams file with headers:                                  │
│   - Content-Type: application/pdf                                   │
│   - Content-Disposition: attachment; filename="document.pdf"        │
│   - Content-Length: {fileSize}                                      │
│   ↓                                                                 │
│ Browser downloads file or opens in viewer                           │
└─────────────────────────────────────────────────────────────────────┘
```

**Total Latency**: ~100ms (excluding file transfer time)

---

## 10. SECURITY MODEL

### 10.1 Defense in Depth Layers

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 1: Network Security                                       │
├─────────────────────────────────────────────────────────────────┤
│ ✓ HTTPS Only (TLS 1.2+)                                         │
│ ✓ CORS restricted origins                                       │
│ ✓ Azure Front Door (optional DDoS protection)                   │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 2: Authentication (Azure AD)                              │
├─────────────────────────────────────────────────────────────────┤
│ ✓ JWT Bearer tokens with signature validation                   │
│ ✓ Token expiration enforcement (1 hour)                         │
│ ✓ Audience validation (api://1e40baad...)                       │
│ ✓ Issuer validation (Azure AD tenant)                           │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 3: Authorization (Dataverse)                              │
├─────────────────────────────────────────────────────────────────┤
│ ✓ Row-Level Security (RLS) via sprk_documentaccess              │
│ ✓ Operation-specific policies (read/write/delete/share)         │
│ ✓ Custom authorization handlers                                 │
│ ✓ Authorization rules evaluated in order                        │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 4: Resource Validation (SharePoint Embedded)              │
├─────────────────────────────────────────────────────────────────┤
│ ✓ Container Type registration validation                        │
│ ✓ User permissions enforced by SharePoint                       │
│ ✓ App registration validated (appid in Token B)                 │
│ ✓ File-level permissions inheritance                            │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│ Layer 5: Audit & Monitoring                                     │
├─────────────────────────────────────────────────────────────────┤
│ ✓ All operations logged to Application Insights                 │
│ ✓ Audit trail in Dataverse (sprk_auditlog)                      │
│ ✓ Security alerts on suspicious activity                        │
│ ✓ Compliance reporting                                          │
└─────────────────────────────────────────────────────────────────┘
```

---

### 10.2 Secrets Management

| Secret | Storage | Access Method | Rotation Policy |
|--------|---------|---------------|-----------------|
| **BFF API Client Secret** | Azure Key Vault | App Service Managed Identity | 365 days |
| **Dataverse URL** | Azure Key Vault | App Service Managed Identity | N/A (configuration) |
| **Service Bus Connection String** | Azure Key Vault | App Service Managed Identity | As needed |
| **Redis Connection String** | Azure Key Vault | App Service Managed Identity | As needed |
| **Certificates** | Azure Key Vault | App Service Managed Identity | Before expiration |

**Key Vault Access**:
- ✅ App Service Managed Identity: Key Vault Secrets User
- ✅ Deployment Service Principal: Key Vault Administrator (CI/CD only)
- ❌ No secrets in source code
- ❌ No secrets in App Service configuration (KeyVault references only)

---

### 10.3 Rate Limiting

**Rate Limit Policies** (per user):

| Policy | Window | Limit | Type | Applied To |
|--------|--------|-------|------|------------|
| `graph-read` | 1 minute | 100 requests | Sliding Window | File list, metadata |
| `graph-write` | 1 minute | 20 requests | Token Bucket | File upload, update |
| `dataverse-query` | 1 minute | 50 requests | Sliding Window | Authorization queries |
| `upload-heavy` | Concurrent | 5 connections | Concurrency | Large file uploads |
| `job-submission` | 1 minute | 10 requests | Fixed Window | Background jobs |
| `anonymous` | 1 minute | 10 requests | Fixed Window | Health checks (no auth) |

**429 Response**:
```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please retry after the specified duration.",
  "instance": "/api/obo/containers/{id}/files/{id}",
  "retryAfter": "60 seconds"
}
```

---

## 11. DEPLOYMENT ARCHITECTURE

### 11.1 Environments

| Environment | Purpose | URL | Dataverse | SPE Container Type |
|-------------|---------|-----|-----------|-------------------|
| **Development** | Local dev & testing | `http://localhost:5000` | spaarkedev1 | Spaarke PAYGO 1 |
| **Azure Dev** | Shared dev environment | `spe-api-dev-67e2xz.azurewebsites.net` | spaarkedev1 | Spaarke PAYGO 1 |
| **Staging** | Pre-production testing | (TBD) | (TBD) | (TBD) |
| **Production** | Live system | (TBD) | (TBD) | (TBD) |

---

### 11.2 Deployment Process

#### Build & Package

```bash
# Build API project
cd /c/code_files/spaarke
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj --configuration Release

# Publish for deployment
dotnet publish src/api/Spe.Bff.Api/Spe.Bff.Api.csproj \
  --configuration Release \
  --output src/api/Spe.Bff.Api/publish

# Create deployment package
cd src/api/Spe.Bff.Api/publish
pwsh -Command "Compress-Archive -Path * -DestinationPath ../deployment.zip -Force"
```

#### Deploy to Azure

```bash
# Deploy using Azure CLI
az webapp deploy \
  --resource-group SharePointEmbedded \
  --name spe-api-dev-67e2xz \
  --src-path deployment.zip \
  --type zip

# Verify deployment
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Check logs
az webapp log tail \
  --resource-group SharePointEmbedded \
  --name spe-api-dev-67e2xz
```

---

### 11.3 Health Checks

**Endpoints**:

| Endpoint | Description | Expected Response |
|----------|-------------|-------------------|
| `/healthz` | Overall health | `Healthy` (200 OK) |
| `/healthz/dataverse` | Dataverse connectivity | `{"status":"healthy","message":"Dataverse connection successful"}` |
| `/healthz/dataverse/crud` | Dataverse CRUD test | `{"status":"healthy","message":"Dataverse CRUD operations successful"}` |
| `/ping` | Basic availability | `{"service":"Spe.Bff.Api","version":"1.0.0","timestamp":"..."}` |

**Monitoring**:
- Azure Application Insights for telemetry
- Azure Monitor alerts for failures
- Custom health check dashboard

---

## APPENDIX

### A. Glossary

| Term | Definition |
|------|------------|
| **SDAP** | Spaarke Document Access Platform |
| **SPE** | SharePoint Embedded - cloud-native file storage service |
| **BFF** | Backend For Frontend - API pattern providing tailored interface for frontend |
| **OBO** | On-Behalf-Of - OAuth 2.0 flow for delegated user permissions |
| **PCF** | Power Apps Component Framework - extensibility framework for Power Apps |
| **RLS** | Row-Level Security - data access restriction at record level |
| **MI** | Managed Identity - Azure identity for services without secrets |
| **S2S** | Service-to-Service - authentication between services without user context |
| **ADR** | Architectural Decision Record - documented design decision |

---

### B. Quick Reference Links

**Azure Portal**:
- App Service: https://portal.azure.com/#@spaarke.com/resource/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.Web/sites/spe-api-dev-67e2xz
- Key Vault: https://portal.azure.com/#@spaarke.com/resource/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/SharePointEmbedded/providers/Microsoft.KeyVault/vaults/spaarke-spekvcert

**Dataverse**:
- Environment: https://admin.powerplatform.microsoft.com/environments/b5a401dd-b42b-e84a-8cab-2aef8471220d
- Maker Portal: https://make.powerapps.com/environments/b5a401dd-b42b-e84a-8cab-2aef8471220d

**SharePoint Admin**:
- SPO Admin: https://spaarke-admin.sharepoint.com

---

### C. Troubleshooting Guide

#### Issue: 401 Unauthorized

**Symptoms**: API returns 401 for all requests

**Possible Causes**:
1. Token expired (check `exp` claim)
2. Wrong audience (check `aud` claim)
3. Invalid signature (check Azure AD signing keys)
4. App registration misconfigured

**Resolution**:
```bash
# Decode token to inspect claims
jwt decode {token}

# Verify Azure AD configuration
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

---

#### Issue: 403 Forbidden (SPE)

**Symptoms**: File operations return 403 from Graph/SPE

**Possible Causes**:
1. BFF API not registered in Container Type
2. User lacks permissions in container
3. Token B has wrong appid
4. Container doesn't exist

**Resolution**:
```powershell
# Verify Container Type registration
Get-SPOContainer -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06

# Check container permissions
Get-SPOContainerMember -ContainerId {containerId}
```

---

#### Issue: 500 Internal Server Error (Dataverse)

**Symptoms**: Dataverse operations fail with 500

**Possible Causes**:
1. Dataverse connection failed (wrong URL/credentials)
2. Application User missing in Dataverse
3. Client secret expired
4. Network connectivity issue

**Resolution**:
```bash
# Test Dataverse health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse

# Check Application Insights logs
az monitor app-insights query \
  --app {app-insights-id} \
  --analytics-query "traces | where message contains 'Dataverse' | take 50"
```

---

### D. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| V1 | 2025-09-22 | Initial | First architecture documentation |
| **V2** | **2025-10-13 22:13:10** | **Current** | **Comprehensive refactoring with sdap_V2 alignment, simplified authentication, ADR compliance** |

---

## DOCUMENT END

**For questions or clarifications, please contact**: ralph.schroeder@spaarke.com

**Related Documents**:
- `SDAP-V2-ALIGNMENT-ANALYSIS.md` - Gap analysis vs V2 target architecture
- `SPE-BFF-API-COMPONENT-INVENTORY.md` - Detailed component catalog
- `DATAVERSE-APPLICATION-USER-CLEANUP.md` - Application User configuration guide
- `SDAP-AZURE-ENVIRONMENT-SETTINGS.md` - Azure resource reference
- `dev/projects/sdap_V2/TARGET-ARCHITECTURE.md` - V2 refactoring target
