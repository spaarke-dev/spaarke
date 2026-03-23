# SDAP Authentication Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Authentication & Security section)
> **Last Updated**: March 2026
> **Applies To**: Authentication code, token handling, Azure AD configuration

---

## TL;DR

SDAP uses nine authentication patterns: (1) MSAL.js in PCF for user tokens, (2) OBO for Graph API, (3) ClientSecret for server-to-Dataverse, (4) OBO for AI file access, (5) OBO for Dataverse authorization, (6) App-only for email processing, (7) MSAL ssoSilent for Code Pages embedded on forms, (8) Parent-to-Child token bridge for dialogs opened from authenticated parents, (9) resolveRuntimeConfig + initAuth for standalone wizard code pages. Patterns 1-3 cover the core flows. Pattern 9 is required for all wizard code pages under `src/solutions/` — using `fetch.bind(window)` instead returns 401. **Tenant ID note**: `resolveRuntimeConfig().tenantId` is empty on first page load (Xrm initializes it asynchronously). Always patch from `getAuthProvider().getTenantId()` after auth init — see Pattern 9 "Tenant ID at Bootstrap" section. `resolveTenantIdSync()` now resolves via: (1) MSAL authority URL, (2) MSAL `accounts[0].tenantId` (JWT-sourced, reliable post-auth), (3) Xrm frame walk.

---

## Applies When

- Debugging authentication failures (401, AADSTS errors)
- Adding new API endpoints that need authentication
- Understanding why user context needed vs app context
- Configuring Azure AD app registrations
- Troubleshooting token scope issues

---

## Authentication Flow Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. USER (Browser) → Dataverse SSO                                       │
│    Already authenticated via Entra ID                                   │
└─────────────────────────────────────────────────────────────────────────┘
                    │
                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 2. PCF CONTROL → MSAL.js                                                │
│    Scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation│
│    Method: acquireTokenSilent (ssoSilent)                               │
│    Result: User access token for BFF API                                │
└─────────────────────────────────────────────────────────────────────────┘
                    │
                    ↓ Authorization: Bearer {user_token}
┌─────────────────────────────────────────────────────────────────────────┐
│ 3. BFF API → Validates JWT                                              │
│    Issuer: https://login.microsoftonline.com/{tenant}/v2.0              │
│    Audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c                 │
└─────────────────────────────────────────────────────────────────────────┘
           │                                      │
           ↓ OBO Exchange                         ↓ ClientSecret
┌──────────────────────────┐      ┌──────────────────────────────────────┐
│ 4. GRAPH API             │      │ 5. DATAVERSE                          │
│    grant_type: jwt-bearer│      │    AuthType: ClientSecret             │
│    User context preserved│      │    App-only (no user context)         │
│    For: File operations  │      │    For: Metadata queries              │
└──────────────────────────┘      └──────────────────────────────────────┘
```

---

## Pattern 1: MSAL.js in PCF Control

**When**: User needs token to call BFF API

```typescript
// MsalAuthProvider.ts
export class MsalAuthProvider {
    private msalInstance: PublicClientApplication;
    
    constructor() {
        this.msalInstance = new PublicClientApplication({
            auth: {
                clientId: "5175798e-f23e-41c3-b09b-7a90b9218189",  // PCF App
                authority: "https://login.microsoftonline.com/{tenant}",
                redirectUri: "https://spaarkedev1.crm.dynamics.com"
            },
            cache: {
                cacheLocation: 'sessionStorage'
            }
        });
    }

    async getToken(scopes: string[]): Promise<string> {
        const account = this.msalInstance.getAllAccounts()[0];
        
        const result = await this.msalInstance.acquireTokenSilent({
            scopes,
            account
        });
        
        return result.accessToken;
    }
}

// Usage - CORRECT scope format
const token = await authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);
```

**Common Mistake**: Using friendly name `api://spe-bff-api/...` instead of full Application ID URI.

> **Note**: Code Pages use a different client ID (`170c98e1-d486-4355-bcbe-170454e0207c`) — see Pattern 7.

---

## Pattern 2: On-Behalf-Of (OBO) Token Exchange

**When**: BFF API needs to call Graph API on behalf of user (file operations, send-as-user email)

```csharp
// GraphClientFactory.cs — Graph SDK v5 with AzureIdentityAuthenticationProvider
public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
{
    // 1. Extract Bearer token from Authorization header
    var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

    // 2. Check Redis cache first (55-min TTL, keyed by SHA256 hash)
    var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
    var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);
    if (cachedToken != null)
        return CreateGraphClientFromToken(cachedToken);  // Cache HIT (~5ms)

    // 3. Cache MISS — OBO exchange (~200ms)
    var result = await _cca.AcquireTokenOnBehalfOf(
        new[] { "https://graph.microsoft.com/.default" },  // Always .default
        new UserAssertion(userAccessToken)
    ).ExecuteAsync();

    // 4. Cache and create client
    await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));
    return CreateGraphClientFromToken(result.AccessToken);
}

private GraphServiceClient CreateGraphClientFromToken(string accessToken)
{
    // SimpleTokenCredential wraps pre-acquired token as Azure.Core.TokenCredential
    var tokenCredential = new SimpleTokenCredential(accessToken);
    var authProvider = new AzureIdentityAuthenticationProvider(
        tokenCredential,
        scopes: new[] { "https://graph.microsoft.com/.default" }
    );
    // Named HttpClient with Polly resilience (retry, circuit breaker, timeout)
    var httpClient = _httpClientFactory.CreateClient("GraphApiClient");
    return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/v1.0");
}
```

**OBO Request Details**:
```
POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token

grant_type:          urn:ietf:params:oauth2:grant-type:jwt-bearer
client_id:           1e40baad-e065-4aea-a8d4-4b7ab273458c
client_secret:       {BFF_CLIENT_SECRET}
assertion:           {user_access_token}
requested_token_use: on_behalf_of
scope:               https://graph.microsoft.com/.default
```

**Why OBO**: User's permissions apply to SharePoint files - can't use app-only.

---

## Pattern 3: ClientSecret for Dataverse

**When**: BFF API needs Dataverse metadata (no user context needed)

```csharp
// DataverseServiceClientImpl.cs
public DataverseServiceClientImpl(IConfiguration config)
{
    var connectionString = 
        $"AuthType=ClientSecret;" +
        $"Url={config["Dataverse:ServiceUrl"]};" +
        $"ClientId={config["API_APP_ID"]};" +
        $"ClientSecret={config["API_CLIENT_SECRET"]}";

    _serviceClient = new ServiceClient(connectionString);
}
```

