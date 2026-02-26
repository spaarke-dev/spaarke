using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding semantic search authorization to endpoints.
/// </summary>
public static class SemanticSearchAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds semantic search authorization filter that validates scope-based access.
    /// Validates tenant membership and logs scope-based access for audit.
    /// </summary>
    public static TBuilder AddSemanticSearchAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<SemanticSearchAuthorizationFilter>>();
            var filter = new SemanticSearchAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for semantic search that validates scope-based access.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Follows ADR-016: AI Security - Ensure tenant data isolation.
/// </para>
/// <para>
/// Authorization rules:
/// <list type="bullet">
/// <item>Validates user's tenant membership via Azure AD 'tid' claim</item>
/// <item>Logs scope-based access requests for audit trail</item>
/// <item>Tenant isolation enforced at query time via SearchFilterBuilder</item>
/// <item>scope=all allowed (R3) — tenant isolation via tenantId filter in SearchFilterBuilder</item>
/// </list>
/// </para>
/// <para>
/// Future enhancements:
/// <list type="bullet">
/// <item>Entity-level authorization (validate user has read access to parent entity)</item>
/// <item>Document-level authorization (validate user has access to specific documents)</item>
/// </list>
/// </para>
/// </remarks>
public class SemanticSearchAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<SemanticSearchAuthorizationFilter>? _logger;

    // Azure AD claim names
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public SemanticSearchAuthorizationFilter(ILogger<SemanticSearchAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Step 1: Extract and validate tenant ID from Azure AD claims
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning(
                "Semantic search authorization denied: No tenant claim found in token");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Step 2: Extract search request from arguments
        var request = ExtractSearchRequest(context);
        if (request == null)
        {
            // No request body found - let endpoint handle the error
            return await next(context);
        }

        // Step 3: Validate scope and log for audit
        var authResult = ValidateScopeAuthorization(request, userTenantId);

        if (!authResult.Authorized)
        {
            _logger?.LogWarning(
                "Semantic search authorization denied for tenant {TenantId}: {Reason}",
                userTenantId, authResult.Reason);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: authResult.Reason);
        }

        // Log successful authorization for audit trail
        LogAuthorizationGranted(request, userTenantId);

        return await next(context);
    }

    /// <summary>
    /// Extract SemanticSearchRequest from endpoint arguments.
    /// </summary>
    private static SemanticSearchRequest? ExtractSearchRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is SemanticSearchRequest request)
            {
                return request;
            }
        }
        return null;
    }

    /// <summary>
    /// Validate scope-based authorization.
    /// All scopes (entity, documentIds, all) are permitted; tenant isolation enforced at query time.
    /// </summary>
    private AuthorizationResult ValidateScopeAuthorization(
        SemanticSearchRequest request,
        string userTenantId)
    {
        var scope = request.Scope?.ToLowerInvariant();

        switch (scope)
        {
            case SearchScope.Entity:
                // R1: Allow entity scope - tenant isolation enforced by SearchFilterBuilder
                // Future: Add entity-level authorization via AuthorizationService
                return new AuthorizationResult(true, null);

            case SearchScope.DocumentIds:
                // R1: Allow documentIds scope - tenant isolation enforced by SearchFilterBuilder
                // Future: Add document-level authorization validation
                return new AuthorizationResult(true, null);

            case SearchScope.All:
                // R3: scope=all is now supported for system-wide document search
                // Tenant isolation enforced by SearchFilterBuilder (tenantId filter applied)
                return new AuthorizationResult(true, null);

            default:
                // Empty or unknown scope - let endpoint handle validation
                return new AuthorizationResult(true, null);
        }
    }

    /// <summary>
    /// Log authorization granted for audit trail.
    /// </summary>
    private void LogAuthorizationGranted(SemanticSearchRequest request, string tenantId)
    {
        var scope = request.Scope?.ToLowerInvariant();

        switch (scope)
        {
            case SearchScope.Entity:
                _logger?.LogInformation(
                    "Semantic search authorization granted: tenant={TenantId}, scope=entity, entityType={EntityType}, entityId={EntityId}",
                    tenantId, request.EntityType, request.EntityId);
                break;

            case SearchScope.DocumentIds:
                _logger?.LogInformation(
                    "Semantic search authorization granted: tenant={TenantId}, scope=documentIds, count={DocumentCount}",
                    tenantId, request.DocumentIds?.Count ?? 0);
                break;

            case SearchScope.All:
                _logger?.LogInformation(
                    "Semantic search authorization granted: tenant={TenantId}, scope=all (system-wide)",
                    tenantId);
                break;

            default:
                _logger?.LogDebug(
                    "Semantic search authorization granted: tenant={TenantId}, scope={Scope}",
                    tenantId, scope ?? "none");
                break;
        }
    }

    /// <summary>
    /// Result of authorization check.
    /// </summary>
    private record AuthorizationResult(bool Authorized, string? Reason);
}
