// -----------------------------------------------------------------------------
// SpeContainerTypeOptions.cs
//
// Bound options for the H8 handler's collaborators (container-type provisioner
// + app-only verifier + KV writer). Loaded from the "SpeContainerType"
// configuration section by Program.cs — runtime-configurable so the linux-x64
// App Service publish layout can be honored without recompiling.
//
// PATTERN PARITY: mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs and
// Handlers/KvSecretsPopulation/KvSecretsPopulationOptions.cs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Bound options for <see cref="H8SpeContainerTypeHandler"/> collaborators.
/// Configuration key: <c>SpeContainerType</c>.
/// </summary>
public sealed class SpeContainerTypeOptions
{
    /// <summary>Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via PATH).</summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to <c>scripts/Create-NewContainerType.ps1</c> (task 011
    /// confidential-client cert-based hardening). Defaults relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string CreateNewContainerTypeScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Create-NewContainerType.ps1");

    /// <summary>
    /// Absolute path to <c>scripts/Get-SpeContainerMetadata-AppOnly.ps1</c>
    /// (new — task 051, T6-compliant app-only GET verification).
    /// </summary>
    public string GetContainerMetadataAppOnlyScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Get-SpeContainerMetadata-AppOnly.ps1");

    /// <summary>
    /// Maximum time to wait for a single Create-NewContainerType.ps1 invocation.
    /// Defaults to 10 minutes — the script performs 4-5 Graph/SharePoint REST
    /// calls + cert bootstrap; a healthy invocation completes in well under a
    /// minute, but Graph transient faults can extend it.
    /// </summary>
    public TimeSpan ProvisionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Maximum time to wait for a single app-only GET verification invocation.</summary>
    public TimeSpan VerifyTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// KV secret name holding the base64 PFX SPE owner cert, used when the run
    /// parameter <c>speCertSecretName</c> is absent. This is a NEW canonical
    /// naming decision (task 051) — no prior canonical name existed in
    /// design.md §7.9 or the H4 secret manifest for the cert itself (only for
    /// the resulting container-type id, `SPE-ContainerTypeId`). Flagged for
    /// reconciliation into the Phase H canonical secret-catalog manifest
    /// (task 084) — see notes/task-051-h8-deviations.md.
    /// </summary>
    public string DefaultCertSecretName { get; set; } = "SPE-OwnerCert-Pfx";

    /// <summary>
    /// Canonical KV secret name for the SPE container-type id (§7.9; matches
    /// StaticKvSecretManifest.cs's <c>SPE-ContainerTypeId</c> entry — H4
    /// pre-creates this slot with a placeholder; H8 populates the real value).
    /// </summary>
    public string ContainerTypeIdSecretName { get; set; } = "SPE-ContainerTypeId";

    /// <summary>Default container-type display name when the run parameter is absent.</summary>
    public string DefaultDisplayName { get; set; } = "Spaarke Document Storage";

    /// <summary>Path to the az CLI executable. Defaults to <c>az</c> (resolved via PATH).</summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>Maximum time to wait for a single <c>az keyvault secret set/show</c> invocation.</summary>
    public TimeSpan KvOperationTimeout { get; set; } = TimeSpan.FromSeconds(90);
}
