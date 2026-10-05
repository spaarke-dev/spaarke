using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// IDENTITY PRECONDITION for the <c>/api/communications</c> routes: the caller is authenticated and carries a
/// resolvable Entra <c>oid</c>. It is NOT a record gate and decides nothing about any record — any signed-in user
/// with an oid passes. Applied per ADR-008 (endpoint filters, not global middleware).
/// </summary>
/// <remarks>
/// <para>unified-access-control-r2 task 161 (route authorization sweep, 2026-10-02): routes described as
/// "auth-scoped via the endpoint filter" relied on this filter alone while their handlers read or wrote, as the BFF's
/// own identity, a record the caller chose. The per-record decision is
/// <see cref="CommunicationRecordAuthorizationFilter"/>, attached AFTER this filter in each such route's chain. Do not
/// add a decision service here: RouteAuthorizationGuardTests Rule B would then treat every bare use of this filter as
/// a deciding gate, so identity-only routes would look record-gated to the guard.</para>
/// </remarks>
public sealed class CommunicationAuthorizationFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Verify the user is authenticated and has a valid identity
        if (user.Identity is not { IsAuthenticated: true })
        {
            return ValueTask.FromResult<object?>(
                Results.Problem(
                    title: "Unauthorized",
                    detail: "Authentication required to send communications.",
                    statusCode: StatusCodes.Status401Unauthorized));
        }

        // Verify user has a valid object ID claim (Azure AD oid)
        var userId = CallerResolution.ResolveObjectId(user);

        if (string.IsNullOrEmpty(userId))
        {
            return ValueTask.FromResult<object?>(
                ProblemDetailsHelper.Forbidden("COMMUNICATION_NOT_AUTHORIZED"));
        }

        return next(context);
    }
}
