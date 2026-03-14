using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Endpoints.SpeAdmin;

/// <summary>
/// Endpoints for listing and retrieving SharePoint Embedded container types from the Graph API.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET /api/spe/containertypes?configId={id}           — list all container types
///   GET /api/spe/containertypes/{typeId}?configId={id}  — get single container type by ID
///
/// The configId query parameter identifies the sprk_specontainertypeconfig Dataverse record whose
/// app registration credentials are used to authenticate with Graph API. This allows the admin UI
/// to display container types available for a selected business unit configuration.
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class ContainerTypeEndpoints
{
    /// <summary>
    /// Registers the container type list and get-by-ID endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static RouteGroupBuilder MapContainerTypeEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/spe/containertypes?configId={id}
        group.MapGet("/containertypes", ListContainerTypesAsync)
            .WithName("SpeListContainerTypes")
            .WithSummary("List SPE container types for a container type config")
            .WithDescription(
                "Returns all SharePoint Embedded container types visible to the app registration " +
                "associated with the specified container type config. Requires a valid configId that " +
                "exists in the sprk_specontainertypeconfig Dataverse table.")
            .Produces<ContainerTypeListDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containertypes/{typeId}?configId={id}
        group.MapGet("/containertypes/{typeId}", GetContainerTypeAsync)
            .WithName("SpeGetContainerType")
            .WithSummary("Get a single SPE container type by ID")
            .WithDescription(
                "Returns details for a specific SharePoint Embedded container type, authenticated using " +
                "the specified container type config. Returns 404 when the container type is not found " +
                "in Graph API.")
            .Produces<ContainerTypeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/containertypes?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client authenticated as the config's app
    /// registration, lists all container types visible to that app, and returns them as a
    /// <see cref="ContainerTypeListDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Container types returned (may be an empty list).
    ///   400 Bad Request — configId is missing or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> ListContainerTypesAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning("GET /api/spe/containertypes — missing or empty configId");
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_id_required" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "GET /api/spe/containertypes — config {ConfigId} not found in Dataverse. TraceId: {TraceId}",
                configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_not_found" });
        }

        try
        {
            // Get the Graph client authenticated for this config's app registration
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // List container types from Graph API
            var containerTypes = await graphService.ListContainerTypesAsync(graphClient, ct);

            logger.LogInformation(
                "GET /api/spe/containertypes — returned {Count} container types for config {ConfigId}. TraceId: {TraceId}",
                containerTypes.Count, configId, context.TraceIdentifier);

            // Map domain records to API DTOs
            var items = containerTypes
                .Select(ct2 => new ContainerTypeDto
                {
                    Id = ct2.Id,
                    DisplayName = ct2.DisplayName,
                    Description = ct2.Description,
                    BillingClassification = ct2.BillingClassification,
                    CreatedDateTime = ct2.CreatedDateTime
                })
                .ToList();

            return Results.Ok(new ContainerTypeListDto
            {
                Items = items,
                Count = items.Count
            });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "Graph API error listing container types for config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                configId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return Results.Problem(
                detail: "Failed to retrieve container types from the Graph API. Check the app registration credentials in the config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Graph API Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.graph_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error listing container types for config {ConfigId}. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                detail: "An unexpected error occurred while retrieving container types.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// GET /api/spe/containertypes/{typeId}?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client, retrieves a single container type
    /// by its Graph ID, and returns it as a <see cref="ContainerTypeDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Container type returned.
    ///   400 Bad Request — configId is missing or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> GetContainerTypeAsync(
        string typeId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning("GET /api/spe/containertypes/{TypeId} — missing or empty configId", typeId);
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_id_required" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "GET /api/spe/containertypes/{TypeId} — config {ConfigId} not found. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_not_found" });
        }

        try
        {
            // Get the Graph client authenticated for this config's app registration
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // Retrieve the specific container type from Graph API
            var containerType = await graphService.GetContainerTypeAsync(graphClient, typeId, ct);

            if (containerType is null)
            {
                logger.LogInformation(
                    "GET /api/spe/containertypes/{TypeId} — not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, configId, context.TraceIdentifier);
                return Results.NotFound();
            }

            logger.LogDebug(
                "GET /api/spe/containertypes/{TypeId} — returned container type '{DisplayName}'. TraceId: {TraceId}",
                typeId, containerType.DisplayName, context.TraceIdentifier);

            // Map domain record to API DTO
            return Results.Ok(new ContainerTypeDto
            {
                Id = containerType.Id,
                DisplayName = containerType.DisplayName,
                Description = containerType.Description,
                BillingClassification = containerType.BillingClassification,
                CreatedDateTime = containerType.CreatedDateTime
            });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "Graph API error getting container type {TypeId} for config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                typeId, configId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return Results.Problem(
                detail: "Failed to retrieve the container type from the Graph API. Check the app registration credentials in the config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Graph API Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.graph_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error getting container type {TypeId} for config {ConfigId}. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);

            return Results.Problem(
                detail: "An unexpected error occurred while retrieving the container type.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }
}
