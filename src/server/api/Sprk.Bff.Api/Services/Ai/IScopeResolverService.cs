namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Loads and resolves analysis scopes (Skills, Knowledge, Tools) from Dataverse.
/// Supports both explicit scope selection and playbook-based resolution.
/// Extended with CRUD operations for scope management.
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
    /// Load scopes from a PlaybookNode configuration.
    /// Resolves node-level N:N relationships for skills and knowledge,
    /// plus the single tool lookup.
    /// </summary>
    /// <param name="nodeId">PlaybookNode entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved scopes from node relationships and tool lookup.</returns>
    Task<ResolvedScopes> ResolveNodeScopesAsync(
        Guid nodeId,
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

    #region Action CRUD Operations

    /// <summary>
    /// Create a new analysis action.
    /// </summary>
    /// <param name="request">The action creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created action with generated ID.</returns>
    Task<AnalysisAction> CreateActionAsync(
        CreateActionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing analysis action.
    /// </summary>
    /// <param name="id">The action ID to update.</param>
    /// <param name="request">The action update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated action.</returns>
    /// <exception cref="InvalidOperationException">Thrown if action is immutable (system-owned).</exception>
    Task<AnalysisAction> UpdateActionAsync(
        Guid id,
        UpdateActionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delete an analysis action.
    /// </summary>
    /// <param name="id">The action ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if action is immutable (system-owned).</exception>
    Task<bool> DeleteActionAsync(
        Guid id,
        CancellationToken cancellationToken);

    #endregion

    #region Skill CRUD Operations

    /// <summary>
    /// Get a skill by ID.
    /// </summary>
    /// <param name="skillId">Skill entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Skill definition or null if not found.</returns>
    Task<AnalysisSkill?> GetSkillAsync(
        Guid skillId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a new analysis skill.
    /// </summary>
    /// <param name="request">The skill creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created skill with generated ID.</returns>
    Task<AnalysisSkill> CreateSkillAsync(
        CreateSkillRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing analysis skill.
    /// </summary>
    /// <param name="id">The skill ID to update.</param>
    /// <param name="request">The skill update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated skill.</returns>
    /// <exception cref="InvalidOperationException">Thrown if skill is immutable (system-owned).</exception>
    Task<AnalysisSkill> UpdateSkillAsync(
        Guid id,
        UpdateSkillRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delete an analysis skill.
    /// </summary>
    /// <param name="id">The skill ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if skill is immutable (system-owned).</exception>
    Task<bool> DeleteSkillAsync(
        Guid id,
        CancellationToken cancellationToken);

    #endregion

    #region Knowledge CRUD Operations

    /// <summary>
    /// Get a knowledge source by ID.
    /// </summary>
    /// <param name="knowledgeId">Knowledge entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Knowledge definition or null if not found.</returns>
    Task<AnalysisKnowledge?> GetKnowledgeAsync(
        Guid knowledgeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a new analysis knowledge source.
    /// </summary>
    /// <param name="request">The knowledge creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created knowledge with generated ID.</returns>
    Task<AnalysisKnowledge> CreateKnowledgeAsync(
        CreateKnowledgeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing analysis knowledge source.
    /// </summary>
    /// <param name="id">The knowledge ID to update.</param>
    /// <param name="request">The knowledge update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated knowledge.</returns>
    /// <exception cref="InvalidOperationException">Thrown if knowledge is immutable (system-owned).</exception>
    Task<AnalysisKnowledge> UpdateKnowledgeAsync(
        Guid id,
        UpdateKnowledgeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delete an analysis knowledge source.
    /// </summary>
    /// <param name="id">The knowledge ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if knowledge is immutable (system-owned).</exception>
    Task<bool> DeleteKnowledgeAsync(
        Guid id,
        CancellationToken cancellationToken);

    #endregion

    #region Tool CRUD Operations

    /// <summary>
    /// Get a tool by ID.
    /// </summary>
    /// <param name="toolId">Tool entity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool definition or null if not found.</returns>
    Task<AnalysisTool?> GetToolAsync(
        Guid toolId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a new analysis tool.
    /// </summary>
    /// <param name="request">The tool creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created tool with generated ID.</returns>
    Task<AnalysisTool> CreateToolAsync(
        CreateToolRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing analysis tool.
    /// </summary>
    /// <param name="id">The tool ID to update.</param>
    /// <param name="request">The tool update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated tool.</returns>
    /// <exception cref="InvalidOperationException">Thrown if tool is immutable (system-owned).</exception>
    Task<AnalysisTool> UpdateToolAsync(
        Guid id,
        UpdateToolRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Delete an analysis tool.
    /// </summary>
    /// <param name="id">The tool ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted, false if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if tool is immutable (system-owned).</exception>
    Task<bool> DeleteToolAsync(
        Guid id,
        CancellationToken cancellationToken);

    #endregion

    #region Search Operations

    /// <summary>
    /// Search across all scope types with unified query.
    /// </summary>
    /// <param name="query">The search query with filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results grouped by scope type.</returns>
    Task<ScopeSearchResult> SearchScopesAsync(
        ScopeSearchQuery query,
        CancellationToken cancellationToken);

    #endregion

    #region Save As Operations

    /// <summary>
    /// Create a copy of an existing action with a new name.
    /// The copy will have BasedOnId set to the original action's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="sourceId">The source action ID to copy from.</param>
    /// <param name="newName">The name for the new action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created action copy.</returns>
    Task<AnalysisAction> SaveAsActionAsync(
        Guid sourceId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a copy of an existing skill with a new name.
    /// The copy will have BasedOnId set to the original skill's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="sourceId">The source skill ID to copy from.</param>
    /// <param name="newName">The name for the new skill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created skill copy.</returns>
    Task<AnalysisSkill> SaveAsSkillAsync(
        Guid sourceId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a copy of an existing knowledge source with a new name.
    /// The copy will have BasedOnId set to the original knowledge's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="sourceId">The source knowledge ID to copy from.</param>
    /// <param name="newName">The name for the new knowledge source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created knowledge copy.</returns>
    Task<AnalysisKnowledge> SaveAsKnowledgeAsync(
        Guid sourceId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a copy of an existing tool with a new name.
    /// The copy will have BasedOnId set to the original tool's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="sourceId">The source tool ID to copy from.</param>
    /// <param name="newName">The name for the new tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created tool copy.</returns>
    Task<AnalysisTool> SaveAsToolAsync(
        Guid sourceId,
        string newName,
        CancellationToken cancellationToken);

    #endregion

    #region Extend Operations

    /// <summary>
    /// Create a child action that extends the parent.
    /// The child will have ParentScopeId set to the parent action's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="parentId">The parent action ID to extend from.</param>
    /// <param name="childName">The name for the child action.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created child action.</returns>
    Task<AnalysisAction> ExtendActionAsync(
        Guid parentId,
        string childName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a child skill that extends the parent.
    /// The child will have ParentScopeId set to the parent skill's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="parentId">The parent skill ID to extend from.</param>
    /// <param name="childName">The name for the child skill.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created child skill.</returns>
    Task<AnalysisSkill> ExtendSkillAsync(
        Guid parentId,
        string childName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a child knowledge source that extends the parent.
    /// The child will have ParentScopeId set to the parent knowledge's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="parentId">The parent knowledge ID to extend from.</param>
    /// <param name="childName">The name for the child knowledge source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created child knowledge source.</returns>
    Task<AnalysisKnowledge> ExtendKnowledgeAsync(
        Guid parentId,
        string childName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a child tool that extends the parent.
    /// The child will have ParentScopeId set to the parent tool's ID.
    /// Always creates a customer-owned (CUST-) scope.
    /// </summary>
    /// <param name="parentId">The parent tool ID to extend from.</param>
    /// <param name="childName">The name for the child tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created child tool.</returns>
    Task<AnalysisTool> ExtendToolAsync(
        Guid parentId,
        string childName,
        CancellationToken cancellationToken);

    #endregion
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

    /// <summary>
    /// Action type for executor routing.
    /// Maps to sprk_analysisaction.sprk_actiontype choice value.
    /// </summary>
    public Sprk.Bff.Api.Services.Ai.Nodes.ActionType ActionType { get; init; } = Sprk.Bff.Api.Services.Ai.Nodes.ActionType.AiAnalysis;

    // Ownership properties
    /// <summary>
    /// Owner type: System (SYS-) or Customer (CUST-).
    /// </summary>
    public ScopeOwnerType OwnerType { get; init; } = ScopeOwnerType.Customer;

    /// <summary>
    /// Whether this scope is immutable (system-owned scopes are immutable).
    /// </summary>
    public bool IsImmutable { get; init; }

    /// <summary>
    /// Parent scope ID for extended scopes.
    /// </summary>
    public Guid? ParentScopeId { get; init; }

    /// <summary>
    /// Original scope ID when created via "Save As".
    /// </summary>
    public Guid? BasedOnId { get; init; }
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

    // Ownership properties
    /// <summary>
    /// Owner type: System (SYS-) or Customer (CUST-).
    /// </summary>
    public ScopeOwnerType OwnerType { get; init; } = ScopeOwnerType.Customer;

    /// <summary>
    /// Whether this scope is immutable (system-owned scopes are immutable).
    /// </summary>
    public bool IsImmutable { get; init; }

    /// <summary>
    /// Parent scope ID for extended scopes.
    /// </summary>
    public Guid? ParentScopeId { get; init; }

    /// <summary>
    /// Original scope ID when created via "Save As".
    /// </summary>
    public Guid? BasedOnId { get; init; }
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

    // Ownership properties
    /// <summary>
    /// Owner type: System (SYS-) or Customer (CUST-).
    /// </summary>
    public ScopeOwnerType OwnerType { get; init; } = ScopeOwnerType.Customer;

    /// <summary>
    /// Whether this scope is immutable (system-owned scopes are immutable).
    /// </summary>
    public bool IsImmutable { get; init; }

    /// <summary>
    /// Parent scope ID for extended scopes.
    /// </summary>
    public Guid? ParentScopeId { get; init; }

    /// <summary>
    /// Original scope ID when created via "Save As".
    /// </summary>
    public Guid? BasedOnId { get; init; }
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

    // Ownership properties
    /// <summary>
    /// Owner type: System (SYS-) or Customer (CUST-).
    /// </summary>
    public ScopeOwnerType OwnerType { get; init; } = ScopeOwnerType.Customer;

    /// <summary>
    /// Whether this scope is immutable (system-owned scopes are immutable).
    /// </summary>
    public bool IsImmutable { get; init; }

    /// <summary>
    /// Parent scope ID for extended scopes.
    /// </summary>
    public Guid? ParentScopeId { get; init; }

    /// <summary>
    /// Original scope ID when created via "Save As".
    /// </summary>
    public Guid? BasedOnId { get; init; }
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

#region Search DTOs

/// <summary>
/// Scope owner type for access control.
/// </summary>
public enum ScopeOwnerType
{
    /// <summary>System-owned scope (SYS- prefix). Immutable.</summary>
    System = 1,

    /// <summary>Customer-owned scope (CUST- prefix). Editable.</summary>
    Customer = 2
}

/// <summary>
/// Query for searching scopes across all types.
/// </summary>
public record ScopeSearchQuery
{
    /// <summary>
    /// Text to search in name and description.
    /// </summary>
    public string? SearchText { get; init; }

    /// <summary>
    /// Filter by scope type(s). If empty, searches all types.
    /// </summary>
    public ScopeType[] ScopeTypes { get; init; } = [];

    /// <summary>
    /// Filter by owner type. If null, returns all.
    /// </summary>
    public ScopeOwnerType? OwnerType { get; init; }

    /// <summary>
    /// Filter by category (applies to skills).
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Include only editable (non-immutable) scopes.
    /// </summary>
    public bool EditableOnly { get; init; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Search result containing scopes grouped by type.
/// </summary>
public record ScopeSearchResult
{
    /// <summary>
    /// Matching actions.
    /// </summary>
    public AnalysisAction[] Actions { get; init; } = [];

    /// <summary>
    /// Matching skills.
    /// </summary>
    public AnalysisSkill[] Skills { get; init; } = [];

    /// <summary>
    /// Matching knowledge sources.
    /// </summary>
    public AnalysisKnowledge[] Knowledge { get; init; } = [];

    /// <summary>
    /// Matching tools.
    /// </summary>
    public AnalysisTool[] Tools { get; init; } = [];

    /// <summary>
    /// Total count across all types.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Breakdown of counts by type.
    /// </summary>
    public Dictionary<ScopeType, int> CountsByType { get; init; } = new();
}

#endregion
