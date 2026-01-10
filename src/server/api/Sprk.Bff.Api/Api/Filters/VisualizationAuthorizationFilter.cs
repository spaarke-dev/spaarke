using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding VisualizationAuthorizationFilter to endpoints.
/// </summary>
public static class VisualizationAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds visualization authorization to an endpoint.
    /// Reads documentId from route parameter and verifies read access.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddVisualizationAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<VisualizationAuthorizationFilter>>();
            var filter = new VisualizationAuthorizationFilter(authService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for document visualization endpoints.
/// Validates user has read access to the source document before returning related documents.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Uses IAiAuthorizationService for FullUAC authorization via RetrievePrincipalAccess.
/// </para>
///
/// <para>
/// <strong>Authorization Flow:</strong>
/// <list type="number">
/// <item>Extract documentId from route parameter</item>
/// <item>Call IAiAuthorizationService.AuthorizeAsync to verify read access</item>
/// <item>Return 403 Forbidden if user lacks access</item>
/// <item>Proceed to endpoint if authorized</item>
/// </list>
/// </para>
///
/// <para>
/// <strong>Route Parameter:</strong>
/// Expects documentId as a Guid route parameter (e.g., /api/ai/visualization/related/{documentId})
/// </para>
/// </remarks>
public class VisualizationAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<VisualizationAuthorizationFilter>? _logger;

    public VisualizationAuthorizationFilter(
        IAiAuthorizationService authorizationService,
        ILogger<VisualizationAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Extract Azure AD Object ID from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user in Dataverse.
        // Fallback chain matches other authorization filters in the codebase.
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning("[VISUALIZATION-AUTH] Authorization failed: User identity not found in claims");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Extract documentId from route parameter
        var documentId = ExtractDocumentId(context);
        if (documentId == Guid.Empty)
        {
            _logger?.LogWarning("[VISUALIZATION-AUTH] Authorization failed: Invalid or missing documentId in route");
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Document identifier not found or invalid in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var result = await _authorizationService.AuthorizeAsync(
                user,
                [documentId],
                httpContext,
                httpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[VISUALIZATION-AUTH] Document access DENIED: DocumentId={DocumentId}, UserId={UserId}, Reason={Reason}",
                    documentId,
                    userId,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to document",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[VISUALIZATION-AUTH] Document access GRANTED: DocumentId={DocumentId}, UserId={UserId}",
                documentId,
                userId);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[VISUALIZATION-AUTH] Authorization check failed with exception: DocumentId={DocumentId}, UserId={UserId}",
                documentId,
                userId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Extract documentId from route parameter or endpoint arguments.
    /// </summary>
    private static Guid ExtractDocumentId(EndpointFilterInvocationContext context)
    {
        // Try route values first (e.g., /api/ai/visualization/related/{documentId})
        var routeValues = context.HttpContext.Request.RouteValues;
        if (routeValues.TryGetValue("documentId", out var routeValue) &&
            routeValue is string stringValue &&
            Guid.TryParse(stringValue, out var routeGuid))
        {
            return routeGuid;
        }

        // Try endpoint arguments (bound parameters)
        foreach (var argument in context.Arguments)
        {
            if (argument is Guid guid && guid != Guid.Empty)
            {
                return guid;
            }
        }

        return Guid.Empty;
    }
}
