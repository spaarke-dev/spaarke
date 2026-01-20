using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Structured output schema for AI-powered intent classification.
/// Designed for Azure OpenAI structured output (JSON mode) to replace rule-based ParseIntent().
/// </summary>
/// <remarks>
/// This schema supports:
/// - Confidence-based clarification triggering
/// - Type-safe parameters per operation
/// - Multi-turn disambiguation
/// - Azure OpenAI structured output compatibility
/// </remarks>

#region Operation Types

/// <summary>
/// High-level operation categories for playbook builder intent classification.
/// Maps to the 6 primary operation categories in the design spec.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OperationType
{
    /// <summary>
    /// Build operations: Create playbook, add nodes, create edges, create scopes.
    /// Creates new artifacts on the canvas or in Dataverse.
    /// </summary>
    [JsonPropertyName("BUILD")]
    Build,

    /// <summary>
    /// Modify operations: Configure nodes, link scopes, update settings, rearrange layout.
    /// Changes existing artifacts without creating new ones.
    /// </summary>
    [JsonPropertyName("MODIFY")]
    Modify,

    /// <summary>
    /// Test operations: Run playbook test, validate configuration, preview execution.
    /// Executes or validates the playbook without persisting changes.
    /// </summary>
    [JsonPropertyName("TEST")]
    Test,

    /// <summary>
    /// Explain operations: Answer questions, describe playbook state, provide guidance.
    /// Provides information without changing canvas state.
    /// </summary>
    [JsonPropertyName("EXPLAIN")]
    Explain,

    /// <summary>
    /// Search operations: Find scopes, search available actions/skills/knowledge/tools.
    /// Queries available resources without modifying canvas.
    /// </summary>
    [JsonPropertyName("SEARCH")]
    Search,

    /// <summary>
    /// Clarify operations: Intent unclear, need more information from user.
    /// Triggers clarification flow with questions/options.
    /// </summary>
    [JsonPropertyName("CLARIFY")]
    Clarify
}

/// <summary>
/// Specific intent actions within each operation category.
/// Fine-grained classification for execution routing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntentAction
{
    // Build actions
    /// <summary>Create a complete playbook from description.</summary>
    [JsonPropertyName("CREATE_PLAYBOOK")]
    CreatePlaybook,

    /// <summary>Add a single node to the canvas.</summary>
    [JsonPropertyName("ADD_NODE")]
    AddNode,

    /// <summary>Create an edge between two nodes.</summary>
    [JsonPropertyName("CREATE_EDGE")]
    CreateEdge,

    /// <summary>Create a new custom scope in Dataverse.</summary>
    [JsonPropertyName("CREATE_SCOPE")]
    CreateScope,

    // Modify actions
    /// <summary>Remove a node from the canvas.</summary>
    [JsonPropertyName("REMOVE_NODE")]
    RemoveNode,

    /// <summary>Remove an edge between nodes.</summary>
    [JsonPropertyName("REMOVE_EDGE")]
    RemoveEdge,

    /// <summary>Configure a node's properties.</summary>
    [JsonPropertyName("CONFIGURE_NODE")]
    ConfigureNode,

    /// <summary>Link an existing scope to a node.</summary>
    [JsonPropertyName("LINK_SCOPE")]
    LinkScope,

    /// <summary>Unlink a scope from a node.</summary>
    [JsonPropertyName("UNLINK_SCOPE")]
    UnlinkScope,

    /// <summary>Rearrange canvas layout (auto-arrange).</summary>
    [JsonPropertyName("MODIFY_LAYOUT")]
    ModifyLayout,

    /// <summary>Undo the last operation.</summary>
    [JsonPropertyName("UNDO")]
    Undo,

    /// <summary>Redo a previously undone operation.</summary>
    [JsonPropertyName("REDO")]
    Redo,

    /// <summary>Save the playbook to Dataverse.</summary>
    [JsonPropertyName("SAVE_PLAYBOOK")]
    SavePlaybook,

    // Test actions
    /// <summary>Run playbook test in specified mode.</summary>
    [JsonPropertyName("TEST_PLAYBOOK")]
    TestPlaybook,

    /// <summary>Validate playbook configuration without running.</summary>
    [JsonPropertyName("VALIDATE_PLAYBOOK")]
    ValidatePlaybook,

    // Explain actions
    /// <summary>Answer a question about the playbook or builder.</summary>
    [JsonPropertyName("ANSWER_QUESTION")]
    AnswerQuestion,

    /// <summary>Describe current playbook state.</summary>
    [JsonPropertyName("DESCRIBE_STATE")]
    DescribeState,

    /// <summary>Provide guidance or suggestions.</summary>
    [JsonPropertyName("PROVIDE_GUIDANCE")]
    ProvideGuidance,

    // Search actions
    /// <summary>Search for available scopes matching criteria.</summary>
    [JsonPropertyName("SEARCH_SCOPES")]
    SearchScopes,

    /// <summary>Browse scope catalog by category.</summary>
    [JsonPropertyName("BROWSE_CATALOG")]
    BrowseCatalog,

    // Clarify actions
    /// <summary>Request clarification from user.</summary>
    [JsonPropertyName("REQUEST_CLARIFICATION")]
    RequestClarification,

    /// <summary>Confirm understanding before proceeding.</summary>
    [JsonPropertyName("CONFIRM_UNDERSTANDING")]
    ConfirmUnderstanding
}

