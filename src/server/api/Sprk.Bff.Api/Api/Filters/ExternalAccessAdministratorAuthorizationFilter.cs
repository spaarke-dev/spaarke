using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>Extension methods for adding <see cref="ExternalAccessAdministratorAuthorizationFilter"/> to endpoints.</summary>
public static class ExternalAccessAdministratorAuthorizationFilterExtensions
{
    /// <summary>
    /// Admits only an external-access administrator: a member of the standing-grant administrators field security profile,
    /// directly or through a team (<see cref="ExternalAccessAdministratorAuthority"/>). ADR-008: authorization in an endpoint
    /// filter, not middleware. The authority is built per request from services the BFF already registers
    /// (<see cref="CallerRecordAccessProbe"/>, <see cref="DataverseWebApiClient"/>); nothing new is registered.
    /// </summary>
    public static TBuilder AddExternalAccessAdministratorAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var authority = new ExternalAccessAdministratorAuthority(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetRequiredService<DataverseWebApiClient>(),
                services.GetRequiredService<ILogger<ExternalAccessAdministratorAuthority>>());
            return await new ExternalAccessAdministratorAuthorizationFilter(authority).InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The external-access administrator gate for the person-level administration routes (<c>/remove-person</c>,
/// <c>/reinstate-person</c>): the caller must hold the authority <see cref="ExternalAccessAdministratorAuthority"/> decides —
/// who the caller is from WhoAmI over their own staff OBO token, and their profile membership read with the app credential.
/// Every refusal (not a member, an unresolved caller, a missing or ambiguous profile, a fault, a timeout) is ONE uniform 403;
/// the specific reason is logged for operators only. The decision is left on the request for the handler's audit trace.
/// </summary>
public sealed class ExternalAccessAdministratorAuthorizationFilter : IEndpointFilter
{
    /// <summary>The reason code of the uniform refusal.</summary>
    public const string AdministratorRequiredReasonCode = "sdap.access.external_admin_required";

    private const string DecisionItemKey = "sprk.externalAccess.administratorDecision";

    private readonly ExternalAccessAdministratorAuthority _authority;

    public ExternalAccessAdministratorAuthorizationFilter(ExternalAccessAdministratorAuthority authority)
        => _authority = authority ?? throw new ArgumentNullException(nameof(authority));

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var decision = await _authority.DecideAsync(
            TokenHelper.ExtractBearerTokenOrNull(httpContext), httpContext.RequestAborted).ConfigureAwait(false);
        if (!decision.IsAdministrator)
            return Refusal(httpContext);

        httpContext.Items[DecisionItemKey] = decision;
        return await next(context).ConfigureAwait(false);
    }

    /// <summary>The decision this filter left on the request, or null when it did not run or refused.</summary>
    internal static AdministratorAuthorityDecision? DecisionOf(HttpContext httpContext)
        => httpContext.Items.TryGetValue(DecisionItemKey, out var value) ? value as AdministratorAuthorityDecision : null;

    /// <summary>Puts a decision on the request as the filter does (for tests that drive a handler directly).</summary>
    internal static void SetDecision(HttpContext httpContext, AdministratorAuthorityDecision decision)
        => httpContext.Items[DecisionItemKey] = decision;

    /// <summary>The ONE refusal: 403, the same body for every reason.</summary>
    internal static IResult Refusal(HttpContext httpContext)
        => ProblemDetailsHelper.Forbidden(
            AdministratorRequiredReasonCode,
            "Only an external-access administrator can remove or reinstate a person.",
            httpContext.TraceIdentifier);
}
