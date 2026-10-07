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
// unified-access-control-r2 task 132 (defect C12) EXTENDS this interface with the two access-cache evictions the
// BFF's own write paths need — per user (a team or business-unit change) and per re-owned record (an owner change).
// They are not pub/sub: they delete the affected keys in the shared Redis directly, so they do not depend on the
// Membership:CacheInvalidator:Enabled channel switch, and they work with no HttpContext (every tenant segment).
//
// Why an interface (ADR-010 testing seam): the publisher and the
// real-vs-null peer are swapped at registration time per ADR-032
// (Null-Object Kill-Switch Pattern) — the consumer side
// (MembershipJunctionUpdater) injects this interface and never branches
// on the kill-switch state. ALL endpoints + handlers receive a
// non-null IMembershipCacheInvalidator regardless of Redis state.
//
// Resilience contract (per .claude/patterns/api/resilience.md +
// bff-extensions.md §A): no member MUST throw.
// Redis failures, serialization failures, channel unavailability — all
// caught + logged at Warning. The cache TTL is the backstop: stale
// entries clear within 2 min naturally (task 132; was 5); eviction is the
// latency optimization, NOT a correctness mechanism.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.8 +
//            AC-1P2.7; docs/adr/ADR-009-redis-caching.md;
//            .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            .claude/patterns/api/resilience.md.

using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Membership / access cache invalidation: the junction pub/sub publisher (task 086) and — since
/// unified-access-control-r2 task 132 — the ONE hook every BFF writer that changes who can see a record must call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resilience contract</b>: implementations MUST NOT throw. Redis
/// connectivity errors, serialization failures, and "no subscribers"
/// states are all caught + logged at Warning and return normally. The
/// 2-min cache TTLs (<see cref="MembershipResolverService.CacheTtl"/>,
/// <see cref="IdentityNormalizationService.CacheTtl"/>) are the correctness
/// backstop — eviction is the latency optimization. A failed eviction never fails the write that called it.
/// </para>
/// <para>
/// <b>Symmetric registration (ADR-032)</b>: exactly one implementation
/// is always bound to this interface. The real implementation
/// (<see cref="MembershipCacheInvalidator"/>) is registered whenever Redis is the distributed cache; the Null peer
/// (<see cref="NullMembershipCacheInvalidator"/>, in-memory cache — Development/Testing only) logs once that every
/// invalidation is inert + returns. Endpoints unconditionally inject <c>IMembershipCacheInvalidator</c>;
/// minimal-API param inference resolves cleanly in every config state.
/// </para>
/// <para>
/// <b>It is also the share-write observer</b> (task 132, main-session round 55): the BFF registers this interface, real or
/// Null, as the <see cref="IRecordShareWriteObserver"/> that <see cref="DataverseWebApiService"/> notifies after every POA
/// share write it makes; the default member at the end forwards each notification to
/// <see cref="InvalidateRecordShareChangeAsync"/>. That is the ONE share-write eviction path.
/// </para>
/// </remarks>
public interface IMembershipCacheInvalidator : IRecordShareWriteObserver
{
    /// <summary>
    /// Publish a <see cref="MembershipCacheInvalidationMessage"/> to the
    /// configured Redis channel. Fire-and-forget — the method completes
    /// once the publish is dispatched (or fails silently if Redis is
    /// unavailable). Subscribers across BFF instances evict any cached
    /// membership entries whose key prefix matches
    /// <paramref name="personId"/> + <paramref name="entityLogicalName"/>.
    /// Inert unless <c>Membership:CacheInvalidator:Enabled</c> is true (the channel switch).
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

