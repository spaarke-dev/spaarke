using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Resolves any non-blank Entra oid to one fixed Dataverse systemuserid, so workspace fixtures
/// exercise the ownerid filter rather than the fail-closed path.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for a blank oid so the fail-closed branch stays reachable and testable.
/// </remarks>
internal sealed class FixtureSystemUserIdentityResolver(string systemUserId) : ISystemUserIdentityResolver
{
    private readonly Guid _systemUserId = Guid.TryParse(systemUserId, out var id) ? id : Guid.NewGuid();

    public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult<string?>(_systemUserId.ToString("D"));

    public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(string.IsNullOrWhiteSpace(oid) ? null : _systemUserId);

    public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
