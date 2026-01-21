using Spaarke.Core.Auth;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding EntityAccessFilter to Office save endpoints.
/// </summary>
public static class EntityAccessFilterExtensions
{
    /// <summary>
    /// Adds entity access filter that validates user has access to referenced entities in save requests.
    /// Returns 403 Forbidden if user lacks access to the target entity.
    /// Returns 404 Not Found if entity does not exist.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This filter should be applied after OfficeAuthFilter to ensure userId is available.
    /// Extracts target entity from SaveRequest.TargetEntity.
    /// </remarks>
    public static TBuilder AddEntityAccessFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<EntityAccessFilter>>();
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new EntityAccessFilter(authService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user has access to referenced entities in save requests.
/// Verifies the user can associate documents with the target account, contact, matter, project, or invoice.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// </para>
/// <para>
/// Entity access is determined by:
/// 1. User has read access to the entity (can view it)
/// 2. User has write/associate permission for document association
/// 3. Entity exists and is active
/// </para>
/// <para>
/// Supported entity types (from spec):
/// - account (Dataverse standard)
/// - contact (Dataverse standard)
/// - sprk_matter (Spaarke custom)
/// - sprk_project (Spaarke custom)
/// - sprk_invoice (Spaarke custom)
/// </para>
/// </remarks>
public class EntityAccessFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly ILogger<EntityAccessFilter>? _logger;

    // Operation constant for entity association
    private const string AssociateOperation = "entity.associate_document";

    public EntityAccessFilter(
        AuthorizationService authorizationService,
        ILogger<EntityAccessFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Get userId from HttpContext.Items (set by OfficeAuthFilter)
        var userId = httpContext.Items[OfficeAuthFilter.UserIdKey] as string;
        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "Entity access check failed: No userId in HttpContext.Items. " +
                "Ensure OfficeAuthFilter runs before EntityAccessFilter. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not established",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_003",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract target entity from request arguments
        var targetEntity = ExtractTargetEntity(context);
        if (targetEntity == null)
        {
            _logger?.LogDebug(
                "Entity access check: No target entity in request, validation will be handled by endpoint. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            // Per spec, association is required - but let the endpoint handle validation
            // This filter is for authorization only
            return await next(context);
        }

        // Validate entity type
        if (!IsValidEntityType(targetEntity.EntityType))
        {
            _logger?.LogWarning(
                "Entity access check failed: Invalid entity type '{EntityType}'. " +
                "CorrelationId: {CorrelationId}",
                targetEntity.EntityType, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: $"Invalid association entity type: {targetEntity.EntityType}",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_002",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Checking entity access for user {UserId} on {EntityType} {EntityId}. " +
            "CorrelationId: {CorrelationId}",
            userId, targetEntity.EntityType, targetEntity.EntityId, httpContext.TraceIdentifier);

        // Build resource ID combining entity type and ID for authorization context
        var resourceId = $"{targetEntity.EntityType}:{targetEntity.EntityId}";

        var authContext = new AuthorizationContext
        {
            UserId = userId,
            ResourceId = resourceId,
            Operation = AssociateOperation,
            CorrelationId = httpContext.TraceIdentifier
        };

        try
        {
            var result = await _authorizationService.AuthorizeAsync(authContext, httpContext.RequestAborted);

            if (!result.IsAllowed)
            {
                // Determine appropriate error based on reason
                var (statusCode, errorCode, detail) = MapAuthorizationDenial(result.ReasonCode);

                _logger?.LogWarning(
                    "Entity access denied: User {UserId} cannot associate documents with {EntityType} {EntityId}. " +
                    "Reason: {Reason}. CorrelationId: {CorrelationId}",
                    userId, targetEntity.EntityType, targetEntity.EntityId, result.ReasonCode, httpContext.TraceIdentifier);

                return Results.Problem(
                    statusCode: statusCode,
                    title: statusCode == 404 ? "Not Found" : "Forbidden",
                    detail: detail,
                    type: statusCode == 404
                        ? "https://tools.ietf.org/html/rfc7231#section-6.5.4"
                        : "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = errorCode,
                        ["reasonCode"] = result.ReasonCode,
                        ["entityType"] = targetEntity.EntityType,
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Entity access check failed for {EntityType} {EntityId}. " +
                "User: {UserId}. CorrelationId: {CorrelationId}",
                targetEntity.EntityType, targetEntity.EntityId, userId, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Entity access verified: User {UserId} can associate documents with {EntityType} {EntityId}. " +
            "CorrelationId: {CorrelationId}",
            userId, targetEntity.EntityType, targetEntity.EntityId, httpContext.TraceIdentifier);

        // Store entity info for downstream use
        httpContext.Items["Office.TargetEntityType"] = targetEntity.EntityType;
        httpContext.Items["Office.TargetEntityId"] = targetEntity.EntityId;

        return await next(context);
    }

    /// <summary>
    /// Extract target entity from request arguments.
    /// Supports SaveRequest with TargetEntity property.
    /// </summary>
    private static SaveEntityReference? ExtractTargetEntity(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is SaveRequest saveRequest && saveRequest.TargetEntity != null)
            {
                return saveRequest.TargetEntity;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates the entity type is a supported association target.
    /// </summary>
    private static bool IsValidEntityType(string entityType)
    {
        // Supported entity types per spec
        return entityType.ToLowerInvariant() switch
        {
            "account" => true,
            "contact" => true,
            "sprk_matter" => true,
            "matter" => true,  // Allow short name
            "sprk_project" => true,
            "project" => true,  // Allow short name
            "sprk_invoice" => true,
            "invoice" => true,  // Allow short name
            _ => false
        };
    }

    /// <summary>
    /// Maps authorization denial reason to appropriate HTTP status and error code.
    /// </summary>
    private static (int statusCode, string errorCode, string detail) MapAuthorizationDenial(string? reasonCode)
    {
        return reasonCode?.ToLowerInvariant() switch
        {
            "entity_not_found" or "resource_not_found" =>
                (404, "OFFICE_007", "Association target not found"),

            "inactive" or "deleted" =>
                (404, "OFFICE_007", "Association target is inactive or deleted"),

            "team_mismatch" or "tenant_mismatch" =>
                (403, "OFFICE_009", "Access denied to this entity"),

            "permission_denied" or "insufficient_role" =>
                (403, "OFFICE_009", "You do not have permission to associate documents with this entity"),

            _ =>
                (403, "OFFICE_009", "Access denied to association target")
        };
    }
}
