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
/// 30-minute absolute TTL; cache key: <c>analysis-context:{analysisId}</c>).
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
            .WithName("GetAnalysisChatContext")
            .WithSummary("Resolve analysis-scoped SprkChat context")
            .WithDescription(
                "Returns the full chat context for an analysis record: default playbook, available playbooks, " +
                "inline action chips derived from sprk_playbookcapabilities, knowledge sources, and analysis metadata. " +
                "Results are Redis-cached for 30 minutes (cache key: analysis-context:{analysisId}). " +
                "Returns 404 when the analysis record cannot be resolved.")
            .Produces<AnalysisChatContextResponse>(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .Produces(404);

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
    /// Resolution is delegated to <see cref="AnalysisChatContextResolver"/> which applies
    /// Redis caching (ADR-009) and Dataverse fallback resolution. Returns 404 when
    /// the resolver returns null (record not found or resolution failure).
    /// </summary>
    private static async Task<Results<Ok<AnalysisChatContextResponse>, NotFound>> GetAnalysisChatContextAsync(
        string analysisId,
        AnalysisChatContextResolver resolver,
        ILogger<AnalysisChatContextResolver> logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Resolving analysis chat context for {AnalysisId}",
            analysisId);

        var result = await resolver.ResolveAsync(analysisId, cancellationToken);

        if (result is null)
        {
            logger.LogWarning(
                "Analysis chat context not found for {AnalysisId}",
                analysisId);

            return TypedResults.NotFound();
        }

        logger.LogDebug(
            "Analysis chat context resolved for {AnalysisId}: Playbook={PlaybookName}, Actions={ActionCount}",
            analysisId,
            result.DefaultPlaybookName,
            result.InlineActions.Count);

        return TypedResults.Ok(result);
    }
}
