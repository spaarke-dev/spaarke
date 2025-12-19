# OAuth 2.0 OBO Flow - Implementation Patterns

> **Source**: Knowledge Management - OAuth/OBO
> **Last Updated**: December 3, 2025
> **Applies To**: Any middle-tier API calling downstream APIs on behalf of user

---

## TL;DR

On-Behalf-Of (OBO) exchanges a user token for a new token to call downstream APIs while preserving user identity. Requires confidential client (has secret), `.default` scope, and proper token validation. Cache tokens with TTL buffer.

---

## Applies When

- Middle-tier API needs to call Graph API on user's behalf
- Middle-tier API needs to call another API preserving user context
- Building BFF (Backend-for-Frontend) patterns
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

## Pattern 1: Basic OBO (No Caching)

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

---

## Pattern 2: OBO with Token Caching (Recommended)

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

---

## Pattern 3: Error Handling

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

## Related Articles

- [oauth-obo-errors.md](oauth-obo-errors.md) - Error codes and fixes
- [oauth-obo-anti-patterns.md](oauth-obo-anti-patterns.md) - What NOT to do
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - SDAP-specific auth flows

---

*Condensed from OAuth/OBO knowledge base*
