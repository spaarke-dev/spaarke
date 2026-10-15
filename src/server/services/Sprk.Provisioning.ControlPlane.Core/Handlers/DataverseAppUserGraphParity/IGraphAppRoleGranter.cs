// -----------------------------------------------------------------------------
// IGraphAppRoleGranter.cs
//
// L2 abstraction over reconciling the stamp identity's Microsoft Graph
// application (app-only) roles (Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles,
// mirrored locally via IGraphAppRolesRegistry) on the stamp UAMI service
// principal:
//   - GrantRolesAsync — delta-apply: already-granted roles are a no-op; only
//     missing roles are POSTed (parity with scripts/Grant-GraphAppRoles.ps1).
//   - RemoveUnexpectedRolesAsync (task 261 / G31) — DELETEs every Graph app
//     role assignment the stamp identity holds that is not in the allowed set
//     (roles granted by an earlier catalog, or by hand). Each removal is logged.
//
// NEVER silent-skips a role on failure — a failed grant or removal is reported
// so the handler can classify + surface it (design.md §4B T3 rationale: silent
// skip re-introduces the trap the granter exists to close).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Reconciles the expected Graph app-role catalog onto a UAMI service principal.
/// Idempotent — re-invoking after a prior partial success only re-attempts what is still missing or still extra.
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

    /// <summary>
    /// Removes every Microsoft Graph app-role assignment on <paramref name="uamiServicePrincipalObjectId"/> whose role
    /// is not in <paramref name="allowedRoles"/>. Assignments on other resources (Dataverse, Key Vault, …) are never
    /// touched. Refuses (Failure, nothing removed) when <paramref name="allowedRoles"/> is empty or carries a null
    /// AppRoleId — an incomplete allowed set would otherwise strip roles the stamp needs — and when the target is not
    /// the stamp's managed identity: its <c>appId</c> must equal <paramref name="uamiClientId"/> and its type must be
    /// <c>ManagedIdentity</c>, so a corrupted object id never strips another identity (the L2 Worker's own, say).
    /// </summary>
    Task<GraphAppRoleRemovalOutcome> RemoveUnexpectedRolesAsync(
        string uamiServicePrincipalObjectId,
        string uamiClientId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> allowedRoles,
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

/// <summary>
/// Result of one <see cref="IGraphAppRoleGranter.RemoveUnexpectedRolesAsync"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record GraphAppRoleRemovalOutcome
{
    private GraphAppRoleRemovalOutcome() { }

    /// <summary>
    /// No Graph app role outside the allowed set remains. <paramref name="RemovedRoleValues"/> names what this call
    /// removed (empty when there was nothing to remove).
    /// </summary>
    public sealed record Success(IReadOnlyList<string> RemovedRoleValues) : GraphAppRoleRemovalOutcome;

    /// <summary>
    /// The assignments could not be read, or one or more DELETEs failed. <paramref name="RemovedRoleValues"/> were
    /// removed before the failure; <paramref name="FailedRoleValues"/> are still assigned.
    /// </summary>
    public sealed record Failure(
        string Diagnostic,
        IReadOnlyList<string> RemovedRoleValues,
        IReadOnlyList<string> FailedRoleValues) : GraphAppRoleRemovalOutcome;
}
