// R3 Part 1 Phase 2 — Task 086 (2026-06-22)
// Redis pub/sub payload for membership cache invalidation.
//
// Per spec FR-2P2.8 + AC-1P2.7: junction-row writes (by either task 084's
// MembershipJunctionUpdater or task 085's MembershipReconciliationJob)
// publish to Redis channel `membership-cache-invalidate` carrying
// `{personId, entityLogicalName}`. All BFF instances subscribed; evict
// matching membership-cache entries.
//
// This record is the payload shape for the channel — JSON-serialized
// (camelCase) so subscribers can deserialize without taking a hard
// dependency on the Sprk.Bff.Api assembly (future Insights Engine or
// other-process consumers can implement the same contract).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md.

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Payload published to the <c>membership-cache-invalidate</c> Redis
/// channel when a <c>sprk_userentityassociation</c> junction row is
/// written (Added / Updated / Removed) by either
/// <see cref="MembershipJunctionUpdater"/> (task 084 — Service Bus path)
/// or <see cref="MembershipReconciliationJob"/> (task 085 — recon path,
/// reuses the same <see cref="IMembershipJunctionUpdater"/> contract).
/// </summary>
/// <param name="PersonId">Identity GUID of the user/contact/team/organization
/// whose membership changed. Matches the <c>sprk_personid</c> column.</param>
/// <param name="EntityLogicalName">Dataverse logical name of the parent
/// entity (e.g., <c>sprk_matter</c>, <c>sprk_document</c>) whose junction
/// row was mutated. Matches the <c>sprk_entitylogicalname</c> column.</param>
/// <param name="PublishedAtUtc">UTC publish timestamp; subscribers MAY
/// use this for staleness diagnostics (NOT for ordering — Redis pub/sub
/// is at-most-once unordered delivery, and the cache TTL is the backstop).</param>
/// <param name="CorrelationId">Optional correlation id propagated from
/// the originating mutation event (or recon-job run). Surfaces in
/// invalidation logs so an operator can trace a junction write through
/// to cache eviction.</param>
public sealed record MembershipCacheInvalidationMessage(
    Guid PersonId,
    string EntityLogicalName,
    DateTime PublishedAtUtc,
    string? CorrelationId);
