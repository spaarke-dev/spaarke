// -----------------------------------------------------------------------------
// AiSeedChainOptions.cs
//
// Bound options for the H12a handler's collaborators (manifest reader +
// Dataverse Web API seed writer). Loaded from the "AiSeedChain" configuration
// section by <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/>
// — runtime-configurable so the linux-x64 App Service publish layout can be
// honored without recompiling. Parity with <c>BicepInfraDeployOptions</c>.
//
// Task 150 (Wave G-5 Batch G-5A): removed <c>PwshExecutable</c> +
// <c>InvokeSeedManifestScriptPath</c> — both were only consumed by the
// now-deleted <c>InvokeSeedManifestScriptRunner</c> (pwsh shell-out, replaced
// by <see cref="DataverseWebApiSeedWriter"/>). Added
// <see cref="DataverseRequestTimeout"/> — parity with
// <c>RuntimeReferencesOptions.DataverseRequestTimeout</c> (H12c).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Bound options for <see cref="H12aAiSeedChainHandler"/> collaborators.
/// Configuration key: <c>AiSeedChain</c>.
/// </summary>
public sealed class AiSeedChainOptions
{
    /// <summary>
    /// Absolute path to <c>scripts/seed-data/manifest.yaml</c> — consumed by
    /// <see cref="FileSeedManifestReader"/> (SHA-256 hash + defense-in-depth
    /// retired-artifact scan) ONLY. <see cref="DataverseWebApiSeedWriter"/>
    /// reads the SAME source file via an embedded resource instead (task 150
    /// file header "SCOPE BOUNDARY" note) — this path is unaffected by that
    /// change. Defaults relative to <see cref="AppContext.BaseDirectory"/>;
    /// production deployments should override via app-setting so the
    /// linux-x64 publish layout is honored.
    /// </summary>
    public string ManifestPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "seed-data", "manifest.yaml");

    /// <summary>
    /// Maximum time to wait for the full seed-manifest invocation (all
    /// artifacts, in topological order). Defaults to 20 minutes — the seed
    /// chain fans out across ~10 artifacts; each per-artifact seed is
    /// single-digit-second per row + the target Dataverse env is throttled at
    /// ~30-60 rows/min, so 20 min covers the full manifest without truncating
    /// a slow-but-progressing run. Not directly enforced by
    /// <see cref="DataverseWebApiSeedWriter"/> itself (no internal
    /// CancellationTokenSource.CancelAfter, unlike the retired PS-process
    /// runner it replaces) — the caller's <see cref="CancellationToken"/>
    /// (the reconciler's own Service-Bus-lock-renewal budget, per FR-22/R20)
    /// governs the overall wall-clock instead.
    /// </summary>
    public TimeSpan SeedTimeout { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Per-request timeout for <see cref="DataverseWebApiSeedWriter"/>'s
    /// Dataverse Web API HTTP calls. Parity with
    /// <c>RuntimeReferencesOptions.DataverseRequestTimeout</c> (H12c).
    /// </summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

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
