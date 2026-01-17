namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages CRUD operations for Dataverse scope entities (Actions, Skills, Knowledge, Tools, Outputs).
/// Handles ownership validation (SYS- immutable, CUST- editable) and duplicate name handling.
/// </summary>
/// <remarks>
/// This service complements IScopeResolverService (read operations) with write operations.
/// Implements ownership model per spec: SYS- prefixed scopes are immutable system records,
/// CUST- prefixed scopes are user-created and editable.
/// </remarks>
public interface IScopeManagementService
{
    // ========================================
    // Action CRUD
    // ========================================

    /// <summary>
    /// Create a new action scope record.
    /// Name will be auto-prefixed with CUST- if not already prefixed.
    /// </summary>
    /// <param name="request">Action creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created action with assigned ID.</returns>
    Task<AnalysisAction> CreateActionAsync(CreateActionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing action scope record.
    /// </summary>
    /// <param name="id">Action ID to update.</param>
    /// <param name="request">Updated action data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated action.</returns>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to modify a SYS- scope.</exception>
    Task<AnalysisAction> UpdateActionAsync(Guid id, UpdateActionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Delete an action scope record.
    /// </summary>
    /// <param name="id">Action ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to delete a SYS- scope.</exception>
    Task DeleteActionAsync(Guid id, CancellationToken cancellationToken);

    // ========================================
    // Skill CRUD
    // ========================================

    /// <summary>
    /// Create a new skill scope record.
    /// </summary>
    Task<AnalysisSkill> CreateSkillAsync(CreateSkillRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing skill scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to modify a SYS- scope.</exception>
    Task<AnalysisSkill> UpdateSkillAsync(Guid id, UpdateSkillRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a skill scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to delete a SYS- scope.</exception>
    Task DeleteSkillAsync(Guid id, CancellationToken cancellationToken);

    // ========================================
    // Knowledge CRUD
    // ========================================

    /// <summary>
    /// Create a new knowledge scope record.
    /// </summary>
    Task<AnalysisKnowledge> CreateKnowledgeAsync(CreateKnowledgeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing knowledge scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to modify a SYS- scope.</exception>
    Task<AnalysisKnowledge> UpdateKnowledgeAsync(Guid id, UpdateKnowledgeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a knowledge scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to delete a SYS- scope.</exception>
    Task DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken);

    // ========================================
    // Tool CRUD
    // ========================================

    /// <summary>
    /// Create a new tool scope record.
    /// </summary>
    Task<AnalysisTool> CreateToolAsync(CreateToolRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing tool scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to modify a SYS- scope.</exception>
    Task<AnalysisTool> UpdateToolAsync(Guid id, UpdateToolRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Delete a tool scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to delete a SYS- scope.</exception>
    Task DeleteToolAsync(Guid id, CancellationToken cancellationToken);

    // ========================================
    // Output CRUD
    // ========================================

    /// <summary>
    /// Create a new output scope record.
    /// </summary>
    Task<AnalysisOutput> CreateOutputAsync(CreateOutputRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Update an existing output scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to modify a SYS- scope.</exception>
    Task<AnalysisOutput> UpdateOutputAsync(Guid id, UpdateOutputRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Delete an output scope record.
    /// </summary>
    /// <exception cref="ScopeOwnershipException">Thrown if attempting to delete a SYS- scope.</exception>
    Task DeleteOutputAsync(Guid id, CancellationToken cancellationToken);

    // ========================================
    // N:N Link Operations
    // ========================================

    /// <summary>
    /// Link a scope to a playbook via N:N relationship table.
    /// Relationship tables: sprk_aianalysisplaybook_action, sprk_aianalysisplaybook_skill, etc.
    /// </summary>
    /// <param name="playbookId">Playbook ID to link to.</param>
    /// <param name="scopeType">Type of scope being linked.</param>
    /// <param name="scopeId">Scope ID to link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if link already exists.</exception>
    Task LinkScopeToPlaybookAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken);

    /// <summary>
    /// Unlink a scope from a playbook (remove N:N relationship).
    /// </summary>
    /// <param name="playbookId">Playbook ID to unlink from.</param>
    /// <param name="scopeType">Type of scope being unlinked.</param>
    /// <param name="scopeId">Scope ID to unlink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnlinkScopeFromPlaybookAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken);

    /// <summary>
    /// Check if a scope is already linked to a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID to check.</param>
    /// <param name="scopeType">Type of scope to check.</param>
    /// <param name="scopeId">Scope ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if link exists, false otherwise.</returns>
    Task<bool> IsLinkedAsync(Guid playbookId, ScopeType scopeType, Guid scopeId, CancellationToken cancellationToken);

    /// <summary>
    /// Get all scope IDs linked to a playbook for a given scope type.
    /// </summary>
    /// <param name="playbookId">Playbook ID to query.</param>
    /// <param name="scopeType">Type of scopes to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of linked scope IDs.</returns>
    Task<IReadOnlyList<Guid>> GetLinkedScopeIdsAsync(Guid playbookId, ScopeType scopeType, CancellationToken cancellationToken);

    // ========================================
    // Utility Methods
    // ========================================

    /// <summary>
    /// Check if a scope name is a system scope (SYS- prefix).
    /// </summary>
    bool IsSystemScope(string name);

    /// <summary>
    /// Generate a unique name by adding suffix if duplicate exists.
    /// Example: "My Action" → "My Action (1)" → "My Action (2)"
    /// </summary>
    Task<string> GenerateUniqueNameAsync(ScopeType scopeType, string baseName, CancellationToken cancellationToken);
}

// ========================================
// Request Records
// ========================================

/// <summary>
/// Request to create a new action scope.
/// </summary>
public record CreateActionRequest
{
    /// <summary>Action name. Will be auto-prefixed with CUST- if not already prefixed.</summary>
    public required string Name { get; init; }

    /// <summary>Action description.</summary>
    public string? Description { get; init; }

    /// <summary>System prompt for AI processing.</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>Sort order for display.</summary>
    public int SortOrder { get; init; } = 100;

    /// <summary>Action type for executor routing.</summary>
    public Sprk.Bff.Api.Services.Ai.Nodes.ActionType ActionType { get; init; } = Sprk.Bff.Api.Services.Ai.Nodes.ActionType.AiAnalysis;
}

/// <summary>
/// Request to update an action scope.
/// </summary>
public record UpdateActionRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? SystemPrompt { get; init; }
    public int? SortOrder { get; init; }
    public Sprk.Bff.Api.Services.Ai.Nodes.ActionType? ActionType { get; init; }
}

/// <summary>
/// Request to create a new skill scope.
/// </summary>
public record CreateSkillRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string PromptFragment { get; init; }
    public string? Category { get; init; }
}

/// <summary>
/// Request to update a skill scope.
/// </summary>
public record UpdateSkillRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? PromptFragment { get; init; }
    public string? Category { get; init; }
}

/// <summary>
/// Request to create a new knowledge scope.
/// </summary>
public record CreateKnowledgeRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public KnowledgeType Type { get; init; } = KnowledgeType.Inline;
    public string? Content { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? DeploymentId { get; init; }
}

/// <summary>
/// Request to update a knowledge scope.
/// </summary>
public record UpdateKnowledgeRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public KnowledgeType? Type { get; init; }
    public string? Content { get; init; }
    public Guid? DocumentId { get; init; }
    public Guid? DeploymentId { get; init; }
}

/// <summary>
/// Request to create a new tool scope.
/// </summary>
public record CreateToolRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ToolType Type { get; init; } = ToolType.Custom;
    public string? HandlerClass { get; init; }
    public string? Configuration { get; init; }
}

/// <summary>
/// Request to update a tool scope.
/// </summary>
public record UpdateToolRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public ToolType? Type { get; init; }
    public string? HandlerClass { get; init; }
    public string? Configuration { get; init; }
}

/// <summary>
/// Request to create a new output scope.
/// </summary>
public record CreateOutputRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string FieldName { get; init; }
    public OutputFieldType FieldType { get; init; } = OutputFieldType.Text;
    public string? JsonPath { get; init; }
}

/// <summary>
/// Request to update an output scope.
/// </summary>
public record UpdateOutputRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? FieldName { get; init; }
    public OutputFieldType? FieldType { get; init; }
    public string? JsonPath { get; init; }
}

