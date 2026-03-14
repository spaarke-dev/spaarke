using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for the SPE Admin application.
/// Bound from appsettings.json section "SpeAdmin".
///
/// Phase 1: App-only token flow. OBO token exchange is architected but not implemented.
/// Container type config (client ID, secret, tenant) is stored in sprk_specontainertypeconfig
/// Dataverse table and loaded at runtime by SpeAdminGraphService.
/// </summary>
public class SpeAdminOptions
{
    public const string SectionName = "SpeAdmin";

    /// <summary>
    /// Interval in minutes between dashboard metric sync runs.
    /// SpeDashboardSyncService uses this to schedule background updates.
    /// Default: 15 minutes.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "DashboardSyncIntervalMinutes must be between 1 and 1440.")]
    public int DashboardSyncIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of containers to retrieve per Graph API page during sync.
    /// Default: 100 (Graph API page size limit).
    /// </summary>
    [Range(1, 999, ErrorMessage = "MaxContainersPerPage must be between 1 and 999.")]
    public int MaxContainersPerPage { get; set; } = 100;
}
