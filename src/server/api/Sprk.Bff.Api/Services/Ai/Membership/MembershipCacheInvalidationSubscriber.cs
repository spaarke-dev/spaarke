// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Subscriber-side wiring for the membership-cache-invalidate Redis
// channel (FR-2P2.8 + AC-1P2.7).
//
// Hosted service that:
//   1. On StartAsync: subscribes to the configured Redis channel
//      (default "membership-cache-invalidate") via IConnectionMultiplexer.
//      Handler deserializes MembershipCacheInvalidationMessage payloads
//      and evicts matching MembershipResolverService cache entries by
//      key prefix.
//   2. On StopAsync: unsubscribes cleanly and disposes resources.
//
// Eviction strategy:
//   The cache key prefix is "membership:resolved:{personId:D}:" — see
//   MembershipResolverService.CacheKeyPrefix + BuildCacheKey. The
//   StackExchange.Redis distributed cache prefixes everything with the
//   configured InstanceName (default "sdap:"). To wipe per-user entries
//   for a specific entity type, we SCAN the Redis keyspace for
//   "{instanceName}membership:resolved:{personId:D}:{entityLogicalName}:*"
//   and delete each match. SCAN is O(N) over the keyspace but is the
//   only safe way to remove a prefix-batch on StackExchange.Redis
//   without KEYS (which blocks).
//
// Why a dedicated hosted service (not extending MembershipResolverService):
//   - MembershipResolverService is a Singleton today (no Dispose hook
//     in the .NET container's normal lifecycle). Subscribing + cleanly
//     unsubscribing is a concern of host startup/shutdown, not
//     request-orchestration. Mixing them violates SRP.
//   - The subscriber needs IConnectionMultiplexer (Redis-specific) which
//     the resolver does not — keeping the dependency narrow honors
//     ADR-010.
//   - HostedService gives us proper StartAsync/StopAsync hooks for
//     subscribe + unsubscribe (the .NET container does NOT call Dispose
//     on Singletons until container shutdown, which is too late for
//     graceful Redis cleanup).
//
// Resilience contract:
//   - On message deserialization failure: log Warning + skip (do NOT
//     unsubscribe — one bad payload should not break the channel).
//   - On Redis SCAN/DEL failure during eviction: log Warning + return.
//     The 5-min cache TTL is the backstop.
//   - On unsubscribe failure (during shutdown): log Warning; do not
//     block host shutdown.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md;
//            src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs
//            (subscribe pattern within BFF).

