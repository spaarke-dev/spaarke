// -----------------------------------------------------------------------------
// IAdminConsentVerifier.cs
//
// L2 abstraction over the Microsoft Graph admin-consent verification H3
// performs AFTER provisioning the BFF app-registration. Post-provisioning,
// the app-registration exists but its Graph application-role assignments
// (14 of them per Infrastructure/Auth/GraphAppRoles.cs) require a tenant
// admin's explicit consent before the tokens can carry those roles. H3
// queries Graph to confirm the tenant admin has consented and — if not —
// transitions the run to WaitingOnGate rather than failing.
//
// SPEC / DESIGN references:
//   - spec.md FR-06 (H3 acceptance): "app-reg's 14 permissions returned by
//     Graph oauth2PermissionGrants query"
//   - spec.md NFR-09: Graph v6 / Kiota 2.0 error type — catch ODataError
//     (not ServiceException); ResponseStatusCode is int
//   - design.md §4.1 H3 row: WaitingOnGate on admin-consent-pending is a
//     resumable pause, NOT a failure
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: MI-outbound MUST rule —
//     Wave C5 real impl uses DefaultAzureCredential, never account-key
//
// DESIGN CHOICE (verifier deferred, not implemented in Wave C4):
//   Parity with NullSubscriptionReadinessProbe (task 043) and
//   NullDataverseEnvironmentRegistryClient (task 042). Wave C4 ships the
//   interface + a "Verified" null placeholder so H3 is fully unit-testable
//   without wiring up the Graph SDK against a live customer tenant. Wave C5
//   replaces with a real Microsoft.Graph SDK v6 implementation querying
//   `oauth2PermissionGrants` or `appRoleAssignments` for the BFF servicePrincipal.
//
// GATE SEMANTICS:
//   - Verified(grantedCount, expectedCount) — enough grants observed;
//     `admin-consent` gate flips to Verified; handler returns Success.
//   - Pending(grantedCount, expectedCount, diagnostic) — some grants missing;
//     handler transitions run to WaitingOnGate with `admin-consent` = Pending;
//     Reconciler re-invokes H3 after operator grants consent.
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Verifies tenant admin consent has been granted for the BFF app-registration's
/// requested Graph application roles (14 per <c>Sprk.Bff.Api.Infrastructure.
/// Auth.GraphAppRoles</c>). Domain outcomes (consent granted, consent pending)
/// return typed results; only unexpected infrastructure errors (transient
/// Graph SDK fault, network fault) should throw.
/// </summary>
public interface IAdminConsentVerifier
{
    /// <summary>
    /// Queries Microsoft Graph for the current admin-consent state on the BFF
    /// app-registration in the target tenant. Wave C5 real impl calls
    /// <c>/oauth2PermissionGrants</c> and/or <c>/servicePrincipals/{id}/appRoleAssignments</c>
    /// against the customer tenant using DefaultAzureCredential (Wave C4 uses
    /// <see cref="NullAdminConsentVerifier"/>).
    /// </summary>
    /// <param name="bffAppRegId">The BFF app-registration <c>appId</c> to verify.</param>
    /// <param name="tenantId">Target Entra tenant id (§4D I1 — mandatory).</param>
    /// <param name="expectedRoleCount">
    /// Number of Graph application roles expected per <c>GraphAppRoles.cs</c>
    /// (14 as of 2026-08-17). The verifier compares observed grants against
    /// this expected count to decide Verified vs Pending.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminConsentVerificationResult> VerifyAsync(
        string bffAppRegId,
        string tenantId,
        int expectedRoleCount,
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
