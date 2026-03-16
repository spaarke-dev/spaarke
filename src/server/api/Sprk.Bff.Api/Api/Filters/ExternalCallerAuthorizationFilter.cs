using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that validates Power Pages portal-issued OAuth tokens and resolves
/// the authenticated Contact's project participation data.
///
/// Enforces that:
///   - The token is a valid portal-issued JWT (not an internal Azure AD token)
///   - The Contact has at least one active sprk_externalrecordaccess record
///   - Access data is cached in Redis with 60-second TTL (ADR-009)
///
/// On success: sets <see cref="ExternalCallerContext"/> on HttpContext.Items for downstream handlers.
/// On failure: returns 401 (invalid/missing token) or 403 (no active participation records).
///
/// ADR-008: Endpoint filter pattern — no global middleware.
/// ADR-009: Redis-first access data caching with 60s TTL.
/// ADR-010: Concrete type injection — no unnecessary interfaces.
/// </summary>
public static class ExternalCallerAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds external caller authorization to an endpoint.
    /// Validates portal JWT token and resolves Contact participation data.
    /// </summary>
    public static TBuilder AddExternalCallerAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var tokenValidator = context.HttpContext.RequestServices.GetRequiredService<PortalTokenValidator>();
            var participationService = context.HttpContext.RequestServices.GetRequiredService<ExternalParticipationService>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ExternalCallerAuthorizationFilter>>();

            var filter = new ExternalCallerAuthorizationFilter(tokenValidator, participationService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

public class ExternalCallerAuthorizationFilter : IEndpointFilter
{
    private readonly PortalTokenValidator _tokenValidator;
    private readonly ExternalParticipationService _participationService;
    private readonly ILogger<ExternalCallerAuthorizationFilter> _logger;

    public ExternalCallerAuthorizationFilter(
        PortalTokenValidator tokenValidator,
        ExternalParticipationService participationService,
        ILogger<ExternalCallerAuthorizationFilter> logger)
    {
        _tokenValidator = tokenValidator;
        _participationService = participationService;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract Bearer token
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("[EXT-AUTH] Missing or invalid Authorization header");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Portal authentication token required",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var rawToken = authHeader["Bearer ".Length..].Trim();

        // Step 1: Validate portal token
        var validationResult = await _tokenValidator.ValidateAsync(rawToken, httpContext.RequestAborted);

        if (!validationResult.IsPortalToken)
        {
            // Not a portal token — could be an internal caller hitting the wrong endpoint
            _logger.LogWarning("[EXT-AUTH] Non-portal token presented to external endpoint");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "This endpoint requires a portal-issued authentication token",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("[EXT-AUTH] Portal token validation failed: {Error}", validationResult.Error);
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Portal token is invalid or expired",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Step 2: Resolve Contact ID
        Guid contactId;
        if (validationResult.IsContactGuid)
        {
            contactId = validationResult.ContactId;
        }
        else
        {
            // Sub claim is an email — look up via adx_externalidentity
            var resolved = await _participationService.ResolveContactByEmailAsync(
                validationResult.Email ?? validationResult.ContactIdClaim!,
                httpContext.RequestAborted);

            if (!resolved.HasValue)
            {
                _logger.LogWarning("[EXT-AUTH] Cannot resolve Contact for email {Email}",
                    validationResult.Email);
                return ProblemDetailsHelper.Forbidden("sdap.access.deny.contact_not_found");
            }

            contactId = resolved.Value;
        }

        // Step 3: Load participation data (Redis cache → Dataverse fallback)
        var participations = await _participationService.GetParticipationsAsync(
            contactId, httpContext.RequestAborted);

        if (participations.Count == 0)
        {
            _logger.LogWarning(
                "[EXT-AUTH] Contact {ContactId} has no active participation records — access denied",
                contactId);
            return ProblemDetailsHelper.Forbidden("sdap.access.deny.no_participation");
        }

        // Step 4: Store ExternalCallerContext on HttpContext for downstream handlers
        var callerContext = new ExternalCallerContext
        {
            ContactId = contactId,
            Email = validationResult.Email ?? string.Empty,
            Participations = participations
        };

        httpContext.Items[ExternalCallerContext.HttpContextItemsKey] = callerContext;

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} authenticated with {Count} active project participations",
            contactId, participations.Count);

        return await next(context);
    }
}
