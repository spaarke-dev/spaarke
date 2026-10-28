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
///                             ADR-028 A9 (r3 task 045): the policy also accepts the WorkforceHome scheme (a
///                             workforce contact's customer home-directory token); every route here is pinned by
///                             AuthenticationSchemeCensusTests.
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

        // POST /api/v1/external/registration — ADR-028 A9 (r3 task 047). Its OWN group: the ExternalCollaboration policy
        // WITHOUT CallerPrincipalAuthorizationFilter (the caller is by definition not yet resolved); the handler admits the
        // WorkforceHome scheme only.
        app.MapExternalRegistrationEndpoints();

        // POST /api/v1/external/workforce/link — ADR-028 A9 (r3 task 048). Its OWN group, as registration: a licensed user
        // signed in with their home account is not resolvable until the link exists. WorkforceHome scheme only.
        app.MapExternalWorkforceLinkEndpoints();

        // GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{recordId}/no-access — the per-record No Access
        // read (task 064, owner round 59 item 3). NOT on the management group: its DelegationRuleFilter demands Write for
        // every route, and the form banner (task 153) must answer callers who hold only Read. Each route carries the
        // route-record Read gate itself; the entries are added for a caller who also holds Write (owner O2).
        app.MapRecordNoAccessEndpoint();

        // POST /api/v1/external-access/remove-person · /reinstate-person — person-level administration (slice C3). NOT on the
        // management group: its delegation filter needs a record, and these routes name a person. Their own group carries the
        // external-access administrator filter instead.
        app.MapPersonAccessAdministrationEndpoints();
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
            .AddCallerPrincipalAuthorizationFilter()
            // The workforce privilege ceiling (unified-access-control-r2 task 186): every route below declares its
            // requirement set, or why it is checked in its handler or exempt (WorkforceCeilingEndpointFilter.cs). After the
            // principal filter, which it needs; before every route filter and handler, so it decides before any record read.
            .AddWorkforceCeilingFilter();

        // GET /api/v1/external/me — Returns the caller's project access context (either plane)
        externalGroup.MapGet("/me", ExternalUserContextEndpoint.Handle)
            .WithName("GetExternalUserContext")
            .ExemptFromWorkforceCeiling("Identity: the caller's own access context, read to start the SPA; no table operation.")
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
        // Returns the module-code list the external-spa widget registry gates tab visibility on: default
        // modules by server-resolved principal kind (A8: workforce → legal-front-door + policy-library; CIAM →
        // assigned-work, + legal-front-door when the partner switch is on), plus sprk_approlemodulemap extras. Distinct from
        // /me above (Tier-2 record access). Same group → same dual-scheme policy + CallerPrincipal filter.
        externalGroup.MapGet("/me/entitlements", MeEntitlementsEndpoint.Handle)
            .WithName("GetExternalUserEntitlements")
            .WorkforceCeilingInHandler("Module codes and intake types are intersected with the ceiling in the handler (review round 1 F2).")
            .WithSummary("Get the authenticated collaboration caller's Tier-1 module entitlement context")
            .WithDescription(
                "Returns the caller's entitled module codes (Tier-1): default modules by server-resolved " +
                "principal kind (workforce: legal-front-door + policy-library; CIAM: assigned-work, plus " +
                "legal-front-door when the partner service-request switch is on), plus App-Role→module map " +
                "(sprk_approlemodulemap) extras. Consumed by the external-spa widget registry " +
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

        // GET /api/v1/external/{root}/{id}/messages — the message thread regarding a record (r3 task 033, FR-04). Inherits
        // this group's policy + CallerPrincipalAuthorizationFilter; the record is checked against the caller's accessible
        // set (service requests: the caller's own) before an app-only read, and internal-only messages reach only
        // internal systemusers.
        externalGroup.MapExternalMessageEndpoints();

        // POST /api/v1/external/servicerequests — the Legal Front Door intake submit (r3 task 035, FR-05/FR-06). Inherits this
        // group's policy + CallerPrincipalAuthorizationFilter; gated server-side on the legal-front-door module entitlement
        // (the one ModuleEntitlementResolver check /me/entitlements reports), and creates the request app-only with the
        // caller's contact as sprk_requestedby.
        externalGroup.MapExternalIntakeEndpoints();

        // GET /api/v1/external/notifications · POST /api/v1/external/notifications/seen — the derived in-portal feed (r3 task
        // 037, FR-08). Inherits this group's policy + CallerPrincipalAuthorizationFilter; grants are re-checked against the
        // caller's accessible set, messages come only from records in it under the external read rules (no privileged, no
        // internal-only to a contact, not the caller's own), and the last-seen marker is Redis keyed by the principal id.
        externalGroup.MapExternalNotificationFeedEndpoints();

        // Contact-side Grant Access (unified-access-control-r2 task 140, owner C4 / Q2): a contact holding Collaborate or
        // Full Access grants colleagues of their OWN organization, at or below their own level, never organization-wide.
        // On THIS group because a contact can authenticate nowhere else (ADR-028 A3: one ExternalCollaboration group);
        // every route carries ContactGrantorAuthorizationFilter, which refuses a systemuser (Manage Access is theirs).
        externalGroup.MapContactGrantEndpoints();
    }

    // =========================================================================
    // Internal management endpoints — Azure AD authentication
    // =========================================================================

    private static void MapInternalManagementEndpoints(WebApplication app)
    {
        // FR-07 / finding A-6 (task 008): RequireAuthorization() alone asks only "are you anyone?".
        // Until this filter landed, every write on this group — mint a grant, revoke one, onboard a
        // CIAM identity, cascade-close a project, provision a business unit — was reachable by ANY
        // authenticated caller and then executed app-only. AddDelegationRuleFilter enforces owner
        // decision B-14 + round 89 (task 179): you may change who can access a record only if YOU hold
        // Write on it and the Share privilege on its table, evaluated as the caller (OBO), before the
        // handler runs.
        //
        // Group-level, not per-route, deliberately: DelegationRuleFilter denies any request whose
        // target record it cannot identify, so a route added to this group later is gated from its
        // first request rather than inheriting the hole this filter exists to close. See
        // DelegationRuleFilter's remarks for the request-type → target-record map.
        //
        // design.md §6 marks this the blocking prerequisite for the Manage Access PCF (task 065):
        // without it the "+ User" button is one-click privilege escalation on a confidential matter.
        var adminGroup = app.MapGroup("/api/v1/external-access")
            .WithTags("External Access Management")
            .RequireAuthorization()
            .AddDelegationRuleFilter();

        // POST /api/v1/external-access/grant — Grant Contact access to a Secure Project
        adminGroup.MapGrantExternalAccessEndpoint();

        // POST /api/v1/external-access/revoke — Revoke Contact access from a Secure Project
        adminGroup.MapRevokeExternalAccessEndpoint();

        // POST /api/v1/external-access/set-record-share-expiry — one expiry on every active share of a
        // record, all-or-nothing (spec FR-33, task 098: the Manage Access toolbar Expiration).
        adminGroup.MapSetRecordShareExpiryEndpoint();

        // GET /api/v1/external-access/can-manage-access — the delegation question, ASKED rather than guessed
        // (spec FR-07, task 118 / owner decision D-1 option C: the server's rule, with the server's fail
        // direction, is what the Manage Access affordance gates on). On this group deliberately: the answer is
        // the group filter's own verdict, so the client cannot be told something the enforcement point would
        // contradict. Detaching the filter would make it answer "yes" to everyone — see the endpoint's remarks.
        adminGroup.MapRecordAccessGateEndpoint();

        // GET /api/v1/external-access/record-grants — the record's active external grants for Manage Access's Current
        // Access (task 099a, #1010(a)). On this group so the delegation filter gates it with the /can-manage-access rule
        // (Write and Share on the record): the reader is the dialog's audience, whatever business unit owns the grant rows.
        adminGroup.MapRecordGrantsEndpoint();

        // POST /share-user · POST /unshare-user · GET /user-shares — internal system-user shares on a record
        // (spec FR-29, task 063): the server half of the Manage Access "+ User" picker (task 065). On this group so
        // they inherit the same delegation gate (Write and Share on the record) as every route above.
        adminGroup.MapInternalShareEndpoints();

        // POST /api/v1/external-access/no-access/enforce — enforce one No Access entry now (task 143, owner Q4 + R3:
        // "immediate on save"). On this group so the delegation filter gates it on Write on the ENTRY; it only removes.
        adminGroup.MapNoAccessEnforceEndpoint();

        // POST /assigned-access/sync · GET /assigned-access · POST /assigned-access/dismiss — the Assigned-To auto-grants
        // (task 142, owner Q5 + R3: the form save, the wizards and "Update Access" call the BFF; owner A3: suggestions on
        // secure records). On this group so the delegation filter gates each on the RECORD (sync: Write; list and dismiss: Write and Share).
        adminGroup.MapAssignedAccessEndpoints();

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

        // POST /api/v1/external-access/unsecure-project — reverse the Secure Project designation (task 061)
        adminGroup.MapUnsecureProjectEndpoint();
    }

}
