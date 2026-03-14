using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for reading and updating custom properties on SPE containers.
///
/// Custom properties are administrator-controlled key-value pairs that provide
/// additional metadata on containers. Properties marked as searchable are indexed
/// by SharePoint Embedded and can be used to filter containers in search queries.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET /api/spe/containers/{containerId}/customproperties?configId={id}
///   PUT /api/spe/containers/{containerId}/customproperties?configId={id}
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain DTOs only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-056: Container custom property endpoints.
/// </remarks>
public static class ContainerCustomPropertyEndpoints
{
    /// <summary>
    /// Registers the GET and PUT custom property endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapContainerCustomPropertyEndpoints(RouteGroupBuilder group)
    {
        // GET /api/spe/containers/{containerId}/customproperties?configId={id}
        group.MapGet("/containers/{containerId}/customproperties", GetCustomPropertiesAsync)
            .WithName("SpeGetContainerCustomProperties")
            .WithSummary("Get all custom properties on an SPE container")
            .WithDescription(
                "Returns all custom properties attached to the specified container. " +
                "Custom properties are key-value pairs with an optional isSearchable flag that " +
                "controls whether the value is indexed for SharePoint Embedded search queries.")
            .Produces<CustomPropertiesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/spe/containers/{containerId}/customproperties?configId={id}
        group.MapPut("/containers/{containerId}/customproperties", PutCustomPropertiesAsync)
            .WithName("SpePutContainerCustomProperties")
            .WithSummary("Replace all custom properties on an SPE container")
            .WithDescription(
                "Replaces the complete set of custom properties on the specified container. " +
                "This is a full-replace operation — any properties not included in the request body " +
                "will be removed. Pass an empty properties array to clear all custom properties.")
            .Produces<CustomPropertiesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    /// <summary>
    /// GET /api/spe/containers/{containerId}/customproperties?configId={id}
    ///
    /// Reads custom properties from the specified SPE container via Graph API.
    /// Returns an empty properties list when the container has no custom properties.
    /// </summary>
    private static async Task<IResult> GetCustomPropertiesAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "GetCustomProperties: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate containerId
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "containerId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                throw new SpeAdminGraphService.ConfigNotFoundException(configGuid);
            }

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var properties = await graphService.GetCustomPropertiesAsync(graphClient, containerId, ct);

            if (properties is null)
            {
                logger.LogInformation(
                    "GetCustomProperties: container '{ContainerId}' not found for configId {ConfigId}, TraceId={TraceId}",
                    containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "GetCustomProperties: returned {Count} properties for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                properties.Count, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(new CustomPropertiesResponse(properties, properties.Count));
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "GetCustomProperties: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "GetCustomProperties: Graph returned 404 for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "GetCustomProperties: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);

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
                ex, "GetCustomProperties: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving custom properties.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// PUT /api/spe/containers/{containerId}/customproperties?configId={id}
    ///
    /// Replaces all custom properties on the specified container.
    /// Validates that no property has an empty name before calling Graph API.
    /// Returns the updated properties as confirmed by a post-PATCH read.
    /// </summary>
    private static async Task<IResult> PutCustomPropertiesAsync(
        string containerId,
        [FromQuery] string? configId,
        UpdateCustomPropertiesRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "PutCustomProperties: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate containerId
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "containerId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate request body
        if (request?.Properties is null)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Request body is required. Provide a 'properties' array (use an empty array to clear all properties).",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate that no property has an empty name
        var emptyNameIndex = request.Properties
            .Select((p, i) => (Property: p, Index: i))
            .FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Property?.Name));

        if (emptyNameIndex.Property is not null || request.Properties.Any(p => string.IsNullOrWhiteSpace(p?.Name)))
        {
            logger.LogWarning(
                "PutCustomProperties: one or more properties have empty names, containerId='{ContainerId}', configId={ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "Property name must not be empty or whitespace.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                throw new SpeAdminGraphService.ConfigNotFoundException(configGuid);
            }

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var updated = await graphService.UpdateCustomPropertiesAsync(
                graphClient, containerId, request.Properties, ct);

            if (updated is null)
            {
                logger.LogInformation(
                    "PutCustomProperties: container '{ContainerId}' not found for configId {ConfigId}, TraceId={TraceId}",
                    containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "PutCustomProperties: updated {Count} properties on container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                updated.Count, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(new CustomPropertiesResponse(updated, updated.Count));
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "PutCustomProperties: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "PutCustomProperties: Graph returned 404 for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "PutCustomProperties: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);

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
                ex, "PutCustomProperties: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while updating custom properties.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Response DTOs (ADR-007: no Graph SDK types in public surface)
    // =========================================================================

    /// <summary>
    /// Response body for both GET and PUT /api/spe/containers/{id}/customproperties.
    /// </summary>
    /// <param name="Properties">The complete list of custom properties on the container.</param>
    /// <param name="Count">Number of properties returned (convenience field).</param>
    public sealed record CustomPropertiesResponse(
        IReadOnlyList<CustomPropertyDto> Properties,
        int Count);
}
