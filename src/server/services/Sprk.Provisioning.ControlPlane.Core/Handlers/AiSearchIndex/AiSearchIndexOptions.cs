// -----------------------------------------------------------------------------
// AiSearchIndexOptions.cs
//
// Bound options for the H2b handler's collaborators (SearchIndexClient
// provisioner + REST-API verifier + tenant-filter template provisioner).
// Loaded from the "AiSearchIndex" configuration section by Program.cs.
//
// SHELL-OUT OPTIONS REMOVED (task 124, Wave G-2): PwshExecutable +
// DeployAllIndexesScriptPath + DeployTimeout were retired alongside
// DeployAllIndexesScriptProvisioner.cs's deletion — SearchIndexClientProvisioner
// (its replacement) is a pure SDK client with zero ProcessStartInfo/shell-out
// (spec.md MUST rule post-line-254 block; design.md §4.1b Class A). Per-PUT
// timeout now uses RestCallTimeout (shared with the verifier + template
// provisioner) instead of a separate whole-script budget.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Bound options for <see cref="H2bAiSearchIndexHandler"/> collaborators.
/// Configuration key: <c>AiSearchIndex</c>.
/// </summary>
public sealed class AiSearchIndexOptions
{
    /// <summary>
    /// AI Search REST API version. Defaults to the version pinned in
    /// <c>infrastructure/ai-search/*.json</c>'s deploy history as of 2026-08-17.
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
    /// Maximum time to wait for a single AI Search REST call (index PUT via
    /// SearchIndexClientProvisioner / verifier GET / tenant-filter template
    /// write). Defaults to 60 seconds; a full 7-index pass therefore has an
    /// effective outer bound well under NFR-12's 30-minute target.
    /// </summary>
    public TimeSpan RestCallTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
