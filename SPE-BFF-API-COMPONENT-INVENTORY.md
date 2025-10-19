# Spe.Bff.Api - Complete Component Inventory

**Generated**: 2025-10-13
**Purpose**: Complete inventory of all components, their functions, processes, and Azure resource references

---

## 1. ARCHITECTURE OVERVIEW

### System Purpose
Backend-For-Frontend API that provides secure access to:
- **SharePoint Embedded (SPE)** file storage operations
- **Microsoft Dataverse** metadata and authorization
- **Microsoft Graph API** for SPE container and file management

### Authentication Pattern
**Dual Authentication Architecture**:
1. **OBO (On-Behalf-Of) Flow**: User-context operations (file upload/download)
   - Uses client secret for token exchange
   - Preserves user identity for SharePoint permissions
2. **Managed Identity**: Service-context operations (Dataverse queries)
   - Currently configured to use system-assigned MI
   - No secrets required in code

---

## 2. AZURE RESOURCE IDENTIFIERS

### App Registrations

#### SPE-BFF-API (Primary API)
- **App ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Purpose**: BFF API identity for JWT validation and OBO token exchange
- **Used In**:
  - `appsettings.json`: `API_APP_ID`, `AzureAd.ClientId`, `AzureAd.Audience`
  - `GraphClientFactory.cs`: ConfidentialClientApplication for OBO flow
- **Authentication**: Client Secret (stored in KeyVault: `BFF-API-ClientSecret`)
- **Required Permissions**:
  - `Files.ReadWrite.All` (delegated)
  - `Sites.FullControl.All` (delegated)
- **Configuration**:
  - `knownClientApplications`: Contains `170c98e1...` (PCF client)
  - Must be registered in SPE Container Type

#### Spaarke DSM-SPE Dev 2 (PCF Client)
- **App ID**: `170c98e1-d486-4355-bcbe-170454e0207c`
- **Purpose**: Public client (SPA/PCF control) for user authentication
- **Used In**: Pre-authorized in SPE-BFF-API manifest
- **Authentication**: Public client (no secrets)
- **Has Application User in Dataverse**: Yes

### Azure Resources

#### App Service: spe-api-dev-67e2xz
- **Resource Group**: `spe-infrastructure-westus2`
- **Subscription**: `Spaarke SPE Subscription 1` (484bc857-3802-427f-9ea5-ca47b43db0f0)
- **System-Assigned Managed Identity**:
  - **Principal ID (Object ID)**: `56ae2188-c978-4734-ad16-0bc288973f20`
  - **App ID (Client ID)**: `6bbcfa82-14a0-40b5-8695-a271f4bac521`
  - **Display Name**: `spe-api-dev-67e2xz`
  - **Has Application User in Dataverse**: Yes (System Admin role)
  - **KeyVault Access**: Key Vault Secrets User role

#### Key Vault: spaarke-spekvcert
- **Resource Group**: `SharePointEmbedded`
- **Vault URI**: `https://spaarke-spekvcert.vault.azure.net/`
- **RBAC Enabled**: Yes
- **Secrets Referenced**:
  - `SPRK-DEV-DATAVERSE-URL`: Dataverse environment URL
  - `BFF-API-ClientSecret`: Client secret for OBO flow
  - `ServiceBus-ConnectionString`: Azure Service Bus connection string

#### Azure Service Bus
- **Connection String**: Stored in KeyVault
- **Queues**:
  - `sdap-jobs`: Background job processing
  - `document-events`: Document event processing
- **Issue**: Currently returning 401 errors (Managed Identity needs Service Bus Data Receiver role)

#### SharePoint Embedded
- **Default Container Type ID**: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- **Registered App**: SPE-BFF-API (`1e40baad...`) must be registered in Container Type

