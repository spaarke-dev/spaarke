// -----------------------------------------------------------------------------
// AppConfigSeedOptions.cs
//
// Bound options for the H12b app-config seed handler + its
// PowerShellAppConfigSeeder instances. Loaded from the <c>AppConfigSeed</c>
// configuration section by <see cref="AppConfigSeedModule"/>.
//
// PATTERN PARITY:
//   Mirrors <see cref="BicepInfraDeploy.BicepInfraDeployOptions"/> +
//   <see cref="Preflight.PreflightModuleOptions"/> in shape — pwsh path,
//   scripts directory, per-invocation timeout. Manifest path is separately
//   settable so unit tests can point at a fixture file without touching the
//   repo-root manifest.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// Configuration for H12b app-config seed shell-out invocations. Bound from
/// the <c>AppConfigSeed</c> configuration section; defaults cover an operator
/// running from a workstation with <c>pwsh</c> on PATH + <c>scripts/</c>
/// under <see cref="AppContext.BaseDirectory"/>. Production deployments
/// override via App Service settings so the linux-x64 publish layout is
/// honored.
/// </summary>
public sealed class AppConfigSeedOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). On Linux App Service the pwsh binary lives at
    /// <c>/usr/bin/pwsh</c> after installation; on Windows it's typically
    /// <c>C:\Program Files\PowerShell\7\pwsh.exe</c>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to the <c>scripts/</c> directory in the L2 publish
    /// output. Defaults to <c>scripts</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>. Production deployments should
    /// override via app-setting.
    /// </summary>
    public string ScriptsDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts");

    /// <summary>
    /// Absolute path to <c>scripts/seed-data/manifest.yaml</c> — the H12b
    /// idempotency key incorporates SHA-256 of this file's contents so a
    /// manifest edit forces a re-seed on next invocation.
    /// </summary>
    public string ManifestPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "seed-data", "manifest.yaml");

    /// <summary>
    /// Maximum time to wait for a single seeder invocation. Defaults to
    /// 5 minutes — the DataGrid + workspace-layout scripts each complete in
    /// single-digit seconds against a warm dev env; the 5-min ceiling catches
    /// hung processes without prematurely aborting a first-time
    /// customer-env seed where cold-path Dataverse latency + record creation
    /// can push per-script time into the low minutes.
    /// </summary>
    public TimeSpan SeederTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Basename (relative to <see cref="ScriptsDirectory"/>) of the
    /// DataGrid config seeder script. Defaults to the current shipping
    /// script name (task 004 §4b row 10).
    /// </summary>
    public string DataGridScriptFileName { get; set; }
        = "seed-reconciliation-gridconfig.ps1";

    /// <summary>
    /// Basename (relative to <see cref="ScriptsDirectory"/>) of the system
    /// workspace-layout seeder script. Defaults to the current shipping
    /// script name (task 004 §4b row 12). Vestigial as of task 151 (Wave
    /// G-5) — <see cref="PowerShellAppConfigSeeder"/> is no longer registered
    /// for either the DataGrid or WorkspaceLayout scope (both ported to
    /// direct Dataverse Web API calls); kept so the retired collaborator
    /// class still compiles per the Wave G-2..G-4 retirement convention
    /// (see PowerShellAppConfigSeeder.cs file header).
    /// </summary>
    public string WorkspaceLayoutScriptFileName { get; set; }
        = "Deploy-SystemWorkspaceLayouts.ps1";

    /// <summary>
    /// Maximum wall-clock time for a single Dataverse Web API HTTP call
    /// issued by <see cref="DataverseWebApiDataGridSeeder"/> /
    /// <see cref="DataverseWebApiWorkspaceLayoutSeeder"/> (task 151, Wave
    /// G-5 — DV-REST ports replacing the two PowerShellAppConfigSeeder
    /// shell-outs). Defaults to 30 seconds — parity with
    /// <c>EnvVarValuesOptions.RequestTimeout</c> (H7); these are
    /// single-record CRUD upserts, not long-running jobs.
    /// </summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Startup validation applied by <see cref="AppConfigSeedModule.AddH12bAppConfigSeedHandler"/>'s
    /// <c>AddOptions&lt;AppConfigSeedOptions&gt;().ValidateOnStart()</c>
    /// registration (task 151, NFR-05 parity with
    /// <c>EnvVarValuesOptions.Validate</c> / task 142's H7 precedent).
    /// Throws <see cref="InvalidOperationException"/> on an invalid value so
    /// a misconfigured Worker fails fast at boot rather than on H12b's first
    /// dispatch.
    /// </summary>
    internal void Validate()
    {
        if (DataverseRequestTimeout < TimeSpan.FromSeconds(1) || DataverseRequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{AppConfigSeedModule.ConfigSection}:DataverseRequestTimeout' must be between " +
                $"1 second and 5 minutes (actual: {DataverseRequestTimeout}).");
        }
    }
}