using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Hosted service that subscribes to the
/// <c>membership-cache-invalidate</c> Redis channel and evicts matching
/// <see cref="MembershipResolverService"/> cache entries on each
/// invalidation message. Registered alongside the real
/// <see cref="MembershipCacheInvalidator"/> when Redis + the
/// invalidator feature flag are enabled.
/// </summary>
public sealed class MembershipCacheInvalidationSubscriber : IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;
    private readonly RedisChannel _channel;
    // Format: {InstanceName}tenant:{*}:membership-resolved: — covers ALL tenants in
    // the single-Redis-per-BFF deployment (FR-05 tenant-scoped keys; subscriber
    // evicts cross-tenant in case junction writes affect users in multiple tenants).
    private readonly string _instanceName;
    private readonly ILogger<MembershipCacheInvalidationSubscriber> _logger;
    private ChannelMessageQueue? _messageQueue;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="MembershipCacheInvalidationSubscriber"/> class.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer.</param>
    /// <param name="configuration">Configuration (read to compute the
    /// distributed-cache InstanceName prefix, so eviction keys match what
    /// IDistributedCache writes).</param>
    /// <param name="options">Invalidator options — channel name shared
    /// with the publisher.</param>
    /// <param name="logger">Logger.</param>
    public MembershipCacheInvalidationSubscriber(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        IOptions<MembershipCacheInvalidatorOptions> options,
        ILogger<MembershipCacheInvalidationSubscriber> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _configuration = configuration;
        _logger = logger;

        var channelName = string.IsNullOrWhiteSpace(options.Value.Channel)
            ? MembershipCacheInvalidatorOptions.DefaultChannel
            : options.Value.Channel.Trim();
        _channel = RedisChannel.Literal(channelName);

        // StackExchange.Redis distributed cache prepends InstanceName to
        // every key. Match the CacheModule default ("spaarke:") if unset
        // (spaarke-redis-cache-remediation-r1 FR-07: dropped deprecated "sdap:").
        _instanceName = _configuration["Redis:InstanceName"] ?? "spaarke:";
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subscriber = _redis.GetSubscriber();
            _messageQueue = await subscriber.SubscribeAsync(_channel).ConfigureAwait(false);

            // Fire-and-forget background consume loop. The queue is a
            // StackExchange.Redis ChannelMessageQueue — its enumeration
            // is asynchronous and stops cleanly when Unsubscribe is called.
            _ = Task.Run(ProcessMessagesAsync);

            _logger.LogInformation(
                "MembershipCacheInvalidationSubscriber subscribed to channel '{Channel}' (instanceName='{InstanceName}')",
                _channel.ToString(), _instanceName);
        }
        catch (Exception ex)
        {
            // Subscribe failure → log + continue. The TTL backstop keeps
            // correctness; we do NOT crash the host on Redis errors.
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber failed to subscribe to channel '{Channel}'; cache invalidations will rely on TTL backstop",
                _channel.ToString());
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_messageQueue is null)
        {
            return;
        }

        try
        {
            await _messageQueue.UnsubscribeAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "MembershipCacheInvalidationSubscriber unsubscribed from channel '{Channel}'",
                _channel.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber failed to unsubscribe cleanly from channel '{Channel}'",
                _channel.ToString());
        }
        finally
        {
            _messageQueue = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_messageQueue is not null)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Background consume loop. Reads from the channel queue and
    /// dispatches each message to <see cref="OnMessageAsync"/>. One bad
    /// payload does NOT break the loop.
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        var queue = _messageQueue;
        if (queue is null)
        {
            return;
        }

        try
        {
            await foreach (var message in queue.WithCancellation(CancellationToken.None).ConfigureAwait(false))
            {
                try
                {
                    await OnMessageAsync(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "MembershipCacheInvalidationSubscriber message handler threw — continuing to consume");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber consume loop exited unexpectedly");
        }
    }

    /// <summary>
    /// Handle one channel message: deserialize → evict matching cache
    /// keys via SCAN/DEL on the shared multiplexer.
    /// </summary>
    internal async Task OnMessageAsync(ChannelMessage message)
    {
        var payloadRedisValue = message.Message;
        if (payloadRedisValue.IsNullOrEmpty)
        {
            _logger.LogDebug(
                "MembershipCacheInvalidationSubscriber received empty payload — skipping");
            return;
        }

        var payload = payloadRedisValue.ToString();
        MembershipCacheInvalidationMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<MembershipCacheInvalidationMessage>(payload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber failed to deserialize payload — skipping");
            return;
        }

        if (msg is null || msg.PersonId == Guid.Empty || string.IsNullOrWhiteSpace(msg.EntityLogicalName))
        {
            _logger.LogDebug(
                "MembershipCacheInvalidationSubscriber received malformed payload — skipping (payload={Payload})",
                payload);
            return;
        }

        await EvictAsync(msg).ConfigureAwait(false);
    }

    /// <summary>
    /// Evict cache entries by prefix. After the FR-05 tenant-scoping migration,
    /// the on-wire cache key shape is
    /// <c>{InstanceName}tenant:{tenantId}:membership-resolved:{personId:D}:{entityType}:{optionsHash}:v{version}</c>
    /// (see <see cref="MembershipResolverService.CacheResource"/> + <c>BuildCacheId</c>).
    /// We delete every key matching
    /// <c>{InstanceName}tenant:*:membership-resolved:{personId}:{entityLogicalName}:*</c>
    /// across all tenants — the user may exist in multiple tenants (rare but
    /// possible in B2B/guest scenarios), the optionsHash suffix varies per query
    /// shape, and the schema version suffix varies if multiple BFF versions
    /// share the Redis instance during deploy windows.
    /// </summary>
    private async Task EvictAsync(MembershipCacheInvalidationMessage msg)
    {
        var personIdString = msg.PersonId.ToString("D");
        var entity = msg.EntityLogicalName.Trim().ToLowerInvariant();
        var matchPattern = $"{_instanceName}tenant:*:{MembershipResolverService.CacheResource}:{personIdString}:{entity}:*";

        int deleted = 0;
        try
        {
            var endpoints = _redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    // Run against primary only — replicas reject keyspace
                    // writes. Some endpoints may be unreachable; skip.
                    continue;
                }

                var db = _redis.GetDatabase();
                await foreach (var key in server.KeysAsync(pattern: matchPattern).ConfigureAwait(false))
                {
                    var removed = await db.KeyDeleteAsync(key).ConfigureAwait(false);
                    if (removed)
                    {
                        deleted++;
                    }
                }
            }

            _logger.LogInformation(
                "MembershipCacheInvalidationSubscriber evicted {Count} cache entries for personId={PersonId} entity={EntityLogicalName} (correlationId={CorrelationId})",
                deleted, msg.PersonId, entity, msg.CorrelationId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber Redis connection error during eviction — personId={PersonId} entity={EntityLogicalName} (TTL backstop: stale entries clear within 5 min)",
                msg.PersonId, entity);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MembershipCacheInvalidationSubscriber error during eviction — personId={PersonId} entity={EntityLogicalName} (TTL backstop: stale entries clear within 5 min)",
                msg.PersonId, entity);
        }
    }
}
