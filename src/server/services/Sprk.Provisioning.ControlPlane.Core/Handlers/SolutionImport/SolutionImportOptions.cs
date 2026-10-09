// -----------------------------------------------------------------------------
// SolutionImportOptions.cs
//
// Bound options for the H6 handler's collaborators (Web API importer,
// verifier and org-settings applier). Loaded from the "SolutionImportOptions" configuration section
// by Program.cs — runtime-configurable so the linux-x64 App Service publish
// layout can be honored without recompiling. Parity with
// DataverseEnvAdoptionOptions + AiSeedChainOptions.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Bound options for <see cref="H6SolutionImportHandler"/> collaborators.
/// Configuration key: <c>SolutionImportOptions</c>.
/// </summary>
public sealed class SolutionImportOptions
{
    /// <summary>Configuration section name (bound via Program.cs <c>GetSection(nameof(SolutionImportOptions))</c>).</summary>
    public const string SectionName = nameof(SolutionImportOptions);

    // Task 253 (G38): PacCliExecutable and VerifierCallTimeout were DELETED with the pac pre-import steps — the
    // org-settings applier is a Dataverse Web API client (its per-request timeout is DataverseWebApiRequestTimeout)
    // and H6 installs no application (SpaarkeMaster depends on none that a fresh environment lacks).

    /// <summary>
    /// Maximum wall-clock time for one package import (async-operation polling). Defaults to 60 minutes. If exceeded, the
    /// importer returns Timeout which the handler maps to
    /// <see cref="SolutionImportRejectionCodes.ImportTimeout"/> (Resumable — a re-run re-reads the installed version).
    /// </summary>
    public TimeSpan ImportTimeout { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Client secret for the BFF Entra app registration (legacy FR-39 ClientSecret chain only). MUST be null / whitespace
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

    /// <summary>
    /// FR-39 ordered credential chain for H6's Dataverse auth (A44.5, task
    /// 205i — SAME seam H7's <see cref="EnvVarValues.EnvVarValuesOptions.Credentials"/>
    /// carries; both sections are driven from ONE Bicep param so the two
    /// chains cannot drift). Bound from <c>SolutionImportOptions:Credentials</c>
    /// (<c>SolutionImportOptions__Credentials__Order__0=ManagedIdentityFederated</c>
    /// on secret-free environments). Unconfigured = legacy <c>[ClientSecret]</c>
    /// chain — task-141/204a behavior preserved (empty secret stays a RUNTIME
    /// Resumable <c>MissingClientSecret</c>, never a boot fail — see
    /// <see cref="Validate"/> remarks).
    /// </summary>
    public WorkerCredentialSelectionOptions Credentials { get; set; } = new();

    /// <summary>
    /// Base URI of the `provisioning-artifacts` blob container (e.g.
    /// <c>https://{account}.blob.core.windows.net/provisioning-artifacts</c>).
    /// SAME underlying container task 116's <c>deploy-bff-api.yml</c> +
    /// task 117's ARM-JSON publish + task 132's H9 artifact both use — naming
    /// matches <c>BffDeployOptions.ProvisioningArtifactsContainerUri</c> /
    /// <c>BicepInfraDeployOptions.ProvisioningArtifactsContainerUri</c>
    /// EXACTLY (DS-1b §1 H6 row: "coordinate the packaging mechanism with
    /// task 116's blob-artifact pattern"). Required for
    /// <see cref="DataverseWebApiSolutionImporter"/> to resolve the package
    /// ZIP as a versioned artifact rather than a local filesystem path.
    /// </summary>
    public string ProvisioningArtifactsContainerUri { get; set; } = string.Empty;

    /// <summary>
    /// Blob name of the solution-artifact manifest — the mutable "latest" pointer
    /// <see cref="DataverseWebApiSolutionImporter"/> reads inside <see cref="ProvisioningArtifactsContainerUri"/>.
    /// Defaults to <c>dataverse-solutions-latest.json</c>. Shape (T218b):
    /// <c>{"solutions":{"SpaarkeMaster":{"version":"1.2.0.0","managedBlobName":"…","unmanagedBlobName":"…"}}}</c>,
    /// published by <c>publish-dataverse-solutions-manifest.yml</c> (T218d).
    /// </summary>
    public string SolutionArtifactManifestBlobName { get; set; } = "dataverse-solutions-latest.json";

    /// <summary>
    /// Per-HTTP-request timeout for a single Dataverse Web API call (installed-solution GET, the
    /// ImportSolutionAsync/StageAndUpgradeAsync POST — which returns at once — or a single poll GET). Applied to the
    /// importer's named HttpClient and to the verifier's and org-settings applier's typed clients (Worker/Program.cs).
    /// Defaults to 100 seconds — headroom for uploading the base64
    /// package. Distinct from <see cref="ImportTimeout"/> (the overall deadline for the import to complete).
    /// </summary>
    public TimeSpan DataverseWebApiRequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// Interval between <c>asyncoperations({AsyncOperationId})</c> poll GETs while
    /// waiting for the package import to reach a terminal state
    /// (<c>statecode</c> 3 — Completed). Defaults to 15 seconds — solution
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
    ///
    /// <para><b>A44.5 addition — chain SHAPE only:</b> the FR-39 credential
    /// chain is validated for shape (unknown kind / duplicate /
    /// RequireSecretFreeIdentity contradiction fail fast), but an empty
    /// <see cref="ClientSecret"/> deliberately does NOT fail here even on a
    /// secret-first chain — H6's missing-secret classification is a RUNTIME
    /// Resumable failure on the affected run
    /// (<see cref="SolutionImportRejectionCodes.MissingClientSecret"/>, per
    /// the §4C rollback classification task 204a documented), unlike H7's
    /// boot fail-fast. That existing lifecycle difference is preserved.</para>
    /// </summary>
    public void Validate()
    {
        // A44.5: fail-fast on invalid FR-39 provider-chain configuration
        // (throws with an actionable message; result discarded — only the
        // shape check is wanted at this boundary).
        _ = Credentials.ResolveEffectiveOrder(SectionName);

        if (string.IsNullOrWhiteSpace(ProvisioningArtifactsContainerUri))
        {
            throw new InvalidOperationException(
                "SolutionImportOptions:ProvisioningArtifactsContainerUri is required — " +
                "DataverseWebApiSolutionImporter cannot resolve the SpaarkeMaster package as a versioned " +
                "artifact without it. Set the app-setting to the provisioning-artifacts container " +
                "(the same container the BFF zip and ARM JSON are published to).");
        }
        if (string.IsNullOrWhiteSpace(SolutionArtifactManifestBlobName))
        {
            throw new InvalidOperationException("SolutionImportOptions:SolutionArtifactManifestBlobName is required.");
        }
    }
}
