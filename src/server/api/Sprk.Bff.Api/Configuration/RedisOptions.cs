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
    /// Redis connection string. Required when Enabled is true.
    /// Store in Key Vault (production) or user-secrets (development).
    /// Example: localhost:6379 (dev) or your-redis.redis.cache.windows.net:6380,password=...,ssl=True (prod)
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
