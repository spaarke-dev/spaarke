// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Null-Object peer for IMembershipCacheInvalidator (ADR-032 P2 Quiet no-op).
//
// Registered when Redis is NOT the distributed cache (Redis:Enabled=false — the in-memory cache, which CacheModule
// permits only in Development and Testing). unified-access-control-r2 task 132 changed the selection: it used to be
// "flag off OR no multiplexer", but CacheModule always registers an IConnectionMultiplexer (real or
// NullConnectionMultiplexer) and runs AFTER MembershipModule, so the old "is a multiplexer registered?" check was
// always false at registration time and this peer was the only one ever registered — in every deployed environment.
//
// In that state there is nothing a pattern eviction could reach (an in-memory cache cannot be scanned), so every
// member is a no-op and the constructor says so once. The consumer side (MembershipJunctionUpdater, the registration
// service, the provisioning endpoints) unconditionally injects IMembershipCacheInvalidator and calls it.
//
// Per the 2-min cache TTLs (task 132) on MembershipResolverService and IdentityNormalizationService, missing
// invalidation is non-fatal: stale entries naturally clear within the TTL window.
//
// Reference: .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Null-Object peer for <see cref="IMembershipCacheInvalidator"/>. Logs
/// once at construction; returns <see cref="Task.CompletedTask"/> from every member. Registered only when Redis is
/// not the distributed cache (in-memory — Development/Testing).
/// </summary>
public sealed class NullMembershipCacheInvalidator : IMembershipCacheInvalidator
{
    private readonly ILogger<NullMembershipCacheInvalidator> _logger;

    public NullMembershipCacheInvalidator(ILogger<NullMembershipCacheInvalidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning(
            "NullMembershipCacheInvalidator active — EVERY membership / access cache invalidation is a no-op " +
            "(junction pub/sub AND the BFF write-path team/BU/owner/share evictions, task 132): the distributed cache is " +
            "in-memory (Redis:Enabled=false), which cannot be scanned. TTL backstop: identity/membership/root-set " +
            "entries clear within {Ttl}. Set Redis:Enabled=true to activate eviction.",
            MembershipResolverService.CacheTtl);
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

    /// <inheritdoc />
    public Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct)
    {
        _logger.LogDebug(
            "NullMembershipCacheInvalidator.InvalidateUserAccessAsync no-op — systemUserId={SystemUserId} correlationId={CorrelationId} (TTL backstop active)",
            systemUserId, correlationId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateRecordOwnerChangeAsync(
        string entityLogicalName,
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "NullMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync no-op — {EntityLogicalName} {RecordId} correlationId={CorrelationId} (TTL backstop active)",
            entityLogicalName, recordId, correlationId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InvalidateRecordShareChangeAsync(
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "NullMembershipCacheInvalidator.InvalidateRecordShareChangeAsync no-op — {EntitySet} {RecordId} correlationId={CorrelationId} (TTL backstop active)",
            entitySetName, recordId, correlationId);
        return Task.CompletedTask;
    }
}
