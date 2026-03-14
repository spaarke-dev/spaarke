using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for bulk SPE container operations (batch delete and batch permission assignment).
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   POST /api/spe/bulk/delete                    — enqueue a bulk soft-delete job
///   POST /api/spe/bulk/permissions               — enqueue a bulk permission assignment job
///   GET  /api/spe/bulk/{operationId}/status      — poll progress of a bulk operation
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: BackgroundService for long-running processing — no Azure Functions.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-083: Bulk operations with background processing and progress tracking.
/// </remarks>
public static class BulkOperationEndpoints
{
    // =========================================================================
    // Validation constants
    // =========================================================================

    /// <summary>Maximum number of container IDs accepted in a single bulk request.</summary>
    private const int MaxBulkItems = 500;

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "reader", "writer", "manager", "owner"
    };

    // =========================================================================
    // Route registration
    // =========================================================================

    /// <summary>
    /// Registers bulk operation endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapBulkOperationEndpoints(RouteGroupBuilder group)
    {
        // POST /api/spe/bulk/delete
        group.MapPost("/bulk/delete", EnqueueBulkDelete)
            .WithName("SpeBulkDelete")
            .WithSummary("Enqueue a bulk soft-delete operation for SPE containers")
            .WithDescription(
                "Accepts a list of container IDs and enqueues a background job to soft-delete " +
                "(move to recycle bin) each container. Returns the operation ID immediately (202 Accepted). " +
                "Poll GET /api/spe/bulk/{operationId}/status to track progress.")
            .Accepts<BulkDeleteRequest>("application/json")
            .Produces<BulkOperationAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/bulk/permissions
        group.MapPost("/bulk/permissions", EnqueueBulkPermissions)
            .WithName("SpeBulkPermissions")
            .WithSummary("Enqueue a bulk permission assignment operation for SPE containers")
            .WithDescription(
                "Accepts a list of container IDs and a user/group identity with role, then enqueues " +
                "a background job to grant that permission on each container. " +
                "Returns the operation ID immediately (202 Accepted). " +
                "Poll GET /api/spe/bulk/{operationId}/status to track progress.")
            .Accepts<BulkPermissionsRequest>("application/json")
            .Produces<BulkOperationAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/bulk/{operationId}/status
        group.MapGet("/bulk/{operationId}/status", GetBulkOperationStatusAsync)
            .WithName("SpeBulkOperationStatus")
            .WithSummary("Get the progress of a bulk SPE operation")
            .WithDescription(
                "Returns the current progress of a bulk operation: total items, completed count, " +
                "failed count, and per-item error details. Poll this endpoint until isFinished is true.")
            .Produces<BulkOperationStatus>(StatusCodes.Status200OK)
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
    /// POST /api/spe/bulk/delete
    ///
    /// Validates the request and enqueues a background soft-delete job via <see cref="BulkOperationService"/>.
    /// Returns 202 Accepted with the operation ID immediately — processing happens in the background.
    /// </summary>
    private static IResult EnqueueBulkDelete(
        [FromBody] BulkDeleteRequest request,
        BulkOperationService bulkOperationService,
        ILogger<Program> logger,
        HttpContext context)
    {
        // Validate container IDs
        if (request.ContainerIds is null || request.ContainerIds.Count == 0)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "containerIds must contain at least one container ID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (request.ContainerIds.Count > MaxBulkItems)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: $"Bulk delete accepts at most {MaxBulkItems} container IDs per request. Received {request.ContainerIds.Count}.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate configId
        if (string.IsNullOrWhiteSpace(request.ConfigId) || !Guid.TryParse(request.ConfigId, out _))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        var operationId = bulkOperationService.EnqueueDelete(request);

        logger.LogInformation(
            "BulkDelete: enqueued {Count} containers for configId={ConfigId}, operationId={OperationId}, TraceId={TraceId}",
            request.ContainerIds.Count, request.ConfigId, operationId, context.TraceIdentifier);

        var accepted = new BulkOperationAccepted(
            OperationId: operationId,
            StatusUrl: $"/api/spe/bulk/{operationId}/status");

        return TypedResults.Accepted($"/api/spe/bulk/{operationId}/status", accepted);
    }

    /// <summary>
    /// POST /api/spe/bulk/permissions
    ///
    /// Validates the request and enqueues a background permission assignment job.
    /// Returns 202 Accepted with the operation ID immediately — processing happens in the background.
    /// </summary>
    private static IResult EnqueueBulkPermissions(
        [FromBody] BulkPermissionsRequest request,
        BulkOperationService bulkOperationService,
        ILogger<Program> logger,
        HttpContext context)
    {
        // Validate container IDs
        if (request.ContainerIds is null || request.ContainerIds.Count == 0)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "containerIds must contain at least one container ID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (request.ContainerIds.Count > MaxBulkItems)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: $"Bulk permissions accepts at most {MaxBulkItems} container IDs per request. Received {request.ContainerIds.Count}.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate configId
        if (string.IsNullOrWhiteSpace(request.ConfigId) || !Guid.TryParse(request.ConfigId, out _))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate: exactly one of UserId or GroupId must be provided
        var hasUser = !string.IsNullOrWhiteSpace(request.UserId);
        var hasGroup = !string.IsNullOrWhiteSpace(request.GroupId);

        if (!hasUser && !hasGroup)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Either userId or groupId is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (hasUser && hasGroup)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "Provide either userId or groupId, not both.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate role
        if (string.IsNullOrWhiteSpace(request.Role) || !ValidRoles.Contains(request.Role))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: $"role is required and must be one of: {string.Join(", ", ValidRoles)}.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        var operationId = bulkOperationService.EnqueuePermissions(request);

        logger.LogInformation(
            "BulkPermissions: enqueued {Count} containers for configId={ConfigId}, role={Role}, operationId={OperationId}, TraceId={TraceId}",
            request.ContainerIds.Count, request.ConfigId, request.Role, operationId, context.TraceIdentifier);

        var accepted = new BulkOperationAccepted(
            OperationId: operationId,
            StatusUrl: $"/api/spe/bulk/{operationId}/status");

        return TypedResults.Accepted($"/api/spe/bulk/{operationId}/status", accepted);
    }

    /// <summary>
    /// GET /api/spe/bulk/{operationId}/status
    ///
    /// Returns the current progress of a bulk operation identified by <paramref name="operationId"/>.
    /// Returns 404 if the operation ID is unknown or has expired (retention window: 30 minutes after completion).
    /// </summary>
    private static IResult GetBulkOperationStatusAsync(
        string operationId,
        BulkOperationService bulkOperationService,
        ILogger<Program> logger,
        HttpContext context)
    {
        if (!Guid.TryParse(operationId, out var operationGuid))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "operationId must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        var status = bulkOperationService.GetStatus(operationGuid);

        if (status is null)
        {
            logger.LogInformation(
                "BulkOperationStatus: operation {OperationId} not found, TraceId={TraceId}",
                operationGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Bulk operation '{operationGuid}' was not found. It may have expired or never existed.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        logger.LogDebug(
            "BulkOperationStatus: operation {OperationId} — {Completed}/{Total} done, {Failed} failed, finished={IsFinished}, TraceId={TraceId}",
            operationGuid, status.Completed, status.Total, status.Failed, status.IsFinished, context.TraceIdentifier);

        return TypedResults.Ok(status);
    }
}
