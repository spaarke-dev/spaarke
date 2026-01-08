# On-Behalf-Of (OBO) Flow Pattern

> **Domain**: OAuth / Token Exchange
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-004, ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | OBO implementation |
| `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Token caching |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/TokenHelper.cs` | Token extraction |

---

## OBO Flow Overview

```
PCF → [Token A] → BFF API → [OBO Exchange] → [Token B] → Graph API
```

1. **PCF** acquires Token A with scope `api://{bff}/user_impersonation`
2. **BFF** validates Token A (JWT bearer)
3. **BFF** exchanges Token A for Token B via OBO
4. **BFF** calls Graph API with Token B

---

## Implementation Pattern

### 1. Extract Bearer Token

```csharp
// TokenHelper.cs
public static string ExtractBearerToken(HttpContext httpContext)
{
    var authHeader = httpContext.Request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(authHeader))
        throw new UnauthorizedAccessException("Missing Authorization header");

    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        throw new UnauthorizedAccessException("Invalid Authorization header format");

    return authHeader["Bearer ".Length..].Trim();
}
```

### 2. OBO Token Exchange

```csharp
// GraphClientFactory.cs
private async Task<string> AcquireGraphTokenAsync(string userToken)
{
    // Check cache first (see token-caching.md)
    var tokenHash = _tokenCache.ComputeTokenHash(userToken);
    var cached = await _tokenCache.GetTokenAsync(tokenHash);
    if (cached != null) return cached;

    // OBO exchange - MUST use .default scope
    var result = await _cca.AcquireTokenOnBehalfOf(
        scopes: new[] { "https://graph.microsoft.com/.default" },
        userAssertion: new UserAssertion(userToken)
    ).ExecuteAsync();

    // Cache the token (55-minute TTL)
    await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

    return result.AccessToken;
}
```

### 3. ConfidentialClientApplication Setup

```csharp
// GraphClientFactory constructor
_cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientSecret(clientSecret)
    .Build();
```

---

## Error Handling

```csharp
try
{
    var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
    return result.AccessToken;
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    // User token expired or revoked
    _logger.LogWarning("OBO failed: User token invalid");
    throw new UnauthorizedAccessException("Session expired. Please sign in again.");
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS50013")
{
    // Audience mismatch
    _logger.LogError("OBO failed: Token audience mismatch");
    throw new InvalidOperationException("Token configuration error");
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS70011")
{
    // Invalid scope (using individual scopes instead of .default)
    _logger.LogError("OBO failed: Invalid scope format");
    throw new InvalidOperationException("Use .default scope for OBO");
}
catch (MsalServiceException ex)
{
    _logger.LogError(ex, "OBO failed: {ErrorCode}", ex.ErrorCode);
    throw;
}
```

---

## Global Exception Handler

```csharp
// Program.cs exception handler
(int status, string code, string title, string detail) = exception switch
{
    MsalServiceException ms => (
        401,
        "obo_failed",
        "Token Exchange Failed",
        $"Failed to acquire Graph token: {ms.ErrorCode}"
    ),
    // ... other exceptions
};
```

---

## JWT Token Debugging

```csharp
// Introspect incoming token for debugging
var handler = new JwtSecurityTokenHandler();
var jwt = handler.ReadJwtToken(userToken);

_logger.LogInformation("Token claims - aud: {Aud}, iss: {Iss}, appid: {AppId}",
    jwt.Audiences.FirstOrDefault(),
    jwt.Issuer,
    jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value);
```

---

## Usage in Endpoints

```csharp
app.MapGet("/api/obo/containers/{id}/children", async (
    string id,
    HttpContext ctx,
    SpeFileStore speFileStore,
    CancellationToken ct) =>
{
    // SpeFileStore internally calls GraphClientFactory
    // which handles OBO token exchange
    var result = await speFileStore.ListChildrenAsUserAsync(ctx, id, ct);
    return TypedResults.Ok(result);
}).RequireAuthorization();
```

---

## Key Points

