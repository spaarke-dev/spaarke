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

    /// <summary>
    /// Whether THIS deployment is a Spaarke-operated environment (today only dev; Spaarke's production operator environment
    /// carries it from the change that stands it up — owner round 57 item 1) — the only
    /// place the SPE admin routes whose answer spans the whole SharePoint Embedded tenant or a whole container type may be
    /// used (unified-access-control-r2 task 165, owner round 49 item 1): the security alerts and secure score, and the
    /// app-only container-type permission, consumer and register routes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a deployment setting, not a Dataverse column:</b> under Model 1 every customer environment's root admin is a
    /// "platform operator" in their own environment, so a root-unit check judged against one environment's own hierarchy
    /// cannot confine routes that reach every customer of a shared tenant or type — and a customer admin can edit a
    /// column. App Service configuration is set by Spaarke's deployment (Bicep <c>speAdminPlatformOperatorEnvironment</c>,
    /// <c>config/environments.json</c> → <c>scripts/Deploy-BffApi.ps1</c>), never by a customer.
    /// </para>
    /// <para>
    /// <b>Fails closed:</b> the default is <c>false</c>, so a missing setting refuses the routes; a value that is not a
    /// boolean fails the host at startup (the options are validated on start). Customer environments never carry it
    /// (customer provisioning does not emit it; <c>SpeAdminOperatorEnvironmentMarkerGuardTests</c>).
    /// </para>
    /// </remarks>
    public bool PlatformOperatorEnvironment { get; set; }
}
