// -----------------------------------------------------------------------------
// IAdminConsentVerifier.cs
//
// L2 abstraction over the Microsoft Graph admin-consent verification H3
// performs AFTER provisioning the BFF app-registration. Post-provisioning,
// the app-registration's DELEGATED permissions (5 per
// EntraAppRegPermissionCatalog.cs — Files/Sites/User.Read/Mail.Send +
// Dynamics user_impersonation) require a tenant admin's explicit consent
// before tokens can carry those scopes. H3 queries Graph
// (oauth2PermissionGrants) to confirm the tenant admin has consented and —
// if not — transitions the run to WaitingOnGate rather than failing.
//
// SCOPE CORRECTION (task 130, Wave G-3 — Path C per root CLAUDE.md §6.5,
// documented in task 130's completion notes): the Wave-C4 scaffold's doc
// comments described this verifier as covering the 14 APPLICATION-ONLY
// (app-role) grants from Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.cs.
// That catalog is H10's exclusive concern (GraphRestAppRoleGranter +
// GraphRestAppRoleParityVerifier already grant + verify all 14 onto the
// customer's UAMI service principal — a DIFFERENT principal than this
// app-registration's own service principal). design.md §4.1's H3 SDK-surface
// table (Applications/ServicePrincipals/Oauth2PermissionGrants — no
// AppRoleAssignedTo) confirms H3 never owned the app-role catalog. This
// verifier's true scope is the 5 DELEGATED (OAuth2PermissionScope) grants —
// EntraAppRegPermissionCatalog.cs is the shared source of truth both this
// verifier and GraphAppRegistrationProvisioner consume.
//
// SPEC / DESIGN references:
//   - spec.md FR-06 (H3 acceptance): app-reg permissions returned by a Graph
//     oauth2PermissionGrants query.
//   - spec.md NFR-09: Graph v6 / Kiota 2.0 error type — catch ODataError
//     (not ServiceException); ResponseStatusCode is int.
//   - design.md §4.1 H3 row: WaitingOnGate on admin-consent-pending is a
//     resumable pause, NOT a failure.
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: MI-outbound MUST rule.
//
// GATE SEMANTICS:
//   - Verified(grantedCount, expectedCount) — all 5 delegated scope values
//     observed on the app's oauth2PermissionGrants; `admin-consent` gate
//     flips to Verified; handler returns Success.
//   - Pending(grantedCount, expectedCount, diagnostic) — some/all scopes
//     missing (or the service principal does not exist yet in the target
//     tenant — the multi-tenant SP is only materialized once someone in that
//     tenant interacts with the app); handler transitions run to
//     WaitingOnGate with `admin-consent` = Pending; reconciler re-invokes H3
//     after operator grants consent.
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Verifies tenant admin consent has been granted for the BFF app-registration's
/// 5 requested DELEGATED permissions (<see cref="EntraAppRegPermissionCatalog"/>).
/// Domain outcomes (consent granted, consent pending) return typed results;
/// only unexpected infrastructure errors (transient Graph SDK fault, network
/// fault) should throw.
/// </summary>
public interface IAdminConsentVerifier
{
    /// <summary>
    /// Queries Microsoft Graph <c>oauth2PermissionGrants</c> for the BFF
    /// app-registration's service principal in the target tenant, using
    /// DefaultAzureCredential scoped explicitly to that tenant (§4D I5).
    /// </summary>
    /// <param name="bffAppRegId">The BFF app-registration <c>appId</c> to verify.</param>
    /// <param name="tenantId">Target Entra tenant id (§4D I1 — mandatory).</param>
    /// <param name="expectedDelegatedScopeCount">
    /// Number of delegated scopes expected per
    /// <see cref="EntraAppRegPermissionCatalog.All"/> (5 as of task 130). The
    /// verifier compares observed grant scope values against this expected
    /// count to decide Verified vs Pending.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminConsentVerificationResult> VerifyAsync(
        string bffAppRegId,
        string tenantId,
        int expectedDelegatedScopeCount,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IAdminConsentVerifier.VerifyAsync"/> invocation.
/// Exhaustive: <see cref="Verified"/> | <see cref="Pending"/>.
/// </summary>
public abstract record AdminConsentVerificationResult
{
    private AdminConsentVerificationResult() { }

    /// <summary>Sufficient grants observed — tenant admin has consented.</summary>
    /// <param name="GrantedCount">Number of application-role assignments observed on the BFF app-reg's service principal.</param>
    /// <param name="ExpectedCount">The expected count that was requested (typically 14 per <c>GraphAppRoles.cs</c>).</param>
    /// <param name="Evidence">Optional Graph response payload for the gate's <c>Evidence</c> field (operator diagnostic without log-diving).</param>
    public sealed record Verified(
        int GrantedCount,
        int ExpectedCount,
        JsonElement? Evidence) : AdminConsentVerificationResult;

    /// <summary>
    /// Admin consent has NOT been granted (or is only partially granted).
    /// Handler transitions run to <see cref="Sprk.Provisioning.ControlPlane.Models.RunStatus.WaitingOnGate"/>
    /// with the <c>admin-consent</c> gate marked <see cref="Sprk.Provisioning.ControlPlane.Models.GateState.Pending"/>.
    /// </summary>
    /// <param name="GrantedCount">Number of grants currently observed (may be zero).</param>
    /// <param name="ExpectedCount">The expected count.</param>
    /// <param name="Diagnostic">Human-readable diagnostic (which subset of grants is missing, when useful).</param>
    /// <param name="Evidence">Optional Graph response payload for the gate's <c>Evidence</c> field.</param>
    public sealed record Pending(
        int GrantedCount,
        int ExpectedCount,
        string Diagnostic,
        JsonElement? Evidence) : AdminConsentVerificationResult;
}