// ========================================
// Entity Models
// ========================================

/// <summary>
/// Analysis output definition from sprk_aianalysisoutput entity.
/// Defines field mappings for analysis results.
/// </summary>
public record AnalysisOutput
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string FieldName { get; init; } = string.Empty;
    public OutputFieldType FieldType { get; init; }
    public string? JsonPath { get; init; }
}

/// <summary>
/// Output field type for result mapping.
/// </summary>
public enum OutputFieldType
{
    /// <summary>Text/string output.</summary>
    Text = 0,

    /// <summary>Numeric output.</summary>
    Number = 1,

    /// <summary>Date/time output.</summary>
    DateTime = 2,

    /// <summary>Boolean output.</summary>
    Boolean = 3,

    /// <summary>JSON object output.</summary>
    Json = 4,

    /// <summary>Array/list output.</summary>
    Array = 5
}

/// <summary>
/// Scope type enumeration for utility methods.
/// </summary>
public enum ScopeType
{
    Action,
    Skill,
    Knowledge,
    Tool,
    Output
}

// ========================================
// Exception
// ========================================

/// <summary>
/// Exception thrown when attempting to modify an immutable system scope.
/// </summary>
public class ScopeOwnershipException : InvalidOperationException
{
    public ScopeOwnershipException(string scopeName, string operation)
        : base($"Cannot {operation} system scope '{scopeName}'. System scopes (SYS- prefix) are immutable.")
    {
        ScopeName = scopeName;
        Operation = operation;
    }

    public string ScopeName { get; }
    public string Operation { get; }
}
