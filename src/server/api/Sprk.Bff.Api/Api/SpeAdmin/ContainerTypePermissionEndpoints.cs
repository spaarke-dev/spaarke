using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoint for listing application permissions on a SharePoint Embedded container type.
///
/// Route (under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET /api/spe/containertypes/{typeId}/permissions?configId={id}
///
/// This read-only endpoint provides visibility into which consuming applications have been granted
/// permissions for a container type, supporting compliance auditing of access control configuration.
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class ContainerTypePermissionEndpoints
{
    /// <summary>
    /// Registers the container type permissions endpoint on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static RouteGroupBuilder MapContainerTypePermissionEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/spe/containertypes/{typeId}/permissions?configId={id}
        group.MapGet("/containertypes/{typeId}/permissions", GetContainerTypePermissionsAsync)
            .WithName("SpeGetContainerTypePermissions")
            .WithSummary("List application permissions for an SPE container type")
            .WithDescription(
                "Returns all application permissions registered for the specified SharePoint Embedded " +
                "container type. Each entry identifies a consuming application (by appId) and lists " +
                "its delegated and application permission scopes. Returns an empty list when no " +
                "permissions have been registered. Returns 404 when the container type is not found.")
            .Produces<ContainerTypePermissionListDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        /*
         * ── Container-type OWNERS (FR-C09, task 027) ──
         *
         * 🔑 Routed at /owners, NOT /permissions, and the distinction is load-bearing.
         *
         * "/containertypes/{id}/permissions" above already means Graph's `applicationPermissions` —
         * which APPLICATIONS may access containers of this type, with what scopes. These endpoints
         * expose `fileStorageContainerType.permissions`, which is `Collection(graph.permission)` —
         * which PEOPLE own the container type. The two are orthogonal; neither supersedes the other.
         *
         * Task 027's own POML conflated them (it called this a supersession of the screen above).
         * Naming the route /owners makes the difference legible in the URL, so the next reader cannot
         * repeat that conflation by glancing at a route table.
         *
         * All three run DELEGATED against BETA — the only combination Graph will serve. See
         * SpeAdminGraphService's container-type owners region for the measurement.
         */
        group.MapGet("/containertypes/{typeId}/owners", ListContainerTypeOwnersAsync)
            .WithName("SpeListContainerTypeOwners")
            .WithSummary("List the owners of an SPE container type")
            .WithDescription(
                "Returns the people who own (administer) the specified container type. Distinct from " +
                "/permissions, which lists consuming APPLICATIONS. Returns 404 when the container " +
                "type is not found.")
            .Produces<ContainerTypeOwnerListDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/containertypes/{typeId}/owners", AddContainerTypeOwnerAsync)
            .WithName("SpeAddContainerTypeOwner")
            .WithSummary("Grant ownership of an SPE container type to a user")
            .WithDescription(
                "Grants the 'owner' role on the container type to the supplied user (email/UPN or " +
                "directory object id). The identifier is passed to Graph as given — an unknown user " +
                "surfaces Graph's own error rather than succeeding silently.")
            .Produces<ContainerTypeOwnerDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/containertypes/{typeId}/owners/{permissionId}", RemoveContainerTypeOwnerAsync)
            .WithName("SpeRemoveContainerTypeOwner")
            .WithSummary("Revoke an ownership grant on an SPE container type")
            .WithDescription(
                "Revokes the named ownership grant. Returns 404 when the grant or the container type " +
                "does not exist — a removal that removed nothing is reported as such, not as success.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Owner handlers (FR-C09)
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> ListContainerTypeOwnersAsync(
        string typeId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var owners = await graphService.ListContainerTypeOwnersForUserAsync(context, typeId, ct);
            if (owners is null)
            {
                return Results.NotFound();
            }

            return TypedResults.Ok(new ContainerTypeOwnerListDto
            {
                Items = owners.Select(ContainerTypeOwnerDto.FromDomain).ToList(),
            });
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex, "ListContainerTypeOwners failed for {TypeId}. Status={Status}. TraceId={TraceId}",
                typeId, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: $"Could not list owners for container type '{typeId}'.",
                errorCode: "spe.containertypes.owners.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
    }

    private static async Task<IResult> AddContainerTypeOwnerAsync(
        string typeId,
        AddContainerTypeOwnerRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.UserIdentifier))
        {
            return Results.Problem(
                detail: "A 'userIdentifier' (email/UPN or directory object id) is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.owners.user_required",
                    ["traceId"] = context.TraceIdentifier,
                });
        }

        try
        {
            var owner = await graphService.AddContainerTypeOwnerForUserAsync(
                context, typeId, request.UserIdentifier, ct);

            if (owner is null)
            {
                return Results.NotFound();
            }

            return TypedResults.Ok(ContainerTypeOwnerDto.FromDomain(owner));
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex, "AddContainerTypeOwner failed for {TypeId}. Status={Status}. TraceId={TraceId}",
                typeId, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: $"Could not add an owner to container type '{typeId}'.",
                errorCode: "spe.containertypes.owners.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
    }

    private static async Task<IResult> RemoveContainerTypeOwnerAsync(
        string typeId,
        string permissionId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        try
        {
            var removed = await graphService.RemoveContainerTypeOwnerForUserAsync(
                context, typeId, permissionId, ct);

            // A removal that removed nothing is a 404, not a 204. Reporting success here would be
            // this project's signature defect: the caller reloads, the owner is still listed, and
            // nothing ever said the operation did not apply.
            return removed ? Results.NoContent() : Results.NotFound();
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex, "RemoveContainerTypeOwner failed for {TypeId}/{PermissionId}. Status={Status}. TraceId={TraceId}",
                typeId, permissionId, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: $"Could not remove the owner from container type '{typeId}'.",
                errorCode: "spe.containertypes.owners.graph_error",
                statusCode: ex.ClientStatusFor(),
                traceId: context.TraceIdentifier,
                title: "Graph API Error");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/containertypes/{typeId}/permissions?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client authenticated as the config's app
    /// registration, retrieves all application permissions for the container type, and returns them
    /// as a <see cref="ContainerTypePermissionListDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Permissions returned (may be an empty list).
    ///   400 Bad Request — configId is missing, invalid, or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> GetContainerTypePermissionsAsync(
        string typeId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate typeId path parameter
        if (string.IsNullOrWhiteSpace(typeId))
        {
            logger.LogWarning("GET /api/spe/containertypes/permissions — missing typeId");
            return Results.Problem(
                detail: "The 'typeId' path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.permissions.type_id_required",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        // Validate required configId query parameter
        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            logger.LogWarning(
                "GET /api/spe/containertypes/{TypeId}/permissions — missing or invalid configId", typeId);
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.permissions.config_id_required",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configGuid, ct);
        if (config is null)
        {
            logger.LogWarning(
                "GET /api/spe/containertypes/{TypeId}/permissions — config {ConfigId} not found in Dataverse. TraceId: {TraceId}",
                typeId, configGuid, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configGuid}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.permissions.config_not_found",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        try
        {
            // Retrieve application permissions for the container type from Graph API
            var permissions = await graphService.GetContainerTypePermissionsForConfigAsync(
                config, typeId, ct);

            // null indicates the container type was not found (Graph 404)
            if (permissions is null)
            {
                logger.LogInformation(
                    "GET /api/spe/containertypes/{TypeId}/permissions — container type not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, configGuid, context.TraceIdentifier);
                return Results.Problem(
                    detail: $"Container type '{typeId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.containertypes.permissions.not_found",
                        ["traceId"] = context.TraceIdentifier
                    });
            }

            logger.LogInformation(
                "GET /api/spe/containertypes/{TypeId}/permissions — returned {Count} app permissions for config {ConfigId}. TraceId: {TraceId}",
                typeId, permissions.Count, configGuid, context.TraceIdentifier);

            // Map domain records to API DTOs
            var items = permissions
                .Select(p => new ContainerTypePermissionDto
                {
                    AppId = p.AppId,
                    DelegatedPermissions = p.DelegatedPermissions,
                    ApplicationPermissions = p.ApplicationPermissions
                })
                .ToList();

            return Results.Ok(new ContainerTypePermissionListDto
            {
                Items = items,
                Count = items.Count
            });
        }
        catch (SpaarkeStorageException ex)
        {
            logger.LogError(
                ex,
                "Graph API error getting permissions for container type {TypeId}, config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                typeId, configGuid, ex.StatusCode, context.TraceIdentifier);

            return ex.ToProblemDetails(
                summary: $"Could not retrieve permissions for container type '{typeId}'.",
                errorCode: "spe.containertypes.permissions.graph_error",
                statusCode: StatusCodes.Status500InternalServerError,
                traceId: context.TraceIdentifier);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Unexpected error getting permissions for container type {TypeId}, config {ConfigId}. TraceId: {TraceId}",
                typeId, configGuid, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving container type permissions.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.permissions.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }
}
