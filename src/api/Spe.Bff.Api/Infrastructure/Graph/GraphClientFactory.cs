using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Authentication.Azure;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory implementation for creating Microsoft Graph clients.
/// Uses client secret authentication for app-only operations and OBO flow for user operations.
/// Updated for Task 4.1: Uses IHttpClientFactory for centralized resilience via GraphHttpMessageHandler.
/// </summary>
public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly IConfidentialClientApplication _cca;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<GraphClientFactory> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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
    public GraphServiceClient CreateAppOnlyClient()
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

        return new GraphServiceClient(httpClient, authProvider);
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow.
    /// For user context operations where SPE must enforce user permissions.
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// Task 4.1: Now uses named HttpClient with GraphHttpMessageHandler for centralized resilience.
    /// </summary>
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Log configuration for debugging OBO issues
        _logger.LogInformation("OBO Token Exchange - CCA configured with ClientId from API_APP_ID");
        _logger.LogDebug("Token length: {TokenLength}, First 20 chars: {TokenPrefix}",
            userAccessToken?.Length ?? 0,
            userAccessToken?.Length > 20 ? userAccessToken.Substring(0, 20) : userAccessToken);

        // Decode and log token claims for debugging (DO NOT log in production!)
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

        try
        {
            // Try using Sites.FullControl.All explicitly to bypass FileStorageContainer.Selected restrictions
            // Sites.FullControl.All doesn't have app-specific container restrictions
            var result = await _cca.AcquireTokenOnBehalfOf(
                new[] {
                    "https://graph.microsoft.com/Sites.FullControl.All",
                    "https://graph.microsoft.com/Files.ReadWrite.All"
                },
                new UserAssertion(userAccessToken)
            ).ExecuteAsync();

            _logger.LogInformation("OBO token exchange successful");
            _logger.LogInformation("OBO token scopes: {Scopes}", string.Join(", ", result.Scopes));

            // TEMPORARY DEBUG - Log full Token B for analysis (REMOVE BEFORE PRODUCTION)
            _logger.LogWarning("Token B (FULL JWT - REMOVE IN PRODUCTION): {TokenB}", result.AccessToken);

            // Create a simple token credential that returns the acquired access token
            var tokenCredential = new SimpleTokenCredential(result.AccessToken);

            var authProvider = new AzureIdentityAuthenticationProvider(
                tokenCredential,
                scopes: new[] { "https://graph.microsoft.com/.default" }
            );

            // Get HttpClient with GraphHttpMessageHandler (retry, circuit breaker, timeout)
            var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

            _logger.LogDebug("Created OBO Graph client with centralized resilience handler");

            // Use beta endpoint for SharePoint Embedded support
            return new GraphServiceClient(httpClient, authProvider, "https://graph.microsoft.com/beta");
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
}
