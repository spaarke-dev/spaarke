// teams-app-r1 Task 025 (2026-08-05) — Principal-agnostic caller authorization filter (R2 FR-22).
//
// The single ADR-008 endpoint filter for the dual-scheme /api/v1/external collaboration group. It
// runs AFTER the multi-scheme policy has authenticated the token (CIAM or workforce), resolves the
// caller to a plane-agnostic CallerPrincipal via ICallerPrincipalResolver, and:
//   - on deny → returns the strategy's ProblemDetails (401 missing-claims / 403 not-resolved / etc.)
//   - on success → sets CallerPrincipal on HttpContext.Items and continues.
//
// It replaces the CIAM-only AddExternalCallerAuthorizationFilter on this group; the CIAM strategy
// inside the resolver reproduces that filter's behavior byte-for-byte (R2 guardrail #3), so the
// external SPA is unaffected while the workforce plane is now served by the SAME endpoints.
//
// ADR-008: per-endpoint (here, group-level) filter — no global middleware.
// ADR-019: ProblemDetails on every 401/403.

using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension that adds principal-agnostic caller authorization to the collaboration surface.
/// </summary>
public static class CallerPrincipalAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the <see cref="CallerPrincipalAuthorizationFilter"/> to an endpoint (or route group).
    /// Resolves the CIAM-or-workforce caller to a <see cref="CallerPrincipal"/> on HttpContext.Items.
    /// </summary>
    public static TBuilder AddCallerPrincipalAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var resolver = context.HttpContext.RequestServices
                .GetRequiredService<ICallerPrincipalResolver>();

            var resolution = await resolver
                .ResolveAsync(context.HttpContext, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (!resolution.IsResolved)
            {
                // Strategy already logged the reason; short-circuit with its ProblemDetails result.
                return resolution.Failure;
            }

            context.HttpContext.Items[CallerPrincipal.HttpContextItemsKey] = resolution.Principal;
            return await next(context);
        });
    }
}
