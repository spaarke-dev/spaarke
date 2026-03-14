using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Endpoints.SpeAdmin;

/// <summary>
/// Endpoints for managing SharePoint Embedded (SPE) container permissions.
///
/// Routes (all under the /api/spe group from <see cref="Api.SpeAdminEndpoints"/>):
///   GET    /api/spe/containers/{containerId}/permissions?configId={id}
///   POST   /api/spe/containers/{containerId}/permissions?configId={id}
///   PATCH  /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
///   DELETE /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// Permission changes are security-sensitive — all mutations are audit logged.
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class ContainerPermissionEndpoints
{
    // =========================================================================
    // Valid SPE permission roles
    // =========================================================================

    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "reader", "writer", "manager", "owner"
    };

    // =========================================================================
    // Route registration
    // =========================================================================

    /// <summary>
    /// Registers container permission CRUD endpoints on the provided route group.
    /// Called from <see cref="Api.SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static void MapContainerPermissionEndpoints(RouteGroupBuilder group)
    {
        // GET /api/spe/containers/{containerId}/permissions?configId={id}
        group.MapGet("/containers/{containerId}/permissions", ListPermissionsAsync)
            .WithName("SpeListContainerPermissions")
            .WithSummary("List all permissions on an SPE container")
            .WithDescription(
                "Returns the full permission list for the specified container, " +
                "including user/group identity and assigned role.")
            .Produces<ContainerPermissionListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containers/{containerId}/permissions?configId={id}
        group.MapPost("/containers/{containerId}/permissions", GrantPermissionAsync)
            .WithName("SpeGrantContainerPermission")
            .WithSummary("Grant a new permission on an SPE container")
            .WithDescription(
                "Grants the specified user or group a role on the container. " +
                "Valid roles: reader, writer, manager, owner.")
            .Produces<ContainerPermissionDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PATCH /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
        group.MapPatch("/containers/{containerId}/permissions/{permissionId}", UpdatePermissionAsync)
            .WithName("SpeUpdateContainerPermission")
            .WithSummary("Update an existing permission's role on an SPE container")
            .WithDescription(
                "Changes the role assigned to an existing permission. " +
                "Valid roles: reader, writer, manager, owner.")
            .Produces<ContainerPermissionDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
        group.MapDelete("/containers/{containerId}/permissions/{permissionId}", RevokePermissionAsync)
            .WithName("SpeRevokeContainerPermission")
            .WithSummary("Revoke a permission from an SPE container")
            .WithDescription(
                "Removes the specified permission entry, revoking the user/group's access to the container.")
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
    /// GET /api/spe/containers/{containerId}/permissions?configId={id}
    ///
    /// Returns all permission entries on the specified container.
    /// Each entry includes the user or group identity and the assigned SPE role.
    /// </summary>
    private static async Task<IResult> ListPermissionsAsync(
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

            var permissions = await graphService.ListContainerPermissionsAsync(graphClient, containerId, ct);

            var result = new ContainerPermissionListResponse(
                permissions.Select(ContainerPermissionDto.FromDomain).ToList(),
                permissions.Count);

            logger.LogInformation(
                "ListPermissions: returned {Count} permissions for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                result.Count, containerId, configGuid, context.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "ListPermissions: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "ListPermissions: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "ListPermissions: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("listing permissions", context);
        }
    }

    /// <summary>
    /// POST /api/spe/containers/{containerId}/permissions?configId={id}
    ///
    /// Grants a new permission to a user or group on the specified container.
    /// The role must be one of: reader, writer, manager, owner.
    /// Audit logged on success.
    /// </summary>
    private static async Task<IResult> GrantPermissionAsync(
        string containerId,
        [FromQuery] string? configId,
        [FromBody] GrantPermissionRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        if (!TryValidateGrantRequest(request, context, out problem))
            return problem!;

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var granted = await graphService.GrantContainerPermissionAsync(
                graphClient, containerId, request.UserId, request.GroupId, request.Role, ct);

            logger.LogInformation(
                "GrantPermission: granted '{Role}' to principal (userId={UserId}, groupId={GroupId}) " +
                "on container '{ContainerId}', configId {ConfigId}, permissionId={PermissionId}, TraceId={TraceId}",
                request.Role, request.UserId, request.GroupId,
                containerId, configGuid, granted.Id, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            _ = auditService.LogOperationAsync(
                operation: "PermissionGranted",
                category: "Permission",
                targetResource: $"{containerId}/permissions/{granted.Id}",
                responseStatus: StatusCodes.Status201Created,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            var dto = ContainerPermissionDto.FromDomain(granted);
            return TypedResults.Created($"/api/spe/containers/{containerId}/permissions/{granted.Id}", dto);
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "GrantPermission: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "GrantPermission: Graph API error for container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "GrantPermission: unexpected error for container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("granting permission", context);
        }
    }

    /// <summary>
    /// PATCH /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
    ///
    /// Updates the role assigned to an existing permission entry.
    /// Audit logged on success.
    /// </summary>
    private static async Task<IResult> UpdatePermissionAsync(
        string containerId,
        string permissionId,
        [FromQuery] string? configId,
        [FromBody] UpdatePermissionRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (string.IsNullOrWhiteSpace(permissionId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "permissionId path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }

        if (!TryValidateConfigId(configId, context, out var configGuid, out problem))
            return problem!;

        if (!TryValidateRole(request?.Role, context, out problem))
            return problem!;

        try
        {
            var config = await graphService.ResolveConfigAsync(configGuid, ct);
            if (config is null)
                return ConfigNotFoundProblem(configGuid, context);

            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            var updated = await graphService.UpdateContainerPermissionAsync(
                graphClient, containerId, permissionId, request!.Role, ct);

            if (updated is null)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Permission '{permissionId}' was not found on container '{containerId}'.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "UpdatePermission: updated permission '{PermissionId}' to role '{Role}' " +
                "on container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                permissionId, request.Role, containerId, configGuid, context.TraceIdentifier);

            // Audit log — fire-and-forget.
            _ = auditService.LogOperationAsync(
                operation: "PermissionUpdated",
                category: "Permission",
                targetResource: $"{containerId}/permissions/{permissionId}",
                responseStatus: StatusCodes.Status200OK,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.Ok(ContainerPermissionDto.FromDomain(updated));
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "UpdatePermission: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "UpdatePermission: Graph returned 404 for permission '{PermissionId}' on container '{ContainerId}', TraceId={TraceId}",
                permissionId, containerId, context.TraceIdentifier);
            return Results.Problem(
                title: "Not Found",
                detail: $"Permission '{permissionId}' was not found on container '{containerId}'.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "UpdatePermission: Graph API error for permission '{PermissionId}', container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                permissionId, containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "UpdatePermission: unexpected error for permission '{PermissionId}', container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                permissionId, containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("updating permission", context);
        }
    }

    /// <summary>
    /// DELETE /api/spe/containers/{containerId}/permissions/{permissionId}?configId={id}
    ///
    /// Revokes an existing permission entry, removing the user/group's access to the container.
    /// Audit logged on success.
    /// </summary>
    private static async Task<IResult> RevokePermissionAsync(
        string containerId,
        string permissionId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!TryValidateContainerId(containerId, context, out var problem))
            return problem!;

        if (string.IsNullOrWhiteSpace(permissionId))
        {
            return Results.Problem(
                title: "Bad Request",
                detail: "permissionId path parameter is required.",
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

            var deleted = await graphService.RevokeContainerPermissionAsync(
                graphClient, containerId, permissionId, ct);

            if (!deleted)
            {
                return Results.Problem(
                    title: "Not Found",
                    detail: $"Permission '{permissionId}' was not found on container '{containerId}'.",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            }

            logger.LogInformation(
                "RevokePermission: revoked permission '{PermissionId}' from container '{ContainerId}', " +
                "configId {ConfigId}, TraceId={TraceId}",
                permissionId, containerId, configGuid, context.TraceIdentifier);

            // Audit log — fire-and-forget.
            _ = auditService.LogOperationAsync(
                operation: "PermissionRevoked",
                category: "Permission",
                targetResource: $"{containerId}/permissions/{permissionId}",
                responseStatus: StatusCodes.Status204NoContent,
                configId: configGuid,
                cancellationToken: CancellationToken.None);

            return TypedResults.NoContent();
        }
        catch (SpeAdminGraphService.ConfigNotFoundException ex)
        {
            logger.LogWarning(
                ex, "RevokePermission: configId {ConfigId} not found, TraceId={TraceId}",
                configGuid, context.TraceIdentifier);
            return ConfigNotFoundProblem(configGuid, context);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status404NotFound)
        {
            logger.LogInformation(
                "RevokePermission: Graph returned 404 for permission '{PermissionId}' on container '{ContainerId}', TraceId={TraceId}",
                permissionId, containerId, context.TraceIdentifier);
            return Results.Problem(
                title: "Not Found",
                detail: $"Permission '{permissionId}' was not found on container '{containerId}'.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "RevokePermission: Graph API error for permission '{PermissionId}', container '{ContainerId}', configId {ConfigId}, Status={Status}, TraceId={TraceId}",
                permissionId, containerId, configGuid, odataError.ResponseStatusCode, context.TraceIdentifier);
            return GraphApiProblem(odataError, context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "RevokePermission: unexpected error for permission '{PermissionId}', container '{ContainerId}', configId {ConfigId}, TraceId={TraceId}",
                permissionId, containerId, configGuid, context.TraceIdentifier);
            return UnexpectedProblem("revoking permission", context);
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

    private static bool TryValidateRole(string? role, HttpContext context, out IResult? problem)
    {
        if (string.IsNullOrWhiteSpace(role) || !ValidRoles.Contains(role))
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: $"Invalid role '{role}'. Valid roles are: reader, writer, manager, owner.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        problem = null;
        return true;
    }

    private static bool TryValidateGrantRequest(
        GrantPermissionRequest? request,
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

        // Must supply exactly one of userId or groupId
        var hasUserId = !string.IsNullOrWhiteSpace(request.UserId);
        var hasGroupId = !string.IsNullOrWhiteSpace(request.GroupId);

        if (!hasUserId && !hasGroupId)
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: "Either userId or groupId must be provided.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        if (hasUserId && hasGroupId)
        {
            problem = Results.Problem(
                title: "Bad Request",
                detail: "Only one of userId or groupId may be provided, not both.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
            return false;
        }

        return TryValidateRole(request.Role, context, out problem);
    }

    // =========================================================================
    // ProblemDetails factory helpers (avoid repetition across handlers)
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

    // =========================================================================
    // Request DTOs
    // =========================================================================

    /// <summary>
    /// Request body for POST — grants a new permission on a container.
    /// Exactly one of <see cref="UserId"/> or <see cref="GroupId"/> must be provided.
    /// </summary>
    public sealed record GrantPermissionRequest(
        /// <summary>Azure AD user object ID to grant access to. Mutually exclusive with <see cref="GroupId"/>.</summary>
        string? UserId,
        /// <summary>Azure AD group object ID to grant access to. Mutually exclusive with <see cref="UserId"/>.</summary>
        string? GroupId,
        /// <summary>SPE role to assign: reader, writer, manager, or owner.</summary>
        string Role);

    /// <summary>
    /// Request body for PATCH — updates an existing permission's role.
    /// </summary>
    public sealed record UpdatePermissionRequest(
        /// <summary>New SPE role: reader, writer, manager, or owner.</summary>
        string Role);

    // =========================================================================
    // Response DTOs (ADR-007: no Graph SDK types in public surface)
    // =========================================================================

    /// <summary>Paginated response containing all permission entries for a container.</summary>
    public sealed record ContainerPermissionListResponse(
        IReadOnlyList<ContainerPermissionDto> Items,
        int Count);

    /// <summary>Single permission entry: who has access and at what role.</summary>
    public sealed record ContainerPermissionDto(
        /// <summary>Graph permission ID (opaque string).</summary>
        string Id,
        /// <summary>SPE role assigned: reader, writer, manager, or owner.</summary>
        string Role,
        /// <summary>User or group display name. Null when principal cannot be resolved.</summary>
        string? DisplayName,
        /// <summary>User or group email address. Null for service principals or when unavailable.</summary>
        string? Email,
        /// <summary>Azure AD object ID of the user or group.</summary>
        string? PrincipalId,
        /// <summary>Type of principal: user, group, or application.</summary>
        string? PrincipalType)
    {
        /// <summary>Maps a <see cref="SpeAdminGraphService.SpeContainerPermission"/> domain record to a DTO.</summary>
        public static ContainerPermissionDto FromDomain(SpeAdminGraphService.SpeContainerPermission perm) =>
            new(
                perm.Id,
                perm.Role,
                perm.DisplayName,
                perm.Email,
                perm.PrincipalId,
                perm.PrincipalType);
    }
}
