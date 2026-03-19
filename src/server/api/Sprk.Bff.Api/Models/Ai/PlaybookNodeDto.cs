using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Output types for DeliverOutput nodes (R2).
/// Determines how the PlaybookDispatcher presents the final result to the user.
/// Serialized as camelCase strings in JPS JSON.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputType
{
    /// <summary>Inline text/markdown response in chat.</summary>
    Text,

    /// <summary>Open a Code Page dialog (targetPage required).</summary>
    Dialog,

    /// <summary>Navigate to a record or page.</summary>
    Navigation,

    /// <summary>Generate a downloadable file.</summary>
    Download,

    /// <summary>Insert content into the current document context.</summary>
    Insert
}

/// <summary>
/// Response model for playbook node operations.
/// </summary>
public record PlaybookNodeDto
{
    /// <summary>
    /// Node ID (primary key).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Parent playbook ID.
    /// </summary>
    public Guid PlaybookId { get; init; }

    /// <summary>
    /// Coarse node category. Determines which scopes the orchestrator resolves.
    /// Maps to sprk_playbooknode.sprk_nodetype choice column.
    /// </summary>
    public NodeType NodeType { get; init; }

    /// <summary>
    /// Action ID to execute (only required when NodeType == AI).
    /// </summary>
    public Guid ActionId { get; init; }

    /// <summary>
    /// Associated tool IDs (N:N relationship).
    /// </summary>
    public Guid[] ToolIds { get; init; } = [];

    /// <summary>
    /// Node display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Execution order for sequential processing.
    /// </summary>
    public int ExecutionOrder { get; init; }

    /// <summary>
    /// Array of node IDs this node depends on.
    /// </summary>
    public Guid[] DependsOn { get; init; } = [];

    /// <summary>
    /// Variable name for referencing output in downstream nodes.
    /// </summary>
    public string OutputVariable { get; init; } = string.Empty;

    /// <summary>
    /// Condition expression for conditional execution (JSON).
    /// </summary>
    public string? ConditionJson { get; init; }

    /// <summary>
    /// Action-specific configuration (JSON).
    /// </summary>
    public string? ConfigJson { get; init; }

    /// <summary>
    /// Override AI model deployment for this node.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Execution timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Number of retry attempts on failure.
    /// </summary>
    public int? RetryCount { get; init; }

    /// <summary>
    /// X coordinate on visual canvas.
    /// </summary>
    public int? PositionX { get; init; }

    /// <summary>
    /// Y coordinate on visual canvas.
    /// </summary>
    public int? PositionY { get; init; }

    /// <summary>
    /// Whether node is enabled.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Associated skill IDs (N:N relationship).
    /// </summary>
    public Guid[] SkillIds { get; init; } = [];

    /// <summary>
    /// Associated knowledge source IDs (N:N relationship).
    /// </summary>
    public Guid[] KnowledgeIds { get; init; } = [];

    /// <summary>
    /// Whether this node requires human confirmation before execution (R2).
    /// When true, PlaybookDispatcher opens HITL confirmation UI before executing.
    /// When false, executes autonomously. Default is determined by OutputType
    /// in PlaybookDispatcher (dialog → true, text → false).
    /// Null means use default behavior based on output type.
    /// </summary>
    public bool? RequiresConfirmation { get; init; }

    /// <summary>
    /// Typed output for DeliverOutput nodes (R2).
    /// Determines how the PlaybookDispatcher presents the result.
    /// Only applicable when NodeType is Output.
    /// </summary>
    public OutputType? OutputType { get; init; }

    /// <summary>
    /// Code Page web resource name for dialog/navigation outputs (R2).
    /// Example: "sprk_emailcomposer". Only used when OutputType is Dialog or Navigation.
    /// </summary>
    public string? TargetPage { get; init; }

    /// <summary>
    /// Field name → AI-extracted value mapping for pre-populating target dialogs (R2).
    /// Keys are field names on the target Code Page; values are variable references
    /// or literal values from the playbook execution context.
    /// </summary>
    public Dictionary<string, string>? PrePopulateFields { get; init; }

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>
    /// Record modification timestamp.
    /// </summary>
    public DateTime ModifiedOn { get; init; }
}

/// <summary>
/// Request model for creating a new node.
/// </summary>
public record CreateNodeRequest
{
    /// <summary>
    /// Node display name.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Action ID to execute.
    /// </summary>
    [Required]
    public Guid ActionId { get; init; }

    /// <summary>
    /// Execution order. If not specified, will be placed at the end.
    /// </summary>
    public int? ExecutionOrder { get; init; }

    /// <summary>
    /// Array of node IDs this node depends on.
    /// </summary>
    public Guid[]? DependsOn { get; init; }

    /// <summary>
    /// Variable name for referencing output in downstream nodes.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string OutputVariable { get; init; } = string.Empty;

