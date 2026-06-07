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

    /// <summary>
    /// List all available analysis personas (sprk_aipersona rows) visible to the calling tenant
    /// with pagination/filtering/sorting per the same contract as the 4 sibling scope entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// R6 Pillar 1 (D-A-02). Persona resolution (most-specific-wins: global SYS- &lt; tenant CUST- &lt;
    /// playbook-attached) is OWNED by task 003 (Resolve*Persona methods). This method is the
    /// CRUD-side LIST surface only — it returns the raw catalog without applying resolution
    /// precedence, mirroring <see cref="ListActionsAsync(ScopeListOptions, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Per refined ADR-013, this is a Zone B (CRUD-side) facade method — does NOT route through
    /// Services/Ai/PublicContracts/ because the LIST surface has no AI internals; it is a thin
    /// Dataverse query.
    /// </para>
    /// </remarks>
    Task<ScopeListResult<AnalysisPersona>> ListPersonasAsync(
        ScopeListOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolve the effective persona for the chat agent using Q1 most-specific-wins precedence:
    /// global SYS- &lt; tenant CUST- &lt; playbook-attached.
    /// </summary>
    /// <param name="tenantId">Calling tenant identifier (Dataverse tenant scope; used for both
    /// tenant-isolation filtering and Redis cache-key scoping per ADR-014).</param>
    /// <param name="playbookId">Optional bound playbook ID. When supplied, a playbook-attached
    /// persona is checked first; if present, it wins.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The most-specific persona for the (tenant, playbook?) context. When no override exists,
    /// returns the seeded global SYS- default (task 004) — preserving identical behavior to today's
    /// <c>BuildDefaultSystemPrompt()</c> per FR-04. Throws <see cref="InvalidOperationException"/>
    /// if no SYS- default is found (catastrophic seed-data failure).
    /// </returns>
    /// <remarks>
    /// <para>
    /// R6 Pillar 1 (D-A-03). This resolver is wired by task 005 into
    /// <c>SprkChatAgentFactory.CreateAgentAsync</c> to replace the hardcoded
    /// <c>BuildDefaultSystemPrompt()</c> call. Resolution semantics per FR-03 + Q1: walk from
    /// most-specific (playbook-attached) to least-specific (global SYS-); first non-null match wins.
    /// </para>
    /// <para>
    /// NFR-01 binding: the returned persona supplies a <c>SystemPrompt</c> that augments the
    /// conversational system prompt but never replaces conversational ability. Caller (task 005)
    /// is responsible for composing the persona text alongside the conversational scaffold.
    /// </para>
    /// <para>
    /// Per refined ADR-013, this is an AI-internal resolver method — NOT exposed via
    /// Services/Ai/PublicContracts/. CRUD callers route through facades.
    /// </para>
    /// </remarks>
    Task<AnalysisPersona> ResolvePersonaForChatAsync(
        string tenantId,
        Guid? playbookId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get the effective persona for the (tenant, playbook?) context, or null when neither a
    /// tenant CUST- override nor a playbook-attached persona nor a SYS- default is found.
    /// </summary>
    /// <param name="tenantId">Calling tenant identifier.</param>
    /// <param name="playbookId">Optional bound playbook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The most-specific persona, or <c>null</c> if no candidate at any layer matches. Use this
    /// when callers need to handle the "no persona configured at all" case explicitly; otherwise
    /// prefer <see cref="ResolvePersonaForChatAsync"/> which throws on missing seed.
    /// </returns>
    /// <remarks>
    /// Mirrors the existing <c>GetXyzByNameAsync</c> + <c>GetXyzAsync</c> Optional shapes on the
    /// 4 sibling scope entities. Tenant isolation enforced per NFR-14: a tenant cannot see another
    /// tenant's CUST- persona.
    /// </remarks>
    Task<AnalysisPersona?> GetEffectivePersonaAsync(
        string tenantId,
        Guid? playbookId,
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

    #region Name-Based Lookups

    /// <summary>
    /// Look up a knowledge source by name.
    /// Uses OData $filter query to find the first matching record.
    /// </summary>
    /// <param name="name">The knowledge source name to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Knowledge definition or null if not found.</returns>
    Task<AnalysisKnowledge?> GetKnowledgeByNameAsync(
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Look up a skill by name.
    /// Uses OData $filter query to find the first matching record.
    /// </summary>
    /// <param name="name">The skill name to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Skill definition or null if not found.</returns>
    Task<AnalysisSkill?> GetSkillByNameAsync(
        string name,
        CancellationToken cancellationToken);

    #endregion

    #region Lookup Queries

    /// <summary>
    /// Query all values of a single field from a Dataverse entity.
    /// Used by <c>$choices</c> resolution to load lookup reference values
    /// (e.g., all matter type names from <c>sprk_mattertype_refs</c>).
    /// </summary>
    /// <param name="entitySetName">OData entity set name (plural, e.g., "sprk_mattertype_refs").</param>
    /// <param name="fieldName">Field name to select (e.g., "sprk_name").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of field values, or empty if none found.</returns>
    Task<string[]> QueryLookupValuesAsync(
        string entitySetName,
        string fieldName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Query option set (picklist) or multi-select picklist labels from Dataverse entity metadata.
    /// Used by <c>$choices</c> resolution to load choice field values
    /// (e.g., status options from <c>sprk_matter.sprk_status</c>).
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_matter").</param>
    /// <param name="attributeLogicalName">Attribute logical name (e.g., "sprk_status").</param>
    /// <param name="isMultiSelect">True for MultiSelectPicklistAttributeMetadata, false for PicklistAttributeMetadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of option labels, or empty if none found.</returns>
    Task<string[]> QueryOptionSetLabelsAsync(
        string entityLogicalName,
        string attributeLogicalName,
        bool isMultiSelect,
        CancellationToken cancellationToken);

    /// <summary>
    /// Query boolean (two-option) field labels from Dataverse entity metadata.
    /// Returns the TrueOption and FalseOption labels (e.g., ["Yes", "No"] or ["Active", "Inactive"]).
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_matter").</param>
    /// <param name="attributeLogicalName">Attribute logical name (e.g., "sprk_isactive").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of two labels [TrueLabel, FalseLabel], or empty if not found.</returns>
    Task<string[]> QueryBooleanLabelsAsync(
        string entityLogicalName,
        string attributeLogicalName,
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

    /// <summary>
    /// Indicates which invocation contexts this tool is available in:
    /// <see cref="ToolAvailabilityContext.Playbook"/> (default for existing rows; classic playbook orchestration),
    /// <see cref="ToolAvailabilityContext.Chat"/> (exposed to SprkChatAgentFactory.ResolveTools() in R6 Pillar 2),
    /// or <see cref="ToolAvailabilityContext.Both"/> (dual-context tool).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in R6 Pillar 2 (task D-A-07, FR-07) as the discriminator that the chat agent's
    /// data-driven tool registry filters on. Backed by the
    /// <c>sprk_availableincontexts</c> option-set on the <c>sprk_analysistool</c> Dataverse
    /// entity (100000000=Playbook, 100000001=Chat, 100000002=Both).
    /// </para>
    /// <para>
    /// Nullable for migration safety: rows queried before the column is populated
    /// fall back to <see cref="ToolAvailabilityContext.Playbook"/> at resolve time
    /// (backward-compat per FR-07 — every pre-R6 analysistool row is a playbook tool).
    /// </para>
    /// </remarks>
    public ToolAvailabilityContext? AvailableInContexts { get; init; }

    /// <summary>
    /// JSON Schema document (Draft 2020-12 family) describing the tool's parameter shape
    /// for LLM function-calling. Consumed by <c>ToolHandlerToAIFunctionAdapter</c>
    /// (R6 Pillar 2, task D-A-10) to wrap an <see cref="IToolHandler"/> as a
    /// <c>Microsoft.Extensions.AI.AIFunction</c>: the LLM sees this schema as the
    /// function's parameter declaration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added in R6 Pillar 2 (task D-A-08, FR-08). Backed by the
    /// <c>sprk_jsonschema</c> Memo (multi-line text, ~100 KB max) attribute on the
    /// <c>sprk_analysistool</c> Dataverse entity.
    /// </para>
    /// <para>
    /// <b>Nullability contract (FR-08)</b>: nullable on the DTO for backward-compat
    /// with pre-R6 playbook-only tool rows whose column is unpopulated. REQUIRED for
    /// chat-available tools (rows with <see cref="AvailableInContexts"/> ∋
    /// <see cref="ToolAvailabilityContext.Chat"/> or <see cref="ToolAvailabilityContext.Both"/>).
    /// The "required-for-chat" rule is enforced by the chat-side resolver
    /// (task 011) — not by this DTO contract — because the DTO must remain assignable
    /// from playbook-only Dataverse rows. Migrating chat tools (task 012) populates this
    /// field for the 10 migrated tools.
    /// </para>
    /// <para>
    /// <b>Validation contract (FR-08)</b>: when non-null, the string MUST parse as
    /// valid JSON. The DTO does NOT validate JSON Schema semantics (e.g., required
    /// keywords, type correctness) — that's the adapter's responsibility (task 010).
    /// The mapper logs malformed JSON and stores null rather than passing garbage
    /// to the LLM (see <c>AnalysisToolService.MapJsonSchema</c>).
    /// </para>
    /// </remarks>
    public string? JsonSchema { get; init; }
}

/// <summary>
/// Discriminator for which invocation contexts a tool is exposed in.
/// Backs <see cref="AnalysisTool.AvailableInContexts"/>. R6 Pillar 2 (FR-07).
/// </summary>
/// <remarks>
/// Option-set values match the canonical Spaarke Dataverse convention
/// (<c>100000000+</c>) used by <c>sprk_availableincontexts</c> on
/// <c>sprk_analysistool</c>. The single-select discriminator is deliberately
/// plain-enum (NOT <c>[Flags]</c>) — Dataverse single-select picklist semantics
/// require an explicit <see cref="Both"/> value rather than bit composition.
/// </remarks>
public enum ToolAvailabilityContext
{
    /// <summary>Tool is available only inside playbook orchestration (default; matches all pre-R6 tool rows).</summary>
    Playbook = 100000000,

    /// <summary>Tool is exposed only to the chat agent via SprkChatAgentFactory.ResolveTools().</summary>
    Chat = 100000001,

    /// <summary>Tool is available in both playbook orchestration and chat-agent invocation.</summary>
    Both = 100000002
}

/// <summary>
/// Analysis persona definition from sprk_aipersona entity.
/// </summary>
/// <remarks>
/// <para>
/// R6 Pillar 1 (D-A-02). Persona is a standalone Dataverse entity that supplies the system
/// prompt + persona metadata to <c>SprkChatAgentFactory.CreateAgentAsync</c> (wired in task 005).
/// Resolution semantics: most-specific-wins — global SYS- &lt; tenant CUST- &lt; playbook-attached.
/// Resolution methods (Resolve*Persona) are owned by task 003; this DTO is the thin record
/// returned by the LIST surface only.
/// </para>
/// <para>
/// SYS-/CUST- prefix enforcement is API-side (not Dataverse-side) — matches the 4 canonical
/// scope entities' pattern. See <see cref="DataverseHttpServiceBase.EnsureCustomerPrefix"/>
/// + <see cref="DataverseHttpServiceBase.ValidateOwnership"/>.
/// </para>
/// </remarks>
public record AnalysisPersona
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>
    /// The system prompt text used to seed the chat agent (replaces hardcoded
    /// <c>BuildDefaultSystemPrompt()</c> after task 005 lands).
    /// </summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>
    /// Persona scope type controlling resolution precedence. Maps to <c>sprk_scopetype</c>
    /// CHOICE (Global = 100000000, Tenant = 100000001, PlaybookAttached = 100000002).
    /// </summary>
    public PersonaScopeType ScopeType { get; init; } = PersonaScopeType.Global;

    /// <summary>
    /// Tags for filtering / discovery (multi-line CSV in <c>sprk_tags</c>).
    /// </summary>
    public string? Tags { get; init; }

    /// <summary>
    /// Whether the persona may be invoked ad-hoc from the chat surface (separate from playbook attach).
    /// </summary>
    public bool AvailableAdHoc { get; init; }

    // Ownership properties — mirror the 4-scope pattern.

    /// <summary>
    /// Owner type: System (SYS-) or Customer (CUST-). API-side prefix enforcement matches
    /// the 4-scope pattern; Dataverse has no enforcement.
    /// </summary>
    public ScopeOwnerType OwnerType { get; init; } = ScopeOwnerType.Customer;

    /// <summary>
    /// Whether this persona is immutable (system-owned personas are immutable).
    /// </summary>
    public bool IsImmutable { get; init; }

    /// <summary>
    /// Parent persona ID for extended personas. Maps to <c>sprk_parentpersonaid</c>
    /// (self-lookup). Drives the most-specific-wins resolution implemented in task 003.
    /// </summary>
    public Guid? ParentScopeId { get; init; }
}

/// <summary>
/// Persona scope type for resolution precedence. Mirrors the <c>sprk_aipersona.sprk_scopetype</c>
/// Dataverse CHOICE values.
/// </summary>
public enum PersonaScopeType
{
    /// <summary>Global SYS- persona (lowest precedence, e.g., the default system prompt).</summary>
    Global = 100000000,

    /// <summary>Tenant-level CUST- persona (overrides global).</summary>
    Tenant = 100000001,

    /// <summary>Playbook-attached persona (highest precedence, overrides tenant + global).</summary>
    PlaybookAttached = 100000002
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
