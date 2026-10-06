using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Production <see cref="IMemoryAccessAuthorizer"/> — delegates to existing BFF machinery, adding NO
/// new permission store (task AIR2-052, FR-B-03; CLAUDE.md §11 reuse).
///
/// <list type="bullet">
///   <item>User-subject resolution → <see cref="NotificationService.ResolveSystemUserIdAsync"/>
///   (AAD <c>oid</c> → Dataverse <c>systemuserid</c>, ADR-028 one-hop).</item>
/// </list>
///
/// <para>The record-read half (<c>CanCallerReadRecordAsync</c>, an entity-type privilege check over
/// <c>IDataversePrivilegeChecker</c>) was deleted with its only route by unified-access-control-r2 task
/// 166 — see <see cref="IMemoryAccessAuthorizer"/>.</para>
/// </summary>
internal sealed class MemoryAccessAuthorizer : IMemoryAccessAuthorizer
{
    private readonly NotificationService _notificationService;

    public MemoryAccessAuthorizer(NotificationService notificationService)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    /// <inheritdoc/>
    public async Task<Guid?> ResolveCallerUserSubjectAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var oid = ExtractOid(caller);
        if (oid is null)
        {
            return null;
        }

        return await _notificationService.ResolveSystemUserIdAsync(oid.Value, ct);
    }

    private static Guid? ExtractOid(ClaimsPrincipal caller)
    {
        var raw = CallerResolution.ResolveObjectId(caller);

        return Guid.TryParse(raw, out var oid) ? oid : null;
    }
}
