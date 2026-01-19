using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Prompts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for classifying user intent from natural language messages.
/// Uses AI to determine which of the 11 intent categories a message belongs to,
/// extract relevant entities, and determine if clarification is needed.
/// </summary>
/// <remarks>
/// Implements intent classification per the AI Chat Playbook Builder design:
/// - 11 intent categories (CREATE_PLAYBOOK, ADD_NODE, etc.)
/// - Confidence scoring with 0.75 threshold for clarification
/// - Entity extraction for nodes, scopes, connections
/// - Structured JSON response parsing
/// </remarks>
public class IntentClassificationService : IIntentClassificationService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<IntentClassificationService> _logger;

    // Model for intent classification (fast, cost-effective)
    private const string ClassificationModel = "gpt-4o-mini";

    // JSON serialization options for parsing AI response
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IntentClassificationService(
        IOpenAiClient openAiClient,
        ILogger<IntentClassificationService> logger)
    {
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IntentClassificationResult> ClassifyAsync(
        string message,
        ClassificationCanvasContext? canvasContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        _logger.LogDebug("Classifying intent for message: {Message}", message);

        try
        {
            // Build the prompt with canvas context
            var prompt = BuildClassificationPrompt(message, canvasContext);

            // Call AI for classification
            var response = await _openAiClient.GetCompletionAsync(
                prompt,
                ClassificationModel,
                cancellationToken);

            // Parse the structured response
            var result = ParseClassificationResponse(response, message);

            _logger.LogInformation(
                "Intent classified: {Intent} with confidence {Confidence:F2}",
                result.Intent,
                result.Confidence);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse classification response, using fallback");
            return CreateFallbackResult(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during intent classification");
            throw;
        }
    }

    /// <summary>
    /// Build the complete prompt for intent classification.
    /// </summary>
    private static string BuildClassificationPrompt(
        string message,
        ClassificationCanvasContext? canvasContext)
    {
        var systemPrompt = PlaybookBuilderSystemPrompt.IntentClassification;
        var canvasSection = BuildCanvasContextSection(canvasContext);

        return $"""
            {systemPrompt}

            ## Current Canvas State

            {canvasSection}

            ## User Message

            Message: {message}

            ## Instructions

            Analyze the user's message and classify it into one of the 11 intent categories.
            Extract any relevant entities mentioned in the message.
            Return your response as valid JSON matching the specified format.

            IMPORTANT:
            - Return ONLY the JSON object, no markdown code blocks or explanations.
            - Use the exact field names: intent, confidence, entities, needsClarification, clarificationQuestion, clarificationOptions, reasoning
            - Intent must be one of: CREATE_PLAYBOOK, ADD_NODE, REMOVE_NODE, CONNECT_NODES, CONFIGURE_NODE, LINK_SCOPE, CREATE_SCOPE, QUERY_STATUS, MODIFY_LAYOUT, UNDO, UNCLEAR
            """;
    }

    /// <summary>
    /// Build canvas context section for the prompt.
    /// </summary>
    private static string BuildCanvasContextSection(ClassificationCanvasContext? context)
    {
        if (context == null || context.NodeCount == 0)
        {
            return """
                Canvas is empty (new playbook).
                - No nodes
                - No edges
                - Not saved
                """;
        }

        var nodeList = context.Nodes != null && context.Nodes.Length > 0
            ? string.Join("\n", context.Nodes.Select(n =>
                $"  - {n.Id}: {n.Type} \"{n.Label ?? "unnamed"}\""))
            : "  (no node details available)";

        return $"""
            Current state:
            - {context.NodeCount} nodes, {context.EdgeCount} edges
            - Selected node: {context.SelectedNodeId ?? "none"}
            - Status: {(context.IsSaved ? "Saved" : "Unsaved")}

            Nodes:
            {nodeList}
            """;
    }

    /// <summary>
    /// Parse the AI response into a structured result.
    /// </summary>
    private IntentClassificationResult ParseClassificationResponse(
        string response,
        string originalMessage)
    {
        // Clean up response (remove markdown code blocks if present)
        var cleanResponse = CleanJsonResponse(response);

        // Parse the JSON response
        var parsed = JsonSerializer.Deserialize<IntentClassificationResponse>(
            cleanResponse, JsonOptions);

        if (parsed == null)
        {
            _logger.LogWarning("Parsed response was null, using fallback");
            return CreateFallbackResult(originalMessage);
        }

        // Map string intent to enum
        var intentCategory = ParseIntentCategory(parsed.Intent);

        // Check if clarification needed based on confidence threshold
        var needsClarification = parsed.NeedsClarification ||
            parsed.Confidence < PlaybookBuilderSystemPrompt.Thresholds.IntentConfidence;

        // Map entities to dictionary for backward compatibility
        var entities = MapEntitiesToDictionary(parsed.Entities);

        return new IntentClassificationResult
        {
            Intent = intentCategory,
            Confidence = parsed.Confidence,
            Entities = parsed.Entities,
            EntityDictionary = entities,
            NeedsClarification = needsClarification,
            ClarificationQuestion = needsClarification
                ? parsed.ClarificationQuestion ?? GenerateDefaultClarificationQuestion(intentCategory)
                : null,
            ClarificationOptions = parsed.ClarificationOptions,
            Reasoning = parsed.Reasoning
        };
    }

    /// <summary>
    /// Clean JSON response by removing markdown code blocks.
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var cleaned = response.Trim();

        // Remove markdown code block if present
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[7..];
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned[3..];
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3];
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Parse intent string to enum.
    /// </summary>
    private static BuilderIntentCategory ParseIntentCategory(string intent)
    {
        return intent.ToUpperInvariant() switch
        {
            "CREATE_PLAYBOOK" => BuilderIntentCategory.CreatePlaybook,
            "ADD_NODE" => BuilderIntentCategory.AddNode,
            "REMOVE_NODE" => BuilderIntentCategory.RemoveNode,
            "CONNECT_NODES" => BuilderIntentCategory.ConnectNodes,
            "CONFIGURE_NODE" => BuilderIntentCategory.ConfigureNode,
            "LINK_SCOPE" => BuilderIntentCategory.LinkScope,
            "CREATE_SCOPE" => BuilderIntentCategory.CreateScope,
            "QUERY_STATUS" => BuilderIntentCategory.QueryStatus,
            "MODIFY_LAYOUT" => BuilderIntentCategory.ModifyLayout,
            "UNDO" => BuilderIntentCategory.Undo,
            "UNCLEAR" => BuilderIntentCategory.Unclear,
            _ => BuilderIntentCategory.Unclear
        };
    }

    /// <summary>
    /// Map structured entities to dictionary for backward compatibility.
    /// </summary>
    private static Dictionary<string, string>? MapEntitiesToDictionary(IntentEntities? entities)
    {
        if (entities == null) return null;

        var dict = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(entities.NodeType))
            dict["nodeType"] = entities.NodeType;
        if (!string.IsNullOrEmpty(entities.NodeId))
            dict["nodeId"] = entities.NodeId;
        if (!string.IsNullOrEmpty(entities.NodeLabel))
            dict["nodeLabel"] = entities.NodeLabel;
        if (!string.IsNullOrEmpty(entities.ScopeType))
            dict["scopeType"] = entities.ScopeType;
        if (!string.IsNullOrEmpty(entities.ScopeId))
            dict["scopeId"] = entities.ScopeId;
        if (!string.IsNullOrEmpty(entities.ScopeName))
            dict["scopeName"] = entities.ScopeName;
        if (!string.IsNullOrEmpty(entities.SourceNode))
            dict["sourceNode"] = entities.SourceNode;
        if (!string.IsNullOrEmpty(entities.TargetNode))
            dict["targetNode"] = entities.TargetNode;
        if (!string.IsNullOrEmpty(entities.ConfigKey))
            dict["configKey"] = entities.ConfigKey;
        if (!string.IsNullOrEmpty(entities.ConfigValue))
            dict["configValue"] = entities.ConfigValue;
        if (!string.IsNullOrEmpty(entities.OutputVariable))
            dict["outputVariable"] = entities.OutputVariable;

        return dict.Count > 0 ? dict : null;
    }

    /// <summary>
    /// Generate a default clarification question based on intent.
    /// </summary>
    private static string GenerateDefaultClarificationQuestion(BuilderIntentCategory intent)
    {
        return intent switch
        {
            BuilderIntentCategory.AddNode =>
                "What type of node would you like to add? (e.g., AI Analysis, Condition, Deliver Output)",
            BuilderIntentCategory.RemoveNode =>
                "Which node would you like to remove?",
            BuilderIntentCategory.ConnectNodes =>
                "Which nodes would you like to connect? Please specify the source and target.",
            BuilderIntentCategory.ConfigureNode =>
                "Which node would you like to configure, and what settings should I change?",
            BuilderIntentCategory.LinkScope =>
                "Which scope would you like to link, and to which node?",
            BuilderIntentCategory.CreateScope =>
                "What type of scope would you like to create? (Action, Skill, Knowledge, or Tool)",
            _ =>
                "I'm not sure I understand. Could you please rephrase or provide more details?"
        };
    }

    /// <summary>
    /// Create a fallback result when parsing fails.
    /// </summary>
    private IntentClassificationResult CreateFallbackResult(string message)
    {
        // Simple rule-based fallback for common cases
        var lower = message.ToLowerInvariant();

        var (intent, confidence) = lower switch
        {
            _ when lower.Contains("create") && lower.Contains("playbook") =>
                (BuilderIntentCategory.CreatePlaybook, 0.70),
            _ when lower.Contains("add") && (lower.Contains("node") || lower.Contains("step")) =>
                (BuilderIntentCategory.AddNode, 0.70),
            _ when lower.Contains("remove") || lower.Contains("delete") =>
                (BuilderIntentCategory.RemoveNode, 0.65),
            _ when lower.Contains("connect") || lower.Contains("link") =>
                (BuilderIntentCategory.ConnectNodes, 0.65),
            _ when lower.Contains("configure") || lower.Contains("update") || lower.Contains("change") =>
                (BuilderIntentCategory.ConfigureNode, 0.60),
            _ when lower.Contains("undo") || lower.Contains("revert") || lower.Contains("go back") =>
                (BuilderIntentCategory.Undo, 0.80),
            _ when lower.Contains("arrange") || lower.Contains("layout") || lower.Contains("organize") =>
                (BuilderIntentCategory.ModifyLayout, 0.70),
            _ when lower.Contains("what") || lower.Contains("how") || lower.Contains("explain") || lower.Contains("?") =>
                (BuilderIntentCategory.QueryStatus, 0.60),
            _ => (BuilderIntentCategory.Unclear, 0.50)
        };

        return new IntentClassificationResult
        {
            Intent = intent,
            Confidence = confidence,
            NeedsClarification = confidence < PlaybookBuilderSystemPrompt.Thresholds.IntentConfidence,
            ClarificationQuestion = confidence < PlaybookBuilderSystemPrompt.Thresholds.IntentConfidence
                ? GenerateDefaultClarificationQuestion(intent)
                : null,
            Reasoning = "Fallback classification used due to parsing error"
        };
    }
}

