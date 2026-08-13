using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

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
/// resolver applies the Option-B per-plane term (workforce App-Role → <c>sprk_approlemodulemap</c>; CIAM
/// blanket outside-counsel set).
///
/// ADR-001: Minimal API. ADR-008: group filter (no global middleware). ADR-009: the resolver caches the
/// App-Role→module DATA, never an authorization decision. NFR-06: entitlement is UX-honest data, never
/// the security boundary (Tier-1 here + Tier-2 record scope are).
/// </summary>
public static class MeEntitlementsEndpoint
{
    public static async Task<IResult> Handle(
        HttpContext httpContext,
        ModuleEntitlementResolver resolver,
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
        var plane = caller.Plane == CallerPrincipalPlane.CiamContact ? "ciam" : "workforce";
        var displayName = httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? caller.Email;

        logger.LogInformation(
            "[EXT-ENTITLE] Caller {ContactId} ({Plane}) entitled to {Count} module(s). TraceId={TraceId}",
            caller.ContactId, plane, entitlements.Count, httpContext.TraceIdentifier);

        return Results.Ok(new MeEntitlementsResponse(displayName ?? string.Empty, caller.Email, plane, entitlements));
    }
}
