using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for the two DISTINCT SharePoint Embedded recycle bins (spec decision D3).
///
/// <b>1. Deleted CONTAINERS</b> — whole containers that were soft-deleted (SPE-059):
///   GET    /api/spe/recyclebin?configId={id}              — list deleted containers
///   POST   /api/spe/recyclebin/{id}/restore?configId={id} — restore a deleted container
///   DELETE /api/spe/recyclebin/{id}?configId={id}         — permanently delete a container
///
/// <b>2. Deleted ITEMS inside one container</b> — files and folders (FR-E03 / task 052):
///   GET    /api/spe/containers/{containerId}/recyclebin/items?configId={id}
///   POST   /api/spe/containers/{containerId}/recyclebin/items/restore?configId={id}
///   POST   /api/spe/containers/{containerId}/recyclebin/items/delete?configId={id}
///
/// These are different Graph resources serving different admin needs and neither replaces the
/// other — a container-level restore cannot recover one deleted file, and an item-level restore
/// cannot recover a deleted container. Spec D3 keeps both deliberately.
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

        // ── Per-container ITEM recycle bin (FR-E03) ──────────────────────────
        // A separate resource from the deleted-CONTAINERS routes above (spec D3).

        // GET /api/spe/containers/{containerId}/recyclebin/items?configId={id}
        group.MapGet("/containers/{containerId}/recyclebin/items", ListRecycleBinItemsAsync)
            .WithName("SpeListRecycleBinItems")
            .WithSummary("List deleted items (files and folders) in a container's recycle bin")
            .WithDescription(
                "Returns the items currently in the specified container's recycle bin. " +
                "An empty list means the bin is empty — a valid state, distinct from a failure.")
            .Produces<RecycleBinItemListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/recyclebin/items/restore?configId={id}
        group.MapPost("/containers/{containerId}/recyclebin/items/restore", RestoreRecycleBinItemsAsync)
            .WithName("SpeRestoreRecycleBinItems")
            .WithSummary("Restore items from a container's recycle bin (per-item outcomes)")
            .WithDescription(
                "Restores the specified items. Returns 200 when every item was restored and " +
                "207 Multi-Status when the outcome differed across items — per-item outcomes are " +
                "always reported and are never collapsed to a single pass/fail. " +
                "Returns 409 when Graph rejected the whole batch, in which case NOTHING was restored.")
            .Produces<RecycleBinItemActionResponse>(StatusCodes.Status200OK)
            .Produces<RecycleBinItemActionResponse>(StatusCodes.Status207MultiStatus)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/recyclebin/items/delete?configId={id}
        //
        // POST, not DELETE, and that is deliberate: Graph models this as an ACTION bound to the item
        // collection (`POST .../recycleBin/items/delete` with an `ids` body), and the request carries
        // a body listing what to destroy. DELETE with a body is not reliably supported by
        // intermediaries.
        group.MapPost("/containers/{containerId}/recyclebin/items/delete", PermanentDeleteRecycleBinItemsAsync)
            .WithName("SpePermanentDeleteRecycleBinItems")
            .WithSummary("Permanently delete items from a container's recycle bin (irreversible)")
            .WithDescription(
                "Permanently purges the specified items. IRREVERSIBLE. Graph reports 204 regardless " +
                "of what it actually purged, so this endpoint re-reads the recycle bin and reports " +
                "the verified per-item outcome. Returns 207 Multi-Status whenever the outcome was " +
                "not uniform success, including when the result could not be verified.")
            .Produces<RecycleBinItemActionResponse>(StatusCodes.Status200OK)
            .Produces<RecycleBinItemActionResponse>(StatusCodes.Status207MultiStatus)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
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

            var deleted = await graphService.ListDeletedContainersForConfigAsync(
                config, config.ContainerTypeId, ct);

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
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "ListDeletedContainers: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.recyclebin.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "ListDeletedContainers: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while listing deleted containers.", ex),
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

            var found = await graphService.RestoreContainerForConfigAsync(
                config, containerId, ct);

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
        catch (SpaarkeStorageException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
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
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "RestoreContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.recyclebin.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "RestoreContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while restoring the container.", ex),
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

            var found = await graphService.PermanentDeleteContainerForConfigAsync(
                config, containerId, ct);

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
        catch (SpaarkeStorageException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
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
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "PermanentDeleteContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.recyclebin.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "PermanentDeleteContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while permanently deleting the container.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Per-container ITEM recycle bin handlers (FR-E03 / task 052)
    // =========================================================================

    /// <summary>
    /// GET /api/spe/containers/{containerId}/recyclebin/items?configId={id}
    ///
    /// Lists deleted items in the container's recycle bin. An empty list is a valid, successful
    /// result and the client MUST render it as an empty state distinguishable from an error.
    /// </summary>
    private static async Task<IResult> ListRecycleBinItemsAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var resolved = await ResolveItemBinRequestAsync(
            containerId, configId, graphService, logger, context, "ListRecycleBinItems", ct);

        if (resolved.Invalid is not null) return resolved.Invalid;

        var config = resolved.Config!;
        var configGuid = config.ConfigId;

        try
        {
            var items = await graphService.ListRecycleBinItemsForConfigAsync(config, containerId, ct);

            var dtos = items
                .Select(i => new RecycleBinItemDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    Size = i.Size,
                    DeletedDateTime = i.DeletedDateTime,
                    DeletedFromLocation = i.DeletedFromLocation,
                    DeletedByDisplayName = i.DeletedByDisplayName
                })
                .ToList();

            logger.LogInformation(
                "ListRecycleBinItems: container '{ContainerId}' bin holds {Count} item(s), configId {ConfigId}, TraceId={TraceId}",
                containerId, dtos.Count, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(new RecycleBinItemListResponse(dtos, dtos.Count));
        }
        catch (SpaarkeStorageException ex)
        {
            return LogAndMapStorageException(
                ex, logger, context, "ListRecycleBinItems", containerId, configGuid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "ListRecycleBinItems: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain(
                    "An unexpected error occurred while listing recycle bin items.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/recyclebin/items/restore?configId={id}
    ///
    /// Restores items and reports the outcome of EVERY requested id.
    /// </summary>
    /// <remarks>
    /// 200 only when every requested item was restored; 207 Multi-Status otherwise. Graph's own
    /// response is a 207 whose body lists only the ids that succeeded, so a partial failure is
    /// expressed by absence — returning 200 for that would tell the admin everything worked when
    /// some items are still deleted.
    ///
    /// A Graph-level rejection maps to <b>409 Conflict</b>, not 400. The request was well-formed;
    /// what failed is that the caller's view of the bin no longer matches the server's — and the
    /// operation is atomic, so nothing at all was restored. A generic 400 would send an admin
    /// looking for a malformed request that does not exist.
    /// </remarks>
    private static async Task<IResult> RestoreRecycleBinItemsAsync(
        string containerId,
        [FromQuery] string? configId,
        RecycleBinItemIdsRequest? request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var resolved = await ResolveItemBinRequestAsync(
            containerId, configId, graphService, logger, context, "RestoreRecycleBinItems", ct);

        if (resolved.Invalid is not null) return resolved.Invalid;

        var config = resolved.Config!;
        var configGuid = config.ConfigId;

        if (ValidateItemIds(request, context) is { } badIds) return badIds;

        var itemIds = request!.Ids!;

        try
        {
            var result = await graphService.RestoreRecycleBinItemsForConfigAsync(
                config, containerId, itemIds, ct);

            var response = ToActionResponse(
                result.Outcomes,
                verified: true,
                summary: result.RestoredCount == result.RequestedCount
                    ? $"All {result.RequestedCount} item(s) were restored."
                    : $"{result.RestoredCount} of {result.RequestedCount} item(s) were restored. " +
                      "The items listed as not restored are still in the recycle bin or no longer exist.");

            logger.LogInformation(
                "RestoreRecycleBinItems: container '{ContainerId}' restored {Restored}/{Requested}, configId {ConfigId}, TraceId={TraceId}",
                containerId, result.RestoredCount, result.RequestedCount, configGuid, context.TraceIdentifier);

            _ = auditService.LogOperationAsync(
                operation: "RestoreRecycleBinItems",
                category: "RecycleBin",
                targetResource: containerId,
                responseStatus: StatusCodes.Status200OK,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            // 207 whenever the outcome was not uniform success — the acceptance criterion forbids
            // collapsing a mixed result into a single successful status.
            return result.RestoredCount == result.RequestedCount
                ? TypedResults.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status207MultiStatus);
        }
        catch (SpeAdminGraphService.RecycleBinRestoreRejectedException ex)
        {
            logger.LogWarning(
                "RestoreRecycleBinItems: Graph rejected the batch for container '{ContainerId}' — nothing restored. configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Nothing Was Restored",
                detail:
                    "Graph rejected the restore request, so none of the selected items were restored " +
                    "and the recycle bin is unchanged. This happens when one or more of the selected " +
                    "items is no longer in the recycle bin — the whole request fails together.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = context.TraceIdentifier,
                    ["remediation"] = "Refresh the recycle bin list and retry with the items that are still present.",
                    ["requestedIds"] = ex.RequestedIds,
                    ["graphMessage"] = ex.GraphMessage
                });
        }
        catch (SpaarkeStorageException ex)
        {
            return LogAndMapStorageException(
                ex, logger, context, "RestoreRecycleBinItems", containerId, configGuid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "RestoreRecycleBinItems: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain(
                    "An unexpected error occurred while restoring recycle bin items.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/recyclebin/items/delete?configId={id}
    ///
    /// Permanently purges items. <b>Irreversible.</b>
    /// </summary>
    /// <remarks>
    /// Graph answers 204 whether it purged everything, some, or nothing, so the service layer
    /// re-reads the bin and diffs. This endpoint reports that verified outcome per item.
    ///
    /// When verification itself fails the response is 207 with <c>verified: false</c> — NOT a 5xx.
    /// The delete was issued and data may well be gone; an error status would imply nothing
    /// happened, which is the opposite of what we know. 207 plus an explicit unverified flag is the
    /// only shape that does not assert something unestablished in either direction.
    /// </remarks>
    private static async Task<IResult> PermanentDeleteRecycleBinItemsAsync(
        string containerId,
        [FromQuery] string? configId,
        RecycleBinItemIdsRequest? request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var resolved = await ResolveItemBinRequestAsync(
            containerId, configId, graphService, logger, context, "PermanentDeleteRecycleBinItems", ct);

        if (resolved.Invalid is not null) return resolved.Invalid;

        var config = resolved.Config!;
        var configGuid = config.ConfigId;

        if (ValidateItemIds(request, context) is { } badIds) return badIds;

        var itemIds = request!.Ids!;

        try
        {
            var result = await graphService.PermanentDeleteRecycleBinItemsForConfigAsync(
                config, containerId, itemIds, ct);

            var summary = result.Verified
                ? result.PurgedCount == result.RequestedCount
                    ? $"All {result.RequestedCount} item(s) were permanently deleted. This cannot be undone."
                    : $"{result.PurgedCount} of {result.RequestedCount} item(s) were permanently deleted. " +
                      "The rest were not purged — see the per-item detail."
                : "The delete was sent but could NOT be verified. Some or all of these items may " +
                  "have been permanently destroyed. Refresh the recycle bin to see its current contents.";

            var response = ToActionResponse(result.Outcomes, result.Verified, summary);

            logger.LogWarning(
                "PermanentDeleteRecycleBinItems: container '{ContainerId}' purged {Purged}/{Requested} (verified={Verified}), configId {ConfigId}, TraceId={TraceId}",
                containerId, result.PurgedCount, result.RequestedCount, result.Verified,
                configGuid, context.TraceIdentifier);

            // Irreversible operation — always audit, whatever the outcome.
            _ = auditService.LogOperationAsync(
                operation: "PermanentDeleteRecycleBinItems",
                category: "RecycleBin",
                targetResource: containerId,
                responseStatus: StatusCodes.Status200OK,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return result.Verified && result.PurgedCount == result.RequestedCount
                ? TypedResults.Ok(response)
                : Results.Json(response, statusCode: StatusCodes.Status207MultiStatus);
        }
        catch (SpaarkeStorageException ex)
        {
            return LogAndMapStorageException(
                ex, logger, context, "PermanentDeleteRecycleBinItems", containerId, configGuid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "PermanentDeleteRecycleBinItems: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain(
                    "An unexpected error occurred while permanently deleting recycle bin items.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Shared helpers for the item-bin handlers
    // =========================================================================

    /// <summary>
    /// Validates <c>configId</c> + <c>containerId</c> and resolves the config. Returns the config on
    /// success, or the ProblemDetails result to return on failure — never both.
    /// </summary>
    private static async Task<(SpeAdminGraphService.ContainerTypeConfig? Config, IResult? Invalid)> ResolveItemBinRequestAsync(
        string containerId,
        string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        string operation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "{Operation}: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                operation, configId, context.TraceIdentifier);

            return (null, Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }));
        }

        if (string.IsNullOrWhiteSpace(containerId))
        {
            return (null, Results.Problem(
                title: "Bad Request",
                detail: "containerId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }));
        }

        var config = await graphService.ResolveConfigAsync(configGuid, ct);

        if (config is null)
        {
            logger.LogWarning(
                "{Operation}: configId {ConfigId} not found, TraceId={TraceId}",
                operation, configGuid, context.TraceIdentifier);

            return (null, Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }));
        }

        return (config, null);
    }

    /// <summary>
    /// Validates the <c>ids</c> body. Returns null when valid, otherwise the ProblemDetails result.
    /// </summary>
    /// <remarks>
    /// The batch cap is a deliberate guard on an irreversible operation: an unbounded id list turns
    /// one mis-sent request into unbounded destruction, and no admin screen legitimately purges more
    /// than this in a single action.
    /// </remarks>
    private static IResult? ValidateItemIds(RecycleBinItemIdsRequest? request, HttpContext context)
    {
        const int MaxIdsPerRequest = 200;

        var ids = request?.Ids;

        if (ids is null || ids.Count == 0)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "The request body must contain a non-empty 'ids' array.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (ids.Any(string.IsNullOrWhiteSpace))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "The 'ids' array must not contain empty values.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (ids.Count > MaxIdsPerRequest)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: $"A maximum of {MaxIdsPerRequest} items may be processed in one request; {ids.Count} were supplied.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        return null;
    }

    private static RecycleBinItemActionResponse ToActionResponse(
        IReadOnlyList<SpeAdminGraphService.SpeRecycleBinItemOutcome> outcomes,
        bool verified,
        string summary)
        => new(
            Outcomes: outcomes
                .Select(o => new RecycleBinItemOutcomeDto
                {
                    Id = o.Id,
                    Name = o.Name,
                    Succeeded = o.Succeeded,
                    Detail = o.Detail
                })
                .ToList(),
            RequestedCount: outcomes.Count,
            SucceededCount: outcomes.Count(o => o.Succeeded),
            Verified: verified,
            Summary: summary);

    private static IResult LogAndMapStorageException(
        SpaarkeStorageException ex,
        ILogger<Program> logger,
        HttpContext context,
        string operation,
        string containerId,
        Guid configGuid)
    {
        logger.LogError(
            ex,
            "{Operation}: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
            operation, containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

        return ex.ToProblemDetails(
            summary: "An error occurred communicating with the Graph API.",
            errorCode: "spe.recyclebin.items.graph_error",
            statusCode: ex.ClientStatusFor(),
            traceId: context.TraceIdentifier,
            title: "Graph API Error");
    }

    // =========================================================================
    // Response DTOs (ADR-007: no Graph SDK types in public surface)
    // =========================================================================

    /// <summary>Paginated list of deleted containers in the recycle bin.</summary>
    public sealed record RecycleBinListResponse(
        IReadOnlyList<DeletedContainerDto> Items,
        int Count);

    /// <summary>Request body carrying the item ids to restore or permanently delete.</summary>
    public sealed class RecycleBinItemIdsRequest
    {
        /// <summary>The recycle-bin item ids to act on. Required and non-empty.</summary>
        public List<string>? Ids { get; set; }
    }

    /// <summary>One deleted item in a container's recycle bin.</summary>
    public sealed class RecycleBinItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Size in bytes. Null means Graph did not report a size — never render as 0.</summary>
        public long? Size { get; set; }

        public DateTimeOffset? DeletedDateTime { get; set; }

        /// <summary>Where the item was deleted from, e.g. "contentstorage/CSP_.../Document Library".</summary>
        public string? DeletedFromLocation { get; set; }

        /// <summary>Who deleted it. Null means Graph did not report it, NOT "nobody".</summary>
        public string? DeletedByDisplayName { get; set; }
    }

    /// <summary>List of items currently in a container's recycle bin.</summary>
    public sealed record RecycleBinItemListResponse(
        IReadOnlyList<RecycleBinItemDto> Items,
        int Count);

    /// <summary>What happened to one requested item.</summary>
    public sealed class RecycleBinItemOutcomeDto
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>The item's name where known — an outcome report that only lists ids is unreadable.</summary>
        public string? Name { get; set; }

        public bool Succeeded { get; set; }

        /// <summary>What actually happened, in terms an admin can act on.</summary>
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>
    /// Per-item outcomes of a restore or permanent delete. The per-item collection is the contract —
    /// clients MUST render it and MUST NOT reduce it to a single success/failure banner.
    /// </summary>
    /// <param name="Verified">
    /// Whether the reported outcomes were confirmed against the recycle bin's actual state. Only
    /// ever false on permanent delete, when the post-delete re-read failed. When false the outcomes
    /// are what we could NOT confirm, not what we observed.
    /// </param>
    public sealed record RecycleBinItemActionResponse(
        IReadOnlyList<RecycleBinItemOutcomeDto> Outcomes,
        int RequestedCount,
        int SucceededCount,
        bool Verified,
        string Summary);
}
