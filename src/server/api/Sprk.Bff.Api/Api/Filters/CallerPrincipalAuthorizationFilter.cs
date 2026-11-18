// teams-app-r1 Task 025 (2026-08-05) — Principal-agnostic caller authorization filter (R2 FR-22).
//
// The single ADR-008 endpoint filter for the dual-scheme /api/v1/external collaboration group. It
// runs AFTER the multi-scheme policy has authenticated the token (CIAM or workforce), resolves the
// caller to a plane-agnostic CallerPrincipal via ICallerPrincipalResolver, and:
//   - on deny → returns the strategy's ProblemDetails (401 missing-claims / 403 not-resolved / etc.)
//   - on success → sets CallerPrincipal on HttpContext.Items and continues.
//
// It replaced the retired CIAM-only external-caller filter on this group, so the workforce plane is
// served by the SAME endpoints as the external SPA. The CIAM strategy inside the resolver began as a
// byte-for-byte port of that filter (R2 guardrail #3); since uac-r2 task 135 (C1) it runs the same
// contact-plane composition and veto pipeline as the workforce contact plane.
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
