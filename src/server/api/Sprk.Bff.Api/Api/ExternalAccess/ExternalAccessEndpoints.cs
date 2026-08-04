using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Maps all external access API endpoints.
///
/// Three route groups (distinct authentication schemes / planes):
///   /api/v1/external        — external user endpoints. CIAM-only: pinned to the "Ciam" JwtBearer
///                             scheme via <see cref="AuthPolicies.CiamExternal"/> (task 021), plus the
///                             per-endpoint <c>ExternalCallerAuthorizationFilter</c> (ADR-028 A1).
///   /api/v1/external-access — internal management endpoints. Workforce default scheme (Azure AD JWT).
///   /api/v1/collab          — WORKFORCE collaboration endpoints (ADR-028 Amendment A2 · teams-app-r1
///                             FR-04). Workforce default scheme + the per-endpoint
///                             <c>WorkforceCallerAuthorizationFilter</c> which resolves the caller to a
///                             systemuser / contact-only principal (or denies). This is the
///                             collaboration surface generalized from CIAM-contact-only to the
///                             workforce plane; it does NOT touch the CIAM /api/v1/external group
///                             (FR-15 — the standalone SPA is unchanged).
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
        MapWorkforceCollaborationEndpoints(app);
    }

    // =========================================================================
    // External user endpoints — Entra External ID (CIAM) authentication
    // =========================================================================

    private static void MapExternalUserEndpoints(WebApplication app)
    {
        // CiamExternal policy pins the "Ciam" JwtBearer scheme (task 020) so ONLY CIAM tokens
        // authenticate here — a workforce (default-scheme) token is rejected (ADR-028 A1).
        // ExternalCallerAuthorizationFilter (per-endpoint) then resolves Contact + participation
        // data from claims — the scheme pin is additive to the filter, not a replacement.
        var externalGroup = app.MapGroup("/api/v1/external")
            .WithTags("External Access")
            .RequireAuthorization(AuthPolicies.CiamExternal);

        // GET /api/v1/external/me — Returns external user's project access context
        externalGroup.MapGet("/me", ExternalUserContextEndpoint.Handle)
            .WithName("GetExternalUserContext")
            .WithSummary("Get authenticated external user's project access context")
            .WithDescription(
                "Returns the Contact's project access list with access levels. " +
                "Called by the external Secure Project Workspace SPA on startup to initialize navigation. " +
                "Requires a valid Entra External ID (CIAM) JWT (the 'Ciam' scheme).")
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

    // =========================================================================
    // Workforce collaboration endpoints — workforce Entra (Teams host) authentication
    // (ADR-028 Amendment A2 · teams-app-r1 FR-04)
    // =========================================================================

    private static void MapWorkforceCollaborationEndpoints(WebApplication app)
    {
        // Workforce DEFAULT JwtBearer scheme (NOT the CIAM pin) — Teams-host workforce tokens
        // authenticate here. RequireAuthorization() applies the default-scheme JWT policy; the
        // per-endpoint WorkforceCallerAuthorizationFilter then resolves the caller to a systemuser /
        // contact-only principal (ADR-028 A2 / FR-04) and DENIES an unresolvable caller. ADR-008:
        // per-endpoint filter, no global middleware. Tasks 021 (contact-anchored membership) + 022
        // (accessible-record-set enforcement) extend this group with data/download endpoints that
        // compose on the resolved principal.
        var collabGroup = app.MapGroup("/api/v1/collab")
            .WithTags("Workforce Collaboration")
            .RequireAuthorization();

        // GET /api/v1/collab/me — Returns the caller's resolved workforce principal (kind + ids).
        collabGroup.MapGet("/me", WorkforcePrincipalContextEndpoint.Handle)
            .WithName("GetWorkforcePrincipalContext")
            .WithSummary("Get the authenticated workforce caller's resolved collaboration principal")
            .WithDescription(
                "Resolves the workforce (Teams-host) caller via AAD oid → systemuser (with derived " +
                "contactId), else oid/verified-email → contact, else DENY. Returns the resolved " +
                "principal kind + ids. Requires a valid workforce Entra JWT (default scheme); an " +
                "unresolvable caller receives 401 (missing identity) or 403 (not provisioned).")
            .Produces<Dtos.WorkforcePrincipalContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddWorkforceCallerAuthorizationFilter();

        // GET /api/v1/collab/projects/{projectId}/documents/{documentId}/content — broker-only
        // document download for ALL workforce collaboration principals (teams-app-r1 FR-07 / task 030).
        // Authz-before-stream, no Graph pointer to the client, app-only SPE (no OBO on any plane).
        //
        // Filter order is load-bearing (filters run in the order added — outermost first):
        //   1. AddWorkforceCallerAuthorizationFilter()  — resolves the WorkforcePrincipal (task 020)
        //   2. AddAccessibleRecordSetAuthorizationFilter("sprk_project", "projectId") — enforces
        //      project ∈ accessible(principal) (task 022) and DENIES (403, zero bytes) a non-member
        //      BEFORE the handler — hence before any SPE pointer resolution or byte streaming.
        // The handler then adds document→project scoping and streams app-only via SpeFileStore.
        collabGroup.MapGet(
                "/projects/{projectId:guid}/documents/{documentId:guid}/content",
                WorkforceCollaborationDownloadEndpoint.DownloadDocumentContent)
            .WithName("DownloadWorkforceCollaborationDocument")
            .WithSummary("Download a Secure Project document's content for a workforce collaboration principal")
            .WithDescription(
                "Streams a Secure Project document's bytes to a workforce-authenticated collaboration " +
                "caller (systemuser / contact-with-grant / contact-with-standing-grant). Broker-only " +
                "(app-only SPE, no OBO on any plane) with authz-before-stream: project access is enforced " +
                "by the accessible-record-set filter and document→project scoping by the handler BEFORE " +
                "any bytes stream. A non-member receives 403 with no bytes; no driveId/itemId or other " +
                "Graph pointer ever reaches the client (success or error).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .AddWorkforceCallerAuthorizationFilter()
            .AddAccessibleRecordSetAuthorizationFilter("sprk_project", "projectId");
    }
}
