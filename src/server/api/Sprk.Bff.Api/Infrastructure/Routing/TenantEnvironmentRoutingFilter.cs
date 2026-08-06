// teams-app-r1 Task 060 (2026-08-03) — `tid`→environment routing endpoint filter.
//
// The ADR-008 integration seam for the router: a per-endpoint filter (NOT global middleware) that a
// collaboration endpoint attaches via `.AddTenantEnvironmentRoutingFilter()`. It runs AFTER the
// workforce default JwtBearer scheme has validated the token (so HttpContext.User is populated),
// resolves the caller's tid to a single environment, and:
//   - missing tid claim         → 401 (cannot establish tenant identity)
//   - unmapped/ambiguous/malformed → 403 (+ machine-readable sdap.routing.deny.* reasonCode)
//   - success                   → sets ResolvedTenantEnvironment on HttpContext.Items and continues.
//
// On EVERY deny path, no environment context is attached to the request — the request cannot proceed
// against any environment. ADR-019: ProblemDetails for every 401/403.

using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Infrastructure.Routing;

/// <summary>
/// Extension that attaches <see cref="TenantEnvironmentRoutingFilter"/> to a collaboration endpoint.
/// </summary>
public static class TenantEnvironmentRoutingFilterExtensions
{
    /// <summary>
    /// Adds the <c>tid</c>→environment routing filter to an endpoint. Compose alongside the workforce
    /// caller filter on the collaboration-endpoint group (task 020 resolves WHO the caller is; this
    /// resolves WHICH environment their tenant's data lives in — both deny, never default).
    /// </summary>
    public static TBuilder AddTenantEnvironmentRoutingFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var router = context.HttpContext.RequestServices
                .GetRequiredService<ITenantEnvironmentRouter>();
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<TenantEnvironmentRoutingFilter>>();

            var filter = new TenantEnvironmentRoutingFilter(router, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter (ADR-008) that routes an authenticated <c>tid</c> to its environment and denies
/// any tid that is missing, unmapped, ambiguous, or malformed — never a default environment (FR-09).
/// </summary>
public sealed class TenantEnvironmentRoutingFilter : IEndpointFilter
{
    private readonly ITenantEnvironmentRouter _router;
    private readonly ILogger<TenantEnvironmentRoutingFilter> _logger;

    public TenantEnvironmentRoutingFilter(
        ITenantEnvironmentRouter router,
        ILogger<TenantEnvironmentRoutingFilter> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        var resolution = _router.Resolve(httpContext.User);

        if (!resolution.IsResolved)
        {
            // Explicit deny — never a silent pass-through, never an environment on the request.
            switch (resolution.DenyReason)
            {
                case TenantEnvironmentDenyReason.MissingTenantClaim:
                    _logger.LogWarning("[TID-ROUTE] Denying request: {DenyCode}", resolution.DenyCode);
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized",
                        detail: "Token is missing the tenant (tid) claim required to route the request",
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                        extensions: new Dictionary<string, object?>
                        {
                            ["reasonCode"] = resolution.DenyCode
                        });

                case TenantEnvironmentDenyReason.UnmappedTenant:
                case TenantEnvironmentDenyReason.AmbiguousMapping:
                case TenantEnvironmentDenyReason.MalformedMapping:
                default:
                    _logger.LogWarning("[TID-ROUTE] Denying request: {DenyCode}", resolution.DenyCode);
                    return ProblemDetailsHelper.Forbidden(
                        resolution.DenyCode ?? TenantEnvironmentRouter.DenyUnmappedTenant);
            }
        }

        // Success — hand the resolved environment to downstream handlers / connection selection.
        httpContext.Items[ResolvedTenantEnvironment.HttpContextItemsKey] = resolution.Environment;

        return await next(context);
    }
}
