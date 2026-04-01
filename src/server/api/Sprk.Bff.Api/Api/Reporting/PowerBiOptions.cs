using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Configuration options for the Power BI Embedded Reporting module.
/// Binds to the "PowerBi" configuration section (environment variables: PowerBi__*).
///
/// All credentials must come from environment variables or Azure Key Vault.
/// Never commit values for TenantId, ClientId, or ClientSecret to source control.
///
/// Registration (in ReportingModule.cs — task PBI-007):
/// <code>
/// services
///     .AddOptions&lt;PowerBiOptions&gt;()
///     .Bind(configuration.GetSection(PowerBiOptions.SectionName))
///     .ValidateDataAnnotations()
///     .ValidateOnStart();
/// </code>
/// </summary>
public class PowerBiOptions
{
    public const string SectionName = "PowerBi";

    /// <summary>
    /// Azure AD Tenant ID for the service principal used to acquire Power BI embed tokens.
    /// Environment variable: PowerBi__TenantId
    /// </summary>
    [Required(ErrorMessage = "PowerBi:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the Entra ID app registration that has Power BI API permissions.
    /// Environment variable: PowerBi__ClientId
    /// </summary>
    [Required(ErrorMessage = "PowerBi:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for the service principal. Must be stored in Azure Key Vault (production)
    /// or user-secrets (development). Never commit to source control.
    /// Environment variable: PowerBi__ClientSecret
    /// </summary>
    [Required(ErrorMessage = "PowerBi:ClientSecret is required")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD authority URL used to acquire tokens for the service principal.
    /// Defaults to "https://login.microsoftonline.com/{TenantId}" when not explicitly set.
    /// Environment variable: PowerBi__AuthorityUrl
    /// </summary>
    public string? AuthorityUrl { get; set; }

    /// <summary>
    /// Returns the effective authority URL, substituting TenantId into the default template
    /// when AuthorityUrl is not explicitly configured.
    /// </summary>
    public string GetEffectiveAuthorityUrl() =>
        string.IsNullOrWhiteSpace(AuthorityUrl)
            ? $"https://login.microsoftonline.com/{TenantId}"
            : AuthorityUrl;

    /// <summary>
    /// OAuth 2.0 scope used when acquiring tokens for the Power BI REST API.
    /// Environment variable: PowerBi__Scope
    /// </summary>
    public string Scope { get; set; } = "https://analysis.windows.net/.default";

    /// <summary>
    /// Base URL for the Power BI REST API.
    /// Environment variable: PowerBi__ApiUrl
    /// </summary>
    public string ApiUrl { get; set; } = "https://api.powerbi.com";
}
