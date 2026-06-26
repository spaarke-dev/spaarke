namespace Sprk.Bff.Api.Infrastructure.Cache;

/// <summary>
/// Tenant-scoped distributed cache wrapper (ADR-009 amended; FR-05).
/// </summary>
/// <remarks>
/// <para>
/// Justified per CLAUDE.md §11: (1) Existing — none (no tenant-scoping abstraction).
/// (2) Extension — not viable on static helpers. (3) Cost-of-doing-nothing —
/// read-old/write-new bug class + no central metrics seam + no future multi-Redis seam.
/// Satisfies ADR-010 ≥2-impls test via default today + future named instances per NFR-12.
/// </para>
/// <para>
/// Every cache key is mandatorily tenant-scoped. Internal key format produced by the
/// implementation is <c>tenant:{tenantId}:{resource}:{id}:v{version}</c>; the configured
/// <c>InstanceName</c> (currently <c>spaarke:</c>) is prepended by
/// <c>StackExchangeRedisCache</c>, yielding the on-wire key
/// <c>spaarke:tenant:{tenantId}:{resource}:{id}:v{version}</c>.
/// </para>
/// <para>
/// The <c>cacheInstance</c> parameter is the future-extensibility seam for multi-Redis
/// (NFR-12). Only <c>"default"</c> is registered today; other values throw
/// <see cref="NotSupportedException"/>.
/// </para>
/// </remarks>
public interface ITenantCache
{
    /// <summary>
    /// Gets a cached value by tenant-scoped key.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type (e.g., <c>"session"</c>, <c>"token"</c>). Required; must be non-empty.</param>
    /// <param name="id">Resource identifier within the tenant. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload (key versioning per ADR-009).</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized value, or <c>default</c> when the key is absent.</returns>
    Task<T?> GetAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default);

    /// <summary>
    /// Sets a tenant-scoped cached value.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="value">Value to cache. JSON-serialized via <c>System.Text.Json</c>.</param>
    /// <param name="ttl">Optional absolute expiration; when null, the provider's default expiration applies.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        T value,
        TimeSpan? ttl = null,
        string cacheInstance = "default",
        CancellationToken ct = default);

    /// <summary>
    /// Removes a tenant-scoped cached value.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default);

    /// <summary>
    /// Gets a cached value or produces and stores it via the supplied factory if absent.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="factory">Factory invoked on cache miss to produce the value.</param>
    /// <param name="ttl">Optional absolute expiration for newly produced entries; when null, the provider's default expiration applies.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached or newly produced value.</returns>
    Task<T> GetOrCreateAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        string cacheInstance = "default",
        CancellationToken ct = default);

    /// <summary>
    /// Gets a cached UTF-8 string value by tenant-scoped key. Direct overlay over
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache.GetStringAsync"/>
    /// used by call sites that already serialize/deserialize their own payloads (e.g., JSON
    /// or opaque tokens). Returns <c>null</c> when the key is absent.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> GetStringAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "GetStringAsync is not implemented by this ITenantCache adapter. " +
            "Default interface method provided so legacy test doubles compile; production " +
            "TenantCache (the canonical implementation) overrides it. Override in your fake " +
            "if your test exercises the string-overlay path.");

    /// <summary>
    /// Sets a tenant-scoped UTF-8 string value. Direct overlay over
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache.SetStringAsync"/>
    /// for sites that pre-serialize their payload.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="value">String value to store. Required.</param>
    /// <param name="ttl">Optional absolute expiration; when null, the provider's default expiration applies.</param>
    /// <param name="slidingExpiration">Optional sliding expiration; when null, no sliding refresh applies.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetStringAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string value,
        TimeSpan? ttl = null,
        TimeSpan? slidingExpiration = null,
        string cacheInstance = "default",
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "SetStringAsync is not implemented by this ITenantCache adapter. " +
            "Production TenantCache overrides it; override in test doubles when needed.");

    /// <summary>
    /// Refreshes a tenant-scoped sliding-TTL entry without reading or returning its value.
    /// Direct overlay over <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache.RefreshAsync"/>
    /// used by sliding-TTL session caches that want to extend the entry's lifetime on access
    /// without paying the deserialization cost.
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshAsync(
        string tenantId,
        string resource,
        string id,
        int version,
        string cacheInstance = "default",
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "RefreshAsync is not implemented by this ITenantCache adapter. " +
            "Production TenantCache overrides it; override in test doubles when needed.");

    /// <summary>
    /// Variant of <see cref="SetAsync{T}(string,string,string,int,T,TimeSpan?,string,CancellationToken)"/> that
    /// applies a sliding expiration instead of an absolute one. Used by call sites that
    /// previously set <c>SlidingExpiration</c> on <c>DistributedCacheEntryOptions</c> (e.g.,
    /// 24-hour idle session caches per NFR-07).
    /// </summary>
    /// <param name="tenantId">Tenant ID. Required; must be non-empty.</param>
    /// <param name="resource">Resource type. Required; must be non-empty.</param>
    /// <param name="id">Resource identifier. Required; must be non-empty.</param>
    /// <param name="version">Schema version of the cached payload.</param>
    /// <param name="value">Value to cache. JSON-serialized via <c>System.Text.Json</c>.</param>
    /// <param name="slidingExpiration">Sliding TTL applied on every access. Required when calling this overload.</param>
    /// <param name="cacheInstance">Reserved for multi-instance routing per NFR-12. Only <c>"default"</c> is registered today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSlidingAsync<T>(
        string tenantId,
        string resource,
        string id,
        int version,
        T value,
        TimeSpan slidingExpiration,
        string cacheInstance = "default",
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "SetSlidingAsync is not implemented by this ITenantCache adapter. " +
            "Production TenantCache overrides it; override in test doubles when needed.");
}
