using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Production <see cref="IMemoryAccessAuthorizer"/> — delegates to existing BFF machinery, adding NO
/// new permission store (task AIR2-052, FR-B-03; CLAUDE.md §11 reuse).
///
/// <list type="bullet">
///   <item>Record-read alignment → <see cref="IDataversePrivilegeChecker.HasReadPrivilegeAsync"/>
///   (impersonated <c>RetrieveUserPrivileges</c> — caller-derived, cached, generic across entity types).</item>
///   <item>User-subject resolution → <see cref="NotificationService.ResolveSystemUserIdAsync"/>
///   (AAD <c>oid</c> → Dataverse <c>systemuserid</c>, ADR-028 one-hop).</item>
/// </list>
/// </summary>
internal sealed class MemoryAccessAuthorizer : IMemoryAccessAuthorizer
{
    private const string OidClaimType = "oid";
    private const string AltOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    private readonly IDataversePrivilegeChecker _privilegeChecker;
    private readonly NotificationService _notificationService;
    private readonly ILogger<MemoryAccessAuthorizer> _logger;

    public MemoryAccessAuthorizer(
        IDataversePrivilegeChecker privilegeChecker,
        NotificationService notificationService,
        ILogger<MemoryAccessAuthorizer> logger)
    {
        _privilegeChecker = privilegeChecker ?? throw new ArgumentNullException(nameof(privilegeChecker));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> CanCallerReadRecordAsync(
        ClaimsPrincipal caller, string entityLogicalName, Guid recordId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return false;
        }

        var oid = ExtractOid(caller);
        if (oid is null)
        {
            _logger.LogWarning("Memory record-read denied: caller has no valid AAD oid claim (record {RecordId})", recordId);
            return false;
        }

        // Entity-type Read privilege — the generic, caller-derived, no-parallel-ACL gate. recordId is
        // carried for the forthcoming row-level OBO implementation (deferred) and for correlation.
        var allowed = await _privilegeChecker.HasReadPrivilegeAsync(
            oid.Value, entityLogicalName.Trim().ToLowerInvariant(), ct);

        _logger.LogDebug(
            "Memory record-read authorization: oid={Oid} entity={Entity} record={RecordId} allowed={Allowed}",
            oid.Value, entityLogicalName, recordId, allowed);

        return allowed;
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
        var raw = caller.FindFirst(OidClaimType)?.Value
                  ?? caller.FindFirst(AltOidClaimType)?.Value
                  ?? caller.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var oid) ? oid : null;
    }
}
