# Token Caching Pattern

> **Domain**: OAuth / Token Management
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Server-side Redis cache |
| `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` | Client-side sessionStorage |

---

## Server-Side Caching (Redis)

### Token Hash Computation
```csharp
public string ComputeTokenHash(string userToken)
{
    // NEVER store user tokens in plaintext
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
    return Convert.ToBase64String(hashBytes);
}
```

### Cache Operations
```csharp
public class GraphTokenCache
{
    private const string CacheKeyPrefix = "sdap:graph:token:";
    private readonly IDistributedCache _cache;

    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var cacheKey = $"{CacheKeyPrefix}{tokenHash}";
        var cached = await _cache.GetStringAsync(cacheKey);

        if (cached != null)
            _logger.LogDebug("Cache HIT for token {Hash}...", tokenHash[..8]);
        else
            _logger.LogDebug("Cache MISS for token {Hash}...", tokenHash[..8]);

        return cached;
    }

    public async Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry)
    {
        var cacheKey = $"{CacheKeyPrefix}{tokenHash}";

        await _cache.SetStringAsync(cacheKey, graphToken,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            });
    }
}
```

### TTL Strategy
- **Token lifetime**: 60 minutes (Azure AD default)
- **Cache TTL**: 55 minutes (5-minute buffer)
- **Target hit rate**: 95%+

---

## Client-Side Caching (sessionStorage)

### Cache Entry Structure
```typescript
interface TokenCacheEntry {
    token: string;
    expiresAt: number;  // Unix timestamp
    scopes: string[];
}

const CACHE_KEY_PREFIX = "msal.token.";
const EXPIRATION_BUFFER_MS = 5 * 60 * 1000;  // 5 minutes
```

### Get Cached Token
```typescript
private getCachedToken(scopes: string[]): string | null {
    const cacheKey = this.getCacheKey(scopes);
    const cached = sessionStorage.getItem(cacheKey);
    if (!cached) return null;

    const entry: TokenCacheEntry = JSON.parse(cached);
    const now = Date.now();
    const bufferExpiration = entry.expiresAt - EXPIRATION_BUFFER_MS;

    // Token expired
    if (now >= bufferExpiration) {
        this.removeCachedToken(scopes);
        return null;
    }

    // Token nearing expiration - trigger background refresh
    const refreshThreshold = bufferExpiration - ((bufferExpiration - now) / 2);
    if (now >= refreshThreshold) {
        this.refreshTokenInBackground(scopes);
    }

    return entry.token;
}
```

### Set Cached Token
```typescript
private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
    const cacheKey = this.getCacheKey(scopes);

    const entry: TokenCacheEntry = {
        token,
        expiresAt: expiresOn.getTime(),
        scopes
    };

    sessionStorage.setItem(cacheKey, JSON.stringify(entry));
}
```

### Background Refresh
```typescript
private refreshTokenInBackground(scopes: string[]): void {
    const scopesKey = this.getCacheKey(scopes);

    // Prevent duplicate refresh requests
    if (this.refreshPromises.has(scopesKey)) return;

    const refreshPromise = (async () => {
        try {
            const tokenResponse = await this.acquireTokenSilent(scopes);
            this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn!, scopes);
        } catch {
            this.removeCachedToken(scopes);
        } finally {
            this.refreshPromises.delete(scopesKey);
        }
    })();

    this.refreshPromises.set(scopesKey, refreshPromise);
}
```

---

## Cache Key Patterns

### Server-Side (Redis)
```
sdap:graph:token:{sha256-hash-of-user-token}
```

### Client-Side (sessionStorage)
```
msal.token.{scope-string}
```

---

## Error Handling

### Server-Side
```csharp
public async Task<string?> GetTokenAsync(string tokenHash)
{
    try
    {
        return await _cache.GetStringAsync($"{CacheKeyPrefix}{tokenHash}");
    }
    catch (Exception ex)
    {
        // Cache failure should not break OBO flow
        _logger.LogWarning(ex, "Cache read failed, continuing without cache");
        return null;
    }
}
```

### Client-Side
```typescript
private getCachedToken(scopes: string[]): string | null {
    try {
        // ... cache logic
    } catch (error) {
        console.warn("Failed to read cached token, will reacquire", error);
        return null;
    }
}
```

---

## Cache Clearing

### Server-Side
- Tokens auto-expire after TTL
- No manual clearing needed

### Client-Side
```typescript
public clearCache(): void {
    // Cancel ongoing refreshes
    this.refreshPromises.clear();

    // Clear sessionStorage entries
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key?.startsWith(CACHE_KEY_PREFIX)) {
            sessionStorage.removeItem(key);
        }
    }

    // Clear MSAL internal cache
    this.msalInstance?.clearCache?.();
    this.currentAccount = null;
}
```

---

## Key Points

1. **Never store user tokens in plaintext** - Hash before using as cache key
2. **Use 5-minute buffer** - Refresh before expiration
3. **Fail gracefully** - Cache errors shouldn't break authentication
4. **Background refresh** - Proactively refresh nearing expiration
5. **sessionStorage for browser** - Cleared on tab close (security)

---

## Related Patterns

- [OBO Flow](obo-flow.md) - Uses server-side caching
- [MSAL Client](msal-client.md) - Uses client-side caching

---

**Lines**: ~120
