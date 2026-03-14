using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoint for searching SharePoint Embedded containers via the Microsoft Graph Search API.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   POST /api/spe/search/containers?configId={id}
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; static handler methods.
/// ADR-007: No Graph SDK types in public API surface — endpoint returns domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-057: Search containers via Graph Search API with fileStorageContainer entity type.
/// </remarks>
public static class SearchContainersEndpoints
{
    /// <summary>
    /// Registers the search containers endpoint on the provided /api/spe route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register the endpoint on.</param>
    /// <returns>The route group for chaining.</returns>
    public static RouteGroupBuilder MapSearchContainersEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/spe/search/containers?configId={id}
        group.MapPost("/search/containers", SearchContainersAsync)
            .WithName("SpeSearchContainers")
            .WithSummary("Search SPE containers via Graph Search API")
            .WithDescription(
                "Searches for SharePoint Embedded containers using the Microsoft Graph Search API " +
                "with entity type fileStorageContainer. Supports full-text search by container name, " +
                "description, and custom properties. Use pageSize and skipToken for pagination.")
            .Produces<SearchContainersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// POST /api/spe/search/containers?configId={id}
    ///
    /// Searches SPE containers using the Graph Search API.
    /// Returns a paginated list of matching containers with optional next-page token.
    /// </summary>
    private static async Task<IResult> SearchContainersAsync(
        [FromQuery] string? configId,
        [FromBody] SearchContainersRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "SearchContainers: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate query — empty query is rejected (acceptance criterion)
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            logger.LogWarning(
                "SearchContainers: empty query for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "query is required and must not be empty.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        try
        {
            // Resolve config (validates configId exists in Dataverse) then get Graph client.
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                throw new SpeAdminGraphService.ConfigNotFoundException(configGuid);
            }

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var searchPage = await graphService.SearchContainersAsync(
                graphClient,
                request.Query,
                request.PageSize,
                request.SkipToken,
                ct);

            var response = new SearchContainersResponse(
                Items: searchPage.Items
                    .Select(r => new SearchContainerDto(r.Id, r.DisplayName, r.Description, r.ContainerTypeId))
                    .ToList(),
                TotalCount: searchPage.TotalCount,
                NextSkipToken: searchPage.NextSkipToken);

            logger.LogInformation(
                "SearchContainers: returned {Count} results for query '{Query}', configId {ConfigId}, TraceId={TraceId}",
                response.Items.Count, request.Query, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(response);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "SearchContainers: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "SearchContainers: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);

            return Results.Problem(
                title: "Graph API Error",
                detail: odataError.Error?.Message ?? "An error occurred communicating with the Graph API.",
                statusCode: odataError.ResponseStatusCode is >= 400 and < 600
                    ? odataError.ResponseStatusCode
                    : StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "SearchContainers: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while searching containers.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Request / Response DTOs
    // =========================================================================

    /// <summary>
    /// Request body for POST /api/spe/search/containers.
    /// </summary>
    /// <param name="Query">
    /// Full-text search query string. Required; must not be empty.
    /// Searched against container displayName, description, and custom properties.
    /// </param>
    /// <param name="PageSize">
    /// Number of results to return per page (1–50). Defaults to 25 when omitted.
    /// </param>
    /// <param name="SkipToken">
    /// Opaque pagination token from a prior <see cref="SearchContainersResponse.NextSkipToken"/>.
    /// Omit or pass <c>null</c> for the first page.
    /// </param>
    public sealed record SearchContainersRequest(
        string Query,
        int? PageSize,
        string? SkipToken);

    /// <summary>Paginated search results returned by the search containers endpoint.</summary>
    /// <param name="Items">Matching containers on this page.</param>
    /// <param name="TotalCount">
    /// Total number of matching containers as reported by Graph Search.
    /// May be <c>null</c> when Graph Search does not report a count.
    /// </param>
    /// <param name="NextSkipToken">
    /// Opaque token for fetching the next page. Pass as <c>skipToken</c> in the next request.
    /// <c>null</c> when this is the last page.
    /// </param>
    public sealed record SearchContainersResponse(
        IReadOnlyList<SearchContainerDto> Items,
        long? TotalCount,
        string? NextSkipToken);

    /// <summary>Single container match returned from the search endpoint.</summary>
    /// <param name="Id">Graph FileStorageContainer ID.</param>
    /// <param name="DisplayName">Container display name.</param>
    /// <param name="Description">Optional container description.</param>
    /// <param name="ContainerTypeId">Container type GUID string. May be <c>null</c> when not returned by Graph Search.</param>
    public sealed record SearchContainerDto(
        string Id,
        string DisplayName,
        string? Description,
        string? ContainerTypeId);
}
