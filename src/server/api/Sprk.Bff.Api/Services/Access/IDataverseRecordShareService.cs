using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// The BFF's single seam over Dataverse's <c>principalobjectaccess</c> (POA) record-sharing surface:
/// grant a principal access to a record, change or revoke it, and read back who currently holds a share.
/// </summary>
/// <remarks>
/// <para><b>Why this interface exists (ADR-010)</b>: it is a testing seam, not an abstraction layer.
/// The underlying methods live on the concrete <see cref="DataverseWebApiService"/> singleton and are
/// not mockable there (a non-virtual concrete class), and ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// so <see cref="Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService"/>'s no-leak
/// negative tests (a third user must never be granted) need a module-boundary seam to substitute.
/// Mirrors <c>IImpersonatedCommunicationQuery</c>'s "interface as a testing seam only" pattern.</para>
///
/// <para><b>Consolidation (unified-access-control-r2 task 060, CLAUDE.md §11)</b>: this replaces the
/// former <c>IDataverseAccessGrantService</c> (systemuser-only, grant-only, no revoke) AND the private
/// POA client that lived inside <c>PlaybookSharingService</c> (teams-only, but with revoke). Two clients
/// became one seam parameterized by <see cref="DataversePrincipalKind"/>. It was renamed out of
/// "…AccessGrant…" because it no longer only grants, and moved out of <c>Services/Communication/Access/</c>
/// because it is now cross-cutting — Communication threads, Ai playbooks, and Secure Projects
/// (FR-28 / FR-29) all share records through it. <b>Do not add a second POA client.</b></para>
///
/// <para><b>Two reads, on purpose</b> (task 063). <see cref="GetPrincipalAccessAsync"/> fails soft — an empty list
/// when the read fails — which suits callers that only display shares or grant from them.
/// <see cref="GetPrincipalAccessOrThrowAsync"/> is the complete answer or an exception. A caller that decides a
/// WRITE from the current shares (the FR-29 "+ User" endpoints) must use it: "no share" and "the read failed" call
/// for different writes, and the soft read cannot tell them apart.</para>
///
/// <para><b>Every write evicts the access caches it stales</b> (task 132; main-session round 55): not here — in the
/// write itself. <see cref="DataverseWebApiService"/> notifies its <see cref="IRecordShareWriteObserver"/> (in the BFF,
/// <see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator"/>, whose default member calls
/// <see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/>) after
/// every grant / modify / revoke it makes, whoever called it. A writer needs no eviction of its own, and this seam adds
/// none (one eviction per write).</para>
/// </remarks>
public interface IDataverseRecordShareService
{
    /// <inheritdoc cref="DataverseWebApiService.GrantAccessAsync"/>
    Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default);

    /// <inheritdoc cref="DataverseWebApiService.ModifyAccessAsync"/>
    Task ModifyAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default);

    /// <inheritdoc cref="DataverseWebApiService.RevokeAccessAsync(string, Guid, DataversePrincipalRef, CancellationToken)"/>
    /// <remarks>App-only: it says nothing about who owns the record. Dataverse refuses this for the share of the record's
    /// current owning user (0x80040223) — a caller that may be revoking THAT share uses the overload that names the
    /// owner.</remarks>
    Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes <paramref name="principal"/>'s share on a record whose CURRENT owner the caller knows
    /// (<paramref name="recordOwner"/>, read back by the caller). When the principal IS the owning user, the revoke runs as
    /// that user — the only identity Dataverse lets revoke the owner's own share ("Only owner can revoke access to the
    /// owner", 0x80040223); for every other principal, and for a team-owned record, it is the app-only revoke above.
    /// </summary>
    /// <remarks>
    /// <para>unified-access-control-r2 (live on dev 2026-10-06): the unsecure sweep revoked the new owner's own share
    /// app-only and every creator-driven unsecure ended <c>sweepComplete: false</c>; provisioning's undo on a record the
    /// creator owns ended <c>sharesRestored: false</c>. Both callers know the owner, so they say so here rather than this
    /// seam reading the owner again per revoke.</para>
    /// <para>The default body is the app-only revoke: an implementation that predates this member behaves exactly as it
    /// did, and against Dataverse that fails closed (the owner's share is refused, never silently kept as revoked).</para>
    /// </remarks>
    Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        DataversePrincipalRef recordOwner,
        CancellationToken ct = default)
        => RevokeAccessAsync(entitySetName, recordId, principal, ct);

    /// <inheritdoc cref="DataverseWebApiService.GetPrincipalAccessAsync"/>
    Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default);

    /// <inheritdoc cref="DataverseWebApiService.GetPrincipalAccessOrThrowAsync"/>
    Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default);

    /// <inheritdoc cref="DataverseWebApiService.RetrievePrincipalRightsAsync"/>
    /// <remarks>Task 146 c1-r1 (owner round 13 item 8): the EFFECTIVE-rights read beside the share reads — the one question
    /// a writer that impersonates a user (and holds no token of theirs) needs for F3. Shares say who was GRANTED access;
    /// this says what the user can DO, roles and teams included.
    /// <para>The default body FAULTS (never "no rights", never "all rights"), so a test double that predates this member
    /// answers F3 as "could not be checked" — fail closed — without every double having to change.</para></remarks>
    Task<AccessRights> GetPrincipalRightsAsync(
        Guid principalSystemUserId,
        string entitySetName,
        Guid recordId,
        CancellationToken ct = default)
        => Task.FromException<AccessRights>(new NotSupportedException(
            $"{GetType().Name} does not read a principal's effective rights."));

    /// <inheritdoc cref="DataverseWebApiService.RetrievePrincipalRightsOrUnknownAsync"/>
    /// <remarks>Task 171 (adversarial finding 3): for a caller that REVOKES on "no rights". The default body faults, so a
    /// double that predates this member answers "could not be checked" — the grant is kept.</remarks>
    Task<AccessRights?> GetPrincipalRightsOrUnknownAsync(
        Guid principalSystemUserId,
        string entitySetName,
        Guid recordId,
        CancellationToken ct = default)
        => Task.FromException<AccessRights?>(new NotSupportedException(
            $"{GetType().Name} does not read a principal's effective rights."));

    /// <inheritdoc cref="DataverseWebApiService.GetPrincipalAccessForRecordsOrThrowAsync"/>
    /// <remarks>unified-access-control-r2 task 149: the strict read for many records of one table, for the secure-child
    /// share synchronizer. Every record asked about is in the answer, or the call throws.</remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName,
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IDataverseRecordShareService"/> — a pass-through to the shared <see cref="DataverseWebApiService"/>
/// POA primitives. Holds no state; safe as a singleton over the singleton <see cref="DataverseWebApiService"/>.
/// </summary>
/// <remarks>
/// <para><b>It does not evict, on purpose</b> (unified-access-control-r2 task 132, main-session round 55). A grant, a
/// rights change or a revoke changes who can read the record exactly as an owner change does, so the access caches must
/// be evicted after each one. That eviction used to live here — which made it hold only for writes that came through
/// this seam, and three verification rounds each found one more way around the seam. It now lives in the write itself:
/// <see cref="DataverseWebApiService"/> notifies its <see cref="IRecordShareWriteObserver"/> (the BFF's
/// <see cref="IMembershipCacheInvalidator"/>, whose default member calls
/// <see cref="IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/>) after every share write it makes, on
/// every path, not bound to the caller's token, never changing the write's outcome. This seam adding an eviction of its
/// own would evict twice per write.</para>
///
/// <para><b>What it is still for</b>: the ADR-010 testing seam and the one POA entry point every writer injects (task
/// 060's consolidation, above). The build guard (<c>PoaShareClientSingletonGuardTests</c>) keeps it that way, and — as
/// defence in depth behind the client's own notification — rejects a POA write that would bypass the client. Exactly
/// what it checks, and what it does not, is listed in that guard's header; nothing here claims more.</para>
/// </remarks>
public sealed class DataverseRecordShareService : IDataverseRecordShareService
{
    private readonly DataverseWebApiService _dataverse;

