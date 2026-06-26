using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// Default <see cref="ITenantCache"/> implementation wrapping <see cref="IDistributedCache"/>
/// (the application's singleton Redis or in-memory provider per <c>CacheModule</c>).
/// </summary>
/// <remarks>
/// Constructs tenant-scoped keys of the form
/// <c>tenant:{tenantId}:{resource}:{id}:v{version}</c>. The configured
/// <c>InstanceName</c> (currently <c>spaarke:</c>) is prepended by
/// <c>StackExchangeRedisCache</c>, not by this wrapper.
/// </remarks>
internal sealed class TenantCache : ITenantCache
{
    private const string DefaultCacheInstance = "default";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // FR-16: Custom cache metrics. Hit-rate is computed downstream from hits/(hits+misses).
    // Meter name follows the BFF's "Sprk.Bff.Api.*" convention (matches the existing
    // `metrics.AddMeter("Sprk.Bff.Api.Cache")` registration in `TelemetryModule.cs` so
    // metrics flow to App Insights via OpenTelemetry without additional registration).
    internal static readonly Meter Meter = new("Sprk.Bff.Api.Cache", "1.0.0");
    internal static readonly Counter<long> HitsCounter = Meter.CreateCounter<long>("cache.hits");
    internal static readonly Counter<long> MissesCounter = Meter.CreateCounter<long>("cache.misses");
    internal static readonly Histogram<double> CallDurationHistogram =
        Meter.CreateHistogram<double>("cache.redis_call_duration_ms");
    // FR-01 (spaarke-redis-cache-remediation-r2 task 001): cache.failures Counter
    // dimensioned by outcome (timeout/canceled/connection/serialization/other) and op.
    // Emitted by MetricsDistributedCache try/catch wrapper so Redis outages are observable.
    internal static readonly Counter<long> FailuresCounter = Meter.CreateCounter<long>(
        "cache.failures",
        unit: "{failure}",
        description: "Count of cache operation failures by outcome and op.");

    private readonly IDistributedCache _cache;
    private readonly ILogger<TenantCache> _logger;

    public TenantCache(IDistributedCache cache, ILogger<TenantCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<T?> GetAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);
        // Metrics (hits/misses/duration) are emitted by MetricsDistributedCache decorator
        // at the IDistributedCache layer so all cache I/O — including the system-cache
        // exception path — is counted exactly once. R7-S7 sub-gap #2 closure (2026-06-26).
        var bytes = await _cache.GetAsync(key, ct).ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
        {
            return default;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "TenantCache: failed to deserialize cached value for key '{CacheKey}'. Returning default.",
                key);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        T value,
        TimeSpan? ttl = null,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);

        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, ct).ConfigureAwait(false);
        var bytes = stream.ToArray();

        var options = new DistributedCacheEntryOptions();
        if (ttl.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = ttl.Value;
        }

        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        await _cache.SetAsync(key, bytes, options, ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);
        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var produced = await factory(ct).ConfigureAwait(false);
        if (produced is not null)
        {
            await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct).ConfigureAwait(false);
        }

        return produced!;
    }

    public async Task<string?> GetStringAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);
        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        return await _cache.GetStringAsync(key, ct).ConfigureAwait(false);
    }

    public async Task SetStringAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string value,
        TimeSpan? ttl = null,
        TimeSpan? slidingExpiration = null,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);
        ArgumentNullException.ThrowIfNull(value);

        var key = BuildKey(tenantId, resource, id, version);
        var options = new DistributedCacheEntryOptions();
        if (ttl.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = ttl.Value;
        }
        if (slidingExpiration.HasValue)
        {
            options.SlidingExpiration = slidingExpiration.Value;
        }

        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        await _cache.SetStringAsync(key, value, options, ct).ConfigureAwait(false);
    }

    public async Task RefreshAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);
        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        await _cache.RefreshAsync(key, ct).ConfigureAwait(false);
    }

    public async Task SetSlidingAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        T value,
        TimeSpan slidingExpiration,
        string cacheInstance = "default",
        CancellationToken ct = default)
    {
        ValidateArguments(tenantId, resource, id, cacheInstance);

        var key = BuildKey(tenantId, resource, id, version);

        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, ct).ConfigureAwait(false);
        var bytes = stream.ToArray();

        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = slidingExpiration
        };

        // Metrics emitted at IDistributedCache decorator layer (see GetAsync comment).
        await _cache.SetAsync(key, bytes, options, ct).ConfigureAwait(false);
    }

    private static string BuildKey(string tenantId, string resource, string id, int version)
        => $"tenant:{tenantId}:{resource}:{id}:v{version}";

    private static void ValidateArguments(string tenantId, string resource, string id, string cacheInstance)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must be a non-empty string.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource must be a non-empty string.", nameof(resource));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id must be a non-empty string.", nameof(id));
        }

        if (!string.Equals(cacheInstance, DefaultCacheInstance, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Cache instance '{cacheInstance}' is not registered. " +
                "Only 'default' is supported today. See NFR-12 for future multi-instance pattern.");
        }
    }
}
