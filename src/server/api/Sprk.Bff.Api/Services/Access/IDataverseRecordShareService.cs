using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// The BFF's single seam over Dataverse's <c>principalobjectaccess</c> (POA) record-sharing surface:
/// grant a principal access to a record, revoke it, and read back who currently holds a share.
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
}

/// <summary>
/// Default <see cref="IDataverseRecordShareService"/> — a pass-through to the shared
/// <see cref="DataverseWebApiService"/> POA primitives. Holds no state; safe as a singleton over the
/// singleton <see cref="DataverseWebApiService"/>.
/// </summary>
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
    public Task RevokeAccessAsync(
        string entitySetName,
        Guid recordId,
        DataversePrincipalRef principal,
        CancellationToken ct = default)
        => _dataverse.RevokeAccessAsync(entitySetName, recordId, principal, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.GetPrincipalAccessAsync(entityLogicalName, recordId, ct);
}