#endregion

#region Intent Result

/// <summary>
/// The structured result from AI intent classification.
/// This is the root schema for Azure OpenAI structured output.
/// </summary>
public record AiIntentResult
{
    /// <summary>High-level operation category.</summary>
    [JsonPropertyName("operation")]
    public required OperationType Operation { get; init; }

    /// <summary>Specific intent action within the operation category.</summary>
    [JsonPropertyName("action")]
    public required IntentAction Action { get; init; }

    /// <summary>
    /// Confidence score from 0.0 to 1.0.
    /// - >= 0.80: Execute immediately
    /// - 0.60-0.79: Execute with confirmation
    /// - < 0.60: Trigger clarification
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>
    /// Parameters specific to the classified action.
    /// Null if no parameters are needed or confidence is too low.
    /// </summary>
    [JsonPropertyName("parameters")]
    public IntentParameters? Parameters { get; init; }

    /// <summary>
    /// Clarification request if confidence is below threshold.
    /// Populated when Operation is Clarify or confidence < 0.60.
    /// </summary>
    [JsonPropertyName("clarification")]
    public ClarificationRequest? Clarification { get; init; }

    /// <summary>
    /// AI reasoning for the classification decision.
    /// Useful for debugging and audit trails.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    /// <summary>
    /// Alternative interpretations if ambiguous.
    /// Populated when multiple intents could match.
    /// </summary>
    [JsonPropertyName("alternatives")]
    public AlternativeIntent[]? Alternatives { get; init; }
}

/// <summary>
/// An alternative interpretation of the user's intent.
/// </summary>
public record AlternativeIntent
{
    /// <summary>Alternative operation type.</summary>
    [JsonPropertyName("operation")]
    public required OperationType Operation { get; init; }

    /// <summary>Alternative action.</summary>
    [JsonPropertyName("action")]
    public required IntentAction Action { get; init; }

    /// <summary>Confidence for this alternative.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Brief reasoning for this alternative.</summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

#endregion

#region Intent Parameters

/// <summary>
/// Union type for action-specific parameters.
/// Only the relevant property is populated based on the action type.
/// </summary>
public record IntentParameters
{
    // Build parameters
    /// <summary>Parameters for CREATE_PLAYBOOK action.</summary>
    [JsonPropertyName("createPlaybook")]
    public CreatePlaybookParams? CreatePlaybook { get; init; }

