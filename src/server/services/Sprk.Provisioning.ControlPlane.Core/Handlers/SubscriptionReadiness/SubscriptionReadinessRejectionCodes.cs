// -----------------------------------------------------------------------------
// SubscriptionReadinessRejectionCodes.cs
//
// Machine-stable rejection codes emitted by H1SubscriptionReadinessHandler on
// HandlerResult.Failure. Operators + BFF surface these verbatim so external
// tooling (dashboards, runbook lookups) can pattern-match without parsing
// English diagnostics.
//
// DESIGN REF:
//   - HandlerResult.Failure.RejectionCode (Handlers/HandlerResult.cs)
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-03 (H1 acceptance
//     — each failure branch has a distinct code)
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1 (tenant
//     hardcoding forbidden — MissingTenantId + MissingSubscriptionId enforce)
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H1 row +
//     §4C rollback (H1 failures are Resumable — operator resolves the missing
//     precondition + POST /api/runs/{id}/resume)
//
// PATTERN PARITY:
//   Mirrors Handlers/ConsentCapture/ConsentCaptureRejectionCodes.cs (task 042)
//   — one const per failure branch + lowercase kebab-case for greppability +
//   runbook lookups.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>
/// Stable rejection codes returned by <c>H1SubscriptionReadinessHandler</c>
/// on <c>HandlerResult.Failure</c>. Values are lowercase kebab-case so grep
/// + runbook lookup are trivial (parity with
/// <see cref="ConsentCapture.ConsentCaptureRejectionCodes"/>).
/// </summary>
public static class SubscriptionReadinessRejectionCodes
{
    /// <summary>
    /// Cosmos partition read returned no matching ProvisioningRun for
    /// <c>(customerId, runId)</c>. Reconciler treats as Resumable (operator
    /// verifies the run exists + resumes).
    /// </summary>
    public const string RunNotFound = "subready-run-not-found";

    /// <summary>
    /// <c>run.Parameters.NonSecret["subscriptionId"]</c> was null / whitespace.
    /// §4D I1 forbids a default-subscription fallback — operator must supply
    /// the target subscriptionId in the run's non-secret parameters.
    /// </summary>
    public const string MissingSubscriptionId = "subready-missing-subscription-id";

    /// <summary>
    /// <c>run.Parameters.NonSecret["tenantId"]</c> was null / whitespace.
    /// §4D I1 forbids a default-tenant fallback — operator must supply the
    /// target tenantId in the run's non-secret parameters.
    /// </summary>
    public const string MissingTenantId = "subready-missing-tenant-id";

    /// <summary>
    /// <c>run.TenancyModel</c> was null / whitespace OR was an unrecognized
    /// value (not one of <c>SpaarkeOwned</c> / <c>Model1Shared</c> /
    /// <c>CustomerOwned</c> / <c>Model2Dedicated</c>). Handler cannot decide
    /// whether the Lighthouse branch applies — Resumable.
    /// </summary>
    public const string InvalidTenancyModel = "subready-invalid-tenancy-model";

    /// <summary>
    /// ARM subscription-show against the target <c>subscriptionId</c> failed —
    /// either the subscription does not exist, the L2 UAMI lacks Reader
    /// permission on the sub, or ARM is transiently unreachable. Operator
    /// resolves the config / permission / connectivity issue and resumes.
    /// </summary>
    public const string SubscriptionUnreachable = "subready-subscription-unreachable";

    /// <summary>
    /// CustomerOwned tenancy — Lighthouse delegation from the customer tenant
    /// to Spaarke's managing tenant is MISSING or the delegated
    /// resource-group scope is inaccessible. Operator (or customer admin)
    /// must complete the Lighthouse offer acceptance before H1 can pass.
    /// </summary>
    public const string LighthouseDelegationMissing = "subready-lighthouse-delegation-missing";

    /// <summary>
    /// The subscription-readiness probe itself threw an unanticipated
    /// exception. <c>ArmSubscriptionReadinessProbe</c> (task 121) catches
    /// <c>RequestFailedException</c> internally and returns a domain failure
    /// (<see cref="SubscriptionUnreachable"/> / <see cref="LighthouseDelegationMissing"/>)
    /// instead of throwing, so this code is reserved for a genuine bug in the
    /// probe implementation, not an ARM-side rejection.
    /// </summary>
    public const string ProbeInfrastructureError = "subready-probe-infrastructure-error";

    /// <summary>
    /// Optimistic-concurrency race: another writer advanced the run row
    /// between H1's read and its state-mutating write. Reconciler resumes
    /// (safe: H1 re-runs the probes and rediscovers the winning state).
    /// </summary>
    public const string ConcurrentWriteConflict = "subready-concurrent-write-conflict";

    /// <summary>
    /// The run row was deleted between H1's read and its state-mutating
    /// write. Rare — surfaces the row-delete race distinctly from ETag
    /// conflict so the operator knows to recreate the run rather than
    /// resume.
    /// </summary>
    public const string RunDeletedDuringCheck = "subready-run-deleted-during-check";

    /// <summary>
    /// HANDLER-04 (Wave 2 pre-dispatch remediation 2026-08-27) — F6 verbatim.
    /// One or more required Azure resource providers did NOT reach
    /// <c>Registered</c> state within the H1 poll timeout (default 5 min).
    /// Diagnostic cites which providers failed + their observed
    /// registrationState so the operator can escalate manually via
    /// <c>az provider register</c> under an elevated identity.
    /// </summary>
    public const string ProviderRegistrationFailed = "subready-provider-registration-failed";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c>
/// by H1 subscription-readiness. Kept as string constants so grep across the
/// codebase finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class SubscriptionReadinessGates
{
    /// <summary>
    /// The gate H1 owns for the CustomerOwned tenancy branch — flipped from
    /// <c>Pending</c> to <c>Verified</c> when the Lighthouse delegation
    /// check passes. Value matches design.md §6.2 gateStates naming
    /// convention (kebab-case).
    /// </summary>
    public const string LighthouseDelegation = "lighthouse-delegation";

    /// <summary>
    /// The gate H1 owns for the ARM subscription-reachability check —
    /// flipped from <c>Pending</c> to <c>Verified</c> when
    /// <c>az account show</c> equivalent succeeds against the target sub.
    /// Applies to BOTH Spaarke-owned and Customer-owned tenancies.
    /// </summary>
    public const string SubscriptionReachable = "subscription-reachable";
}
