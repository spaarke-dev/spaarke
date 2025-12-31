using System.Security.Claims;
using System.Text.Json;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Ai;

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
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
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
/// </remarks>
public class AiAuthorizationFilter : IEndpointFilter
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<AiAuthorizationFilter>? _logger;

    public AiAuthorizationFilter(IAuthorizationService authorizationService, ILogger<AiAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract Azure AD Object ID from claims.
        // DataverseAccessDataSource requires the 'oid' claim to lookup user in Dataverse.
        // Fallback chain matches other authorization filters in the codebase.
        var userId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

        // Authorize access to all requested documents
        foreach (var documentId in documentIds)
        {
            var authContext = new AuthorizationContext
            {
                UserId = userId,
                ResourceId = documentId.ToString(),
                Operation = "read",
                CorrelationId = httpContext.TraceIdentifier
            };

            try
            {
                var result = await _authorizationService.AuthorizeAsync(authContext);

                if (!result.IsAllowed)
                {
                    _logger?.LogWarning(
                        "AI authorization denied: User {UserId} lacks read access to document {DocumentId}",
                        userId, documentId);

                    return ProblemDetailsHelper.Forbidden(result.ReasonCode);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "AI authorization failed for user {UserId} on document {DocumentId}",
                    userId, documentId);

                return Results.Problem(
                    statusCode: 500,
                    title: "Authorization Error",
                    detail: "An error occurred during authorization",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
            }
        }

        _logger?.LogDebug(
            "AI authorization granted: User {UserId} authorized for {Count} document(s)",
            userId, documentIds.Count);

        return await next(context);
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