    /// <summary>Parameters for ADD_NODE action.</summary>
    [JsonPropertyName("addNode")]
    public AddNodeParams? AddNode { get; init; }

    /// <summary>Parameters for CREATE_EDGE action.</summary>
    [JsonPropertyName("createEdge")]
    public CreateEdgeParams? CreateEdge { get; init; }

    /// <summary>Parameters for CREATE_SCOPE action.</summary>
    [JsonPropertyName("createScope")]
    public CreateScopeParams? CreateScope { get; init; }

    // Modify parameters
    /// <summary>Parameters for REMOVE_NODE action.</summary>
    [JsonPropertyName("removeNode")]
    public RemoveNodeParams? RemoveNode { get; init; }

    /// <summary>Parameters for CONFIGURE_NODE action.</summary>
    [JsonPropertyName("configureNode")]
    public ConfigureNodeParams? ConfigureNode { get; init; }

    /// <summary>Parameters for LINK_SCOPE action.</summary>
    [JsonPropertyName("linkScope")]
    public LinkScopeParams? LinkScope { get; init; }

    /// <summary>Parameters for SAVE_PLAYBOOK action.</summary>
    [JsonPropertyName("savePlaybook")]
    public SavePlaybookParams? SavePlaybook { get; init; }

    // Test parameters
    /// <summary>Parameters for TEST_PLAYBOOK action.</summary>
    [JsonPropertyName("testPlaybook")]
    public TestPlaybookParams? TestPlaybook { get; init; }

    // Search parameters
    /// <summary>Parameters for SEARCH_SCOPES action.</summary>
    [JsonPropertyName("searchScopes")]
    public SearchScopesParams? SearchScopes { get; init; }
}

#endregion

#region Build Parameters

/// <summary>
/// Parameters for creating a new playbook.
/// </summary>
public record CreatePlaybookParams
{
    /// <summary>User's description of the playbook goal.</summary>
    [JsonPropertyName("goal")]
    public required string Goal { get; init; }

    /// <summary>Target document types (optional).</summary>
    [JsonPropertyName("documentTypes")]
    public string[]? DocumentTypes { get; init; }

    /// <summary>Matter types for context (optional).</summary>
    [JsonPropertyName("matterTypes")]
    public string[]? MatterTypes { get; init; }

    /// <summary>Suggested pattern to follow (e.g., "lease-analysis").</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; init; }

    /// <summary>Estimated complexity level (1-5).</summary>
    [JsonPropertyName("complexity")]
    public int? Complexity { get; init; }
}

/// <summary>
/// Parameters for adding a node.
/// </summary>
public record AddNodeParams
{
    /// <summary>Type of node to add (from PlaybookNodeTypes).</summary>
    [JsonPropertyName("nodeType")]
    public required string NodeType { get; init; }

    /// <summary>Human-readable label for the node.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }

    /// <summary>Desired position on canvas.</summary>
    [JsonPropertyName("position")]
    public NodePositionParams? Position { get; init; }

    /// <summary>Node to connect from (for auto-edge creation).</summary>
    [JsonPropertyName("connectFrom")]
    public string? ConnectFrom { get; init; }

    /// <summary>Scope to link (if node requires a scope).</summary>
    [JsonPropertyName("scopeReference")]
    public ScopeReferenceParams? ScopeReference { get; init; }
}

/// <summary>
/// Parameters for creating an edge between nodes.
/// </summary>
public record CreateEdgeParams
{
    /// <summary>Source node reference (ID or label).</summary>
    [JsonPropertyName("sourceNode")]
    public required string SourceNode { get; init; }

    /// <summary>Target node reference (ID or label).</summary>
    [JsonPropertyName("targetNode")]
    public required string TargetNode { get; init; }

