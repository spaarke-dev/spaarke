using Microsoft.AspNetCore.Authentication;

namespace Sprk.Bff.Api.Infrastructure.Authentication;

/// <summary>
/// Options for the named API key authentication scheme (task AUTHV2-045).
/// Configured via the standard <see cref="AuthenticationBuilder.AddScheme{TOptions, THandler}(string, Action{TOptions}?)"/>
/// pipeline so endpoints can use <c>.RequireAuthorization(policyName)</c> instead of inline header checks.
/// </summary>
/// <remarks>
/// <para>
/// Each policy/handler combination is associated with a single named scheme. Multiple endpoints
/// can share a scheme if they accept the same key (e.g., the RAG bulk ingestion scheme), or each
/// can have its own scheme + key for blast-radius isolation (recommended).
/// </para>
/// <para>
/// Keys are sourced from <see cref="IConfiguration"/> at the path specified by <see cref="ConfigKey"/>,
/// allowing Key Vault references via App Service configuration (e.g., <c>BuilderAdmin:ApiKey</c> →
/// <c>@Microsoft.KeyVault(SecretUri=...)</c>).
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Configuration key (dotted path) where the expected API key value is stored.
    /// Example: <c>"BuilderAdmin:ApiKey"</c> or <c>"Rag:ApiKey"</c>.
    /// Resolved via <see cref="IConfiguration"/> at request time.
    /// </summary>
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>
    /// HTTP header name carrying the API key. Defaults to <c>X-Api-Key</c>.
    /// </summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Identity name assigned to <see cref="System.Security.Claims.ClaimsIdentity"/> on successful
    /// authentication. Useful for log enrichment and audit trails. Defaults to the scheme name.
    /// </summary>
    public string? IdentityName { get; set; }
}
