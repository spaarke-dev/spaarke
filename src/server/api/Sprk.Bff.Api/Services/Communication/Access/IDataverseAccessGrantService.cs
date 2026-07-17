using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Thin test-seam over <see cref="DataverseWebApiService"/>'s POA (principalobjectaccess) primitives —
/// GrantAccess + shared-principal read (messaging-communication-app-r1 task 043). Mirrors
/// <see cref="IImpersonatedCommunicationQuery"/>'s "interface as a testing seam only" pattern (ADR-010):
/// the underlying methods exist on the concrete <see cref="DataverseWebApiService"/> singleton but are not
/// mockable there (a non-virtual concrete class), and ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>, so
/// <see cref="IDirectThreadAccessService"/>'s no-leak negative tests (a third user must never be granted)
/// need a module-boundary seam to substitute.
/// </summary>
public interface IDataverseAccessGrantService
{
    /// <inheritdoc cref="DataverseWebApiService.GrantAccessAsync"/>
    Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        Guid principalSystemUserId,
        string accessRightsCsv,
        CancellationToken ct = default);

    /// <inheritdoc cref="DataverseWebApiService.GetSharedSystemUserIdsAsync"/>
    Task<IReadOnlyList<Guid>> GetSharedSystemUserIdsAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IDataverseAccessGrantService"/> — a pass-through to the shared
/// <see cref="DataverseWebApiService"/> POA primitives. Holds no state; safe as a singleton over the
/// singleton <see cref="DataverseWebApiService"/>.
/// </summary>
public sealed class DataverseAccessGrantService : IDataverseAccessGrantService
{
    private readonly DataverseWebApiService _dataverse;

    public DataverseAccessGrantService(DataverseWebApiService dataverse)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
    }

    /// <inheritdoc />
    public Task GrantAccessAsync(
        string entitySetName,
        Guid recordId,
        Guid principalSystemUserId,
        string accessRightsCsv,
        CancellationToken ct = default)
        => _dataverse.GrantAccessAsync(entitySetName, recordId, principalSystemUserId, accessRightsCsv, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> GetSharedSystemUserIdsAsync(
        string entityLogicalName,
        Guid recordId,
        CancellationToken ct = default)
        => _dataverse.GetSharedSystemUserIdsAsync(entityLogicalName, recordId, ct);
}
