using System.Security.Claims;
using Spaarke.Contracts.Provisioning;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>Extension methods for adding <see cref="KeylessProofAuthorizationFilter"/> to endpoints.</summary>
public static class KeylessProofAuthorizationFilterExtensions
{
    /// <summary>
    /// Admits only an application (app-only) token carrying the <see cref="KeylessProofContract.AppRoleValue"/> app
    /// role. ADR-008: authorization in an endpoint filter, not middleware.
    /// </summary>
    public static TBuilder AddKeylessProofAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<KeylessProofAuthorizationFilter>>();
            return await new KeylessProofAuthorizationFilter(logger).InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization for <see cref="KeylessProofContract.Route"/> (task 230b): the caller must present an
/// <b>application</b> token for this BFF carrying the <see cref="KeylessProofContract.AppRoleValue"/> app role.
/// </summary>
/// <remarks>
/// <para><b>Who holds the role.</b> H3 defines it on the customer's BFF app registration with
/// <c>allowedMemberTypes: ["Application"]</c> and assigns it to the L2 Worker identity only — no user can be assigned
/// it, and no other application is.</para>
/// <para><b>Why app-only is checked as well as the role.</b> A delegated token never carries an application role,
/// but the check costs nothing and keeps the rule true if a future change lets users hold the role: a token with a
/// <c>scp</c> claim acts for a user and is refused. A token whose <c>idtyp</c> claim is present and is not
/// <c>app</c> is refused too.</para>
/// <para>The audience and issuer are validated by the default JwtBearer scheme before this filter runs (this BFF's
/// app registration, this tenant).</para>
/// </remarks>
public sealed class KeylessProofAuthorizationFilter : IEndpointFilter
{
    // Deny codes follow {domain}.{area}.{action}.{reason}.
    internal const string DenyCode = "sdap.access.deny.keyless_proof_role";
    internal const string UnauthenticatedCode = "sdap.access.deny.unauthenticated";

    private const string ScopeClaim = "scp";
    private const string ScopeClaimLong = "http://schemas.microsoft.com/identity/claims/scope";

    private readonly ILogger<KeylessProofAuthorizationFilter>? _logger;

    public KeylessProofAuthorizationFilter(ILogger<KeylessProofAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "An application token for this API is required.",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = UnauthenticatedCode,
                    ["traceId"] = httpContext.TraceIdentifier,
                });
        }

        if (!IsAdmitted(user))
        {
            _logger?.LogWarning(
                "Keyless proof denied: caller appid={AppId} is not an application holding the {Role} role.",
                user.FindFirst("appid")?.Value ?? user.FindFirst("azp")?.Value, KeylessProofContract.AppRoleValue);

            return ProblemDetailsHelper.Forbidden(
                DenyCode,
                detail: $"Only an application holding the '{KeylessProofContract.AppRoleValue}' role may call this endpoint.",
                traceId: httpContext.TraceIdentifier);
        }

        return await next(context);
    }

    /// <summary>True for an app-only token carrying the keyless-proof role.</summary>
    internal static bool IsAdmitted(ClaimsPrincipal user)
    {
        var actsForUser = user.HasClaim(c => c.Type is ScopeClaim or ScopeClaimLong);
        var idtyp = user.FindFirst("idtyp")?.Value;
        var appOnly = !actsForUser && (idtyp is null || string.Equals(idtyp, "app", StringComparison.Ordinal));

        var hasRole = user.IsInRole(KeylessProofContract.AppRoleValue)
            || user.HasClaim(c => (c.Type is "roles" or ClaimTypes.Role) && c.Value == KeylessProofContract.AppRoleValue);

        return appOnly && hasRole;
    }
}
