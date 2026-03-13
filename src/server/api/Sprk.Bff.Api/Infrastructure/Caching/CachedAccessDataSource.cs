using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.Caching;

/// <summary>
/// Decorator over <see cref="IAccessDataSource"/> that caches authorization DATA in Redis
/// while ensuring authorization DECISIONS are always computed fresh per-request.
///
/// ADR-003: Cache data, NOT decisions. Decisions are computed by AuthorizationService/OperationAccessRule.
/// ADR-009: Redis-first via IDistributedCache; short TTLs for security-sensitive data.
///
/// Cache key scheme:
/// - User roles:       sdap:auth:roles:{userOid}           TTL 2 min
/// - Team memberships: sdap:auth:teams:{userOid}           TTL 2 min
/// - Resource access:  sdap:auth:access:{userOid}:{resId}  TTL 60s
///
/// Performance target: Authorization overhead drops from 50-200ms (Dataverse) to &lt;10ms on cache hit.
/// Security: Fail-open to inner data source on cache errors (cache is optimization, not requirement).
/// </summary>
public class CachedAccessDataSource : IAccessDataSource
{
    private readonly IAccessDataSource _inner;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedAccessDataSource> _logger;
    private readonly CacheMetrics? _metrics;

    /// <summary>TTL for user roles cache (security-sensitive, keep short).</summary>
    private static readonly TimeSpan RolesTtl = TimeSpan.FromMinutes(2);

    /// <summary>TTL for team memberships cache (security-sensitive, keep short).</summary>
    private static readonly TimeSpan TeamsTtl = TimeSpan.FromMinutes(2);

    /// <summary>TTL for per-resource access cache (most sensitive, shortest TTL).</summary>
    private static readonly TimeSpan ResourceAccessTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CachedAccessDataSource(
        IAccessDataSource inner,
        IDistributedCache cache,
        ILogger<CachedAccessDataSource> logger,
        CacheMetrics? metrics = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics; // Optional: metrics can be null if not configured
    }

    /// <inheritdoc />
    public async Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        // Try to get the full access snapshot from resource-level cache first
        var resourceCacheKey = $"sdap:auth:access:{userId}:{resourceId}";
        var sw = Stopwatch.StartNew();

        try
        {
            var cachedJson = await _cache.GetStringAsync(resourceCacheKey, ct);
            sw.Stop();

            if (cachedJson != null)
            {
                var cached = JsonSerializer.Deserialize<CachedAccessSnapshot>(cachedJson, JsonOptions);
                if (cached != null)
                {
                    _logger.LogDebug(
                        "[AUTH-CACHE] HIT resource access: UserId={UserId}, ResourceId={ResourceId}, Latency={LatencyMs}ms",
                        userId, resourceId, sw.ElapsedMilliseconds);
                    _metrics?.RecordHit(sw.Elapsed.TotalMilliseconds, "auth-access");

                    return cached.ToAccessSnapshot();
                }
            }

            _logger.LogDebug(
                "[AUTH-CACHE] MISS resource access: UserId={UserId}, ResourceId={ResourceId}, Latency={LatencyMs}ms",
                userId, resourceId, sw.ElapsedMilliseconds);
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, "auth-access");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[AUTH-CACHE] Error reading resource access cache: UserId={UserId}, ResourceId={ResourceId}. Falling through to Dataverse.",
                userId, resourceId);
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, "auth-access");
        }

        // Cache miss or error - fetch from Dataverse
        var snapshot = await _inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);

        // Cache the result (fire-and-forget style, don't block the response)
        _ = CacheSnapshotAsync(resourceCacheKey, snapshot, ResourceAccessTtl);

        // Also cache roles and teams separately with longer TTL (2 min)
        // These are user-level data reusable across resources
        _ = CacheRolesAsync(userId, snapshot.Roles);
        _ = CacheTeamsAsync(userId, snapshot.TeamMemberships);

        return snapshot;
    }

    /// <summary>
    /// Caches the full access snapshot for a user+resource combination.
    /// </summary>
    private async Task CacheSnapshotAsync(string cacheKey, AccessSnapshot snapshot, TimeSpan ttl)
    {
        try
        {
            var cached = CachedAccessSnapshot.FromAccessSnapshot(snapshot);
            var json = JsonSerializer.Serialize(cached, JsonOptions);

            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                });

            _logger.LogDebug(
                "[AUTH-CACHE] Cached resource access: UserId={UserId}, ResourceId={ResourceId}, TTL={TtlSeconds}s",
                snapshot.UserId, snapshot.ResourceId, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AUTH-CACHE] Error caching resource access for UserId={UserId}, ResourceId={ResourceId}. Non-critical.",
                snapshot.UserId, snapshot.ResourceId);
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <summary>
    /// Caches user roles with 2-min TTL.
    /// </summary>
    private async Task CacheRolesAsync(string userId, IEnumerable<string> roles)
    {
        try
        {
            var cacheKey = $"sdap:auth:roles:{userId}";
            var json = JsonSerializer.Serialize(roles.ToList(), JsonOptions);

            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = RolesTtl
                });

            _logger.LogDebug("[AUTH-CACHE] Cached roles: UserId={UserId}, TTL={TtlSeconds}s",
                userId, RolesTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AUTH-CACHE] Error caching roles for UserId={UserId}. Non-critical.", userId);
        }
    }

    /// <summary>
    /// Caches user team memberships with 2-min TTL.
    /// </summary>
    private async Task CacheTeamsAsync(string userId, IEnumerable<string> teams)
    {
        try
        {
            var cacheKey = $"sdap:auth:teams:{userId}";
            var json = JsonSerializer.Serialize(teams.ToList(), JsonOptions);

            await _cache.SetStringAsync(
                cacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TeamsTtl
                });

            _logger.LogDebug("[AUTH-CACHE] Cached teams: UserId={UserId}, TTL={TtlSeconds}s",
                userId, TeamsTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AUTH-CACHE] Error caching teams for UserId={UserId}. Non-critical.", userId);
        }
    }

    /// <summary>
    /// DTO for serializing AccessSnapshot to/from Redis.
    /// Avoids serializing the full AccessSnapshot which has enum flags and DateTimeOffset.
    /// </summary>
    private sealed class CachedAccessSnapshot
    {
        public string UserId { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public int AccessRightsValue { get; set; }
        public List<string> TeamMemberships { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public DateTimeOffset CachedAt { get; set; }

        public static CachedAccessSnapshot FromAccessSnapshot(AccessSnapshot snapshot)
        {
            return new CachedAccessSnapshot
            {
                UserId = snapshot.UserId,
                ResourceId = snapshot.ResourceId,
                AccessRightsValue = (int)snapshot.AccessRights,
                TeamMemberships = snapshot.TeamMemberships.ToList(),
                Roles = snapshot.Roles.ToList(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }

        public AccessSnapshot ToAccessSnapshot()
        {
            return new AccessSnapshot
            {
                UserId = UserId,
                ResourceId = ResourceId,
                AccessRights = (AccessRights)AccessRightsValue,
                TeamMemberships = TeamMemberships,
                Roles = Roles,
                CachedAt = CachedAt
            };
        }
    }
}