    /// <summary>Edge label (e.g., "true", "false" for conditions).</summary>
    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

/// <summary>
/// Parameters for creating a custom scope.
/// </summary>
public record CreateScopeParams
{
    /// <summary>Type of scope (action, skill, knowledge, tool).</summary>
    [JsonPropertyName("scopeType")]
    public required string ScopeType { get; init; }

    /// <summary>Name for the new scope.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Description of what the scope does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Initial content/configuration.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>Category for organization.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Whether to base on existing scope (Save As).</summary>
    [JsonPropertyName("basedOnId")]
    public string? BasedOnId { get; init; }
}

#endregion

#region Modify Parameters

/// <summary>
/// Parameters for removing a node.
/// </summary>
public record RemoveNodeParams
{
    /// <summary>Node reference (ID or label) to remove.</summary>
    [JsonPropertyName("nodeReference")]
    public required string NodeReference { get; init; }

    /// <summary>Whether to also remove connected edges.</summary>
    [JsonPropertyName("removeEdges")]
    public bool RemoveEdges { get; init; } = true;
}

/// <summary>
/// Parameters for configuring a node.
/// </summary>
public record ConfigureNodeParams
{
    /// <summary>Node reference (ID or label) to configure.</summary>
    [JsonPropertyName("nodeReference")]
    public required string NodeReference { get; init; }

    /// <summary>Property to update.</summary>
    [JsonPropertyName("property")]
    public required string Property { get; init; }

    /// <summary>New value for the property.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// Parameters for linking a scope to a node.
/// </summary>
public record LinkScopeParams
{
    /// <summary>Node reference (ID or label) to link to.</summary>
    [JsonPropertyName("nodeReference")]
    public required string NodeReference { get; init; }

    /// <summary>Scope reference to link.</summary>
    [JsonPropertyName("scopeReference")]
    public required ScopeReferenceParams ScopeReference { get; init; }
}

/// <summary>
/// Parameters for saving a playbook.
/// </summary>
public record SavePlaybookParams
{
    /// <summary>New name for the playbook (if renaming or save as).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether this is a Save As operation.</summary>
    [JsonPropertyName("saveAsNew")]
    public bool SaveAsNew { get; init; }
}

#endregion

#region Test Parameters

/// <summary>
/// Parameters for testing a playbook.
/// </summary>
public record TestPlaybookParams
{
    /// <summary>Test mode: mock, quick, or production.</summary>
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>Maximum nodes to execute (optional limit).</summary>
    [JsonPropertyName("maxNodes")]
    public int? MaxNodes { get; init; }

    /// <summary>Specific node to start from (optional).</summary>
    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; init; }

    /// <summary>Test document reference (for quick/production modes).</summary>
    [JsonPropertyName("testDocumentId")]
    public string? TestDocumentId { get; init; }
}

#endregion

#region Search Parameters

/// <summary>
/// Parameters for searching scopes.
/// </summary>
public record SearchScopesParams
{
    /// <summary>Search text query.</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>Scope types to search (action, skill, knowledge, tool).</summary>
    [JsonPropertyName("scopeTypes")]
    public string[]? ScopeTypes { get; init; }

    /// <summary>Category filter.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>Whether to include system scopes.</summary>
    [JsonPropertyName("includeSystem")]
    public bool IncludeSystem { get; init; } = true;

    /// <summary>Maximum results to return.</summary>
    [JsonPropertyName("maxResults")]
    public int MaxResults { get; init; } = 10;
}

#endregion

#region Shared Parameter Types

/// <summary>
/// Position parameters for canvas placement.
/// </summary>
public record NodePositionParams
{
    /// <summary>X coordinate.</summary>
    [JsonPropertyName("x")]
    public double X { get; init; }

    /// <summary>Y coordinate.</summary>
    [JsonPropertyName("y")]
    public double Y { get; init; }

