using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

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
    /// Default: "sdap:"
    /// </summary>
    public string InstanceName { get; set; } = "sdap:";
}
