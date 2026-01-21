using Spaarke.Core.Auth;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding OfficeDocumentAccessFilter to Office share endpoints.
/// </summary>
public static class OfficeDocumentAccessFilterExtensions
{
    /// <summary>
    /// Adds document access filter that validates user has access to documents in share requests.
    /// Returns 403 Forbidden if user lacks access to any document.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">The operation being authorized (e.g., "share", "attach").</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This filter should be applied after OfficeAuthFilter to ensure userId is available.
    /// Extracts document IDs from request body (ShareLinksRequest, ShareAttachRequest).
    /// </remarks>
    public static TBuilder AddOfficeDocumentAccessFilter<TBuilder>(
        this TBuilder builder,
        string operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<OfficeDocumentAccessFilter>>();
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new OfficeDocumentAccessFilter(authService, operation, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user has access to documents in share requests.
/// Extracts document IDs from request body and verifies access for each.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// </para>
/// <para>
/// Document access is verified using the core AuthorizationService which checks:
/// 1. User has read access to the document
/// 2. User has share permission for share operations
/// 3. User's tenant matches the document's tenant
/// </para>
/// <para>
/// This filter expects:
/// - Request body with document IDs (ShareLinksRequest or ShareAttachRequest)
/// - userId in HttpContext.Items[OfficeAuthFilter.UserIdKey] (set by OfficeAuthFilter)
/// </para>
/// </remarks>
public class OfficeDocumentAccessFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;
    private readonly ILogger<OfficeDocumentAccessFilter>? _logger;

    public OfficeDocumentAccessFilter(
        AuthorizationService authorizationService,
        string operation,
        ILogger<OfficeDocumentAccessFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Get userId from HttpContext.Items (set by OfficeAuthFilter)
        var userId = httpContext.Items[OfficeAuthFilter.UserIdKey] as string;
        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "Document access check failed: No userId in HttpContext.Items. " +
                "Ensure OfficeAuthFilter runs before OfficeDocumentAccessFilter. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not established",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_003",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract document IDs from request arguments
        var documentIds = ExtractDocumentIds(context);
        if (documentIds == null || documentIds.Count == 0)
        {
            _logger?.LogDebug(
                "Document access check: No document IDs found in request, proceeding. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            // No documents to check - let the endpoint handle validation
            return await next(context);
        }

        _logger?.LogDebug(
            "Checking document access for user {UserId} on {DocumentCount} documents. " +
            "Operation: {Operation}. CorrelationId: {CorrelationId}",
            userId, documentIds.Count, _operation, httpContext.TraceIdentifier);

        // Check access for each document
        var deniedDocuments = new List<Guid>();
        foreach (var documentId in documentIds)
        {
            var authContext = new AuthorizationContext
            {
                UserId = userId,
                ResourceId = documentId.ToString(),
                Operation = _operation,
                CorrelationId = httpContext.TraceIdentifier
            };

            try
            {
                var result = await _authorizationService.AuthorizeAsync(authContext, httpContext.RequestAborted);
                if (!result.IsAllowed)
                {
                    _logger?.LogWarning(
                        "Document access denied: User {UserId} cannot {Operation} document {DocumentId}. " +
                        "Reason: {Reason}. CorrelationId: {CorrelationId}",
                        userId, _operation, documentId, result.ReasonCode, httpContext.TraceIdentifier);

                    deniedDocuments.Add(documentId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Document access check failed for document {DocumentId}. " +
                    "User: {UserId}, Operation: {Operation}. CorrelationId: {CorrelationId}",
                    documentId, userId, _operation, httpContext.TraceIdentifier);

                // Treat authorization errors as denials for security
                deniedDocuments.Add(documentId);
            }
        }

        // If any documents were denied, return 403
        if (deniedDocuments.Count > 0)
        {
            _logger?.LogWarning(
                "Document access denied: User {UserId} lacks access to {DeniedCount}/{TotalCount} documents. " +
                "CorrelationId: {CorrelationId}",
                userId, deniedDocuments.Count, documentIds.Count, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: $"Access denied to {deniedDocuments.Count} document(s)",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_009",
                    ["reasonCode"] = "sdap.office.document.access_denied",
                    ["deniedDocumentCount"] = deniedDocuments.Count,
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Document access verified: User {UserId} has access to all {DocumentCount} documents. " +
            "CorrelationId: {CorrelationId}",
            userId, documentIds.Count, httpContext.TraceIdentifier);

        return await next(context);
    }

    /// <summary>
    /// Extract document IDs from request arguments.
    /// Supports ShareLinksRequest, ShareAttachRequest, and direct Guid arrays.
    /// </summary>
    private static List<Guid>? ExtractDocumentIds(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                // Share links request (IReadOnlyList<Guid>)
                case ShareLinksRequest linksRequest when linksRequest.DocumentIds?.Count > 0:
                    return linksRequest.DocumentIds.ToList();

                // Share attach request (Guid[])
                case ShareAttachRequest attachRequest when attachRequest.DocumentIds?.Length > 0:
                    return attachRequest.DocumentIds.ToList();

                // Direct array of GUIDs
                case IEnumerable<Guid> guids:
                    var list = guids.ToList();
                    if (list.Count > 0)
                        return list;
                    break;
            }
        }

        return null;
    }
}
