using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Dataverse Web API client.
/// Used for querying user permissions and accessing sprk_document entity.
/// </summary>
public class DataverseOptions
{
    public const string SectionName = "Dataverse";

    /// <summary>
    /// Dataverse environment URL.
    /// Example: https://your-env.crm.dynamics.com
    /// </summary>
    [Required(ErrorMessage = "Dataverse:EnvironmentUrl is required")]
    [Url(ErrorMessage = "Dataverse:EnvironmentUrl must be a valid URL")]
    public string EnvironmentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID from Azure AD app registration.
    /// Requires Dynamics CRM user_impersonation permission.
    /// </summary>
    [Required(ErrorMessage = "Dataverse:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for app-only authentication.
    /// Store in Key Vault (production) or user-secrets (development).
    /// </summary>
    [Required(ErrorMessage = "Dataverse:ClientSecret is required")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Tenant ID.
    /// Must match the Dataverse environment's tenant.
    /// </summary>
    [Required(ErrorMessage = "Dataverse:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;
}
