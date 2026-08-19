// -----------------------------------------------------------------------------
// IBicepDeployRunner.cs
//
// L2 abstraction over the actual per-customer Bicep deploy invocation. The
// production implementation
// (<see cref="ProvisionCustomerScriptBicepDeployRunner"/>) shells out to
// <c>scripts/Provision-Customer.ps1</c> (the hardened wrapper around
// <c>customer.bicep</c> / <c>stacks/model1-shared.bicep</c> / etc.). Unit
// tests inject stubs to avoid pwsh + Azure round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="ProvisionCustomerScriptBicepDeployRunner"/> —
//       shells out to Provision-Customer.ps1 with parsed outputs.
//     - Test: stubs injected per unit test that construct
//       <see cref="BicepDeployOutcome"/> directly (see
//       <c>H2aBicepInfraDeployHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs SDK):
//   Same trade-off as H0 preflight probes (see
//   <c>Handlers/Preflight/IPreflightQuotaProbe.cs</c>): the PS script IS the
//   source-of-truth for the 13-step provisioning orchestration + naming
//   conventions + state-file resume logic. Re-implementing in C# via
//   Azure.ResourceManager SDK is a large surface area that duplicates the
//   Bicep/az CLI flow the script already tested. H2a therefore WRAPS the
//   script (adding post-condition verification the script does not perform,
//   per POML acceptance §4B T1) rather than replacing it.
//
// UPGRADE-MODE INTEGRATION:
//   H2a itself (NOT the runner) owns the upgrade-mode branch:
//     1. Call <see cref="IUpgradeDriftDetector"/> FIRST to `what-if` the
//        deploy.
//     2. If drift → REJECT + write drift report + return
//        Failure(QuarantineRequired, upgrade-mode-drift).
//     3. Otherwise call this runner to perform the actual deploy.
//   The runner is drift-agnostic — it either deploys or it doesn't.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Executes the per-customer Bicep deploy for handler H2a. Production impl
/// shells out to <c>scripts/Provision-Customer.ps1</c>; test impls return
/// canned <see cref="BicepDeployOutcome"/>s.
/// </summary>
public interface IBicepDeployRunner
{
    /// <summary>
    /// Runs the deploy. Returns a typed outcome — success carries the outputs
    /// consumed by downstream handlers (UAMI resource id, endpoint URIs);
    /// failure carries a diagnostic. Domain failures do NOT throw (parity
    /// with <see cref="IPreflightQuotaProbe"/> — infra faults MAY throw).
    /// </summary>
    /// <param name="request">Deploy inputs (customerId, tenantId, subscription id, bicep-repo git SHA, tenancy model, feature flags).</param>
    /// <param name="cancellationToken">Cancellation token — a long-running deploy MUST honor it.</param>
    Task<BicepDeployOutcome> DeployAsync(BicepDeployRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Bicep deploy invocation. Immutable record; the caller
/// (<see cref="H2aBicepInfraDeployHandler"/>) constructs one per run from
/// <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>
/// + <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.TenancyModel"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit, never default).</param>
/// <param name="SubscriptionId">Target subscription id (ADR-027 D4 — customer subscription, never platform).</param>
/// <param name="TenancyModel">
/// <c>Model1Shared</c> or <c>Model2Dedicated</c> per <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.TenancyModel"/>.
/// Selects the Bicep stack: Model1Shared → <c>stacks/model1-shared.bicep</c>;
/// Model2Dedicated → <c>customer.bicep</c> (or <c>stacks/model2-full.bicep</c>).
/// </param>
/// <param name="BicepVersion">
/// git SHA of the <c>infrastructure/bicep/</c> tree — feeds the idempotency
/// key <c>infra-{customerId}-{bicepVer}</c> per POML constraint + spec FR-04.
/// </param>
/// <param name="EnvironmentName">Target environment (<c>dev</c> / <c>staging</c> / <c>prod</c>) — feeds naming per §7.1.</param>
/// <param name="Location">Azure region for all customer resources (default westus2 per <c>customer.bicep</c>).</param>
/// <param name="SignalREnabled">Feature-gate for the SignalR resource (ADR-032 Null-Object kill-switch — see §7.2 #13).</param>
public sealed record BicepDeployRequest(
    string CustomerId,
    string TenantId,
    string SubscriptionId,
    string TenancyModel,
    string BicepVersion,
    string EnvironmentName,
    string Location,
    bool SignalREnabled);

/// <summary>
/// Discriminated result of <see cref="IBicepDeployRunner.DeployAsync"/>.
/// Success carries the deploy outputs; Failure carries a runner-side
/// diagnostic (later mapped to <see cref="BicepDeployRejectionCodes.BicepDeployFailed"/>
/// by the handler).
/// </summary>
public abstract record BicepDeployOutcome
{
    private BicepDeployOutcome() { }

    /// <summary>Deploy succeeded — outputs carry the resource IDs / endpoints the T1 probe + interStepState writes need.</summary>
    public sealed record Success(BicepDeployOutputs Outputs) : BicepDeployOutcome;

    /// <summary>
    /// Deploy failed. <paramref name="Diagnostic"/> is the operator-facing message
    /// (e.g. "az deployment sub create exit 1: Cognitive Services capacity 0/150
    /// for gpt-4o in eastus"). Handler wraps this in a
    /// <see cref="Handlers.FailureClass.QuarantineRequired"/> §4C classification —
    /// partial Bicep deploys can leave orphaned resources.
    /// </summary>
    public sealed record Failure(string Diagnostic) : BicepDeployOutcome;
}