**Why ClientSecret over ManagedIdentity**: More reliable, easier to configure, Microsoft's recommended approach for ServiceClient.

**Prerequisite**: Application User must exist in Dataverse with System Administrator role (or custom role with metadata read permissions).

---

## Pattern 4: OBO for AI Analysis (SPE File Access)

**When**: AI analysis service needs to download files from SharePoint Embedded

**Critical**: AI analysis features that process documents stored in SPE containers **must use OBO authentication** via `HttpContext`, not app-only authentication. App-only tokens don't have SPE container-level permissions and will return HTTP 403 (Access Denied).

```csharp
// AnalysisOrchestrationService.cs
private async Task<string> ExtractDocumentTextAsync(
    DocumentEntity document,
    HttpContext httpContext,  // Required for OBO token exchange
    CancellationToken cancellationToken)
{
    // ✅ CORRECT: OBO authentication via HttpContext
    using var fileStream = await _speFileStore.DownloadFileAsUserAsync(
        httpContext,
        document.GraphDriveId!,
        document.GraphItemId!,
        cancellationToken);

    var result = await _textExtractor.ExtractTextAsync(
        fileStream,
        document.FileName ?? "document",
        cancellationToken);

    return result.Text;
}

// ❌ WRONG: App-only authentication returns 403 Access Denied
// using var fileStream = await _speFileStore.DownloadFileAsync(driveId, itemId, ct);
```

**How it works internally**:
```csharp
// DriveItemOperations.cs - DownloadFileAsUserAsync
public async Task<Stream> DownloadFileAsUserAsync(
    HttpContext ctx,
    string driveId,
    string itemId,
    CancellationToken ct = default)
{
    // Uses OBO to exchange user token for Graph token
    var graphClient = await _factory.ForUserAsync(ctx, ct);

    return await graphClient.Drives[driveId]
        .Items[itemId]
        .Content
        .GetAsync(cancellationToken: ct) ?? Stream.Null;
}
```

**HttpContext propagation**: All analysis endpoint methods must propagate `HttpContext` through the call chain:
- `AnalysisEndpoints.MapAnalysisEndpoints()` → passes `HttpContext context`
- `IAnalysisOrchestrationService.ExecuteAnalysisAsync(request, httpContext, ct)`
- `IAnalysisOrchestrationService.ContinueAnalysisAsync(id, message, httpContext, ct)`
- `IAnalysisOrchestrationService.ResumeAnalysisAsync(id, request, httpContext, ct)`

**Why OBO is required for AI Analysis**:
1. SPE containers have user-level permissions, not app-level permissions
2. The app registration (`1e40baad-...`) doesn't have container owner permissions
3. Users access files through their Dataverse user identity, which maps to SPE permissions
4. OBO preserves the user's identity when the BFF downloads files for analysis

---

## Pattern 5: OBO for Dataverse Authorization Checks

**When**: AI analysis service needs to verify user has Read access to documents in Dataverse

**Critical**: FullUAC authorization mode validates user permissions by executing Dataverse queries **as the user**, not as the service principal. This requires OBO token exchange for Dataverse.

### The Problem: Service Principal vs User Permissions

```
❌ Service Principal (App-Only):
   - Uses ClientSecret authentication
   - Executes as Application User with System Administrator role
   - ALWAYS sees ALL documents (bypasses row-level security)
   - Cannot validate user's actual permissions

✅ OBO (Delegated User):
   - Exchanges user's BFF token for Dataverse token
   - Executes as the actual user
   - Row-level security ENFORCED
   - Queries fail with 403 if user lacks access
```

### OBO Flow for Dataverse Authorization

**Code Location**: `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`

```csharp
public async Task<AuthorizationResult> GetUserAccessAsync(
    ClaimsPrincipal user,
    IReadOnlyList<Guid> resourceIds,
    string? userAccessToken,  // User's BFF token
    CancellationToken ct = default)
{
    if (!string.IsNullOrEmpty(userAccessToken))
    {
        // Step 1: Exchange user BFF token for Dataverse token via MSAL OBO
        var dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

        // Step 2: CRITICAL - Set OBO token on HttpClient for all subsequent requests
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", dataverseToken);

        // Step 3: Lookup Dataverse user by Azure AD OID
        var userOid = user.FindFirst("oid")?.Value;
        var systemUserId = await LookupDataverseUserIdAsync(userOid, ct);

        // Step 4: Check document access using direct query pattern
        foreach (var resourceId in resourceIds)
        {
            var permissions = await QueryUserPermissionsAsync(
                systemUserId,
                resourceId.ToString(),
                dataverseToken,
                ct);

            // If query succeeds → User has Read access
            // If query fails (403/404) → User doesn't have access
        }
    }
}
```

### MSAL OBO Exchange Implementation

```csharp
private async Task<string> GetDataverseTokenViaOBOAsync(
    string userAccessToken,
    CancellationToken ct)
{
    // Build confidential client application
    var app = ConfidentialClientApplicationBuilder
        .Create(_dataverseOptions.ClientId)  // 1e40baad-e065-4aea-a8d4-4b7ab273458c
        .WithClientSecret(_dataverseOptions.ClientSecret)  // l8b8Q~J...
        .WithAuthority(_dataverseOptions.Authority)  // https://login.microsoftonline.com/{tenant}
        .Build();

    // Acquire Dataverse token on behalf of user
    var result = await app.AcquireTokenOnBehalfOf(
        scopes: new[] { $"{_dataverseOptions.ServiceUrl}/.default" },
        userAssertion: new UserAssertion(userAccessToken))
        .ExecuteAsync(ct);

    return result.AccessToken;
}
```

### Critical Bug #1: Missing HttpClient Authorization Header

**Symptom**:
```
[UAC-DIAG] Failed to lookup Dataverse user: Unauthorized
```

**Root Cause**: OBO token was obtained but never set on HttpClient headers, so subsequent API calls used the old service principal token (or no token).

**Fix**:
```csharp
// BEFORE (Bug):
var dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);
// HttpClient still has old token!
var response = await _httpClient.GetAsync(url);  // Returns 401

// AFTER (Fixed):
var dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);
_httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", dataverseToken);  // ✅ Set OBO token
var response = await _httpClient.GetAsync(url);  // Now works!
```

### Critical Bug #2: RetrievePrincipalAccess Not Available with OBO Tokens

