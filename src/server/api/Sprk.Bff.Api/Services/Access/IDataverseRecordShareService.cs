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
/// <para><b>Every write evicts the access caches it stales</b> (task 132, batch 4 integration residual): the production
/// implementation, <see cref="DataverseRecordShareService"/>, calls
/// <see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/> after
/// each grant / modify / revoke. A writer therefore needs no eviction of its own — and must not reach POA any other way
/// (the routes the build guard closes are listed on <see cref="DataverseRecordShareService"/>).</para>
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

    /// <inheritdoc cref="DataverseWebApiService.RevokeAccessAsync"/>
    Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default);

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

    /// <inheritdoc cref="DataverseWebApiService.GetPrincipalAccessForRecordsOrThrowAsync"/>
    /// <remarks>unified-access-control-r2 task 149: the strict read for many records of one table, for the secure-child
    /// share synchronizer. Every record asked about is in the answer, or the call throws.</remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName,
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IDataverseRecordShareService"/> — a pass-through to the shared
/// <see cref="DataverseWebApiService"/> POA primitives that also evicts the access caches every share WRITE makes stale.
/// Holds no state; safe as a singleton over the singleton <see cref="DataverseWebApiService"/>.
/// </summary>
/// <remarks>
/// <para><b>Share writes evict (unified-access-control-r2 task 132, batch 4 integration residual).</b> A grant, a
/// rights change or a revoke changes who can read the record exactly as an owner change does, so after each one —
/// whether it returned or threw (a write that reports failure can have committed) — this seam calls
/// <see cref="IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync"/>: every user's impersonated root set for the
/// record's root type and every user's access snapshots of the record. The eviction lives HERE, in the one POA client,
/// rather than at each writer, so no share writer can be born without it. <c>PoaShareClientSingletonGuardTests</c> fails
/// the build on every route around this seam that exists in compiled code. An IL scan of every <c>src</c> assembly the BFF
/// runs, or that can name the client, rejects: any reference to <see cref="DataverseWebApiService"/>'s
/// GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync outside this type, the client's own code included (whatever
/// the receiver expression, and inside a lambda, async method, method group or expression tree); any use of the SDK's
/// GrantAccess / ModifyAccess / RevokeAccess request messages; and any POA action or POA write-method name loaded as a
/// string constant outside the client (an SDK request by name, a hand-built POST, a reflective lookup, a <c>dynamic</c>
/// call). It also pins the client's POA writes to exactly those three methods, and this type's writes to its own three
/// evicting methods. Text rules over every <c>src/server</c> file add breadth: a POA write call whose receiver is not
/// declared, only, as <see cref="IDataverseRecordShareService"/>; the SDK messages; a second POA payload; a write method
/// named as a string. No static guard can see a method name or action URL the code computes or reads at run time (from
/// non-constant pieces, configuration or reflection metadata); that is review's to catch. It is not bound to the caller's
/// token
/// (<see cref="CancellationToken.None"/>) and never fails or changes the write's own outcome: the hook does not throw, and
/// a defect that made it throw is caught and logged here. Reads evict nothing.</para>
/// </remarks>
public sealed class DataverseRecordShareService : IDataverseRecordShareService
{
    private readonly DataverseWebApiService _dataverse;
    private readonly IMembershipCacheInvalidator _accessCacheInvalidator;
    private readonly ILogger<DataverseRecordShareService> _logger;

    public DataverseRecordShareService(
        DataverseWebApiService dataverse,
        IMembershipCacheInvalidator accessCacheInvalidator,
        ILogger<DataverseRecordShareService> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _accessCacheInvalidator = accessCacheInvalidator ?? throw new ArgumentNullException(nameof(accessCacheInvalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
    {
        try
        {
            await _dataverse.GrantAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAfterShareWriteAsync(entitySetName, recordId, "grant").ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task ModifyAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        string accessRightsCsv,
        CancellationToken ct = default)
    {
        try
        {
            await _dataverse.ModifyAccessAsync(entitySetName, recordId, principal, accessRightsCsv, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAfterShareWriteAsync(entitySetName, recordId, "modify").ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default)
    {
        try
        {
            await _dataverse.RevokeAccessAsync(entitySetName, recordId, principal, ct).ConfigureAwait(false);
        }
        finally
        {
            await EvictAfterShareWriteAsync(entitySetName, recordId, "revoke").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The share-change eviction, after a write that returned OR threw. Never throws (the write's own outcome — its
    /// return or its exception — is what the caller sees) and is not bound to the caller's token: the write may already
    /// have committed, and a caller that went away must not leave the clean-up undone.
    /// </summary>
    private async Task EvictAfterShareWriteAsync(string entitySetName, Guid recordId, string write)
    {
        try
        {
            await _accessCacheInvalidator
                .InvalidateRecordShareChangeAsync(entitySetName, recordId, $"share:{write}", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ACCESS-EVICT] The share-change eviction for {EntitySet} {RecordId} ({Write}) threw; the share write's own " +
                "outcome stands and the cached entries lapse on their TTLs.", entitySetName, recordId, write);
        }
    }

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

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
        string entityLogicalName,
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken ct = default)
        => _dataverse.GetPrincipalAccessForRecordsOrThrowAsync(entityLogicalName, recordIds, ct);
}
