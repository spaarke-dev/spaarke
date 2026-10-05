using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Redis distributed cache.
/// Falls back to in-memory cache when disabled.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Enable Redis caching. When false, uses in-memory cache.
    /// Recommended: false for dev, true for staging/production.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Azure Managed Redis endpoint, <c>host:port</c> (e.g. <c>sprk-acme-prod-redis.westus2.redis.azure.net:10000</c>).
    /// When set, the BFF authenticates ONLY with its user-assigned managed identity (Microsoft Entra; the cache has
    /// access keys disabled) over RESP3, and any connection string is ignored. Required whenever Redis is enabled
    /// outside Development/Testing (task 242, owner D12/D13). Not a secret — a plain app setting, never a Key Vault
    /// reference.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Redis connection string — Development and Testing only (e.g. <c>localhost:6379</c>), and only when
    /// <see cref="Endpoint"/> is not set. Outside Development/Testing the BFF refuses to start on a connection string
    /// without an endpoint (task 242: deployed environments are keyless).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Redis instance name prefix for cache keys.
    /// Default: "spaarke:"
    /// </summary>
    public string InstanceName { get; set; } = "spaarke:";

    /// <summary>
    /// Opt-in to in-memory cache fallback when <see cref="Enabled"/> is false.
    /// Defaults to <c>false</c> so that any deployed environment without explicit opt-in
    /// fails fast at startup rather than silently degrading to a non-distributed cache.
    /// Only honored in the Development environment when <see cref="Enabled"/> is false.
    /// In deployed environments (Staging/Production) the CacheModule throws at startup
    /// regardless of this value when <see cref="Enabled"/> is false (env-guard behavior).
    /// Consumed by the CacheModule 4-branch selection logic (FR-03, ADR-009 amendment).
    /// </summary>
    public bool AllowInMemoryFallback { get; set; } = false;
}
