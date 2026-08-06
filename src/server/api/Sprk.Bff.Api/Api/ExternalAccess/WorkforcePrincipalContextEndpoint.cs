// teams-app-r1 Task 020 (2026-08-03) — Workforce collaboration principal context endpoint.
//
// GET /api/v1/collab/me — the workforce-plane analog of GET /api/v1/external/me. The
// WorkforceCallerAuthorizationFilter has already resolved (or denied) the caller and, on success,
// placed a WorkforcePrincipal on HttpContext.Items; this handler just projects it. It exists so the
// collaboration surface is demonstrably generalized to accept a workforce principal via the
// ADR-008 endpoint filter (FR-04 AC), and gives tasks 021/022 a live workforce group to extend with
// membership + accessible-record-set enforcement.

using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Handler for <c>GET /api/v1/collab/me</c> — returns the caller's resolved workforce principal.
/// </summary>
public static class WorkforcePrincipalContextEndpoint
{
    public static IResult Handle(HttpContext httpContext)
    {
        // The WorkforceCallerAuthorizationFilter sets this on success and short-circuits on deny,
        // so reaching here without a principal indicates a misconfigured pipeline — fail closed.
        if (httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] is not WorkforcePrincipal principal)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Workforce principal was not resolved for this request.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var kind = principal.IsSystemUser ? "systemuser" : "contact";
        return Results.Ok(new WorkforcePrincipalContextResponse(
            kind,
            principal.SystemUserId,
            principal.ContactId));
    }
}
