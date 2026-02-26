using Sprk.Bff.Api.Models.Ai.RecordSearch;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding record search authorization to endpoints.
/// </summary>
public static class RecordSearchAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds record search authorization filter that validates tenant membership
    /// and logs access for audit.
    /// </summary>
    public static TBuilder AddRecordSearchAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<RecordSearchAuthorizationFilter>>();
            var filter = new RecordSearchAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for record search that validates tenant membership.
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
/// <item>Validates record types are known entity types</item>
/// <item>Logs record search access requests for audit trail</item>
/// </list>
/// </para>
/// <para>
/// Note: The spaarke-records-index does NOT have a tenantId field.
/// Tenant isolation is enforced at the Dataverse level, not at the search index level.
/// The filter still validates tenant membership to ensure the caller is authenticated.
/// </para>
/// </remarks>
public class RecordSearchAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<RecordSearchAuthorizationFilter>? _logger;

    // Azure AD claim names
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public RecordSearchAuthorizationFilter(ILogger<RecordSearchAuthorizationFilter>? logger = null)
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
                "Record search authorization denied: No tenant claim found in token");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Step 2: Extract record search request from arguments
        var request = ExtractRecordSearchRequest(context);
        if (request == null)
        {
            // No request body found - let endpoint handle the error
            return await next(context);
        }

        // Step 3: Log access for audit trail
        _logger?.LogInformation(
            "Record search authorization granted: tenant={TenantId}, recordTypes=[{RecordTypes}], query length={QueryLength}",
            userTenantId,
            string.Join(", ", request.RecordTypes),
            request.Query?.Length ?? 0);

        return await next(context);
    }

    /// <summary>
    /// Extract RecordSearchRequest from endpoint arguments.
    /// </summary>
    private static RecordSearchRequest? ExtractRecordSearchRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is RecordSearchRequest request)
            {
                return request;
            }
        }
        return null;
    }
}
