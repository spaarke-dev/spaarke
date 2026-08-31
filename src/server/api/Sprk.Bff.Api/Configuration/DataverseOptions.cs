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
    ///
    /// <para><b>No longer <c>[Required]</c> (auth-v4 task 024, FR-B5).</b> This attribute plus
    /// <c>ValidateOnStart</c> (<c>ConfigurationModule.cs:31-34</c>) was the startup-crash dependency on
    /// the client secret: the BFF could not boot without one, which makes a secret-free deployment
    /// impossible by construction.</para>
    ///
    /// <para><b>Verified before relaxing, not assumed:</b> this property has <b>zero runtime
    /// consumers</b>. Nothing in <c>src/</c> reads <c>DataverseOptions.ClientSecret</c> — the Dataverse
    /// credential is resolved from <c>API_CLIENT_SECRET</c> / <c>AzureAd:ClientSecret</c> by
    /// <c>DataverseAccessDataSource</c> and (from task 021) by <c>OrderedCredentialClientProvider</c>.
    /// So this <c>[Required]</c> mandated a value that no code path consumed. The same is true of
    /// <see cref="ClientId"/> and <see cref="TenantId"/>; those are left alone because task 024's scope
    /// is the credential mandate, and removing unused-but-harmless validation is a separate decision.</para>
    ///
    /// <para><b>Validation is not weakened, it moved to where it can be correct.</b> Whether a usable
    /// credential exists is a question about the ordered credential list, not about this section, and it
    /// is answered at startup by <c>CredentialSelectionOptionsValidator</c> and
    /// <c>IdentityConfigurationValidator</c> (ADR-028 A4: one shared credential provider, not seven call
    /// sites each rolling their own).</para>
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Tenant ID.
    /// Must match the Dataverse environment's tenant.
    /// </summary>
    [Required(ErrorMessage = "Dataverse:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;
}
