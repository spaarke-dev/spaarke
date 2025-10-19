OAUTH-OBO-ANTI-PATTERNS

# OAuth 2.0 OBO Flow - Anti-Patterns

## ❌ ANTI-PATTERN 1: Not Validating Token Audience
```csharp
// ❌ WRONG
var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
// Danger: Token might be for wrong API!
```

**Why Bad:** OBO will fail if token audience doesn't match  
**Error:** `AADSTS50013: Assertion audience claim does not match`
```csharp
// ✅ CORRECT
var token = new JwtSecurityTokenHandler().ReadJwtToken(userToken);
if (token.Audiences.FirstOrDefault() != $"api://{_apiAppId}")
{
    throw new UnauthorizedAccessException("Token audience mismatch");
}
```

---

## ❌ ANTI-PATTERN 2: Wrong Scope Format
```csharp
// ❌ WRONG
var scopes = new[] { "User.Read", "Files.ReadWrite" };
```

**Why Bad:** OBO requires `.default` scope  
**Error:** `AADSTS70011`
```csharp
// ✅ CORRECT
var scopes = new[] { "https://graph.microsoft.com/.default" };
```

---

## ❌ ANTI-PATTERN 3: Caching Without Expiration
```csharp
// ❌ WRONG
private static string _cachedToken;
_cachedToken = result.AccessToken;  // Cached forever!
```

**Why Bad:** Token expires after 1 hour
```csharp
// ✅ CORRECT
var ttl = result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
await _cache.SetStringAsync(key, token, new DistributedCacheEntryOptions 
{ 
    AbsoluteExpirationRelativeToNow = ttl 
});
```

---

## ❌ ANTI-PATTERN 4: Using Public Client
```csharp
// ❌ WRONG
var pca = PublicClientApplicationBuilder.Create(clientId).Build();
await pca.AcquireTokenOnBehalfOf(...).ExecuteAsync();  // Will fail!
```

**Why Bad:** Public clients can't hold secrets  
**Error:** `AADSTS7000218: client_assertion or client_secret required`
```csharp
// ✅ CORRECT
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(secret)
    .Build();
```