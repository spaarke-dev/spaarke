// -----------------------------------------------------------------------------
// AiSeedChainOptions.cs
//
// Bound options for the H12a handler's collaborators (manifest reader + PS
// runner). Loaded from the "AiSeedChain" configuration section by
// <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/> —
// runtime-configurable so the linux-x64 App Service publish layout can be
// honored without recompiling. Parity with <c>BicepInfraDeployOptions</c>.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Bound options for <see cref="H12aAiSeedChainHandler"/> collaborators.
/// Configuration key: <c>AiSeedChain</c>.
/// </summary>
public sealed class AiSeedChainOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="BicepInfraDeploy.BicepInfraDeployOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to <c>scripts/seed-data/manifest.yaml</c> — the declarative
    /// AI seed manifest authored by task 069. Defaults relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string ManifestPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "seed-data", "manifest.yaml");

    /// <summary>
    /// Absolute path to <c>scripts/seed-data/Invoke-SeedManifest.ps1</c> —
    /// the task-069 thin orchestrator. Defaults relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments override
    /// via app-setting when the publish layout differs.
    /// </summary>
    public string InvokeSeedManifestScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "seed-data", "Invoke-SeedManifest.ps1");

    /// <summary>
    /// Maximum time to wait for a single Invoke-SeedManifest.ps1 invocation.
    /// Defaults to 20 minutes. The seed chain fans out across ~10 artifacts;
    /// each per-artifact seeder is single-digit-second per row + the target
    /// Dataverse env is throttled at ~30-60 rows/min, so 20 min covers the
    /// full manifest (types + knowledge + skills + playbooks + output-types +
    /// consumers) without truncating a slow-but-progressing run.
    /// </summary>
    public TimeSpan SeedTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Regex patterns whose presence in <see cref="ManifestPath"/> content
    /// triggers a <see cref="AiSeedChainRejectionCodes.ManifestContainsRetiredArtifact"/>
    /// startup failure. Defense-in-depth against a hand-edit that bypasses the
    /// generator + Invoke-SeedManifest.ps1's own <c>retiredArtifacts</c> scan.
    /// Values are matched case-insensitively; the check runs against ARTIFACT
    /// declarations only (a line whose text starts with <c>- id:</c> or
    /// contains <c>authoritativeSource:</c>), so the <c>retiredArtifacts:</c>
    /// section listing them for governance purposes does NOT trip the check.
    /// </summary>
    /// <remarks>
    /// Patterns are LITERAL substrings, not regex. This keeps the check
    /// trivially auditable + prevents catastrophic backtracking. Defaults
    /// enumerate the three tokens ADR-039 amendment 2026-07-05 forbids:
    /// <list type="bullet">
    /// <item><c>spaarke-playbook-embeddings</c> — retired AI Search index.</item>
    /// <item><c>multinode</c> — frozen node-graph engine playbook variant.</item>
    /// <item><c>dispatcher</c> — retired routing-config surface.</item>
    /// </list>
    /// </remarks>
    public IList<string> RetiredArtifactPatterns { get; set; } = new List<string>
    {
        "spaarke-playbook-embeddings",
        "multinode",
        "dispatcher",
    };
}
