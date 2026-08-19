// -----------------------------------------------------------------------------
// BffDeployOptions.cs
//
// Bound options for the H9 handler's collaborators (r3-gate verifier + BFF
// deploy runner + slot swapper + health probe + publish-size reporter).
// Loaded from the "BffDeploy" configuration section — runtime-configurable so
// the linux-x64 App Service publish layout can be honored without recompiling.
//
// SPEC / DESIGN references:
//   - spec.md FR-12 (H9 acceptance).
//   - spec.md NFR-01 (publish-size ceiling + per-task delta reporting).
//   - spec.md NFR-05 (post-deploy /health returns 200 resolving Tier-1 KV refs).
//   - design.md §4.1 H9 row + Gap 2 (Deploy-Release.ps1 Phase 4 hardening).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Bound options for <see cref="H9BffDeployHandler"/> collaborators.
/// Configuration key: <c>BffDeploy</c>.
/// </summary>
public sealed class BffDeployOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via PATH).
    /// Parity with <see cref="BicepInfraDeploy.BicepInfraDeployOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Path to the <c>az</c> CLI executable. Defaults to <c>az</c> (resolved
    /// via PATH). On Linux App Service the operator install path is
    /// <c>/usr/bin/az</c>.
    /// </summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>
    /// Path to the <c>dotnet</c> CLI executable used by the r3-gate verifier
    /// to run <c>dotnet build</c> + <c>dotnet test</c>. Defaults to
    /// <c>dotnet</c> (resolved via PATH).
    /// </summary>
    public string DotnetExecutable { get; set; } = "dotnet";

    /// <summary>
    /// Absolute path to <c>scripts/Deploy-BffApi.ps1</c>. Defaults to
    /// <c>scripts/Deploy-BffApi.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string DeployBffApiScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Deploy-BffApi.ps1");

    /// <summary>
    /// Absolute path to <c>scripts/Deploy-Release.ps1</c>. Read by the H9
    /// handler's <c>spaarkedev1</c>-regression pre-flight scan (Gap 2 /
    /// FR-28 / §4D I1 assertion — per POML criterion 5, deploy MUST fail if
    /// Phase 4's post-Phase-B hardening has been regressed to include a
    /// hardcoded <c>spaarkedev1</c> literal).
    /// </summary>
    public string DeployReleaseScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Deploy-Release.ps1");

    /// <summary>
    /// Absolute path to the BFF publish output directory used to measure
    /// publish size for NFR-01 reporting. Defaults to <c>deploy/api-publish</c>
    /// relative to <see cref="AppContext.BaseDirectory"/> (parity with
    /// Deploy-BffApi.ps1's default publish location).
    /// </summary>
    public string BffPublishDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "deploy", "api-publish");

    /// <summary>
    /// Absolute path to the BFF publish zip used to measure the deploy-artifact
    /// compressed size (the metric spec.md NFR-01 tracks — 44.96 MB baseline
    /// 2026-08-13, ≤60 MB hard ceiling, ≥+5 MB single-task delta forces
    /// escalation). Defaults to <c>deploy/api-publish.zip</c> relative to
    /// <see cref="AppContext.BaseDirectory"/> (Deploy-BffApi.ps1 default).
    /// </summary>
    public string BffPublishZipPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "deploy", "api-publish.zip");

    /// <summary>
    /// Baseline compressed publish size in bytes for NFR-01 delta computation.
    /// Defaults to 44.96 MB (the net10 framework-dependent linux-x64 baseline
    /// per dotnet-10-upgrade-r1 task 031, 2026-08-13). Overridable via config
    /// as newer baselines land.
    /// </summary>
    public long BaselinePublishSizeBytes { get; set; } = 44_960_000L;

    /// <summary>
    /// Publish-size delta ceiling in bytes above which the H9 handler REJECTS
    /// the deploy with <see cref="BffDeployRejectionCodes.PublishSizeDeltaExceeded"/>
    /// per spec.md NFR-01 (≥+5 MB single-task delta → explicit justification
    /// required). Defaults to 5 MB.
    /// </summary>
    public long PublishSizeDeltaThresholdBytes { get; set; } = 5L * 1024L * 1024L;

    /// <summary>
    /// Absolute publish-size ceiling in bytes above which the H9 handler
    /// REJECTS the deploy per spec.md NFR-01 (≥60 MB → HARD STOP).
    /// Defaults to 60 MB.
    /// </summary>
    public long AbsolutePublishSizeCeilingBytes { get; set; } = 60L * 1024L * 1024L;

    /// <summary>
    /// Absolute path to the repository root used by the r3-gate verifier to
    /// locate <c>src/server/api/Sprk.Bff.Api/</c> for the analyzers-as-errors
    /// build and <c>tests/Spaarke.ArchTests/</c> for the god-class ratchet
    /// + ArchTest gates. Defaults to the effective repo root of the L2
    /// publish. Override via app-setting for non-standard publish layouts.
    /// </summary>
    public string RepositoryRoot { get; set; }
        = AppContext.BaseDirectory;

    /// <summary>
    /// Absolute path to <c>scripts/naming-conformance-check.ps1</c> used by
    /// the r3-gate verifier. Defaults to the canonical script path relative
    /// to <see cref="AppContext.BaseDirectory"/>. The verifier tolerates
    /// script-not-found and reports the gate as
    /// <c>Skipped</c> — parity with the other non-yet-authored r3-era gates.
    /// </summary>
    public string NamingConformanceScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "naming-conformance-check.ps1");

    /// <summary>
    /// Maximum time to wait for a single r3-gate step (dotnet build /
    /// dotnet test / naming-conformance script). Defaults to 10 minutes.
    /// The whole verifier fan-out therefore ceilings at ~50 minutes worst-case;
    /// in practice the god-class ratchet + ArchTests run in seconds.
    /// </summary>
    public TimeSpan R3GateStepTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Maximum time to wait for the <c>Deploy-BffApi.ps1</c> invocation
    /// (build + zip + deploy-to-staging-slot + slot health check). Defaults
    /// to 30 minutes.
    /// </summary>
    public TimeSpan DeployTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum time to wait for a single <c>az webapp deployment slot swap</c>
    /// invocation (staging → production, or the rollback re-swap). Defaults
    /// to 10 minutes — App Service slot swaps typically complete in 30-90 s
    /// but the ceiling absorbs cold-warm-up on large plans.
    /// </summary>
    public TimeSpan SlotSwapTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Post-swap production /health probe retry count. Defaults to 24 (matches
    /// Deploy-BffApi.ps1's <c>MaxHealthCheckRetries</c> default).
    /// </summary>
    public int HealthProbeMaxRetries { get; set; } = 24;

    /// <summary>
    /// Interval between /health probe retries. Defaults to 5 s (matches
    /// Deploy-BffApi.ps1's <c>HealthCheckIntervalSeconds</c>).
    /// </summary>
    public TimeSpan HealthProbeInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Per-request timeout for a single /health HTTP GET. Defaults to 10 s
    /// (Deploy-BffApi.ps1 parity).
    /// </summary>
    public TimeSpan HealthProbeRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default App Service staging slot name if the run parameter is absent.
    /// Defaults to <c>staging</c> (Deploy-BffApi.ps1 parity).
    /// </summary>
    public string DefaultStagingSlotName { get; set; } = "staging";

    /// <summary>
    /// Default health-check path if the run parameter is absent. Defaults to
    /// <c>/healthz</c> (Deploy-BffApi.ps1 parity + Sprk.Bff.Api's ValidateOnStart
    /// binding — r3 task 061).
    /// </summary>
    public string DefaultHealthCheckPath { get; set; } = "/healthz";
}
