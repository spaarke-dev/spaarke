using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Endpoints for listing, retrieving, creating, and registering SharePoint Embedded container types.
///
/// Routes (all under the /api/spe group from <see cref="Api.SpeAdminEndpoints"/>):
///   GET  /api/spe/containertypes?configId={id}                      — list all container types
///   GET  /api/spe/containertypes/{typeId}?configId={id}             — get single container type by ID
///   POST /api/spe/containertypes?configId={id}                      — create a new container type
///   POST /api/spe/containertypes/{typeId}/register?configId={id}    — register container type (grant app permissions)
///
/// The configId query parameter identifies the sprk_specontainertypeconfig Dataverse record whose
/// app registration credentials are used to authenticate with Graph API and SharePoint REST API.
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoints return domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// SPE-053: Registration endpoint calls SharePoint REST API (not Graph API) for applicationPermissions.
/// </remarks>
public static class ContainerTypeEndpoints
{
    /// <summary>
    /// Valid billing classification values accepted by the Graph API and this endpoint.
    /// Defined as a static set for O(1) lookup during request validation.
    /// </summary>
    private static readonly HashSet<string> ValidBillingClassifications =
        new(StringComparer.OrdinalIgnoreCase) { "standard", "premium" };

    /// <summary>
    /// Registers the container type list, get-by-ID, and create endpoints on the provided route group.
    /// Called from <see cref="Api.SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static RouteGroupBuilder MapContainerTypeEndpoints(this RouteGroupBuilder group)
    {
        // GET /api/spe/containertypes?configId={id}
        group.MapGet("/containertypes", ListContainerTypesAsync)
            .WithName("SpeListContainerTypes")
            .WithSummary("List SPE container types for a container type config")
            .WithDescription(
                "Returns all SharePoint Embedded container types visible to the app registration " +
                "associated with the specified container type config. Requires a valid configId that " +
                "exists in the sprk_specontainertypeconfig Dataverse table.")
            .Produces<ContainerTypeListDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/containertypes/{typeId}?configId={id}
        group.MapGet("/containertypes/{typeId}", GetContainerTypeAsync)
            .WithName("SpeGetContainerType")
            .WithSummary("Get a single SPE container type by ID")
            .WithDescription(
                "Returns details for a specific SharePoint Embedded container type, authenticated using " +
                "the specified container type config. Returns 404 when the container type is not found " +
                "in Graph API.")
            .Produces<ContainerTypeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containertypes?configId={id}
        group.MapPost("/containertypes", CreateContainerTypeAsync)
            .WithName("SpeCreateContainerType")
            .WithSummary("Create a new SPE container type")
            .WithDescription(
                "Creates a new SharePoint Embedded container type via the Graph API. " +
                "The displayName is required; billingClassification defaults to 'standard' when omitted. " +
                "Writes an audit log entry on success.")
            .Produces<ContainerTypeDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/spe/containertypes/{typeId}/register?configId={id}
        group.MapPost("/containertypes/{typeId}/register", RegisterContainerTypeAsync)
            .WithName("SpeRegisterContainerType")
            .WithSummary("Register an SPE container type (grant app permissions)")
            .WithDescription(
                "Registers a SharePoint Embedded container type by granting the consuming application " +
                "the specified delegated and application permissions via the SharePoint REST API. " +
                "This is required before containers of the type can be created by the consuming app. " +
                "Writes an audit log entry on success.")
            .Produces<RegisterContainerTypeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/containertypes?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client authenticated as the config's app
    /// registration, lists all container types visible to that app, and returns them as a
    /// <see cref="ContainerTypeListDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Container types returned (may be an empty list).
    ///   400 Bad Request — configId is missing or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> ListContainerTypesAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning("GET /api/spe/containertypes — missing or empty configId");
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_id_required" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "GET /api/spe/containertypes — config {ConfigId} not found in Dataverse. TraceId: {TraceId}",
                configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_not_found" });
        }

