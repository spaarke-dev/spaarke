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

## Related Patterns

- [OAuth Scopes](oauth-scopes.md) - Scope format requirements
- [Token Caching](token-caching.md) - Redis caching implementation

---

**Lines**: ~130
