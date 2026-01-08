# SDAP Authentication Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Authentication & Security section)
> **Last Updated**: January 3, 2026
> **Applies To**: Authentication code, token handling, Azure AD configuration

---

## TL;DR

SDAP uses three authentication patterns: (1) MSAL.js in PCF for user tokens, (2) On-Behalf-Of (OBO) flow for Graph API, (3) ClientSecret for server-to-Dataverse. User identity flows through OBO to Graph; app identity used for Dataverse metadata queries.

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
                cacheLocation: 'localStorage'
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

---

## Pattern 2: On-Behalf-Of (OBO) Token Exchange

**When**: BFF API needs to call Graph API on behalf of user (file operations)

```csharp
// GraphClientFactory.cs
public async Task<GraphServiceClient> CreateClientAsync(string userAccessToken)
{
    // Exchange user token for Graph token
    var tokenResponse = await _confidentialClient.AcquireTokenOnBehalfOf(
        scopes: new[] { "https://graph.microsoft.com/.default" },
        userAssertion: new UserAssertion(userAccessToken)
    ).ExecuteAsync();

    return new GraphServiceClient(
        new DelegateAuthenticationProvider(request =>
        {
            request.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
            return Task.CompletedTask;
        }));
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

## Token Scopes Reference

| Token For | Scope | Pattern |
|-----------|-------|---------|
| BFF API (from PCF) | `api://1e40baad-.../user_impersonation` | MSAL.js |
| Graph API (from BFF) | `https://graph.microsoft.com/.default` | OBO |
| Graph API for AI Analysis | `https://graph.microsoft.com/.default` | OBO (via HttpContext) |
| Dataverse (from BFF, metadata queries) | `https://{org}.crm.dynamics.com/.default` | ClientCredentials |
| Dataverse (from BFF, authorization checks) | `https://{org}.crm.dynamics.com/.default` | OBO (via HttpContext) |

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
