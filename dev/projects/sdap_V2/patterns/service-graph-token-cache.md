# Pattern: Graph Token Cache (Redis)

**Use For**: Caching OBO tokens to reduce Azure AD load by 97%
**Task**: Implementing token caching with Redis
**Time**: 20 minutes

---

## Quick Copy-Paste

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Spe.Bff.Api.Services;

/// <summary>
/// Caches Graph API OBO tokens to reduce Azure AD load (ADR-009).
/// Target: 95% cache hit rate, 97% reduction in auth latency.
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Compute SHA256 hash of user token for cache key.
    /// Ensures consistent key length and prevents token exposure in logs.
    /// </summary>
    public string ComputeTokenHash(string userToken)
    {
        if (string.IsNullOrEmpty(userToken))
            throw new ArgumentException("User token cannot be null or empty", nameof(userToken));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Get cached Graph token by user token hash.
    /// </summary>
    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            var cachedToken = await _cache.GetStringAsync(cacheKey);

            if (cachedToken != null)
                _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
            else
                _logger.LogDebug("Cache MISS for token hash {Hash}...", tokenHash[..8]);

            return cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token from cache");
            return null; // Fail gracefully, will perform OBO exchange
        }
    }

    /// <summary>
    /// Cache Graph token with TTL.
    /// </summary>
    public async Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                graphToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry
                });

            _logger.LogDebug(
                "Cached token for hash {Hash}... with TTL {TTL} minutes",
                tokenHash[..8],
                expiry.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching token");
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <summary>
    /// Remove token from cache (e.g., on logout).
    /// </summary>
    public async Task RemoveTokenAsync(string tokenHash)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Removed cached token for hash {Hash}...", tokenHash[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing token from cache");
        }
    }
}
```

---

## Cache Key Strategy (ADR-009)

| Cache Key Pattern | TTL | Purpose |
|------------------|-----|---------|
| `sdap:graph:token:{SHA256(userToken)}` | 55 min | OBO Graph tokens |
| `sdap:access:user:{userId}:v1` | 5 min | Authorization snapshots |
| `sdap:document:{documentId}:metadata:v1` | 10 min | Document metadata |

---

## Key Points

1. **SHA256 hashing** - User token hashed for security
2. **55-minute TTL** - 5-minute buffer before 60-minute expiration
3. **Versioned keys** - Include `:v1` for cache invalidation
4. **Graceful failures** - Catch errors, don't throw (caching is optimization)
5. **Distributed cache** - Redis for cross-instance caching

---

## Redis Configuration

```csharp
// In RedisCacheExtensions.cs
public static IServiceCollection AddRedisCache(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var redisOptions = configuration.GetSection("Redis").Get<RedisOptions>();

    if (redisOptions?.Enabled == true)
    {
        var connectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string not configured");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = redisOptions.InstanceName ?? "sdap:";
        });
    }
    else
    {
        // Fallback to in-memory cache for development
        services.AddDistributedMemoryCache();
    }

    services.AddSingleton<GraphTokenCache>();
    return services;
}
```

---

## appsettings.json

```json
{
  "Redis": {
    "Enabled": true,
    "InstanceName": "sdap-prod:",
    "DefaultExpirationMinutes": 60
  },
  "ConnectionStrings": {
    "Redis": "@Microsoft.KeyVault(SecretUri=...Redis-ConnectionString)"
  }
}
```

---

## Checklist

- [ ] Uses `IDistributedCache` (Redis)
- [ ] SHA256 hashing for cache keys
- [ ] Cache key includes version (`:v1`)
- [ ] 55-minute TTL for tokens
- [ ] Graceful error handling (don't throw)
- [ ] Logs cache hits/misses at Debug level
- [ ] Only log first 8 chars of hash (security)

---

## Related Files

- Create: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`
- Used by: `GraphClientFactory`
- Requires: Redis connection string in KeyVault

---

## DI Registration

```csharp
// In DocumentsModuleExtensions.cs (ADR-010)
services.AddSingleton<GraphTokenCache>();

// In RedisCacheExtensions.cs
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = connectionString;
    options.InstanceName = "sdap:";
});
```