    /// <summary>
    /// Condition expression for conditional execution (JSON).
    /// </summary>
    public string? ConditionJson { get; init; }

    /// <summary>
    /// Action-specific configuration (JSON).
    /// </summary>
    public string? ConfigJson { get; init; }

    /// <summary>
    /// Override AI model deployment for this node.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Execution timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Number of retry attempts on failure.
    /// </summary>
    public int? RetryCount { get; init; }

    /// <summary>
    /// X coordinate on visual canvas.
    /// </summary>
    public int? PositionX { get; init; }

    /// <summary>
    /// Y coordinate on visual canvas.
    /// </summary>
    public int? PositionY { get; init; }

    /// <summary>
    /// Whether node is enabled.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Skill IDs to associate with this node.
    /// </summary>
    public Guid[]? SkillIds { get; init; }

    /// <summary>
    /// Knowledge source IDs to associate with this node.
    /// </summary>
    public Guid[]? KnowledgeIds { get; init; }

    /// <summary>
    /// Tool IDs to associate with this node.
    /// </summary>
    public Guid[]? ToolIds { get; init; }

    /// <summary>
    /// Whether this node requires human confirmation before execution (R2).
    /// </summary>
    public bool? RequiresConfirmation { get; init; }

    /// <summary>
    /// Typed output for DeliverOutput nodes (R2).
    /// </summary>
    public OutputType? OutputType { get; init; }

    /// <summary>
    /// Code Page web resource name for dialog/navigation outputs (R2).
    /// </summary>
    public string? TargetPage { get; init; }

    /// <summary>
    /// Field name → value mapping for pre-populating target dialogs (R2).
    /// </summary>
    public Dictionary<string, string>? PrePopulateFields { get; init; }
}

/// <summary>
/// Request model for updating an existing node.
/// </summary>
public record UpdateNodeRequest
{
    /// <summary>
    /// Node display name.
    /// </summary>
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; init; }

    /// <summary>
    /// Action ID to execute.
    /// </summary>
    public Guid? ActionId { get; init; }

    /// <summary>
    /// Array of node IDs this node depends on.
    /// </summary>
    public Guid[]? DependsOn { get; init; }

    /// <summary>
    /// Variable name for referencing output in downstream nodes.
    /// </summary>
    [StringLength(100, MinimumLength = 1)]
    public string? OutputVariable { get; init; }

    /// <summary>
    /// Condition expression for conditional execution (JSON).
    /// </summary>
    public string? ConditionJson { get; init; }

    /// <summary>
    /// Action-specific configuration (JSON).
    /// </summary>
    public string? ConfigJson { get; init; }

    /// <summary>
    /// Override AI model deployment for this node.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Execution timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>
    /// Number of retry attempts on failure.
    /// </summary>
    public int? RetryCount { get; init; }

    /// <summary>
    /// X coordinate on visual canvas.
    /// </summary>
    public int? PositionX { get; init; }

    /// <summary>
    /// Y coordinate on visual canvas.
    /// </summary>
    public int? PositionY { get; init; }

    /// <summary>
    /// Whether node is enabled.
    /// </summary>
    public bool? IsActive { get; init; }

    /// <summary>
    /// Skill IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? SkillIds { get; init; }

    /// <summary>
    /// Knowledge source IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? KnowledgeIds { get; init; }

    /// <summary>
    /// Tool IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? ToolIds { get; init; }

    /// <summary>
    /// Whether this node requires human confirmation before execution (R2).
    /// </summary>
    public bool? RequiresConfirmation { get; init; }

    /// <summary>
    /// Typed output for DeliverOutput nodes (R2).
    /// </summary>
    public OutputType? OutputType { get; init; }

    /// <summary>
    /// Code Page web resource name for dialog/navigation outputs (R2).
    /// </summary>
    public string? TargetPage { get; init; }

    /// <summary>
    /// Field name → value mapping for pre-populating target dialogs (R2).
    /// </summary>
    public Dictionary<string, string>? PrePopulateFields { get; init; }
}

/// <summary>
/// Request model for updating node scopes (skills and knowledge).
/// </summary>
public record NodeScopesRequest
{
    /// <summary>
    /// Skill IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? SkillIds { get; init; }

    /// <summary>
    /// Knowledge source IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? KnowledgeIds { get; init; }

    /// <summary>
    /// Tool IDs to associate with this node (replaces existing).
    /// </summary>
    public Guid[]? ToolIds { get; init; }
}

/// <summary>
/// Validation result for node configuration.
/// </summary>
public record NodeValidationResult
{
    /// <summary>
    /// Whether the node configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors if any.
    /// </summary>
    public string[] Errors { get; init; } = [];

    /// <summary>
    /// Create a successful validation result.
    /// </summary>
    public static NodeValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Create a failed validation result with errors.
    /// </summary>
    public static NodeValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}
