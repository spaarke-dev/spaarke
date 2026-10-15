// -----------------------------------------------------------------------------
// BffDeployOptions.cs
//
// Bound options for the H9 handler's collaborators (artifact manifest
// verifier + blob artifact downloader + Kudu zip-deployer + slot swapper +
// health probe + publish-size reporter). Loaded from the "BffDeploy"
// configuration section — runtime-configurable so the linux-x64 App Service
// publish layout can be honored without recompiling.
//
// TASK 132 (Wave G-3, DS-4 §5 re-scope) ADDITIONS:
//   ProvisioningArtifactsContainerUri / ArtifactManifestBlobName /
//   LocalArtifactDownloadDirectory / KuduZipDeployTimeout — feed the new
//   ArtifactManifestVerifier + BlobArtifactDownloader + KuduZipDeployer
//   collaborators. ProvisioningArtifactsContainerUri naming matches task
//   123's BicepInfraDeployOptions.ProvisioningArtifactsContainerUri EXACTLY
//   (same underlying blob container — deploy-bff-api.yml pushes BOTH the
//   BFF zip/manifest AND publish-provisioning-arm-artifacts.yml pushes the
//   ARM JSON to the SAME `provisioning-artifacts` container; this handler
//   constructs its OWN BlobContainerClient rather than sharing H2a's DI
//   registration, per this project's established "self-contained against
//   sibling handler ports" convention — see Worker/Program.cs's H2a
//   registration comment). The retired shell-out collaborators
//   (DeployBffApiScriptRunner / AzCliAppServiceSlotSwapper /
//   DotnetR3GateVerifier) are gone from disk; task 253 (G38) deleted the
//   pwsh / az / dotnet / script-path fields only they read.
//
// SPEC / DESIGN references:
//   - spec.md FR-12 (H9 acceptance).
//   - spec.md NFR-01 (publish-size ceiling + per-task delta reporting).
//   - spec.md NFR-05 (post-deploy /health returns 200 resolving Tier-1 KV refs).
//   - design.md §4.1 H9 row + Gap 2 (Deploy-Release.ps1 Phase 4 hardening).
//   - notes/design-study-ds4-handler-audit.md §5 (H9 artifact-based re-scope).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Bound options for <see cref="H9BffDeployHandler"/> collaborators.
/// Configuration key: <c>BffDeploy</c>.
/// </summary>
public sealed class BffDeployOptions
{
    // Task 253 (G38): PwshExecutable, AzCliExecutable, DeployBffApiScriptPath, BffPublishDirectory and DeployTimeout
    // were DELETED — they served the retired Deploy-BffApi.ps1 / az shell-outs (task 132); nothing read them, and the
    // Worker host has no pwsh or az.

    // DeployReleaseScriptPath and BffPublishZipPath were DELETED (task 253 follow-up, 2026-10-09): the first fed a scan
    // of a script H9 never runs and the Worker never carries; the second was never read (H9 measures the zip it
    // downloads from the artifact store).

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

    // Task 253 (G38): RepositoryRoot, NamingConformanceScriptPath, R3GateStepTimeout and DotnetExecutable were
    // DELETED with the retired DotnetR3GateVerifier / IR3GateVerifier (dotnet + pwsh shell-outs, unregistered since
    // task 132) — the five r3-era gates run in CI.

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
    /// Default App Service staging slot name if <c>run.InterStepState.AppServiceStagingSlotName</c>
    /// (H2a's output) is blank.
    /// Defaults to <c>staging</c> (Deploy-BffApi.ps1 parity).
    /// </summary>
    public string DefaultStagingSlotName { get; set; } = "staging";

    /// <summary>
    /// Default health-check path if the run parameter is absent. Defaults to
    /// <c>/healthz</c> (Deploy-BffApi.ps1 parity + Sprk.Bff.Api's ValidateOnStart
    /// binding — r3 task 061).
    /// </summary>
    public string DefaultHealthCheckPath { get; set; } = "/healthz";

    // ---- task 132 (Wave G-3, DS-4 §5 re-scope) additions ----

    /// <summary>
    /// Base URI of the `provisioning-artifacts` blob container (e.g.
    /// <c>https://{account}.blob.core.windows.net/provisioning-artifacts</c>).
    /// Same underlying container task 116's <c>deploy-bff-api.yml</c> +
    /// task 117's <c>publish-provisioning-arm-artifacts.yml</c> both push to
    /// — naming matches <c>BicepInfraDeployOptions.ProvisioningArtifactsContainerUri</c>
    /// EXACTLY. Required — <see cref="Validate"/> throws if blank.
    /// </summary>
    public string ProvisioningArtifactsContainerUri { get; set; } = string.Empty;

    /// <summary>
    /// Blob name of task 116's manifest — the mutable "latest" pointer
    /// <see cref="ArtifactManifestVerifier"/> reads. Defaults to
    /// <c>latest.json</c> (matches <c>deploy-bff-api.yml</c>'s "Generate +
    /// push latest.json manifest" step exactly).
    /// </summary>
    public string ArtifactManifestBlobName { get; set; } = "latest.json";

    /// <summary>
    /// Local directory <see cref="BlobArtifactDownloader"/> streams the
    /// resolved artifact zip to before Kudu zip-deploy + NFR-01 size
    /// measurement. Defaults to <c>deploy/bff-artifacts</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string LocalArtifactDownloadDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "deploy", "bff-artifacts");

    /// <summary>
    /// Maximum time to wait for a single Kudu zip-deploy POST to complete
    /// (synchronous — no <c>?isAsync=true</c> polling). Defaults to 15
    /// minutes; a zip-deploy of a ~45 MB linux-x64 publish typically
    /// completes in 1-3 minutes, the ceiling absorbs cold Kudu instances.
    /// </summary>
    public TimeSpan KuduZipDeployTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Validates the task-132 additions that have no safe default (per this
    /// project's fail-fast config posture — parity with
    /// <c>BicepInfraDeployOptions.Validate</c>). Wired via
    /// <c>PostConfigure&lt;BffDeployOptions&gt;(o =&gt; o.Validate())</c> in
    /// Worker/Program.cs.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProvisioningArtifactsContainerUri))
        {
            throw new InvalidOperationException(
                "BffDeployOptions:ProvisioningArtifactsContainerUri is required — H9's " +
                "ArtifactManifestVerifier/BlobArtifactDownloader cannot resolve the manifest/artifact " +
                "blob without it. Set the app-setting to the provisioning-artifacts storage account " +
                "task 116's blob-push targets (live-ceremony item — see project current-task.md).");
        }
        if (string.IsNullOrWhiteSpace(ArtifactManifestBlobName))
        {
            throw new InvalidOperationException("BffDeployOptions:ArtifactManifestBlobName is required.");
        }
    }
}
