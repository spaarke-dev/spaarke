using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding AI authorization to endpoints.
/// </summary>
public static class AiAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds AI document authorization to an endpoint.
    /// Reads documentId from request body and verifies read access.
    /// </summary>
    public static TBuilder AddAiAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<AiAuthorizationFilter>>();
            var filter = new AiAuthorizationFilter(authService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for AI endpoints.
/// Validates user has read access to the document being analyzed.
/// Extracts documentId from request body (DocumentAnalysisRequest or batch).
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// Uses IAiAuthorizationService for FullUAC authorization via RetrievePrincipalAccess.
/// </remarks>
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<AiAuthorizationFilter>? _logger;

    public AiAuthorizationFilter(IAiAuthorizationService authorizationService, ILogger<AiAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var user = httpContext.User;

        // Extract Azure AD Object ID from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user in Dataverse.
        // Fallback chain matches other authorization filters in the codebase.
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Extract document IDs from request arguments
        var documentIds = ExtractDocumentIds(context);
        if (documentIds.Count == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "No document identifier found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var result = await _authorizationService.AuthorizeAsync(
                user,
                documentIds,
                httpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[AI-AUTH-FILTER] Document access DENIED: DocumentCount={Count}, Reason={Reason}",
                    documentIds.Count,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to one or more documents",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[AI-AUTH-FILTER] Document access GRANTED: DocumentCount={Count}",
                documentIds.Count);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AI-AUTH-FILTER] Authorization check failed with exception");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Extract document IDs from request arguments.
    /// Supports both single DocumentAnalysisRequest and batch IEnumerable&lt;DocumentAnalysisRequest&gt;.
    /// </summary>
    private static List<Guid> ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        var documentIds = new List<Guid>();

        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                case DocumentAnalysisRequest request:
                    documentIds.Add(request.DocumentId);
                    break;

                case IEnumerable<DocumentAnalysisRequest> requests:
                    documentIds.AddRange(requests.Select(r => r.DocumentId));
                    break;

                case Guid documentId when documentId != Guid.Empty:
                    documentIds.Add(documentId);
                    break;
            }
        }

        return documentIds.Distinct().ToList();
    }
}