    public DataverseRecordShareService(DataverseWebApiService dataverse)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
    }

    /// <inheritdoc />
    public Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
        => _dataverse.GrantAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);

    /// <inheritdoc />
    public Task ModifyAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
        => _dataverse.ModifyAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct);

    /// <inheritdoc />
    public Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default)
        => _dataverse.RevokeAccessAsync(entitySetName, recordId, principal, ct);

    /// <inheritdoc />
    public Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        DataversePrincipalRef recordOwner,
        CancellationToken ct = default)
        => _dataverse.RevokeAccessAsync(entitySetName, recordId, principal, recordOwner, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.GetPrincipalAccessOrThrowAsync(entityLogicalName, recordId, ct);

    /// <inheritdoc />
    public Task<AccessRights> GetPrincipalRightsAsync(
        Guid principalSystemUserId,
        string entitySetName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.RetrievePrincipalRightsAsync(principalSystemUserId, entitySetName, recordId, ct);

    /// <inheritdoc />
    public Task<AccessRights?> GetPrincipalRightsOrUnknownAsync(
        Guid principalSystemUserId,
        string entitySetName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.RetrievePrincipalRightsOrUnknownAsync(principalSystemUserId, entitySetName, recordId, ct);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName,
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct = default)
        => _dataverse.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
}
