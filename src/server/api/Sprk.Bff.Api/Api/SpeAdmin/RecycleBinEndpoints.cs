using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for managing soft-deleted (recycle bin) SharePoint Embedded containers.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET    /api/spe/recyclebin?configId={id}              — list deleted containers
///   POST   /api/spe/recyclebin/{id}/restore?configId={id} — restore a deleted container
///   DELETE /api/spe/recyclebin/{id}?configId={id}         — permanently delete a container
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-059: Recycle bin management (list, restore, permanent delete) with audit logging.
/// </remarks>
public static class RecycleBinEndpoints
{
    /// <summary>
    /// Registers the recycle bin list, restore, and permanent-delete endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapRecycleBinEndpoints(RouteGroupBuilder group)
    {
        // GET /api/spe/recyclebin?configId={id}
        group.MapGet("/recyclebin", ListDeletedContainersAsync)
            .WithName("SpeListDeletedContainers")
            .WithSummary("List soft-deleted SPE containers in the recycle bin")
            .WithDescription(
                "Returns all soft-deleted SPE containers for the container type associated with the specified config. " +
                "Deleted containers remain in the recycle bin until restored or permanently deleted.")
            .Produces<RecycleBinListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/recyclebin/{id}/restore?configId={id}
        group.MapPost("/recyclebin/{containerId}/restore", RestoreContainerAsync)
            .WithName("SpeRestoreContainer")
            .WithSummary("Restore a soft-deleted SPE container from the recycle bin")
            .WithDescription(
                "Restores the specified container from the recycle bin, making it active again. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/recyclebin/{id}?configId={id}
        group.MapDelete("/recyclebin/{containerId}", PermanentDeleteContainerAsync)
            .WithName("SpePermanentDeleteContainer")
            .WithSummary("Permanently delete a soft-deleted SPE container (irreversible)")
            .WithDescription(
                "Permanently purges the specified container from the recycle bin. " +
                "This operation is irreversible — all container data is destroyed. " +
                "Writes an audit log entry on success.")
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
    /// GET /api/spe/recyclebin?configId={id}
    ///
    /// Lists all soft-deleted SPE containers for the container type config identified by
    /// <paramref name="configId"/>. Returns an empty list when no containers are in the recycle bin.
    /// </summary>
    private static async Task<IResult> ListDeletedContainersAsync(
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "ListDeletedContainers: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
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

            var deleted = await graphService.ListDeletedContainersAsync(
                graphClient, config.ContainerTypeId, ct);

            var items = deleted
                .Select(c => new DeletedContainerDto
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName,
                    DeletedDateTime = c.DeletedDateTime,
                    ContainerTypeId = c.ContainerTypeId
                })
                .ToList();

            var result = new RecycleBinListResponse(items, items.Count);

            logger.LogInformation(
                "ListDeletedContainers: returned {Count} deleted containers for configId {ConfigId}, TraceId={TraceId}",
                result.Count, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "ListDeletedContainers: configId {ConfigId} not found, TraceId={TraceId}",
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
                "ListDeletedContainers: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex,
                "ListDeletedContainers: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while listing deleted containers.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// POST /api/spe/recyclebin/{containerId}/restore?configId={id}
    ///
    /// Restores the specified container from the recycle bin.
    /// Returns 200 OK on success, 404 if the container is not found in the recycle bin.
    /// Writes an audit log entry on success (fire-and-forget).
    /// </summary>
    private static async Task<IResult> RestoreContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "RestoreContainer: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

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

            var found = await graphService.RestoreContainerAsync(graphClient, containerId, ct);

            if (!found)
            {
                logger.LogInformation(
                    "RestoreContainer: container '{ContainerId}' not found in recycle bin, configId {ConfigId}, TraceId={TraceId}",
                    containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found in the recycle bin.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "RestoreContainer: container '{ContainerId}' restored from recycle bin for configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            _ = auditService.LogOperationAsync(
                operation: "RestoreContainer",
                category: "RecycleBin",
                targetResource: containerId,
                responseStatus: StatusCodes.Status200OK,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.Ok(new { message = $"Container '{containerId}' has been restored." });
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "RestoreContainer: configId {ConfigId} not found, TraceId={TraceId}",
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
                "RestoreContainer: Graph returned 404 for container '{ContainerId}' in recycle bin, configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found in the recycle bin.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "RestoreContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex,
                "RestoreContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while restoring the container.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// DELETE /api/spe/recyclebin/{containerId}?configId={id}
    ///
    /// Permanently deletes (purges) the specified container from the recycle bin.
    /// This operation is irreversible — all container data is destroyed.
    /// Returns 204 No Content on success, 404 if the container is not found in the recycle bin.
    /// Writes an audit log entry on success (fire-and-forget).
    /// </summary>
    private static async Task<IResult> PermanentDeleteContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "PermanentDeleteContainer: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

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

            var found = await graphService.PermanentDeleteContainerAsync(graphClient, containerId, ct);

            if (!found)
            {
                logger.LogInformation(
                    "PermanentDeleteContainer: container '{ContainerId}' not found in recycle bin, configId {ConfigId}, TraceId={TraceId}",
                    containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found in the recycle bin.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "PermanentDeleteContainer: container '{ContainerId}' permanently deleted from recycle bin for configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            // This is a destructive, irreversible operation — always audit it.
            _ = auditService.LogOperationAsync(
                operation: "PermanentDeleteContainer",
                category: "RecycleBin",
                targetResource: containerId,
                responseStatus: StatusCodes.Status204NoContent,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "PermanentDeleteContainer: configId {ConfigId} not found, TraceId={TraceId}",
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
                "PermanentDeleteContainer: Graph returned 404 for container '{ContainerId}' in recycle bin, configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found in the recycle bin.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "PermanentDeleteContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex,
                "PermanentDeleteContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while permanently deleting the container.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Response DTOs (ADR-007: no Graph SDK types in public surface)
    // =========================================================================

    /// <summary>Paginated list of deleted containers in the recycle bin.</summary>
    public sealed record RecycleBinListResponse(
        IReadOnlyList<DeletedContainerDto> Items,
        int Count);
}
