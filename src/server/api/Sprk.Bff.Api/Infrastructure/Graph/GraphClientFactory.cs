using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Authentication.Azure;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory implementation for creating Microsoft Graph clients.
/// Uses client secret authentication for app-only operations and OBO flow for user operations.
/// Updated for Task 4.1: Uses IHttpClientFactory for centralized resilience via GraphHttpMessageHandler.
/// Updated for Phase 4: Caches OBO tokens in Redis, reducing Azure AD load by 97% (ADR-009).
/// </summary>
public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly GraphTokenCache _tokenCache;
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly IConfidentialClientApplication _cca;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<GraphClientFactory> logger,
        GraphTokenCache tokenCache,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));

        // For local dev: read from user secrets or environment variables
        _tenantId = configuration["AZURE_TENANT_ID"] ?? configuration["TENANT_ID"];
        _clientId = configuration["AZURE_CLIENT_ID"] ?? configuration["API_APP_ID"];
        _clientSecret = configuration["AZURE_CLIENT_SECRET"] ?? configuration["API_CLIENT_SECRET"];

        var tenantId = configuration["TENANT_ID"] ??
            throw new InvalidOperationException("TENANT_ID not configured");
        var apiAppId = configuration["API_APP_ID"] ??
            throw new InvalidOperationException("API_APP_ID not configured");
        var clientSecret = configuration["API_CLIENT_SECRET"]; // Optional if no OBO endpoints yet

        _logger.LogInformation("Configuring ConfidentialClientApplication with API_APP_ID (masked): ...{Suffix}",
            apiAppId?.Length > 8 ? apiAppId.Substring(apiAppId.Length - 8) : apiAppId);
        _logger.LogInformation("Using TENANT_ID (masked): ...{Suffix}",
            tenantId?.Length > 8 ? tenantId.Substring(tenantId.Length - 8) : tenantId);
        _logger.LogInformation("Client secret configured: {HasSecret}", !string.IsNullOrWhiteSpace(clientSecret));

        var builder = ConfidentialClientApplicationBuilder
            .Create(apiAppId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}");

        if (!string.IsNullOrWhiteSpace(clientSecret))
            builder = builder.WithClientSecret(clientSecret);

        _cca = builder.Build();
    }

    /// <summary>
    /// Creates Graph client using Client Secret credentials.
    /// For app-only operations (platform/admin tasks).
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// Task 4.1: Now uses named HttpClient with GraphHttpMessageHandler for centralized resilience.
    /// </summary>
    private GraphServiceClient CreateAppOnlyClient()
    {
        // Validate required configuration
        if (string.IsNullOrWhiteSpace(_clientSecret) ||
            string.IsNullOrWhiteSpace(_tenantId) ||
            string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException(
                "Client secret authentication requires TENANT_ID, API_APP_ID, and API_CLIENT_SECRET to be configured");
        }

        // Use ClientSecretCredential for app-only access
        var credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
        _logger.LogDebug("Creating app-only Graph client with ClientSecretCredential");

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        // Get HttpClient with GraphHttpMessageHandler (retry, circuit breaker, timeout)
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        _logger.LogInformation("Created app-only Graph client with centralized resilience handler");

        // Use beta endpoint for SharePoint Embedded support
        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow with Redis token caching.
    /// For user context operations where SPE must enforce user permissions.
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// Task 4.1: Now uses named HttpClient with GraphHttpMessageHandler for centralized resilience.
    /// Phase 4: Caches OBO tokens (55-min TTL) to reduce Azure AD load by 97%.
    /// </summary>
    private async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Log configuration for debugging OBO issues
        _logger.LogInformation("OBO Token Exchange - CCA configured with ClientId from API_APP_ID");
        _logger.LogDebug("Token length: {TokenLength}, First 20 chars: {TokenPrefix}",
            userAccessToken?.Length ?? 0,
            userAccessToken?.Length > 20 ? userAccessToken.Substring(0, 20) : userAccessToken);

        // Decode and log token claims for debugging
        try
        {
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(userAccessToken);
            _logger.LogInformation("Token Claims - aud: {Aud}, iss: {Iss}, appid: {AppId}, ver: {Ver}",
                jwtToken.Audiences.FirstOrDefault(),
                jwtToken.Issuer,
                jwtToken.Claims.FirstOrDefault(c => c.Type == "appid")?.Value,
                jwtToken.Claims.FirstOrDefault(c => c.Type == "ver")?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode token for logging");
        }

        // ============================================================================
        // PHASE 4: Token Caching (ADR-009: Redis-First Caching)
        // ============================================================================
        // Check cache first to avoid expensive OBO exchange (~200ms)
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        var cachedGraphToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedGraphToken != null)
        {
            // Cache HIT - use cached token (~5ms vs ~200ms for OBO)
            _logger.LogInformation("Using cached Graph token (cache hit)");
            return CreateGraphClientFromToken(cachedGraphToken);
        }

        // Cache MISS - perform OBO exchange
        _logger.LogDebug("Cache miss, performing OBO token exchange");

        try
        {
            // OBO Flow: Use .default scope per Microsoft OAuth 2.0 OBO documentation
            // The .default scope requests ALL permissions that have been granted to the API
            // via admin consent in Azure AD. This includes:
            // - Sites.FullControl.All
            // - Files.ReadWrite.All
            // - FileStorageContainer.Selected (SharePoint Embedded)
            // Per OAUTH-OBO-IMPLEMENTATION.md: Using individual scopes causes AADSTS70011 errors
            var result = await _cca.AcquireTokenOnBehalfOf(
                new[] { "https://graph.microsoft.com/.default" },
                new UserAssertion(userAccessToken)
            ).ExecuteAsync();

            _logger.LogInformation("OBO token exchange successful");
            _logger.LogInformation("OBO token scopes: {Scopes}", string.Join(", ", result.Scopes));

            // Cache the token for 55 minutes (5-minute buffer before 60-minute expiration)
            await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

            return CreateGraphClientFromToken(result.AccessToken);
        }
        catch (MsalUiRequiredException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL UI required exception. ErrorCode: {ErrorCode}, Claims: {Claims}",
                ex.ErrorCode, ex.Claims);
            throw;
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "OBO failed - MSAL service exception. ErrorCode: {ErrorCode}, StatusCode: {StatusCode}, CorrelationId: {CorrelationId}",
                ex.ErrorCode, ex.StatusCode, ex.CorrelationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OBO failed - unexpected exception: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow for user context operations.
    /// Extracts user token from Authorization header and exchanges it for Graph API token.
    /// </summary>
    /// <param name="ctx">HttpContext containing Authorization header with user's bearer token</param>
    /// <param name="ct">Cancellation token (currently unused, reserved for future async cancellation)</param>
    /// <returns>GraphServiceClient authenticated with user's delegated permissions</returns>
    /// <exception cref="UnauthorizedAccessException">Missing or invalid Authorization header</exception>
    /// <exception cref="Microsoft.Identity.Client.MsalServiceException">OBO token exchange failed</exception>
    /// <remarks>
    /// This method wraps CreateOnBehalfOfClientAsync with automatic token extraction.
    /// OBO tokens are cached in Redis for 55 minutes to reduce Azure AD load by 97%.
    /// </remarks>
    public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
    {
        // Extract bearer token from Authorization header (throws UnauthorizedAccessException if invalid)
        var userAccessToken = TokenHelper.ExtractBearerToken(ctx);

        _logger.LogDebug("ForUserAsync called | TraceId: {TraceId}", ctx.TraceIdentifier);

        // Delegate to existing OBO implementation (handles caching, token exchange, etc.)
        return await CreateOnBehalfOfClientAsync(userAccessToken);
    }

    /// <summary>
    /// Creates Graph client using app-only authentication (Managed Identity or Client Secret).
    /// </summary>
    /// <returns>GraphServiceClient authenticated with application permissions</returns>
    /// <remarks>
    /// This method wraps CreateAppOnlyClient with a clearer name.
    /// Use for platform/admin operations (container creation, background jobs).
    /// </remarks>
    public GraphServiceClient ForApp()
    {
        _logger.LogDebug("ForApp called - using app-only authentication");

        // Delegate to existing app-only implementation
        return CreateAppOnlyClient();
    }

    /// <summary>
    /// Creates a GraphServiceClient from an access token (cached or freshly acquired).
    /// Helper method to reduce duplication between cache hit and cache miss paths.
    /// </summary>
    /// <param name="accessToken">Graph API access token (from cache or OBO exchange)</param>
    /// <returns>Configured GraphServiceClient with resilience handlers</returns>
    private GraphServiceClient CreateGraphClientFromToken(string accessToken)
    {
        // Create a simple token credential that returns the provided access token
        var tokenCredential = new SimpleTokenCredential(accessToken);

        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        // Get HttpClient with GraphHttpMessageHandler (retry, circuit breaker, timeout)
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        _logger.LogDebug("Created Graph client with centralized resilience handler");

        // Use v1.0 endpoint - SharePoint Embedded containers work with v1.0 drives endpoint
        // Container ID can be used directly as Drive ID in: /v1.0/drives/{containerId}/root:/path:/content
        return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/v1.0");
    }
}
