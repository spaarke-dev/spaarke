using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for generating user clarification prompts when intent or entity resolution
/// has low confidence. Implements the clarification loop pattern from the conversational UX spec.
/// </summary>
/// <remarks>
/// Clarification thresholds:
/// - Intent confidence &lt; 75%: Ask for intent clarification
/// - Entity confidence &lt; 80%: Show matching options
/// - Scope confidence &lt; 70%: Ask user to select or create
/// </remarks>
public interface IClarificationService
{
    /// <summary>
    /// Generate a clarification request when intent classification has low confidence.
    /// </summary>
    /// <param name="classification">The low-confidence intent classification result.</param>
    /// <returns>Clarification request with options for the user.</returns>
    ClarificationRequest GenerateIntentClarification(IntentClassificationResult classification);

    /// <summary>
    /// Generate a clarification request when entity resolution is ambiguous.
    /// </summary>
    /// <param name="resolution">The entity resolution result with multiple candidates.</param>
    /// <returns>Clarification request with entity options.</returns>
    ClarificationRequest GenerateEntityClarification(EntityResolutionResult resolution);

    /// <summary>
    /// Generate a clarification request when scope resolution needs user input.
    /// </summary>
    /// <param name="resolution">The scope resolution result.</param>
    /// <param name="originalReference">The user's original reference text.</param>
    /// <returns>Clarification request for scope selection or creation.</returns>
    ClarificationRequest GenerateScopeClarification(
        EntityResolutionResult resolution,
        string originalReference);

    /// <summary>
    /// Determine if clarification is needed based on confidence scores.
    /// </summary>
    /// <param name="intentConfidence">Intent classification confidence (0-1).</param>
    /// <param name="entityConfidence">Entity resolution confidence (0-1), or null if no entities.</param>
    /// <returns>True if clarification is needed.</returns>
    bool NeedsClarification(double intentConfidence, double? entityConfidence);

    /// <summary>
    /// Format a clarification request for SSE streaming.
    /// </summary>
    /// <param name="clarification">The clarification request.</param>
    /// <returns>Stream chunk ready for SSE response.</returns>
    BuilderStreamChunk FormatForStreaming(ClarificationRequest clarification);

    /// <summary>
    /// Get likely alternative intents for a low-confidence classification.
    /// </summary>
    /// <param name="primaryIntent">The primary classified intent.</param>
    /// <param name="message">The original user message.</param>
    /// <returns>Array of likely alternative intents with confidence scores.</returns>
    IntentAlternative[] GetLikelyAlternatives(BuilderIntentCategory primaryIntent, string message);
}

/// <summary>
/// Implementation of clarification service.
/// </summary>
public class ClarificationService : IClarificationService
{
    private readonly ILogger<ClarificationService> _logger;

    // Confidence thresholds per spec
    private const double IntentConfidenceThreshold = 0.75;
    private const double EntityConfidenceThreshold = 0.80;
    private const double ScopeConfidenceThreshold = 0.70;

    public ClarificationService(ILogger<ClarificationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ClarificationRequest GenerateIntentClarification(IntentClassificationResult classification)
    {
        ArgumentNullException.ThrowIfNull(classification);

        _logger.LogDebug(
            "Generating intent clarification for confidence {Confidence}",
            classification.Confidence);

        // Get top candidate intents based on primary intent and reasoning
        var topIntents = GetLikelyAlternatives(classification.Intent, classification.Reasoning ?? "");

        // Generate user-friendly question
        var question = topIntents.Length switch
        {
            0 => "I'm not sure what you'd like to do. Could you please rephrase?",
            1 => $"Did you mean to {GetIntentDescription(topIntents[0].Intent)}?",
            2 => $"Did you mean to {GetIntentDescription(topIntents[0].Intent)} or {GetIntentDescription(topIntents[1].Intent)}?",
            _ => "Which of these actions would you like to perform?"
        };

        var options = topIntents
            .Select(t => new ClarifyOption
            {
                Id = t.Intent.ToString(),
                Label = GetIntentLabel(t.Intent),
                Description = GetIntentDescription(t.Intent),
                Confidence = t.Confidence
            })
            .ToArray();

        return new ClarificationRequest
        {
            Type = ClarificationType.Intent,
            Question = question,
            Options = options,
            AllowFreeText = true,
            FreeTextPrompt = "Or describe what you'd like to do in different words"
        };
    }

    /// <inheritdoc />
    public ClarificationRequest GenerateEntityClarification(EntityResolutionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        _logger.LogDebug(
            "Generating entity clarification for {EntityType} with {CandidateCount} candidates",
            resolution.EntityType,
            resolution.CandidateMatches?.Length ?? 0);

        var entityTypeName = resolution.EntityType == EntityType.Node ? "node" : "scope";
        var question = resolution.CandidateMatches?.Length > 0
            ? $"Which {entityTypeName} did you mean?"
            : $"I couldn't find a matching {entityTypeName}. Could you clarify?";

        var options = (resolution.CandidateMatches ?? [])
            .Select(c => new ClarifyOption
            {
                Id = c.Id,
                Label = c.Label,
                Description = c.MatchReason ?? $"{c.Type} - {c.Label}",
                Confidence = c.Confidence
            })
            .ToArray();

        return new ClarificationRequest
        {
            Type = ClarificationType.Entity,
            Question = question,
            Options = options,
            AllowFreeText = true,
            FreeTextPrompt = "Or specify the exact name or ID"
        };
    }

    /// <inheritdoc />
    public ClarificationRequest GenerateScopeClarification(
        EntityResolutionResult resolution,
        string originalReference)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalReference);

