using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Spaarke.Core.Auth;
using Spe.Bff.Api.Infrastructure.Errors;

namespace Spe.Bff.Api.Api.Filters;

public class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;

    public DocumentAuthorizationFilter(AuthorizationService authorizationService, string operation)
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

        // Extract resource ID from route values (containerId, driveId, itemId, etc.)
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
            var logger = httpContext.RequestServices.GetService<ILogger<DocumentAuthorizationFilter>>();
            logger?.LogError(ex, "Authorization failed for user {UserId} on resource {ResourceId} operation {Operation}",
                userId, resourceId, _operation);

            return Results.Problem(
                statusCode: 500,
                title: "Authorization Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    private static string? ExtractResourceId(EndpointFilterInvocationContext context)
    {
        var routeValues = context.HttpContext.Request.RouteValues;

        // Try different possible resource ID parameter names
        return routeValues.TryGetValue("documentId", out var documentId) ? documentId?.ToString() :
               routeValues.TryGetValue("containerId", out var containerId) ? containerId?.ToString() :
               routeValues.TryGetValue("driveId", out var driveId) ? driveId?.ToString() :
               routeValues.TryGetValue("itemId", out var itemId) ? itemId?.ToString() :
               routeValues.TryGetValue("resourceId", out var resourceId) ? resourceId?.ToString() :
               null;
    }
}
