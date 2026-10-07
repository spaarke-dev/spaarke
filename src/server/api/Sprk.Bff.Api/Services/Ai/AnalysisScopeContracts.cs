namespace Sprk.Bff.Api.Services.Ai;
// NOTE: the IScopeManagementService interface and its ScopeManagementService implementation were
// DELETED 2026-09-29 (unified-access-control-r2 task 129, owner-approved). They had ZERO production
// consumers and were superseded by ScopeResolverService, which injects AnalysisActionService and
// AnalysisSkillService directly. See notes/D-15-scope-management-retirement.md.
//
// The REQUEST/RESULT CONTRACTS BELOW ARE LIVE and were never part of that dead surface -- they are
// consumed by AnalysisActionService, AnalysisSkillService, AnalysisKnowledgeService and others. They
// stayed here because the interface happened to share their file, which is the whole reason a
// "delete the class and its interface" instruction was too coarse: the first attempt removed this
// file wholesale and broke five live services.

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
    public Sprk.Bff.Api.Services.Ai.Nodes.ExecutorType ExecutorType { get; init; } = Sprk.Bff.Api.Services.Ai.Nodes.ExecutorType.AiAnalysis;
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
    public Sprk.Bff.Api.Services.Ai.Nodes.ExecutorType? ExecutorType { get; init; }
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
