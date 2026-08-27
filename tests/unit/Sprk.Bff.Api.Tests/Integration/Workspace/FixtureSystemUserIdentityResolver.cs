using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.Integration.Workspace;

/// <summary>
/// Resolves any non-blank Entra oid to one fixed Dataverse systemuserid, so workspace fixtures
/// exercise the ownerid filter rather than the fail-closed path.
/// </summary>
/// <remarks>
/// Returns <c>null</c> for a blank oid so the fail-closed branch stays reachable and testable.
/// </remarks>
internal sealed class FixtureSystemUserIdentityResolver : ISystemUserIdentityResolver
{
    private readonly Guid _systemUserId;
    private readonly bool _resolvesAnyCaller;

    public FixtureSystemUserIdentityResolver(string systemUserId)
    {
        _systemUserId = Guid.TryParse(systemUserId, out var id) ? id : Guid.NewGuid();
        _resolvesAnyCaller = true;
    }

    /// <summary>
    /// Resolves NO caller — for exercising the fail-closed branch of ownership-scoped operations.
    /// </summary>
    public FixtureSystemUserIdentityResolver(bool resolvesAnyCaller)
    {
        _systemUserId = Guid.Empty;
        _resolvesAnyCaller = resolvesAnyCaller;
    }

    public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult<string?>(_systemUserId.ToString("D"));

    public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(
            !_resolvesAnyCaller || string.IsNullOrWhiteSpace(oid) ? null : _systemUserId);

    public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
