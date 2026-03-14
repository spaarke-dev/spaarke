using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;

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

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // Use paginated list — supports $top and $skipToken for cursor-based pagination.
            var page = await graphService.ListContainersPageAsync(
                graphClient, config.ContainerTypeId, top, skipToken, ct);

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
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError, "ListContainers: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex, "ListContainers: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while listing containers.",
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

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // Retrieve single container directly from Graph (more efficient than listing all).
            var container = await graphService.GetContainerAsync(graphClient, containerId, ct);

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
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
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
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "GetContainer: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex, "GetContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving the container.",
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

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // Create the container in SharePoint Embedded via Graph API.
            var created = await graphService.CreateContainerAsync(
                graphClient,
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
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "CreateContainer: Graph API error for configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
                ex, "CreateContainer: unexpected error for configId {ConfigId}, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while creating the container.",
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

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var found = await graphService.UpdateContainerAsync(
                graphClient, containerId, request.DisplayName, request.Description, ct);

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
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "PatchContainer: Graph error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
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
            logger.LogError(ex,
                "PatchContainer: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while updating the container.",
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
            graphOperation: (svc, client, id, token) => svc.ActivateContainerAsync(client, id, token));
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
            graphOperation: (svc, client, id, token) => svc.LockContainerAsync(client, id, token));
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
            graphOperation: (svc, client, id, token) => svc.UnlockContainerAsync(client, id, token));
    }

    /// <summary>
    /// Shared implementation for activate, lock, and unlock lifecycle operations.
    /// Validates inputs, resolves the Graph client, invokes the Graph operation,
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
        Func<SpeAdminGraphService, Microsoft.Graph.GraphServiceClient, string, CancellationToken, Task<bool>> graphOperation)
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

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var found = await graphOperation(graphService, graphClient, containerId, ct);

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
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status409Conflict)
        {
            // Invalid state transition (e.g., activating already active, locking already locked)
            logger.LogWarning(
                "{Operation}: Graph returned 409 for container '{ContainerId}' — invalid state transition, TraceId={TraceId}",
                operationName, containerId, context.TraceIdentifier);

            return Results.Problem(
                title: "Conflict",
                detail: odataError.Error?.Message ?? "The container is already in the requested state or the state transition is not permitted.",
                statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError,
                "{Operation}: Graph error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                operationName, containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);

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
    public sealed record ContainerDto(
        string Id,
        string DisplayName,
        string? Description,
        string ContainerTypeId,
        DateTimeOffset CreatedDateTime,
        long? StorageUsedInBytes)
    {
        /// <summary>Maps a <see cref="SpeAdminGraphService.SpeContainerSummary"/> domain record to a DTO.</summary>
        public static ContainerDto FromSummary(SpeAdminGraphService.SpeContainerSummary summary) =>
            new(
                summary.Id,
                summary.DisplayName,
                summary.Description,
                summary.ContainerTypeId,
                summary.CreatedDateTime,
                summary.StorageUsedInBytes);
    }
}
