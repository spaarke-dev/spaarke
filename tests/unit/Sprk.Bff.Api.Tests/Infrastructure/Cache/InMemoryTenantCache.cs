using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Tests.Infrastructure.Cache;

/// <summary>
/// Process-local <see cref="ITenantCache"/> stand-in for tests that previously instantiated
/// <c>MemoryDistributedCache</c> directly. Constructs the same FR-05 on-wire key format
/// (<c>tenant:{tenantId}:{resource}:{id}:v{version}</c>) so tests asserting key shape via
/// the legacy <c>BuildCacheKey</c>/<c>BuildPendingPlanKey</c> static helpers continue to
/// match.
/// </summary>
/// <remarks>
/// Provided by task 011 of spaarke-redis-cache-remediation-r1 to unblock the test-side
/// rewrite: every legacy test fixture that took a <c>MemoryDistributedCache</c> or
/// <c>IDistributedCache</c> for the cache slot can be migrated mechanically to
/// <see cref="InMemoryTenantCache"/> without rewriting the test body.
/// </remarks>
public sealed class InMemoryTenantCache : ITenantCache
{
    private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset? AbsoluteExpiry, TimeSpan? Sliding, DateTimeOffset LastAccess)> _store = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string BuildKey(string tenantId, string resource, string id, int version)
        => $"tenant:{tenantId}:{resource}:{id}:v{version}";

    public Task<T?> GetAsync<T>(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        lock (_gate)
        {
            if (!_store.TryGetValue(key, out var entry))
            {
                return Task.FromResult<T?>(default);
            }
            if (IsExpired(entry))
            {
                _store.Remove(key);
                return Task.FromResult<T?>(default);
            }
            entry.LastAccess = DateTimeOffset.UtcNow;
            _store[key] = entry;
            return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Bytes, JsonOptions));
        }
    }

    public Task SetAsync<T>(
        string tenantId, string resource, string id, int version,
        T value, TimeSpan? ttl = null,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        lock (_gate)
        {
            var expiry = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : (DateTimeOffset?)null;
            _store[key] = (bytes, expiry, null, DateTimeOffset.UtcNow);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        lock (_gate)
        {
            _store.Remove(key);
        }
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string tenantId, string resource, string id, int version,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var existing = await GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
        if (existing is not null)
        {
            return existing;
        }
        var produced = await factory(ct);
        if (produced is not null)
        {
            await SetAsync(tenantId, resource, id, version, produced, ttl, cacheInstance, ct);
        }
        return produced!;
    }

    public Task<string?> GetStringAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        lock (_gate)
        {
            if (!_store.TryGetValue(key, out var entry))
            {
                return Task.FromResult<string?>(null);
            }
            if (IsExpired(entry))
            {
                _store.Remove(key);
                return Task.FromResult<string?>(null);
            }
            entry.LastAccess = DateTimeOffset.UtcNow;
            _store[key] = entry;
            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(entry.Bytes));
        }
    }

    public Task SetStringAsync(
        string tenantId, string resource, string id, int version,
        string value, TimeSpan? ttl = null, TimeSpan? slidingExpiration = null,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        lock (_gate)
        {
            var expiry = ttl.HasValue ? DateTimeOffset.UtcNow + ttl.Value : (DateTimeOffset?)null;
            _store[key] = (bytes, expiry, slidingExpiration, DateTimeOffset.UtcNow);
        }
        return Task.CompletedTask;
    }

    public Task RefreshAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        lock (_gate)
        {
            if (_store.TryGetValue(key, out var entry))
            {
                entry.LastAccess = DateTimeOffset.UtcNow;
                _store[key] = entry;
            }
        }
        return Task.CompletedTask;
    }

    public Task SetSlidingAsync<T>(
        string tenantId, string resource, string id, int version,
        T value, TimeSpan slidingExpiration,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        var key = BuildKey(tenantId, resource, id, version);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        lock (_gate)
        {
            _store[key] = (bytes, null, slidingExpiration, DateTimeOffset.UtcNow);
        }
        return Task.CompletedTask;
    }

    private static bool IsExpired((byte[] Bytes, DateTimeOffset? AbsoluteExpiry, TimeSpan? Sliding, DateTimeOffset LastAccess) entry)
    {
        if (entry.AbsoluteExpiry.HasValue && entry.AbsoluteExpiry.Value < DateTimeOffset.UtcNow)
        {
            return true;
        }
        if (entry.Sliding.HasValue && (DateTimeOffset.UtcNow - entry.LastAccess) > entry.Sliding.Value)
        {
            return true;
        }
        return false;
    }
}
