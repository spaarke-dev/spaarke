using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

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
    /// Action ID to execute.
    /// </summary>
    public Guid ActionId { get; init; }

    /// <summary>
    /// Optional tool handler ID (single tool per node).
    /// </summary>
    public Guid? ToolId { get; init; }

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
    /// Optional tool handler ID.
    /// </summary>
    public Guid? ToolId { get; init; }

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
    /// Optional tool handler ID.
    /// </summary>
    public Guid? ToolId { get; init; }

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