        try
        {
            // Container types are readable ONLY with a delegated token — app-only returns 403
            // accessDenied on v1.0 and beta alike (verified live, task 010). Use the BFF's existing
            // OBO exchange, the same one SPE file operations already run on.
            var containerTypes = await graphService.ListContainerTypesForUserAsync(context, ct);

            logger.LogInformation(
                "GET /api/spe/containertypes — returned {Count} container types for config {ConfigId}. TraceId: {TraceId}",
                containerTypes.Count, configId, context.TraceIdentifier);

            // Map domain records to API DTOs
            var items = containerTypes
                .Select(ct2 => new ContainerTypeDto
                {
                    Id = ct2.Id,
                    DisplayName = ct2.DisplayName,
                    Description = ct2.Description,
                    BillingClassification = ct2.BillingClassification,
                    CreatedDateTime = ct2.CreatedDateTime,
                    // Both are nullable all the way to the client. An absent owning app must render
                    // as unknown, and an absent expiry must not read as "never expires" (task 030).
                    OwningAppId = ct2.OwningAppId,
                    ExpiryDateTime = ct2.ExpirationDateTime,
                    Settings = ContainerTypeSettingsDto.FromDomain(ct2.Settings)
                })
                .ToList();

            return Results.Ok(new ContainerTypeListDto
            {
                Items = items,
                Count = items.Count
            });
        }
        catch (SpaarkeStorageException sse) when (sse.StatusCode == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                sse,
                "Graph denied listing container types for config {ConfigId} — reporting the Entra " +
                "directory-role prerequisite. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return EntraRoleDeniedProblem(sse, "Could not list container types.", context.TraceIdentifier);
        }
        catch (SpaarkeStorageException sse)
        {
            logger.LogError(
                sse,
                "Graph API error listing container types for config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                configId, sse.StatusCode, context.TraceIdentifier);

            return sse.ToProblemDetails(
                summary: "Could not retrieve container types.",
                errorCode: "spe.containertypes.graph_error",
                statusCode: StatusCodes.Status500InternalServerError,
                traceId: context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error listing container types for config {ConfigId}. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving container types.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// GET /api/spe/containertypes/{typeId}?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client, retrieves a single container type
    /// by its Graph ID, and returns it as a <see cref="ContainerTypeDto"/>.
    ///
    /// Responses:
    ///   200 OK          — Container type returned.
    ///   400 Bad Request — configId is missing or does not exist in Dataverse.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> GetContainerTypeAsync(
        string typeId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning("GET /api/spe/containertypes/{TypeId} — missing or empty configId", typeId);
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_id_required" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "GET /api/spe/containertypes/{TypeId} — config {ConfigId} not found. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_not_found" });
        }

        try
        {
            // Retrieve the specific container type from Graph API
            var containerType = await graphService.GetContainerTypeForConfigAsync(config, typeId, ct);

            if (containerType is null)
            {
                logger.LogInformation(
                    "GET /api/spe/containertypes/{TypeId} — not found in Graph for config {ConfigId}. TraceId: {TraceId}",
                    typeId, configId, context.TraceIdentifier);
                return Results.NotFound();
            }

            logger.LogDebug(
                "GET /api/spe/containertypes/{TypeId} — returned container type '{DisplayName}'. TraceId: {TraceId}",
                typeId, containerType.DisplayName, context.TraceIdentifier);

            // Map domain record to API DTO
            return Results.Ok(new ContainerTypeDto
            {
                Id = containerType.Id,
                DisplayName = containerType.DisplayName,
                Description = containerType.Description,
                BillingClassification = containerType.BillingClassification,
                CreatedDateTime = containerType.CreatedDateTime,
                OwningAppId = containerType.OwningAppId,
                ExpiryDateTime = containerType.ExpirationDateTime,
                Settings = ContainerTypeSettingsDto.FromDomain(containerType.Settings)
            });
        }
        catch (SpaarkeStorageException sse) when (sse.StatusCode == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                sse,
                "Graph denied reading container type {TypeId} for config {ConfigId} — reporting the " +
                "Entra directory-role prerequisite. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);

            return EntraRoleDeniedProblem(
                sse, $"Could not open container type '{typeId}'.", context.TraceIdentifier);
        }
        catch (SpaarkeStorageException sse)
        {
            logger.LogError(
                sse,
                "Graph API error getting container type {TypeId} for config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                typeId, configId, sse.StatusCode, context.TraceIdentifier);

            return sse.ToProblemDetails(
                summary: $"Could not retrieve container type '{typeId}'.",
                errorCode: "spe.containertypes.graph_error",
                statusCode: StatusCodes.Status500InternalServerError,
                traceId: context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error getting container type {TypeId} for config {ConfigId}. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving the container type.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// POST /api/spe/containertypes?configId={id}
    ///
    /// Creates a new SharePoint Embedded container type via the Graph API authenticated as the
    /// app registration identified by the specified configId. On success, writes an audit log entry
    /// and returns the created container type as <see cref="ContainerTypeDto"/> with HTTP 201 Created.
    ///
    /// Validation:
    ///   - configId must be present and a valid non-empty GUID.
    ///   - config must exist in the sprk_specontainertypeconfig Dataverse table.
    ///   - request.displayName must not be null or whitespace.
    ///   - request.billingClassification, when provided, must be "standard" or "premium".
    ///
    /// Responses:
    ///   201 Created     — Container type created; body contains the new ContainerTypeDto.
    ///   400 Bad Request — configId invalid/missing, config not found, or validation failure.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> CreateContainerTypeAsync(
        [FromQuery] Guid? configId,
        [FromBody] CreateContainerTypeRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning("POST /api/spe/containertypes — missing or empty configId");
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_id_required" });
        }

        // Validate required displayName
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes — missing displayName. TraceId: {TraceId}",
                context.TraceIdentifier);
            return Results.Problem(
                detail: "The 'displayName' field is required and must not be empty.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.display_name_required" });
        }

        // Validate billingClassification when provided
        if (!string.IsNullOrWhiteSpace(request.BillingClassification) &&
            !ValidBillingClassifications.Contains(request.BillingClassification))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes — invalid billingClassification '{BillingClassification}'. TraceId: {TraceId}",
                request.BillingClassification, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Invalid billingClassification '{request.BillingClassification}'. Accepted values: standard, premium.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.invalid_billing_classification" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes — config {ConfigId} not found in Dataverse. TraceId: {TraceId}",
                configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.config_not_found" });
        }