    /// <summary>Relative position (e.g., "after:node1", "below:node2").</summary>
    [JsonPropertyName("relative")]
    public string? Relative { get; init; }
}

/// <summary>
/// Reference to a scope (for linking or searching).
/// </summary>
public record ScopeReferenceParams
{
    /// <summary>Scope type (action, skill, knowledge, tool).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Scope ID (if known).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Scope name (for search).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Search query (for fuzzy matching).</summary>
    [JsonPropertyName("searchQuery")]
    public string? SearchQuery { get; init; }
}

#endregion

#region Clarification Schema

/// <summary>
/// Clarification request when intent is unclear or ambiguous.
/// </summary>
public record ClarificationRequest
{
    /// <summary>The clarification question to ask the user.</summary>
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    /// <summary>Type of clarification needed.</summary>
    [JsonPropertyName("type")]
    public required ClarificationType Type { get; init; }

    /// <summary>Predefined options for the user to choose from.</summary>
    [JsonPropertyName("options")]
    public ClarificationOption[]? Options { get; init; }

    /// <summary>Context that triggered the clarification need.</summary>
    [JsonPropertyName("context")]
    public ClarificationContext? Context { get; init; }

    /// <summary>Whether user can provide free-form response.</summary>
    [JsonPropertyName("allowFreeform")]
    public bool AllowFreeform { get; init; } = true;

    /// <summary>Suggested follow-up prompts.</summary>
    [JsonPropertyName("suggestions")]
    public string[]? Suggestions { get; init; }
}

/// <summary>
/// Type of clarification needed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClarificationType
{
    /// <summary>Which intent did the user mean?</summary>
    [JsonPropertyName("INTENT_DISAMBIGUATION")]
    IntentDisambiguation,

    /// <summary>Which entity was the user referring to?</summary>
    [JsonPropertyName("ENTITY_DISAMBIGUATION")]
    EntityDisambiguation,

    /// <summary>Missing required parameter.</summary>
    [JsonPropertyName("MISSING_PARAMETER")]
    MissingParameter,

    /// <summary>Confirmation before destructive action.</summary>
    [JsonPropertyName("CONFIRMATION")]
    Confirmation,

    /// <summary>Multiple matches found, need selection.</summary>
    [JsonPropertyName("SELECTION")]
    Selection,

    /// <summary>General clarification needed.</summary>
    [JsonPropertyName("GENERAL")]
    General
}

/// <summary>
/// Context information for clarification.
/// </summary>
public record ClarificationContext
{
    /// <summary>What the AI understood from the message.</summary>
    [JsonPropertyName("understood")]
    public string? Understood { get; init; }

    /// <summary>What was unclear or ambiguous.</summary>
    [JsonPropertyName("unclear")]
    public string? Unclear { get; init; }

    /// <summary>Related canvas elements (node IDs, etc.).</summary>
    [JsonPropertyName("relatedElements")]
    public string[]? RelatedElements { get; init; }

    /// <summary>Previous messages in context (for multi-turn).</summary>
    [JsonPropertyName("previousContext")]
    public string? PreviousContext { get; init; }
}

/// <summary>
/// A clarification option for the user to select.
/// Extends the existing ClarificationOption with additional fields.
/// </summary>
public record ClarificationOptionExtended
{
    /// <summary>Option identifier for programmatic handling.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Display label for the option.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>Longer description of what this option means.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The intent this option would resolve to.</summary>
    [JsonPropertyName("resolvedIntent")]
    public ResolvedIntentInfo? ResolvedIntent { get; init; }

    /// <summary>Whether this is the recommended option.</summary>
    [JsonPropertyName("recommended")]
    public bool Recommended { get; init; }
}

/// <summary>
/// Information about the resolved intent for an option.
/// </summary>
public record ResolvedIntentInfo
{
    /// <summary>The operation type.</summary>
    [JsonPropertyName("operation")]
    public OperationType Operation { get; init; }

    /// <summary>The specific action.</summary>
    [JsonPropertyName("action")]
    public IntentAction Action { get; init; }

