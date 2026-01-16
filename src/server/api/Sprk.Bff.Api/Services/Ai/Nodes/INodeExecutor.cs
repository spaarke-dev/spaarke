using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Nodes;

/// <summary>
/// Interface for node executors that process specific action types.
/// Each executor handles one or more ActionTypes and produces NodeOutput.
/// </summary>
/// <remarks>
/// <para>
/// Executors bridge the node-based orchestration system to actual
/// implementation. For example, AiAnalysisNodeExecutor delegates to
/// existing IAnalysisToolHandler implementations.
/// </para>
/// <para>
/// Follows the registry pattern per ADR-010 - executors register with
/// INodeExecutorRegistry for ActionType-based dispatch.
/// </para>
/// </remarks>
public interface INodeExecutor
{
    /// <summary>
    /// Gets the ActionTypes this executor can handle.
    /// Most executors handle a single type; some may handle multiple.
    /// </summary>
    IReadOnlyList<ActionType> SupportedActionTypes { get; }

    /// <summary>
    /// Executes the node and produces output.
    /// </summary>
    /// <param name="context">Execution context with node, scopes, and previous outputs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Node output containing results and metrics.</returns>
    Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates that the node configuration is valid for execution.
    /// Called before ExecuteAsync to fail fast on invalid inputs.
    /// </summary>
    /// <param name="context">Execution context to validate.</param>
    /// <returns>Validation result with success/failure and any error messages.</returns>
    NodeValidationResult Validate(NodeExecutionContext context);
}

/// <summary>
/// Action types available in the node-based playbook system.
/// Maps to sprk_analysisaction.sprk_actiontype choice values.
/// </summary>
public enum ActionType
{
    /// <summary>AI analysis using tool handlers (existing pipeline).</summary>
    AiAnalysis = 0,

    /// <summary>Raw LLM completion with prompt template.</summary>
    AiCompletion = 1,

    /// <summary>Generate embeddings for text.</summary>
    AiEmbedding = 2,

    /// <summary>Business rules evaluation.</summary>
    RuleEngine = 10,

    /// <summary>Formula/computation.</summary>
    Calculation = 11,

    /// <summary>JSON/XML transformation.</summary>
    DataTransform = 12,

    /// <summary>Create Dataverse task.</summary>
    CreateTask = 20,

    /// <summary>Send email via Microsoft Graph.</summary>
    SendEmail = 21,

    /// <summary>Update Dataverse entity.</summary>
    UpdateRecord = 22,

    /// <summary>External HTTP webhook call.</summary>
    CallWebhook = 23,

    /// <summary>Teams notification.</summary>
    SendTeamsMessage = 24,

    /// <summary>Conditional branching.</summary>
    Condition = 30,

    /// <summary>Fork into parallel paths.</summary>
    Parallel = 31,

    /// <summary>Wait for human approval.</summary>
    Wait = 32,

    /// <summary>Render and deliver final output.</summary>
    DeliverOutput = 40
}
