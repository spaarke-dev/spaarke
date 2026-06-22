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
// The TTL on the membership cache (5 min — see MembershipResolverService)
// is the correctness backstop: if Redis is unavailable, OR if pub/sub
// fails to reach a subscriber, stale entries naturally clear within the
// TTL window. Pub/sub is the latency optimization, NOT a correctness
// mechanism. See spec FR-2P2.8 commentary + ADR-009.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md;
//            src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs
//            (canonical Redis pub/sub pattern within BFF).

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Default <see cref="IMembershipCacheInvalidator"/>. Publishes
/// <see cref="MembershipCacheInvalidationMessage"/> payloads to the
/// configured Redis channel via the shared
/// <see cref="IConnectionMultiplexer"/> (registered by
/// <c>CacheModule</c> when <c>Redis:Enabled=true</c>).
/// </summary>
public sealed class MembershipCacheInvalidator : IMembershipCacheInvalidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISubscriber _subscriber;
    private readonly RedisChannel _channel;
    private readonly ILogger<MembershipCacheInvalidator> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="MembershipCacheInvalidator"/> class.
    /// </summary>
    /// <param name="redis">Required Redis connection multiplexer. The DI
    /// module only registers this concrete when <c>IConnectionMultiplexer</c>
    /// is resolvable + <c>Membership:CacheInvalidator:Enabled=true</c>;
    /// the Null peer wins otherwise.</param>
    /// <param name="options">Options binding. Channel name defaults to
    /// <c>membership-cache-invalidate</c> (spec FR-2P2.8).</param>
    /// <param name="clock">Time provider for <c>PublishedAtUtc</c>.</param>
    /// <param name="logger">Logger.</param>
    public MembershipCacheInvalidator(
        IConnectionMultiplexer redis,
        IOptions<MembershipCacheInvalidatorOptions> options,
        TimeProvider clock,
        ILogger<MembershipCacheInvalidator> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _subscriber = redis.GetSubscriber();
        var channelName = string.IsNullOrWhiteSpace(options.Value.Channel)
            ? MembershipCacheInvalidatorOptions.DefaultChannel
            : options.Value.Channel.Trim();
        _channel = RedisChannel.Literal(channelName);
        _clock = clock;
        _logger = logger;

        _logger.LogInformation(
            "MembershipCacheInvalidator initialized — channel='{Channel}'",
            channelName);
    }

    /// <inheritdoc />
    public async Task PublishInvalidationAsync(
        Guid personId,
        string entityLogicalName,
        string? correlationId,
        CancellationToken ct)
    {
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
            // Per resilience contract: log + return; the 5-min cache TTL
            // is the backstop. Stale entries naturally clear.
            _logger.LogWarning(
                ex,
                "Redis connection error publishing membership cache invalidation — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId} (TTL backstop: stale entries clear within 5 min)",
                personId, message.EntityLogicalName, correlationId);
        }
        catch (Exception ex)
        {
            // Catch-all — serialization, channel unavailable, transport
            // exceptions. Same resilience contract.
            _logger.LogWarning(
                ex,
                "Error publishing membership cache invalidation — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId} (TTL backstop: stale entries clear within 5 min)",
                personId, message.EntityLogicalName, correlationId);
        }
    }
}
