using System.Security.Claims;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Extension methods for adding agent authorization to endpoints.
/// </summary>
public static class AgentAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds agent gateway authorization to an endpoint.
    /// Validates the caller has a valid Azure AD identity with the required claims
    /// for M365 Copilot agent-to-BFF communication.
    /// </summary>
    public static TBuilder AddAgentAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<AgentAuthorizationFilter>>();
            var filter = new AgentAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for M365 Copilot agent gateway endpoints.
///
/// Validates the authenticated identity has the required claims for agent-to-BFF calls.
/// The M365 Copilot agent authenticates via Azure AD on-behalf-of flow, so the JWT
/// contains the end-user's identity with 'oid' and 'tid' claims.
///
/// ADR-008: Endpoint filter pattern — no global middleware.
/// ADR-010: Concrete type — no unnecessary interface.
/// </summary>
public class AgentAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<AgentAuthorizationFilter>? _logger;

    public AgentAuthorizationFilter(ILogger<AgentAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Extract Azure AD Object ID from claims.
        // Copilot agent tokens carry the end-user's identity via OBO flow.
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning("[AGENT-AUTH] No user identity claim found in token");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found in token",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Extract tenant ID — required for multi-tenant isolation.
        var tenantId = user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrEmpty(tenantId))
        {
            _logger?.LogWarning("[AGENT-AUTH] No tenant claim found in token for user {UserId}", userId);
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in token",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // TODO: Validate agent-specific audience or app role claims when agent app registration is finalized.
        // For now, a valid Azure AD token with oid + tid is sufficient (same as other BFF endpoints).

        _logger?.LogDebug(
            "[AGENT-AUTH] Agent request authorized: UserId={UserId}, TenantId={TenantId}",
            userId, tenantId);

        return await next(context);
    }
}
