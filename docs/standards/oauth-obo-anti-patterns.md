# OAuth 2.0 OBO Flow - Anti-Patterns

> **Source**: Knowledge Management - OAuth/OBO
> **Last Updated**: December 3, 2025
> **Applies To**: Code review, avoiding common OBO mistakes

---

## TL;DR

Top OBO mistakes: (1) not validating token audience, (2) using individual scopes instead of `.default`, (3) caching without expiration, (4) using public client. Each causes specific AADSTS errors.

---

## Applies When

- Reviewing OBO implementation code
- Debugging intermittent auth failures
- Security audit of token handling
- Onboarding new developers to OBO patterns

---

## ❌ Anti-Pattern 1: Not Validating Token Audience

```csharp
// ❌ WRONG - Blindly trusting incoming token
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
// ✅ CORRECT - Validate audience first
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

---

## ❌ Anti-Pattern 2: Wrong Scope Format

```csharp
// ❌ WRONG - Individual permission scopes
var scopes = new[] { "User.Read", "Files.ReadWrite.All" };
var result = await _cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

**Why Bad:** OBO requires `.default` scope  
**Error:** `AADSTS70011: The provided request must include a 'scope' input parameter`

```csharp
// ✅ CORRECT - Use .default scope
var scopes = new[] { "https://graph.microsoft.com/.default" };
var result = await _cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

**Why `.default`:** It requests all pre-consented permissions for the resource in a single scope.

---

## ❌ Anti-Pattern 3: Caching Without Expiration

```csharp
// ❌ WRONG - Static cache, no expiration
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
// ✅ CORRECT - Cache with TTL buffer
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

---

## ❌ Anti-Pattern 4: Using Public Client

```csharp
// ❌ WRONG - Public client can't do OBO
var pca = PublicClientApplicationBuilder
    .Create(clientId)
    .Build();

await pca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();  // FAILS!
```

**Why Bad:** Public clients don't have secrets; OBO requires confidential client  
**Error:** `AADSTS7000218: The request body must contain 'client_assertion' or 'client_secret'`

```csharp
// ✅ CORRECT - Confidential client with secret
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .Build();

await cca.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
```

---

## ❌ Anti-Pattern 5: Logging Tokens

```csharp
// ❌ WRONG - Tokens in logs = security vulnerability
_logger.LogInformation("Got token: {Token}", result.AccessToken);
_logger.LogDebug("User token: {UserToken}", userToken);
```

**Why Bad:** Tokens in logs can be stolen and replayed

```csharp
// ✅ CORRECT - Log metadata only
_logger.LogInformation("OBO token acquired. Expires: {Expiry}, Scopes: {Scopes}", 
    result.ExpiresOn, string.Join(",", result.Scopes));
```

---

## ❌ Anti-Pattern 6: Swallowing Exceptions

```csharp
// ❌ WRONG - Silent failure
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
// ✅ CORRECT - Specific exception handling
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

- [oauth-obo-implementation.md](oauth-obo-implementation.md) - Correct patterns
- [oauth-obo-errors.md](oauth-obo-errors.md) - Error reference
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - SDAP-specific auth

---

*Condensed from OAuth/OBO knowledge base*
