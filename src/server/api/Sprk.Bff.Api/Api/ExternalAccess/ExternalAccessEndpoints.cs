using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps all external access API endpoints.
///
/// Two route groups:
///   /api/v1/external        — PRINCIPAL-AGNOSTIC collaboration endpoints (teams-app-r1 task 025 ·
///                             R2 FR-22 · Option A). Dual-scheme: accepts BOTH the CIAM "Ciam" scheme
///                             AND the workforce default scheme via <see cref="AuthPolicies.ExternalCollaboration"/>,
///                             plus the group-level <c>CallerPrincipalAuthorizationFilter</c> which
///                             resolves either token to a plane-agnostic <c>CallerPrincipal</c> + its
///                             Tier-2 record scope. Serves the standalone external SPA (CIAM) AND the
///                             Teams workforce host through ONE endpoint set. CIAM behavior is
///                             unchanged (FR-15 — the CIAM strategy reproduces the old filter exactly).
///   /api/v1/external-access — internal management endpoints. Workforce default scheme (Azure AD JWT).
///
/// The transitional /api/v1/collab workforce group (ADR-028 A2 · teams-app-r1 task 020/030) was
/// removed by R2 task 018 — its /me + download were consolidated onto the principal-agnostic
/// /api/v1/external surface (task 025), leaving it with no first-party caller.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Authorization applied per-endpoint or via route group — no global middleware; the
///          scheme pin is additive to (not a replacement for) the caller-authorization filters.
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
    // Collaboration endpoints — PRINCIPAL-AGNOSTIC (CIAM external contact + workforce user)
    // teams-app-r1 task 025 · R2 FR-22 · Option A
    // =========================================================================

    private static void MapExternalUserEndpoints(WebApplication app)
    {
        // ExternalCollaboration policy accepts BOTH the CIAM "Ciam" scheme AND the workforce default
        // scheme (task 025), so a CIAM external contact AND a workforce (Teams-host) user authenticate
        // here. The group-level CallerPrincipalAuthorizationFilter then resolves either token to a
        // plane-agnostic CallerPrincipal (with its Tier-2 record scope) on HttpContext.Items — the CIAM
        // strategy reproduces the old ExternalCallerAuthorizationFilter byte-for-byte (FR-15). Every
        // handler in this group is principal-agnostic (no if(ciam)…else…).
        var externalGroup = app.MapGroup("/api/v1/external")
            .WithTags("External Access")
            .RequireAuthorization(AuthPolicies.ExternalCollaboration)
            .AddCallerPrincipalAuthorizationFilter();

        // GET /api/v1/external/me — Returns the caller's project access context (either plane)
        externalGroup.MapGet("/me", ExternalUserContextEndpoint.Handle)
            .WithName("GetExternalUserContext")
            .WithSummary("Get the authenticated collaboration caller's project access context")
            .WithDescription(
                "Returns the caller's project access list with access levels. Called by the external " +
                "Secure Project Workspace SPA (CIAM) AND the Teams workforce host on startup to " +
                "initialize navigation. Accepts a valid Entra External ID (CIAM) JWT OR a workforce " +
                "Entra JWT; the caller is resolved to a plane-agnostic principal with its record scope.")
            .Produces<ExternalUserContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/v1/external/me/entitlements — Tier-1 MODULE entitlement context (task 072, Option B).
        // Returns the module-code list the external-spa widget registry gates tab visibility on: workforce
        // from sprk_approlemodulemap (App-Role → module); CIAM blanket outside-counsel set. Distinct from
        // /me above (Tier-2 record access). Same group → same dual-scheme policy + CallerPrincipal filter.
        externalGroup.MapGet("/me/entitlements", MeEntitlementsEndpoint.Handle)
            .WithName("GetExternalUserEntitlements")
            .WithSummary("Get the authenticated collaboration caller's Tier-1 module entitlement context")
            .WithDescription(
                "Returns the caller's entitled module codes (Tier-1). Workforce callers resolve from the " +
                "App-Role→module map (sprk_approlemodulemap); CIAM outside-counsel callers are blanket- " +
                "entitled to the outside-counsel module set. Consumed by the external-spa widget registry " +
                "to gate tab visibility. Record visibility within a module is governed by Tier-2 grants.")
            .Produces<MeEntitlementsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Project data endpoints — list/read projects, documents, todos, contacts, organizations,
        // document download. All principal-agnostic (filter the CallerPrincipal's accessible-record set).
        externalGroup.MapExternalProjectDataEndpoints();

        // Module-host widget-data read seam (task 015 · FR-22 · ADR-028 A3): the per-module read-data
        // endpoints (/api/dataverse/* under this group) consumed by the read-only BffDataverseClient.
        // They inherit this group's ExternalCollaboration dual-scheme policy + CallerPrincipalAuthorizationFilter
        // (the generalized resolver) and Tier-2-scope every data read through the requested module's
        // registered predicate (app-only, no OBO, no Graph pointers). Additive — handlers + the group
        // filter above are untouched.
        externalGroup.MapExternalModuleDataEndpoints();
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

        // POST /api/v1/external-access/invite — Onboard an external user via CIAM (idempotent)
        adminGroup.MapInviteExternalUserEndpoint();

        // POST /api/v1/external-access/invite-and-grant — core-user "Invite to Secure Workspace"
        // action (FR-11 / task 029): onboard (idempotent) + grant the Contact in one action.
        adminGroup.MapInviteAndGrantExternalUserEndpoint();

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
