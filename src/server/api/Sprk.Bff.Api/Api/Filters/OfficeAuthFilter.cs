using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding OfficeAuthFilter to endpoints.
/// </summary>
public static class OfficeAuthFilterExtensions
{
    /// <summary>
    /// Adds Office authentication filter that validates user identity from claims.
    /// Returns 401 Unauthorized if user identity cannot be extracted.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddOfficeAuthFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<OfficeAuthFilter>>();
            var filter = new OfficeAuthFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user authentication for Office endpoints.
/// Extracts user identity from Azure AD claims and stores in HttpContext.Items for downstream use.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for authorization.
/// All Office endpoints require authenticated users with valid Azure AD tokens.
/// </para>
/// <para>
/// Claim extraction priority:
/// 1. Azure AD 'oid' (object identifier) claim
/// 2. Standard 'http://schemas.microsoft.com/identity/claims/objectidentifier' claim
/// 3. Standard NameIdentifier claim
/// 4. OIDC 'sub' claim
/// </para>
/// </remarks>
public class OfficeAuthFilter : IEndpointFilter
{
    private readonly ILogger<OfficeAuthFilter>? _logger;

    // Azure AD claim types
    private const string OidClaimType = "oid";
    private const string AltOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    // HttpContext.Items keys for downstream use
    public const string UserIdKey = "Office.UserId";
    public const string TenantIdKey = "Office.TenantId";
    public const string UserEmailKey = "Office.UserEmail";

    public OfficeAuthFilter(ILogger<OfficeAuthFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Check if user is authenticated
        if (user.Identity?.IsAuthenticated != true)
        {
            _logger?.LogWarning(
                "Office authorization denied: User is not authenticated. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Authentication required for Office endpoints",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_001",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract user ID from claims
        var userId = ExtractUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "Office authorization denied: No user identifier found in claims. " +
                "Claims present: {ClaimTypes}. CorrelationId: {CorrelationId}",
                string.Join(", ", user.Claims.Select(c => c.Type)),
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found in authentication token",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_002",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract tenant ID from claims (for multi-tenant scenarios)
        var tenantId = user.FindFirst(TenantIdClaimType)?.Value
            ?? user.FindFirst(AltTenantIdClaimType)?.Value;

        // Extract user email for logging/audit
        var userEmail = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("email")?.Value;

        // Store extracted values in HttpContext.Items for downstream use
        httpContext.Items[UserIdKey] = userId;
        if (!string.IsNullOrEmpty(tenantId))
        {
            httpContext.Items[TenantIdKey] = tenantId;
        }
        if (!string.IsNullOrEmpty(userEmail))
        {
            httpContext.Items[UserEmailKey] = userEmail;
        }

        _logger?.LogDebug(
            "Office authentication successful for user {UserId} (tenant: {TenantId}, email: {UserEmail}). " +
            "CorrelationId: {CorrelationId}",
            userId, tenantId ?? "N/A", userEmail ?? "N/A", httpContext.TraceIdentifier);

        return await next(context);
    }

    /// <summary>
    /// Resolves the caller's Entra object id, or <see langword="null"/> when the caller cannot be
    /// identified (the filter then answers 401).
    /// </summary>
    /// <remarks>
    /// <para><b>The NameIdentifier and sub fallbacks were removed on 2026-08-27.</b> The value this
    /// returns is stored in <see cref="UserIdKey"/> and consumed as the caller identity by
    /// <c>OfficeDocumentAccessFilter</c>, <c>EntityAccessFilter</c> and <c>JobOwnershipFilter</c> —
    /// all of which authorize against Dataverse, which matches on
    /// <c>systemuser.azureactivedirectoryobjectid</c>. Under inbound claim mapping both fallbacks
    /// resolve the same pairwise <c>sub</c>, matching no systemuser.</para>
    /// <para>Contrast <c>OfficeRateLimitFilter</c>, which legitimately accepts <c>sub</c> because a
    /// rate-limit partition only needs stability, not resolvability. Same claim, different purpose —
    /// which is why the two now call differently named resolvers instead of sharing one chain.</para>
    /// </remarks>
    private static string? ExtractUserId(ClaimsPrincipal user)
        => CallerResolution.ResolveObjectId(user);
}
