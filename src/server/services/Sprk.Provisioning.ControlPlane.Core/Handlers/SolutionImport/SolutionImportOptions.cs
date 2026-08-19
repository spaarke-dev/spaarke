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
}
