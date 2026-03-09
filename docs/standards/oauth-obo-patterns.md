# OAuth 2.0 OBO Flow - Implementation Patterns & Anti-Patterns

> **Source**: Knowledge Management - OAuth/OBO
> **Last Updated**: 2026-03
> **Applies To**: Any middle-tier API calling downstream APIs on behalf of user
>
> *Consolidated from oauth-obo-implementation.md and oauth-obo-anti-patterns.md*

---

## TL;DR

On-Behalf-Of (OBO) exchanges a user token for a new token to call downstream APIs while preserving user identity. Requires confidential client (has secret), `.default` scope, and proper token validation. Cache tokens with TTL buffer.

Top OBO mistakes: (1) not validating token audience, (2) using individual scopes instead of `.default`, (3) caching without expiration, (4) using public client. Each causes specific AADSTS errors.

---

## Applies When

- Middle-tier API needs to call Graph API on user's behalf
- Middle-tier API needs to call another API preserving user context
- Building BFF (Backend-for-Frontend) patterns
- Reviewing OBO implementation code
- Debugging intermittent auth failures
- Security audit of token handling
- NOT for direct client-to-API calls (use authorization code flow)

---

## Decision Tree

```
Need to call downstream API on behalf of user?
├─ YES
│   ├─ Have user token? YES → Use OBO (this guide)
│   └─ No user token? → ERROR: OBO requires user token
└─ NO → Use Client Credentials instead
```

---

## Part 1: Implementation Patterns

### Pattern 1: Basic OBO (No Caching)

```csharp
public class GraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;

    public GraphClientFactory(IConfiguration config)
    {
        _cca = ConfidentialClientApplicationBuilder
            .Create(config["API_APP_ID"])
            .WithClientSecret(config["API_CLIENT_SECRET"])
            .WithAuthority(AzureCloudInstance.AzurePublic, config["TENANT_ID"])
            .Build();
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userToken)
    {
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        var userAssertion = new UserAssertion(userToken);

        var result = await _cca
            .AcquireTokenOnBehalfOf(scopes, userAssertion)
            .ExecuteAsync();

        return CreateGraphClientWithToken(result.AccessToken);
    }
}
```

### Pattern 2: OBO with Token Caching (Recommended)

```csharp
public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userToken)
{
    var tokenHash = ComputeTokenHash(userToken);

    // Check cache first
    var cached = await _tokenCache.GetTokenAsync(tokenHash);
    if (cached != null) return CreateGraphClientWithToken(cached);

    // Cache miss - perform OBO
    var result = await _cca
        .AcquireTokenOnBehalfOf(
            new[] { "https://graph.microsoft.com/.default" },
            new UserAssertion(userToken))
        .ExecuteAsync();

    // Cache with 55-min TTL (5-min buffer before expiry)
    var ttl = result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
    if (ttl > TimeSpan.Zero)
    {
        await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, ttl);
    }

    return CreateGraphClientWithToken(result.AccessToken);
}

private static string ComputeTokenHash(string token)
{
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
    return Convert.ToBase64String(bytes);
}
```

### Pattern 3: Error Handling

```csharp
public async Task<string> AcquireOboTokenAsync(string userToken, string[] scopes)
{
    try
    {
        var result = await _cca
            .AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken))
            .ExecuteAsync();
        return result.AccessToken;
    }
    catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
    {
        // User token expired or revoked - user must re-authenticate
        throw new UnauthorizedAccessException("User session expired. Please sign in again.", ex);
    }
    catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS50013")
    {
        // Token audience mismatch
        throw new UnauthorizedAccessException("Token audience mismatch. Check API configuration.", ex);
    }
    catch (MsalServiceException ex) when (ex.ErrorCode == "AADSTS70011")
    {
        // Invalid scope format
        throw new InvalidOperationException("Invalid scope format. Use .default scope.", ex);
    }
}
```

---

## Required Configuration

```json
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=...)",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2"
}
```

---

## Pre-Implementation Checklist

- [ ] Middle-tier API is confidential client (has client secret or certificate)
- [ ] Have valid user access token from upstream
- [ ] Token audience matches your API (`api://{your-api-id}`)
- [ ] Using `.default` scope for downstream API
- [ ] `knownClientApplications` configured in app manifest (for consent)
- [ ] Error handling for `MsalServiceException`

---

## Validation

```bash
# Test OBO flow manually
curl -X POST https://your-api.azurewebsites.net/api/documents/upload \
  -H "Authorization: Bearer {user-token}" \
  -H "Content-Type: application/json"

# Check logs for:
# "OBO token acquired successfully"
# "Token cached with TTL: 55 minutes"
```

---

## Part 2: Anti-Patterns

### Anti-Pattern 1: Not Validating Token Audience

