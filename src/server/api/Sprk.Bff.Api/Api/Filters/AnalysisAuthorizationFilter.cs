using System.Security.Claims;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

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
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
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
            var authService = context.HttpContext.RequestServices.GetRequiredService<IAiAuthorizationService>();
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
///
/// Uses IAiAuthorizationService for FullUAC authorization via RetrievePrincipalAccess.
/// </remarks>
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    private readonly IAiAuthorizationService _authorizationService;
    private readonly ILogger<AnalysisAuthorizationFilter>? _logger;
    private readonly AuthorizationMode _mode;

    public AnalysisAuthorizationFilter(
        IAiAuthorizationService authorizationService,
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
        var user = httpContext.User;

        // Verify user has identity claims
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

        return _mode switch
        {
            AuthorizationMode.DocumentAccess => await AuthorizeDocumentAccessAsync(context, user, next),
            AuthorizationMode.AnalysisAccess => await AuthorizeAnalysisAccessAsync(context, user, next),
            _ => await next(context)
        };
    }

    /// <summary>
    /// Authorize access to documents in the request body.
    /// Used for /execute endpoint.
    /// </summary>
    /// <remarks>
    /// Uses FullUAC authorization via IAiAuthorizationService.
    /// </remarks>
    private async ValueTask<object?> AuthorizeDocumentAccessAsync(
        EndpointFilterInvocationContext context,
        ClaimsPrincipal user,
        EndpointFilterDelegate next)
    {
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
                context.HttpContext.RequestAborted);

            if (!result.Success)
            {
                _logger?.LogWarning(
                    "[ANALYSIS-AUTH] Document access DENIED: DocumentCount={Count}, Reason={Reason}",
                    documentIds.Count,
                    result.Reason);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: result.Reason ?? "Access denied to one or more documents",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
            }

            _logger?.LogDebug(
                "[ANALYSIS-AUTH] Document access GRANTED: DocumentCount={Count}",
                documentIds.Count);

            return await next(context);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ANALYSIS-AUTH] Authorization check failed with exception");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization check failed",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// Authorize access to an analysis record.
    /// Used for /continue, /save, /export, GET endpoints.
    /// </summary>
    /// <remarks>
    /// For analysis record access, we need to look up the associated document
    /// and authorize access to that document. This is a placeholder until
    /// Dataverse integration provides document lookup for analysis records.
    /// </remarks>
    private async ValueTask<object?> AuthorizeAnalysisAccessAsync(
        EndpointFilterInvocationContext context,
        ClaimsPrincipal user,
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

        // TODO: Look up sprk_analysisoutput to find associated document ID
        // For now, analysis records are tied to in-memory state and don't have
        // persistent Dataverse storage. When sprk_analysisoutput storage is implemented,
        // we'll look up the document ID and authorize via IAiAuthorizationService.
        _logger?.LogDebug(
            "[ANALYSIS-AUTH] Analysis record access: AnalysisId={AnalysisId} (document lookup pending)",
            analysisId);

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
