using System.Security.Claims;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Authorization filter for Workspace endpoints.
/// Validates that the request carries a resolvable user identity and stores
/// the user ID in <c>HttpContext.Items["UserId"]</c> for downstream handlers.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
///
/// Claims resolution order:
/// 1. "oid" — Entra ID object ID (preferred; stable across tenant changes)
/// 2. <see cref="ClaimTypes.NameIdentifier"/> — fallback for non-Entra tokens
///
/// This filter does NOT perform matter-level access control — it only ensures
/// the caller is an authenticated user with a resolvable identity. Matter-level
/// access is enforced by the Dataverse query in <c>PortfolioService</c>.
/// </remarks>
public class WorkspaceAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<WorkspaceAuthorizationFilter> _logger;

    public WorkspaceAuthorizationFilter(ILogger<WorkspaceAuthorizationFilter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Resolve user ID — prefer Entra "oid" claim for stability
        var userId = httpContext.User.FindFirst("oid")?.Value
                  ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning(
                "Workspace authorization denied: no resolvable user identity. " +
                "CorrelationId={CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Store userId in Items so the endpoint handler does not repeat claim extraction
        httpContext.Items["UserId"] = userId;

        _logger.LogDebug(
            "Workspace authorization passed. UserId={UserId}, CorrelationId={CorrelationId}",
            userId,
            httpContext.TraceIdentifier);

        return await next(context);
    }
}
