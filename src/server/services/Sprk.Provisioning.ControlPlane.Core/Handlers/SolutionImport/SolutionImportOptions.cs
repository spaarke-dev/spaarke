// -----------------------------------------------------------------------------
// SolutionImportOptions.cs
//
// Bound options for the H6 handler's collaborators (pwsh script importer +
// verifier). Loaded from the "SolutionImportOptions" configuration section
// by Program.cs — runtime-configurable so the linux-x64 App Service publish
// layout can be honored without recompiling. Parity with
// DataverseEnvCreationOptions + AiSeedChainOptions.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Bound options for <see cref="H6SolutionImportHandler"/> collaborators.
/// Configuration key: <c>SolutionImportOptions</c>.
/// </summary>
public sealed class SolutionImportOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="AiSeedChain.AiSeedChainOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Path to the pac CLI executable. Defaults to <c>pac</c> (resolved via
    /// PATH). Parity with <see cref="DataverseEnvCreation.DataverseEnvCreationOptions.PacCliExecutable"/>.
    /// Used by <see cref="PacCliSolutionVerifier"/> to query the target env's
    /// installed solutions post-import.
    /// </summary>
    public string PacCliExecutable { get; set; } = "pac";

    /// <summary>
    /// Absolute path to <c>scripts/Deploy-DataverseSolutions.ps1</c> — the
    /// wave-0 (task 012) hardened Package Deployer script whose
    /// <c>$SolutionImportOrder</c> IS the R5 binding solution-list authority
    /// (task 008 audit + POML constraint 3). Defaults relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments override
    /// via app-setting when the publish layout differs.
    /// </summary>
    public string DeployDataverseSolutionsScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Deploy-DataverseSolutions.ps1");

    /// <summary>
    /// Directory containing the managed-solution ZIPs. Defaults to
    /// <c>src/solutions</c> relative to <see cref="AppContext.BaseDirectory"/>;
    /// production deployments override so the L2 publish layout is honored
    /// (may live in a sidecar container or a mounted share). Passed through
    /// as <c>-SolutionPath</c> to <see cref="DeployDataverseSolutionsScriptPath"/>.
    /// </summary>
    public string SolutionPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "src", "solutions");

    /// <summary>
    /// Maximum wall-clock time for a single <see cref="DeployDataverseSolutionsScriptPath"/>
    /// invocation. Defaults to 60 minutes — 8 solutions × up to 5 min per
    /// large solution + per-tier verification overhead. If exceeded, the
    /// importer returns Timeout which the handler maps to
    /// <see cref="SolutionImportRejectionCodes.ImportTimeout"/> (Resumable —
    /// operator inspects `pac solution list` and resumes idempotently).
    /// </summary>
    public TimeSpan ImportTimeout { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Maximum wall-clock time for a single <c>pac solution list</c> call
    /// used by the verifier. Defaults to 90 seconds — a solution list on a
    /// healthy env returns in under 10 s but slow tenants may take longer.
    /// </summary>
    public TimeSpan VerifierCallTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Effective mode passed as <c>-Mode</c> to the PS script.
    /// <list type="bullet">
    /// <item><c>Auto</c> (default) — per-solution detect: existing → upgrade, absent → fresh install.</item>
    /// <item><c>FreshInstall</c> — treat every solution as fresh install; fails if any target already exists.</item>
    /// <item><c>Upgrade</c> — treat every solution as an upgrade via <c>--stage-and-upgrade</c>; retires the holding solution per spec.md FR-09.</item>
    /// </list>
    /// </summary>
    public string ImportMode { get; set; } = "Auto";

    /// <summary>
    /// Client secret for the BFF Entra app registration (used by the PS script's
    /// <c>pac auth create --clientSecret</c> path). MUST be null / whitespace
    /// in checked-in configs; the wave-C5 KV wiring populates this via a
    /// Key Vault app-setting reference <c>@Microsoft.KeyVault(SecretUri=…)</c>.
    /// Handler emits <see cref="SolutionImportRejectionCodes.MissingClientSecret"/>
    /// if unset when H6 dispatches.
    /// </summary>
    /// <remarks>
    /// For wave-C4 unit tests, this is set to a non-empty placeholder via
    /// <c>Options.Create(...)</c> so tests exercise the happy path without
    /// depending on a real KV. Task 025's CosmosProvisioningSecretGuard
    /// ArchTest applies to Cosmos writes — this options-bound field is NOT
    /// persisted to Cosmos (it flows through the runner's env vars only).
    /// </remarks>
    public string? ClientSecret { get; set; }

    // ---- task 141 (Wave G-4, Option D hybrid) additions ----
    // The fields above (PwshExecutable/PacCliExecutable/DeployDataverseSolutionsScriptPath/
    // SolutionPath/ImportMode) remain UNCHANGED — they stay load-bearing for the
    // retired-but-kept-on-disk DeployDataverseSolutionsScriptImporter /
    // PacCliSolutionVerifier (Wave G retirement convention: retain fields so
    // retired files still compile). The fields below feed the NEW
    // DataverseWebApiSolutionImporter / DataverseWebApiSolutionVerifier.

    /// <summary>
    /// Base URI of the `provisioning-artifacts` blob container (e.g.
    /// <c>https://{account}.blob.core.windows.net/provisioning-artifacts</c>).
    /// SAME underlying container task 116's <c>deploy-bff-api.yml</c> +
    /// task 117's ARM-JSON publish + task 132's H9 artifact both use — naming
    /// matches <c>BffDeployOptions.ProvisioningArtifactsContainerUri</c> /
    /// <c>BicepInfraDeployOptions.ProvisioningArtifactsContainerUri</c>
    /// EXACTLY (DS-1b §1 H6 row: "coordinate the packaging mechanism with
    /// task 116's blob-artifact pattern"). Required for
    /// <see cref="DataverseWebApiSolutionImporter"/> to resolve the 8 solution
    /// ZIPs as versioned artifacts rather than a local filesystem path.
    /// </summary>
    public string ProvisioningArtifactsContainerUri { get; set; } = string.Empty;

    /// <summary>
    /// Blob name of the solution-artifact manifest — the mutable "latest"
    /// pointer <see cref="DataverseWebApiSolutionImporter"/> reads to resolve
    /// each of the 8 catalog solutions' blob name (+ optional version) inside
    /// <see cref="ProvisioningArtifactsContainerUri"/>. Defaults to
    /// <c>dataverse-solutions-latest.json</c> (parity with H9's
    /// <c>latest.json</c> naming convention). Manifest shape:
    /// <c>{"solutions":{"SpaarkeCore":{"blobName":"SpaarkeCore-1.0.0.4.zip","version":"1.0.0.4"}, ...}}</c>
    /// keyed by <see cref="CanonicalSolutionEntry.SolutionUniqueName"/>.
    /// </summary>
    /// <remarks>
    /// LIVE-CEREMONY GAP (documented, not a defect of this task): no CI
    /// workflow publishes this manifest yet as of task 141 — grep of
    /// <c>.github/workflows/**</c> confirmed no solution-ZIP publish step
    /// exists (task 116/117 publish the BFF zip + ARM JSON only). This
    /// handler is fully buildable/unit-testable today (fake-transport tests
    /// exercise the manifest-driven resolution path); a live E2E run
    /// additionally requires (a) the provisioning-artifacts storage account
    /// to exist (Wave G-1 live-ceremony backlog item #4) and (b) a NEW CI
    /// publish step producing this manifest + the 8 solution ZIPs — tracked
    /// as a follow-on live-ceremony backlog item, out of scope for task 141's
    /// 2-collaborator-file deliverable per the POML's <c>&lt;outputs&gt;</c>.
    /// </remarks>
    public string SolutionArtifactManifestBlobName { get; set; } = "dataverse-solutions-latest.json";

    /// <summary>
    /// Per-HTTP-request timeout for a single Dataverse Web API call (existing-
    /// solutions GET, ImportSolution/StageAndUpgrade POST, or a single
    /// importjobs poll GET). Defaults to 100 seconds — generous headroom for
    /// the ImportSolution/StageAndUpgrade POST's base64-encoded solution ZIP
    /// payload (largest of the 8 solutions is still well under Dataverse's
    /// binary-parameter size limits). Distinct from <see cref="ImportTimeout"/>
    /// (the OVERALL 8-solution deadline).
    /// </summary>
    public TimeSpan DataverseWebApiRequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Interval between <c>importjobs({ImportJobId})</c> poll GETs while
    /// waiting for a single solution's import to reach a terminal state
    /// (<c>completedon</c> non-null). Defaults to 15 seconds — solution
    /// imports run minutes, not seconds, so a 15 s cadence keeps API call
    /// volume low across the fleet's longest-running handler (DS-2b §1.2)
    /// without meaningfully delaying terminal-state detection.
    /// </summary>
    public TimeSpan ImportJobPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Validates the task-141 additions that have no safe default (parity
    /// with <c>BffDeployOptions.Validate</c> / <c>BicepInfraDeployOptions.Validate</c>).
    /// Wired via <c>PostConfigure&lt;SolutionImportOptions&gt;(o =&gt; o.Validate())</c>
    /// in Worker/Program.cs.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProvisioningArtifactsContainerUri))
        {
            throw new InvalidOperationException(
                "SolutionImportOptions:ProvisioningArtifactsContainerUri is required — " +
                "DataverseWebApiSolutionImporter cannot resolve the 8 solution ZIPs as versioned " +
                "artifacts without it. Set the app-setting to the provisioning-artifacts storage " +
                "account (same container task 116/117/132 publish to — live-ceremony backlog item #4).");
        }
        if (string.IsNullOrWhiteSpace(SolutionArtifactManifestBlobName))
        {
            throw new InvalidOperationException("SolutionImportOptions:SolutionArtifactManifestBlobName is required.");
        }
    }
}
