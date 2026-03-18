using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using System.Security.Claims;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Endpoint filter that resolves the authenticated Azure AD B2B guest's
/// Dataverse Contact and project participation data from the already-validated JWT identity.
///
/// ASP.NET Core's Microsoft.Identity.Web middleware (AddMicrosoftIdentityWebApi) validates
/// the Azure AD JWT before this filter runs, so HttpContext.User is already populated.
/// This filter resolves the Dataverse Contact record by email claim and loads participation data.
///
/// On success: sets <see cref="ExternalCallerContext"/> on HttpContext.Items for downstream handlers.
/// On failure: returns 401 (missing claims) or 403 (Contact not found in Dataverse).
///
/// ADR-008: Endpoint filter pattern — no global middleware.
/// ADR-009: Redis-first access data caching with 60s TTL.
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

        // Azure AD B2B guest tokens include the user's home-tenant UPN as preferred_username.
        // This is the email the guest uses to authenticate (e.g. alice@contoso.com).
        // Fall back to upn, standard email, and the simple "email" claim if preferred_username is absent.
        var email = user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("upn")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email");

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("[EXT-AUTH] No email/UPN claim found in Azure AD B2B token");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Identity token is missing required user identity claims",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Resolve the Dataverse Contact ID from the email claim.
        // B2B guest users must have a Contact record in Dataverse with a matching emailaddress1 field.
        var contactId = await _participationService.ResolveContactByEmailAsync(email, httpContext.RequestAborted);

        if (!contactId.HasValue)
        {
            _logger.LogWarning("[EXT-AUTH] Cannot resolve Dataverse Contact for email {Email}", email);
            return ProblemDetailsHelper.Forbidden("sdap.access.deny.contact_not_found");
        }

        // Load active project participations (Redis cache → Dataverse fallback per ADR-009).
        // Empty participation list is allowed — authenticated users with no projects yet
        // can reach the workspace and see an empty state.
        var participations = await _participationService.GetParticipationsAsync(contactId.Value, httpContext.RequestAborted);

        _logger.LogInformation(
            "[EXT-AUTH] Contact {ContactId} ({Email}) authenticated — {Count} active project participations",
            contactId.Value, email, participations.Count);

        // Store ExternalCallerContext on HttpContext for downstream handlers.
        httpContext.Items[ExternalCallerContext.HttpContextItemsKey] = new ExternalCallerContext
        {
            ContactId = contactId.Value,
            Email = email,
            Participations = participations
        };

        return await next(context);
    }
}