#### Dataverse Environment
- **Service URL**: Retrieved from KeyVault secret `SPRK-DEV-DATAVERSE-URL`
- **Environment Name**: `spaarkedev1`
- **Application Users**:
  - `170c98e1...` (Spaarke DSM-SPE Dev 2)
  - `6bbcfa82...` (spe-api-dev-67e2xz Managed Identity)
  - **Missing**: `1e40baad...` (SPE-BFF-API) - should NOT have Application User

### Tenant
- **Tenant ID**: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

---

## 3. COMPONENT INVENTORY

### 3.1 API Endpoints (Minimal API Pattern)

#### **DocumentsEndpoints.cs**
- **Process**: SharePoint Embedded container and file management
- **Key Operations**:
  - `GET /api/documents/containers` - List SPE containers
  - `POST /api/documents/containers` - Create SPE container
  - `GET /api/documents/containers/{id}/items` - List files in container
  - `GET /api/documents/items/{id}` - Get file metadata
- **Authentication**: Requires JWT bearer token
- **Authorization**: Custom policies (e.g., `canlistcontainers`, `canreadfiles`)
- **Dependencies**: `IGraphClientFactory`, `SpeFileStore`

#### **OBOEndpoints.cs**
- **Process**: On-Behalf-Of file operations (user-enforced)
- **Key Operations**:
  - `GET /api/obo/containers/{containerId}/files/{fileId}` - Download file as user
  - `PUT /api/obo/containers/{containerId}/files/{fileId}` - Upload file as user
  - `POST /api/obo/containers/{containerId}/upload` - Initiate upload session
- **Authentication**: OBO flow with user token
- **Authorization**: User's SharePoint permissions enforced
- **Dependencies**: `IGraphClientFactory.CreateOnBehalfOfClientAsync()`
- **Azure IDs**: Uses `API_APP_ID` for OBO token exchange

#### **DataverseDocumentsEndpoints.cs**
- **Process**: Dataverse document metadata CRUD
- **Key Operations**:
  - `POST /api/dataverse/documents` - Create document record
  - `GET /api/dataverse/documents/{id}` - Get document metadata
  - `PUT /api/dataverse/documents/{id}` - Update document
  - `DELETE /api/dataverse/documents/{id}` - Delete document
  - `GET /api/dataverse/documents/container/{containerId}` - List by container
- **Authentication**: Managed Identity to Dataverse
- **Dependencies**: `IDataverseService`

#### **UploadEndpoints.cs**
- **Process**: Large file upload management (chunked uploads)
- **Key Operations**:
  - `POST /api/upload/session` - Create upload session
  - `PUT /api/upload/session/{id}/chunk` - Upload file chunk
  - `POST /api/upload/session/{id}/complete` - Finalize upload
- **Dependencies**: `UploadSessionManager`, `GraphClientFactory`

#### **PermissionsEndpoints.cs**
- **Process**: User permission queries for UI
- **Key Operations**:
  - `GET /api/permissions/user` - Get current user permissions
  - `GET /api/permissions/document/{id}` - Get document-specific permissions
- **Dependencies**: `AuthorizationService` (Spaarke.Core)

#### **UserEndpoints.cs**
- **Process**: User identity and profile information
- **Key Operations**:
  - `GET /api/user/me` - Get current user info
  - `GET /api/user/capabilities` - Get user capabilities
- **Authentication**: JWT claims extraction

### 3.2 Infrastructure Components

#### **GraphClientFactory.cs**
- **Process**: Creates Microsoft Graph clients with appropriate authentication
- **Key Methods**:
  - `CreateAppOnlyClient()`: Managed Identity for app-only operations
  - `CreateOnBehalfOfClientAsync(string token)`: OBO for user operations
- **Configuration**:
  - `UAMI_CLIENT_ID`: User-assigned MI client ID (for app-only)
  - `TENANT_ID`: Azure AD tenant
  - `API_APP_ID`: BFF API app ID (for OBO)
  - `API_CLIENT_SECRET`: Client secret (for OBO)
