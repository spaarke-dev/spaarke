using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Execution context for a single node in playbook orchestration.
/// Contains all information needed by node executors to perform their work.
/// </summary>
/// <remarks>
/// <para>
/// NodeExecutionContext is created by PlaybookOrchestrationService for each
/// node before execution. It provides:
/// </para>
/// <list type="bullet">
/// <item>Node definition with configuration</item>
/// <item>Action definition determining behavior type</item>
/// <item>Resolved scopes (skills, knowledge, tool)</item>
/// <item>Document context for analysis nodes</item>
/// <item>Previous node outputs for template substitution</item>
/// <item>Model deployment settings</item>
/// </list>
/// </remarks>
public record NodeExecutionContext
{
    /// <summary>
    /// Unique identifier for this playbook execution run.
    /// Used for correlation across all nodes in a single run.
    /// </summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// The playbook being executed.
    /// </summary>
    public required Guid PlaybookId { get; init; }

    /// <summary>
    /// The node being executed.
    /// </summary>
    public required PlaybookNodeDto Node { get; init; }

    /// <summary>
    /// Action definition for this node (determines ActionType).
    /// Contains system prompt and other action-level settings.
    /// </summary>
    public required AnalysisAction Action { get; init; }

    /// <summary>
    /// The ActionType for this node (extracted from Action for convenience).
    /// </summary>
    public ActionType ActionType { get; init; }

    /// <summary>
    /// Resolved scopes for this node.
    /// Contains skills (prompt modifiers), knowledge (context), and tool (handler).
    /// </summary>
    public required ResolvedScopes Scopes { get; init; }

    /// <summary>
    /// Document context for analysis nodes.
    /// Contains extracted text and document metadata.
    /// May be null for non-document-based actions.
    /// </summary>
    public DocumentContext? Document { get; init; }

    /// <summary>
    /// Outputs from previously completed nodes in this run.
    /// Keyed by outputVariable name for template substitution.
    /// </summary>
    public IReadOnlyDictionary<string, NodeOutput> PreviousOutputs { get; init; }
        = new Dictionary<string, NodeOutput>();

    /// <summary>
    /// Tenant identifier for multi-tenant isolation.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// User-provided context or instructions for this execution.
    /// May include specific questions or focus areas.
    /// </summary>
    public string? UserContext { get; init; }

    /// <summary>
    /// AI model deployment ID override for this node.
    /// If null, uses the default model for the action type.
    /// </summary>
    public Guid? ModelDeploymentId { get; init; }

    /// <summary>
    /// Maximum tokens to use for AI model calls.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature setting for AI model calls (0.0 - 1.0).
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Timestamp when this execution context was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional callback invoked for each streaming token from the AI model.
    /// When set, node executors supporting streaming will invoke this for each token,
    /// enabling per-token SSE events (NodeProgress) to reach the client.
    /// When null, executors use the blocking (non-streaming) path.
    /// </summary>
    public Func<string, ValueTask>? OnTokenReceived { get; init; }

    /// <summary>
    /// Gets the configured timeout for this node in seconds.
    /// Uses node-specific timeout or default of 300 seconds.
    /// </summary>
    public int TimeoutSeconds => Node.TimeoutSeconds ?? 300;

    /// <summary>
    /// Gets the configured retry count for this node.
    /// Uses node-specific retry count or default of 0.
    /// </summary>
    public int RetryCount => Node.RetryCount ?? 0;

    /// <summary>
    /// Gets the single tool for this node, if configured.
    /// Returns null if no tool is assigned (pure AI completion nodes).
    /// </summary>
    public AnalysisTool? Tool => Scopes.Tools.FirstOrDefault();

    /// <summary>
    /// Looks up a previous output by variable name.
    /// </summary>
    /// <param name="variableName">The output variable name to find.</param>
    /// <returns>The NodeOutput if found, otherwise null.</returns>
    public NodeOutput? GetPreviousOutput(string variableName)
    {
        return PreviousOutputs.TryGetValue(variableName, out var output) ? output : null;
    }
}
