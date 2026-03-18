using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Endpoint for resolving analysis-scoped SprkChat context.
///
/// Provides <c>GET /api/ai/chat/context-mappings/analysis/{analysisId}</c> which
/// returns the full <see cref="AnalysisChatContextResponse"/> for the given analysis record.
///
/// Used by the AnalysisWorkspace Code Page to populate QuickActionChips, the
/// slash command menu, and the initial playbook selection when SprkChat is
/// opened alongside an analysis output.
///
/// Follows ADR-001 (Minimal API) and ADR-008 (endpoint filters for authorization).
/// Caching is handled by <see cref="AnalysisChatContextResolver"/> (ADR-009 — Redis-first,
/// 30-minute absolute TTL; cache key: <c>chat-context:{tenantId}:{analysisId}</c>).
/// </summary>
public static class AnalysisChatContextEndpoints
{
    /// <summary>
    /// Registers the analysis chat context endpoint on the provided route builder.
    /// Called from <see cref="Sprk.Bff.Api.Infrastructure.DI.EndpointMappingExtensions"/>:
    /// <c>app.MapAnalysisChatContextEndpoints();</c>
    /// </summary>
    public static IEndpointRouteBuilder MapAnalysisChatContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("AI", "Chat");

        // GET /api/ai/chat/context-mappings/analysis/{analysisId}
        group.MapGet("/context-mappings/analysis/{analysisId}", GetAnalysisChatContextAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-context")
            .WithName("GetAnalysisChatContext")
            .WithSummary("Resolve analysis-scoped SprkChat context")
            .WithDescription(
                "Returns the full chat context for an analysis record: default playbook, available playbooks, " +
                "inline action chips derived from sprk_playbookcapabilities, knowledge sources, commands, " +
                "search guidance, scope metadata, and analysis metadata. " +
                "Results are Redis-cached for 30 minutes (cache key: chat-context:{tenantId}:{analysisId}). " +
                "Returns 404 when the analysis record cannot be resolved. " +
                "Optional query parameters: entityType, entityId for entity-scoped context (ADR-013).")
            .Produces<AnalysisChatContextResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .Produces(404)
            .ProducesProblem(429);

        return app;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// Resolve the analysis chat context for the given <paramref name="analysisId"/>.
    ///
    /// GET /api/ai/chat/context-mappings/analysis/{analysisId}
    ///
    /// Extracts tenantId from JWT claims or X-Tenant-Id header (ADR-014 — tenant-scoped cache keys).
    /// Optionally accepts <c>entityType</c> and <c>entityId</c> query parameters for entity-scoped
    /// context resolution (ADR-013 — ChatHostContext flows through pipeline).
    ///
    /// Resolution is delegated to <see cref="AnalysisChatContextResolver"/> which applies
    /// Redis caching (ADR-009) and Dataverse fallback resolution. Returns 404 when
    /// the resolver returns null (record not found or resolution failure).
    /// </summary>
    private static async Task<Results<Ok<AnalysisChatContextResponse>, NotFound, ProblemHttpResult>> GetAnalysisChatContextAsync(
        string analysisId,
        HttpContext httpContext,
        AnalysisChatContextResolver resolver,
        ILogger<AnalysisChatContextResolver> logger,
        string? entityType = null,
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        // Extract tenantId from JWT claims or X-Tenant-Id header (ADR-014)
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogWarning(
                "Tenant ID not found in claims or headers for analysis context request {AnalysisId}",
                analysisId);

            return TypedResults.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Tenant ID not found in token claims (tid) or X-Tenant-Id header.");
        }

        logger.LogDebug(
            "Resolving analysis chat context for {AnalysisId} (tenant={TenantId})",
            analysisId, tenantId);

        // Build optional ChatHostContext from query parameters (ADR-013)
        ChatHostContext? hostContext = null;
        if (!string.IsNullOrWhiteSpace(entityType) && !string.IsNullOrWhiteSpace(entityId))
        {
            hostContext = new ChatHostContext(entityType, entityId);
        }

        var result = await resolver.ResolveAsync(analysisId, tenantId, hostContext, cancellationToken);

        if (result is null)
        {
            logger.LogWarning(
                "Analysis chat context not found for {AnalysisId} (tenant={TenantId})",
                analysisId, tenantId);

            return TypedResults.NotFound();
        }

        logger.LogDebug(
            "Analysis chat context resolved for {AnalysisId}: Playbook={PlaybookName}, Actions={ActionCount}, Commands={CommandCount}",
            analysisId,
            result.DefaultPlaybookName,
            result.InlineActions.Count,
            result.Commands?.Count ?? 0);

        return TypedResults.Ok(result);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Extracts the tenant ID from Azure AD JWT claims (<c>tid</c>) or the
    /// <c>X-Tenant-Id</c> request header for service-to-service calls.
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        // Primary: 'tid' claim from Azure AD JWT token
        // Microsoft.Identity.Web may map 'tid' to the long-form URI claim
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        // Fallback: X-Tenant-Id request header (service-to-service calls)
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }

        return tenantId;
    }
}
