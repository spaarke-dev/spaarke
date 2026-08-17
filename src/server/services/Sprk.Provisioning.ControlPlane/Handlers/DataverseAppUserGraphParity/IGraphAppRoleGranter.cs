// -----------------------------------------------------------------------------
// IGraphAppRoleGranter.cs
//
// L2 abstraction over granting the 14 Microsoft Graph application (app-only)
// roles (per Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles, mirrored locally
// via IGraphAppRolesRegistry) onto the UAMI service principal. Parity with
// scripts/Grant-GraphAppRoles.ps1 (task 015) — same idempotent delta-apply
// semantics (already-granted roles are a no-op; only missing roles are
// POSTed), same Graph endpoint shape
// (POST /servicePrincipals/{uamiSpId}/appRoleAssignments).
//
// NEVER silent-skips a role on failure — a failed grant is reported so the
// handler can classify + surface it (design.md §4B T3 rationale: silent skip
// re-introduces the trap the granter exists to close).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Grants the expected Graph app-role catalog onto a UAMI service principal.
/// Idempotent — re-invoking after a prior partial success only re-attempts
/// the still-missing roles.
/// </summary>
public interface IGraphAppRoleGranter
{
    /// <summary>
    /// Reads current app-role assignments on <paramref name="uamiServicePrincipalObjectId"/>
    /// scoped to the Microsoft Graph resource SP, computes the delta against
    /// <paramref name="expectedRoles"/>, and POSTs any missing grants.
    /// </summary>
    Task<GraphAppRoleGrantOutcome> GrantRolesAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IGraphAppRoleGranter.GrantRolesAsync"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record GraphAppRoleGrantOutcome
{
    private GraphAppRoleGrantOutcome() { }

    /// <summary>Every expected role is now granted (already-present + newly-applied).</summary>
    public sealed record Success(int GrantedCount) : GraphAppRoleGrantOutcome;

    /// <summary>
    /// One or more grant POSTs failed. <paramref name="FailedRoleValues"/>
    /// names the roles whose grant call errored (for the operator diagnostic);
    /// roles NOT in this list either were already present or were granted
    /// successfully this invocation.
    /// </summary>
    public sealed record Failure(string Diagnostic, IReadOnlyList<string> FailedRoleValues) : GraphAppRoleGrantOutcome;
}
