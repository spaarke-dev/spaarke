using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding SpeAdminAuthorizationFilter to endpoints.
/// </summary>
public static class SpeAdminAuthorizationFilterExtensions
{
    /// <summary>
    /// Restricts the endpoint or route group to users with an admin role.
    /// Returns a 403 ProblemDetails response for non-admin users.
    /// </summary>
    /// <remarks>
    /// Follows ADR-008: Use endpoint filters for authorization — no global auth middleware.
    /// Admin check mirrors the "SystemAdmin" policy in AuthorizationModule.cs.
    /// </remarks>
    public static TBuilder AddSpeAdminAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<SpeAdminAuthorizationFilter>>();
            var filter = new SpeAdminAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that restricts access to SPE Admin endpoints to users with an admin role.
/// Non-admin users receive a 403 ProblemDetails response with deny code
/// <c>sdap.access.deny.role_insufficient</c>.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for authorization — no global auth middleware.
/// Each route group applies its own authorization filter.
///
/// Admin role detection checks both Azure AD app roles and the "roles" claim:
/// - <c>IsInRole("Admin")</c>
/// - <c>IsInRole("SystemAdmin")</c>
/// - <c>HasClaim("roles", "Admin")</c>
/// - <c>HasClaim("roles", "SystemAdmin")</c>
///
/// Reuses the existing System Administrator Dataverse security role (no new role created in Phase 1).
/// </remarks>
public class SpeAdminAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<SpeAdminAuthorizationFilter>? _logger;

    // Deny code following pattern: {domain}.{area}.{action}.{reason}
    private const string DenyCode = "sdap.access.deny.role_insufficient";

    public SpeAdminAuthorizationFilter(ILogger<SpeAdminAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Verify the user identity is present (base authentication must already have run)
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "SPE Admin authorization denied: No user identity found in token. " +
                "Request path: {Path}", httpContext.Request.Path);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found in authentication token");
        }

        // Check for admin role — mirrors the "SystemAdmin" policy in AuthorizationModule.cs
        var isAdmin = httpContext.User.IsInRole("Admin")
            || httpContext.User.IsInRole("SystemAdmin")
            || httpContext.User.HasClaim(c => c.Type == "roles" && c.Value == "Admin")
            || httpContext.User.HasClaim(c => c.Type == "roles" && c.Value == "SystemAdmin");

        if (!isAdmin)
        {
            _logger?.LogWarning(
                "SPE Admin authorization denied: User {UserId} does not have admin role. " +
                "Request path: {Path}", userId, httpContext.Request.Path);

            return ProblemDetailsHelper.Forbidden(DenyCode);
        }

        _logger?.LogDebug(
            "SPE Admin authorization granted for user {UserId}. Request path: {Path}",
            userId, httpContext.Request.Path);

        return await next(context);
    }
}
