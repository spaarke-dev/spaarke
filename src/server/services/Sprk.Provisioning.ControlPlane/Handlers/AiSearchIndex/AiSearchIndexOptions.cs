// -----------------------------------------------------------------------------
// AiSearchIndexOptions.cs
//
// Bound options for the H2b handler's collaborators (script provisioner +
// REST-API verifier + tenant-filter template provisioner). Loaded from the
// "AiSearchIndex" configuration section by Program.cs — runtime-configurable
// so the linux-x64 App Service publish layout can be honored without
// recompiling (parity with BicepInfraDeployOptions).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Bound options for <see cref="H2bAiSearchIndexHandler"/> collaborators.
/// Configuration key: <c>AiSearchIndex</c>.
/// </summary>
public sealed class AiSearchIndexOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="BicepInfraDeploy.BicepInfraDeployOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to <c>scripts/ai-search/Deploy-AllIndexes.ps1</c>. Defaults
    /// to <c>scripts/ai-search/Deploy-AllIndexes.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string DeployAllIndexesScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "ai-search", "Deploy-AllIndexes.ps1");

    /// <summary>
    /// AI Search REST API version. Defaults to the version pinned in
    /// <c>Deploy-AllIndexes.ps1</c> as of 2026-08-17.
    /// </summary>
    public string SearchApiVersion { get; set; } = "2024-07-01";

    /// <summary>
    /// AI Search endpoint for the SHARED platform service consumed by Model 1
    /// tenants (e.g. <c>https://spaarke-search-prod.search.windows.net</c>).
    /// Model 1 verifier + tenant-filter template provisioner target this
    /// endpoint; Model 2 uses <see cref="Models.InterStepState.AiSearchEndpoint"/>
    /// populated by H2a instead. Null / whitespace = Model 1 branch fails with
    /// <see cref="AiSearchIndexRejectionCodes.MissingSearchEndpoint"/>.
    /// </summary>
    public string? SharedPlatformSearchEndpoint { get; set; }

    /// <summary>
    /// Maximum time to wait for a single <c>Deploy-AllIndexes.ps1</c> invocation.
    /// Defaults to 30 minutes per NFR-12 (full 7-index deploy target &lt; 30 min).
    /// </summary>
    public TimeSpan DeployTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum time to wait for a single AI Search REST call (verifier /
    /// tenant-filter template PUT). Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RestCallTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
