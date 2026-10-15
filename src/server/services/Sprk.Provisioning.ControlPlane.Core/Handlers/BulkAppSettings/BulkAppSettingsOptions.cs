// -----------------------------------------------------------------------------
// BulkAppSettingsOptions.cs
//
// Task 201 — bound options for the H4b handler + its collaborators. Loaded
// from the "BulkAppSettings" configuration section by Worker Program.cs.
//
// PATTERN PARITY: mirrors KvSecretsPopulationOptions / EntraAppRegOptions /
// BicepInfraDeployOptions.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Bound options for <see cref="H4bBulkAppSettingsHandler"/> and its
/// collaborators. Configuration key: <c>BulkAppSettings</c>.
/// </summary>
public sealed class BulkAppSettingsOptions
{
    // Task 253 (G38): PwshExecutable, ConfigureScriptPath and ScriptTimeout were DELETED with the pwsh run of the
    // generated Configure script — H4b writes the settings through the ARM SDK (IAppServiceSettingsWriter); the Worker
    // host has no pwsh and no scripts/ folder.

    /// <summary>
    /// URL template for the /healthz probe. <c>{appServiceName}</c> is
    /// substituted at runtime; the operator can override for staging-slot
    /// probes (e.g. <c>https://{appServiceName}-staging.azurewebsites.net/healthz</c>)
    /// though the default targets the production slot the H4b run just wrote.
    /// </summary>
    public string HealthzUrlTemplate { get; set; } = "https://{appServiceName}.azurewebsites.net/healthz";

    /// <summary>
    /// Kudu SCM host template for docker log fetching on healthz timeout.
    /// <c>{appServiceName}</c> substituted at runtime.
    /// </summary>
    public string KuduHostTemplate { get; set; } = "{appServiceName}.scm.azurewebsites.net";
}
