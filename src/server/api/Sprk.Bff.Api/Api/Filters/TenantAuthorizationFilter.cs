using System.Security.Claims;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding tenant authorization to endpoints.
/// </summary>
public static class TenantAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds tenant authorization filter that validates the user belongs to the requested tenant.
    /// Extracts tenantId from request body (RagSearchRequest, KnowledgeDocument, etc.) or query parameters.
    /// </summary>
    public static TBuilder AddTenantAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<TenantAuthorizationFilter>>();
            var filter = new TenantAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user's tenant membership.
/// Prevents cross-tenant data access in RAG and other multi-tenant endpoints.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Follows ADR-016: AI Security - Ensure tenant data isolation.
///
/// Security requirement:
/// - User's Azure AD 'tid' (tenant ID) claim must match the tenantId in the request
/// - Prevents authenticated users from accessing other tenants' data
/// </remarks>
public class TenantAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<TenantAuthorizationFilter>? _logger;

    // Azure AD claim names
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public TenantAuthorizationFilter(ILogger<TenantAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract user's tenant ID from Azure AD claims
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning(
                "Tenant authorization denied: No tenant claim found in token. " +
                "Expected 'tid' or 'http://schemas.microsoft.com/identity/claims/tenantid' claim.");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Extract requested tenant ID from request
        var requestedTenantId = ExtractTenantId(context, httpContext);

        if (string.IsNullOrEmpty(requestedTenantId))
        {
            // If no tenant specified in request, pass through - service should handle defaults
            _logger?.LogDebug("No tenantId found in request, allowing service to apply defaults");
            return await next(context);
        }

        // Validate tenant match
        if (!string.Equals(userTenantId, requestedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Tenant authorization denied: User tenant {UserTenantId} does not match requested tenant {RequestedTenantId}",
                userTenantId, requestedTenantId);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have access to the requested tenant's data");
        }

        _logger?.LogDebug(
            "Tenant authorization granted: User tenant {TenantId} matches request",
            userTenantId);

        return await next(context);
    }

    /// <summary>
    /// Extract tenant ID from request arguments (body) or query parameters.
    /// </summary>
    private static string? ExtractTenantId(EndpointFilterInvocationContext context, HttpContext httpContext)
    {
        // Check request body arguments for tenantId
        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                // RAG search request
                case RagSearchRequest request when !string.IsNullOrEmpty(request.Options?.TenantId):
                    return request.Options.TenantId;

                // Knowledge document (index operations)
                case KnowledgeDocument document when !string.IsNullOrEmpty(document.TenantId):
                    return document.TenantId;

                // Batch of knowledge documents
                case IEnumerable<KnowledgeDocument> documents:
                    var firstTenantId = documents.FirstOrDefault()?.TenantId;
                    if (!string.IsNullOrEmpty(firstTenantId))
                        return firstTenantId;
                    break;

                // Embedding request doesn't have tenant - no isolation needed
                case EmbeddingRequest:
                    return null;
            }
        }

        // Check query parameters for tenantId (used in DELETE operations)
        if (httpContext.Request.Query.TryGetValue("tenantId", out var tenantIdQuery))
        {
            return tenantIdQuery.FirstOrDefault();
        }

        return null;
    }
}