**Symptom** (after fix #1):
```
[UAC-DIAG] RetrievePrincipalAccess FAILED: StatusCode=NotFound
Resource not found for the segment 'RetrievePrincipalAccess'
Error code: 0x80060888
```

**Root Cause**: The `RetrievePrincipalAccess` Dataverse action doesn't work with OBO (delegated) tokens, only with application tokens.

**Fix: Direct Query Authorization Pattern**

Instead of calling `RetrievePrincipalAccess`, query the document directly:

```csharp
// BEFORE (Doesn't work with OBO):
// POST /api/data/v9.2/RetrievePrincipalAccess
// Body: { Target: {...}, Principal: {...} }
// Result: 404 Not Found

// AFTER (Works with OBO):
private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
    string userId,
    string resourceId,
    string dataverseToken,
    CancellationToken ct)
{
    // Query document directly using OBO token
    var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

    using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
    {
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
    };

    var response = await _httpClient.SendAsync(requestMessage, ct);

    if (response.IsSuccessStatusCode)
    {
        // Success → User has Read access (Dataverse enforced row-level security)
        return new List<PermissionRecord>
        {
            new PermissionRecord(userId, resourceId, AccessRights.Read)
        };
    }
    else
    {
        // 403 or 404 → User doesn't have access
        return new List<PermissionRecord>();
    }
}
```

**Benefits of Direct Query Pattern**:
- ✅ Works with OBO (delegated) tokens
- ✅ Leverages Dataverse's native row-level security
- ✅ Simpler implementation (no complex payload)
- ✅ Standard HTTP status codes (easier to debug)

**Trade-off**: Can only detect Read access (not Write, Delete, etc.) - This is acceptable for AI analysis which only needs Read.

### Complete Authorization Flow

```
1. User clicks "AI Summary" in PCF
   ↓
2. PCF acquires token (MSAL.js)
   Scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
   ↓
3. PCF calls POST /api/ai/analysis/execute
   Headers: Authorization: Bearer {user_bff_token}
   ↓
4. AnalysisAuthorizationFilter extracts bearer token
   TokenHelper.ExtractBearerToken(httpContext)
   ↓
5. AiAuthorizationService orchestrates authorization
   authService.AuthorizeAsync(user, documentIds, httpContext, ct)
   ↓
6. DataverseAccessDataSource performs OBO exchange
   a. MSAL: User BFF token → Dataverse token
   b. Set OBO token on HttpClient.DefaultRequestHeaders.Authorization
   ↓
7. Lookup Dataverse user
   GET /systemusers?$filter=azureactivedirectoryobjectid eq '{oid}'
   (Uses OBO token)
   ↓
8. Check document access
   GET /sprk_documents({id})?$select=sprk_documentid
   (Uses OBO token, enforces user's permissions)
   ↓
9. If GET succeeds (200 OK) → User has access
   If GET fails (403/404) → User doesn't have access
   ↓
10. Return AuthorizationResult to endpoint
    Proceed with AI analysis or return 403
```

### Configuration Requirements

**BFF API App Registration** (`1e40baad-e065-4aea-a8d4-4b7ab273458c`):

```
API Permissions (Delegated):
  ✅ Dynamics CRM → user_impersonation

Exposed API:
  ✅ api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation

Known Client Applications:
  ✅ ["5175798e-f23e-41c3-b09b-7a90b9218189"]  (PCF app)

Client Secret:
  ✅ Created: 2025-12-18
  ✅ Expires: 2027-12-18
  ✅ Config key: API_CLIENT_SECRET
```

### Debugging OBO Authorization

**Application Insights Query**:
```kusto
traces
| where message contains "UAC-DIAG"
| where timestamp > ago(1h)
| project timestamp, message
| order by timestamp desc
```

**Expected log sequence** (successful OBO):
```
[UAC-DIAG] Using OBO authentication for user context
[UAC-DIAG] Set OBO token on HttpClient authorization header
[UAC-DIAG] Dataverse user lookup successful: {systemuserid}
[UAC-DIAG] Direct query GRANTED for user {userId} on document {documentId}
```

**Common OBO Errors**:

| Error | Cause | Solution |
|-------|-------|----------|
| `AADSTS65001: The user or administrator has not consented` | Missing `user_impersonation` permission | Add Dynamics CRM delegated permission to BFF app |
| `401 Unauthorized` after OBO exchange | Token not set on HttpClient | Ensure `_httpClient.DefaultRequestHeaders.Authorization` is set with OBO token |
| `404 RetrievePrincipalAccess not found` | Using wrong API with OBO token | Use direct query pattern (`GET /sprk_documents({id})`) instead |

### Code Changes for OBO Authorization

**1. IAiAuthorizationService Interface** - Add HttpContext parameter:
```csharp
Task<AuthorizationResult> AuthorizeAsync(
    ClaimsPrincipal user,
    IReadOnlyList<Guid> documentIds,
    HttpContext httpContext,  // NEW: Required for OBO token extraction
    CancellationToken cancellationToken = default);
```

**2. AnalysisAuthorizationFilter** - Pass HttpContext:
```csharp
var result = await _authorizationService.AuthorizeAsync(
    user,
    documentIds,
    context.HttpContext,  // NEW: Pass HttpContext
    context.HttpContext.RequestAborted);
```

**3. AiAuthorizationService** - Extract user token:
```csharp
string? userAccessToken = TokenHelper.ExtractBearerToken(httpContext);

var result = await _accessDataSource.GetUserAccessAsync(
    user,
    documentIds,
    userAccessToken,  // NEW: Pass to data source for OBO
    cancellationToken);
```

### Related Documentation

- **Azure Resources**: `docs/architecture/auth-azure-resources.md` (OBO Token Exchange section)
- **Architecture Changes**: `projects/ai-summary-and-analysis-enhancements/ARCHITECTURE-CHANGES.md` (section 3)
- **BFF API Patterns**: `docs/architecture/sdap-bff-api-patterns.md` (Authorization service patterns)

---

## Pattern 6: App-Only Auth for Background Email Processing

**When**: Email webhook triggers or background job handlers need to upload files to SPE and update Dataverse **without user context**.

**Full Documentation**: See [email-to-document-automation.md](email-to-document-automation.md) for complete architecture.

**Critical**: This pattern is **separate from OBO** because:
1. Webhooks arrive from Dataverse with no user context
2. Background job handlers run asynchronously without `HttpContext`
3. The application acts on its own behalf, not on behalf of a user

### App-Only vs OBO Authentication

```
┌─────────────────────────────────────────────────────────────────────────┐
│  User-Initiated (OBO)              Background/Webhook (App-Only)        │
│  ─────────────────────             ─────────────────────────────        │
│  AnalysisOrchestrationService      EmailToDocumentJobHandler            │
│  - PCF triggers                    - Dataverse webhook triggers         │
│  - User context (OBO)              - No user context                    │
│  - HttpContext required            - Uses ClientCredentials             │
│  - SPE: User permissions apply     - SPE: App registration permissions  │
│                                                                          │
│  ✅ Use for: AI analysis           ✅ Use for: Email archival           │
│  ✅ Use for: Document preview      ✅ Use for: Bulk document uploads    │
│  ✅ Use for: User file operations  ❌ NOT for: AI analysis (needs OBO)  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Complete App-Only Authentication Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  Email Activity Created in Dataverse                                         │
│  (via Server-Side Sync or Outgoing email)                                   │
└─────────────────────────────────────────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        ↓                       ↓
┌───────────────────────┐ ┌───────────────────────────────────────────────────┐
│ Webhook Path          │ │ Polling Backup Path                               │
│ ────────────────────  │ │ ────────────────────────────────────────────────  │
│ POST /api/v1/emails/  │ │ EmailPollingBackupService (BackgroundService)     │
│   webhook-trigger     │ │ Queries: emails where processingstatus = null     │
│ AllowAnonymous +      │ │ Interval: configurable (default 5 min)            │
│   Signature validation│ │                                                   │
└───────────────────────┘ └───────────────────────────────────────────────────┘
                    │                       │
                    └───────────┬───────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  ServiceBusJobProcessor → EmailToDocumentJobHandler                          │
│  ───────────────────────────────────────────────────                        │
│  Job Type: "ProcessEmailToDocument"                                         │
│  Idempotency Key: "Email:{emailId}:Archive"                                 │
└─────────────────────────────────────────────────────────────────────────────┘
                                │
                                ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  ClientSecretCredential (Azure.Identity)                                    │
│  ─────────────────────────────────────────                                  │
│  Configuration:                                                             │
│    TENANT_ID:       {azure-ad-tenant-id}                                    │
│    API_APP_ID:      1e40baad-e065-4aea-a8d4-4b7ab273458c                    │
│    API_CLIENT_SECRET: {secret from Key Vault}                               │
└─────────────────────────────────────────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        ↓                       ↓
┌───────────────────────┐ ┌───────────────────────────────────────────────────┐
│  Dataverse Scope      │ │  Microsoft Graph Scope                            │
│  ────────────────     │ │  ───────────────────                              │
│  {orgUrl}/.default    │ │  https://graph.microsoft.com/.default             │
│                       │ │                                                   │
│  Operations:          │ │  Operations:                                      │
│  • Fetch email data   │ │  • Resolve container → drive ID                   │
│  • Fetch attachments  │ │  • Upload .eml file to SPE                        │
│  • Create/Update      │ │  • Upload attachments to SPE                      │
│    sprk_document      │ │  • Get file handle (ID, WebUrl)                   │
└───────────────────────┘ └───────────────────────────────────────────────────┘
```

### Token Management Implementation

**File**: `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs`

```csharp
// Token caching and refresh (lines 492-518)
private async Task EnsureAuthenticatedAsync(CancellationToken ct)
{
    // Check if current token is valid (with 5-minute buffer before expiry)
    if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
        // Build scope for Dataverse
        var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";

        // Acquire new token using client credentials
        _currentToken = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            ct);

        // Set on HttpClient for subsequent requests
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

        _logger.LogDebug("Refreshed Dataverse access token for EmailToEmlConverter");
    }
}
```

**Key Points**:
- Token cached in `_currentToken` field (per-instance)
- Refresh triggered 5 minutes before expiry
- Single responsibility: Each service manages its own token lifecycle
- No shared token cache between services (stateless design)

### App-Only SPE File Upload Pattern

```csharp
// EmailToDocumentJobHandler.cs
public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
{
    // No HttpContext available - use app-only SpeFileStore methods

    // 1. Resolve container ID to Drive ID (app-only)
    var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct);

    // 2. Upload file using app-only credentials
    var fileHandle = await _speFileStore.UploadSmallAsync(
        driveId,
        uploadPath,
        emlStream,
        ct);

    // 3. fileHandle contains WebUrl for sprk_filepath field
    var updateRequest = new UpdateDocumentRequest
    {
        GraphDriveId = driveId,
        GraphItemId = fileHandle.Id,
        FilePath = fileHandle.WebUrl  // SharePoint URL for links
    };
}
```

### SpeFileStore App-Only Methods

| Method | Auth Type | Use Case |
|--------|-----------|----------|
| `ResolveDriveIdAsync(containerId)` | App-only | Convert container GUID to Drive ID |
| `UploadSmallAsync(driveId, path, stream)` | App-only | Upload files <4MB |
| `UploadLargeAsync(driveId, path, stream, size)` | App-only | Upload files ≥4MB |
| `DownloadFileAsUserAsync(httpContext, ...)` | OBO | User-initiated downloads |

### DefaultContainerId Configuration

**Critical**: The `DefaultContainerId` must be in **Drive ID format** (`b!xxx`), not a raw GUID.

```json
// appsettings.json
{
  "EmailProcessing": {
    "DefaultContainerId": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
  }
}
```

**Why Drive ID format?** The Graph API expects Drive ID for SPE operations. The `b!` prefix indicates a base64-encoded SharePoint container ID.

### App Registration Requirements for App-Only

The BFF API app registration (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) must have:

**API Permissions (Application - not Delegated)**:
- Microsoft Graph: `Files.ReadWrite.All` (Application)
- Microsoft Graph: `Sites.ReadWrite.All` (Application)

**SPE Container Permissions**:
- App must be registered as a container owner/contributor in SharePoint Embedded
- This is separate from Azure AD permissions

### When to Use Each Pattern

| Scenario | Pattern | Reason |
|----------|---------|--------|
| AI Analysis (Document Profile) | OBO | Needs user's SPE permissions |
| AI Analysis (Playbooks) | OBO | File downloads need user context |
| PCF file upload | OBO | User is uploading their files |
| Email webhook processing | App-Only | No user context available |
| Background job file upload | App-Only | No HttpContext in job handlers |
| Bulk document import | App-Only | System operation, no user |

### Future: AppOnlyAnalysisService

For AI analysis of email documents (where no user context exists), a separate `AppOnlyAnalysisService` is planned:

```
AnalysisOrchestrationService     AppOnlyAnalysisService (planned)
├─ OBO authentication            ├─ App-only authentication
├─ PCF-triggered analysis        ├─ Email/webhook-triggered analysis
├─ User context required         ├─ No user context needed
│                                │
└─── Shared Components ──────────┘
     - OpenAiClient
     - TextExtractorService (limited: no .eml native support)
     - IDataverseService
```

**Note**: Azure Document Intelligence does NOT natively process `.eml` files. Email analysis requires extracting attachments separately.

---

## Pattern 7: Code Page Embedded Auth via MSAL ssoSilent

**When**: A React Code Page (HTML web resource) needs to call the BFF API and may be loaded either via `Xrm.Navigation.navigateTo` (dialog mode) or embedded directly on a Dataverse form (iframe mode).

**Problem**: `Xrm.Utility.getGlobalContext().getAccessToken()` only works when the page is opened via `navigateTo`. When the same HTML web resource is embedded as an iframe on a form, the Xrm token APIs are not available.

**Solution**: Dual-strategy token acquisition — try Xrm platform strategies first (fast, no MSAL overhead), then fall back to MSAL `ssoSilent()` for embedded scenarios.

### Why ssoSilent Works

The user is already authenticated in Dataverse via Azure AD. The browser holds a valid session cookie for `login.microsoftonline.com`. MSAL's `ssoSilent()` opens a hidden iframe to the Azure AD authorize endpoint and uses the existing session to obtain a token — no user interaction required.

> **Note**: PCF controls use a different client ID (`5175798e-f23e-41c3-b09b-7a90b9218189`) — see Pattern 1. Both registrations are listed in `knownClientApplications` on the BFF API app. See `docs/architecture/auth-azure-resources.md` "Why Two Client App Registrations?" for the historical reason.

### Token Acquisition Priority

```
┌─────────────────────────────────────────────────────────────────┐
│  1. In-memory cache (fastest — no I/O)                         │
│     Return cached token if not within 5-min expiry buffer      │
└────────────────────────────────────┬────────────────────────────┘
                                     │ cache miss
                                     ↓
┌─────────────────────────────────────────────────────────────────┐
│  2. Xrm platform strategies (navigateTo mode)                  │
│     a. getGlobalContext().getAccessToken()  (modern 2024+)     │
│     b. __crmTokenProvider.getToken()       (legacy global)     │
│     c. AUTHENTICATION_TOKEN                (some configs)      │
│     d. Xrm.Page.context.getAuthToken()     (deprecated)       │
│     e. __SPAARKE_BFF_TOKEN__               (PCF bridge token)  │
└────────────────────────────────────┬────────────────────────────┘
                                     │ all fail (embedded mode)
                                     ↓
┌─────────────────────────────────────────────────────────────────┐
│  3. MSAL ssoSilent (embedded web resource fallback)            │
│     a. acquireTokenSilent (cached account + refresh token)     │
│     b. ssoSilent (Azure AD session cookie via hidden iframe)   │
│     Scope: api://1e40baad-.../user_impersonation               │
└─────────────────────────────────────────────────────────────────┘
```

### Code Implementation

**MSAL Configuration**: `src/client/code-pages/AnalysisWorkspace/src/config/msalConfig.ts`

```typescript
export const msalConfig: Configuration = {
    auth: {
        clientId: CLIENT_ID,  // 170c98e1-d486-4355-bcbe-170454e0207c (DSM-SPE Dev 2)
        authority: "https://login.microsoftonline.com/organizations",  // multi-tenant
        redirectUri: window.location.origin,  // auto-detect from environment
        navigateToLoginRequestUrl: false,
    },
    cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false,
    },
};

export const BFF_API_SCOPE =
    "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation";
```

> **Authority best practice**: Use `https://login.microsoftonline.com/organizations` for Code Pages to ensure environment portability (works across Dataverse environments without code changes). Avoid `common` (allows personal Microsoft accounts). Older code pages (SemanticSearch, DocumentRelationshipViewer) currently hardcode the tenant ID (`https://login.microsoftonline.com/{tenantId}/v2.0`) -- this is a known tech debt item that will be resolved when migrating to the planned `@spaarke/auth` shared package.

**Auth Service**: `src/client/code-pages/AnalysisWorkspace/src/services/authService.ts`

```typescript
// Lazy MSAL initialization (avoid blocking initial render)
async function ensureMsalInitialized(): Promise<PublicClientApplication | null> {
    if (_msalInstance) return _msalInstance;
    const instance = new PublicClientApplication(msalConfig);
    await instance.initialize();
    await instance.handleRedirectPromise();
    _msalInstance = instance;
    return _msalInstance;
}

// MSAL token acquisition (embedded fallback)
async function acquireTokenViaMsal(): Promise<TokenCache | null> {
    const msal = await ensureMsalInitialized();
    if (!msal) return null;

    // Try cached account first
    const accounts = msal.getAllAccounts();
    if (accounts.length > 0) {
        const result = await msal.acquireTokenSilent({
            scopes: [BFF_API_SCOPE],
            account: accounts[0],
        });
        if (result?.accessToken) return { token: result.accessToken, expiresAt: ... };
    }

    // Fall back to ssoSilent (Azure AD session cookie)
    const ssoResult = await msal.ssoSilent({ scopes: [BFF_API_SCOPE] });
    if (ssoResult?.accessToken) return { token: ssoResult.accessToken, expiresAt: ... };

    return null;
}

// Combined: Xrm first → MSAL fallback
async function acquireToken(): Promise<TokenCache> {
    const platformToken = await extractPlatformToken();
    if (platformToken) return platformToken;

    const msalToken = await acquireTokenViaMsal();
    if (msalToken) return msalToken;

    throw new AuthError("Could not acquire BFF API token");
}
```

### Code Page Auth Matrix

| Code Page | Client ID | Authority | Primary Strategy | Bridge Support | Auth Lines |
|-----------|-----------|-----------|-----------------|----------------|------------|
| LegalWorkspace | `170c98e1` | `organizations` | Bridge → MSAL ssoSilent | Yes | 268 |
| AnalysisWorkspace | `170c98e1` | `organizations` | Xrm platform → MSAL ssoSilent | Yes | 532 |
| PlaybookBuilder | `170c98e1` | `organizations` | Xrm platform → MSAL ssoSilent | Yes | 331 |
| SprkChatPane | `170c98e1` | `organizations` | Xrm platform → MSAL ssoSilent | Yes | 353 |
| SemanticSearch | `170c98e1` | **Hardcoded tenant** | MSAL acquireTokenSilent → popup | No | 235 |
| DocumentRelationshipViewer | `170c98e1` | **Hardcoded tenant** | MSAL acquireTokenSilent → popup | No | 213 |

> **Note**: SemanticSearch and DocumentRelationshipViewer have known tech debt: hardcoded authority and no bridge support. To be resolved in `@spaarke/auth` shared package.

### Reference Implementation

The MSAL ssoSilent pattern originates from:
- **LegalWorkspace**: `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts`
- **AnalysisWorkspace**: `src/client/code-pages/AnalysisWorkspace/src/services/authService.ts`

Both share identical MSAL config (CLIENT_ID, BFF_API_SCOPE, authority). A future shared package (`@spaarke/auth`) should consolidate this.

### App Registration Requirements

**DSM-SPE Dev 2** (`170c98e1-d486-4355-bcbe-170454e0207c`):
```
Platform: Single-page application (SPA)
Redirect URIs:
  - https://spaarkedev1.crm.dynamics.com  (dev Dataverse origin)
  - Additional Dataverse environment origins as needed

API Permissions (Delegated):
  - BFF API: user_impersonation (api://1e40baad-.../user_impersonation)

Known Client Applications (on BFF app):
  - 170c98e1-d486-4355-bcbe-170454e0207c  (DSM-SPE Dev 2)
```

### When to Use This Pattern

| Scenario | Pattern |
|----------|---------|
| Code Page opened via navigateTo | Xrm strategies work → MSAL not needed (but loaded as fallback) |
| Code Page embedded as iframe on form | Xrm strategies fail → MSAL ssoSilent acquires token |
| PCF control on form | Use Pattern 1 (MSAL.js in PCF — platform React context) |
| Server-to-server API calls | Use Pattern 2/3 (OBO or ClientSecret) |

### Debugging

```
// Browser console — successful navigateTo mode:
[AnalysisWorkspace:AuthService] Dataverse org: https://spaarkedev1.crm.dynamics.com
[AnalysisWorkspace:AuthService] Token acquired via Xrm platform strategy

// Browser console — successful embedded mode:
[AnalysisWorkspace:AuthService] Xrm SDK not available, will use MSAL ssoSilent
[AnalysisWorkspace:AuthService] MSAL initialized successfully
[AnalysisWorkspace:AuthService] Token acquired via MSAL ssoSilent
```

---

## Pattern 8: Parent-to-Child Token Bridge

> **Added**: March 2026
> **Status**: Production (LegalWorkspace, AnalysisWorkspace)
> **Use case**: Parent page passes its BFF token to a child code page opened as iframe dialog

### When to Use

When one authenticated page (PCF control, code page, or custom page) opens another code page as a child iframe/dialog via `Xrm.Navigation.navigateTo`. Instead of the child doing its own MSAL initialization (~500-1300ms), the parent shares its already-acquired token.

### Flow

```
Parent (already authenticated)
  1. Acquires BFF token via MSAL or Xrm platform
  2. Sets: window.__SPAARKE_BFF_TOKEN__ = currentToken
  3. Opens child via Xrm.Navigation.navigateTo({ pageType: 'webresource', ... })

Child (iframe code page)
  Token acquisition priority:
  1. Check window.__SPAARKE_BFF_TOKEN__         → not found (own frame)
  2. Check window.parent.__SPAARKE_BFF_TOKEN__  → FOUND! (~0.1ms)
  3. (skipped) MSAL init + ssoSilent            → would be ~500-1300ms
```

### Performance Impact

| Scenario | Token Acquisition Time |
|----------|----------------------|
| No bridge (MSAL ssoSilent) | ~500-1300ms |
| With bridge (parent token) | ~0.1ms |

### Security Considerations

- Token lifetime: Child inherits parent's token expiry. If parent refreshes, child still has old token.
- Same-origin only: `window.parent` access requires same origin (both in Dataverse domain).
- No refresh mechanism: Child should fall back to MSAL ssoSilent if bridge token is expired.
- Cleanup: Parent should clear `__SPAARKE_BFF_TOKEN__` on page unload.

### Bridge Variables

| Variable | Purpose | Set By |
|----------|---------|--------|
| `window.__SPAARKE_BFF_TOKEN__` | BFF API Bearer token | Parent page |
| `window.__SPAARKE_BFF_BASE_URL__` | BFF API base URL | Parent page (optional) |
| `window.__SPAARKE_MSAL_CLIENT_ID__` | MSAL client ID override | Parent page (optional) |

### Current Implementations

- **LegalWorkspace** (`bffAuthProvider.ts`): Reads bridge as 2nd priority (after in-memory cache)
- **AnalysisWorkspace** (`authService.ts`): Reads bridge as part of Xrm platform strategies
- **PlaybookBuilder** (`authService.ts`): Same as AnalysisWorkspace
- **SprkChatPane** (`authService.ts`): Same as AnalysisWorkspace

### Not Yet Implemented

- SemanticSearch: Does NOT check parent bridge (always does full MSAL init)
- DocumentRelationshipViewer: Does NOT check parent bridge
- These will be fixed when migrating to `@spaarke/auth`

---

## Pattern 9: Standalone Wizard Code Page Auth (resolveRuntimeConfig + initAuth)

> **Added**: March 2026
> **Status**: Production (all six wizard code pages under `src/solutions/`)
> **Use case**: Wizard code pages (opened as standalone dialogs via `Xrm.Navigation.navigateTo`) that call the BFF API directly — e.g., AI pre-fill, file summarization

### When to Use

When a code page opened as a **standalone Dataverse dialog** needs to call the BFF API. These pages:
- Are opened via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... })`
- Do NOT inherit auth context from the calling page
- Cannot use Pattern 7 (Xrm platform strategies) because they are standalone — no parent form with active Xrm token

### Why `fetch.bind(window)` Returns 401

Wizard code pages are standalone web resources. Without explicit MSAL initialization, there is no bearer token. `fetch.bind(window)` sends requests without an `Authorization` header and the BFF returns `401 Unauthorized`.

### Flow

```
Wizard Code Page Opens (navigateTo dialog)
    │
    1. resolveRuntimeConfig()
    │   ├─ Reads Dataverse env vars via session cookie (no MSAL needed yet)
    │   │   • sprk_BffApiBaseUrl  → bffBaseUrl
    │   │   • sprk_BffApiAppId    → bffOAuthScope (api://{id}/user_impersonation)
    │   │   • sprk_MsalClientId   → msalClientId
    │   └─ Returns: { bffBaseUrl, bffOAuthScope, msalClientId, tenantId }
    │
    2. initAuth({ clientId, bffBaseUrl, bffApiScope, tenantId })
    │   └─ Initializes MSAL PublicClientApplication with ssoSilent
    │
    3. Render wizard — pass authenticatedFetch from @spaarke/auth
    │   └─ authenticatedFetch adds Authorization: Bearer {token} to all BFF requests
    │
    4. Wizard component calls BFF:
        POST /api/workspace/matters/pre-fill  → 200 OK (pre-fill fields)
        POST /workspace/files/summarize       → 200 OK (SSE stream)
```

### Entry Point Implementation (main.tsx)

```tsx
import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from "@spaarke/auth";
import { MyWizardDialog } from "@spaarke/ui-components";

const params = new URLSearchParams(window.location.search);
const isDark = window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
const theme = isDark ? webDarkTheme : webLightTheme;

function App() {
    const [isAuthReady, setIsAuthReady] = React.useState(false);
    const [resolvedBffBaseUrl, setResolvedBffBaseUrl] = React.useState<string>("");

    React.useEffect(() => {
        let cancelled = false;
        async function initializeAuth(): Promise<void> {
            try {
                const config = await resolveRuntimeConfig();
                await initAuth({
                    clientId: config.msalClientId,
                    bffBaseUrl: config.bffBaseUrl,
                    bffApiScope: config.bffOAuthScope,
                    tenantId: config.tenantId || undefined,
                    proactiveRefresh: true,
                });
                if (!cancelled) {
                    setResolvedBffBaseUrl(config.bffBaseUrl);
                    setIsAuthReady(true);
                }
            } catch (err) {
                console.error("[MyWizard] Failed to initialize auth:", err);
                if (!cancelled) setIsAuthReady(true); // render anyway
            }
        }
        void initializeAuth();
        return () => { cancelled = true; };
    }, []);

    if (!isAuthReady) {
        return (
            <FluentProvider theme={theme} style={{ height: "100%" }}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
                    <span>Initializing...</span>
                </div>
            </FluentProvider>
        );
    }

    return (
        <FluentProvider theme={theme} style={{ height: "100%" }}>
            <MyWizardDialog
                authenticatedFetch={authenticatedFetch}   // ← from @spaarke/auth
                bffBaseUrl={resolvedBffBaseUrl}
            />
        </FluentProvider>
    );
}

createRoot(document.getElementById("root")!).render(<App />);
```

### vite.config.ts Requirements

The `@spaarke/auth` library is aliased to source. Its bare module imports (e.g., `@azure/msal-browser`) must resolve from the solution's own `node_modules`. Use the `resolveSharedLibDeps()` plugin:

```typescript
// vite.config.ts — required when aliasing @spaarke/auth to source
resolve: {
    alias: {
        "@spaarke/auth": path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
        "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
        // ...
    }
}
// Plus: resolveSharedLibDeps() plugin in plugins array
// Redirects bare imports from shared lib source to solution's node_modules
```

Also add `@azure/msal-browser` to the solution's `package.json` `dependencies`.

### resolveRuntimeConfig() Details

| Step | Description |
|------|-------------|
| 1 | Resolve org URL from `Xrm.Utility.getGlobalContext().getClientUrl()` (walks frame hierarchy) |
| 2 | Fetch `environmentvariabledefinitions` via Dataverse REST API (session cookie auth) |
| 3 | Fetch `environmentvariablevalues` for override values |
| 4 | Merge: override value takes precedence over default value |
| 5 | Build `IRuntimeConfig` with normalized URLs |
| 6 | Cache in memory (5 min) + localStorage (60 min) |

**Pre-auth bootstrap**: The Dataverse REST API is accessible without a bearer token in a web resource context — the browser session cookie authenticates the request. This is why `resolveRuntimeConfig()` can run before MSAL is initialized.

### Canonical Implementations

| Wizard | File | Notes |
|--------|------|-------|
| SummarizeFilesWizard | `src/solutions/SummarizeFilesWizard/src/main.tsx` | No uploadService |
| CreateMatterWizard | `src/solutions/CreateMatterWizard/src/main.tsx` | Has uploadService (memoized on resolvedBffBaseUrl) |
| CreateProjectWizard | `src/solutions/CreateProjectWizard/src/main.tsx` | Has uploadService |
| CreateEventWizard | `src/solutions/CreateEventWizard/src/main.tsx` | Has uploadService |
| CreateTodoWizard | `src/solutions/CreateTodoWizard/src/main.tsx` | No uploadService |
| CreateWorkAssignmentWizard | `src/solutions/CreateWorkAssignmentWizard/src/main.tsx` | No uploadService |

### Common Mistakes

| Mistake | Result | Fix |
|---------|--------|-----|
| `authenticatedFetch={fetch.bind(window)}` | 401 on all BFF calls | Use `authenticatedFetch` from `@spaarke/auth` |
| Missing `@azure/msal-browser` in `package.json` | Build error or silent MSAL failure | Add to `dependencies` |
| Missing `resolveSharedLibDeps()` plugin in vite.config.ts | Module not found for `@azure/msal-browser` | Add the Vite plugin (copy from `SummarizeFilesWizard/vite.config.ts`) |
| Services created before `resolvedBffBaseUrl` set | Wrong empty URL passed to uploadService | Use `useMemo` with `[resolvedBffBaseUrl]` dependency |
| No loading state | Wizard renders before auth ready | Gate with `isAuthReady` state |
| Relying on `resolveRuntimeConfig().tenantId` for downstream sync calls | Empty string on first load — `Xrm.organizationSettings.tenantId` is uninitialized at bootstrap | After `initAuth()`, call `await getAuthProvider().getTenantId()` and patch the stored config (see LegalWorkspace `main.tsx`) |
| Calling `resolveTenantIdSync()` before `initAuth()` completes | Returns empty — MSAL accounts not yet populated | Ensure `initAuth()` is `await`-ed before the component that calls `resolveTenantIdSync()` renders |

### Tenant ID at Bootstrap — The Async Race

`resolveRuntimeConfig()` reads `Xrm.organizationSettings.tenantId` from the Xrm global context. On **first page load**, Dataverse initializes this property asynchronously — it is reliably empty until about 1–3 seconds after the web resource starts. This means:

```
First load: resolveRuntimeConfig().tenantId === ''   ← empty at bootstrap time
Refresh:    resolveRuntimeConfig().tenantId === 'abc…' ← Xrm initialized by reload
```

**How to get a reliable tenant ID in bootstrap:**

```typescript
// 1. Resolve config (may return empty tenantId on first load)
const config = await resolveRuntimeConfig();
setRuntimeConfig(config);

// 2. Initialize auth
await initAuth({ clientId: config.msalClientId, bffApiScope: config.bffOAuthScope, bffBaseUrl: config.bffBaseUrl });

// 3. Patch empty tenantId from MSAL accounts (JWT-sourced — always correct post-auth)
if (!config.tenantId) {
  const tenantId = await getAuthProvider().getTenantId(); // reads accounts[0].tenantId
  if (tenantId) setRuntimeConfig({ ...config, tenantId });
}
```

**Why `getAuthProvider().getTenantId()` is reliable**: After `initAuth()` completes, MSAL has performed a silent token acquisition and `msal.getAllAccounts()[0].tenantId` contains the real tenant GUID extracted from the access token JWT. This is authoritative regardless of Xrm initialization state.

See `src/solutions/LegalWorkspace/src/main.tsx` for the canonical implementation.

### resolveTenantIdSync() — Resolution Order (for click handlers)

`resolveTenantIdSync()` is safe to call from synchronous click handlers. It resolves in this order:

1. **MSAL authority URL** — if `initAuth()` was called with a tenant-specific authority (e.g. `https://login.microsoftonline.com/{tenantId}`). Filtered: `organizations`, `common`.
2. **MSAL accounts[0].tenantId** — populated after `initAuth()` completes silent auth (`getCachedTenantId()` on `SpaarkeAuthProvider`). The JWT-sourced tenant ID — always correct.
3. **Xrm.organizationSettings.tenantId** — frame-walk fallback. Reliable once Xrm has fully initialized (works well for PCF controls embedded in forms, less reliable for standalone code page bootstraps).

**Rule**: As long as `initAuth()` has been `await`-ed before the user can trigger a click, step 2 guarantees a correct tenant ID.

---

## Token Scopes Reference

| Token For | Scope | Pattern |
|-----------|-------|---------|
| BFF API (from PCF) | `api://1e40baad-.../user_impersonation` | MSAL.js (Pattern 1) |
| BFF API (from Code Page - navigateTo) | `api://1e40baad-.../user_impersonation` | Xrm platform (Pattern 7) |
| BFF API (from Code Page - embedded) | `api://1e40baad-.../user_impersonation` | MSAL ssoSilent (Pattern 7) |
| BFF API (from standalone wizard code page) | `api://1e40baad-.../user_impersonation` | resolveRuntimeConfig + initAuth (Pattern 9) |
| Graph API (from BFF) | `https://graph.microsoft.com/.default` | OBO (Pattern 2) |
| Graph API for AI Analysis | `https://graph.microsoft.com/.default` | OBO via HttpContext (Pattern 4) |
| Graph API for Email Processing | `https://graph.microsoft.com/.default` | ClientCredentials (Pattern 6) |
| Dataverse (from BFF, metadata queries) | `https://{org}.crm.dynamics.com/.default` | ClientCredentials (Pattern 3) |
| Dataverse (from BFF, authorization checks) | `https://{org}.crm.dynamics.com/.default` | OBO via HttpContext (Pattern 5) |

---

## App Registration Requirements

### PCF App (5175798e-f23e-41c3-b09b-7a90b9218189)

```
Platform: Single-page application (SPA)
Redirect URIs:
  - https://spaarkedev1.crm.dynamics.com
  - http://localhost:8181 (dev)
  
API Permissions (Delegated):
  - Microsoft Graph: User.Read, offline_access
  - BFF API: user_impersonation
  
Admin Consent: Required
```

### BFF API App (1e40baad-e065-4aea-a8d4-4b7ab273458c)

```
Platform: Web
Client Secret: Stored in Key Vault

Expose an API:
  - App ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
  - Scope: user_impersonation

API Permissions (Delegated):
  - Microsoft Graph: Files.ReadWrite.All, Sites.ReadWrite.All, User.Read
  - Dynamics CRM: user_impersonation

Admin Consent: Required for all
```

---

## Dataverse Application User

**Critical**: Without this, ServiceClient auth fails with AADSTS500011.

```
Setup via Power Platform Admin Center:
1. Environments → SPAARKE DEV 1 → Settings
2. Users + permissions → Application users
3. + New app user
4. Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
5. Security Role: System Administrator

Verification:
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
```

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| Wrong scope URI format | AADSTS500011: Resource principal not found | Use full `api://{guid}/...` not friendly name |
| Missing Dynamics CRM permission | AADSTS500011 for Dataverse | Add `user_impersonation` to BFF app |
| No Application User | ServiceClient connection failed | Create via Power Platform Admin Center |
| Using ManagedIdentity for Dataverse | No User Assigned Managed Identity found | Use ClientSecret connection string |
| Expired client secret | 401 on all BFF calls | Rotate secret in Azure AD + Key Vault |
| Using app-only auth for AI file download | 403 Access Denied from SPE | Use `DownloadFileAsUserAsync(httpContext, ...)` with OBO |
| DefaultContainerId as raw GUID | Graph API rejects container ID | Use Drive ID format (`b!xxx`) - see Pattern 6 |
| Wrong Dataverse field type for FileSize | Type mismatch error | `sprk_filesize` is Whole Number (int), cast `(int)` |

---

## Debugging Authentication

### Check PCF Token Acquisition
```javascript
// Browser console
[MsalAuthProvider] Token acquired successfully
// vs
[MsalAuthProvider] Failed to acquire token: {...}
```

### Check BFF API Logs
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2

# Look for:
# - AADSTS errors (Azure AD issues)
# - 401 responses (token validation failed)
# - ServiceClient connection errors (Dataverse auth)
```

### Validate Token Claims
```bash
# Decode JWT at jwt.ms or jwt.io
# Check: aud (audience), scp (scopes), exp (expiration)
```

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture overview
- [dataverse-oauth-authentication.md](dataverse-oauth-authentication.md) - General Dataverse OAuth patterns
- [sdap-troubleshooting.md](sdap-troubleshooting.md) - Common issues including auth failures

---

*Condensed from authentication sections of architecture guide*
