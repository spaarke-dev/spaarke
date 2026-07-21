using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that resolves the authenticated external (Entra External ID / CIAM) caller's
/// Dataverse Contact and project participation data from the already-validated JWT identity.
///
/// The "Ciam" JwtBearer scheme (task 020, pinned on /api/v1/external in task 021) validates the CIAM
/// JWT before this filter runs, so HttpContext.User is already populated. This filter resolves the
/// Dataverse Contact by the stable CIAM <c>oid</c> claim (Contact.sprk_externalobjectid) per ADR-028
/// Amendment A1, using email only as a first-login fallback that then binds the oid.
///
/// On success: sets <see cref="ExternalCallerContext"/> on HttpContext.Items for downstream handlers.
/// On failure: returns 401 (missing identity claims) or 403 (Contact not found in Dataverse).
///
/// ADR-008: Endpoint filter pattern — no global middleware.
/// ADR-009: Redis-first access data caching with 60s TTL (keyed under the CIAM tenant `tid`).
/// ADR-010: Concrete type injection — no unnecessary interfaces.
/// </summary>
public static class ExternalCallerAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds external caller authorization to an endpoint.
    /// Resolves the B2B guest's Contact and participation data from JWT identity claims.
    /// </summary>
    public static TBuilder AddExternalCallerAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var participationService = context.HttpContext.RequestServices.GetRequiredService<ExternalParticipationService>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ExternalCallerAuthorizationFilter>>();

            var filter = new ExternalCallerAuthorizationFilter(participationService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

public class ExternalCallerAuthorizationFilter : IEndpointFilter
{
    private readonly ExternalParticipationService _participationService;
    private readonly ILogger<ExternalCallerAuthorizationFilter> _logger;

    public ExternalCallerAuthorizationFilter(
        ExternalParticipationService participationService,
        ILogger<ExternalCallerAuthorizationFilter> logger)
    {
        _participationService = participationService;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Stable identity: the CIAM 'oid' (immutable directory object id) is the canonical link to the
        // Dataverse Contact (Contact.sprk_externalobjectid) per ADR-028 Amendment A1. Check both the raw
        // 'oid' claim and the mapped objectidentifier URI (claim mapping varies by scheme config).
        var oid = user.FindFirstValue("oid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        // Email/UPN is used only as a first-login fallback that then binds the oid onto the Contact.
        var email = user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("upn")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email");

        // Escalation (task 023): a CIAM token with neither oid nor a usable email cannot be resolved
        // or first-login-bound — reject rather than guess an identity.
        if (string.IsNullOrEmpty(oid) && string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("[EXT-AUTH] CIAM token missing both oid and email/UPN identity claims");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Identity token is missing required user identity claims",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Resolve the Dataverse Contact by stable oid (email as first-login fallback that binds the oid).
        // Once a Contact is bound to an oid, a mismatched email neither redirects resolution nor grants access.
        var contactId = await _participationService.ResolveExternalContactAsync(oid, email, httpContext.RequestAborted);

        if (!contactId.HasValue)
        {
            _logger.LogWarning(
                "[EXT-AUTH] Cannot resolve Dataverse Contact (oid present: {HasOid}, email present: {HasEmail})",
                !string.IsNullOrEmpty(oid), !string.IsNullOrEmpty(email));
            return ProblemDetailsHelper.Forbidden("sdap.access.deny.contact_not_found");
        }

        // Load active project participations (Redis cache → Dataverse fallback per ADR-009).
        // Empty participation list is allowed — authenticated users with no projects yet
        // can reach the workspace and see an empty state.
        var participations = await _participationService.GetParticipationsAsync(contactId.Value, httpContext.RequestAborted);

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} authenticated (oid-resolved: {ByOid}) — {Count} active project participations",
            contactId.Value, !string.IsNullOrEmpty(oid), participations.Count);

        // Store ExternalCallerContext on HttpContext for downstream handlers.
        httpContext.Items[ExternalCallerContext.HttpContextItemsKey] = new ExternalCallerContext
        {
            ContactId = contactId.Value,
            Email = email ?? string.Empty,
            Oid = oid,
            Participations = participations
        };

        return await next(context);
    }
}
