using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for managing consuming application registrations on a SharePoint Embedded container type.
///
/// Routes (under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   GET    /api/spe/containertypes/{typeId}/consumers?configId={id}            — list consumers
///   POST   /api/spe/containertypes/{typeId}/consumers?configId={id}            — register new consumer
///   PUT    /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}    — update consumer permissions
///   DELETE /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}    — remove consumer
///
/// In multi-tenant SPE scenarios, a single container type owned by one app can be consumed by
/// multiple applications from different tenants. These endpoints provide visibility and management
/// of those consuming application registrations.
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// All four verbs require admin role — no per-endpoint auth filter needed (ADR-008).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class ConsumingTenantEndpoints
{
    /// <summary>
    /// Registers all /api/spe/containertypes/{typeId}/consumers endpoints on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// Authorization is inherited from the parent group (ADR-008).
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    /// <returns>The route group for chaining.</returns>
    public static RouteGroupBuilder MapConsumingTenantEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/spe/containertypes/{typeId}/consumers?configId={id}
        group.MapGet("/containertypes/{typeId}/consumers", ListConsumersAsync)
            .WithName("SpeListConsumingTenants")
            .WithSummary("List consuming application registrations for an SPE container type")
            .WithDescription(
                "Returns all consuming application registrations for the specified SharePoint Embedded " +
                "container type. Each entry identifies a consuming application (by appId), its optional " +
                "display name, tenant ID, and the permissions granted. Returns an empty list when no " +
                "consuming apps have been registered.")
            .Produces<ConsumingTenantListDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containertypes/{typeId}/consumers?configId={id}
        group.MapPost("/containertypes/{typeId}/consumers", RegisterConsumerAsync)
            .WithName("SpeRegisterConsumingTenant")
            .WithSummary("Register a new consuming application for an SPE container type")
            .WithDescription(
                "Registers a consuming application with the specified container type, granting it " +
                "the requested delegated and application permissions. The consuming app can be from " +
                "the same tenant or a different tenant (multi-tenant scenario).")
            .Produces<ConsumingTenantDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // PUT /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
        group.MapPut("/containertypes/{typeId}/consumers/{appId}", UpdateConsumerAsync)
            .WithName("SpeUpdateConsumingTenant")
            .WithSummary("Update permissions for an existing consuming application registration")
            .WithDescription(
                "Replaces the delegated and application permissions for the specified consuming application. " +
                "The appId in the path must match an existing consuming application registration for " +
                "the container type.")
            .Produces<ConsumingTenantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // DELETE /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
        group.MapDelete("/containertypes/{typeId}/consumers/{appId}", RemoveConsumerAsync)
            .WithName("SpeRemoveConsumingTenant")
            .WithSummary("Remove a consuming application registration from an SPE container type")
            .WithDescription(
                "Removes the consuming application registration, revoking all permissions previously " +
                "granted to that application for the container type.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/spe/containertypes/{typeId}/consumers?configId={id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all consuming application registrations for the specified container type.
    ///
    /// Resolves the container type config, obtains a Graph client, retrieves all consuming
    /// app registrations from the Graph API, and returns them as a <see cref="ConsumingTenantListDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Consumers returned (may be an empty list).
    ///   400 Bad Request — configId is missing, invalid, or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> ListConsumersAsync(
        string typeId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var validationResult = ValidateTypeIdAndConfigId(typeId, configId, "consumers.list", context);
        if (validationResult.Error is not null) return validationResult.Error;

        var configGuid = validationResult.ConfigGuid;

        var config = await graphService.ResolveConfigAsync(configGuid, ct);
        if (config is null)
        {
            return ConfigNotFound(configGuid, "consumers.list", context);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var consumers = await graphService.ListConsumingTenantsAsync(graphClient, typeId, ct);

            if (consumers is null)
            {
                logger.LogInformation(
                    "GET /api/spe/containertypes/{TypeId}/consumers — container type not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, configGuid, context.TraceIdentifier);
                return ContainerTypeNotFound(typeId, "consumers.list", context);
            }

            logger.LogInformation(
                "GET /api/spe/containertypes/{TypeId}/consumers — returned {Count} consumers for config {ConfigId}. TraceId: {TraceId}",
                typeId, consumers.Count, configGuid, context.TraceIdentifier);

            var items = consumers.Select(c => MapToDto(c)).ToList();
            return Results.Ok(new ConsumingTenantListDto { Items = items, Count = items.Count });
        }
        catch (ODataError odataError)
        {
            return GraphError(odataError, typeId, configGuid, "consumers.list", context, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnexpectedError(ex, typeId, configGuid, "consumers.list", context, logger);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/spe/containertypes/{typeId}/consumers?configId={id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new consuming application for the specified container type.
    ///
    /// Responses:
    ///   201 Created     — Consumer registered successfully; returns the new registration.
    ///   400 Bad Request — configId/typeId invalid, or request body validation failed.
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   409 Conflict    — The consuming app is already registered for this container type.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> RegisterConsumerAsync(
        string typeId,
        [FromQuery] string? configId,
        RegisterConsumingTenantRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var validationResult = ValidateTypeIdAndConfigId(typeId, configId, "consumers.register", context);
        if (validationResult.Error is not null) return validationResult.Error;

        var configGuid = validationResult.ConfigGuid;

        // Validate request body
        if (string.IsNullOrWhiteSpace(request.AppId))
        {
            return Results.Problem(
                detail: "The 'appId' field is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.consumers.app_id_required",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        var config = await graphService.ResolveConfigAsync(configGuid, ct);
        if (config is null)
        {
            return ConfigNotFound(configGuid, "consumers.register", context);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var registered = await graphService.RegisterConsumingTenantAsync(
                graphClient, typeId, request.AppId, request.TenantId,
                request.DelegatedPermissions, request.ApplicationPermissions, ct);

            if (registered is null)
            {
                return ContainerTypeNotFound(typeId, "consumers.register", context);
            }

            logger.LogInformation(
                "POST /api/spe/containertypes/{TypeId}/consumers — registered consuming app {AppId} for config {ConfigId}. TraceId: {TraceId}",
                typeId, request.AppId, configGuid, context.TraceIdentifier);

            var dto = MapToDto(registered) with { DisplayName = request.DisplayName ?? registered.DisplayName };
            return Results.Created(
                $"/api/spe/containertypes/{typeId}/consumers/{request.AppId}",
                dto);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == StatusCodes.Status409Conflict)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/consumers — conflict: app {AppId} already registered. Config {ConfigId}. TraceId: {TraceId}",
                typeId, request.AppId, configGuid, context.TraceIdentifier);
            return Results.Problem(
                detail: $"The application '{request.AppId}' is already registered as a consuming app for container type '{typeId}'.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.consumers.already_registered",
                    ["traceId"] = context.TraceIdentifier
                });
        }
        catch (ODataError odataError)
        {
            return GraphError(odataError, typeId, configGuid, "consumers.register", context, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnexpectedError(ex, typeId, configGuid, "consumers.register", context, logger);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates permissions for an existing consuming application registration.
    ///
    /// Responses:
    ///   200 OK          — Permissions updated; returns the updated registration.
    ///   400 Bad Request — configId/typeId/appId invalid.
    ///   404 Not Found   — Container type or consuming app not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> UpdateConsumerAsync(
        string typeId,
        string appId,
        [FromQuery] string? configId,
        UpdateConsumingTenantRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var validationResult = ValidateTypeIdAndConfigId(typeId, configId, "consumers.update", context);
        if (validationResult.Error is not null) return validationResult.Error;

        var configGuid = validationResult.ConfigGuid;

        if (string.IsNullOrWhiteSpace(appId))
        {
            return Results.Problem(
                detail: "The 'appId' path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.consumers.app_id_required",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        var config = await graphService.ResolveConfigAsync(configGuid, ct);
        if (config is null)
        {
            return ConfigNotFound(configGuid, "consumers.update", context);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var updated = await graphService.UpdateConsumingTenantAsync(
                graphClient, typeId, appId,
                request.DelegatedPermissions, request.ApplicationPermissions, ct);

            if (updated is null)
            {
                logger.LogInformation(
                    "PUT /api/spe/containertypes/{TypeId}/consumers/{AppId} — not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, appId, configGuid, context.TraceIdentifier);
                return Results.Problem(
                    detail: $"Container type '{typeId}' or consuming application '{appId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.containertypes.consumers.not_found",
                        ["traceId"] = context.TraceIdentifier
                    });
            }

            logger.LogInformation(
                "PUT /api/spe/containertypes/{TypeId}/consumers/{AppId} — updated consuming app for config {ConfigId}. TraceId: {TraceId}",
                typeId, appId, configGuid, context.TraceIdentifier);

            return Results.Ok(MapToDto(updated));
        }
        catch (ODataError odataError)
        {
            return GraphError(odataError, typeId, configGuid, "consumers.update", context, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnexpectedError(ex, typeId, configGuid, "consumers.update", context, logger);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/spe/containertypes/{typeId}/consumers/{appId}?configId={id}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a consuming application registration from the specified container type.
    ///
    /// Responses:
    ///   204 No Content  — Consumer removed successfully.
    ///   400 Bad Request — configId/typeId/appId invalid.
    ///   404 Not Found   — Container type or consuming app not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> RemoveConsumerAsync(
        string typeId,
        string appId,
        [FromQuery] string? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var validationResult = ValidateTypeIdAndConfigId(typeId, configId, "consumers.remove", context);
        if (validationResult.Error is not null) return validationResult.Error;

        var configGuid = validationResult.ConfigGuid;

        if (string.IsNullOrWhiteSpace(appId))
        {
            return Results.Problem(
                detail: "The 'appId' path parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.consumers.app_id_required",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        var config = await graphService.ResolveConfigAsync(configGuid, ct);
        if (config is null)
        {
            return ConfigNotFound(configGuid, "consumers.remove", context);
        }

        try
        {
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);
            var removed = await graphService.RemoveConsumingTenantAsync(graphClient, typeId, appId, ct);

            if (!removed)
            {
                logger.LogInformation(
                    "DELETE /api/spe/containertypes/{TypeId}/consumers/{AppId} — not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, appId, configGuid, context.TraceIdentifier);
                return Results.Problem(
                    detail: $"Container type '{typeId}' or consuming application '{appId}' was not found.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.containertypes.consumers.not_found",
                        ["traceId"] = context.TraceIdentifier
                    });
            }

            logger.LogInformation(
                "DELETE /api/spe/containertypes/{TypeId}/consumers/{AppId} — removed consuming app for config {ConfigId}. TraceId: {TraceId}",
                typeId, appId, configGuid, context.TraceIdentifier);

            return Results.NoContent();
        }
        catch (ODataError odataError)
        {
            return GraphError(odataError, typeId, configGuid, "consumers.remove", context, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnexpectedError(ex, typeId, configGuid, "consumers.remove", context, logger);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Result of typeId + configId validation.</summary>
    private readonly record struct ValidationResult(IResult? Error, Guid ConfigGuid);

    /// <summary>Validates the typeId path parameter and configId query parameter.</summary>
    private static ValidationResult ValidateTypeIdAndConfigId(
        string typeId,
        string? configId,
        string errorCodePrefix,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return new ValidationResult(
                Results.Problem(
                    detail: "The 'typeId' path parameter is required.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.type_id_required",
                        ["traceId"] = context.TraceIdentifier
                    }),
                Guid.Empty);
        }

        if (string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out var configGuid))
        {
            return new ValidationResult(
                Results.Problem(
                    detail: "The 'configId' query parameter is required and must be a valid GUID.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.config_id_required",
                        ["traceId"] = context.TraceIdentifier
                    }),
                Guid.Empty);
        }

        return new ValidationResult(null, configGuid);
    }

    /// <summary>Returns a 400 config-not-found ProblemDetails result.</summary>
    private static IResult ConfigNotFound(Guid configGuid, string errorCodePrefix, HttpContext context) =>
        Results.Problem(
            detail: $"Container type config '{configGuid}' was not found. Verify the configId is correct.",
            statusCode: StatusCodes.Status400BadRequest,
            title: "Config Not Found",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.config_not_found",
                ["traceId"] = context.TraceIdentifier
            });

    /// <summary>Returns a 404 container-type-not-found ProblemDetails result.</summary>
    private static IResult ContainerTypeNotFound(string typeId, string errorCodePrefix, HttpContext context) =>
        Results.Problem(
            detail: $"Container type '{typeId}' was not found.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.type_not_found",
                ["traceId"] = context.TraceIdentifier
            });

    /// <summary>Returns a 500 Graph API error ProblemDetails result and logs the error.</summary>
    private static IResult GraphError(
        ODataError odataError,
        string typeId,
        Guid configGuid,
        string errorCodePrefix,
        HttpContext context,
        ILogger<Program> logger)
    {
        logger.LogError(
            odataError,
            "Graph API error for container type {TypeId}, config {ConfigId}. Status: {Status}. ErrorCode: {ErrorCode}. TraceId: {TraceId}",
            typeId, configGuid, odataError.ResponseStatusCode, errorCodePrefix, context.TraceIdentifier);

        return Results.Problem(
            detail: "Failed to complete the operation via the Graph API. Check the app registration credentials in the config.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Graph API Error",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.graph_error",
                ["traceId"] = context.TraceIdentifier
            });
    }

    /// <summary>Returns a 500 unexpected error ProblemDetails result and logs the exception.</summary>
    private static IResult UnexpectedError(
        Exception ex,
        string typeId,
        Guid configGuid,
        string errorCodePrefix,
        HttpContext context,
        ILogger<Program> logger)
    {
        logger.LogError(
            ex,
            "Unexpected error for container type {TypeId}, config {ConfigId}. ErrorCode: {ErrorCode}. TraceId: {TraceId}",
            typeId, configGuid, errorCodePrefix, context.TraceIdentifier);

        return Results.Problem(
            detail: "An unexpected error occurred while processing the request.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = $"spe.containertypes.{errorCodePrefix}.unexpected_error",
                ["traceId"] = context.TraceIdentifier
            });
    }

    /// <summary>Maps a Graph service domain record to the API DTO (ADR-007: no Graph SDK types above service facade).</summary>
    private static ConsumingTenantDto MapToDto(SpeAdminGraphService.SpeConsumingTenant consumer) =>
        new()
        {
            AppId = consumer.AppId,
            DisplayName = consumer.DisplayName,
            TenantId = consumer.TenantId,
            DelegatedPermissions = consumer.DelegatedPermissions,
            ApplicationPermissions = consumer.ApplicationPermissions
        };
}
