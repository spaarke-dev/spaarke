// -----------------------------------------------------------------------------
// ISubscriptionReadinessProbe.cs
//
// Abstraction over the two ARM-side checks H1 performs against the target
// customer subscription. One probe implementation ships this wave (the
// NullSubscriptionReadinessProbe placeholder); Wave C5 replaces it with a
// real ARM-backed impl (Azure.ResourceManager SDK) once the L2 App Service
// UAMI has the necessary Reader RBAC on each customer subscription.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-03
//     (H1 acceptance: az account show succeeds + Lighthouse RG scope
//     accessible for CustomerOwned)
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H1 row
//     + §3A A2 (Model 1 = SpaarkeOwned; Model 2 = CustomerOwned)
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md — MI-outbound MUST rule
//     (Wave C5 real impl uses DefaultAzureCredential, never account-key
//     credentials)
//   - .claude/adr/ADR-010-di-minimalism.md — seam justification: ≥2
//     implementations from day 1 (Null placeholder + test-only stubs; Wave
//     C5 adds AzureResourceManagerSubscriptionReadinessProbe)
//
// DESIGN CHOICE (probe deferred, not shell-out per task 041):
//   Task 041 (H0 preflight) shells out to scripts/preflight/*.ps1 — the
//   underlying quota-query logic is non-trivial (per-model TPM, per-family
//   vCPU, per-tenant env rate, cert age). H1's checks are trivially cheap
//   (az account show + az account list --query for delegation) but require
//   real Azure subscription context that Wave C4 does not yet wire. Rather
//   than author new scripts for the ~3 lines of ARM query each check needs,
//   this wave follows task 042's pattern: interface + Null placeholder now,
//   real impl (either SDK-based or shell-out) at Wave C5 alongside the
//   subscription-context wiring. Deviation from the POML "shell-out
//   consistent with 041" is documented in notes/task-043-deviations.md.
//
// NON-GOALS (Wave C4):
//   - This interface does NOT model Azure resource-provider registration
//     verification (Microsoft.KeyVault / Storage / DocumentDB / OpenAI /
//     Search / Web / ServiceBus / ManagedIdentity). That belongs to H2a's
//     pre-Bicep gate (task 044) or a Wave-C5 extension of this probe.
//     Comment in the dispatcher note is a Wave-C5 aspiration — H1's
//     acceptance criteria per spec.md FR-03 are narrower ("az account show
//     succeeds + Lighthouse RG scope").
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>
/// Executes the two ARM-side readiness checks H1 needs. The
/// <see cref="CheckSubscriptionReachableAsync"/> method runs unconditionally;
/// <see cref="CheckLighthouseDelegationAsync"/> runs only for CustomerOwned
/// tenancy (Model 2). Domain failures (subscription not found, delegation
/// missing) return <c>Passed=false</c> WITH a diagnostic; only unexpected
/// infrastructure errors (transient ARM SDK fault, network fault) should
/// throw.
/// </summary>
public interface ISubscriptionReadinessProbe
{
    /// <summary>
    /// Verifies the target subscription is reachable via ARM (equivalent to
    /// <c>az account show --subscription {subscriptionId}</c>). MUST use
    /// <c>DefaultAzureCredential</c> (Wave C5 real impl) — no account-key
    /// credentials per ADR-028 MI-outbound rule.
    /// </summary>
    /// <param name="subscriptionId">
    /// Target Azure subscription id (GUID). Never a default-fallback value —
    /// the handler enforces §4D I1 (missing → HandlerResult.Failure BEFORE
    /// this method fires).
    /// </param>
    /// <param name="tenantId">
    /// Target Entra tenant id. For SpaarkeOwned tenancy this is Spaarke's
    /// tenant; for CustomerOwned it is the customer's tenant (captured via
    /// H0.5 consent-callback). Handler enforces §4D I1 non-empty.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SubscriptionReadinessCheckResult> CheckSubscriptionReachableAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies Lighthouse delegation is in place from the customer tenant
    /// (<paramref name="tenantId"/>) to Spaarke's managing tenant AND the
    /// delegated resource-group scope is accessible. Only invoked by the
    /// handler when <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.TenancyModel"/>
    /// denotes CustomerOwned (Model 2). Wave C5 real impl queries ARM for
    /// <c>Microsoft.ManagedServices/registrationAssignments</c> under the
    /// customer subscription.
    /// </summary>
    /// <param name="subscriptionId">Target customer subscription id (GUID).</param>
    /// <param name="tenantId">Customer Entra tenant id (delegation source).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SubscriptionReadinessCheckResult> CheckLighthouseDelegationAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="ISubscriptionReadinessProbe"/> check invocation.
/// Mirrors the shape of <see cref="Preflight.PreflightCheckResult"/> so a
/// future migration to a unified probe contract is trivial.
/// </summary>
/// <param name="Passed">
/// <c>true</c> when the check succeeded; <c>false</c> on any failure.
/// </param>
/// <param name="Diagnostic">
/// Human-readable diagnostic. On failure MUST cite BOTH the observed state
/// (subscription id / tenant id / RG scope tried) AND the remediation path
/// (grant Reader / accept Lighthouse offer / verify subscription exists) so
/// the operator can resolve the missing precondition without further
/// probing.
/// </param>
/// <param name="Evidence">
/// Arbitrary per-check evidence (raw ARM response body, retrieved
/// registrationAssignment properties, etc.). Written to
/// <c>ProvisioningRun.GateStates[gateId].Evidence</c> so the operator sees
/// the raw diagnostic without pulling logs. May be <c>null</c> when the
/// check has no structured evidence to carry.
/// </param>
public sealed record SubscriptionReadinessCheckResult(
    bool Passed,
    string Diagnostic,
    JsonElement? Evidence);
