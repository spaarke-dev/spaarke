using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for listing, retrieving, creating, and managing lifecycle of SharePoint Embedded containers.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET   /api/spe/containers?configId={id}[&amp;top={n}][&amp;skipToken={token}]
///   GET   /api/spe/containers/{containerId}?configId={id}
///   POST  /api/spe/containers?configId={id}
///   PATCH /api/spe/containers/{containerId}?configId={id}
///   POST  /api/spe/containers/{containerId}/activate?configId={id}
///   POST  /api/spe/containers/{containerId}/lock?configId={id}
///   POST  /api/spe/containers/{containerId}/unlock?configId={id}
///   POST  /api/spe/containers/{containerId}/archive?configId={id}     — FR-E01
///   POST  /api/spe/containers/{containerId}/unarchive?configId={id}   — FR-E01
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoint returns domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-015: Lifecycle management endpoints (PATCH, activate, lock, unlock) with audit logging.
/// </remarks>
public static class ContainerEndpoints
{
    // Maximum display name length enforced by SharePoint Embedded (Graph API).
    private const int MaxDisplayNameLength = 256;

    /// <summary>
    /// Registers the container list, get-by-ID, and create endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapContainerEndpoints(RouteGroupBuilder group)
    {
        // GET /api/spe/containers?configId={id}&top={n}&skipToken={token}
        group.MapGet("/containers", ListContainersAsync)
            .WithName("SpeListContainers")
            .WithSummary("List SPE containers for a container type config")
            .WithDescription(
                "Returns paginated SPE containers belonging to the container type associated with the specified config. " +
                "Use $top and $skipToken for pagination.")
            .Produces<ContainerListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containers/{containerId}?configId={id}
        group.MapGet("/containers/{containerId}", GetContainerAsync)
            .WithName("SpeGetContainer")
            .WithSummary("Get a single SPE container by ID")
            .WithDescription(
                "Returns details for a specific SPE container, authenticated using the specified container type config.")
            .Produces<ContainerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers?configId={id}
        group.MapPost("/containers", CreateContainerAsync)
            .WithName("SpeCreateContainer")
            .WithSummary("Create a new SPE container")
            .WithDescription(
                "Creates a new SharePoint Embedded container for the container type associated with the specified config. " +
                "Writes an audit log entry on success.")
            .Produces<ContainerDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PATCH /api/spe/containers/{containerId}?configId={id}
        group.MapPatch("/containers/{containerId}", PatchContainerAsync)
            .WithName("SpePatchContainer")
            .WithSummary("Update SPE container display name and/or description")
            .WithDescription(
                "Updates the displayName and/or description of an SPE container. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/activate?configId={id}
        group.MapPost("/containers/{containerId}/activate", ActivateContainerAsync)
            .WithName("SpeActivateContainer")
            .WithSummary("Activate an SPE container")
            .WithDescription(
                "Transitions the container status to active. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/lock?configId={id}
        group.MapPost("/containers/{containerId}/lock", LockContainerAsync)
            .WithName("SpeLockContainer")
            .WithSummary("Lock an SPE container")
            .WithDescription(
                "Transitions the container status to locked, preventing user access. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/unlock?configId={id}
        group.MapPost("/containers/{containerId}/unlock", UnlockContainerAsync)
            .WithName("SpeUnlockContainer")
            .WithSummary("Unlock an SPE container")
            .WithDescription(
                "Restores access to a locked container by transitioning its status back to active. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/archive?configId={id}   — FR-E01
        group.MapPost("/containers/{containerId}/archive", ArchiveContainerAsync)
            .WithName("SpeArchiveContainer")
            .WithSummary("Archive an SPE container (asynchronous)")
            .WithDescription(
                "Archives the container, reducing its storage cost and de-prioritising its content in " +
                "Copilot results. ACCEPTANCE IS NOT COMPLETION: Graph performs this asynchronously — the " +
                "container enters 'recentlyArchived' and reaches 'fullyArchived' later. " +
                "Returns 409 when the container TYPE has not opted into archival, which is an operator " +
                "action (SharePoint PowerShell), not a caller-permission problem. " +
                "Writes an audit log entry on success.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/unarchive?configId={id} — FR-E01
        //
        // NOT the same as /recyclebin/{id}/restore. That recovers a soft-DELETED container; this
        // reverses ARCHIVAL on a container that was never deleted. Graph models them as two distinct
        // actions (`restore` vs `unarchive`) and so do we.
        group.MapPost("/containers/{containerId}/unarchive", UnarchiveContainerAsync)
            .WithName("SpeUnarchiveContainer")
            .WithSummary("Return an archived SPE container to active use (asynchronous)")
            .WithDescription(
                "Reverses archival. ACCEPTANCE IS NOT COMPLETION: the container enters 'reactivating' " +
                "and is not usable when this returns. Returns 409 when the container type has not " +
                "opted into archival. Writes an audit log entry on success.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    /// <summary>
    /// GET /api/spe/containers?configId={id}[&amp;top={n}][&amp;skipToken={token}]
    ///
    /// Lists SPE containers for the container type config identified by <paramref name="configId"/>.
    /// Supports OData-style pagination via $top / $skipToken.
    /// </summary>
    private static async Task<IResult> ListContainersAsync(
        [FromQuery] string? configId,
        [FromQuery] int? top,
        [FromQuery] string? skipToken,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "ListContainers: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
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

            // Use paginated list — supports $top and $skipToken for cursor-based pagination.
            var page = await graphService.ListContainersPageForConfigAsync(
                config, config.ContainerTypeId, top, skipToken, ct);

            var result = new ContainerListResponse(
                page.Items.Select(ContainerDto.FromSummary).ToList(),
                page.NextSkipToken,
                page.Items.Count);

            logger.LogInformation(
                "ListContainers: returned {Count} containers for configId {ConfigId}, TraceId={TraceId}",
                result.Count, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "ListContainers: configId {ConfigId} not found, TraceId={TraceId}",
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
                ex, "ListContainers: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.containers.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "ListContainers: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while listing containers.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// GET /api/spe/containers/{containerId}?configId={id}
    ///
    /// Retrieves a single SPE container by its Graph container ID.
    /// </summary>
    private static async Task<IResult> GetContainerAsync(
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
                "GetContainer: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
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
            // Resolve config (validates configId exists in Dataverse) then get Graph client.
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
            {
                throw new SpeAdminGraphService.ConfigNotFoundException(configGuid);
            }

            // Retrieve single container directly from Graph (more efficient than listing all).
            var container = await graphService.GetContainerForConfigAsync(
                config, containerId, ct);

            if (container is null)
            {
                logger.LogInformation(
                    "GetContainer: container '{ContainerId}' not found for configId {ConfigId}, TraceId={TraceId}",
                    containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "GetContainer: returned container '{ContainerId}' for configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(ContainerDto.FromSummary(container));
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "GetContainer: configId {ConfigId} not found, TraceId={TraceId}",
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
                "GetContainer: Graph returned 404 for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "GetContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.containers.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "GetContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving the container.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Handlers — Create
    // =========================================================================

    /// <summary>
    /// POST /api/spe/containers?configId={id}
    ///
    /// Creates a new SharePoint Embedded container for the container type associated
    /// with the specified config. Validates the request body, creates the container
    /// via Graph API, and writes an audit log entry on success.
    /// </summary>
    private static async Task<IResult> CreateContainerAsync(
        [FromQuery] string? configId,
        CreateContainerRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate configId
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "CreateContainer: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "configId is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        // Validate displayName — required, non-whitespace, within Graph API limit
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            logger.LogWarning(
                "CreateContainer: missing or empty displayName, configId={ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: "displayName is required and cannot be empty or whitespace.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (request.DisplayName.Length > MaxDisplayNameLength)
        {
            logger.LogWarning(
                "CreateContainer: displayName exceeds {Max} characters ({Len}), configId={ConfigId}, TraceId={TraceId}",
                MaxDisplayNameLength, request.DisplayName.Length, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"displayName must not exceed {MaxDisplayNameLength} characters.",
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

            // Create the container in SharePoint Embedded via Graph API.
            var created = await graphService.CreateContainerForConfigAsync(
                config,
                config.ContainerTypeId,
                request.DisplayName,
                request.Description,
                ct);

            logger.LogInformation(
                "CreateContainer: created container '{ContainerId}' ('{DisplayName}') for configId {ConfigId}, TraceId={TraceId}",
                created.Id, created.DisplayName, configGuid, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            _ = auditService.LogOperationAsync(
                operation: "CreateContainer",
                category: "ContainerCreated",
                targetResource: created.Id,
                responseStatus: StatusCodes.Status201Created,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            var dto = ContainerDto.FromSummary(created);
            return TypedResults.Created($"/api/spe/containers/{created.Id}", dto);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "CreateContainer: configId {ConfigId} not found, TraceId={TraceId}",
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
                "CreateContainer: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.containers.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "CreateContainer: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while creating the container.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Handlers — Lifecycle (SPE-015): PATCH / activate / lock / unlock
    // =========================================================================

    /// <summary>
    /// PATCH /api/spe/containers/{containerId}?configId={id}
    ///
    /// Updates the displayName and/or description of a container.
    /// Returns 204 No Content on success, 404 if the container does not exist.
    /// Audit-logs the operation on success (fire-and-forget).
    /// </summary>
    private static async Task<IResult> PatchContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        UpdateContainerRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
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

        // At least one field must be provided to PATCH.
        if (request.DisplayName is null && request.Description is null)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "At least one of displayName or description must be provided.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (request.DisplayName is not null && request.DisplayName.Length > MaxDisplayNameLength)
        {
            return Results.Problem(
                title: "Bad Request",
                detail: $"displayName must not exceed {MaxDisplayNameLength} characters.",
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

            var found = await graphService.UpdateContainerForConfigAsync(
                config, containerId, request.DisplayName, request.Description, ct);

            if (!found)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "PatchContainer: updated container '{ContainerId}' for configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            _ = auditService.LogOperationAsync(
                operation: "UpdateContainer",
                category: "ContainerUpdated",
                targetResource: containerId,
                responseStatus: StatusCodes.Status204NoContent,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(ex, "PatchContainer: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(ex,
                "PatchContainer: Graph error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.containers.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "PatchContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while updating the container.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/activate?configId={id}
    ///
    /// Activates the container (status → active).
    /// Returns 204 on success, 404 if not found, 409 if Graph rejects the state transition.
    /// </summary>
    private static async Task<IResult> ActivateContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await ExecuteLifecycleOperationAsync(
            containerId, configId, graphService, auditService, logger, context, ct,
            operationName: "ActivateContainer",
            auditCategory: "ContainerActivated",
            graphOperation: graphService.ActivateContainerForConfigAsync);
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/lock?configId={id}
    ///
    /// Locks the container (status → locked), preventing user access.
    /// Returns 204 on success, 404 if not found, 409 if Graph rejects the state transition.
    /// </summary>
    private static async Task<IResult> LockContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await ExecuteLifecycleOperationAsync(
            containerId, configId, graphService, auditService, logger, context, ct,
            operationName: "LockContainer",
            auditCategory: "ContainerLocked",
            graphOperation: graphService.LockContainerForConfigAsync);
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/unlock?configId={id}
    ///
    /// Unlocks the container (status → active from locked), restoring user access.
    /// Returns 204 on success, 404 if not found, 409 if Graph rejects the state transition.
    /// </summary>
    private static async Task<IResult> UnlockContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await ExecuteLifecycleOperationAsync(
            containerId, configId, graphService, auditService, logger, context, ct,
            operationName: "UnlockContainer",
            auditCategory: "ContainerUnlocked",
            graphOperation: graphService.UnlockContainerForConfigAsync);
    }

    /// <summary>
    /// Shared implementation for activate, lock, and unlock lifecycle operations.
    /// Validates inputs, invokes the Graph operation (caller-supplied closure that
    /// captures graphService + builds the graphClient internally — keeps
    /// Microsoft.Graph.GraphServiceClient out of this file per ADR-007),
    /// logs the result, and fires an audit log entry.
    /// </summary>
    private static async Task<IResult> ExecuteLifecycleOperationAsync(
        string containerId,
        string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct,
        string operationName,
        string auditCategory,
        Func<SpeAdminGraphService.ContainerTypeConfig, string, CancellationToken, Task<bool>> graphOperation)
    {
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
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

            // Caller delegate calls a *ForConfig* facade on SpeAdminGraphService — keeps
            // Microsoft.Graph.GraphServiceClient out of this file per ADR-007 §1. The
            // facade also translates ODataError to SpaarkeStorageException internally.
            var found = await graphOperation(config, containerId, ct);

            if (!found)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "{Operation}: container '{ContainerId}' for configId {ConfigId}, TraceId={TraceId}",
                operationName, containerId, configGuid, context.TraceIdentifier);

            _ = auditService.LogOperationAsync(
                operation: operationName,
                category: auditCategory,
                targetResource: containerId,
                responseStatus: StatusCodes.Status204NoContent,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(ex,
                "{Operation}: configId {ConfigId} not found, TraceId={TraceId}",
                operationName, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (SpaarkeStorageException ex) when (ex.StatusCode == StatusCodes.Status409Conflict)
        {
            // Invalid state transition (e.g., activating already active, locking already locked)
            logger.LogWarning(
                "{Operation}: Graph returned 409 for container '{ContainerId}' — invalid state transition, TraceId={TraceId}",
                operationName, containerId, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "The container is already in the requested state or the state transition is not permitted.",
                errorCode: "spe.containers.graph_error",
                statusCode: StatusCodes.Status409Conflict,
                traceId: context.TraceIdentifier,
                title: "Conflict");
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(ex,
                "{Operation}: Graph error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                operationName, containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: "An error occurred communicating with the Graph API.",
                errorCode: "spe.containers.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "{Operation}: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                operationName, containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: $"An unexpected error occurred during {operationName}.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    // =========================================================================
    // Response DTOs (ADR-007: no Graph SDK types in public surface)
    // =========================================================================

    /// <summary>
    /// Request body for POST /api/spe/containers.
    /// <para>
    /// <c>DisplayName</c> is required; it becomes the container's visible name in SharePoint Embedded.
    /// <c>Description</c> is optional free-text.
    /// </para>
    /// </summary>
    public sealed record CreateContainerRequest(
        string DisplayName,
        string? Description);

    /// <summary>
    /// Request body for PATCH /api/spe/containers/{containerId}.
    /// All fields are optional — only non-null fields are sent to Graph API.
    /// At least one field must be non-null.
    /// </summary>
    public sealed record UpdateContainerRequest(
        string? DisplayName,
        string? Description);

    /// <summary>Paginated list of containers with optional skip token for next page.</summary>
    public sealed record ContainerListResponse(
        IReadOnlyList<ContainerDto> Items,
        string? NextSkipToken,
        int Count);

    /// <summary>Single container details returned by both list and get endpoints.</summary>
    /// <param name="CreatedDateTime">
    /// When the container was created, or null when Graph does not report it. Nullable since
    /// 2026-08-24 (task 024): the mapper previously substituted <c>DateTimeOffset.UtcNow</c>, so a
    /// container of unknown age rendered as "created today".
    /// </param>
    /// <param name="StorageUsedInBytes">
    /// Consumption in bytes, or null meaning <b>NOT REPORTED</b> — never zero. Graph returns this
    /// only on the <b>beta LIST</b> surface (measured live, task 020): it is absent from the v1.0
    /// schema entirely, and absent from GET even on beta. So a container fetched individually
    /// legitimately has no value here while the same container in the grid does. Clients MUST render
    /// that difference rather than collapsing null to 0 B (spec NFR-06).
    /// <para>
    /// This is <b>consumption</b>. The per-container quota <b>ceiling</b> is
    /// <c>maxStoragePerContainerInBytes</c> on the container TYPE — a different concept on a
    /// different resource, deliberately kept apart by task 023 (spec FR-C05).
    /// </para>
    /// </param>
    // =========================================================================
    // Archival (FR-E01) — task 050
    // =========================================================================

    /// <summary>
    /// The remediation an operator must perform when a container type has not opted into archival.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Verified by reflection over the shipping cmdlet, 2026-08-27 — do not "correct" this back.</b>
    /// The POML for this task, spec FR-E01, design.md §4.3 and the knowledge corpus all name
    /// <c>Set-SPOContainerType -IsArchiveEnabled</c>. <b>That parameter does not exist on that cmdlet
    /// in any module version.</b> <c>Set-SPOContainerType</c> accepts only ContainerTypeId,
    /// ContainerTypeName, AzureSubscriptionId, ResourceGroup and ApplicationRedirectUrl.
    /// <c>IsArchiveEnabled</c> lives on <b><c>Set-SPOContainerTypeConfiguration</c></b> (and on
    /// <c>New-SPOContainerType</c> at creation), typed <c>Nullable&lt;bool&gt;</c> — so it needs
    /// <c>$true</c>, not a bare switch.
    ///
    /// The module floor is quoted because it is load-bearing: the version widely installed today
    /// (16.0.26413.0) has no archive parameter on ANY cmdlet, so an admin following an otherwise
    /// correct instruction still hits "a parameter cannot be found". Naming the cmdlet without the
    /// version would just move the dead end one step later.
    ///
    /// See notes/task-050-findings.md §1.
    /// </remarks>
    private const string ArchivalOptInRemediation =
        "Enable archival on the container type with SharePoint Online PowerShell " +
        "(module 16.0.27515.12000 or later — earlier modules do not expose this parameter): " +
        "Set-SPOContainerTypeConfiguration -ContainerTypeId <containerTypeId> -IsArchiveEnabled $true";

    /// <summary>
    /// POST /api/spe/containers/{containerId}/archive?configId={id}
    /// </summary>
    private static Task<IResult> ArchiveContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
        => RunArchivalActionAsync(
            containerId, configId, graphService, auditService, logger, context, ct,
            isArchive: true);

    /// <summary>
    /// POST /api/spe/containers/{containerId}/unarchive?configId={id}
    /// </summary>
    private static Task<IResult> UnarchiveContainerAsync(
        string containerId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
        => RunArchivalActionAsync(
            containerId, configId, graphService, auditService, logger, context, ct,
            isArchive: false);

    /// <summary>
    /// Shared handler for archive and unarchive — same validation, same failure taxonomy, same audit
    /// shape. Written once so the two cannot drift apart in their error handling, which is the part
    /// that matters most here.
    /// </summary>
    private static async Task<IResult> RunArchivalActionAsync(
        string containerId,
        string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct,
        bool isArchive)
    {
        var operation = isArchive ? "ArchiveContainer" : "UnarchiveContainer";
        var verb = isArchive ? "archive" : "unarchive";

        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "{Operation}: missing or invalid configId '{ConfigId}', TraceId={TraceId}",
                operation, configId, context.TraceIdentifier);

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

            var found = isArchive
                ? await graphService.ArchiveContainerForConfigAsync(config, containerId, ct)
                : await graphService.UnarchiveContainerForConfigAsync(config, containerId, ct);

            if (!found)
            {
                logger.LogInformation(
                    "{Operation}: container '{ContainerId}' not found, configId {ConfigId}, TraceId={TraceId}",
                    operation, containerId, configGuid, context.TraceIdentifier);

                return Results.Problem(
                    title: "Not Found",
                    detail: $"Container '{containerId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "{Operation}: Graph accepted {Verb} for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                operation, verb, containerId, configGuid, context.TraceIdentifier);

            _ = auditService.LogOperationAsync(
                operation: operation,
                category: "ContainerLifecycle",
                targetResource: containerId,
                responseStatus: StatusCodes.Status202Accepted,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            // 202, not 200. Graph performs this asynchronously and models the intermediate states
            // explicitly (recentlyArchived → fullyArchived; reactivating → active), so returning 200
            // would assert a completion that has demonstrably not happened. `pending: true` makes the
            // client state that in words rather than leaving it to be inferred from a status code.
            return TypedResults.Accepted(
                (string?)null,
                new
                {
                    message = isArchive
                        ? $"Archival of container '{containerId}' has been accepted. Archiving continues in the background."
                        : $"Restore of container '{containerId}' has been accepted. The container is reactivating and is not yet available.",
                    pending = true,
                    expectedNextState = isArchive ? "recentlyArchived" : "reactivating"
                });
        }
        catch (SpeAdminGraphService.ArchivalNotEnabledException ex)
        {
            // The whole point of this task's error work. Graph answers 403 here, but relaying 403
            // would send an administrator to audit their own permissions — which cannot fix it, because
            // nothing about the caller is wrong. 409 Conflict says what is true: the request is
            // well-formed and authorized, and conflicts with the current state of the container type.
            logger.LogWarning(
                ex,
                "{Operation}: refused — container type has not opted into archival. Container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                operation, containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Archival Not Enabled",
                detail:
                    $"Container '{containerId}' cannot be {(isArchive ? "archived" : "unarchived")} because archival is not " +
                    "enabled on its container type. This is a one-time operator action and is not " +
                    "configurable from this app. " + ArchivalOptInRemediation,
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = context.TraceIdentifier,
                    ["errorCode"] = "spe.container.archival_not_enabled",
                    ["remediation"] = ArchivalOptInRemediation,
                    // Graph's own words, kept verbatim. If Microsoft's message ever stops matching our
                    // diagnosis, an operator can see that from the payload instead of trusting ours.
                    ["graphMessage"] = ex.GraphMessage,
                });
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "{Operation}: configId {ConfigId} not found, TraceId={TraceId}",
                operation, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Bad Request",
                detail: $"Container type config '{configGuid}' was not found.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (SpaarkeStorageException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "{Operation}: Graph returned 404 for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                operation, containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Not Found",
                detail: $"Container '{containerId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "{Operation}: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                operation, containerId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: $"An error occurred while attempting to {verb} the container.",
                errorCode: $"spe.container.{verb}_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "{Operation}: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                operation, containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: ProblemDetailsHelper.Explain(
                    $"An unexpected error occurred while attempting to {verb} the container.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }

    /// <param name="Status">
    /// <c>active</c> / <c>inactive</c>, or <b>null meaning NOT REPORTED</b> — never a guess.
    ///
    /// 🔑 Null on <b>every list row</b>: Graph drops <c>status</c> from container collection rows even
    /// when <c>$select</c> asks for it (measured 2026-08-27, task 050 — notes/task-050-findings.md §4).
    /// Populated on GET-single and on create.
    ///
    /// This was non-nullable <c>string Status</c> until 2026-08-27, and the mapper behind it ended
    /// <c>: "active"</c>. Because <c>status</c> is a <i>typed</i> property on the Graph SDK's
    /// <c>FileStorageContainer</c>, the old reader — which searched <c>AdditionalData</c> — could never
    /// match it, so that fallback fired for <b>100% of responses on every path</b>. The API asserted
    /// "active" for every container including ones Graph had just reported as <c>inactive</c>.
    /// Unlike <c>WebUrl</c> this is emitted as an explicit <c>null</c> rather than omitted, because the
    /// grid has a Status column that must render something: null renders as an explicit absent marker,
    /// and a client MUST NOT substitute a default (spec NFR-06).
    /// </param>
    /// <param name="ArchiveStatus">
    /// <c>recentlyArchived</c> / <c>fullyArchived</c> / <c>reactivating</c>, or null for "no archive
    /// state to show" (FR-E01).
    ///
    /// ⚠️ A <b>separate dimension</b> from <paramref name="Status"/> — a container can be
    /// <c>active</c> and <c>fullyArchived</c> simultaneously. Graph has no <c>notArchived</c> value on
    /// either API version, so null cannot distinguish "not archived" from "not reported" and must
    /// never be rendered as a positive claim that content is online.
    ///
    /// Omitted from the payload when null, like <c>WebUrl</c>: archival is opt-in per container type,
    /// and emitting <c>"archiveStatus": null</c> across a whole grid would read as "we checked, none
    /// are archived" on a tenant where the capability is simply switched off.
    /// </param>
    public sealed record ContainerDto(
        string Id,
        string DisplayName,
        string? Description,
        string ContainerTypeId,
        DateTimeOffset? CreatedDateTime,
        long? StorageUsedInBytes,
        string? Status,
        // FR-C10 — the container's SharePoint URL, the scoping key for a Purview eDiscovery search.
        //
        // 🔑 `WhenWritingNull` is LOAD-BEARING, not tidiness. Graph cannot return this on a LIST
        // (measured 2026-08-24 — the collection accepts $expand=drive($select=webUrl), answers 200,
        // and drops `drive` from every row; notes/task-028-findings.md §1). Emitting `"webUrl": null`
        // on 5 list rows would invite exactly one reading — "these containers have no URL" — which is
        // false; we never asked. Omitting the key means a client cannot bind the grid to it by
        // accident, and `webUrl === undefined` on a DETAIL response carries its honest meaning:
        // Graph was asked and did not report one. Pinned by
        // SpeAdminContainerUrlMappingTests.ListRows_DoNotCarryAWebUrlKey_BecauseGraphCannotSupplyIt.
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? WebUrl = null,

        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? ArchiveStatus = null,

        /// <summary>
        /// Per-container storage quota (FR-E02). Present on the DETAIL response only — Graph reports it
        /// on the expanded drive, which a LIST cannot carry. Omitted when absent, like WebUrl.
        /// </summary>
        [property: System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        ContainerQuotaDto? Quota = null)
    {
        /// <summary>Maps a <see cref="SpeAdminGraphService.SpeContainerSummary"/> domain record to a DTO.</summary>
        public static ContainerDto FromSummary(SpeAdminGraphService.SpeContainerSummary summary) =>
            new(
                summary.Id,
                summary.DisplayName,
                summary.Description,
                summary.ContainerTypeId,
                summary.CreatedDateTime,
                summary.StorageUsedInBytes,
                summary.Status,
                summary.WebUrl,
                summary.ArchiveStatus,
                ContainerQuotaDto.FromDomain(summary.Quota));
    }

    /// <summary>
    /// Per-container storage quota (FR-E02, task 051).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔑 <b><see cref="Total"/> is a container-TYPE setting, not a per-container one.</b> It is the
    /// type's <c>maxStoragePerContainerInBytes</c> as it applies to this container, so it is identical
    /// for every container of the type. Clients MUST label it in a way that does not imply this one
    /// container can be given a different cap — it cannot. Graph exposes no per-container ceiling:
    /// <c>fileStorageContainerSettings</c> has no storage property on either API version, and a
    /// container-scope PATCH returns 200 while discarding the value (measured 2026-08-27).
    /// </para>
    /// <para>
    /// <see cref="Used"/> is the only consumption figure available on a single-container fetch —
    /// <c>storageUsedInBytes</c> is LIST-only (tasks 020/024). It is a genuinely different reading
    /// from that field, not a duplicate: this one includes the drive's own accounting and is paired
    /// with <see cref="Deleted"/>.
    /// </para>
    /// </remarks>
    public sealed record ContainerQuotaDto(
        long? Total,
        long? Used,
        long? Remaining,
        long? Deleted,
        string? State)
    {
        public static ContainerQuotaDto? FromDomain(SpeAdminGraphService.SpeContainerQuota? quota) =>
            quota is null
                ? null
                : new ContainerQuotaDto(quota.Total, quota.Used, quota.Remaining, quota.Deleted, quota.State);
    }
}
