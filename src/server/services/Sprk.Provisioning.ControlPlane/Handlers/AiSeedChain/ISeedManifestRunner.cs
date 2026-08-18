// -----------------------------------------------------------------------------
// ISeedManifestRunner.cs
//
// L2 abstraction over the actual invocation of Invoke-SeedManifest.ps1 (task
// 069). Production impl shells out with -Live + -EnvironmentUrl; unit tests
// inject stubs to avoid pwsh + real Dataverse round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="InvokeSeedManifestScriptRunner"/> — shells out
//       to scripts/seed-data/Invoke-SeedManifest.ps1 with the customer's
//       Dataverse URL + -Live + captures stdout/stderr.
//     - Test: stubs injected per unit test that construct
//       <see cref="SeedManifestInvocationOutcome"/> directly (see
//       <c>H12aAiSeedChainHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs re-implementation):
//   Task 069's Invoke-SeedManifest.ps1 IS the source-of-truth for the seed
//   chain — topological sort, dependency resolution, per-artifact deployer
//   dispatch, retired-artifact enforcement, dry-run vs live mode. Re-
//   implementing that in C# would duplicate the logic across two files that
//   would drift; instead H12a WRAPS the script (adding Cosmos state
//   management + idempotency-key semantics + startup verification that the
//   script does not perform). Parity with
//   <see cref="BicepInfraDeploy.ProvisionCustomerScriptBicepDeployRunner"/>.
//
// POML CONSTRAINT COMPLIANCE:
//   "Handler MUST NOT invoke seeder scripts directly — MUST go through
//   Invoke-SeedManifest.ps1 so the single-source-of-truth manifest governs."
//   This runner is the single call site — the handler never touches individual
//   Deploy-*.ps1 scripts.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Executes the task-069 Invoke-SeedManifest.ps1 orchestrator against a
/// target customer Dataverse environment. Production impl shells out to
/// pwsh; test impls return canned <see cref="SeedManifestInvocationOutcome"/>s.
/// </summary>
public interface ISeedManifestRunner
{
    /// <summary>
    /// Invokes Invoke-SeedManifest.ps1 with <c>-Live</c> + <c>-EnvironmentUrl</c>
    /// bound to <paramref name="request"/>. Returns a typed outcome — Success
    /// carries the stdout summary for observability; Failure carries the
    /// captured stderr + tail-of-stdout for operator diagnosis. Domain
    /// failures do NOT throw; infrastructure faults (script missing, pwsh
    /// missing, timeout) MAY throw. Parity with
    /// <see cref="BicepInfraDeploy.IBicepDeployRunner.DeployAsync"/>.
    /// </summary>
    /// <param name="request">Seed inputs (customerId, tenantId, target Dataverse URL).</param>
    /// <param name="cancellationToken">Cancellation token — the long-running seed MUST honor it.</param>
    Task<SeedManifestInvocationOutcome> InvokeAsync(
        SeedManifestInvocationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Invoke-SeedManifest.ps1 invocation. The runner passes
/// <see cref="TargetDataverseUrl"/> as the script's <c>-EnvironmentUrl</c>
/// parameter; <see cref="TenantId"/> flows via env var so the seeder's
/// downstream Dataverse-client scripts can attach the correct auth scope.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit, never default).</param>
/// <param name="TargetDataverseUrl">Target customer Dataverse env URL (e.g. https://spaarke-acme.crm.dynamics.com). Populated by H5/H6 into <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.DataverseEnvUrl"/>.</param>
public sealed record SeedManifestInvocationRequest(
    string CustomerId,
    string TenantId,
    string TargetDataverseUrl);

/// <summary>
/// Discriminated result of <see cref="ISeedManifestRunner.InvokeAsync"/>.
/// Success carries the run's summary; Failure carries a runner-side diagnostic
/// (later mapped to <see cref="AiSeedChainRejectionCodes.SeedManifestInvocationFailed"/>
/// by the handler).
/// </summary>
public abstract record SeedManifestInvocationOutcome
{
    private SeedManifestInvocationOutcome() { }

    /// <summary>Seed invocation succeeded (exit 0). <paramref name="StdoutSummary"/> is the tail-of-stdout for run-notes.</summary>
    public sealed record Success(string StdoutSummary) : SeedManifestInvocationOutcome;

    /// <summary>
    /// Seed invocation failed. <paramref name="Diagnostic"/> is the operator-
    /// facing message that includes exit code + stderr + stdout tail so the
    /// operator can classify (retired-artifact violation vs cyclic dep vs
    /// per-seeder Dataverse failure) without opening the runNotes file.
    /// </summary>
    public sealed record Failure(string Diagnostic) : SeedManifestInvocationOutcome;
}
