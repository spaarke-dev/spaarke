using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration options for the M365 Copilot agent SSO/OBO token exchange.
///
/// The agent app registration in Azure AD is separate from the BFF's own registration.
/// The Copilot agent sends a bearer token issued to the agent app, which the BFF exchanges
/// via OBO for Graph API and Dataverse tokens using the BFF's own credentials.
///
/// ADR-010: Options pattern with ValidateOnStart().
/// </summary>
public class AgentTokenOptions
{
    public const string SectionName = "AgentToken";

    /// <summary>
    /// Azure AD Tenant ID.
    /// Must match the tenant where both the agent app and the BFF app are registered.
    /// </summary>
    [Required(ErrorMessage = "AgentToken:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the BFF API app registration.
    /// This is the app that performs the OBO exchange (the "middle tier" in the OBO flow).
    /// </summary>
    [Required(ErrorMessage = "AgentToken:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for the BFF API app registration.
    /// Required for OBO token exchange.
    /// Store in Key Vault (production) or user-secrets (development).
    /// </summary>
    [Required(ErrorMessage = "AgentToken:ClientSecret is required")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Application (client) ID of the M365 Copilot agent app registration.
    /// Used to validate that incoming tokens were issued to the expected agent.
    /// </summary>
    [Required(ErrorMessage = "AgentToken:AgentAppId is required")]
    public string AgentAppId { get; set; } = string.Empty;

    /// <summary>
    /// Graph API scopes to request during OBO exchange.
    /// Default: ["https://graph.microsoft.com/.default"]
    /// The .default scope requests all admin-consented permissions.
    /// </summary>
    [Required(ErrorMessage = "AgentToken:GraphScopes is required")]
    [MinLength(1, ErrorMessage = "At least one Graph scope is required")]
    public string[] GraphScopes { get; set; } = new[] { "https://graph.microsoft.com/.default" };

    /// <summary>
    /// Dataverse environment URL for OBO scope construction.
    /// Example: https://spaarkedev1.crm.dynamics.com
    /// The OBO scope will be: {DataverseEnvironmentUrl}/.default
    /// </summary>
    [Required(ErrorMessage = "AgentToken:DataverseEnvironmentUrl is required")]
    [Url(ErrorMessage = "AgentToken:DataverseEnvironmentUrl must be a valid URL")]
    public string DataverseEnvironmentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Token cache TTL in minutes for cached OBO tokens.
    /// Default: 55 minutes (5-minute buffer before standard 60-minute expiration).
    /// </summary>
    [Range(1, 59, ErrorMessage = "AgentToken:CacheTtlMinutes must be between 1 and 59")]
    public int CacheTtlMinutes { get; set; } = 55;
}

/// <summary>
/// Validates AgentTokenOptions with cross-property rules.
/// ADR-010: ValidateOnStart() ensures misconfiguration fails fast at startup.
/// </summary>
public class AgentTokenOptionsValidator : IValidateOptions<AgentTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentTokenOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TenantId))
            errors.Add("AgentToken:TenantId is required");

        if (string.IsNullOrWhiteSpace(options.ClientId))
            errors.Add("AgentToken:ClientId is required");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            errors.Add("AgentToken:ClientSecret is required (store in Key Vault or user-secrets)");

        if (string.IsNullOrWhiteSpace(options.AgentAppId))
            errors.Add("AgentToken:AgentAppId is required (the M365 Copilot agent app registration)");

        if (options.GraphScopes == null || options.GraphScopes.Length == 0)
            errors.Add("AgentToken:GraphScopes must contain at least one scope");

        if (string.IsNullOrWhiteSpace(options.DataverseEnvironmentUrl))
            errors.Add("AgentToken:DataverseEnvironmentUrl is required");

        // Validate Dataverse URL doesn't have trailing slash (causes scope issues)
        if (!string.IsNullOrWhiteSpace(options.DataverseEnvironmentUrl) &&
            options.DataverseEnvironmentUrl.EndsWith('/'))
        {
            errors.Add("AgentToken:DataverseEnvironmentUrl must not end with a trailing slash");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