1. **Always use `.default` scope** for OBO (not individual permissions)
2. **Cache tokens** with 55-minute TTL (5-minute buffer)
3. **Hash user tokens** before using as cache keys (never store plaintext)
4. **Fail gracefully** on cache errors (continue with fresh OBO)
5. **Include correlation IDs** in error responses

---

## Dataverse OBO Pattern

> **Implementation**: `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`
> **Use Case**: AI document authorization with user-scoped permissions
> **Status**: Production (2026-01-07)

### Flow Overview

```
PCF → [Token A] → BFF API → [OBO Exchange] → [Token B] → Dataverse API
                      │
                      ├─→ Extract bearer token (TokenHelper)
                      ├─→ OBO exchange via MSAL (DataverseAccessDataSource)
                      ├─→ Set token on HttpClient headers (CRITICAL)
                      ├─→ Lookup Dataverse user (systemusers query)
                      └─→ Check document access (direct query)
```

### Key Differences from Graph API Pattern

| Aspect | Graph API OBO | Dataverse OBO |
|--------|---------------|---------------|
| **Scope** | `https://graph.microsoft.com/.default` | `https://{org}.crm.dynamics.com/.default` |
| **Token caching** | ✅ Redis (55-min TTL) | ❌ No caching (future enhancement) |
| **Authorization API** | Native permissions check | ⚠️ Direct query (RetrievePrincipalAccess doesn't support OBO) |
| **HttpClient setup** | Token set per-request | ⚠️ Must set `DefaultRequestHeaders.Authorization` |
| **Usage context** | SPE file operations | AI document authorization |

### Implementation Steps

#### Step 1: Extract User Token from Request

```csharp
// AiAuthorizationService.cs
string? userAccessToken = null;
try
{
    userAccessToken = TokenHelper.ExtractBearerToken(httpContext);
    _logger.LogDebug("[AI-AUTH] Extracted user bearer token for OBO authentication");
}
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex, "[AI-AUTH] Failed to extract bearer token from request");
    return AuthorizationResult.Denied("Missing or invalid authorization token");
}

// Pass to DataverseAccessDataSource
var accessSnapshot = await _accessDataSource.GetUserAccessAsync(
    userId,
    documentId,
    userAccessToken,  // User's PCF token
    cancellationToken);
```

#### Step 2: OBO Token Exchange (Dataverse)

```csharp
// DataverseAccessDataSource.cs
private async Task<string> GetDataverseTokenViaOBOAsync(string userToken, CancellationToken ct)
{
    _logger.LogDebug("[OBO-DIAG] Starting OBO token exchange for Dataverse");

    var cca = ConfidentialClientApplicationBuilder
        .Create(_clientId)                    // BFF App Registration ID
        .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
        .WithClientSecret(_clientSecret)      // API_CLIENT_SECRET
        .Build();

    var scopes = new[] { $"{_dataverseUri}/.default" };  // Dataverse audience

    var result = await cca.AcquireTokenOnBehalfOf(
        scopes,
        new UserAssertion(userToken)          // PCF user token
    ).ExecuteAsync(ct);

    _logger.LogDebug("[OBO-DIAG] OBO token acquired successfully");
    return result.AccessToken;                // Dataverse-scoped token
}
```

#### Step 3: Set Authorization Header (CRITICAL FIX)

```csharp
// DataverseAccessDataSource.cs - GetUserAccessAsync()
if (!string.IsNullOrEmpty(userAccessToken))
{
    _logger.LogDebug("[UAC-DIAG] Using OBO authentication for user context");
    dataverseToken = await GetDataverseTokenViaOBOAsync(userAccessToken, ct);

    // CRITICAL: Set the OBO token on HttpClient headers for all subsequent API calls
    // WITHOUT THIS: HttpClient uses old service principal token → 401 Unauthorized
    _httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", dataverseToken);

    _logger.LogDebug("[UAC-DIAG] Set OBO token on HttpClient authorization header");
}
```

**Why This Is Critical:**
- MSAL OBO exchange returns a token, but doesn't modify HttpClient
- If you don't set the header, HttpClient uses previous token (service principal or null)
- Result: Dataverse API calls fail with 401 Unauthorized despite successful OBO exchange
- **Symptom**: "Failed to lookup Dataverse user: Unauthorized"

#### Step 4: Lookup Dataverse User

```csharp
// DataverseAccessDataSource.cs
private async Task<Guid?> LookupDataverseUserIdAsync(string azureAdObjectId, CancellationToken ct)
{
    var url = $"systemusers?$filter=azureactivedirectoryobjectid eq {azureAdObjectId}&$select=systemuserid";

    var response = await _httpClient.GetAsync(url, ct);  // Uses OBO token from Step 3

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("[UAC-DIAG] Failed to lookup Dataverse user: {Status}",
            response.StatusCode);
        return null;
    }

    var content = await response.Content.ReadFromJsonAsync<ODataCollectionResponse<SystemUser>>(ct);
    var user = content?.Value?.FirstOrDefault();

    if (user != null)
    {
        _logger.LogDebug("[UAC-DIAG] Found Dataverse user: {UserId}", user.SystemUserId);
        return user.SystemUserId;
    }

    return null;
}
```

#### Step 5: Authorization Check (Direct Query Pattern)

```csharp
// DataverseAccessDataSource.cs
private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(
    string userId,
    string resourceId,
    string dataverseToken,
    CancellationToken ct)
{
    // APPROACH: Query the document directly using the OBO token.
    // If the query succeeds, the user has at least Read access (Dataverse enforces this).
    // If it fails with 403/404, they don't have access.

    var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

    using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
    {
        Headers =
        {
            Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken)
        }
    };

    var response = await _httpClient.SendAsync(requestMessage, ct);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning(
            "[UAC-DIAG] Document access check FAILED: DocumentId={DocumentId}, StatusCode={Status}",
            resourceId, response.StatusCode);

        // 403 or 404 means no access
        return new List<PermissionRecord>();
    }

    _logger.LogDebug(
        "[UAC-DIAG] Document access check PASSED: DocumentId={DocumentId}",
        resourceId);

    // Success! Grant Read access
    return new List<PermissionRecord>
    {
        new PermissionRecord(userId, resourceId, AccessRights.Read)
    };
}
```

**Why Not RetrievePrincipalAccess?**

```csharp
// ORIGINAL APPROACH (FAILED):
var request = new
{
    Target = new {
        sprk_documentid = resourceId,
        "@odata.type" = "Microsoft.Dynamics.CRM.sprk_document"
    },
    PrincipalId = userId
};

var response = await _httpClient.PostAsJsonAsync("RetrievePrincipalAccess", request, ct);
// Result: 404 "Resource not found for the segment 'RetrievePrincipalAccess'"
// Error code: 0x80060888
```

**Root Cause**: `RetrievePrincipalAccess` action doesn't work with delegated (OBO) tokens, only with application tokens.

**Solution**: Direct query approach - if user can retrieve the record, they have Read access. Dataverse security enforces this automatically.

### Error Handling

```csharp
try
{
    var dataverseToken = await GetDataverseTokenViaOBOAsync(userToken, ct);
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", dataverseToken);
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    _logger.LogWarning("[OBO-DIAG] User token expired or invalid");
    return AccessSnapshot.Empty();  // Fail-closed: deny access
}
catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS65001")
{
    // User or admin hasn't consented to Dataverse permissions
    _logger.LogError("[OBO-DIAG] Consent required for Dataverse access");
    return AccessSnapshot.Empty();
}
catch (MsalServiceException ex)
{
    _logger.LogError(ex, "[OBO-DIAG] OBO exchange failed: {ErrorCode}", ex.ErrorCode);
    return AccessSnapshot.Empty();  // Fail-closed
}
```

### Azure AD Configuration

```yaml
BFF App Registration:
  Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c

  API Permissions (Delegated):
    - Dynamics CRM.user_impersonation    # Required for OBO
    - Dynamics CRM.user                  # Required for OBO

  Client Secrets:
    - Name: API_CLIENT_SECRET
      Created: 2025-12-18
      Expires: 2027-12-18
      First chars: l8b8Q~J
      Used by: GraphClientFactory, DataverseAccessDataSource, PlaybookService
```

### Usage in Authorization Filters

```csharp
// AnalysisAuthorizationFilter.cs
public async ValueTask<object?> InvokeAsync(
    EndpointFilterInvocationContext context,
    EndpointFilterDelegate next)
{
    var authService = context.HttpContext.RequestServices
        .GetRequiredService<IAiAuthorizationService>();

    var documentIds = ExtractDocumentIds(context);
    var user = context.HttpContext.User;

    var result = await authService.AuthorizeAsync(
        user,
        documentIds,
        context.HttpContext,  // Passed to extract bearer token
        context.HttpContext.RequestAborted);

    if (!result.Success)
    {
        return Results.Problem(
            statusCode: 403,
            title: "Forbidden",
            detail: result.Reason ?? "Access denied to one or more documents");
    }

    return await next(context);  // Authorization passed
}
```

### Debugging with Application Insights

```kql
// Trace full OBO authorization flow
traces
| where timestamp > ago(1h)
| where message contains "UAC-DIAG" or message contains "AI-AUTH" or message contains "OBO-DIAG"
| project timestamp, message, severityLevel
| order by timestamp desc

// Common OBO errors
exceptions
| where timestamp > ago(1h)
| where outerMessage contains "AcquireTokenOnBehalfOf"
    or outerMessage contains "RetrievePrincipalAccess"
    or outerMessage contains "Failed to lookup Dataverse user"
| project timestamp, outerMessage, problemId, severityLevel
| order by timestamp desc
```

### Critical Bugs Fixed (2026-01-07)

#### Bug #1: OBO Token Not Applied to HttpClient

**Symptom**: 401 Unauthorized when calling Dataverse API, despite successful OBO token exchange

**Root Cause**:
```csharp
// Token acquired but never set on HttpClient
var dataverseToken = await GetDataverseTokenViaOBOAsync(userToken, ct);
// HttpClient still uses old service principal token or null
var response = await _httpClient.GetAsync("systemusers?...", ct);  // ❌ 401
```

**Fix**:
```csharp
var dataverseToken = await GetDataverseTokenViaOBOAsync(userToken, ct);
// CRITICAL: Set the token before making API calls
_httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", dataverseToken);  // ✅
var response = await _httpClient.GetAsync("systemusers?...", ct);  // ✅ 200 OK
```

#### Bug #2: RetrievePrincipalAccess Doesn't Support OBO Tokens

**Symptom**: 404 "Resource not found for the segment 'RetrievePrincipalAccess'"

**Root Cause**: `RetrievePrincipalAccess` action requires application-level permissions, not user-delegated

**Fix**: Changed to direct query authorization - if user can GET the document, they have Read access

### Key Differences Summary

| Pattern Step | Graph API OBO | Dataverse OBO |
|--------------|---------------|---------------|
| **Token scope** | `https://graph.microsoft.com/.default` | `https://{org}.crm.dynamics.com/.default` |
| **Set HttpClient header** | ✅ Automatic (per-request) | ⚠️ **Manual required** (DefaultRequestHeaders) |
| **Authorization check** | Native permissions API | ⚠️ Direct query (RetrievePrincipalAccess unsupported) |
| **User lookup** | Not needed (Graph uses oid) | Required (map Azure AD oid → systemuserid) |
| **Caching** | ✅ Redis (55-min TTL) | ❌ No caching (performance opportunity) |
| **Error handling** | Standard MSAL errors | + Dataverse-specific errors (0x80060888) |

---

## Related Patterns

- [OAuth Scopes](oauth-scopes.md) - Scope format requirements
- [Token Caching](token-caching.md) - Redis caching implementation

---

**Lines**: ~540
