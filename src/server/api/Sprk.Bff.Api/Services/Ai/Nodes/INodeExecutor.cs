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
/// Coarse node category stored as a choice/option set on sprk_playbooknode.
/// Determines which scopes the orchestrator resolves before execution.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>AI — requires Action record + resolves Skills, Knowledge, Tools scopes</description></item>
/// <item><description>Output — structural; no Action or scopes needed</description></item>
/// <item><description>Control — structural; no Action or scopes needed</description></item>
/// <item><description>Workflow — future; rule-based actions, scope TBD</description></item>
/// </list>
/// </remarks>
public enum NodeType
{
    /// <summary>AI-powered node (analysis, completion, embedding). Requires Action + all scopes.</summary>
    AIAnalysis = 100_000_000,

    /// <summary>Delivery/output node. No Action or scopes — assembles previous outputs.</summary>
    Output = 100_000_001,

    /// <summary>Control flow node (condition, parallel, wait). No Action or scopes.</summary>
    Control = 100_000_002,

    /// <summary>Workflow action node (create task, send email, etc.). Future — scope TBD.</summary>
    Workflow = 100_000_003
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

    /// <summary>Start node — canvas anchor, pass-through with no execution logic.</summary>
    Start = 33,

    /// <summary>Render and deliver final output.</summary>
    DeliverOutput = 40,

    /// <summary>Queue document for RAG semantic indexing.</summary>
    DeliverToIndex = 41,

    /// <summary>Create an in-app notification via the Dataverse appnotification entity.</summary>
    CreateNotification = 50
}
