// -----------------------------------------------------------------------------
// AppConfigSeedOptions.cs
//
// Bound options for the H12b app-config seed handler's Dataverse Web API
// seeders. Loaded from the <c>AppConfigSeed</c> configuration section by
// <see cref="AppConfigSeedModule"/>.
//
// Task 253 (G38): PwshExecutable, ScriptsDirectory, ManifestPath,
// SeederTimeout, DataGridScriptFileName and WorkspaceLayoutScriptFileName were
// DELETED. They served the PowerShellAppConfigSeeder shell-outs (retired by
// tasks 151/152) and a disk read of scripts/seed-data/manifest.yaml for H12b's
// idempotency key — the Worker host has no pwsh and its publish no scripts/
// folder. H12b now hashes the embedded manifest through ISeedManifestReader.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// Configuration for the H12b Dataverse Web API seeders. Bound from the <c>AppConfigSeed</c> configuration section.
/// </summary>
public sealed class AppConfigSeedOptions
{
    /// <summary>
    /// Maximum wall-clock time for a single Dataverse Web API HTTP call
    /// issued by the H12b seeders (<see cref="DataverseWebApiDataGridSeeder"/>,
    /// <see cref="DataverseWebApiWorkspaceLayoutSeeder"/>,
    /// <see cref="DataverseWebApiFieldMappingSeeder"/>,
    /// <see cref="DataverseWebApiChartDefSeeder"/>). Defaults to 30 seconds — parity with
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
