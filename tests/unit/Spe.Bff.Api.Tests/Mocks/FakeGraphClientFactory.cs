using Spe.Bff.Api.Infrastructure.Graph;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Spe.Bff.Api.Tests.Mocks;

/// <summary>
/// Minimal fake for IGraphClientFactory. It creates a GraphServiceClient that
/// sets a fake bearer but does not reach Graph successfully. Use this only for
/// tests that never reach Graph (e.g., 401/403 preconditions).
/// </summary>
public sealed class FakeGraphClientFactory : IGraphClientFactory
{
    public GraphServiceClient CreateAppOnlyClient()
    {
        // Create a simple fake client - this won't work for real Graph calls but allows DI to work
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake");
        return new GraphServiceClient(httpClient);
    }

    public Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Create a simple fake client - this won't work for real Graph calls but allows DI to work
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake");
        var client = new GraphServiceClient(httpClient);
        return Task.FromResult(client);
    }
}