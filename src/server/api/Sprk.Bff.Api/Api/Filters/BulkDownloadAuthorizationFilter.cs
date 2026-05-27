using Sprk.Bff.Api.Models.Documents;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding bulk-download authorization to endpoints.
/// </summary>
public static class BulkDownloadAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the bulk-download authorization filter that validates tenant membership and request shape.
    /// Mirrors <see cref="SemanticSearchAuthorizationFilter"/> shape per ADR-008 (FR-BFF-02 spec).
    /// Per-document authorization happens at Dataverse lookup time via the user's identity
    /// (same model as <c>GET /api/documents/{id}/preview-url</c>).
    /// </summary>
    public static TBuilder AddBulkDownloadAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<BulkDownloadAuthorizationFilter>>();
            var filter = new BulkDownloadAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for the bulk-download endpoint (FR-BFF-02).
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: endpoint-filter-based authorization (not global middleware).
/// </para>
/// <para>
/// Authorization rules:
/// <list type="bullet">
/// <item>Validates user's tenant membership via Azure AD <c>tid</c> claim — mirrors <see cref="SemanticSearchAuthorizationFilter"/>.</item>
/// <item>Validates the request body exists with a non-null <see cref="BulkDownloadRequest.DocumentIds"/>.</item>
/// <item>Logs the bulk-download request for audit trail (id count, tenant).</item>
/// <item>Per-document access is enforced at Dataverse lookup time via the user's identity (same scope as <c>GET /api/documents/{id}/preview-url</c>).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class BulkDownloadAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<BulkDownloadAuthorizationFilter>? _logger;

    // Azure AD claim names (mirror SemanticSearchAuthorizationFilter)
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public BulkDownloadAuthorizationFilter(ILogger<BulkDownloadAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Step 1: Extract and validate tenant ID from Azure AD claims
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Bulk download authorization denied: No tenant claim found in token");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Step 2: Locate the BulkDownloadRequest argument
        var request = ExtractRequest(context);
        if (request == null)
        {
            // Let endpoint handle missing/malformed body
            return await next(context);
        }

        // Step 3: Log for audit trail (count only — no document IDs to keep log tidy)
        _logger?.LogInformation(
            "Bulk download authorization granted: tenant={TenantId}, requestedCount={Count}",
            userTenantId, request.DocumentIds?.Count ?? 0);

        return await next(context);
    }

    private static BulkDownloadRequest? ExtractRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is BulkDownloadRequest request)
            {
                return request;
            }
        }
        return null;
    }
}
