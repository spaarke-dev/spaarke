using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Authentication.Azure;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory implementation for creating Microsoft Graph clients.
/// Implements MI-first pattern with OBO support for user operations.
/// </summary>
public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly string _uamiClientId;
    private readonly IConfidentialClientApplication _cca;

    public GraphClientFactory(IConfiguration configuration)
    {
        _uamiClientId = configuration["UAMI_CLIENT_ID"] ??
            throw new InvalidOperationException("UAMI_CLIENT_ID not configured");

        var tenantId = configuration["TENANT_ID"] ??
            throw new InvalidOperationException("TENANT_ID not configured");
        var apiAppId = configuration["API_APP_ID"] ??
            throw new InvalidOperationException("API_APP_ID not configured");
        var clientSecret = configuration["API_CLIENT_SECRET"]; // Optional if no OBO endpoints yet

        var builder = ConfidentialClientApplicationBuilder
            .Create(apiAppId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}");

        if (!string.IsNullOrWhiteSpace(clientSecret))
            builder = builder.WithClientSecret(clientSecret);

        _cca = builder.Build();
    }

    /// <summary>
    /// Creates Graph client using User-Assigned Managed Identity.
    /// For app-only operations (platform/admin tasks).
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// </summary>
    public GraphServiceClient CreateAppOnlyClient()
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = _uamiClientId
        });

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        return new GraphServiceClient(authProvider);
    }

    /// <summary>
    /// Creates Graph client using On-Behalf-Of flow.
    /// For user context operations where SPE must enforce user permissions.
    /// Uses Graph SDK v5 with TokenCredentialAuthenticationProvider.
    /// </summary>
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        var result = await _cca.AcquireTokenOnBehalfOf(
            new[] { "https://graph.microsoft.com/.default" },
            new UserAssertion(userAccessToken)
        ).ExecuteAsync();

        // Create a simple token credential that returns the acquired access token
        var tokenCredential = new SimpleTokenCredential(result.AccessToken);

        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            scopes: new[] { "https://graph.microsoft.com/.default" }
        );

        return new GraphServiceClient(authProvider);
    }
}