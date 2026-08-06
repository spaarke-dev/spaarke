using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// GET /api/v1/external/me
///
/// Called by the Power Pages SPA on startup to retrieve the authenticated portal user's
/// project access information. Returns the Contact's project list with access levels so
/// the SPA can build its navigation and enforce client-side access restrictions.
///
/// Authentication: Power Pages portal JWT (ExternalCallerAuthorizationFilter).
/// The filter validates the token and loads participations from Redis / Dataverse.
///
/// Follows ADR-001: Minimal API — no controllers.
/// Follows ADR-008: ExternalCallerAuthorizationFilter applied per-endpoint.
/// Follows ADR-009: Redis-first caching — participation data is cached by the filter.
/// </summary>
public static class ExternalUserContextEndpoint
{
    /// <summary>
    /// Handles GET /api/v1/external/me.
    ///
    /// The CallerPrincipalAuthorizationFilter (applied at the group level in ExternalAccessEndpoints.cs,
    /// teams-app-r1 task 025) has already authenticated the caller on EITHER plane (CIAM external
    /// contact or workforce user) and stored the resolved CallerPrincipal on HttpContext.Items before
    /// this handler runs.
    /// </summary>
    /// <param name="httpContext">The current HTTP context (used to retrieve ExternalCallerContext).</param>
    /// <param name="logger">Logger for request tracing.</param>
    /// <returns>
    /// 200 OK with ExternalUserContextResponse containing the Contact's project access list.
    /// 401 Unauthorized if the portal token is missing or invalid (returned by filter).
    /// 403 Forbidden if the Contact has no active participation records (returned by filter).
    /// </returns>
    public static IResult Handle(
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        // CallerPrincipalAuthorizationFilter (task 025) has already run and set the principal —
        // for EITHER plane (CIAM external contact or workforce user). Principal-agnostic: this handler
        // does not branch on plane; it projects the resolved principal's project access uniformly.
        var caller = httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;

        if (caller is null)
        {
            // Should not happen if the filter is correctly applied, but guard defensively
            logger.LogError(
                "[EXT-ME] CallerPrincipal not found in HttpContext.Items. " +
                "Ensure AddCallerPrincipalAuthorizationFilter() is applied to this group. TraceId={TraceId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        logger.LogInformation(
            "[EXT-ME] Caller {ContactId} ({Plane}) requested context: {Count} accessible projects. TraceId={TraceId}",
            caller.ContactId, caller.Plane, caller.ProjectAccess.Count, httpContext.TraceIdentifier);

        var projects = caller.ProjectAccess
            .Select(p => new ProjectAccessEntry(
                p.ProjectId,
                p.AccessLevel.ToString()))
            .ToList();

        var response = new ExternalUserContextResponse(
            caller.ContactId,
            caller.Email,
            projects);

        return Results.Ok(response);
    }
}