```csharp
// WRONG - Blindly trusting incoming token
public async Task<string> GetOboToken(string userToken)
{
    var result = await _cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken))
        .ExecuteAsync();
    return result.AccessToken;
}
```

**Why Bad:** OBO fails if token audience doesn't match your API
**Error:** `AADSTS50013: Assertion audience claim does not match`

```csharp
// CORRECT - Validate audience first
public async Task<string> GetOboToken(string userToken)
{
    var token = new JwtSecurityTokenHandler().ReadJwtToken(userToken);
    if (token.Audiences.FirstOrDefault() != $"api://{_apiAppId}")
    {
        throw new UnauthorizedAccessException(
            $"Token audience mismatch. Expected: api://{_apiAppId}");
    }

    var result = await _cca.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userToken))
        .ExecuteAsync();
    return result.AccessToken;
}
```

### Anti-Pattern 2: Wrong Scope Format

```csharp
// WRONG - Individual permission scopes
var scopes = new[] { "User.Read", "Files.ReadWrite.All" };
var result = await _cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

**Why Bad:** OBO requires `.default` scope
**Error:** `AADSTS70011: The provided request must include a 'scope' input parameter`

```csharp
// CORRECT - Use .default scope
var scopes = new[] { "https://graph.microsoft.com/.default" };
var result = await _cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

**Why `.default`:** It requests all pre-consented permissions for the resource in a single scope.

### Anti-Pattern 3: Caching Without Expiration

```csharp
// WRONG - Static cache, no expiration
private static Dictionary<string, string> _tokenCache = new();

public async Task<string> GetOboToken(string userToken)
{
    var key = ComputeHash(userToken);
    if (_tokenCache.TryGetValue(key, out var cached))
        return cached;  // Could be expired!

    var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
    _tokenCache[key] = result.AccessToken;  // Cached forever!
    return result.AccessToken;
}
```

**Why Bad:** Tokens expire after ~1 hour; cached expired tokens cause 401s
**Error:** `401 Unauthorized` on downstream API calls

```csharp
// CORRECT - Cache with TTL buffer
public async Task<string> GetOboToken(string userToken)
{
    var key = $"obo:{ComputeHash(userToken)}";
    var cached = await _cache.GetStringAsync(key);
    if (cached != null) return cached;

    var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();

    // Cache with 5-minute buffer before expiry
    var ttl = result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
    if (ttl > TimeSpan.Zero)
    {
        await _cache.SetStringAsync(key, result.AccessToken,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }

    return result.AccessToken;
}
```

### Anti-Pattern 4: Using Public Client

```csharp
// WRONG - Public client can't do OBO
var pca = PublicClientApplicationBuilder
    .Create(clientId)
    .Build();

await pca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();  // FAILS!
```

**Why Bad:** Public clients don't have secrets; OBO requires confidential client
**Error:** `AADSTS7000218: The request body must contain 'client_assertion' or 'client_secret'`

```csharp
// CORRECT - Confidential client with secret
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();

await cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

### Anti-Pattern 5: Logging Tokens

```csharp
// WRONG - Tokens in logs = security vulnerability
_logger.LogInformation("Got token: {Token}", result.AccessToken);
_logger.LogDebug("User token: {UserToken}", userToken);
```

**Why Bad:** Tokens in logs can be stolen and replayed

```csharp
// CORRECT - Log metadata only
_logger.LogInformation("OBO token acquired. Expires: {Expiry}, Scopes: {Scopes}",
    result.ExpiresOn, string.Join(",", result.Scopes));
```

### Anti-Pattern 6: Swallowing Exceptions

```csharp
// WRONG - Silent failure
try
{
    var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
    return result.AccessToken;
}
catch (Exception)
{
    return null;  // Caller has no idea what went wrong
}
```

**Why Bad:** Hides root cause; caller might retry forever

```csharp
// CORRECT - Specific exception handling
try
{
    var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
    return result.AccessToken;
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    throw new UnauthorizedAccessException("Session expired. Please sign in again.", ex);
}
catch (MsalServiceException ex)
{
    _logger.LogError(ex, "OBO failed: {Error}", ex.ErrorCode);
    throw;
}
```

---

## Code Review Checklist

- [ ] Token audience validated before OBO?
- [ ] Using `.default` scope?
- [ ] Cache has TTL with buffer?
- [ ] Using ConfidentialClientApplication?
- [ ] No tokens logged?
- [ ] Specific exception handling?
- [ ] Secrets from Key Vault, not hardcoded?

---

## Related Articles

- [oauth-obo-errors.md](oauth-obo-errors.md) - Error codes and fixes
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - SDAP-specific auth flows

---

*Condensed from OAuth/OBO knowledge base*
