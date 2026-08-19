// -----------------------------------------------------------------------------
// BicepInfraDeployOptions.cs
//
// Bound options for the H2a handler's collaborators (runner + probes +
// inspector). Loaded from the "BicepInfraDeploy" configuration section by
// <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/> —
// runtime-configurable so the linux-x64 App Service publish layout can be
// honored without recompiling.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Bound options for <see cref="H2aBicepInfraDeployHandler"/> collaborators.
/// Configuration key: <c>BicepInfraDeploy</c>.
/// </summary>
public sealed class BicepInfraDeployOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="Preflight.PreflightModuleOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Path to the <c>az</c> CLI executable. Defaults to <c>az</c> (resolved
    /// via PATH). On Linux App Service the operator install path is
    /// <c>/usr/bin/az</c>.
    /// </summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>
    /// Absolute path to <c>scripts/Provision-Customer.ps1</c>. Defaults to
    /// <c>scripts/Provision-Customer.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// </summary>
    public string ProvisionCustomerScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Provision-Customer.ps1");

    /// <summary>
    /// Absolute path to the <c>infrastructure/bicep/</c> tree — used by
    /// <see cref="FileBicepTemplateInspector"/> to walk template + module
    /// files. Defaults relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string BicepDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "infrastructure", "bicep");

    /// <summary>
    /// Absolute path to the runNotes directory used for upgrade-mode drift
    /// reports (spec.md FR-34: <c>runNotes/drift-{customerId}-{timestamp}.md</c>).
    /// Defaults relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string RunNotesDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "runNotes");

    /// <summary>
    /// Maximum time to wait for a single Bicep deploy invocation. Defaults
    /// to 45 minutes — H2a's Bicep deploys are documented at 10–20 min per
    /// design.md §4.2 / FR-22; the ceiling absorbs cold-provisioning of
    /// Cosmos + OpenAI + AI Search without truncating a slow-but-progressing
    /// deploy.
    /// </summary>
    public TimeSpan DeployTimeout { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>
    /// Maximum time to wait for the <c>az deployment group what-if</c>
    /// upgrade-mode probe. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan WhatIfTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum time to wait for an ARM read of an App Service slot's
    /// <c>keyVaultReferenceIdentity</c>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ArmProbeTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
