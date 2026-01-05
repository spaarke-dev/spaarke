using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Scope listing endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides read-only access to Skills, Knowledge, Tools, and Actions.
/// </summary>
public static class ScopeEndpoints
{
    public static IEndpointRouteBuilder MapScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/scopes")
            .RequireAuthorization()
            .WithTags("AI Scopes");

        // GET /api/ai/scopes/skills - List available skills
        group.MapGet("/skills", ListSkills)
            .WithName("ListSkills")
            .WithSummary("List available analysis skills")
            .WithDescription("Returns a paginated list of analysis skills that can be applied to document analysis.")
            .Produces<ScopeListResult<AnalysisSkill>>()
            .ProducesProblem(401);

        // GET /api/ai/scopes/knowledge - List available knowledge sources
        group.MapGet("/knowledge", ListKnowledge)
            .WithName("ListKnowledge")
            .WithSummary("List available knowledge sources")
            .WithDescription("Returns a paginated list of knowledge sources (inline, document references, RAG indexes).")
            .Produces<ScopeListResult<AnalysisKnowledge>>()
            .ProducesProblem(401);

        // GET /api/ai/scopes/tools - List available tools
        group.MapGet("/tools", ListTools)
            .WithName("ListTools")
            .WithSummary("List available analysis tools")
            .WithDescription("Returns a paginated list of analysis tools (summarizers, extractors, calculators).")
            .Produces<ScopeListResult<AnalysisTool>>()
            .ProducesProblem(401);

        // GET /api/ai/scopes/actions - List available actions
        group.MapGet("/actions", ListActions)
            .WithName("ListActions")
            .WithSummary("List available analysis actions")
            .WithDescription("Returns a paginated list of analysis actions that define how documents are analyzed.")
            .Produces<ScopeListResult<AnalysisAction>>()
            .ProducesProblem(401);

        return app;
    }

    /// <summary>
    /// List available analysis skills.
    /// </summary>
    private static async Task<IResult> ListSkills(
        IScopeResolverService scopeResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        string? category = null,
        string sortBy = "name",
        bool sortDescending = false)
    {
        var logger = loggerFactory.CreateLogger("ScopeEndpoints");

        var options = new ScopeListOptions
        {
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            NameFilter = nameFilter,
            CategoryFilter = category,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await scopeResolver.ListSkillsAsync(options, cancellationToken);
            logger.LogDebug("Listed {Count} skills (page {Page})", result.Items.Length, page);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list skills");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list skills");
        }
    }

    /// <summary>
    /// List available knowledge sources.
    /// </summary>
    private static async Task<IResult> ListKnowledge(
        IScopeResolverService scopeResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        string sortBy = "name",
        bool sortDescending = false)
    {
        var logger = loggerFactory.CreateLogger("ScopeEndpoints");

        var options = new ScopeListOptions
        {
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            NameFilter = nameFilter,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await scopeResolver.ListKnowledgeAsync(options, cancellationToken);
            logger.LogDebug("Listed {Count} knowledge sources (page {Page})", result.Items.Length, page);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list knowledge sources");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list knowledge sources");
        }
    }

    /// <summary>
    /// List available analysis tools.
    /// </summary>
    private static async Task<IResult> ListTools(
        IScopeResolverService scopeResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        string sortBy = "name",
        bool sortDescending = false)
    {
        var logger = loggerFactory.CreateLogger("ScopeEndpoints");

        var options = new ScopeListOptions
        {
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            NameFilter = nameFilter,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await scopeResolver.ListToolsAsync(options, cancellationToken);
            logger.LogDebug("Listed {Count} tools (page {Page})", result.Items.Length, page);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list tools");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list tools");
        }
    }

    /// <summary>
    /// List available analysis actions.
    /// </summary>
    private static async Task<IResult> ListActions(
        IScopeResolverService scopeResolver,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? nameFilter = null,
        string sortBy = "sortorder",
        bool sortDescending = false)
    {
        var logger = loggerFactory.CreateLogger("ScopeEndpoints");

        var options = new ScopeListOptions
        {
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 100),
            NameFilter = nameFilter,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        try
        {
            var result = await scopeResolver.ListActionsAsync(options, cancellationToken);
            logger.LogDebug("Listed {Count} actions (page {Page})", result.Items.Length, page);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list actions");
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to list actions");
        }
    }
}
