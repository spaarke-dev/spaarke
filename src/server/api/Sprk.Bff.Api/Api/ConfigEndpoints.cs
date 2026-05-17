namespace Sprk.Bff.Api.Api;

/// <summary>
/// Anonymous client configuration endpoint.
///
/// Returns non-sensitive MSAL configuration so that the Spaarke AI Code Page
/// can bootstrap MSAL auth when Xrm context is unavailable (e.g., direct URL
/// access without the Dataverse MDA shell).
///
/// WHY THIS ENDPOINT EXISTS:
///   The Code Page normally resolves BFF URL, MSAL client ID, and OAuth scope
///   from Dataverse environment variables via Xrm.Utility.getGlobalContext().
///   When the page is opened via a direct/bookmarked URL (top-frame, no Xrm),
///   it falls back to localStorage cache if available. If there is no cached
///   config (first visit), the page needs another source — this endpoint.
///
///   This endpoint is ANONYMOUS because:
///   1. The page has no auth token yet when it needs this config.
///   2. The data returned is non-sensitive: client ID, authority, scopes.
///      These are the same values that would be in a public MSAL configuration
///      file. The BFF URL is already public (reachable without auth).
///
/// SECURITY NOTE:
///   This endpoint MUST NOT return secrets (client secret, API keys, etc.).
///   It returns only the client-side MSAL configuration values required to
///   initiate an interactive auth flow.
///
/// All endpoints follow ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>
    /// Registers the anonymous client configuration endpoint.
    /// Called from EndpointMappingExtensions.MapSpaarkeEndpoints().
    /// </summary>
    public static IEndpointRouteBuilder MapMsalConfigEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/config/client — anonymous, returns MSAL client config
        app.MapGet("/api/config/client", GetClientConfig)
            .AllowAnonymous()
            .WithName("GetMsalClientConfig")
            .WithTags("Configuration")
            .WithSummary("Get non-sensitive client configuration for MSAL bootstrap")
            .WithDescription(
                "Returns MSAL client ID, authority, scopes, and BFF base URL. " +
                "Anonymous — used by the Code Page when Xrm context is unavailable " +
                "(direct URL access without the Dataverse MDA shell).")
            .Produces<ClientConfigResponse>(200);

        return app;
    }

    /// <summary>
    /// Returns non-sensitive MSAL client configuration.
    /// Reads AzureAd:ClientId, AzureAd:TenantId, AzureAd:Instance from IConfiguration.
    /// HttpContext is injected automatically by Minimal API — no IHttpContextAccessor needed.
    /// </summary>
    private static IResult GetClientConfig(
        IConfiguration configuration,
        HttpContext httpContext)
    {
        var clientId = configuration["AzureAd:ClientId"];
        var tenantId = configuration["AzureAd:TenantId"];
        var instance = configuration["AzureAd:Instance"]
            ?? "https://login.microsoftonline.com/";

        if (string.IsNullOrEmpty(clientId))
        {
            return Results.Problem(
                detail: "AzureAd:ClientId is not configured.",
                statusCode: 500,
                title: "Configuration Error");
        }

        // Build MSAL authority — instance already ends with '/', append tenantId
        // Example: https://login.microsoftonline.com/{tenantId}
        var authority = tenantId is not null and not "common" and not "organizations"
            ? $"{instance.TrimEnd('/')}/{tenantId}"
            : $"{instance.TrimEnd('/')}/organizations";

        // BFF base URL: derive from the current request's origin
        // (same host that served this response is the BFF host)
        var request = httpContext.Request;
        var bffBaseUrl = $"{request.Scheme}://{request.Host}";

        // OAuth scope for the BFF API
        var scope = $"api://{clientId}/user_impersonation";

        var response = new ClientConfigResponse(
            BffBaseUrl: bffBaseUrl,
            MsalClientId: clientId,
            MsalAuthority: authority,
            MsalScopes: [scope],
            TenantId: tenantId ?? string.Empty);

        return Results.Ok(response);
    }

    /// <summary>
    /// Response model for GET /api/config/client.
    /// Contains only non-sensitive MSAL configuration values.
    /// </summary>
    internal record ClientConfigResponse(
        string BffBaseUrl,
        string MsalClientId,
        string MsalAuthority,
        string[] MsalScopes,
        string TenantId);
}