- **Authentication Flow**:
  ```
  Local Dev: ClientSecretCredential (_tenantId, _clientId, _clientSecret)
  Azure: DefaultAzureCredential with ManagedIdentityClientId = _uamiClientId
  OBO: ConfidentialClientApplication.AcquireTokenOnBehalfOf()
  ```
- **Issue**: `UAMI_CLIENT_ID` configuration is unclear - may need system-assigned MI instead

#### **UploadSessionManager.cs**
- **Process**: Manages chunked file upload sessions
- **Key Operations**:
  - Create upload session with Graph API
  - Track upload progress
  - Handle chunk retries
  - Finalize upload
- **Dependencies**: `GraphServiceClient` (OBO flow)

#### **SpeFileStore.cs**
- **Process**: Abstraction layer for SPE file operations
- **Key Operations**:
  - File CRUD operations
  - Container management
  - Permission management
- **Dependencies**: `GraphServiceClient`

#### **ContainerOperations.cs / DriveItemOperations.cs / UserOperations.cs**
- **Process**: Helper classes for specific Graph API operations
- **Purpose**: Encapsulate Graph SDK calls with error handling

#### **GraphHttpMessageHandler.cs**
- **Process**: Resilience patterns for Graph API calls (Task 4.1)
- **Features**:
  - Retry policy (3 retries with exponential backoff)
  - Circuit breaker (5 failures = 30s break)
  - Timeout (30 seconds)
  - Honors Retry-After headers (429 throttling)
- **Configuration**: `GraphResilienceOptions` in appsettings.json

### 3.3 Authentication & Authorization

#### **TokenHelper.cs**
- **Process**: JWT token extraction and validation
- **Key Operations**:
  - Extract bearer token from request
  - Parse JWT claims
  - Validate token structure

#### **ResourceAccessHandler.cs**
- **Process**: Custom authorization handler
- **Logic**:
  1. Extract document ID from request
  2. Query Dataverse for user access rights
  3. Map operation to required Dataverse AccessRights
  4. Authorize or deny based on user's access level
- **Dependencies**: `AuthorizationService` (Spaarke.Core)

#### **SecurityHeadersMiddleware.cs**
- **Process**: Adds security headers to all responses
- **Headers Added**:
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `X-XSS-Protection: 1; mode=block`
  - `Strict-Transport-Security` (HSTS)

### 3.4 Background Services

#### **DocumentEventProcessor.cs**
- **Process**: Service Bus consumer for document events
- **Queue**: `document-events`
- **Configuration**: `DocumentEventProcessor` section in appsettings.json
- **Features**:
  - Concurrent message processing (MaxConcurrentCalls: 5)
  - Retry logic (MaxRetryAttempts: 3)
  - Dead letter queue support
- **Issue**: Currently failing with 401 Unauthorized (MI needs Service Bus role)

#### **ServiceBusJobProcessor.cs**
- **Process**: Background job processor
- **Queue**: `sdap-jobs`
- **Configuration**: `Jobs:ServiceBus` section
- **Handlers**: `DocumentProcessingJobHandler`

#### **IdempotencyService.cs**
- **Process**: Prevents duplicate job processing
- **Storage**: Redis (distributed) or in-memory cache
- **Logic**: Uses job ID to track processed jobs

### 3.5 Shared Libraries

#### **Spaarke.Core**
- **Components**:
  - `AuthorizationService`: Dataverse-based authorization
  - `OperationAccessPolicy`: Maps operations to required Dataverse AccessRights
  - `RequestCache`: Distributed cache wrapper
  - Authorization rules (ExplicitGrantRule, ExplicitDenyRule, etc.)
- **Dependencies**: Dataverse, Distributed Cache

#### **Spaarke.Dataverse**
- **Components**:
  - `DataverseServiceClientImpl`: ServiceClient implementation
  - `DataverseWebApiService`: Web API implementation
  - `DataverseAccessDataSource`: Access rights queries
