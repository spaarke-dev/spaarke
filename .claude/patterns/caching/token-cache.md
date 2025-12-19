# Token Cache Pattern

> **Domain**: Caching / OBO Token Management
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Server-side token cache |
| `src/client/pcf/*/services/auth/MsalAuthProvider.ts` | Client-side token cache |

---

## Security Requirements

- **Never store user tokens in plaintext**
- Hash user tokens (SHA256) before using as cache keys
- Only log hash prefixes (first 8 characters)
- Graph tokens cached with 55-minute TTL (5-minute buffer)

---

## Server-Side Implementation

### Token Hash Computation

```csharp
public string ComputeTokenHash(string userToken)
{
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
    private readonly CacheMetrics _metrics;

    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = $"{CacheKeyPrefix}{tokenHash}";

        var cached = await _cache.GetStringAsync(cacheKey);

        if (cached != null)
        {
            _metrics.RecordHit(sw.ElapsedMilliseconds);
            _logger.LogDebug("Cache HIT for token {Hash}...", tokenHash[..8]);
        }
        else
        {
            _metrics.RecordMiss(sw.ElapsedMilliseconds);
            _logger.LogDebug("Cache MISS for token {Hash}...", tokenHash[..8]);
        }

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

    public async Task RemoveTokenAsync(string tokenHash)
    {
        var cacheKey = $"{CacheKeyPrefix}{tokenHash}";
        await _cache.RemoveAsync(cacheKey);
    }
}
```

---

## Usage in OBO Flow

```csharp
private async Task<string> AcquireGraphTokenAsync(string userToken)
{
    // 1. Hash user token (never store plaintext)
    var tokenHash = _tokenCache.ComputeTokenHash(userToken);

    // 2. Check cache
    var cached = await _tokenCache.GetTokenAsync(tokenHash);
    if (cached != null)
        return cached;

    // 3. Perform OBO exchange
    var result = await _cca.AcquireTokenOnBehalfOf(
        scopes: new[] { "https://graph.microsoft.com/.default" },
        userAssertion: new UserAssertion(userToken)
    ).ExecuteAsync();

    // 4. Cache with 55-minute TTL
    await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

    return result.AccessToken;
}
```

---

## Performance Targets

| Metric | Target |
|--------|--------|
| Cache hit rate | 95%+ |
| Auth latency (hit) | ~5ms |
| Auth latency (miss) | ~200ms |
| TTL | 55 minutes |

---

## Key Points

1. **SHA256 hash user tokens** - Never store/log plaintext
2. **55-minute TTL** - 5-minute buffer before expiry
3. **Log hash prefix only** - First 8 characters for debugging
4. **Fail gracefully** - Cache errors shouldn't break OBO
5. **Track hit rate** - Monitor via CacheMetrics

---

## Related Patterns

- [OBO Flow](../auth/obo-flow.md) - Token exchange flow
- [Distributed Cache](distributed-cache.md) - Redis setup

---

**Lines**: ~100
