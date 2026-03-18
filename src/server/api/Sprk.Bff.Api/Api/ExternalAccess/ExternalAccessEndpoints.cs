using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps all external access API endpoints.
///
/// Two route groups:
///   /api/v1/external        — external user endpoints (Azure AD B2B guest JWT, RequireAuthorization + ExternalCallerAuthorizationFilter)
///   /api/v1/external-access — internal management endpoints (Azure AD JWT auth via RequireAuthorization)
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Authorization applied per-endpoint or via route group — no global middleware.
/// </summary>
public static class ExternalAccessEndpoints
{
    /// <summary>
    /// Registers all external access endpoints on the application.
    /// Called from <see cref="Infrastructure.DI.EndpointMappingExtensions.MapSpaarkeEndpoints"/>.
    /// </summary>
    public static void MapExternalAccessEndpoints(this WebApplication app)
    {
        MapExternalUserEndpoints(app);
        MapInternalManagementEndpoints(app);
    }

    // =========================================================================
    // External user endpoints — Azure AD B2B guest authentication
    // =========================================================================

    private static void MapExternalUserEndpoints(WebApplication app)
    {
        // RequireAuthorization: ASP.NET Core middleware validates the Azure AD B2B JWT.
        // ExternalCallerAuthorizationFilter: resolves Contact + participation data from claims.
        var externalGroup = app.MapGroup("/api/v1/external")
            .WithTags("External Access")
            .RequireAuthorization();

        // GET /api/v1/external/me — Returns external user's project access context
        externalGroup.MapGet("/me", ExternalUserContextEndpoint.Handle)
            .WithName("GetExternalUserContext")
            .WithSummary("Get authenticated external user's project access context")
            .WithDescription(
                "Returns the Contact's project access list with access levels. " +
                "Called by the Power Pages SPA on startup to initialize navigation. " +
                "Requires a valid Azure AD JWT from an authenticated B2B guest account.")
            .Produces<ExternalUserContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddExternalCallerAuthorizationFilter();

        // Project data endpoints — list/read projects, documents, events, contacts, organizations.
        // Write endpoints — create/update events.
        externalGroup.MapExternalProjectDataEndpoints();
    }

    // =========================================================================
    // Internal management endpoints — Azure AD authentication
    // =========================================================================

    private static void MapInternalManagementEndpoints(WebApplication app)
    {
        var adminGroup = app.MapGroup("/api/v1/external-access")
            .WithTags("External Access Management")
            .RequireAuthorization();

        // POST /api/v1/external-access/grant — Grant Contact access to a Secure Project
        adminGroup.MapGrantExternalAccessEndpoint();

        // POST /api/v1/external-access/revoke — Revoke Contact access from a Secure Project
        adminGroup.MapRevokeExternalAccessEndpoint();

        // POST /api/v1/external-access/invite — Create Power Pages portal invitation
        adminGroup.MapInviteExternalUserEndpoint();

        // POST /api/v1/external-access/close-project — Close project and cascade revocation
        adminGroup.MapPost("/close-project", ProjectClosureEndpoint.Handle)
            .WithName("CloseSecureProject")
            .WithSummary("Close a Secure Project and revoke all external access")
            .WithDescription(
                "Deactivates all active sprk_externalrecordaccess records for the project, " +
                "removes external members from the SPE container (if containerId provided), " +
                "and invalidates the Redis participation cache for all affected Contacts.")
            .Produces<CloseProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/v1/external-access/provision-project — Provision infrastructure for Secure Project
        adminGroup.MapProvisionProjectEndpoint();
    }
}