    /// <summary>
    /// 🔴 <b>THE hook every BFF writer that changes a user's TEAM membership or BUSINESS UNIT must call</b>, after the
    /// write succeeds (unified-access-control-r2 task 132 · defect C12). Evicts, under EVERY tenant segment, that
    /// user's identity entry, every membership-resolution entry, and every impersonated root-set entry — so the
    /// user's next request re-reads teams, business unit and the records they confer instead of waiting out the TTLs.
    /// </summary>
    /// <remarks>
    /// <para><b>Who calls it today</b>: <c>RegistrationDataverseService.AddUserToTeamAsync</c>,
    /// <c>RemoveUserFromTeamAsync</c> and <c>CreateSystemUserAsync</c> (the business-unit bind). A future writer that
    /// changes a user's teams or business unit MUST call it too — nothing else observes the change (no Dataverse
    /// plugins, ADR-002), so a writer that skips it leaves the old access in place for the identity + membership TTLs
    /// (over-grant after a removal).</para>
    /// <para><b>Works with no HttpContext</b> (a background job such as <c>DemoExpirationService</c>): eviction is
    /// tenant-agnostic. Only meaningful for a write to THIS BFF's own Dataverse environment — a write to another
    /// environment's users needs none (D-13), and the caller decides that.</para>
    /// <para>The user's per-record access SNAPSHOTS (60 s, keyed by Entra oid, which a team write does not carry) are
    /// not evicted here; they lapse within 60 s.</para>
    /// <para>Never throws; cancellation is not honoured — a write that already happened must not be left half
    /// cleaned up because its caller went away.</para>
    /// </remarks>
    Task InvalidateUserAccessAsync(Guid systemUserId, string? correlationId, CancellationToken ct);

    /// <summary>
    /// 🔴 <b>THE hook every BFF writer that changes a ROOT RECORD's OWNER must call</b>, after the write is applied
    /// (task 132 · C12). Evicts, under EVERY tenant segment: every user's membership-resolution entries for
    /// <paramref name="entityLogicalName"/>, every user's impersonated root-set entry for it, and every user's cached
    /// access snapshot for the record — so a re-own (secure ↔ standard, a team assignment) takes effect on the next
    /// request rather than after the TTLs.
    /// </summary>
    /// <remarks>
    /// <para><b>Who calls it today</b>: <c>ProvisionProjectEndpoint</c> (re-own to the Secure Record owner team),
    /// <c>UnsecureProjectEndpoint</c> (re-own back to a user), and <c>SecureChildReconciler</c> (every CHILD owner write of
    /// a secure / unsecure / re-file pass and of <c>SecureChildReconciliationJob</c>; batch-4 integration, 148 × 132). <b>Every owner-changing writer added or moved later
    /// MUST call it</b> — the C10 work (tasks 144, 146, 148, 149) is named in the task 132 record; whichever lands
    /// second wires the call. Without it, a BU colleague whose cached membership contained the record through
    /// ownership keeps it for the identity + membership TTLs after it is secured (over-grant).</para>
    /// <para>Per-entity eviction is a keyspace SCAN; it runs only on these rare owner-changing writes, never on a
    /// read path (dev keyspace measured at ≤ 178 keys over 7 days, task 132 notes).</para>
    /// <para><b>Only caches that can hold the type are scanned</b> (task 132 integration residual): the membership
    /// pattern only for a type the resolver caches (<c>MembershipResolverService.CachesEntityType</c>), the root-set
    /// pattern only for the three root types (<c>ImpersonatedRootSetSource.CachesEntityType</c>), the record snapshot
    /// only for a set the decorator caches (<c>CachedAccessDataSource.CachesRecordEntitySet</c>), plus the document
    /// snapshot for a <c>sprk_documents</c> record. A child an Assign cascade re-owns (<c>sharepointdocumentlocation</c>,
    /// <c>sharepointdocument</c>) is cached by none of them, so its restore builds no pattern and touches Redis not at
    /// all.</para>
    /// <para>Never throws; cancellation is not honoured (see <see cref="InvalidateUserAccessAsync"/>).</para>
    /// </remarks>
    /// <param name="entityLogicalName">The root's logical name, e.g. <c>sprk_project</c> — the membership and root-set key segment.</param>
    /// <param name="entitySetName">The root's entity SET name, e.g. <c>sprk_projects</c> — the access-snapshot key segment.</param>
    /// <param name="recordId">The re-owned record.</param>
    /// <param name="correlationId">Optional correlation id for the log lines.</param>
    /// <param name="ct">Not honoured (see remarks); accepted for signature symmetry.</param>
    Task InvalidateRecordOwnerChangeAsync(
        string entityLogicalName,
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct);