- **Current Authentication**: Managed Identity (ManagedIdentityCredential)
- **Issue**: Current implementation failing with "No User Assigned or Delegated MI found"
- **Configuration Needed**:
  - `Dataverse:ServiceUrl`: Dataverse environment URL (from KeyVault)
  - `ManagedIdentity:ClientId`: Optional - for user-assigned MI

---

## 4. CONFIGURATION STRUCTURE

### appsettings.json
```json
{
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "DEFAULT_CT_ID": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",

  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(...ServiceBus-ConnectionString)",
    "Redis": null
  },

  "AzureAd": {
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },

  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(...SPRK-DEV-DATAVERSE-URL)",
    "ClientSecret": "@Microsoft.KeyVault(...BFF-API-ClientSecret)"  // NOT USED
  },

  "GraphResilience": {
    "RetryCount": 3,
    "RetryBackoffSeconds": 2,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDurationSeconds": 30,
    "TimeoutSeconds": 30
  }
}
```

### App Service Configuration (Environment Variables)
**Currently Set**:
- `TENANT_ID`
- `API_APP_ID`
- `API_CLIENT_SECRET` (from KeyVault)
- `AzureAd__ClientId`
- `AzureAd__Audience`
- `AzureAd__TenantId`
- `Dataverse__ServiceUrl` (from KeyVault)
- `Dataverse__ClientSecret` (from KeyVault - NOT USED)

**Missing/Unclear**:
- `UAMI_CLIENT_ID`: Referenced in GraphClientFactory but not in App Service settings
- `ManagedIdentity__ClientId`: Removed during troubleshooting

---

## 5. DATA FLOWS

### Flow 1: User File Upload (OBO)
1. **PCF Control** → Authenticates user with MSAL.js (`170c98e1...`)
2. **PCF** → Calls `/api/obo/containers/{id}/files/{name}` with user token (Token A)
3. **BFF API** → Validates Token A (audience: `1e40baad...`)
4. **GraphClientFactory** → Exchanges Token A for Token B using OBO
   - Uses `ConfidentialClientApplication` with client secret
   - Token B audience: Graph API
   - Token B appid: `1e40baad...` (BFF API)
5. **BFF API** → Uploads file to SPE using Token B
6. **SPE** → Validates Token B, enforces user permissions
7. **BFF API** → Creates Dataverse document record using Managed Identity

### Flow 2: Document Metadata Query
1. **PCF Control** → Calls `/api/dataverse/documents/{id}` with user token
2. **BFF API** → Validates user token
3. **ResourceAccessHandler** → Queries Dataverse for user access rights using MI
4. **DataverseServiceClientImpl** → Uses Managed Identity to query Dataverse
5. **BFF API** → Returns document metadata if authorized

### Flow 3: Background Job Processing
1. **API Endpoint** → Submits job to Service Bus queue
2. **ServiceBusJobProcessor** → Receives message
3. **IdempotencyService** → Checks if job already processed
4. **DocumentProcessingJobHandler** → Executes job logic
5. **Handler** → Uses Managed Identity for Dataverse/Graph operations

---

## 6. CURRENT ISSUES

### Issue 1: Dataverse Managed Identity Authentication Failure
**Symptom**: `ManagedIdentityCredential authentication failed: No User Assigned or Delegated MI found`

**Root Cause**: DataverseServiceClientImpl using ManagedIdentityCredential, but error suggests MI not recognized

**Possible Causes**:
- System-assigned MI (`6bbcfa82...`) not properly configured for Dataverse
- Dataverse doesn't support MI auth directly (may require app registration)
- ServiceClient may not support token provider pattern correctly

**Impact**: All Dataverse operations fail, breaking authorization and metadata CRUD

### Issue 2: Service Bus Authentication Failure
**Symptom**: `Put token failed. status-code: 401` in document-events queue

