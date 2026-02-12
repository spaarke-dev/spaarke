using System.Security.Claims;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding FinanceAuthorizationFilter to endpoints.
/// Follows ADR-008 endpoint filter pattern (mirrors DocumentAuthorizationFilter).
/// </summary>
public static class FinanceAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds finance authorization to an endpoint with the specified operation.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">The operation being authorized (e.g., "finance.read", "finance.confirm", "finance.reject").</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddFinanceAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        string operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new FinanceAuthorizationFilter(authService, operation);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter that authorizes finance operations by verifying the authenticated user
/// has access to the associated matter/project for the requested resource.
/// Follows ADR-008: endpoint filters for resource authorization (not global middleware).
/// Returns ProblemDetails (ADR-019) for 403 Forbidden responses.
/// </summary>
public class FinanceAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;

    public FinanceAuthorizationFilter(AuthorizationService authorizationService, string operation)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract user ID from claims
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Extract resource ID from route values or query parameters (matterId, documentId, invoiceId)
        var resourceId = ExtractResourceId(context);
        if (string.IsNullOrEmpty(resourceId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Resource identifier not found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var authContext = new AuthorizationContext
        {
            UserId = userId,
            ResourceId = resourceId,
            Operation = _operation,
            CorrelationId = httpContext.TraceIdentifier
        };

        try
        {
            var result = await _authorizationService.AuthorizeAsync(authContext);

            if (!result.IsAllowed)
            {
                return ProblemDetailsHelper.Forbidden(result.ReasonCode);
            }

            return await next(context);
        }
        catch (Exception ex)
        {
            // Log the actual exception for debugging
            var logger = httpContext.RequestServices.GetService<ILogger<FinanceAuthorizationFilter>>();
            logger?.LogError(ex, "Authorization failed for user {UserId} on resource {ResourceId} operation {Operation}",
                userId, resourceId, _operation);

            return Results.Problem(
                statusCode: 500,
                title: "Authorization Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Extracts the resource identifier from route values or query parameters.
    /// Supports finance-domain identifiers: matterId, documentId, invoiceId, and generic id.
    /// </summary>
    private static string? ExtractResourceId(EndpointFilterInvocationContext context)
    {
        var routeValues = context.HttpContext.Request.RouteValues;

        // Try route values first (most common for RESTful endpoints)
        // Priority: matterId > documentId > invoiceId > id
        if (routeValues.TryGetValue("matterId", out var matterId) && matterId != null)
            return matterId.ToString();

        if (routeValues.TryGetValue("documentId", out var documentId) && documentId != null)
            return documentId.ToString();

        if (routeValues.TryGetValue("invoiceId", out var invoiceId) && invoiceId != null)
            return invoiceId.ToString();

        if (routeValues.TryGetValue("id", out var id) && id != null)
            return id.ToString();

        // Fall back to query parameters
        var query = context.HttpContext.Request.Query;

        if (query.TryGetValue("matterId", out var queryMatterId) && !string.IsNullOrEmpty(queryMatterId))
            return queryMatterId.ToString();

        if (query.TryGetValue("documentId", out var queryDocumentId) && !string.IsNullOrEmpty(queryDocumentId))
            return queryDocumentId.ToString();

        if (query.TryGetValue("invoiceId", out var queryInvoiceId) && !string.IsNullOrEmpty(queryInvoiceId))
            return queryInvoiceId.ToString();

        return null;
    }
}