        try
        {
            // Create the container type in SharePoint Embedded via Graph API
            var created = await graphService.CreateContainerTypeForConfigAsync(
                config,
                request.DisplayName,
                request.BillingClassification,
                ct);

            logger.LogInformation(
                "POST /api/spe/containertypes — created container type '{ContainerTypeId}' ('{DisplayName}') for configId {ConfigId}. TraceId: {TraceId}",
                created.Id, created.DisplayName, configId, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            _ = auditService.LogOperationAsync(
                operation: "CreateContainerType",
                category: "ContainerTypeCreated",
                targetResource: created.Id,
                responseStatus: StatusCodes.Status201Created,
                configId: configId.Value,
                cancellationToken: CancellationToken.None);

            // Map domain record to API DTO and return 201 Created
            var dto = new ContainerTypeDto
            {
                Id = created.Id,
                DisplayName = created.DisplayName,
                Description = created.Description,
                BillingClassification = created.BillingClassification,
                CreatedDateTime = created.CreatedDateTime,
                OwningAppId = created.OwningAppId,
                ExpiryDateTime = created.ExpirationDateTime,
                Settings = ContainerTypeSettingsDto.FromDomain(created.Settings)
            };

            return Results.Created($"/api/spe/containertypes/{created.Id}", dto);
        }
        catch (SpaarkeStorageException sse) when (sse.StatusCode == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                sse,
                "Graph denied creating a container type for config {ConfigId} — reporting the Entra " +
                "directory-role prerequisite. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return EntraRoleDeniedProblem(sse, "Could not create the container type.", context.TraceIdentifier);
        }
        catch (SpaarkeStorageException sse)
        {
            logger.LogError(
                sse,
                "Graph API error creating container type for config {ConfigId}. Status: {Status}. TraceId: {TraceId}",
                configId, sse.StatusCode, context.TraceIdentifier);

            return sse.ToProblemDetails(
                summary: "Could not create the container type.",
                errorCode: "spe.containertypes.graph_error",
                statusCode: StatusCodes.Status500InternalServerError,
                traceId: context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error creating container type for config {ConfigId}. TraceId: {TraceId}",
                configId, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while creating the container type.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// POST /api/spe/containertypes/{typeId}/register?configId={id}
    ///
    /// Registers a container type by granting the consuming application (identified by appId in the request body)
    /// the specified delegated and application permissions. Uses the SharePoint REST API — not Graph API.
    ///
    /// This is the critical step that enables the consuming app to create and manage containers of this type.
    /// Without registration, the container type exists but cannot be used.
    ///
    /// Validation:
    ///   - configId must be present and a valid non-empty GUID.
    ///   - config must exist in the sprk_specontainertypeconfig Dataverse table.
    ///   - request.appId must not be null or whitespace and must be a valid GUID.
    ///   - request.sharePointAdminUrl must not be null or whitespace and must be a valid HTTPS URL.
    ///   - At least one permission must be supplied (delegatedPermissions or applicationPermissions).
    ///   - All permission names must be valid values from <see cref="ContainerTypePermissions.ValidPermissions"/>.
    ///
    /// Responses:
    ///   200 OK          — Registration successful; body contains <see cref="RegisterContainerTypeResponse"/>.
    ///   400 Bad Request — configId invalid/missing, config not found, or validation failure.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   500 Internal    — Unexpected error from SharePoint REST API or Key Vault.
    /// </summary>
    private static async Task<IResult> RegisterContainerTypeAsync(
        string typeId,
        [FromQuery] Guid? configId,
        [FromBody] RegisterContainerTypeRequest request,
        SpeAdminGraphService graphService,
        SpeAuditService auditService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — missing or empty configId",
                typeId);
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.config_id_required" });
        }

        // Validate required appId
        if (string.IsNullOrWhiteSpace(request.AppId))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — missing appId. TraceId: {TraceId}",
                typeId, context.TraceIdentifier);
            return Results.Problem(
                detail: "The 'appId' field is required and must not be empty.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.app_id_required" });
        }

        // Validate appId is a valid GUID
        if (!Guid.TryParse(request.AppId, out _))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — invalid appId '{AppId}' (not a GUID). TraceId: {TraceId}",
                typeId, request.AppId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"The 'appId' value '{request.AppId}' is not a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.app_id_invalid" });
        }

        // Validate required sharePointAdminUrl
        if (string.IsNullOrWhiteSpace(request.SharePointAdminUrl))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — missing sharePointAdminUrl. TraceId: {TraceId}",
                typeId, context.TraceIdentifier);
            return Results.Problem(
                detail: "The 'sharePointAdminUrl' field is required and must be a valid HTTPS URL " +
                        "(e.g., https://contoso-admin.sharepoint.com).",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.sharepoint_url_required" });
        }

        // Validate sharePointAdminUrl is an absolute HTTPS URI
        if (!Uri.TryCreate(request.SharePointAdminUrl, UriKind.Absolute, out var spAdminUri) ||
            !string.Equals(spAdminUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — invalid sharePointAdminUrl '{Url}'. TraceId: {TraceId}",
                typeId, request.SharePointAdminUrl, context.TraceIdentifier);
            return Results.Problem(
                detail: $"The 'sharePointAdminUrl' value '{request.SharePointAdminUrl}' is not a valid HTTPS URL.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.sharepoint_url_invalid" });
        }

        // Validate at least one permission is supplied
        var hasAnyPermission =
            (request.DelegatedPermissions?.Count ?? 0) > 0 ||
            (request.ApplicationPermissions?.Count ?? 0) > 0;

        if (!hasAnyPermission)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — no permissions supplied. TraceId: {TraceId}",
                typeId, context.TraceIdentifier);
            return Results.Problem(
                detail: "At least one permission must be supplied in 'delegatedPermissions' or 'applicationPermissions'.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.permissions_required" });
        }

        // Validate all permission names are valid
        var allPermissions = (request.DelegatedPermissions ?? [])
            .Concat(request.ApplicationPermissions ?? []);
        var invalidPermissions = allPermissions
            .Where(p => !ContainerTypePermissions.ValidPermissions.Contains(p))
            .ToList();

        if (invalidPermissions.Count > 0)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — invalid permission names: [{Invalid}]. TraceId: {TraceId}",
                typeId, string.Join(", ", invalidPermissions), context.TraceIdentifier);
            return Results.Problem(
                detail: $"Invalid permission names: {string.Join(", ", invalidPermissions)}. " +
                        $"Valid values: {string.Join(", ", ContainerTypePermissions.ValidPermissions)}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.invalid_permissions" });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "POST /api/spe/containertypes/{TypeId}/register — config {ConfigId} not found in Dataverse. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "spe.containertypes.register.config_not_found" });
        }

        try
        {
            // Register the container type via SharePoint REST API
            var result = await graphService.RegisterContainerTypeAsync(
                config,
                typeId,
                request.SharePointAdminUrl,
                request.AppId,
                request.DelegatedPermissions ?? [],
                request.ApplicationPermissions ?? [],
                ct);

            logger.LogInformation(
                "POST /api/spe/containertypes/{TypeId}/register — registered for appId '{AppId}' " +
                "via configId {ConfigId}. TraceId: {TraceId}",
                typeId, request.AppId, configId, context.TraceIdentifier);

            // Audit log — fire-and-forget; audit failure must never block the primary response.
            _ = auditService.LogOperationAsync(
                operation: "RegisterContainerType",
                category: "ContainerTypeRegistration",
                targetResource: $"{typeId}::{request.AppId}",
                responseStatus: StatusCodes.Status200OK,
                configId: configId.Value,
                cancellationToken: CancellationToken.None);

            return Results.Ok(new RegisterContainerTypeResponse
            {
                ContainerTypeId = result.ContainerTypeId,
                AppId = result.AppId,
                DelegatedPermissions = result.DelegatedPermissions,
                ApplicationPermissions = result.ApplicationPermissions
            });
        }
        catch (HttpRequestException httpEx)
        {
            logger.LogError(
                httpEx,
                "SharePoint REST API error registering container type {TypeId} for app {AppId}. " +
                "Status: {StatusCode}. TraceId: {TraceId}",
                typeId, request.AppId, (int?)httpEx.StatusCode, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain(
                    $"Could not register container type '{typeId}' via the SharePoint REST API"
                    + (httpEx.StatusCode is { } sc ? $" (HTTP {(int)sc})." : "."),
                    httpEx),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "SharePoint REST API Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.register.sharepoint_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error registering container type {TypeId} for config {ConfigId}. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);

            return Results.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while registering the container type.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.register.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// Stable code for "Graph refused a container-type operation, and the Entra directory role is the
    /// prerequisite that grants it". The client keys its message off this.
    /// </summary>
    internal const string EntraRoleRequiredErrorCode = "spe.containertypes.entra_role_required";

    /// <summary>
    /// Translate a Graph 403 on a container-type operation into a response that names the Entra
    /// directory-role prerequisite — the second of the two authorization layers described on
    /// <see cref="Filters.SpeAdminAuthorizationFilter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this lives at the Graph boundary rather than in the endpoint filter.</b> The filter
    /// cannot see Entra directory roles: <c>SDAP-BFF-SPE-API</c> leaves
    /// <c>groupMembershipClaims</c> unset, so no <c>wids</c> claim is emitted — verified 2026-08-22
    /// against a token issued to a confirmed holder of the SharePoint Embedded Administrator role.
    /// A Graph 403, by contrast, IS authoritative: Graph applied the real rule and refused.
    /// </para>
    /// <para>
    /// <b>What this message may and may not say.</b> It names the role and what the role enables. It
    /// does <b>NOT</b> assert that the caller lacks the role — Graph reports that the request was
    /// denied, not why, and 403 has other causes (an unregistered container type, a consent gap, a
    /// config pointing at another tenant). Asserting "you lack role X" from this signal would be a
    /// guess, and a wrong one told to a genuine role holder is precisely the misleading-error defect
    /// this project removes (spec FR-B03). The wording therefore states the prerequisite and points at
    /// the Graph diagnostics for the alternative causes.
    /// </para>
    /// <para>
    /// Before this existed, all four container-type operations passed a hardcoded
    /// <see cref="StatusCodes.Status500InternalServerError"/>, so a permission denial reached the
    /// admin as "Internal Server Error" — indistinguishable from a server bug.
    /// </para>
    /// </remarks>
    /// <param name="sse">The translated Graph failure. Callers MUST filter on status 403.</param>
    /// <param name="attempted">What was being attempted, stated without asserting why it failed.</param>
    /// <param name="traceId">Correlation id for support.</param>
    // internal rather than private so the wording contract above can be asserted directly. It is pure
    // translation — no I/O, no mocks — so the test is ADR-038 §2 path #6 (domain logic), not scaffolding.
    internal static IResult EntraRoleDeniedProblem(
        SpaarkeStorageException sse,
        string attempted,
        string traceId)
    {
        return sse.ToProblemDetails(
            summary:
                $"{attempted} Microsoft Graph refused the request. Container-type administration " +
                "requires the \"SharePoint Embedded Administrator\" or \"Global Administrator\" role in " +
                "Microsoft Entra, which grants tenant-wide visibility and management of container " +
                "types. That role is granted by a Microsoft Entra administrator and is separate from " +
                "your Spaarke administrator permission — Spaarke cannot see whether you hold it. If you " +
                "do hold it, this denial has another cause and the Graph details below identify it.",
            errorCode: EntraRoleRequiredErrorCode,
            statusCode: StatusCodes.Status403Forbidden,
            traceId: traceId,
            title: "Additional permission required");
    }
}