    /// <summary>Pre-filled parameters.</summary>
    [JsonPropertyName("parameters")]
    public IntentParameters? Parameters { get; init; }
}

#endregion

#region Classification Context

/// <summary>
/// Context provided to the AI for intent classification.
/// Includes canvas state and conversation history.
/// </summary>
public record IntentClassificationContext
{
    /// <summary>The user's message to classify.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Current canvas state summary.</summary>
    [JsonPropertyName("canvas")]
    public CanvasContextSummary? Canvas { get; init; }

    /// <summary>Recent conversation history (last N messages).</summary>
    [JsonPropertyName("conversationHistory")]
    public ConversationMessage[]? ConversationHistory { get; init; }

    /// <summary>Playbook metadata if editing existing.</summary>
    [JsonPropertyName("playbook")]
    public PlaybookContextInfo? Playbook { get; init; }

    /// <summary>User preferences/settings.</summary>
    [JsonPropertyName("userPreferences")]
    public UserPreferences? UserPreferences { get; init; }
}

/// <summary>
/// Summary of canvas state for classification context.
/// </summary>
public record CanvasContextSummary
{
    /// <summary>Total number of nodes on canvas.</summary>
    [JsonPropertyName("nodeCount")]
    public int NodeCount { get; init; }

    /// <summary>Total number of edges.</summary>
    [JsonPropertyName("edgeCount")]
    public int EdgeCount { get; init; }

    /// <summary>List of node summaries.</summary>
    [JsonPropertyName("nodes")]
    public CanvasNodeInfo[]? Nodes { get; init; }

    /// <summary>Currently selected node ID.</summary>
    [JsonPropertyName("selectedNodeId")]
    public string? SelectedNodeId { get; init; }

    /// <summary>Whether the canvas has unsaved changes.</summary>
    [JsonPropertyName("hasUnsavedChanges")]
    public bool HasUnsavedChanges { get; init; }
}

/// <summary>
/// Node information for classification context.
/// </summary>
public record CanvasNodeInfo
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

    /// <summary>Linked scope IDs.</summary>
    [JsonPropertyName("linkedScopes")]
    public string[]? LinkedScopes { get; init; }

    /// <summary>Whether the node is fully configured.</summary>
    [JsonPropertyName("isConfigured")]
    public bool IsConfigured { get; init; }
}

/// <summary>
/// A message in the conversation history.
/// </summary>
public record ConversationMessage
{
    /// <summary>Role: user or assistant.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Message content.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Classified intent (for assistant messages).</summary>
    [JsonPropertyName("classifiedIntent")]
    public IntentAction? ClassifiedIntent { get; init; }
}

/// <summary>
/// Playbook metadata for context.
/// </summary>
public record PlaybookContextInfo
{
    /// <summary>Playbook ID (if saved).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Playbook name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Target document types.</summary>
    [JsonPropertyName("documentTypes")]
    public string[]? DocumentTypes { get; init; }
}

/// <summary>
/// User preferences that may affect classification.
/// </summary>
public record UserPreferences
{
    /// <summary>Preferred test mode.</summary>
    [JsonPropertyName("preferredTestMode")]
    public string? PreferredTestMode { get; init; }

    /// <summary>Default scope ownership (customer/system).</summary>
    [JsonPropertyName("defaultOwnership")]
    public string? DefaultOwnership { get; init; }

    /// <summary>Whether to auto-arrange after adding nodes.</summary>
    [JsonPropertyName("autoArrange")]
    public bool AutoArrange { get; init; }
}

#endregion

#region Clarification Response

/// <summary>
/// User's response to a clarification request.
/// Used to provide additional context for re-classification.
/// </summary>
public record ClarificationResponse
{
    /// <summary>
    /// The clarification session ID (correlates with the original request).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    /// <summary>
    /// The type of response provided.
    /// </summary>
    [JsonPropertyName("responseType")]
    public required ClarificationResponseType ResponseType { get; init; }

