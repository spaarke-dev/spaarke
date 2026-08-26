using Microsoft.Graph;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory for creating Microsoft Graph clients with different authentication modes.
/// </summary>
/// <remarks>
/// Two authentication patterns:
/// - ForUserAsync: On-Behalf-Of (OBO) flow using user's access token (for user operations)
/// - ForApp: App-only using Managed Identity or Client Secret (for admin operations)
/// </remarks>
public interface IGraphClientFactory
{
    /// <summary>
    /// Creates a Graph client using On-Behalf-Of (OBO) flow for user context operations.
    /// Extracts user token from Authorization header and exchanges it for Graph API token.
    /// </summary>
    /// <param name="ctx">HttpContext containing Authorization header with user's bearer token</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>GraphServiceClient authenticated with user's delegated permissions</returns>
    /// <remarks>
    /// Use this for operations that should enforce user permissions (e.g., file access).
    /// The user token must have audience api://{BFF-AppId}/SDAP.Access.
    /// OBO tokens are cached in Redis for 55 minutes to reduce Azure AD load.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">Missing or invalid Authorization header</exception>
    /// <exception cref="Microsoft.Identity.Client.MsalServiceException">OBO token exchange failed</exception>
    Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Delegated (on-behalf-of) client pointed at the Graph <b>beta</b> endpoint.
    /// </summary>
    /// <remarks>
    /// Use ONLY where v1.0 genuinely cannot serve the request. Today that is exactly one surface:
    /// <c>fileStorageContainerType/{id}/permissions</c> (container-type owners), which is absent from
    /// the v1.0 schema while container types simultaneously reject app-only auth — so neither
    /// delegated-v1.0 nor app-only-beta can reach it. Same OBO exchange, same cached token, same
    /// version-agnostic scope as <see cref="ForUserAsync"/>; only the base address differs.
    /// </remarks>
    Task<GraphServiceClient> ForUserBetaAsync(HttpContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Creates a Graph client using app-only authentication (Managed Identity or Client Secret).
    /// </summary>
    /// <returns>GraphServiceClient authenticated with application permissions</returns>
    /// <remarks>
    /// Use this for platform/admin operations (e.g., container creation, background jobs).
    /// Requires application permissions in Azure AD (e.g., Sites.FullControl.All).
    /// </remarks>
    GraphServiceClient ForApp();
}