        _logger.LogDebug(
            "Generating scope clarification for reference '{Reference}' with confidence {Confidence}",
            originalReference,
            resolution.Confidence);

        var categoryName = resolution.ScopeCategory switch
        {
            ScopeCategory.Action => "action",
            ScopeCategory.Skill => "skill",
            ScopeCategory.Knowledge => "knowledge source",
            ScopeCategory.Tool => "tool",
            _ => "scope"
        };

        // Generate options from candidates plus a "create new" option
        var options = new List<ClarifyOption>();

        // Add existing matches
        if (resolution.CandidateMatches != null)
        {
            options.AddRange(resolution.CandidateMatches.Select(c => new ClarifyOption
            {
                Id = c.Id,
                Label = c.Label,
                Description = c.MatchReason ?? $"Existing {categoryName}",
                Confidence = c.Confidence
            }));
        }

        // Add "create new" option if confidence is very low
        if (resolution.Confidence < ScopeConfidenceThreshold)
        {
            options.Add(new ClarifyOption
            {
                Id = ClarificationOptionIds.CreateNew,
                Label = $"Create new {categoryName}",
                Description = $"Create a new {categoryName} named \"{originalReference}\"",
                Confidence = 0
            });
        }

        var question = options.Count > 1
            ? $"Which {categoryName} would you like to use?"
            : $"I couldn't find a {categoryName} matching \"{originalReference}\". Would you like to create one?";

        return new ClarificationRequest
        {
            Type = ClarificationType.Scope,
            Question = question,
            Options = options.ToArray(),
            AllowFreeText = true,
            FreeTextPrompt = "Or search for a different name",
            ScopeCategory = resolution.ScopeCategory
        };
    }

    /// <inheritdoc />
    public bool NeedsClarification(double intentConfidence, double? entityConfidence)
    {
        if (intentConfidence < IntentConfidenceThreshold)
        {
            _logger.LogDebug(
                "Clarification needed: intent confidence {Confidence} < threshold {Threshold}",
                intentConfidence,
                IntentConfidenceThreshold);
            return true;
        }

        if (entityConfidence.HasValue && entityConfidence.Value < EntityConfidenceThreshold)
        {
            _logger.LogDebug(
                "Clarification needed: entity confidence {Confidence} < threshold {Threshold}",
                entityConfidence.Value,
                EntityConfidenceThreshold);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public BuilderStreamChunk FormatForStreaming(ClarificationRequest clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);

        // Format question with options for SSE
        var questionWithOptions = clarification.Question;

        if (clarification.Options.Length > 0)
        {
            var optionsList = string.Join("\n", clarification.Options.Select((o, i) => $"  {i + 1}. {o.Label}"));
            questionWithOptions = $"{clarification.Question}\n\n{optionsList}";
        }

        return BuilderStreamChunk.Clarification(questionWithOptions);
    }

    /// <inheritdoc />
    public IntentAlternative[] GetLikelyAlternatives(BuilderIntentCategory primaryIntent, string message)
    {
        var alternatives = new List<IntentAlternative>();
        var lowerMessage = message.ToLowerInvariant();

        // Add primary intent
        alternatives.Add(new IntentAlternative
        {
            Intent = primaryIntent,
            Confidence = 0.5 // Low confidence since we need clarification
        });

        // Infer likely alternatives based on common patterns
        if (lowerMessage.Contains("add") || lowerMessage.Contains("create") || lowerMessage.Contains("new"))
        {
            if (primaryIntent != BuilderIntentCategory.AddNode)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.AddNode, Confidence = 0.4 });
            if (primaryIntent != BuilderIntentCategory.CreatePlaybook)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.CreatePlaybook, Confidence = 0.35 });
            if (primaryIntent != BuilderIntentCategory.CreateScope)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.CreateScope, Confidence = 0.3 });
        }

        if (lowerMessage.Contains("connect") || lowerMessage.Contains("link") || lowerMessage.Contains("wire"))
        {
            if (primaryIntent != BuilderIntentCategory.ConnectNodes)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.ConnectNodes, Confidence = 0.4 });
            if (primaryIntent != BuilderIntentCategory.LinkScope)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.LinkScope, Confidence = 0.35 });
        }

        if (lowerMessage.Contains("remove") || lowerMessage.Contains("delete"))
        {
            if (primaryIntent != BuilderIntentCategory.RemoveNode)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.RemoveNode, Confidence = 0.4 });
        }

        if (lowerMessage.Contains("config") || lowerMessage.Contains("set") || lowerMessage.Contains("change"))
        {
            if (primaryIntent != BuilderIntentCategory.ConfigureNode)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.ConfigureNode, Confidence = 0.4 });
        }

        if (lowerMessage.Contains("?") || lowerMessage.Contains("how") || lowerMessage.Contains("what"))
        {
            if (primaryIntent != BuilderIntentCategory.QueryStatus)
                alternatives.Add(new IntentAlternative { Intent = BuilderIntentCategory.QueryStatus, Confidence = 0.35 });
        }

        return alternatives
            .DistinctBy(a => a.Intent)
            .OrderByDescending(a => a.Confidence)
            .Take(4) // Max 4 options
            .ToArray();
    }

    #region Private Helpers

    /// <summary>
    /// Get user-friendly label for an intent.
    /// </summary>
    private static string GetIntentLabel(BuilderIntentCategory intent)
    {
        return intent switch
        {
            BuilderIntentCategory.CreatePlaybook => "Create playbook",
            BuilderIntentCategory.AddNode => "Add node",
            BuilderIntentCategory.RemoveNode => "Remove node",
            BuilderIntentCategory.ConnectNodes => "Connect nodes",
            BuilderIntentCategory.ConfigureNode => "Configure node",
            BuilderIntentCategory.LinkScope => "Link scope",
            BuilderIntentCategory.CreateScope => "Create scope",
            BuilderIntentCategory.QueryStatus => "Get information",
            BuilderIntentCategory.ModifyLayout => "Arrange layout",
            BuilderIntentCategory.Undo => "Undo",
            _ => "Unknown action"
        };
    }

    /// <summary>
    /// Get user-friendly description for an intent.
    /// </summary>
    private static string GetIntentDescription(BuilderIntentCategory intent)
    {
        return intent switch
        {
            BuilderIntentCategory.CreatePlaybook => "create a new playbook from scratch",
            BuilderIntentCategory.AddNode => "add a new node to the canvas",
            BuilderIntentCategory.RemoveNode => "remove a node from the canvas",
            BuilderIntentCategory.ConnectNodes => "connect two nodes together",
            BuilderIntentCategory.ConfigureNode => "configure a node's settings",
            BuilderIntentCategory.LinkScope => "link a scope to a node",
            BuilderIntentCategory.CreateScope => "create a new custom scope",
            BuilderIntentCategory.QueryStatus => "get information about the playbook",
            BuilderIntentCategory.ModifyLayout => "rearrange the canvas layout",
            BuilderIntentCategory.Undo => "undo the last action",
            _ => "perform an action"
        };
    }

    #endregion
}

