using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoint for searching items (files and documents) within SPE containers using the Microsoft Graph search API.
///
/// Route:
///   POST /api/spe/search/items?configId={id}
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; static handler methods.
/// ADR-007: No Graph SDK types in public API surface — all domain models returned.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class SearchItemsEndpoints
{
    /// <summary>
    /// Registers the search items endpoint on the provided /api/spe route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> during startup.
    /// </summary>
    /// <param name="group">The /api/spe route group to register on.</param>
    public static RouteGroupBuilder MapSearchItemsEndpoints(this RouteGroupBuilder group)
    {
        // POST /api/spe/search/items?configId={id}
        group.MapPost("/search/items", SearchItemsAsync)
            .WithName("SpeSearchItems")
            .WithSummary("Search for items within SPE containers using Graph search API")
            .WithDescription(
                "Searches for files and documents across SPE containers using the Microsoft Graph search API " +
                "with entityType driveItem. Optionally scoped to a specific container. " +
                "Supports pagination via skipToken. Requires a non-empty query string.")
            .Produces<SearchItemsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// POST /api/spe/search/items?configId={id}
    ///
    /// Searches for driveItem entities across SPE containers via the Graph search API.
    /// When <see cref="SearchItemsRequest.ContainerId"/> is provided, the search is scoped
    /// to that specific container's drive. Otherwise, the search spans all containers
    /// accessible to the app registration.
    /// </summary>
    private static async Task<IResult> SearchItemsAsync(
        [FromQuery] string? configId,
        [FromBody] SearchItemsRequest? request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "SearchItems: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate request body
        if (request is null)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Request body is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate query — empty query is rejected per acceptance criteria
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            logger.LogWarning(
                "SearchItems: empty query string, configId={ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "Query is required and must not be empty.",
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

            // Execute search via Graph search API.
            var searchResult = await graphService.SearchItemsAsync(
                graphClient,
                request.Query,
                request.ContainerId,
                request.FileType,
                request.PageSize,
                request.SkipToken,
                ct);

            // Map from service domain model to endpoint DTO (ADR-007: no Graph SDK types in public surface).
            var response = new SearchItemsResponse(
                Items: searchResult.Items
                    .Select(item => new SearchItemDto(
                        Id: item.Id,
                        Name: item.Name,
                        Size: item.Size,
                        LastModifiedDateTime: item.LastModifiedDateTime,
                        ContainerId: item.ContainerId,
                        ContainerName: item.ContainerName,
                        WebUrl: item.WebUrl,
                        MimeType: item.MimeType))
                    .ToList(),
                NextSkipToken: searchResult.NextSkipToken,
                TotalCount: searchResult.TotalCount);

            logger.LogInformation(
                "SearchItems: query='{Query}', containerId={ContainerId}, results={Count}, hasNextPage={HasNext}, " +
                "configId={ConfigId}, TraceId={TraceId}",
                request.Query, request.ContainerId, response.Items.Count,
                response.NextSkipToken != null, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(response);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "SearchItems: configId {ConfigId} not found, TraceId={TraceId}",
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
                "SearchItems: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex, "SearchItems: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while searching items.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // DTOs (ADR-007: no Graph SDK types in public API surface)
    // =========================================================================

    /// <summary>
    /// Request body for the POST /api/spe/search/items endpoint.
    /// </summary>
    /// <param name="Query">
    /// The search query string. Required; must not be empty.
    /// Supports KQL (Keyword Query Language) syntax understood by Graph search.
    /// </param>
    /// <param name="ContainerId">
    /// Optional SPE container ID to scope the search to a single container.
    /// When null the search spans all containers accessible to the app registration.
    /// </param>
    /// <param name="FileType">
    /// Optional file type filter (e.g., "pdf", "docx"). Applied as a KQL extension when provided.
    /// </param>
    /// <param name="PageSize">
    /// Maximum number of results per page. Defaults to 25 when null or zero.
    /// Graph search accepts values in the range 1–500.
    /// </param>
    /// <param name="SkipToken">
    /// Opaque pagination token from a prior <see cref="SearchItemsResponse.NextSkipToken"/>.
    /// Null for the first page.
    /// </param>
    public sealed record SearchItemsRequest(
        string Query,
        string? ContainerId,
        string? FileType,
        int? PageSize,
        string? SkipToken);

    /// <summary>
    /// Response from the POST /api/spe/search/items endpoint.
    /// </summary>
    /// <param name="Items">Matching drive items on this page.</param>
    /// <param name="NextSkipToken">
    /// Opaque token for the next page. Null when there are no further pages.
    /// </param>
    /// <param name="TotalCount">
    /// Total estimated result count from Graph search. May be an approximation.
    /// </param>
    public sealed record SearchItemsResponse(
        IReadOnlyList<SearchItemDto> Items,
        string? NextSkipToken,
        int TotalCount);

    /// <summary>
    /// A single search result item.
    /// All Graph SDK types are mapped here — callers receive only this domain model (ADR-007).
    /// </summary>
    /// <param name="Id">DriveItem ID.</param>
    /// <param name="Name">File or folder name.</param>
    /// <param name="Size">File size in bytes. Null for folders.</param>
    /// <param name="LastModifiedDateTime">When the item was last modified.</param>
    /// <param name="ContainerId">ID of the SPE container (FileStorageContainer) that owns this item.</param>
    /// <param name="ContainerName">Display name of the container, when available from search results.</param>
    /// <param name="WebUrl">Web URL for opening the item in a browser.</param>
    /// <param name="MimeType">MIME type for files; null for folders.</param>
    public sealed record SearchItemDto(
        string Id,
        string Name,
        long? Size,
        DateTimeOffset? LastModifiedDateTime,
        string? ContainerId,
        string? ContainerName,
        string? WebUrl,
        string? MimeType);
}
