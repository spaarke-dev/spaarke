using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Resolves analysis scopes from Dataverse entities.
/// Loads Skills, Knowledge, Tools by ID or from Playbook configuration.
/// </summary>
/// <remarks>
/// Phase 1 Scaffolding: Returns stub data until Dataverse entity operations are implemented.
/// IDataverseService will be extended with Analysis entity methods in Task 032.
/// </remarks>
public class ScopeResolverService : IScopeResolverService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<ScopeResolverService> _logger;

    // In-memory stub data for Phase 1 (will be replaced with Dataverse in Task 032)
    private static readonly Dictionary<Guid, AnalysisAction> _stubActions = new()
    {
        [Guid.Parse("00000000-0000-0000-0000-000000000001")] = new AnalysisAction
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "Summarize Document",
            Description = "Generate a comprehensive summary of the document",
            SystemPrompt = "You are an AI assistant that creates clear, comprehensive document summaries. " +
                          "Focus on key points, main arguments, and important details.",
            SortOrder = 1
        },
        [Guid.Parse("00000000-0000-0000-0000-000000000002")] = new AnalysisAction
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Name = "Review Agreement",
            Description = "Analyze legal agreement terms and risks",
            SystemPrompt = "You are a legal assistant analyzing document terms. " +
                          "Identify key obligations, risks, deadlines, and important clauses.",
            SortOrder = 2
        }
    };

    public ScopeResolverService(
        IDataverseService dataverseService,
        ILogger<ScopeResolverService> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes: {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            skillIds.Length, knowledgeIds.Length, toolIds.Length);

        // Phase 1: Return empty scopes (actual resolution in Task 032)
        _logger.LogInformation("Phase 1: Returning empty scopes (Dataverse integration in Task 032)");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Resolving scopes from playbook {PlaybookId}", playbookId);

        // Phase 1: Playbook resolution not yet implemented
        _logger.LogWarning("Playbook resolution not yet implemented, returning empty scopes");

        return Task.FromResult(new ResolvedScopes([], [], []));
    }

    /// <inheritdoc />
    public Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading action {ActionId}", actionId);

        // Phase 1: Use stub actions or create default
        if (_stubActions.TryGetValue(actionId, out var action))
        {
            _logger.LogDebug("Found stub action: {ActionName}", action.Name);
            return Task.FromResult<AnalysisAction?>(action);
        }

        // Return a default action for any unknown ID
        _logger.LogInformation("Action {ActionId} not in stub data, returning default action", actionId);
        return Task.FromResult<AnalysisAction?>(new AnalysisAction
        {
            Id = actionId,
            Name = "Default Analysis",
            Description = "Analyze the document",
            SystemPrompt = "You are an AI assistant that analyzes documents and provides helpful insights. " +
                          "Be thorough, accurate, and provide clear explanations.",
            SortOrder = 0
        });
    }
}
