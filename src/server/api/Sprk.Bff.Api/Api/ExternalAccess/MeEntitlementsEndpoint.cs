using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// GET /api/v1/external/me/entitlements
///
/// Returns the authenticated collaboration caller's Tier-1 MODULE entitlement context — the module-code
/// list the external-spa widget registry gates tab visibility on (spec FR-07/FR-08/FR-09, owner Option B;
/// project task 072). Distinct from <c>GET /api/v1/external/me</c> (which returns the Tier-2 RECORD
/// access list): Tier-1 = "which module tabs", Tier-2 = "which records within a module".
///
/// The group-level <c>CallerPrincipalAuthorizationFilter</c> (task 025) has already authenticated the
/// caller on EITHER plane (CIAM external contact or workforce user) and stored the resolved
/// <see cref="CallerPrincipal"/> on <c>HttpContext.Items</c>. Principal-agnostic at the route level; the
/// resolver applies the A8 default-by-principal-kind rule (server-resolved kind: workforce →
/// <c>legal-front-door</c> + <c>policy-library</c>; CIAM → <c>assigned-work</c>, plus <c>legal-front-door</c>
/// when <c>ExternalAccess:PartnerServiceRequestsEnabled</c> is on); <c>sprk_approlemodulemap</c> rows are extras.
///
/// ADR-001: Minimal API. ADR-008: group filter (no global middleware). ADR-009: the resolver caches the
/// App-Role→module DATA, never an authorization decision. NFR-06: entitlement is UX-honest data, never
/// the security boundary (Tier-1 here + Tier-2 record scope are).
///
/// The workforce ceiling (unified-access-control-r2 task 186, review round 1 F2; design note §5: "/me/entitlements never
/// shows a tab the role forbids"): for a workforce-contact caller the module codes are intersected with the Workforce
/// role — a code whose table the role cannot Read (<see cref="WorkforceRoleCeilingGate.ModuleTables"/>) is dropped — and
/// the intake types are offered only when the role holds Create on <c>sprk_servicerequest</c>, the rule the submit route
/// enforces. An unavailable role shows nothing gated: still 200, so the SPA renders.
/// </summary>
public static class MeEntitlementsEndpoint
{
    internal static async Task<IResult> Handle(
        HttpContext httpContext,
        ModuleEntitlementResolver resolver,
        IDataversePrivilegeChecker privileges,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var caller = httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;
        if (caller is null)
        {
            logger.LogError(
                "[EXT-ENTITLE] CallerPrincipal not found in HttpContext.Items. Ensure " +
                "AddCallerPrincipalAuthorizationFilter() is applied to this group. TraceId={TraceId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        var entitlements = await resolver.ResolveAsync(caller, httpContext.User, ct);

        // The workforce ceiling (task 186, F2): null when it does not apply (a systemuser, a CIAM partner).
        var ceiling = await WorkforceRoleCeilingGate.CeilingForAsync(caller, privileges, ct).ConfigureAwait(false);
        if (ceiling is not null)
        {
            entitlements = entitlements
                .Where(code => !WorkforceRoleCeilingGate.ModuleTables.TryGetValue(code, out var table)
                               || WorkforceRoleCeilingGate.CanRead(ceiling, table))
                .ToList();
        }
        var plane = caller.Plane == CallerPrincipalPlane.CiamContact ? "ciam" : "workforce";
        var displayName = httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? caller.Email;

        logger.LogInformation(
            "[EXT-ENTITLE] Caller {ContactId} ({Plane}) entitled to {Count} module(s). TraceId={TraceId}",
            caller.ContactId, plane, entitlements.Count, httpContext.TraceIdentifier);

        // Per-type availability for the Legal Front Door tiles (owner decision 2026-10-12): the same rule the submit route
        // enforces, and only for a caller entitled to the module.
        // A workforce contact is offered a type only when the role also holds Create on the service request (task 186).
        IReadOnlyList<int> intakeTypes = entitlements.Contains(
                ModuleEntitlementResolver.LegalFrontDoorModule, StringComparer.OrdinalIgnoreCase)
            && (ceiling is null || (ceiling.IsAvailable
                                    && ceiling.Holds(WorkforceRoleCeilingGate.ServiceRequestTable, Spaarke.Dataverse.AccessRights.Create)))
            ? resolver.IntakeRequestTypesFor(caller)
            : Array.Empty<int>();

        return Results.Ok(new MeEntitlementsResponse(
            displayName ?? string.Empty, caller.Email, plane, entitlements, intakeTypes));
    }
}
