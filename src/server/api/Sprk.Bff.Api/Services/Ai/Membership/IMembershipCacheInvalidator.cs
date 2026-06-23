// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// IMembershipCacheInvalidator — fire-and-forget publish to the
// `membership-cache-invalidate` Redis channel after a junction-row write.
//
// Per spec FR-2P2.8 + AC-1P2.7: when a junction row is created / updated /
// deleted (by either task 084's MembershipJunctionUpdater on the SB
// consumer path OR task 085's MembershipReconciliationJob on the recon
// path), we publish an invalidation message so every BFF instance
// subscribed to the channel evicts its in-memory + Redis membership
// cache entries for that (personId, entityLogicalName) tuple.
//
// Why an interface (ADR-010 testing seam): the publisher and the
// real-vs-null peer are swapped at registration time per ADR-032
// (Null-Object Kill-Switch Pattern) — the consumer side
// (MembershipJunctionUpdater) injects this interface and never branches
// on the kill-switch state. ALL endpoints + handlers receive a
// non-null IMembershipCacheInvalidator regardless of Redis state.
//
// Resilience contract (per .claude/patterns/api/resilience.md +
// bff-extensions.md §A): PublishInvalidationAsync MUST NOT throw.
// Redis failures, serialization failures, channel unavailability — all
// caught + logged at Warning. The cache TTL is the backstop: stale
// entries clear within 5 min naturally; the pub/sub channel is an
// optimization, NOT a correctness mechanism.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md;
//            .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            .claude/patterns/api/resilience.md.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Fire-and-forget publisher for membership cache invalidation messages.
/// Consumed by <see cref="MembershipJunctionUpdater"/> (task 084) after a
/// successful junction-row write — the recon job (task 085) reuses the
/// same handler so it gets invalidation for free.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resilience contract</b>: implementations MUST NOT throw. Redis
/// connectivity errors, serialization failures, and "no subscribers"
/// states are all caught + logged at Warning and return normally. The
/// 5-min cache TTL on <see cref="MembershipResolverService.CacheTtl"/>
/// is the correctness backstop — pub/sub is the latency optimization.
/// </para>
/// <para>
/// <b>Symmetric registration (ADR-032)</b>: exactly one implementation
/// is always bound to this interface. The real implementation
/// (<see cref="MembershipCacheInvalidator"/>) publishes via
/// <c>IConnectionMultiplexer</c>; the Null peer
/// (<see cref="NullMembershipCacheInvalidator"/>) logs once + returns.
/// Endpoints unconditionally inject <c>IMembershipCacheInvalidator</c>;
/// minimal-API param inference resolves cleanly in every config state.
/// </para>
/// </remarks>
public interface IMembershipCacheInvalidator
{
    /// <summary>
    /// Publish a <see cref="MembershipCacheInvalidationMessage"/> to the
    /// configured Redis channel. Fire-and-forget — the method completes
    /// once the publish is dispatched (or fails silently if Redis is
    /// unavailable). Subscribers across BFF instances evict any cached
    /// membership entries whose key prefix matches
    /// <paramref name="personId"/> + <paramref name="entityLogicalName"/>.
    /// </summary>
    /// <param name="personId">Identity GUID whose membership cache entries
    /// should be evicted (matches the <c>sprk_personid</c> column on
    /// the junction row that was written).</param>
    /// <param name="entityLogicalName">Dataverse logical name of the
    /// parent entity whose junction row was mutated.</param>
    /// <param name="correlationId">Optional correlation id propagated to
    /// the published payload + log lines for distributed tracing.</param>
    /// <param name="ct">Cancellation token. Honored opportunistically;
    /// publish failures + cancellation do NOT throw (per resilience
    /// contract).</param>
    Task PublishInvalidationAsync(
        Guid personId,
        string entityLogicalName,
        string? correlationId,
        CancellationToken ct);
}
