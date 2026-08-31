// -----------------------------------------------------------------------------
// ISeedManifestRunner.cs
//
// L2 abstraction over the actual invocation of the seed-manifest run.
// Production impl (task 150) parses task-069's scripts/seed-data/manifest.yaml
// via YamlDotNet + writes directly to the target Dataverse env via the Web
// API; unit tests inject stubs to avoid real Dataverse round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DataverseWebApiSeedWriter"/> (task 150) —
//       parses manifest.yaml via <see cref="YamlSeedManifestEngine"/>,
//       computes the topological seed order, and writes directly to the
//       customer's Dataverse env via HttpClient + DefaultAzureCredential
//       (the exact idiom H12c's DataverseWebApiModelDeploymentReferenceWriter
//       already uses in-process — see that file's header + task 150's file
//       header "SCOPE BOUNDARY" note for which artifacts it seeds).
//     - Test: stubs injected per unit test that construct
//       <see cref="SeedManifestInvocationOutcome"/> directly (see
//       <c>H12aAiSeedChainHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// HISTORY: prior to task 150, production was <c>InvokeSeedManifestScriptRunner</c>
// (deleted) — a pwsh process shell-out to scripts/seed-data/Invoke-SeedManifest.ps1,
// which itself required a second PowerShell YAML-parsing module (the DS-1b
// matrix-correction finding task 150 closes). Option D's SDK-port program
// (design.md §4.1b / DS-1b §2) forbids any NEW process-shell-out collaborator
// remaining in H12a's target state; task 150 removed the old one rather than
// adding a second parallel Dataverse-write helper.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Executes the task-069 Invoke-SeedManifest.ps1 orchestrator against a
/// target customer Dataverse environment. Production impl (task 150) parses
/// manifest.yaml + writes directly via the Dataverse Web API; test impls
/// return canned <see cref="SeedManifestInvocationOutcome"/>s.
/// </summary>
public interface ISeedManifestRunner
{
    /// <summary>
    /// Seeds every manifest artifact this runner supports (in topological
    /// order) against <paramref name="request"/>'s target Dataverse
    /// environment. Returns a typed outcome — Success carries a per-artifact
    /// summary for observability; Failure carries a diagnostic identifying
    /// which artifact failed + how many rows upserted before the failure
    /// (existence-check-then-insert is retry-safe). Domain failures do NOT
    /// throw; infrastructure faults (token acquisition, manifest parse) MAY
    /// throw. Parity with
    /// <see cref="BicepInfraDeploy.IBicepDeployRunner.DeployAsync"/>.
    /// </summary>
    /// <param name="request">Seed inputs (customerId, tenantId, target Dataverse URL).</param>
    /// <param name="cancellationToken">Cancellation token — the long-running seed MUST honor it.</param>
    Task<SeedManifestInvocationOutcome> InvokeAsync(
        SeedManifestInvocationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single seed-manifest invocation. <see cref="TenantId"/> scopes
/// the <c>DefaultAzureCredential</c> token acquisition (§4D I5 — explicit
/// per-tenant scope, never a default-tenant credential).
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