**Root Cause**: Managed Identity lacks Service Bus Data Receiver role

**Fix**: Grant role assignment:
```bash
az role assignment create \
  --assignee 56ae2188-c978-4734-ad16-0bc288973f20 \
  --role "Azure Service Bus Data Receiver" \
  --scope /subscriptions/.../resourceGroups/.../providers/Microsoft.ServiceBus/namespaces/...
```

### Issue 3: UAMI_CLIENT_ID Configuration Unclear
**Symptom**: GraphClientFactory references `UAMI_CLIENT_ID` but it's not in App Service settings

**Questions**:
- Should this be the system-assigned MI app ID (`6bbcfa82...`)?
- Or a separate user-assigned MI?
- For app-only Graph operations, which MI should be used?

---

## 7. VALIDATION CHECKLIST

### Azure AD App Registrations
- [ ] SPE-BFF-API (`1e40baad...`)
  - [ ] API permissions: Files.ReadWrite.All, Sites.FullControl.All (delegated)
  - [ ] knownClientApplications contains `170c98e1...`
  - [ ] Client secret exists and stored in KeyVault
  - [ ] Registered in SPE Container Type
- [ ] Spaarke DSM-SPE Dev 2 (`170c98e1...`)
  - [ ] Public client configuration
  - [ ] No client secret (public client)
  - [ ] Has Application User in Dataverse

### Azure Resources
- [ ] App Service `spe-api-dev-67e2xz`
  - [ ] System-assigned MI enabled
  - [ ] MI has "Key Vault Secrets User" on spaarke-spekvcert
  - [ ] MI has "Azure Service Bus Data Receiver" on Service Bus namespace
  - [ ] MI has Application User in Dataverse with System Admin role
- [ ] Key Vault `spaarke-spekvcert`
  - [ ] Secrets exist: SPRK-DEV-DATAVERSE-URL, BFF-API-ClientSecret, ServiceBus-ConnectionString
  - [ ] RBAC enabled
  - [ ] MI has access

### Dataverse
- [ ] Application User for `6bbcfa82...` (spe-api-dev-67e2xz MI)
  - [ ] Security role: System Administrator
  - [ ] Application ID: `6bbcfa82...`
- [ ] Application User for `170c98e1...` (PCF client)
  - [ ] Appropriate security roles
- [ ] NO Application User for `1e40baad...` (BFF API should NOT have one)

### SharePoint Embedded
- [ ] Container Type exists (ID: `8a6ce34c...`)
- [ ] SPE-BFF-API (`1e40baad...`) registered in Container Type
- [ ] Permissions configured correctly

---

## 8. RECOMMENDED NEXT STEPS

1. **Fix Dataverse Authentication**:
   - Determine correct authentication method for Dataverse
   - Option A: Use client secret (revert to connection string pattern)
   - Option B: Fix MI authentication (verify Dataverse Application User setup)
   - Option C: Use different auth pattern (OAuth2 client credentials flow)

2. **Fix Service Bus Authentication**:
   - Grant "Azure Service Bus Data Receiver" role to MI
   - Test DocumentEventProcessor startup

3. **Clarify UAMI_CLIENT_ID Configuration**:
   - Determine if system-assigned MI should be used
   - Update configuration accordingly
   - Test app-only Graph operations

4. **Validation Testing**:
   - Test `/healthz/dataverse` endpoint
   - Test file upload end-to-end
   - Test background job processing
   - Monitor Application Insights for errors

---

## 9. ARCHITECTURE DOCUMENTS

Reference documents for validation:
- `CORRECT-SPE-INTEGRATION-PATTERN.md` - Authoritative SPE integration guide
- `AUTHENTICATION-ARCHITECTURE.md` - Auth flow documentation
- `SDAP-ARCHITECTURE-ASSESSMENT.md` - Architecture review
- `OBO-403-RESOLUTION-SUMMARY.md` - Previous OBO issue resolution
