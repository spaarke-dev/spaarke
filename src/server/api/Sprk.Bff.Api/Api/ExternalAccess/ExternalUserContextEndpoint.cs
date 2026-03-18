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
    /// Registers GET /me on the provided external user route group.
    /// Applies ExternalCallerAuthorizationFilter to validate the portal JWT and
    /// resolve Contact participation data before the handler runs.
    /// </summary>
    public static RouteGroupBuilder MapExternalUserContextEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/me", Handle)
            .WithName("GetExternalUserContext")
            .WithSummary("Get authenticated portal user's project access context")
            .WithDescription(
                "Returns the Contact's project access list with access levels. " +
                "Called by the Power Pages SPA on startup to initialize navigation. " +
                "Requires a valid Power Pages portal-issued JWT token.")
            .Produces<ExternalUserContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddExternalCallerAuthorizationFilter();

        return group;
    }

    /// <summary>
    /// Handles GET /api/v1/external/me.
    ///
    /// The ExternalCallerAuthorizationFilter (applied via AddExternalCallerAuthorizationFilter()
    /// in ExternalAccessEndpoints.cs) has already validated the portal token and stored
    /// the resolved ExternalCallerContext on HttpContext.Items before this handler runs.
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
        // ExternalCallerAuthorizationFilter has already run and set the context
        var callerContext = httpContext.Items[ExternalCallerContext.HttpContextItemsKey] as ExternalCallerContext;

        if (callerContext is null)
        {
            // Should not happen if the filter is correctly applied, but guard defensively
            logger.LogError(
                "[EXT-ME] ExternalCallerContext not found in HttpContext.Items. " +
                "Ensure AddExternalCallerAuthorizationFilter() is applied to this endpoint. TraceId={TraceId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        logger.LogInformation(
            "[EXT-ME] Contact {ContactId} requested context: {Count} project participations. TraceId={TraceId}",
            callerContext.ContactId, callerContext.Participations.Count, httpContext.TraceIdentifier);

        var projects = callerContext.Participations
            .Select(p => new ProjectAccessEntry(
                p.ProjectId,
                p.AccessLevel.ToString()))
            .ToList();

        var response = new ExternalUserContextResponse(
            callerContext.ContactId,
            callerContext.Email,
            projects);

        return Results.Ok(response);
    }
}