/// <summary>
/// Interface for intent classification service.
/// </summary>
public interface IIntentClassificationService
{
    /// <summary>
    /// Classify the intent of a user message.
    /// </summary>
    /// <param name="message">The user's message to classify.</param>
    /// <param name="canvasContext">Current canvas context for disambiguation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Classification result with intent, confidence, and entities.</returns>
    Task<IntentClassificationResult> ClassifyAsync(
        string message,
        ClassificationCanvasContext? canvasContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of intent classification.
/// </summary>
public record IntentClassificationResult
{
    /// <summary>The classified intent category.</summary>
    public required BuilderIntentCategory Intent { get; init; }

    /// <summary>Confidence score from 0.0 to 1.0.</summary>
    public double Confidence { get; init; }

    /// <summary>Extracted entities (structured).</summary>
    public IntentEntities? Entities { get; init; }

    /// <summary>Extracted entities as dictionary (for backward compatibility).</summary>
    public Dictionary<string, string>? EntityDictionary { get; init; }

    /// <summary>Whether clarification is needed (confidence below threshold).</summary>
    public bool NeedsClarification { get; init; }

    /// <summary>Clarification question if needed.</summary>
    public string? ClarificationQuestion { get; init; }

    /// <summary>Clarification options if multiple matches.</summary>
    public ClarificationOption[]? ClarificationOptions { get; init; }

    /// <summary>AI reasoning for the classification.</summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Get the human-readable description of the intent.
    /// </summary>
    public string IntentDescription => Intent switch
    {
        BuilderIntentCategory.CreatePlaybook => "create a new playbook",
        BuilderIntentCategory.AddNode => "add a node",
        BuilderIntentCategory.RemoveNode => "remove a node",
        BuilderIntentCategory.ConnectNodes => "connect nodes",
        BuilderIntentCategory.ConfigureNode => "configure a node",
        BuilderIntentCategory.LinkScope => "link a scope",
        BuilderIntentCategory.CreateScope => "create a new scope",
        BuilderIntentCategory.QueryStatus => "get information",
        BuilderIntentCategory.ModifyLayout => "arrange the layout",
        BuilderIntentCategory.Undo => "undo the last action",
        BuilderIntentCategory.Unclear => "perform an action",
        _ => "perform an action"
    };
}
