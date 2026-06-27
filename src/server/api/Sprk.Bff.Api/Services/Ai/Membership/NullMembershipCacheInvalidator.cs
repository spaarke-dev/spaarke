// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Null-Object peer for IMembershipCacheInvalidator (ADR-032 P2 Quiet no-op).
//
// Registered when EITHER (a) Membership:CacheInvalidator:Enabled=false
// (default) OR (b) IConnectionMultiplexer is not resolvable from the
// container (e.g., Redis:Enabled=false in local dev / CI). In either
// state the consumer side (MembershipJunctionUpdater) unconditionally
// injects IMembershipCacheInvalidator and calls PublishInvalidationAsync;
// the Null peer logs once at construction (so operators can confirm the
// kill-switch state from logs) and returns CompletedTask on every call.
//
// Per the 5-min cache TTL on MembershipResolverService, missing
// invalidation is non-fatal: stale entries naturally clear within the
// TTL window. The pub/sub channel is a latency optimization, NOT a
// correctness mechanism (see FR-2P2.8 commentary).
//
// Reference: .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Null-Object peer for <see cref="IMembershipCacheInvalidator"/>. Logs
/// once at construction; returns <see cref="Task.CompletedTask"/> on every
/// <see cref="PublishInvalidationAsync"/> call. Registered when the
/// invalidator is disabled OR when Redis is not configured.
/// </summary>
public sealed class NullMembershipCacheInvalidator : IMembershipCacheInvalidator
{
    private readonly ILogger<NullMembershipCacheInvalidator> _logger;

    public NullMembershipCacheInvalidator(ILogger<NullMembershipCacheInvalidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation(
            "NullMembershipCacheInvalidator active — membership cache invalidations are no-ops. " +
            "TTL backstop: stale entries clear within 5 min (MembershipResolverService.CacheTtl). " +
            "Set Membership:CacheInvalidator:Enabled=true (with Redis enabled) to activate pub/sub eviction.");
    }

    /// <inheritdoc />
    public Task PublishInvalidationAsync(
        Guid personId,
        string entityLogicalName,
        string? correlationId,
        CancellationToken ct)
    {
        // Per ADR-032 P2 Quiet pattern: no-op return. Debug-level log only —
        // production environments with disabled invalidator should not be
        // spammed with one entry per junction write.
        _logger.LogDebug(
            "NullMembershipCacheInvalidator.PublishInvalidationAsync no-op — personId={PersonId} entity={EntityLogicalName} correlationId={CorrelationId} (TTL backstop active)",
            personId, entityLogicalName, correlationId);
        return Task.CompletedTask;
    }
}
