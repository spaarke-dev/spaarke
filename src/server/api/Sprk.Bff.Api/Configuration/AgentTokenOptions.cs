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
    /// Client secret for the BFF API app registration, used for the agent OBO token exchange.
    /// Store in Key Vault (production) or user-secrets (development).
    ///
    /// <para><b>No longer <c>[Required]</c> (auth-v4 task 024, FR-B5).</b> A secret is one of three ways
    /// to satisfy OBO's confidential-credential requirement, and ADR-028 Amendment A4 ranks it last.
    /// Mandating it here made a secret-free deployment impossible for the agent path specifically.
    /// Unlike <c>DataverseOptions.ClientSecret</c>, this property <b>is</b> consumed —
    /// <c>AgentTokenService.cs:105</c> — so relaxing the attribute changes what is <i>required</i>, not
    /// what is <i>used</i>; task 022 migrates that call site to the ordered credential provider and
    /// task 033 removes the secret.</para>
    /// </summary>
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

        // ── RELAXED (auth-v4 task 024, FR-B5) ──────────────────────────────────────────────────────
        // Previously: ClientSecret unconditionally required. A secret is one of three ways to satisfy
        // OBO's confidential-credential requirement and ADR-028 A4 ranks it last, so mandating it here
        // made a secret-free agent path impossible.
        //
        // Whether a usable credential exists is a question about the ordered credential list
        // (Graph:Credentials:Order), which this options type cannot see. It is answered at startup by
        // CredentialSelectionOptionsValidator + IdentityConfigurationValidator. Duplicating that
        // judgement here would be exactly the per-call-site credential handling A4 exists to end.
        //
        // NOT relaxed: TenantId, ClientId, AgentAppId, DataverseEnvironmentUrl. Those identify WHO the
        // exchange is between and are required regardless of which credential proves it.
        // ───────────────────────────────────────────────────────────────────────────────────────────

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
