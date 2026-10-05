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
/// (exactly what the build guard enforces, and what it does not, is listed on <see cref="DataverseRecordShareService"/>).</para>
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
/// rather than at each writer, so every writer that shares through this seam gets it without writing any eviction of its
/// own (how far the build enforces "through this seam" is the next paragraph). It is not bound to the caller's token
/// (<see cref="CancellationToken.None"/>) and never fails or changes the write's own outcome: the hook does not throw, and
/// a defect that made it throw is caught and logged here. Reads evict nothing.</para>
///
/// <para><b>What the build guard enforces</b> (<c>PoaShareClientSingletonGuardTests</c>) — exactly the following, and
/// nothing beyond it (owner round 48). <i>Outside this type</i>, over every <c>src</c> assembly the BFF runs or that can
/// name the client (derived from the csproj graph; each must load): (1) <b>every compiled call path</b> — no reference to
/// <see cref="DataverseWebApiService"/>'s GrantAccessAsync / ModifyAccessAsync / RevokeAccessAsync, the client's own code
/// included (whatever the receiver expression, and inside a lambda, async method, method group or expression tree); no
/// use of the SDK's GrantAccess / ModifyAccess / RevokeAccess request messages; no POA action or POA write-method name
/// carried by a string constant (an SDK request by name, a hand-built POST, a <c>dynamic</c> call, in any letter case), by
/// metadata (a type, member, enum value or parameter of that name; a const, default value, attribute argument or
/// embedded resource holding it), by constant data (a UTF-8 literal, a byte or char array initializer) or by the
/// configuration the BFF is deployed with; and the client's POA writes pinned to exactly those three methods.
/// (2) <b>No <c>[UnsafeAccessor]</c></b> (or <c>[UnsafeAccessorType]</c>) — in that compiled metadata, however spelled or
/// aliased, and in the text of every source and MSBuild file under <c>src/server</c>. (3) <b>No method chosen or invoked
/// by reflection</b>, on any type — so none on the Dataverse service types or on a Type obtained from them, however
/// obtained: no method or member lookup (by name, signature or metadata token), no property or event accessor-method
/// lookup, no name-based <c>Expression.Call</c>, no <c>MethodBase.Invoke</c>, <c>InvokeMember</c>, <c>CreateDelegate</c>,
/// <c>MethodInvoker</c>, method function pointer, compiled expression tree or <c>dynamic</c> member access — and no code
/// the scan cannot read (emitted IL, an assembly loaded at run time).
/// <i>Inside this type</i>, the client's writes may be called only from the three methods the interface map binds to
/// <see cref="IDataverseRecordShareService"/>'s writes (by metadata identity: an overload of the same name is outside
/// them), and an IL path analysis of each — its async state machine, exceptions, suspensions and resumptions, every
/// <c>finally</c> — proves that EVERY path that makes the write awaits it, then calls <c>EvictAfterShareWriteAsync</c> with
/// the same entity set and record id and awaits that, before the method returns, throws or is cancelled; and that the
/// helper, on every path, calls the invalidator with that record and awaits it. What the invalidator then evicts is
/// proved by behaviour tests for every write, every <see cref="DataversePrincipalKind"/> and each outcome. Text rules add
/// breadth over every <c>src/server</c> file (a write call whose receiver is not declared, only, as
/// <see cref="IDataverseRecordShareService"/>; the SDK messages; a second POA payload; a write method named as a string).
/// <b>Not enforced</b> — the build proves nothing about it; it is review's to catch: a POA write by a route that uses
/// none of those mechanisms — above all a raw HTTP call whose action URL exists only at run time (assembled from pieces
/// none of which is the action name — fragments joined by a call, an enum value's name plus a suffix, single characters,
/// a decoding — or read from a live store no repository file holds: an App Service setting set by hand, Key Vault,
/// Dataverse); native code; code outside the scanned assemblies (it cannot name the client and does not run in the BFF).
/// POA writes made outside the BFF (MDA sharing, flows, scripts) are bounded by the caches' TTLs.</para>
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
