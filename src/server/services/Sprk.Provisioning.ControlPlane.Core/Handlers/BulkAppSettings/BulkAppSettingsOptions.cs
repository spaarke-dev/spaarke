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
    /// <summary>
    /// Path to the pwsh executable H4b invokes to run the generated Configure
    /// script. Defaults to <c>pwsh</c> (resolved via PATH). On Linux App
    /// Service the operator install path is <c>/usr/bin/pwsh</c>. Parity with
    /// <c>KvSecretsPopulationOptions.AzCliExecutable</c>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute or work-dir-relative path to the generated Configure script.
    /// Defaults to the canonical path under the repo (script is embedded /
    /// deployed alongside the L2 Worker publish output — the operator adjusts
    /// via app-setting when the deploy layout differs). Overridable per-env.
    /// </summary>
    public string ConfigureScriptPath { get; set; } =
        "scripts/canonical-secret-catalog/generated/Configure-AppServiceSettings.generated.ps1";

    /// <summary>
    /// Hard upper bound for the Configure script invocation. Batched
    /// <c>az webapp config appsettings set --settings @settings</c> per slot,
    /// then Azure schedules a restart — the SET itself is fast (~10-30s per
    /// slot) but 5 min covers slot-swap tail latency + throttling. Failure
    /// on timeout = Resumable (script writes are transactional per slot).
    /// </summary>
    public TimeSpan ScriptTimeout { get; set; } = TimeSpan.FromMinutes(5);

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
