using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that resolves the authenticated Azure AD user's Contact record
/// and validates that they have active project participation data.
///
/// External users authenticate via Azure AD B2B guest accounts (MSAL in the SPA).
/// The Azure AD JWT is validated by ASP.NET Core's authentication middleware before
/// this filter runs — the filter reads the validated claims and resolves participation.
///
/// Enforces that:
///   - The caller has an authenticated Azure AD identity (validated by middleware)
///   - The Contact exists in Dataverse (resolved by email claim)
///   - The Contact has at least one active sprk_externalrecordaccess record
///   - Access data is cached in Redis with 60-second TTL (ADR-009)
///
/// On success: sets <see cref="ExternalCallerContext"/> on HttpContext.Items for downstream handlers.
/// On failure: returns 401 (unauthenticated) or 403 (no active participation records).
///
/// ADR-008: Endpoint filter pattern — no global middleware.
/// ADR-009: Redis-first access data caching with 60s TTL.
/// ADR-010: Concrete type injection — no unnecessary interfaces.
/// </summary>
public static class ExternalCallerAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds external caller authorization to an endpoint.
    /// Resolves Contact and participation data from the authenticated Azure AD identity.
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

        // Step 1: Require authenticated Azure AD identity.
        // JWT validation is performed by ASP.NET Core middleware (RequireAuthorization on route group).
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogDebug("[EXT-AUTH] Request is not authenticated");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Authentication required",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Step 2: Extract email from Azure AD token claims.
        // For B2B guests: 'email' claim = the external user's real email (not the #EXT# UPN).
        var email = httpContext.User.FindFirst("email")?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("[EXT-AUTH] Token missing email/preferred_username claim");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Token is missing required email claim",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Step 3: Resolve Contact by email (contacts.emailaddress1)
        var contactId = await _participationService.ResolveContactByEmailAsync(email, httpContext.RequestAborted);
        if (!contactId.HasValue)
        {
            _logger.LogWarning("[EXT-AUTH] Cannot resolve Contact for email {Email}", email);
            return ProblemDetailsHelper.Forbidden("sdap.access.deny.contact_not_found");
        }

        // Step 4: Load participation data (Redis cache → Dataverse fallback)
        var participations = await _participationService.GetParticipationsAsync(contactId.Value, httpContext.RequestAborted);
        if (participations.Count == 0)
        {
            _logger.LogWarning(
                "[EXT-AUTH] Contact {ContactId} has no active participation records — access denied",
                contactId.Value);
            return ProblemDetailsHelper.Forbidden("sdap.access.deny.no_participation");
        }

        // Step 5: Store ExternalCallerContext for downstream handlers
        httpContext.Items[ExternalCallerContext.HttpContextItemsKey] = new ExternalCallerContext
        {
            ContactId = contactId.Value,
            Email = email,
            Participations = participations
        };

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} ({Email}) authenticated with {Count} active project participations",
            contactId.Value, email, participations.Count);

        return await next(context);
    }
}
