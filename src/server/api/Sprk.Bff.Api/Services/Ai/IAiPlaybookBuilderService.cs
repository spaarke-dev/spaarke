using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates AI-assisted playbook building operations.
/// Coordinates intent classification, build plan generation, and canvas operations.
/// Implements ADR-001 (BFF orchestration pattern) and ADR-013 (AI Architecture).
/// </summary>
public interface IAiPlaybookBuilderService
{
    /// <summary>
    /// Process a user message and generate canvas operations.
    /// Handles the complete flow: intent classification, entity resolution,
    /// build plan generation, and canvas patch creation.
    /// </summary>
    /// <param name="request">The builder request with user message and canvas state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of stream chunks for SSE response.</returns>
    IAsyncEnumerable<BuilderStreamChunk> ProcessMessageAsync(
        BuilderRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generate a build plan from a user's high-level request.
    /// Creates a structured plan with steps to build the requested playbook.
    /// </summary>
    /// <param name="request">The plan generation request with user goal and context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated build plan.</returns>
    Task<BuildPlan> GenerateBuildPlanAsync(
        BuildPlanRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Classify the intent of a user message.
    /// Returns the identified intent type and confidence score.
    /// </summary>
    /// <param name="message">The user message to classify.</param>
    /// <param name="canvasContext">Current canvas context for disambiguation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with intent type and confidence.</returns>
    Task<IntentClassification> ClassifyIntentAsync(
        string message,
        CanvasContext? canvasContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Execute a playbook test with streaming progress.
    /// Supports Mock, Quick, and Production test modes.
    /// </summary>
    /// <param name="request">Test execution request with mode and playbook configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of test execution events for SSE streaming.</returns>
    IAsyncEnumerable<Models.Ai.TestExecutionEvent> ExecuteTestAsync(
        Models.Ai.TestPlaybookRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Request for processing a builder message.
/// </summary>
public record BuilderRequest
{
    /// <summary>The user's message/command.</summary>
    public required string Message { get; init; }

    /// <summary>Current canvas state (nodes and edges).</summary>
    public required CanvasState CanvasState { get; init; }

    /// <summary>Optional playbook ID if editing existing playbook.</summary>
    public Guid? PlaybookId { get; init; }

    /// <summary>Session ID for conversation continuity.</summary>
    public string? SessionId { get; init; }

    /// <summary>Previous messages in the conversation.</summary>
    public BuilderChatMessage[]? ChatHistory { get; init; }

    /// <summary>
    /// User's response to a clarification request (if this is a continuation of a clarification flow).
    /// When provided, the service will re-classify using this context.
    /// </summary>
    public Models.Ai.ClarificationResponse? ClarificationResponse { get; init; }

    /// <summary>
    /// Indicates whether this request is a response to a clarification question.
    /// </summary>
    public bool IsClarificationResponse => ClarificationResponse != null;
}

/// <summary>
/// Current state of the playbook canvas.
/// </summary>
public record CanvasState
{
    /// <summary>Nodes on the canvas.</summary>
    public CanvasNode[] Nodes { get; init; } = [];

    /// <summary>Edges connecting nodes.</summary>
    public CanvasEdge[] Edges { get; init; } = [];

    /// <summary>Playbook metadata.</summary>
    public PlaybookMetadata? Metadata { get; init; }

    /// <summary>
    /// Linked scopes by type (actions, skills, knowledge, tools, outputs).
    /// Key is scope type, value is array of linked scope IDs.
    /// </summary>
    public Dictionary<string, Guid[]>? LinkedScopes { get; init; }
}

/// <summary>
/// A node on the playbook canvas.
/// Matches TypeScript PlaybookNode structure from canvasStore.ts.
/// </summary>
public record CanvasNode
{
    /// <summary>Unique node ID.</summary>
    public required string Id { get; init; }

    /// <summary>Node type (aiAnalysis, aiCompletion, condition, deliverOutput, etc.).</summary>
    public required string Type { get; init; }

    /// <summary>Display label.</summary>
    public string? Label { get; init; }

    /// <summary>Node position on canvas.</summary>
    public NodePosition? Position { get; init; }

    /// <summary>Node configuration data (generic key-value pairs).</summary>
    public Dictionary<string, object?>? Config { get; init; }

    /// <summary>Linked scope ID (if applicable).</summary>
    public Guid? ScopeId { get; init; }

    // PlaybookNodeData properties from TypeScript canvasStore.ts

    /// <summary>Action ID reference.</summary>
    public string? ActionId { get; init; }

    /// <summary>Output variable name for this node's result.</summary>
    public string? OutputVariable { get; init; }

    /// <summary>Timeout in seconds for node execution.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Retry count for failed executions.</summary>
    public int? RetryCount { get; init; }

    /// <summary>JSON condition expression (for condition nodes).</summary>
    public string? ConditionJson { get; init; }

    /// <summary>Linked skill IDs.</summary>
    public string[]? SkillIds { get; init; }

    /// <summary>Linked knowledge IDs.</summary>
    public string[]? KnowledgeIds { get; init; }

    /// <summary>Linked tool ID.</summary>
    public string? ToolId { get; init; }

    /// <summary>AI model deployment ID (for aiAnalysis, aiCompletion nodes).</summary>
    public string? ModelDeploymentId { get; init; }
}

/// <summary>
/// Node position on the canvas.
/// </summary>
public record NodePosition(double X, double Y);

/// <summary>
/// An edge connecting two nodes.
/// Matches TypeScript Edge structure from React Flow.
/// </summary>
public record CanvasEdge
{
    /// <summary>Unique edge ID.</summary>
    public required string Id { get; init; }

    /// <summary>Source node ID.</summary>
    public required string SourceId { get; init; }

    /// <summary>Target node ID.</summary>
    public required string TargetId { get; init; }

    /// <summary>Source handle/port.</summary>
    public string? SourceHandle { get; init; }

    /// <summary>Target handle/port.</summary>
    public string? TargetHandle { get; init; }

    /// <summary>Edge type (smoothstep, trueBranch, falseBranch, etc.).</summary>
    public string? EdgeType { get; init; }

    /// <summary>Whether the edge should animate.</summary>
    public bool? Animated { get; init; }
}

/// <summary>
/// Playbook metadata.
/// </summary>
public record PlaybookMetadata
{
    /// <summary>Playbook name.</summary>
    public string? Name { get; init; }

    /// <summary>Playbook description.</summary>
    public string? Description { get; init; }

    /// <summary>Playbook category.</summary>
    public string? Category { get; init; }
}

/// <summary>
/// Context about the current canvas state for intent classification.
/// </summary>
public record CanvasContext
{
    /// <summary>Number of nodes on canvas.</summary>
    public int NodeCount { get; init; }

    /// <summary>Types of nodes present.</summary>
    public string[] NodeTypes { get; init; } = [];

    /// <summary>Whether playbook has been saved.</summary>
    public bool IsSaved { get; init; }

    /// <summary>Currently selected node ID.</summary>
    public string? SelectedNodeId { get; init; }
}

/// <summary>
/// Chat message in builder conversation history.
/// Named BuilderChatMessage to avoid conflict with OpenAI.Chat.ChatMessage.
/// </summary>
public record BuilderChatMessage
{
    /// <summary>Message role (user/assistant/system).</summary>
    public required string Role { get; init; }

    /// <summary>Message content.</summary>
    public required string Content { get; init; }

    /// <summary>Timestamp.</summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Request for generating a build plan.
/// </summary>
public record BuildPlanRequest
{
    /// <summary>User's high-level goal or description.</summary>
    public required string Goal { get; init; }

    /// <summary>Document type being analyzed (e.g., lease, contract).</summary>
    public string? DocumentType { get; init; }

    /// <summary>Reference playbook IDs for patterns.</summary>
    public Guid[]? ReferencePlaybookIds { get; init; }
}

// NOTE: BuildPlan and related types have been moved to Sprk.Bff.Api.Models.Ai.BuildPlanModels
// with enhanced functionality. Use the Models.Ai versions for all new code.

/// <summary>
/// Result of intent classification.
/// </summary>
public record IntentClassification
{
    /// <summary>Classified intent type.</summary>
    public required BuilderIntent Intent { get; init; }

    /// <summary>Confidence score (0-1).</summary>
    public double Confidence { get; init; }

    /// <summary>Extracted entities from the message.</summary>
    public Dictionary<string, string>? Entities { get; init; }

    /// <summary>Whether clarification is needed (confidence below threshold).</summary>
    public bool NeedsClarification { get; init; }

    /// <summary>Clarification question if needed.</summary>
    public string? ClarificationQuestion { get; init; }

    /// <summary>
    /// Conversational message from the AI to display to the user.
    /// This is the AI's friendly explanation of what it's doing.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Builder intent types per spec.
/// </summary>
public enum BuilderIntent
{
    /// <summary>Create a new playbook from scratch.</summary>
    CreatePlaybook,

    /// <summary>Add a node to the canvas.</summary>
    AddNode,

    /// <summary>Remove a node from the canvas.</summary>
    RemoveNode,

    /// <summary>Connect two nodes with an edge.</summary>
    ConnectNodes,

    /// <summary>Configure a node's settings.</summary>
    ConfigureNode,

    /// <summary>Search for available scopes.</summary>
    SearchScopes,

    /// <summary>Create a new custom scope.</summary>
    CreateScope,

    /// <summary>Link a scope to a node.</summary>
    LinkScope,

    /// <summary>Run a test execution.</summary>
    TestPlaybook,

    /// <summary>Save the playbook.</summary>
    SavePlaybook,

    /// <summary>General question or help request.</summary>
    AskQuestion,

    /// <summary>Intent could not be determined.</summary>
    Unknown
}

/// <summary>
/// A chunk of the builder response stream.
/// </summary>
public record BuilderStreamChunk
{
    /// <summary>Chunk type.</summary>
    public required BuilderChunkType Type { get; init; }

    /// <summary>Text content for message chunks.</summary>
    public string? Text { get; init; }

    /// <summary>Canvas patch for operation chunks.</summary>
    public CanvasPatch? Patch { get; init; }

    /// <summary>Error message if applicable.</summary>
    public string? Error { get; init; }

    /// <summary>Metadata for the chunk.</summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>Create a text message chunk.</summary>
    public static BuilderStreamChunk Message(string text) =>
        new() { Type = BuilderChunkType.Message, Text = text };

    /// <summary>Create a canvas operation chunk.</summary>
    public static BuilderStreamChunk Operation(CanvasPatch patch) =>
        new() { Type = BuilderChunkType.CanvasOperation, Patch = patch };

    /// <summary>Create a clarification request chunk.</summary>
    public static BuilderStreamChunk Clarification(string question) =>
        new() { Type = BuilderChunkType.Clarification, Text = question };

    /// <summary>Create a completion chunk.</summary>
    public static BuilderStreamChunk Complete() =>
        new() { Type = BuilderChunkType.Complete };

    /// <summary>Create an error chunk.</summary>
    public static BuilderStreamChunk ErrorChunk(string error) =>
        new() { Type = BuilderChunkType.Error, Error = error };
}

/// <summary>
/// Types of builder stream chunks.
/// </summary>
public enum BuilderChunkType
{
    /// <summary>Text message to display.</summary>
    Message,

    /// <summary>Canvas operation to apply.</summary>
    CanvasOperation,

    /// <summary>Clarification request.</summary>
    Clarification,

    /// <summary>Plan preview.</summary>
    PlanPreview,

    /// <summary>Stream complete.</summary>
    Complete,

    /// <summary>Error occurred.</summary>
    Error
}

/// <summary>
/// A patch to apply to the canvas.
/// Supports two modes:
/// 1. Individual operations (for streaming): Set Operation + Node/Edge
/// 2. Batch operations (for bulk updates): Set AddNodes/RemoveNodeIds/etc.
/// </summary>
public record CanvasPatch
{
    // Individual operation mode (for SSE streaming - one operation at a time)

    /// <summary>Operation type for individual operations. Null for batch mode.</summary>
    public CanvasPatchOperation? Operation { get; init; }

    /// <summary>Target node ID (for update/remove operations).</summary>
    public string? NodeId { get; init; }

    /// <summary>Target edge ID (for edge operations).</summary>
    public string? EdgeId { get; init; }

    /// <summary>Node data (for add/update operations).</summary>
    public CanvasNode? Node { get; init; }

    /// <summary>Edge data (for edge operations).</summary>
    public CanvasEdge? Edge { get; init; }

    /// <summary>Configuration updates (for configure operations).</summary>
    public Dictionary<string, object?>? Config { get; init; }

    // Batch operation mode (per spec - multiple operations at once)

    /// <summary>Nodes to add to the canvas.</summary>
    public CanvasNode[]? AddNodes { get; init; }

    /// <summary>Node IDs to remove from the canvas.</summary>
    public string[]? RemoveNodeIds { get; init; }

    /// <summary>Nodes to update (partial updates supported).</summary>
    public CanvasNode[]? UpdateNodes { get; init; }

    /// <summary>Edges to add to the canvas.</summary>
    public CanvasEdge[]? AddEdges { get; init; }

    /// <summary>Edge IDs to remove from the canvas.</summary>
    public string[]? RemoveEdgeIds { get; init; }
}

/// <summary>
/// Canvas patch operation types.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CanvasPatchOperation
{
    /// <summary>Add a new node.</summary>
    AddNode,

    /// <summary>Remove a node.</summary>
    RemoveNode,

    /// <summary>Update a node.</summary>
    UpdateNode,

    /// <summary>Add a new edge.</summary>
    AddEdge,

    /// <summary>Remove an edge.</summary>
    RemoveEdge,

    /// <summary>Update node configuration.</summary>
    ConfigureNode,

    /// <summary>Link a scope to a node.</summary>
    LinkScope,

    /// <summary>Auto-layout the canvas.</summary>
    AutoLayout
}
