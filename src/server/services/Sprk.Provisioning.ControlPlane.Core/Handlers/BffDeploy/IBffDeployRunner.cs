// -----------------------------------------------------------------------------
// IBffDeployRunner.cs
//
// L2 abstraction over the actual BFF-to-staging-slot deploy invocation. The
// production impl (<see cref="DeployBffApiScriptRunner"/>) shells out to
// <c>scripts/Deploy-BffApi.ps1 -UseSlotDeploy</c>. Unit tests inject stubs
// to avoid pwsh + Azure round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DeployBffApiScriptRunner"/> shells out to
//       Deploy-BffApi.ps1 with a customerId-driven -AppServiceName +
//       -ResourceGroupName + -UseSlotDeploy true so the script builds + zips +
//       deploys to staging + verifies staging health (steps 1–4 of the
//       script's own 7-step flow); H9 then performs the slot-swap + prod
//       smoke test + rollback in-handler for tight state coupling to Cosmos.
//     - Test: stubs injected per unit test that construct
//       <see cref="BffDeployRunnerOutcome"/> directly (see
//       <c>H9BffDeployHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// SCOPE DIVISION (script vs handler):
//   Deploy-BffApi.ps1 by itself does ALL of:
//     1. Build + zip.
//     2. Deploy to staging slot.
//     3. Verify staging /health.
//     4. Slot swap staging → production.
//     5. Verify production /health post-swap.
//     6. Rollback re-swap on failure.
//     7. Report.
//   The H9 handler needs to interleave Cosmos state writes + r3-gate
//   verification + publish-size delta reporting BETWEEN steps 3 and 4, and it
//   needs to observe + persist the outcome of steps 4 + 5 + 6 (not just the
//   final exit code). The runner therefore invokes Deploy-BffApi.ps1 with
//   -SkipSwap semantics if the script supports it, OR the handler drives the
//   full deploy via a two-phase invocation: (a) runner performs steps 1–3
//   (build + zip + staging deploy + staging health) then RETURNS the metadata
//   the handler needs; (b) the handler's IAppServiceSlotSwapper +
//   IHealthProbe perform steps 4–6 directly against ARM/HTTP so Cosmos
//   state advances precisely at the swap gate.
//
//   Wave-C4 posture: the shipped Deploy-BffApi.ps1 does NOT expose a
//   -SkipSwap switch. The runner therefore parameterizes the invocation so
//   that -UseSlotDeploy is FALSE (script builds + zips + deploys direct to
//   the STAGING slot's dedicated URL — treating the staging slot as the
//   deploy target with no swap) and the handler's own IAppServiceSlotSwapper
//   performs the swap. This preserves the script's build + zip + deploy +
//   verify staging-slot-health path (steps 1–3) without invoking the
//   script's steps 4–6 which the handler owns.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Runs the BFF build + zip + staging-slot deploy + staging-slot health check
/// for H9. Production impl shells out to
/// <c>scripts/Deploy-BffApi.ps1</c>; test impls return canned outcomes.
/// </summary>
public interface IBffDeployRunner
{
    /// <summary>
    /// Runs the deploy up to (but NOT including) the slot swap. Returns a
    /// typed outcome — Success carries the metadata H9's downstream
    /// (slot-swap + smoke-test + rollback) branch needs; Failure carries a
    /// diagnostic. Domain failures do NOT throw (parity with H2a's
    /// IBicepDeployRunner); infra faults MAY throw.
    /// </summary>
    Task<BffDeployRunnerOutcome> DeployAsync(BffDeployRunnerRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single BFF deploy invocation.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines + operator-facing messages.</param>
/// <param name="ResourceGroupName">Azure resource group hosting the BFF App Service.</param>
/// <param name="AppServiceName">BFF App Service name (production slot binds to https://{name}.azurewebsites.net).</param>
/// <param name="StagingSlotName">Staging slot name — the runner deploys HERE, not to production.</param>
/// <param name="EnvironmentName">Target environment (<c>dev</c>/<c>staging</c>/<c>production</c>) — flows into Deploy-BffApi.ps1's <c>-Environment</c>.</param>
/// <param name="BuildId">BFF CI build number — feeds the deterministic idempotency key <c>bff-{customerId}-{buildId}</c>.</param>
/// <param name="SkipBuild">If true, the runner passes <c>-SkipBuild</c> so an existing publish artifact is deployed (useful for retries).</param>
public sealed record BffDeployRunnerRequest(
    string CustomerId,
    string ResourceGroupName,
    string AppServiceName,
    string StagingSlotName,
    string EnvironmentName,
    string BuildId,
    bool SkipBuild);

/// <summary>
/// Discriminated result of <see cref="IBffDeployRunner.DeployAsync"/>. Success
/// carries the metadata H9's downstream branch needs; Failure carries a
/// diagnostic (later mapped to <see cref="BffDeployRejectionCodes.BffDeployFailed"/>
/// by the handler).
/// </summary>
public abstract record BffDeployRunnerOutcome
{
    private BffDeployRunnerOutcome() { }

    /// <summary>Deploy succeeded — outputs carry the publish artifact location + staging URL for the handler's smoke-test scaffolding.</summary>
    public sealed record Success(BffDeployRunnerOutputs Outputs) : BffDeployRunnerOutcome;

    /// <summary>
    /// Deploy failed. <paramref name="Diagnostic"/> is the operator-facing
    /// message (e.g. "Deploy-BffApi.ps1 exit 1: staging slot health check
    /// failed after 24 attempts"). Handler wraps this in a
    /// <see cref="Handlers.FailureClass.Resumable"/> §4C classification —
    /// a staging-slot deploy failure is safe to re-run (staging is
    /// dedicated + re-deployable).
    /// </summary>
    public sealed record Failure(string Diagnostic) : BffDeployRunnerOutcome;
}

/// <summary>
/// Outputs of a successful <see cref="IBffDeployRunner.DeployAsync"/>. These
/// are the metadata H9's slot-swap + smoke-test branch needs.
/// </summary>
public sealed record BffDeployRunnerOutputs
{
    /// <summary>Absolute path to the compressed publish zip Deploy-BffApi.ps1 built (for publish-size reporting).</summary>
    public required string PublishZipPath { get; init; }

    /// <summary>Publicly reachable HTTPS URL of the staging slot (e.g. <c>https://spaarke-bff-acme-staging.azurewebsites.net</c>).</summary>
    public required string StagingSlotUrl { get; init; }

    /// <summary>Publicly reachable HTTPS URL of the production slot (e.g. <c>https://spaarke-bff-acme.azurewebsites.net</c>).</summary>
    public required string ProductionSlotUrl { get; init; }

    /// <summary>Wall-clock duration of the deploy invocation (build + zip + deploy + staging health).</summary>
    public required TimeSpan Duration { get; init; }
}
