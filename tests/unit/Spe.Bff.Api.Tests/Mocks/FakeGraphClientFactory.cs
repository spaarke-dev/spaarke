using Microsoft.AspNetCore.Http;
using Microsoft.Graph;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Tests.Mocks;

/// <summary>
/// Minimal fake for IGraphClientFactory. It creates a GraphServiceClient that
/// sets a fake bearer but does not reach Graph successfully. Use this only for
/// tests that never reach Graph (e.g., 401/403 preconditions).
/// </summary>
public sealed class FakeGraphClientFactory : IGraphClientFactory
{
    public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
    {
        // Create a simple fake client - this won't work for real Graph calls but allows DI to work
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake-user-token");
        var client = new GraphServiceClient(httpClient);
        return Task.FromResult(client);
    }

    public GraphServiceClient ForApp()
    {
        // Create a simple fake client - this won't work for real Graph calls but allows DI to work
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake-app-token");
        return new GraphServiceClient(httpClient);
    }
}