    /// <summary>
    /// 🔴 <b>THE eviction every POA SHARE write triggers</b> — a grant, a rights change or a revoke on a record, for a
    /// user or a team (task 132 integration residual: share-only changes stale the access caches exactly as owner
    /// changes do). Evicts, under EVERY tenant segment: every user's impersonated root-set entry for the record's root
    /// type (the set is answered by an impersonated query, which sees shares; a TEAM share changes every member's set,
    /// so the eviction is per type, not per sharee), and every user's access snapshot of the record (record-scoped, and
    /// document-scoped for a <c>sprk_documents</c> record).
    /// </summary>
    /// <remarks>
    /// <para><b>Who calls it</b>: <see cref="DataverseWebApiService"/> itself, after EVERY POA share write it makes —
    /// <c>GrantAccessAsync</c>, <c>ModifyAccessAsync</c> or <c>RevokeAccessAsync</c>, whether the write succeeded, threw or
    /// was cancelled (a write that reports failure can have committed) — through <see cref="IRecordShareWriteObserver"/>,
    /// which this interface implements by forwarding here (main-session round 55: the eviction is a property of the write,
    /// not of a caller-side seam). So every share writer is covered, however it reaches the client and however the write
    /// is invoked: <c>InternalShareEndpoints</c> share/unshare, provisioning's creator share and its restore (resume and
    /// error paths included), <c>UnsecureProjectEndpoint</c>'s revocations, <c>SecureChildShareSynchronizer</c>'s child
    /// fan-out, the Assigned-To materializer, the No Access enforcer, Direct-thread and playbook sharing — and a write
    /// reached by reflection, a late binder or an expression tree (task 132's behaviour tests run those). Exactly one
    /// eviction per write: <c>DataverseRecordShareService</c>, the seam the writers inject, no longer evicts.</para>
    /// <para><b>What lies outside it</b>: a POA write that does not go through a method of
    /// <see cref="DataverseWebApiService"/> — a raw HTTP call (including one sent with the client's own HttpClient or
    /// credential lifted out of it by reflection), or an SDK request. <c>PoaShareClientSingletonGuardTests</c> rejects
    /// such a write when its action name is a string constant, metadata, constant data or deployed configuration, and the
    /// SDK's POA messages by type; one whose action name exists only at run time, or native code, is review's to catch.
    /// POA writes made outside the BFF (MDA sharing, flows, scripts) are bounded by the caches' TTLs.</para>
    /// <para>Membership resolution is NOT evicted: it is computed from lookup columns and ownership only — a share is
    /// not a membership term.</para>
    /// <para>Never throws; cancellation is not honoured (see <see cref="InvalidateUserAccessAsync"/>).</para>
    /// </remarks>
    /// <param name="entitySetName">The shared record's entity SET name, as the POA write addressed it (<c>sprk_projects</c>).</param>
    /// <param name="recordId">The shared record.</param>
    /// <param name="correlationId">Optional correlation id for the log lines.</param>
    /// <param name="ct">Not honoured (see remarks); accepted for signature symmetry.</param>
    Task InvalidateRecordShareChangeAsync(
        string entitySetName,
        Guid recordId,
        string? correlationId,
        CancellationToken ct);

    /// <summary>
    /// The notification <see cref="DataverseWebApiService"/> sends after every POA share write, forwarded to
    /// <see cref="InvalidateRecordShareChangeAsync"/> for that record (task 132, main-session round 55). A default member,
    /// so every implementation — the real invalidator, the Null peer, a test double — is the observer with no code of its
    /// own, and what a share write evicts is always this interface's share-change eviction.
    /// </summary>
    Task IRecordShareWriteObserver.OnRecordShareWrittenAsync(
        string entitySetName, Guid recordId, RecordShareWrite write, CancellationToken ct)
        => InvalidateRecordShareChangeAsync(
            entitySetName,
            recordId,
            write switch
            {
                RecordShareWrite.Grant => "share:grant",
                RecordShareWrite.Modify => "share:modify",
                _ => "share:revoke",
            },
            ct);
}