/// <summary>
/// A clarification request to present to the user.
/// </summary>
public record ClarificationRequest
{
    /// <summary>
    /// Type of clarification being requested.
    /// </summary>
    public required ClarificationType Type { get; init; }

    /// <summary>
    /// The question to ask the user.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Available options for the user to choose from.
    /// </summary>
    public ClarifyOption[] Options { get; init; } = [];

    /// <summary>
    /// Whether free-text input is allowed.
    /// </summary>
    public bool AllowFreeText { get; init; }

    /// <summary>
    /// Prompt text for free-text input field.
    /// </summary>
    public string? FreeTextPrompt { get; init; }

    /// <summary>
    /// For scope clarifications, the category being clarified.
    /// </summary>
    public ScopeCategory? ScopeCategory { get; init; }
}

/// <summary>
/// A single option in a clarification request.
/// Named ClarifyOption to avoid conflict with ClarificationOption in IntentClassificationModels.
/// </summary>
public record ClarifyOption
{
    /// <summary>
    /// Unique identifier for this option (intent name, entity ID, etc.).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display label for this option.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Description providing more context.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Confidence score for this option (0-1).
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// An alternative intent suggestion for clarification.
/// </summary>
public record IntentAlternative
{
    /// <summary>
    /// The alternative intent.
    /// </summary>
    public required BuilderIntentCategory Intent { get; init; }

    /// <summary>
    /// Confidence score for this alternative (0-1).
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Type of clarification being requested.
/// </summary>
public enum ClarificationType
{
    /// <summary>
    /// Clarifying the user's intent.
    /// </summary>
    Intent,

    /// <summary>
    /// Clarifying which entity (node/scope) was referenced.
    /// </summary>
    Entity,

    /// <summary>
    /// Clarifying scope selection or creation.
    /// </summary>
    Scope
}

/// <summary>
/// Special option IDs for clarification responses.
/// </summary>
public static class ClarificationOptionIds
{
    /// <summary>
    /// User wants to create a new scope instead of using an existing one.
    /// </summary>
    public const string CreateNew = "__create_new__";

    /// <summary>
    /// User wants to cancel the current operation.
    /// </summary>
    public const string Cancel = "__cancel__";

    /// <summary>
    /// User will provide free-text input.
    /// </summary>
    public const string FreeText = "__free_text__";
}
