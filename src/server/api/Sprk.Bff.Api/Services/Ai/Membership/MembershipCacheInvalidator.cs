// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Real publisher implementation for the `membership-cache-invalidate`
// Redis channel (FR-2P2.8 + AC-1P2.7).
//
// Mirrors the convention established by JobStatusService (the canonical
// Redis pub/sub user inside the BFF):
//   - Construct via IConnectionMultiplexer
//   - GetSubscriber() once at construction
//   - RedisChannel.Literal(channelName) for ad-hoc channel naming
//   - PublishAsync wraps RedisConnectionException + generic Exception
//     and logs at Warning — never throws (FR-2P2.8 resilience contract)
//
// unified-access-control-r2 task 132 (defect C12) adds the BFF write-path access-cache evictions
// (InvalidateUserAccessAsync / InvalidateRecordOwnerChangeAsync; InvalidateRecordShareChangeAsync since the batch 4
// integration residual — called, through IRecordShareWriteObserver, by DataverseWebApiService on every share write it
// makes, since round 55). They delete keys in the SHARED Redis directly
// (SCAN + DEL, the same mechanism MembershipCacheInvalidationSubscriber already uses), so every BFF instance sees
// the eviction at once with no pub/sub hop, and they run whether or not the junction channel switch
// (Membership:CacheInvalidator:Enabled) is on. That switch now gates the junction PUBLISH only.
//
// The TTL on the membership cache (2 min since task 132 — see MembershipResolverService)
// is the correctness backstop: if Redis is unavailable, OR if pub/sub
// fails to reach a subscriber, stale entries naturally clear within the
// TTL window. Eviction is the latency optimization, NOT a correctness
// mechanism. See spec FR-2P2.8 commentary + ADR-009.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md;
//            src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs
//            (canonical Redis pub/sub pattern within BFF).

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Default <see cref="IMembershipCacheInvalidator"/>, registered whenever Redis is the distributed cache. Publishes
/// <see cref="MembershipCacheInvalidationMessage"/> payloads to the configured Redis channel (when the channel switch
/// is on) and evicts the BFF's access caches directly on the write paths that change who can see a record (task 132).
/// </summary>
public sealed class MembershipCacheInvalidator : IMembershipCacheInvalidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The Redis glob for "any value" in one key segment.</summary>
    internal const string AnySegment = "*";

    /// <summary>Keys per SCAN round trip during an eviction.</summary>
    private const int ScanPageSize = 1000;

    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;
    private readonly bool _publishEnabled;
    private readonly string _instanceName;
    private readonly ILogger<MembershipCacheInvalidator> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="MembershipCacheInvalidator"/> class.
    /// </summary>
    /// <param name="redis">Required Redis connection multiplexer (the real one — the module registers this class only
    /// when <c>Redis:Enabled</c> is true).</param>
    /// <param name="options">Options binding. Channel name defaults to
    /// <c>membership-cache-invalidate</c> (spec FR-2P2.8); <c>Enabled</c> gates the junction publish only.</param>
    /// <param name="redisOptions">The distributed cache's <c>InstanceName</c> — the prefix
    /// <c>StackExchangeRedisCache</c> puts on every key, and therefore on every eviction pattern (task 132).</param>
    /// <param name="clock">Time provider for <c>PublishedAtUtc</c>.</param>
    /// <param name="logger">Logger.</param>
    public MembershipCacheInvalidator(
        IConnectionMultiplexer redis,
        IOptions<MembershipCacheInvalidatorOptions> options,
        IOptions<RedisOptions> redisOptions,
        TimeProvider clock,
        ILogger<MembershipCacheInvalidator> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(redisOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _subscriber = redis.GetSubscriber();
        var channelName = string.IsNullOrWhiteSpace(options.Value.Channel)
            ? MembershipCacheInvalidatorOptions.DefaultChannel
            : options.Value.Channel.Trim();
        _channel = RedisChannel.Literal(channelName);
        _publishEnabled = options.Value.Enabled;
        _instanceName = redisOptions.Value.InstanceName ?? string.Empty;
        _clock = clock;
        _logger = logger;

        if (_publishEnabled)
        {
            _logger.LogInformation(
                "MembershipCacheInvalidator initialized — junction channel='{Channel}' publishing; BFF write-path access " +
                "eviction ACTIVE (instanceName='{InstanceName}')",
                channelName, _instanceName);
        }
        else
        {
            // ADR-032 tension (task 132): an inert invalidation must be visible. Only the JUNCTION half is inert.
            _logger.LogWarning(
                "MembershipCacheInvalidator: junction pub/sub publication is INERT (Membership:CacheInvalidator:Enabled " +
                "is not true) — sprk_userentityassociation writes evict nothing until the {Ttl} TTL. BFF write-path " +
                "access eviction (team/BU/owner/share changes) is ACTIVE (instanceName='{InstanceName}').",
                MembershipResolverService.CacheTtl, _instanceName);
        }
    }

    /// <inheritdoc />
    public async Task PublishInvalidationAsync(
        Guid personId,
        string entityLogicalName,
        string? correlationId,
        CancellationToken ct)
    {
        if (!_publishEnabled)
        {
            _logger.LogDebug(
                "MembershipCacheInvalidator.PublishInvalidationAsync no-op (channel switch off) — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId}",
                personId, entityLogicalName, correlationId);
            return;
        }

        if (personId == Guid.Empty)
        {
            _logger.LogDebug(
                "MembershipCacheInvalidator.PublishInvalidationAsync called with Guid.Empty personId — skipping (correlationId={CorrelationId})",
                correlationId);
            return;
        }
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            _logger.LogDebug(
                "MembershipCacheInvalidator.PublishInvalidationAsync called with empty entityLogicalName — skipping (personId={PersonId} correlationId={CorrelationId})",
                personId, correlationId);
            return;
        }

        // Honor cancellation as a no-op rather than throw — per the
        // fire-and-forget contract. The caller's HandleAsync path treats
        // the publish as best-effort.
        if (ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                "MembershipCacheInvalidator.PublishInvalidationAsync cancelled before publish (personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId})",
                personId, entityLogicalName, correlationId);
            return;
        }

        var message = new MembershipCacheInvalidationMessage(
            PersonId: personId,
            EntityLogicalName: entityLogicalName.Trim().ToLowerInvariant(),
            PublishedAtUtc: _clock.GetUtcNow().UtcDateTime,
            CorrelationId: correlationId);

        try
        {
            var payload = JsonSerializer.Serialize(message, JsonOptions);
            var subscribers = await _subscriber
                .PublishAsync(_channel, payload)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Published membership cache invalidation — personId={PersonId} entity={EntityLogicalName} subscribers={Subscribers} correlationId={CorrelationId}",
                personId, message.EntityLogicalName, subscribers, correlationId);
        }
        catch (RedisConnectionException ex)
        {
            // Per resilience contract: log + return; the cache TTL
            // is the backstop. Stale entries naturally clear.
            _logger.LogWarning(
                ex,
                "Redis connection error publishing membership cache invalidation — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId} (TTL backstop: stale entries clear within {Ttl})",
                personId, message.EntityLogicalName, correlationId, MembershipResolverService.CacheTtl);
        }
        catch (Exception ex)
        {
            // Catch-all — serialization, channel unavailable, transport
            // exceptions. Same resilience contract.
            _logger.LogWarning(
                ex,
                "Error publishing membership cache invalidation — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId} (TTL backstop: stale entries clear within {Ttl})",
                personId, message.EntityLogicalName, correlationId, MembershipResolverService.CacheTtl);
        }
    }

    /// <inheritdoc />
    public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return EvictAsync(
            UserAccessPatterns(_instanceName, systemUserId),
            $"user {systemUserId:D} (team/business-unit change)",
            correlationId);
    }

    /// <inheritdoc />
    public Task InvalidateRecordOwnerChangeAsync(
        string entityLogicalName,
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct)
    {
        if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(entityLogicalName) || string.IsNullOrWhiteSpace(entitySetName))
        {
            _logger.LogWarning(
                "MembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync called without a record ({EntityLogicalName}/{EntitySet}/{RecordId}) — nothing evicted (correlationId={CorrelationId})",
                entityLogicalName, entitySetName, recordId, correlationId);
            return Task.CompletedTask;
        }

        return EvictAsync(
            RecordOwnerChangePatterns(_instanceName, entityLogicalName, entitySetName, recordId),
            $"{entityLogicalName} {recordId:D} (owner change)",
            correlationId);
    }

    /// <inheritdoc />
    public Task InvalidateRecordShareChangeAsync(
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct)
    {
        if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(entitySetName))
        {
            _logger.LogWarning(
                "MembershipCacheInvalidator.InvalidateRecordShareChangeAsync called without a record ({EntitySet}/{RecordId}) — nothing evicted (correlationId={CorrelationId})",
                entitySetName, recordId, correlationId);
            return Task.CompletedTask;
        }

        return EvictAsync(
            RecordShareChangePatterns(_instanceName, entitySetName, recordId),
            $"{entitySetName} {recordId:D} (share change)",
            correlationId);
    }

    // ── Eviction patterns (task 132) ────────────────────────────────────────────────────────────────────────
    //
    // Every pattern is built by the READERS' own key builders — TenantCache.BuildKey (tenant segment = "*") over each
    // cache's own id composition, prefixed with the InstanceName exactly as StackExchangeRedisCache prefixes it — so a
    // pattern cannot silently drift from the key a read writes. Each targets the CURRENT cache version: an entry under
    // an older version is unreachable by every reader already (the version bump orphans it) and needs no eviction.

    /// <summary>
    /// The on-wire patterns <see cref="InvalidateUserAccessAsync"/> removes: the user's identity entry, every one of the
    /// user's membership-resolution entries (any entity type, any options hash), and every one of the user's
    /// impersonated root-set entries (any root type) — each under every tenant segment.
    /// </summary>
    internal static IReadOnlyList<string> UserAccessPatterns(string? instanceName, Guid systemUserId) => new[]
    {
        TenantCache.OnWire(instanceName, TenantCache.BuildKey(
            TenantCache.AnyTenant,
            IdentityNormalizationService.CacheResource,
            IdentityNormalizationService.CacheId(systemUserId),
            IdentityNormalizationService.CacheVersion)),
        TenantCache.OnWire(instanceName, TenantCache.BuildKey(
            TenantCache.AnyTenant,
            MembershipResolverService.CacheResource,
            MembershipResolverService.ComposeCacheId(
                MembershipResolverService.SystemUserSubject(systemUserId), AnySegment, AnySegment),
            MembershipResolverService.CacheVersion)),
        TenantCache.OnWire(instanceName, TenantCache.BuildKey(
            TenantCache.AnyTenant,
            ImpersonatedRootSetSource.CacheResource,
            ImpersonatedRootSetSource.CacheId(systemUserId.ToString("D"), AnySegment),
            ImpersonatedRootSetSource.CacheVersion)),
    };

    /// <summary>
    /// The on-wire patterns <see cref="InvalidateRecordOwnerChangeAsync"/> removes: every user's membership-resolution
    /// entries for the entity type, every user's impersonated root-set entry for it, and every user's access snapshots
    /// of the record — each under every tenant segment, and each ONLY when its cache can hold that type (task 132
    /// integration residual: a pattern for a type no reader caches would scan the whole key space for nothing). A table
    /// an Assign cascade re-owns (<c>sharepointdocumentlocation</c> / <c>sharepointdocument</c>) gets no pattern at all.
    /// </summary>
    internal static IReadOnlyList<string> RecordOwnerChangePatterns(
        string? instanceName, string entityLogicalName, string entitySetName, Guid recordId)
    {
        // The membership reader lower-cases its entity segment (normalizedEntity); the root-set reader's binding keys
        // are the lower-case logical names. Normalising here keeps a caller's casing from producing a pattern that
        // matches nothing.
        var entity = entityLogicalName.Trim().ToLowerInvariant();

        var patterns = new List<string>();
        if (MembershipResolverService.CachesEntityType(entity))
        {
            patterns.Add(TenantCache.OnWire(instanceName, TenantCache.BuildKey(
                TenantCache.AnyTenant,
                MembershipResolverService.CacheResource,
                MembershipResolverService.ComposeCacheId(AnySegment, entity, AnySegment),
                MembershipResolverService.CacheVersion)));
        }

        if (ImpersonatedRootSetSource.CachesEntityType(entity))
        {
            patterns.Add(RootSetPattern(instanceName, entity));
        }

        patterns.AddRange(RecordSnapshotPatterns(instanceName, entitySetName, recordId));
        return patterns;
    }

    /// <summary>
    /// The on-wire patterns <see cref="InvalidateRecordShareChangeAsync"/> removes: every user's impersonated root-set
    /// entry for the record's root type (when the set is a root's), and every user's access snapshots of the record —
    /// each under every tenant segment, and each only when its cache can hold that set. No membership pattern: a share is
    /// not a membership term.
    /// </summary>
    internal static IReadOnlyList<string> RecordShareChangePatterns(string? instanceName, string entitySetName, Guid recordId)
    {
        var patterns = new List<string>();
        if (ImpersonatedRootSetSource.TryGetEntityTypeForSet(entitySetName, out var rootType))
        {
            patterns.Add(RootSetPattern(instanceName, rootType));
        }

        patterns.AddRange(RecordSnapshotPatterns(instanceName, entitySetName, recordId));
        return patterns;
    }

    /// <summary>Every user's impersonated root-set entry for one root type.</summary>
    private static string RootSetPattern(string? instanceName, string rootType) =>
        TenantCache.OnWire(instanceName, TenantCache.BuildKey(
            TenantCache.AnyTenant,
            ImpersonatedRootSetSource.CacheResource,
            ImpersonatedRootSetSource.CacheId(AnySegment, rootType),
            ImpersonatedRootSetSource.CacheVersion));

    /// <summary>
    /// Every user's access snapshots of one record: the record-scoped snapshot (when the decorator caches the set), and —
    /// for a <c>sprk_documents</c> record — the document-scoped snapshot, whose key carries no set and either auth mode.
    /// </summary>
    private static IEnumerable<string> RecordSnapshotPatterns(string? instanceName, string entitySetName, Guid recordId)
    {
        var set = entitySetName.Trim();
        if (CachedAccessDataSource.CachesRecordEntitySet(set))
        {
            yield return TenantCache.OnWire(instanceName, TenantCache.BuildKey(
                TenantCache.AnyTenant,
                CachedAccessDataSource.RecordAccessResource,
                CachedAccessDataSource.RecordAccessCacheId(set, AnySegment, CachedAccessDataSource.RecordIdSegment(recordId)),
                CachedAccessDataSource.CacheVersion));
        }

        if (CachedAccessDataSource.IsDocumentEntitySet(set))
        {
            yield return TenantCache.OnWire(instanceName, TenantCache.BuildKey(
                TenantCache.AnyTenant,
                CachedAccessDataSource.DocumentAccessResource,
                CachedAccessDataSource.DocumentAccessCacheId(
                    AnySegment, AnySegment, CachedAccessDataSource.DocumentIdSegment(recordId.ToString("D"))),
                CachedAccessDataSource.CacheVersion));
        }
    }

    /// <summary>
    /// Deletes every key matching each pattern on every primary endpoint. Never throws: one pattern's failure is logged
    /// and the next pattern still runs; the TTLs are the backstop. Cancellation is deliberately not honoured — the write
    /// that called this has already happened.
    /// </summary>
    private async Task EvictAsync(IReadOnlyList<string> patterns, string subject, string? correlationId)
    {
        if (patterns.Count == 0)
        {
            // No cache can hold an entry for this record's type (task 132 integration residual) — nothing to scan.
            _logger.LogDebug(
                "[ACCESS-EVICT] Nothing to evict for {Subject}: no access cache holds that type (correlationId={CorrelationId})",
                subject, correlationId);
            return;
        }

        var deleted = 0;
        var failed = 0;

        EndPoint[] endpoints;
        try
        {
            endpoints = _redis.GetEndPoints();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ACCESS-EVICT] Could not enumerate Redis endpoints to evict {Subject}; entries lapse on their TTLs " +
                "(identity/membership/root sets {Ttl}, snapshots {SnapshotTtl}) (correlationId={CorrelationId})",
                subject, MembershipResolverService.CacheTtl, CachedAccessDataSource.ResourceAccessTtl, correlationId);
            return;
        }

        foreach (var pattern in patterns)
        {
            try
            {
                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected || server.IsReplica)
                    {
                        // Primary only — replicas reject keyspace writes; an unreachable endpoint is skipped.
                        continue;
                    }

                    var db = _redis.GetDatabase();
                    // SCAN in pages of 1,000 (the library default is 250): a larger keyspace costs a quarter of the
                    // round trips, and this runs only on rare team / BU / re-own writes.
                    await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: ScanPageSize).ConfigureAwait(false))
                    {
                        if (await db.KeyDeleteAsync(key).ConfigureAwait(false))
                        {
                            deleted++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "[ACCESS-EVICT] Eviction of pattern {Pattern} for {Subject} FAILED; matching entries lapse on their TTL " +
                    "(correlationId={CorrelationId})",
                    pattern, subject, correlationId);
            }
        }

        _logger.LogInformation(
            "[ACCESS-EVICT] Evicted {Count} cache entries for {Subject} across {Patterns} pattern(s), {Failed} failed " +
            "(correlationId={CorrelationId})",
            deleted, subject, patterns.Count, failed, correlationId);
    }
}
