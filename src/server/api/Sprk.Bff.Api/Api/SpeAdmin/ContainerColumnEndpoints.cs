using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for managing column (metadata schema) definitions on SPE containers.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET    /api/spe/containers/{containerId}/columns?configId={id}
///   POST   /api/spe/containers/{containerId}/columns?configId={id}
///   PATCH  /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
///   DELETE /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-055: Container column CRUD (GET list, POST create, PATCH update, DELETE remove).
/// </remarks>
public static class ContainerColumnEndpoints
{
    // Valid column type values (Graph API supported types for SPE containers).
    private static readonly HashSet<string> ValidColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "boolean", "dateTime", "currency", "choice",
        "number", "personOrGroup", "hyperlinkOrPicture"
    };

    // =========================================================================
    // Route registration
    // =========================================================================

    /// <summary>
    /// Registers container column CRUD endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapContainerColumnEndpoints(RouteGroupBuilder group)
    {
        // GET /api/spe/containers/{containerId}/columns?configId={id}
        group.MapGet("/containers/{containerId}/columns", ListColumnsAsync)
            .WithName("SpeListContainerColumns")
            .WithSummary("List all columns on an SPE container")
            .WithDescription(
                "Returns all column definitions on the container's metadata schema, " +
                "including system-managed read-only columns.")
            .Produces<ContainerColumnListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/columns?configId={id}
        group.MapPost("/containers/{containerId}/columns", CreateColumnAsync)
            .WithName("SpeCreateContainerColumn")
            .WithSummary("Create a new column on an SPE container")
            .WithDescription(
                "Adds a new custom metadata column to the container's schema. " +
                "Valid types: text, boolean, dateTime, currency, choice, number, personOrGroup, hyperlinkOrPicture.")
            .Produces<ContainerColumnDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PATCH /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
        group.MapPatch("/containers/{containerId}/columns/{columnId}", UpdateColumnAsync)
            .WithName("SpeUpdateContainerColumn")
            .WithSummary("Update a column on an SPE container")
            .WithDescription(
                "Updates one or more properties of an existing column. " +
                "Only non-null fields in the request body are applied.")
            .Produces<ContainerColumnDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
        group.MapDelete("/containers/{containerId}/columns/{columnId}", DeleteColumnAsync)
            .WithName("SpeDeleteContainerColumn")
            .WithSummary("Delete a column from an SPE container")
            .WithDescription(
                "Removes an existing column from the container's metadata schema. " +
                "System-managed read-only columns cannot be deleted.")
            .Produces(StatusCodes.Status204NoContent)
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
    /// GET /api/spe/containers/{containerId}/columns?configId={id}
    ///
    /// Returns all column definitions on the container's metadata schema.
    /// </summary>
    private static async Task<IResult> ListColumnsAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var columns = await graphService.ListColumnsAsync(graphClient, containerId, ct);

            var result = new ContainerColumnListResponse(
                columns.Select(ContainerColumnDto.FromDomain).ToList(),
                columns.Count);

            logger.LogInformation(
                "ListColumns: returned {Count} columns for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                result.Count, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "ListColumns: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "ListColumns: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "ListColumns: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("listing columns", context);
        }
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/columns?configId={id}
    ///
    /// Creates a new custom column on the container's metadata schema.
    /// Returns 201 Created with the new column DTO on success.
    /// Returns 400 if the column name is missing or the column type is invalid.
    /// </summary>
    private static async Task<IResult> CreateColumnAsync(
        string containerId,
        [FromQuery] string? configId,
        [FromBody] CreateColumnRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        if (!TryValidateCreateRequest(request, context, out problem))
            return problem!;

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var created = await graphService.CreateColumnAsync(
                graphClient,
                containerId,
                request.Name,
                request.DisplayName,
                request.Description,
                request.ColumnType,
                request.Required,
                request.Indexed,
                ct);

            logger.LogInformation(
                "CreateColumn: created column '{ColumnId}' ('{Name}', type={ColumnType}) " +
                "on container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                created.Id, created.Name, created.ColumnType, containerId, configGuid, context.TraceIdentifier);

            var dto = ContainerColumnDto.FromDomain(created);
            return TypedResults.Created(
                $"/api/spe/containers/{containerId}/columns/{created.Id}", dto);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "CreateColumn: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "CreateColumn: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "CreateColumn: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("creating column", context);
        }
    }

    /// <summary>
    /// PATCH /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
    ///
    /// Updates one or more properties of an existing column.
    /// At least one field in the request body must be non-null.
    /// Returns 200 OK with the updated column DTO on success.
    /// Returns 404 if the column does not exist.
    /// </summary>
    private static async Task<IResult> UpdateColumnAsync(
        string containerId,
        string columnId,
        [FromQuery] string? configId,
        [FromBody] UpdateColumnRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (string.IsNullOrWhiteSpace(columnId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "columnId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        // At least one field must be provided.
        if (request is null || (request.DisplayName is null && request.Description is null
            && request.Required is null && request.Indexed is null))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "At least one of displayName, description, required, or indexed must be provided.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var updated = await graphService.UpdateColumnAsync(
                graphClient,
                containerId,
                columnId,
                request.DisplayName,
                request.Description,
                request.Required,
                request.Indexed,
                ct);

            if (updated is null)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Column '{columnId}' was not found on container '{containerId}'.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "UpdateColumn: updated column '{ColumnId}' on container '{ContainerId}', " +
                "configId {ConfigId}, TraceId={TraceId}",
                columnId, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(ContainerColumnDto.FromDomain(updated));
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "UpdateColumn: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "UpdateColumn: Graph returned 404 for column '{ColumnId}' on container '{ContainerId}', TraceId={TraceId}",
                columnId, containerId, context.TraceIdentifier);
            return Results.Problem(
                title: "Not Found",
                detail: $"Column '{columnId}' was not found on container '{containerId}'.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "UpdateColumn: Graph API error for column '{ColumnId}', container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                columnId, containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "UpdateColumn: unexpected error for column '{ColumnId}', container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                columnId, containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("updating column", context);
        }
    }

    /// <summary>
    /// DELETE /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
    ///
    /// Removes an existing column from the container's metadata schema.
    /// Returns 204 No Content on success.
    /// Returns 404 if the column does not exist.
    /// </summary>
    private static async Task<IResult> DeleteColumnAsync(
        string containerId,
        string columnId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (string.IsNullOrWhiteSpace(columnId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "columnId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var deleted = await graphService.DeleteColumnAsync(graphClient, containerId, columnId, ct);

            if (!deleted)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Column '{columnId}' was not found on container '{containerId}'.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "DeleteColumn: deleted column '{ColumnId}' from container '{ContainerId}', " +
                "configId {ConfigId}, TraceId={TraceId}",
                columnId, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.NoContent();
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "DeleteColumn: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "DeleteColumn: Graph returned 404 for column '{ColumnId}' on container '{ContainerId}', TraceId={TraceId}",
                columnId, containerId, context.TraceIdentifier);
            return Results.Problem(
                title: "Not Found",
                detail: $"Column '{columnId}' was not found on container '{containerId}'.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "DeleteColumn: Graph API error for column '{ColumnId}', container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                columnId, containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "DeleteColumn: unexpected error for column '{ColumnId}', container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                columnId, containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("deleting column", context);
        }
    }

    // =========================================================================
    // Validation helpers
    // =========================================================================

    private static bool TryValidateContainerId(string containerId, HttpContext context, out IResult? problem)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: "containerId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        problem = null;
        return true;
    }

    private static bool TryValidateConfigId(
        string? configId,
        HttpContext context,
        out Guid configGuid,
        out IResult? problem)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out configGuid))
        {
            configGuid = Guid.Empty;
            problem = Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        problem = null;
        return true;
    }

    private static bool TryValidateCreateRequest(
        CreateColumnRequest? request,
        HttpContext context,
        out IResult? problem)
    {
        if (request is null)
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: "Request body is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: "name is required and cannot be empty or whitespace.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ColumnType) || !ValidColumnTypes.Contains(request.ColumnType))
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: $"Invalid columnType '{request.ColumnType}'. Valid types are: " +
                        "text, boolean, dateTime, currency, choice, number, personOrGroup, hyperlinkOrPicture.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        problem = null;
        return true;
    }

    // =========================================================================
    // ProblemDetails factory helpers
    // =========================================================================

    private static IResult ConfigNotFoundProblem(Guid configGuid, HttpContext context) =>
        Results.Problem(
            title: "Bad Request",
            detail: $"Container type config '{configGuid}' was not found.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });

    private static IResult GraphApiProblem(ODataError odataError, HttpContext context) =>
        Results.Problem(
            title: "Graph API Error",
            detail: odataError.Error?.Message ?? "An error occurred communicating with the Graph API.",
            statusCode: odataError.ResponseStatusCode is >= 400 and < 600
                ? odataError.ResponseStatusCode
                : StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });

    private static IResult UnexpectedProblem(string operation, HttpContext context) =>
        Results.Problem(
            title: "Internal Server Error",
            detail: $"An unexpected error occurred while {operation}.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
}
