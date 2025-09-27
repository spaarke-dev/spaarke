using Microsoft.Graph;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory for creating Microsoft Graph clients with different authentication modes.
/// MI (Managed Identity) for app-only operations, OBO (On-Behalf-Of) for user context operations.
/// </summary>
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates a Graph client using User-Assigned Managed Identity for app-only operations.
    /// Used for platform/admin operations like container management.
    /// </summary>
    GraphServiceClient CreateAppOnlyClient();

    /// <summary>
    /// Creates a Graph client using On-Behalf-Of flow for user context operations.
    /// Used when SPE must enforce user permissions.
    /// </summary>
    /// <param name="userAccessToken">User's access token from the SPA</param>
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);
}