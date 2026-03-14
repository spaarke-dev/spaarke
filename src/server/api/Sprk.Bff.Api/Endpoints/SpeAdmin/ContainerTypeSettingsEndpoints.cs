using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;

namespace Sprk.Bff.Api.Endpoints.SpeAdmin;

/// <summary>
/// Endpoint for updating SharePoint Embedded container type settings via the Graph API.
///
/// Routes (all under the /api/spe group from <see cref="SpeAdminEndpoints"/>):
///   PUT /api/spe/containertypes/{typeId}/settings?configId={id}
///
/// The configId query parameter identifies the sprk_specontainertypeconfig Dataverse record whose
/// app registration credentials are used to authenticate with Graph API. Administrators update
/// these settings to enforce organizational policies (sharing, versioning, storage) across all
/// containers created from the specified container type.
///
/// Authorization: Inherited from SpeAdminEndpoints route group (RequireAuthorization + SpeAdminAuthorizationFilter).
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — no controllers; MapGroup for route organization.
/// ADR-007: No Graph SDK types in public API surface — endpoint accepts and returns domain records only.
/// ADR-008: Authorization inherited from parent route group (no global middleware).
/// ADR-019: All errors return ProblemDetails (RFC 7807).
/// </remarks>
public static class ContainerTypeSettingsEndpoints
{
    /// <summary>
    /// Registers the container type settings update endpoint on the provided route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/> with the /api/spe group.
    /// </summary>
    /// <param name="group">The /api/spe route group to register endpoints on.</param>
    public static RouteGroupBuilder MapContainerTypeSettingsEndpoints(this RouteGroupBuilder group)
    {
        // PUT /api/spe/containertypes/{typeId}/settings?configId={id}
        group.MapPut("/containertypes/{typeId}/settings", UpdateContainerTypeSettingsAsync)
            .WithName("SpeUpdateContainerTypeSettings")
            .WithSummary("Update settings for an SPE container type")
            .WithDescription(
                "Updates container type settings (sharing capability, versioning policy, storage limits) " +
                "via the Graph API. Only fields supplied in the request body are updated — null fields " +
                "are left unchanged (merge-patch semantics). Returns the updated container type resource. " +
                "Returns 400 when the configId is missing or invalid, or when the sharing capability " +
                "value is not one of: disabled, view, edit, full. Returns 404 when the container type " +
                "does not exist in Graph API.")
            .Produces<ContainerTypeSettingsResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// PUT /api/spe/containertypes/{typeId}/settings?configId={id}
    ///
    /// Resolves the container type config, obtains a Graph client authenticated as the config's app
    /// registration, validates the request, and PATCHes the container type settings in Graph.
    ///
    /// Responses:
    ///   200 OK          — Settings updated successfully; updated resource returned.
    ///   400 Bad Request — configId is missing/invalid, or sharingCapability value is not allowed.
    ///   401 Unauthorized — No authenticated user (handled by RequireAuthorization).
    ///   403 Forbidden   — User is not an admin (handled by SpeAdminAuthorizationFilter).
    ///   404 Not Found   — Container type with the given typeId was not found in Graph API.
    ///   500 Internal    — Unexpected error from Graph API or Key Vault.
    /// </summary>
    private static async Task<IResult> UpdateContainerTypeSettingsAsync(
        string typeId,
        [Microsoft.AspNetCore.Mvc.FromQuery] Guid? configId,
        UpdateContainerTypeSettingsRequest request,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Validate required configId query parameter
        if (configId is null || configId == Guid.Empty)
        {
            logger.LogWarning(
                "PUT /api/spe/containertypes/{TypeId}/settings — missing or empty configId", typeId);
            return Results.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.config_id_required"
                });
        }

        // Validate sharingCapability when provided
        if (!string.IsNullOrWhiteSpace(request.SharingCapability) &&
            !SpeAdminGraphService.ValidSharingCapabilities.Contains(request.SharingCapability))
        {
            logger.LogWarning(
                "PUT /api/spe/containertypes/{TypeId}/settings — invalid sharingCapability '{Value}'. " +
                "TraceId: {TraceId}",
                typeId, request.SharingCapability, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Invalid sharing capability '{request.SharingCapability}'. " +
                        "Allowed values are: disabled, view, edit, full.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Sharing Capability",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.invalid_sharing_capability"
                });
        }

        // Validate majorVersionLimit when provided
        if (request.MajorVersionLimit.HasValue && request.MajorVersionLimit.Value <= 0)
        {
            logger.LogWarning(
                "PUT /api/spe/containertypes/{TypeId}/settings — invalid majorVersionLimit {Value}. " +
                "TraceId: {TraceId}",
                typeId, request.MajorVersionLimit.Value, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Invalid majorVersionLimit '{request.MajorVersionLimit.Value}'. " +
                        "Value must be a positive integer.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Version Limit",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.invalid_major_version_limit"
                });
        }

        // Resolve the container type config from Dataverse
        var config = await graphService.ResolveConfigAsync(configId.Value, ct);
        if (config is null)
        {
            logger.LogWarning(
                "PUT /api/spe/containertypes/{TypeId}/settings — config {ConfigId} not found. TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);
            return Results.Problem(
                detail: $"Container type config '{configId}' was not found. Verify the configId is correct.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Config Not Found",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.config_not_found"
                });
        }

        try
        {
            // Get the Graph client authenticated for this config's app registration
            var graphClient = await graphService.GetClientForConfigAsync(config, ct);

            // PATCH container type settings via Graph API
            var result = await graphService.UpdateContainerTypeSettingsAsync(
                graphClient,
                typeId,
                request.SharingCapability,
                request.IsVersioningEnabled,
                request.MajorVersionLimit,
                request.StorageUsedInBytes,
                ct);

            if (result is null)
            {
                logger.LogInformation(
                    "PUT /api/spe/containertypes/{TypeId}/settings — container type not found in Graph. " +
                    "TraceId: {TraceId}",
                    typeId, context.TraceIdentifier);
                return Results.NotFound();
            }

            logger.LogInformation(
                "PUT /api/spe/containertypes/{TypeId}/settings — settings updated successfully. " +
                "TraceId: {TraceId}",
                typeId, context.TraceIdentifier);

            // Map domain record to response DTO
            return Results.Ok(new ContainerTypeSettingsResponseDto
            {
                Id = result.Id,
                DisplayName = result.DisplayName,
                BillingClassification = result.BillingClassification,
                CreatedDateTime = result.CreatedDateTime
            });
        }
        catch (ODataError odataError)
        {
            logger.LogError(
                odataError,
                "Graph API error updating container type {TypeId} settings for config {ConfigId}. " +
                "Status: {Status}. TraceId: {TraceId}",
                typeId, configId, odataError.ResponseStatusCode, context.TraceIdentifier);

            return Results.Problem(
                detail: "Failed to update container type settings via the Graph API. " +
                        "Check the app registration credentials in the config.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Graph API Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.graph_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error updating container type {TypeId} settings for config {ConfigId}. " +
                "TraceId: {TraceId}",
                typeId, configId, context.TraceIdentifier);

            return Results.Problem(
                detail: "An unexpected error occurred while updating container type settings.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.containertypes.settings.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }
}
