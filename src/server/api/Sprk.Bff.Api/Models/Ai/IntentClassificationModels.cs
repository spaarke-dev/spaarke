using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// The 11 builder intent categories per design spec.
/// Used for classifying user messages in the playbook builder.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BuilderIntentCategory
{
    /// <summary>Build a complete playbook from scratch.</summary>
    [JsonPropertyName("CREATE_PLAYBOOK")]
    CreatePlaybook,

    /// <summary>Add a single node to the canvas.</summary>
    [JsonPropertyName("ADD_NODE")]
    AddNode,

    /// <summary>Remove a node from the canvas.</summary>
    [JsonPropertyName("REMOVE_NODE")]
    RemoveNode,

    /// <summary>Connect two nodes with an edge.</summary>
    [JsonPropertyName("CONNECT_NODES")]
    ConnectNodes,

    /// <summary>Configure a node's properties.</summary>
    [JsonPropertyName("CONFIGURE_NODE")]
    ConfigureNode,

    /// <summary>Link an existing scope to a node.</summary>
    [JsonPropertyName("LINK_SCOPE")]
    LinkScope,

    /// <summary>Create a new custom scope.</summary>
    [JsonPropertyName("CREATE_SCOPE")]
    CreateScope,

    /// <summary>Query about playbook state or ask for help.</summary>
    [JsonPropertyName("QUERY_STATUS")]
    QueryStatus,

    /// <summary>Modify visual layout (auto-arrange).</summary>
    [JsonPropertyName("MODIFY_LAYOUT")]
    ModifyLayout,

    /// <summary>Undo the last operation.</summary>
    [JsonPropertyName("UNDO")]
    Undo,

    /// <summary>Intent unclear, needs clarification.</summary>
    [JsonPropertyName("UNCLEAR")]
    Unclear
}

/// <summary>
/// Result from AI intent classification.
/// Contains the classified intent, confidence, and extracted entities.
/// </summary>
public record IntentClassificationResponse
{
    /// <summary>The classified intent category.</summary>
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    /// <summary>Confidence score from 0.0 to 1.0.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Extracted entities from the message.</summary>
    [JsonPropertyName("entities")]
    public IntentEntities? Entities { get; init; }

    /// <summary>Whether clarification is needed.</summary>
    [JsonPropertyName("needsClarification")]
    public bool NeedsClarification { get; init; }

    /// <summary>Clarification question if needed.</summary>
    [JsonPropertyName("clarificationQuestion")]
    public string? ClarificationQuestion { get; init; }

    /// <summary>Options for clarification if multiple matches.</summary>
    [JsonPropertyName("clarificationOptions")]
    public ClarificationOption[]? ClarificationOptions { get; init; }

    /// <summary>AI reasoning for the classification.</summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

/// <summary>
/// Entities extracted from user message during classification.
/// </summary>
public record IntentEntities
{
    // Node-related entities

    /// <summary>Type of node (aiAnalysis, condition, etc.).</summary>
    [JsonPropertyName("nodeType")]
    public string? NodeType { get; init; }

    /// <summary>Reference to existing node by ID.</summary>
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; init; }

    /// <summary>Human-readable label for node.</summary>
    [JsonPropertyName("nodeLabel")]
    public string? NodeLabel { get; init; }

    /// <summary>Desired position on canvas.</summary>
    [JsonPropertyName("position")]
    public PositionEntity? Position { get; init; }

    // Scope-related entities

    /// <summary>Type of scope (action, skill, knowledge, tool).</summary>
    [JsonPropertyName("scopeType")]
    public string? ScopeType { get; init; }

    /// <summary>Scope ID (GUID string).</summary>
    [JsonPropertyName("scopeId")]
    public string? ScopeId { get; init; }

    /// <summary>Name or description of scope to find/create.</summary>
    [JsonPropertyName("scopeName")]
    public string? ScopeName { get; init; }

    // Connection entities

    /// <summary>Source node reference (ID or label).</summary>
    [JsonPropertyName("sourceNode")]
    public string? SourceNode { get; init; }

    /// <summary>Target node reference (ID or label).</summary>
    [JsonPropertyName("targetNode")]
    public string? TargetNode { get; init; }

    // Configuration entities

    /// <summary>Property key to modify.</summary>
    [JsonPropertyName("configKey")]
    public string? ConfigKey { get; init; }

    /// <summary>New value for property.</summary>
    [JsonPropertyName("configValue")]
    public string? ConfigValue { get; init; }

    /// <summary>Output variable name.</summary>
    [JsonPropertyName("outputVariable")]
    public string? OutputVariable { get; init; }
}

/// <summary>
/// Position entity for canvas placement.
/// </summary>
public record PositionEntity
{
    /// <summary>X coordinate.</summary>
    [JsonPropertyName("x")]
    public double X { get; init; }

    /// <summary>Y coordinate.</summary>
    [JsonPropertyName("y")]
    public double Y { get; init; }
}

/// <summary>
/// An option presented during clarification.
/// </summary>
public record ClarificationOption
{
    /// <summary>Option identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Display label.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Canvas context passed to intent classification for disambiguation.
/// </summary>
public record ClassificationCanvasContext
{
    /// <summary>Number of nodes on canvas.</summary>
    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; init; }

    /// <summary>Number of edges on canvas.</summary>
    [JsonPropertyName("edgeCount")]
    public int EdgeCount { get; init; }

    /// <summary>List of nodes with their IDs, types, and labels.</summary>
    [JsonPropertyName("nodes")]
    public CanvasNodeSummary[]? Nodes { get; init; }

    /// <summary>Currently selected node ID.</summary>
    [JsonPropertyName("selectedNodeId")]
    public string? SelectedNodeId { get; init; }

    /// <summary>Whether playbook has been saved.</summary>
    [JsonPropertyName("isSaved")]
    public bool IsSaved { get; init; }
}

/// <summary>
/// Summary of a canvas node for classification context.
/// </summary>
public record CanvasNodeSummary
{
    /// <summary>Node ID.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Node type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Display label.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }
}
