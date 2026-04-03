using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for demo user provisioning.
/// Bound from appsettings.json section "DemoProvisioning".
/// Supports multiple demo environments via the Environments array.
/// </summary>
public class DemoProvisioningOptions
{
    public const string SectionName = "DemoProvisioning";

    /// <summary>
    /// Available demo environments. Add entries to support multiple demo instances.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one demo environment must be configured.")]
    public DemoEnvironmentConfig[] Environments { get; set; } = Array.Empty<DemoEnvironmentConfig>();

    /// <summary>
    /// Name of the default environment (must match an entry in Environments).
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:DefaultEnvironment is required.")]
    public string DefaultEnvironment { get; set; } = string.Empty;

    /// <summary>
    /// UPN domain for demo user accounts (e.g., "demo.spaarke.com").
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:AccountDomain is required.")]
    public string AccountDomain { get; set; } = string.Empty;

    /// <summary>
    /// Entra ID security group ID for demo users (Conditional Access MFA exclusion).
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:DemoUsersGroupId is required.")]
    public string DemoUsersGroupId { get; set; } = string.Empty;

    /// <summary>
    /// License SKU IDs to assign to demo users.
    /// </summary>
    [Required]
    public LicenseSkuConfig Licenses { get; set; } = new();

    /// <summary>
    /// Admin email addresses to notify when new demo requests are submitted.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one admin notification email is required.")]
    public string[] AdminNotificationEmails { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Configuration for a single demo environment.
/// </summary>
public class DemoEnvironmentConfig
{
    /// <summary>
    /// Display name for this environment (e.g., "Demo 1").
    /// </summary>
    [Required]
    public required string Name { get; set; }

    /// <summary>
    /// Dataverse environment URL (e.g., "https://spaarke-demo.crm.dynamics.com").
    /// </summary>
    [Required]
    [Url]
    public required string DataverseUrl { get; set; }

    /// <summary>
    /// Business unit name in the target Dataverse environment.
    /// </summary>
    [Required]
    public required string BusinessUnitName { get; set; }

    /// <summary>
    /// Team name that inherits the demo user security role.
    /// </summary>
    [Required]
    public required string TeamName { get; set; }

    /// <summary>
    /// SPE container ID for demo documents.
    /// </summary>
    [Required]
    public required string SpeContainerId { get; set; }

    /// <summary>
    /// Default demo access duration in days. Admin can adjust per record.
    /// </summary>
    public int DefaultDemoDurationDays { get; set; } = 14;
}

/// <summary>
/// License SKU IDs for demo user provisioning.
/// Discover via Get-LicenseSkuIds.ps1 script.
/// </summary>
public class LicenseSkuConfig
{
    /// <summary>
    /// SKU ID for Microsoft Power Apps Plan 2 Trial.
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:Licenses:PowerAppsPlan2TrialSkuId is required.")]
    public string PowerAppsPlan2TrialSkuId { get; set; } = string.Empty;

    /// <summary>
    /// SKU ID for Microsoft Fabric (Free).
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:Licenses:FabricFreeSkuId is required.")]
    public string FabricFreeSkuId { get; set; } = string.Empty;

    /// <summary>
    /// SKU ID for Microsoft Power Automate (Free).
    /// </summary>
    [Required(ErrorMessage = "DemoProvisioning:Licenses:PowerAutomateFreeSkuId is required.")]
    public string PowerAutomateFreeSkuId { get; set; } = string.Empty;
}
