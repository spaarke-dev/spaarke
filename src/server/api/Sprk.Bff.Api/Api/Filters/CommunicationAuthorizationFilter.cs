using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that validates the user is authorized to send communications.
/// Phase 1: Any authenticated user with a valid identity can send.
/// Phase 2+: Can be extended to check Dataverse security roles or privileges.
/// Applied per ADR-008 (endpoint filters, not global middleware).
/// </summary>
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
        var userId = user.FindFirst("oid")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return ValueTask.FromResult<object?>(
                ProblemDetailsHelper.Forbidden("COMMUNICATION_NOT_AUTHORIZED"));
        }

        return next(context);
    }
}
