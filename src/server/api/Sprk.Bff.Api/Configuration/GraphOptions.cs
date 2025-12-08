using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Microsoft Graph API client.
/// Supports both client secret and managed identity authentication.
/// </summary>
public class GraphOptions
{
    public const string SectionName = "Graph";

    /// <summary>
    /// Azure AD Tenant ID.
    /// Use "common" for multi-tenant or specific tenant GUID.
    /// </summary>
    [Required(ErrorMessage = "Graph:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID from Azure AD app registration.
    /// </summary>
    [Required(ErrorMessage = "Graph:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for app-only authentication.
    /// Only required when ManagedIdentity.Enabled is false.
    /// Store in Key Vault (production) or user-secrets (development).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Graph API permission scopes.
    /// Default: ["https://graph.microsoft.com/.default"]
    /// </summary>
    [Required(ErrorMessage = "Graph:Scopes is required")]
    [MinLength(1, ErrorMessage = "At least one scope is required")]
    public string[] Scopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Managed identity configuration for production environments.
    /// </summary>
    public ManagedIdentityOptions ManagedIdentity { get; set; } = new();
}

/// <summary>
/// Configuration for Azure User-Assigned Managed Identity (UAMI).
/// </summary>
public class ManagedIdentityOptions
{
    /// <summary>
    /// Enable User-Assigned Managed Identity for production.
    /// Falls back to ClientSecret when false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// UAMI Client ID. Required when Enabled is true.
    /// </summary>
    public string? ClientId { get; set; }
}
