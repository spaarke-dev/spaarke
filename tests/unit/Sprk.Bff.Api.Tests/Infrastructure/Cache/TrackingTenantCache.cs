using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Tests.Infrastructure.Cache;

/// <summary>
/// Tracking decorator over <see cref="InMemoryTenantCache"/> that records call counts
/// and the last (tenantId, resource, id) tuple for each operation. Optionally throws on
/// any of <c>GetAsync</c>/<c>SetAsync</c>/<c>RemoveAsync</c> to simulate Redis outages.
/// </summary>
/// <remarks>
/// Created by the §F.2 test-update rollup so post-Wave-6 tests can assert "the cache was
/// called" without leaning on Moq's generic-method limitations against <see cref="ITenantCache"/>.
/// </remarks>
public sealed class TrackingTenantCache : ITenantCache
{
    private readonly InMemoryTenantCache _inner = new();

    public int GetCount { get; set; }
    public int SetCount { get; set; }
    public int RemoveCount { get; set; }
    public string? LastTenantId { get; private set; }
    public string? LastResource { get; private set; }
    public string? LastId { get; private set; }
    public TimeSpan? LastSetTtl { get; private set; }

    public Exception? GetThrows { get; set; }
    public Exception? SetThrows { get; set; }
    public Exception? RemoveThrows { get; set; }

    public Task<T?> GetAsync<T>(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        GetCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id;
        if (GetThrows is not null) throw GetThrows;
        return _inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
    }

    public Task SetAsync<T>(
        string tenantId, string resource, string id, int version,
        T value, TimeSpan? ttl = null,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        SetCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id; LastSetTtl = ttl;
        if (SetThrows is not null) throw SetThrows;
        return _inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
    }

    public Task RemoveAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        RemoveCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id;
        if (RemoveThrows is not null) throw RemoveThrows;
        return _inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);
    }

    public Task<T> GetOrCreateAsync<T>(
        string tenantId, string resource, string id, int version,
        Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
        string cacheInstance = "default", CancellationToken ct = default)
        => _inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);

    public Task<string?> GetStringAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        GetCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id;
        if (GetThrows is not null) throw GetThrows;
        return _inner.GetStringAsync(tenantId, resource, id, version, cacheInstance, ct);
    }

    public Task SetStringAsync(
        string tenantId, string resource, string id, int version,
        string value, TimeSpan? ttl = null, TimeSpan? slidingExpiration = null,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        SetCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id; LastSetTtl = ttl;
        if (SetThrows is not null) throw SetThrows;
        return _inner.SetStringAsync(tenantId, resource, id, version, value, ttl, slidingExpiration, cacheInstance, ct);
    }

    public Task RefreshAsync(
        string tenantId, string resource, string id, int version,
        string cacheInstance = "default", CancellationToken ct = default)
        => _inner.RefreshAsync(tenantId, resource, id, version, cacheInstance, ct);

    public Task SetSlidingAsync<T>(
        string tenantId, string resource, string id, int version,
        T value, TimeSpan slidingExpiration,
        string cacheInstance = "default", CancellationToken ct = default)
    {
        SetCount++;
        LastTenantId = tenantId; LastResource = resource; LastId = id;
        if (SetThrows is not null) throw SetThrows;
        return _inner.SetSlidingAsync(tenantId, resource, id, version, value, slidingExpiration, cacheInstance, ct);
    }
}
