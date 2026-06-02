using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Spaarke.Core.Cache;

/// <summary>
/// Extensions for <see cref="IDistributedCache"/> to support GetOrCreate patterns with
/// versioned keys and TTLs.
/// Follows ADR-009: Redis-first caching with versioned keys for invalidation.
/// </summary>
/// <remarks>
/// <para>
/// Canonical generic helper for the <c>Get → null check → factory → Set</c> pattern.
/// Per the Q5 audit (2026-05-27), individual caches (EmbeddingCache, ChatSessionManager,
/// InsightsPlaybookExecutionCache, etc.) should adopt this helper rather than
/// reimplementing the pattern. Adoption is opt-in; existing caches are NOT refactored
/// by changes to this class (per task 024 low-risk constraint).
/// </para>
/// <para>
/// Generic constraint is <c>where T : notnull</c> so the helper supports BOTH reference-type
/// POCOs (records, classes) AND value-type POCOs (record struct). The original
/// <c>where T : class</c> constraint was loosened by task 024 to plug the Q5 gap.
/// </para>
/// <para>
/// Serialization is JSON via <see cref="System.Text.Json"/> with camelCase property naming.
/// For binary-optimal payloads (e.g. <see cref="float"/>[] embeddings), use a bespoke cache
/// instead — see <c>EmbeddingCache</c>.
/// </para>
/// </remarks>
public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets a cached value, or invokes the factory and caches the result if not found.
    /// Supports any non-nullable T (records, classes, record structs, primitives).
    /// </summary>
    /// <typeparam name="T">
    /// Value type to cache. Constrained to <c>notnull</c>. JSON-serializable via
    /// <see cref="System.Text.Json"/> with camelCase property naming.
    /// </typeparam>
    /// <param name="cache">The distributed cache.</param>
    /// <param name="key">Cache key. Caller owns prefix/versioning conventions.</param>
    /// <param name="factory">Async factory invoked on cache miss.</param>
    /// <param name="expiration">Absolute expiration relative to now.</param>
    /// <param name="ct">Cancellation token propagated to cache reads and writes (not the factory).</param>
    /// <returns>The cached value, or the freshly produced value if cache miss.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="cache"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is null or empty.</exception>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var cachedValue = await cache.GetStringAsync(key, ct).ConfigureAwait(false);
        if (cachedValue != null)
        {
            var deserialized = JsonSerializer.Deserialize<T>(cachedValue, JsonOptions);
            if (deserialized is not null)
            {
                return deserialized;
            }
            // Defensive: deserialization returning null on a notnull T is a corruption signal;
            // treat as a miss and rewrite below.
        }

        var value = await factory().ConfigureAwait(false);
        var serialized = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        await cache.SetStringAsync(key, serialized, options, ct).ConfigureAwait(false);
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
        CancellationToken ct = default) where T : notnull
    {
        var versionedKey = $"{key}:v:{version}";
        return await cache.GetOrCreateAsync(versionedKey, factory, expiration, ct);
    }

    /// <summary>
    /// Gets a cached value, or invokes the factory and caches the result if not found.
    /// Supports cancellation token propagation to the factory. Canonical helper signature
    /// per ADR-009 §Implementation Pattern.
    /// </summary>
    /// <typeparam name="T">See <see cref="GetOrCreateAsync{T}(IDistributedCache, string, Func{Task{T}}, TimeSpan, CancellationToken)"/>.</typeparam>
    /// <param name="cache">The distributed cache.</param>
    /// <param name="key">Cache key.</param>
    /// <param name="factory">Async factory invoked on cache miss. Receives the cancellation token.</param>
    /// <param name="expiration">Absolute expiration relative to now.</param>
    /// <param name="ct">Cancellation token propagated to cache reads, writes, and the factory.</param>
    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan expiration,
        CancellationToken ct = default) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var cachedValue = await cache.GetStringAsync(key, ct).ConfigureAwait(false);
        if (cachedValue != null)
        {
            var deserialized = JsonSerializer.Deserialize<T>(cachedValue, JsonOptions);
            if (deserialized is not null)
            {
                return deserialized;
            }
            // Defensive: deserialization returning null on a notnull T is a corruption signal;
            // treat as a miss and rewrite below.
        }

        var value = await factory(ct).ConfigureAwait(false);
        var serialized = JsonSerializer.Serialize(value, JsonOptions);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        };

        await cache.SetStringAsync(key, serialized, options, ct).ConfigureAwait(false);
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
        CancellationToken ct = default) where T : notnull
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
