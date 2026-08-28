using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Tests.Services.Workspace;

/// <summary>
/// Resolves any Entra oid to one fixed Dataverse systemuserid.
/// </summary>
/// <remarks>
/// Deliberately NOT a null-returning stub. <c>PortfolioService</c> now fails CLOSED when the caller
/// cannot be resolved, so a null stub would make every test pass against an empty result set — i.e.
/// pass for the wrong reason, which is exactly the failure mode that let the original defect through
/// 11,932 green tests. Returning a real id keeps the ownerid filter on the exercised path.
/// </remarks>
internal sealed class StubSystemUserIdentityResolver : ISystemUserIdentityResolver
{
    internal static readonly StubSystemUserIdentityResolver Instance = new();

    internal static readonly Guid SystemUserId = Guid.Parse("5b5e5c4a-0000-4000-8000-00000000f00d");

    public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult<string?>(Guid.NewGuid().ToString("D"));

    public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(string.IsNullOrWhiteSpace(oid) ? null : SystemUserId);

    public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
