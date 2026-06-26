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
}