    /// <summary>
    /// The selected option ID (if user selected from options).
    /// </summary>
    [JsonPropertyName("selectedOptionId")]
    public string? SelectedOptionId { get; init; }

    /// <summary>
    /// Free-form text response from the user.
    /// </summary>
    [JsonPropertyName("freeTextResponse")]
    public string? FreeTextResponse { get; init; }

    /// <summary>
    /// The original message that triggered clarification.
    /// </summary>
    [JsonPropertyName("originalMessage")]
    public string? OriginalMessage { get; init; }

    /// <summary>
    /// The original classification result (for context).
    /// </summary>
    [JsonPropertyName("originalClassification")]
    public AiIntentResult? OriginalClassification { get; init; }

    /// <summary>
    /// Additional context from the original clarification request.
    /// </summary>
    [JsonPropertyName("clarificationContext")]
    public ClarificationContext? ClarificationContext { get; init; }
}

/// <summary>
/// Type of response to a clarification request.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClarificationResponseType
{
    /// <summary>User selected one of the provided options.</summary>
    [JsonPropertyName("OPTION_SELECTED")]
    OptionSelected,

    /// <summary>User provided free-form text.</summary>
    [JsonPropertyName("FREE_TEXT")]
    FreeText,

    /// <summary>User cancelled the operation.</summary>
    [JsonPropertyName("CANCELLED")]
    Cancelled,

    /// <summary>User confirmed the suggested action.</summary>
    [JsonPropertyName("CONFIRMED")]
    Confirmed,

    /// <summary>User rejected the suggested action.</summary>
    [JsonPropertyName("REJECTED")]
    Rejected
}

/// <summary>
/// Extended clarification question with structured metadata for UI rendering.
/// </summary>
public record ClarificationQuestion
{
    /// <summary>
    /// Unique ID for this clarification question.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The question text to display to the user.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>
    /// Type of clarification being requested.
    /// </summary>
    [JsonPropertyName("type")]
    public required ClarificationType Type { get; init; }

    /// <summary>
    /// Options for the user to choose from.
    /// </summary>
    [JsonPropertyName("options")]
    public ClarificationOption[]? Options { get; init; }

    /// <summary>
    /// Whether free-form text input is allowed.
    /// </summary>
    [JsonPropertyName("allowFreeText")]
    public bool AllowFreeText { get; init; } = true;

    /// <summary>
    /// Placeholder text for free-form input field.
    /// </summary>
    [JsonPropertyName("freeTextPlaceholder")]
    public string? FreeTextPlaceholder { get; init; }

    /// <summary>
    /// Suggested quick responses.
    /// </summary>
    [JsonPropertyName("suggestions")]
    public string[]? Suggestions { get; init; }

    /// <summary>
    /// The ambiguity that triggered this clarification.
    /// </summary>
    [JsonPropertyName("ambiguityReason")]
    public string? AmbiguityReason { get; init; }

    /// <summary>
    /// What the AI understood from the original message.
    /// </summary>
    [JsonPropertyName("understoodContext")]
    public string? UnderstoodContext { get; init; }

    /// <summary>
    /// Timestamp when this clarification was generated.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

#endregion

#region Constants

/// <summary>
/// Confidence thresholds for intent classification decisions.
/// </summary>
public static class IntentConfidenceThresholds
{
    /// <summary>Execute immediately without confirmation.</summary>
    public const double HighConfidence = 0.80;

    /// <summary>Execute with optional confirmation.</summary>
    public const double MediumConfidence = 0.60;

    /// <summary>Below this requires clarification.</summary>
    public const double LowConfidence = 0.60;

    /// <summary>Entity resolution confidence threshold.</summary>
    public const double EntityResolution = 0.80;
}

/// <summary>
/// Test mode values.
/// </summary>
public static class TestModeValues
{
    public const string Mock = "mock";
    public const string Quick = "quick";
    public const string Production = "production";
}

#endregion
