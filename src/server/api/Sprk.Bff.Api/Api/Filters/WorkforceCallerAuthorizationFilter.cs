// teams-app-r1 Task 020 (2026-08-03) — Workforce collaboration authorization filter.
//
// Mirrors ExternalCallerAuthorizationFilter (the CIAM analog) but for the WORKFORCE plane
// (ADR-028 Amendment A2). Runs AFTER the workforce default JwtBearer scheme has validated the
// token (so HttpContext.User is populated), resolves the caller to a WorkforcePrincipal via
// IWorkforcePrincipalResolver, and:
//   - on deny (missing identity claims) → 401
//   - on deny (resolved to neither systemuser nor contact) → 403 (+ machine-readable deny code)
//   - on success → sets WorkforcePrincipal on HttpContext.Items and continues.
//
// ADR-008: per-endpoint filter — no global middleware; the filter has full route/claims context.
// ADR-019: ProblemDetails for every 401/403.

using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension that adds workforce-caller authorization to a collaboration endpoint. Resolves the
/// workforce principal (systemuser / contact-only / deny) from the already-validated JWT identity.
/// </summary>
public static class WorkforceCallerAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the <see cref="WorkforceCallerAuthorizationFilter"/> to an endpoint.
    /// </summary>
    public static TBuilder AddWorkforceCallerAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var resolver = context.HttpContext.RequestServices
                .GetRequiredService<IWorkforcePrincipalResolver>();
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<WorkforceCallerAuthorizationFilter>>();

            var filter = new WorkforceCallerAuthorizationFilter(resolver, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter (ADR-008) that resolves the workforce-authenticated caller to a
/// <see cref="WorkforcePrincipal"/> and denies an unresolvable caller — the collaboration
/// generalization from CIAM-contact-only to the workforce plane (ADR-028 A2 / FR-04).
/// </summary>
public sealed class WorkforceCallerAuthorizationFilter : IEndpointFilter
{
    private readonly IWorkforcePrincipalResolver _resolver;
    private readonly ILogger<WorkforceCallerAuthorizationFilter> _logger;

    public WorkforceCallerAuthorizationFilter(
        IWorkforcePrincipalResolver resolver,
        ILogger<WorkforceCallerAuthorizationFilter> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        var resolution = await _resolver
            .ResolveAsync(httpContext.User, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!resolution.IsResolved)
        {
            // Explicit deny — never a silent pass-through with an unscoped principal.
            switch (resolution.DenyReason)
            {
                case WorkforceDenyReason.MissingIdentityClaims:
                    _logger.LogWarning(
                        "[WF-AUTH] Denying request: {DenyCode}", resolution.DenyCode);
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized",
                        detail: "Identity token is missing required user identity claims",
                        type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                        extensions: new Dictionary<string, object?>
                        {
                            ["reasonCode"] = resolution.DenyCode
                        });

                case WorkforceDenyReason.PrincipalNotResolved:
                default:
                    _logger.LogWarning(
                        "[WF-AUTH] Denying request: {DenyCode}", resolution.DenyCode);
                    return ProblemDetailsHelper.Forbidden(
                        resolution.DenyCode ?? WorkforcePrincipalResolver.DenyPrincipalNotResolved);
            }
        }

        // Success — hand the resolved principal to downstream handlers + enforcement (task 021/022).
        httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] = resolution.Principal;

        return await next(context);
    }
}
