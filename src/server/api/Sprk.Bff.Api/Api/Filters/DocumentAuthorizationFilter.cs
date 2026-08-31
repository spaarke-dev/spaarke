using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding DocumentAuthorizationFilter to endpoints.
/// </summary>
public static class DocumentAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds document authorization to an endpoint with the specified operation.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">The operation being authorized (e.g., "read", "write", "delete").</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddDocumentAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        string operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new DocumentAuthorizationFilter(authService, operation);
            return await filter.InvokeAsync(context, next);
        });
    }
}

public class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;

    public DocumentAuthorizationFilter(AuthorizationService authorizationService, string operation)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // The caller's Entra OBJECT ID (`oid`) — the identifier IAccessDataSource matches against
        // Dataverse `systemuser.azureactivedirectoryobjectid`. Reading ClaimTypes.NameIdentifier
        // directly (as this did until UAT 2026-08-26 / D-6) yields `sub` under inbound claim
        // mapping — a pairwise, non-GUID id that can never match a systemuser, so EVERY caller on
        // EVERY route carrying this filter was denied. See Infrastructure/Authentication/CallerResolution.
        var userId = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Extract resource ID from route values (containerId, driveId, itemId, etc.)
        var resourceId = ExtractResourceId(context);
        if (string.IsNullOrEmpty(resourceId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Resource identifier not found in request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var authContext = new AuthorizationContext
        {
            UserId = userId,
            ResourceId = resourceId,
            Operation = _operation,
            CorrelationId = httpContext.TraceIdentifier,
            UserAccessToken = TokenHelper.ExtractBearerTokenOrNull(httpContext)
        };

        // The try covers the AUTHORIZATION DECISION ONLY — deliberately not next().
        //
        // unified-access-control-r2 task 072: `return await next(context)` used to sit inside this try,
        // so EVERY exception the downstream handler threw was caught here and rendered as
        // 500 "Authorization Error" / "An error occurred during authorization". On the nine routes
        // carrying this filter that silently converted each handler's intended status into a misleading
        // 500 — a document 404, a 409 "no file attached", a 409 "invalid drive id", and (the case that
        // surfaced it) task 072's own 403 for a disallowed anonymous link. It also defeats the global
        // UseExceptionHandler that renders SdapProblemException as canonical ProblemDetails per ADR-019.
        //
        // Two independent reasons to keep the boundary here: correctness of the response contract, and
        // honesty of the log line — "Authorization failed" was being written for faults that had nothing
        // to do with authorization, which is the kind of log that misdirects an incident.
        AuthorizationResult result;
        try
        {
            result = await _authorizationService.AuthorizeAsync(authContext);
        }
        catch (Exception ex)
        {
            // Log the actual exception for debugging
            var logger = httpContext.RequestServices.GetService<ILogger<DocumentAuthorizationFilter>>();
            logger?.LogError(ex, "Authorization failed for user {UserId} on resource {ResourceId} operation {Operation}",
                userId, resourceId, _operation);

            return Results.Problem(
                statusCode: 500,
                title: "Authorization Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        if (!result.IsAllowed)
        {
            return ProblemDetailsHelper.Forbidden(result.ReasonCode);
        }

        return await next(context);
    }

    private static string? ExtractResourceId(EndpointFilterInvocationContext context)
    {
        var routeValues = context.HttpContext.Request.RouteValues;

        // Try different possible resource ID parameter names
        // Note: "id" added for endpoints like /api/v1/documents/{id}/download
        return routeValues.TryGetValue("id", out var id) ? id?.ToString() :
               routeValues.TryGetValue("documentId", out var documentId) ? documentId?.ToString() :
               routeValues.TryGetValue("containerId", out var containerId) ? containerId?.ToString() :
               routeValues.TryGetValue("driveId", out var driveId) ? driveId?.ToString() :
               routeValues.TryGetValue("itemId", out var itemId) ? itemId?.ToString() :
               routeValues.TryGetValue("resourceId", out var resourceId) ? resourceId?.ToString() :
               null;
    }
}
