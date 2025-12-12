using System.Security.Claims;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding analysis authorization to endpoints.
/// </summary>
public static class AnalysisAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds authorization for analysis execute endpoint.
    /// Validates user has read access to all documents being analyzed.
    /// </summary>
    public static TBuilder AddAnalysisExecuteAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<AnalysisAuthorizationFilter>>();
            var filter = new AnalysisAuthorizationFilter(authService, logger, AuthorizationMode.DocumentAccess);
            return await filter.InvokeAsync(context, next);
        });
    }

    /// <summary>
    /// Adds authorization for analysis record access endpoints.
    /// Validates user has access to the analysis record (via underlying document).
    /// </summary>
    public static TBuilder AddAnalysisRecordAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
            var logger = context.HttpContext.RequestServices.GetService<ILogger<AnalysisAuthorizationFilter>>();
            var filter = new AnalysisAuthorizationFilter(authService, logger, AuthorizationMode.AnalysisAccess);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization mode for analysis endpoints.
/// </summary>
public enum AuthorizationMode
{
    /// <summary>Authorize based on document IDs in request body.</summary>
    DocumentAccess,

    /// <summary>Authorize based on analysis record ownership.</summary>
    AnalysisAccess
}

/// <summary>
/// Authorization filter for Analysis endpoints.
/// Validates user has access to documents or analysis records.
/// </summary>
/// <remarks>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
///
/// Authorization strategy:
/// - /execute: User must have read access to all documentIds in request
/// - /{analysisId}/*: User must own the analysis or have access to its source document
/// </remarks>
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<AnalysisAuthorizationFilter>? _logger;
    private readonly AuthorizationMode _mode;

    public AnalysisAuthorizationFilter(
        IAuthorizationService authorizationService,
        ILogger<AnalysisAuthorizationFilter>? logger,
        AuthorizationMode mode)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
        _mode = mode;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract user ID from claims
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        return _mode switch
        {
            AuthorizationMode.DocumentAccess => await AuthorizeDocumentAccessAsync(context, userId, next),
            AuthorizationMode.AnalysisAccess => await AuthorizeAnalysisAccessAsync(context, userId, next),
            _ => await next(context)
        };
    }

    /// <summary>
    /// Authorize access to documents in the request body.
    /// Used for /execute endpoint.
    /// </summary>
    private async ValueTask<object?> AuthorizeDocumentAccessAsync(
        EndpointFilterInvocationContext context,
        string userId,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

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
                        "Analysis authorization denied: User {UserId} lacks read access to document {DocumentId}",
                        userId, documentId);

                    return ProblemDetailsHelper.Forbidden(result.ReasonCode);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Analysis authorization failed for user {UserId} on document {DocumentId}",
                    userId, documentId);

                return Results.Problem(
                    statusCode: 500,
                    title: "Authorization Error",
                    detail: "An error occurred during authorization",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
            }
        }

        _logger?.LogDebug(
            "Analysis execute authorization granted: User {UserId} authorized for {Count} document(s)",
            userId, documentIds.Count);

        return await next(context);
    }

    /// <summary>
    /// Authorize access to an analysis record.
    /// Used for /continue, /save, /export, GET endpoints.
    /// </summary>
    /// <remarks>
    /// Phase 1: Uses in-memory analysis store, so we skip authorization.
    /// Phase 2: Will validate user owns the analysis or has access to source document.
    /// </remarks>
    private async ValueTask<object?> AuthorizeAnalysisAccessAsync(
        EndpointFilterInvocationContext context,
        string userId,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Extract analysisId from route
        if (!httpContext.Request.RouteValues.TryGetValue("analysisId", out var analysisIdValue) ||
            !Guid.TryParse(analysisIdValue?.ToString(), out var analysisId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Analysis identifier not found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        // Phase 1 Scaffolding: Skip Dataverse lookup, allow access
        // In Phase 2, this will:
        // 1. Look up sprk_analysis record
        // 2. Check if ownerid matches userId
        // 3. If not owner, check if user has access to source document
        _logger?.LogDebug(
            "Analysis record authorization: User {UserId} accessing analysis {AnalysisId} (Phase 1: skipping Dataverse lookup)",
            userId, analysisId);

        // TODO Task 032: Implement full authorization when Dataverse integration is complete
        // var analysis = await _dataverseService.GetAnalysisAsync(analysisId);
        // if (analysis.OwnerId != userId)
        // {
        //     // Check document access as fallback
        //     var authContext = new AuthorizationContext { UserId = userId, ResourceId = analysis.DocumentId, Operation = "read" };
        //     var result = await _authorizationService.AuthorizeAsync(authContext);
        //     if (!result.IsAllowed) return ProblemDetailsHelper.Forbidden(result.ReasonCode);
        // }

        return await next(context);
    }

    /// <summary>
    /// Extract document IDs from request arguments.
    /// Supports AnalysisExecuteRequest with DocumentIds array.
    /// </summary>
    private static List<Guid> ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        var documentIds = new List<Guid>();

        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                case AnalysisExecuteRequest request when request.DocumentIds.Length > 0:
                    documentIds.AddRange(request.DocumentIds);
                    break;

                case Guid documentId when documentId != Guid.Empty:
                    documentIds.Add(documentId);
                    break;
            }
        }

        return documentIds.Distinct().ToList();
    }
}
