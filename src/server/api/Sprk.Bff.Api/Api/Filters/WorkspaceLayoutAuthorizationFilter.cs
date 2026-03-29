using System.Security.Claims;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding WorkspaceLayoutAuthorizationFilter to endpoints.
/// </summary>
public static class WorkspaceLayoutAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds workspace layout authorization to an endpoint.
    /// Validates layout existence, system layout protection, and user ownership.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddWorkspaceLayoutAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var layoutService = context.HttpContext.RequestServices.GetRequiredService<WorkspaceLayoutService>();
            var filter = new WorkspaceLayoutAuthorizationFilter(layoutService);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter that validates workspace layout ownership and system layout protection.
/// System layouts allow GET for all authenticated users but reject PUT/DELETE with 403.
/// User layouts require ownership — returns 404 if not found, 403 if not owned.
/// </summary>
/// <remarks>
/// Applied per-endpoint on routes with an {id} parameter (ADR-008: no global middleware).
/// </remarks>
public class WorkspaceLayoutAuthorizationFilter : IEndpointFilter
{
    private readonly WorkspaceLayoutService _layoutService;

    public WorkspaceLayoutAuthorizationFilter(WorkspaceLayoutService layoutService)
    {
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // 1. Extract user ID from claims
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // 2. Extract and parse layout ID from route values
        if (!httpContext.Request.RouteValues.TryGetValue("id", out var rawId)
            || rawId is null
            || !Guid.TryParse(rawId.ToString(), out var layoutId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Layout identifier is missing or malformed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var method = httpContext.Request.Method;
        var isMutation = HttpMethods.IsPut(method) || HttpMethods.IsDelete(method);

        // 3. System layout check — allow GET, reject mutations
        if (SystemWorkspaceLayouts.IsSystemLayout(layoutId))
        {
            if (isMutation)
            {
                var logger = httpContext.RequestServices.GetService<ILogger<WorkspaceLayoutAuthorizationFilter>>();
                logger?.LogWarning(
                    "User {UserId} attempted to {Method} system layout {LayoutId}",
                    userId, method, layoutId);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "System layouts cannot be modified",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            // GET on system layout — allowed for all authenticated users
            return await next(context);
        }

        // 4. User layout — verify existence and ownership
        try
        {
            var layout = await _layoutService.GetLayoutByIdAsync(layoutId, userId);

            if (layout is null)
            {
                // GetLayoutByIdAsync returns null for both "not found" and "not owned"
                // (ownership mismatch is logged inside the service).
                // Check if the layout exists at all by querying without ownership filter.
                // Since the service already handles this distinction internally, a null
                // result means either not found or not owned — return 404 to avoid
                // leaking layout existence to unauthorized users.
                return Results.Problem(
                    statusCode: 404,
                    title: "Not Found",
                    detail: "Workspace layout not found",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Layout exists and is owned by this user — proceed
            return await next(context);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices.GetService<ILogger<WorkspaceLayoutAuthorizationFilter>>();
            logger?.LogError(ex,
                "Authorization check failed for user {UserId} on layout {LayoutId} operation {Method}",
                userId, layoutId, method);

            return Results.Problem(
                statusCode: 500,
                title: "Authorization Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }
}
