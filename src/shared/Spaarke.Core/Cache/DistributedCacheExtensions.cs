using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Spaarke.Core.Cache;

/// <summary>
/// Extensions for IDistributedCache to support GetOrCreate patterns with versioned keys and TTLs.
/// Follows ADR-009: Redis-first caching with versioned keys for invalidation.
/// </summary>
public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets a cached value, or creates and caches it if not found.
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : class
    {
        var cachedValue = await cache.GetStringAsync(key, ct);
        if (cachedValue != null)
        {
            return JsonSerializer.Deserialize<T>(cachedValue, JsonOptions)!;
        }

        var value = await factory();
        var serialized = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        await cache.SetStringAsync(key, serialized, options, ct);
        return value;
    }

    /// <summary>
    /// Gets a cached value with version support, or creates and caches it if not found.
    /// Version key is appended to create unique cache entries per version.
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        string version,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : class
    {
        var versionedKey = $"{key}:v:{version}";
        return await cache.GetOrCreateAsync(versionedKey, factory, expiration, ct);
    }

    /// <summary>
    /// Gets a cached value, or creates and caches it if not found.
    /// Supports cancellation token propagation to the factory.
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : class
    {
        var cachedValue = await cache.GetStringAsync(key, ct);
        if (cachedValue != null)
        {
            return JsonSerializer.Deserialize<T>(cachedValue, JsonOptions)!;
        }

        var value = await factory(ct);
        var serialized = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        await cache.SetStringAsync(key, serialized, options, ct);
        return value;
    }

    /// <summary>
    /// Gets a cached value with version support, or creates and caches it if not found.
    /// Supports cancellation token propagation to the factory.
    /// </summary>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        string version,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : class
    {
        var versionedKey = $"{key}:v:{version}";
        return await cache.GetOrCreateAsync(versionedKey, factory, expiration, ct);
    }

    /// <summary>
    /// Creates a cache key with standard SDAP prefix and formatting.
    /// </summary>
    public static string CreateKey(string category, string identifier, params string[] parts)
    {
        var keyParts = new[] { "sdap", category, identifier }.Concat(parts);
        return string.Join(":", keyParts);
    }

    /// <summary>
    /// Standard TTL for security-sensitive data like UAC snapshots.
    /// Short TTL reduces staleness risk for authorization-related data.
    /// </summary>
    public static readonly TimeSpan SecurityDataTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Standard TTL for document metadata and other less sensitive data.
    /// </summary>
    public static readonly TimeSpan MetadataTtl = TimeSpan.FromMinutes(15);
}
