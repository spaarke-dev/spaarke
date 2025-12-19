# Distributed Cache Pattern

> **Domain**: Caching / IDistributedCache
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-009

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | Extension methods |
| `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Redis configuration |
| `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` | Cache observability |

---

## GetOrCreate Pattern

Primary cache access pattern with factory function:

```csharp
public static async Task<T> GetOrCreateAsync<T>(
    this IDistributedCache cache,
    string key,
    Func<Task<T>> factory,
    TimeSpan expiration,
    CancellationToken ct = default) where T : class
{
    // 1. Try get from cache
    var cached = await cache.GetStringAsync(key, ct);
    if (cached != null)
        return JsonSerializer.Deserialize<T>(cached, JsonOptions)!;

    // 2. Execute factory if miss
    var value = await factory();

    // 3. Store in cache
    var json = JsonSerializer.Serialize(value, JsonOptions);
    await cache.SetStringAsync(key, json,
        new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        }, ct);

    return value;
}
```

---

## Versioned Cache Keys

For cache invalidation via version changes:

```csharp
public static async Task<T> GetOrCreateAsync<T>(
    this IDistributedCache cache,
    string key,
    string version,
    Func<Task<T>> factory,
    TimeSpan expiration,
    CancellationToken ct = default) where T : class
{
    // Include version in key: sdap:category:id:v:abc123
    var versionedKey = $"{key}:v:{version}";
    return await cache.GetOrCreateAsync(versionedKey, factory, expiration, ct);
}
```

---

## Cache Key Helper

Consistent key format with namespace prefix:

```csharp
public static string CreateKey(string category, string identifier, string? version = null)
{
    var key = $"sdap:{category}:{identifier}";
    return version != null ? $"{key}:v:{version}" : key;
}
```

---

## TTL Constants

```csharp
public static class CacheTtl
{
    public static readonly TimeSpan SecurityDataTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MetadataTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(55);  // 5-min buffer
}
```

---

## Redis Configuration

```csharp
// RedisOptions.cs
public class RedisOptions
{
    public bool Enabled { get; set; }           // false = dev, true = prod
    public string? ConnectionString { get; set; }
    public string InstanceName { get; set; } = "sdap:";
}

// Program.cs setup
if (redisOptions.Enabled)
{
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisOptions.ConnectionString;
        options.InstanceName = redisOptions.InstanceName;
        options.ConfigurationOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,      // Graceful degradation
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            ConnectRetry = 3
        };
    });
}
else
{
    services.AddDistributedMemoryCache();    // In-memory fallback
}
```

---

## Cache Metrics

OpenTelemetry-compatible metrics tracking:

```csharp
public class CacheMetrics : IDisposable
{
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _cacheLatency;

    public void RecordHit(double latencyMs, string cacheType = "graph")
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs, new KeyValuePair<string, object?>("cache.result", "hit"));
    }

    public void RecordMiss(double latencyMs, string cacheType = "graph")
    {
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs, new KeyValuePair<string, object?>("cache.result", "miss"));
    }
}
```

---

## Key Points

1. **Redis in production only** - Use in-memory for development
2. **AbortOnConnectFail = false** - Don't crash if Redis unavailable
3. **Version keys for invalidation** - Change version to invalidate
4. **JSON serialization** - camelCase naming policy
5. **Track metrics** - Hit rate target: 95%+

---

## Related Patterns

- [Request Cache](request-cache.md) - Per-request memoization
- [Token Cache](token-cache.md) - OBO token caching

---

**Lines**: ~120
