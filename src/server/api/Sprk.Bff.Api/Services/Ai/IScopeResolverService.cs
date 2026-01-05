namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Loads and resolves analysis scopes (Skills, Knowledge, Tools) from Dataverse.
/// Supports both explicit scope selection and playbook-based resolution.
/// </summary>
public interface IScopeResolverService
{
    /// <summary>
    /// Load scope definitions from Dataverse by explicit IDs.
    /// </summary>
    /// <param name="skillIds">Skill entity IDs.</param>
    /// <param name="knowledgeIds">Knowledge entity IDs.</param>
    /// <param name="toolIds">Tool entity IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved scopes with full entity data.</returns>
    Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Load scopes from a Playbook configuration.
    /// </summary>
    /// <param name="playbookId">Playbook entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved scopes from playbook N:N relationships.</returns>
    Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get action definition by ID.
    /// </summary>
    /// <param name="actionId">Action entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Action definition or null if not found.</returns>
    Task<AnalysisAction?> GetActionAsync(
        Guid actionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// List all available skills with pagination.
    /// </summary>
    Task<ScopeListResult<AnalysisSkill>> ListSkillsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// List all available knowledge sources with pagination.
    /// </summary>
    Task<ScopeListResult<AnalysisKnowledge>> ListKnowledgeAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// List all available tools with pagination.
    /// </summary>
    Task<ScopeListResult<AnalysisTool>> ListToolsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// List all available actions with pagination.
    /// </summary>
    Task<ScopeListResult<AnalysisAction>> ListActionsAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options for listing scope items.
/// </summary>
public record ScopeListOptions
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? NameFilter { get; init; }
    public string? CategoryFilter { get; init; }
    public string SortBy { get; init; } = "name";
    public bool SortDescending { get; init; } = false;
}

/// <summary>
/// Paginated result for scope listings.
/// </summary>
public record ScopeListResult<T>
{
    public required T[] Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasMore => Page < TotalPages;
}

/// <summary>
/// Resolved scopes containing full entity data for prompt construction.
/// </summary>
public record ResolvedScopes(
    AnalysisSkill[] Skills,
    AnalysisKnowledge[] Knowledge,
    AnalysisTool[] Tools);

/// <summary>
/// Analysis action definition from sprk_analysisaction entity.
/// </summary>
public record AnalysisAction
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

/// <summary>
/// Analysis skill definition from sprk_analysisskill entity.
/// </summary>
public record AnalysisSkill
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string PromptFragment { get; init; } = string.Empty;
    public string? Category { get; init; }
}

/// <summary>
/// Analysis knowledge definition from sprk_analysisknowledge entity.
/// </summary>
public record AnalysisKnowledge
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public KnowledgeType Type { get; init; }
    public string? Content { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? DeploymentId { get; init; }
}

/// <summary>
/// Knowledge source type.
/// </summary>
public enum KnowledgeType
{
    /// <summary>Inline text content.</summary>
    Inline = 0,

    /// <summary>Reference to a document in SPE.</summary>
    Document = 1,

    /// <summary>RAG index reference.</summary>
    RagIndex = 2
}

/// <summary>
/// Analysis tool definition from sprk_analysistool entity.
/// </summary>
public record AnalysisTool
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ToolType Type { get; init; }
    public string? HandlerClass { get; init; }
    public string? Configuration { get; init; }
}

/// <summary>
/// Tool type for execution routing.
/// </summary>
public enum ToolType
{
    /// <summary>Entity extraction tool.</summary>
    EntityExtractor = 0,

    /// <summary>Clause analysis tool.</summary>
    ClauseAnalyzer = 1,

    /// <summary>Document classification tool.</summary>
    DocumentClassifier = 2,

    /// <summary>Document summarization tool.</summary>
    Summary = 3,

    /// <summary>Risk detection and assessment tool.</summary>
    RiskDetector = 4,

    /// <summary>Clause comparison tool.</summary>
    ClauseComparison = 5,

    /// <summary>Date extraction and normalization tool.</summary>
    DateExtractor = 6,

    /// <summary>Financial calculation tool.</summary>
    FinancialCalculator = 7,

    /// <summary>Custom tool with handler class.</summary>
    Custom = 99
}
