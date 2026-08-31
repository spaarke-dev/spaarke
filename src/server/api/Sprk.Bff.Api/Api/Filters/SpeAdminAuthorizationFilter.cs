using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding SpeAdminAuthorizationFilter to endpoints.
/// </summary>
public static class SpeAdminAuthorizationFilterExtensions
{
    /// <summary>
    /// Restricts the endpoint or route group to users holding a Spaarke admin app role.
    /// Returns a 403 ProblemDetails response for non-admin users.
    /// </summary>
    /// <remarks>
    /// Follows ADR-008: Use endpoint filters for authorization — no global auth middleware.
    /// Admin check mirrors the "SystemAdmin" policy in AuthorizationModule.cs.
    /// </remarks>
    public static TBuilder AddSpeAdminAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<SpeAdminAuthorizationFilter>>();
            var filter = new SpeAdminAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter restricting SPE Admin endpoints to callers holding a <b>Spaarke</b> admin app
/// role. This is <b>layer 1 of two independent authorization layers</b> — see the remarks.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE TWO LAYERS ARE NOT THE SAME THING, AND PASSING ONE SAYS NOTHING ABOUT THE OTHER.</b>
/// Conflating them is what produced the misleading denials this filter now avoids
/// (spec FR-B03).
/// </para>
///
/// <list type="table">
///   <listheader>
///     <term>Layer</term><description>What it gates, who decides, and how it is observed</description>
///   </listheader>
///   <item>
///     <term><b>1 — Spaarke admin app role</b> (this filter)</term>
///     <description>
///       Gates whether the caller may reach the <c>/api/spe</c> surface at all. Granted by Spaarke, as
///       an <b>app role</b> on the BFF registration, and observed directly in the access token's
///       <c>roles</c> claim. Denial is authoritative and is reported as
///       <c>sdap.access.deny.role_insufficient</c>.
///     </description>
///   </item>
///   <item>
///     <term><b>2 — Entra directory role</b> (NOT checked here)</term>
///     <description>
///       Gates what Microsoft Graph will return for container-type operations. Granted by a Microsoft
///       Entra tenant administrator as a <b>directory role</b> — "SharePoint Embedded Administrator" or
///       "Global Administrator". It is <b>not observable from the token</b> (see below), so it is
///       neither checked nor asserted here. It surfaces where it is authoritative: as a Graph 403,
///       translated by <c>ContainerTypeEndpoints</c>.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// <b>Why layer 2 is not checked here — measured, not assumed (2026-08-22).</b> Entra directory roles
/// reach a token only through the <c>wids</c> claim, which Entra emits only when the resource
/// application sets <c>groupMembershipClaims</c> to <c>All</c> or <c>DirectoryRole</c>. On
/// <c>SDAP-BFF-SPE-API</c> that property is <c>null</c>. This was confirmed with a positive control: a
/// real access token was issued for <c>aud = api://{bff}</c> to a user who <b>is</b> a member of the
/// tenant's SharePoint Embedded Administrator role, and the token carried <b>no <c>wids</c> claim at
/// all</b> — while <c>roles</c> (layer 1) was present.
/// </para>
/// <para>
/// The consequence is the load-bearing one: <b>absence of the claim does not mean absence of the
/// role.</b> Any check here would tell genuine role holders they lack the role — manufacturing exactly
/// the misleading-error defect this project exists to remove. So <b>do not "complete" this filter by
/// adding a <c>wids</c> check.</b> It cannot fire as the registration stands, and if it ever did it
/// would still be a claim about tenant-wide roles only. Enabling <c>groupMembershipClaims</c> is an
/// operator decision affecting every token issued for the BFF across all Spaarke client surfaces — it
/// is recorded in <c>notes/task-012-completion.md</c>, not taken unilaterally here.
/// </para>
///
/// <para>ADR-008: authorization lives in endpoint filters; each route group applies its own. No global
/// auth middleware. ADR-019: denials are RFC 7807 ProblemDetails.</para>
/// </remarks>
public class SpeAdminAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<SpeAdminAuthorizationFilter>? _logger;

    // Deny codes follow {domain}.{area}.{action}.{reason}.
    // Layer 1 denial — the caller lacks the Spaarke admin app role.
    private const string DenyCode = "sdap.access.deny.role_insufficient";

    // Distinct from the above so the client can tell "not signed in" from "signed in, not an admin"
    // instead of rendering one message for both.
    private const string UnauthenticatedCode = "sdap.access.deny.unauthenticated";

    /// <summary>
    /// The Spaarke-granted app roles that satisfy layer 1. These are app roles on the BFF
    /// registration — NOT Entra directory roles.
    /// </summary>
    private static readonly string[] AdminAppRoles = ["Admin", "SystemAdmin"];

    public SpeAdminAuthorizationFilter(ILogger<SpeAdminAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Verify the user identity is present (base authentication must already have run)
        // Order matters: NameIdentifier already carries `sub` under inbound claim mapping, so both
        // tails below were unreachable and this resolved to `sub`. See CallerResolution.
        var userId = CallerResolution.ResolveObjectId(httpContext.User);

        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "SPE Admin authorization denied: No user identity found in token. " +
                "Request path: {Path}", httpContext.Request.Path);

            // 401, not 403 — nothing is known about this caller's roles, so saying anything about
            // roles would be a guess. Sign-in is the action; a role grant is not.
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "You are not signed in, or your session has expired. Sign in again to continue.",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = UnauthenticatedCode,
                    ["traceId"] = httpContext.TraceIdentifier
                });
        }

        // Layer 1 ONLY: the Spaarke admin app role, read from the token's `roles` claim.
        // IsInRole covers the mapped role claim type; the explicit HasClaim covers tokens where
        // "roles" arrives unmapped.
        var isAdmin = AdminAppRoles.Any(role =>
            httpContext.User.IsInRole(role)
            || httpContext.User.HasClaim(c => c.Type == "roles" && c.Value == role));

        if (!isAdmin)
        {
            _logger?.LogWarning(
                "SPE Admin authorization denied: User {UserId} lacks a Spaarke admin app role " +
                "({Roles}). Request path: {Path}",
                userId, string.Join(" or ", AdminAppRoles), httpContext.Request.Path);

            // State precisely what was checked and what grants it. This says nothing about Entra
            // directory roles — layer 2 is not observable here, and claiming otherwise would be the
            // misleading-error defect (see the class remarks).
            return ProblemDetailsHelper.Forbidden(
                DenyCode,
                detail:
                    "Your account is signed in but does not have the Spaarke administrator permission " +
                    "required for SharePoint Embedded administration. Ask a Spaarke administrator to " +
                    "assign you the Admin or SystemAdmin role. This is a Spaarke permission and is " +
                    "separate from any Microsoft Entra directory role you may hold.",
                traceId: httpContext.TraceIdentifier);
        }

        _logger?.LogDebug(
            "SPE Admin authorization granted for user {UserId}. Request path: {Path}",
            userId, httpContext.Request.Path);

        return await next(context);
    }
}
