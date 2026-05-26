using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Summarizes AI chat sessions using GPT-4o when they exceed the 25-message or 8,000-token
/// threshold (AIPU2-032).
///
/// Design decisions:
///   - GPT-4o (not GPT-4o-mini): legal context requires the full model to preserve exact
///     qualifications such as "except in cases of gross negligence" (ADR-013, AIPU2-032).
///   - Uses <see cref="IChatClient"/> (the pipeline client with function invocation) rather
///     than the "raw" keyed client, consistent with all other direct IChatClient callers.
///   - Structured output: the model is prompted to return a JSON block embedded in its
///     response containing the key conclusions array. The narrative summary is extracted
///     from the text portion preceding the JSON block.
///   - Token estimation: total content character count / 4. This approximation is fast
///     and sufficient for threshold detection. Exact token counts are not required.
///   - Failure policy: SummarizeAsync throws on model failure. Callers (SessionPersistenceService
///     extension and ChatEndpoints) must fire-and-forget or catch to avoid blocking SSE streaming.
///
/// Lifetime: Scoped — one instance per HTTP request (IChatClient is singleton/thread-safe).
/// </summary>
public class SessionSummarizationService : ISessionSummarizationService
{
    /// <summary>
    /// Azure OpenAI deployment name used for summarization.
    /// GPT-4o is required — not GPT-4o-mini (AIPU2-032 acceptance criterion).
    /// </summary>
    internal const string SummarizationModel = "gpt-4o";

    /// <summary>Number of messages to retain in-memory after summarization.</summary>
    internal const int TailMessageCount = 10;

    private static readonly string SystemPrompt =
        """
        Summarize this legal AI conversation. Preserve:
        - All legal conclusions and qualifications (e.g., "except in cases of gross negligence")
        - Key document references and citations
        - Decisions made and actions taken
        - Entity context (matter name, parties, key dates)

        Also extract key legal conclusions as a separate structured list.

        Respond with exactly two sections:

        NARRATIVE_SUMMARY:
        <one or two paragraph narrative summary here>

        KEY_CONCLUSIONS_JSON:
        <a JSON array of objects, each with fields: topic (string), conclusion (string), confidence ("high"|"medium"|"low"), sourceReference (string or null)>
        """;

    private readonly IChatClient _chatClient;
    private readonly ILogger<SessionSummarizationService> _logger;

    public SessionSummarizationService(
        IChatClient chatClient,
        ILogger<SessionSummarizationService> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool ShouldSummarize(IReadOnlyList<SessionMessage> messages)
    {
        if (messages.Count >= ISessionSummarizationService.MessageThreshold)
        {
            return true;
        }

        var estimatedTokens = EstimateTokens(messages);
        return estimatedTokens >= ISessionSummarizationService.TokenThreshold;
    }

    /// <inheritdoc/>
    public async Task<SessionSummary> SummarizeAsync(
        IReadOnlyList<SessionMessage> messages,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
        {
            throw new ArgumentException("Cannot summarize an empty message list.", nameof(messages));
        }

        _logger.LogInformation(
            "SessionSummarizationService: Starting summarization of {MessageCount} messages using {Model}",
            messages.Count, SummarizationModel);

        var conversationText = BuildConversationText(messages);

        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, $"Conversation to summarize:\n\n{conversationText}")
        };

        var options = new ChatOptions
        {
            ModelId = SummarizationModel,
            MaxOutputTokens = 2000
        };

        var response = await _chatClient.GetResponseAsync(chatMessages, options, ct);
        var responseText = response.Text ?? string.Empty;

        var (narrative, conclusions) = ParseResponse(responseText);

        var summary = new SessionSummary(
            NarrativeSummary: narrative,
            KeyConclusions: conclusions,
            OriginalMessageCount: messages.Count,
            SummarizedAt: DateTimeOffset.UtcNow,
            ModelUsed: SummarizationModel);

        _logger.LogInformation(
            "SessionSummarizationService: Summarization complete — {ConclusionCount} key conclusions extracted, model={Model}",
            conclusions.Count, SummarizationModel);

        return summary;
    }

    // =========================================================================
    // Static helpers — token estimation
    // =========================================================================

    /// <summary>
    /// Estimates the token count of the messages using the character/4 approximation.
    /// Fast and sufficient for threshold detection — exact token counts are not needed.
    /// </summary>
    internal static int EstimateTokens(IReadOnlyList<SessionMessage> messages)
    {
        var totalChars = 0;
        foreach (var message in messages)
        {
            totalChars += message.Content?.Length ?? 0;
        }

        return totalChars / 4;
    }

    // =========================================================================
    // Private helpers — prompt construction and response parsing
    // =========================================================================

    /// <summary>
    /// Formats the messages into a readable conversation transcript for the summarization prompt.
    /// Role labels are normalized to user/assistant/system for clarity.
    /// </summary>
    private static string BuildConversationText(IReadOnlyList<SessionMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            var roleLabel = msg.Role?.ToLowerInvariant() switch
            {
                "user" => "User",
                "assistant" => "Assistant",
                "system" => "System",
                _ => msg.Role ?? "Unknown"
            };

            // Skip system messages from the conversation body — they carry the playbook prompt,
            // not user/assistant dialogue, and would inflate the token count unnecessarily.
            if (string.Equals(msg.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                sb.AppendLine($"[{roleLabel}]: {msg.Content}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses the model response into a narrative summary and structured key conclusions list.
    ///
    /// Expected format:
    ///   NARRATIVE_SUMMARY:
    ///   &lt;narrative text&gt;
    ///
    ///   KEY_CONCLUSIONS_JSON:
    ///   &lt;JSON array&gt;
    ///
    /// If parsing fails (malformed model output), logs a warning and returns the full response
    /// as the narrative with an empty conclusions list — partial data is better than failure.
    /// </summary>
    private (string Narrative, List<KeyConclusion> Conclusions) ParseResponse(string responseText)
    {
        const string narrativeMarker = "NARRATIVE_SUMMARY:";
        const string conclusionsMarker = "KEY_CONCLUSIONS_JSON:";

        var narrativeStart = responseText.IndexOf(narrativeMarker, StringComparison.OrdinalIgnoreCase);
        var conclusionsStart = responseText.IndexOf(conclusionsMarker, StringComparison.OrdinalIgnoreCase);

        if (narrativeStart < 0 || conclusionsStart < 0)
        {
            _logger.LogWarning(
                "SessionSummarizationService: Model response did not contain expected section markers. " +
                "Returning full response as narrative with empty conclusions.");
            return (responseText.Trim(), []);
        }

        // Extract narrative text between the two markers
        var narrativeContentStart = narrativeStart + narrativeMarker.Length;
        var narrativeText = responseText[narrativeContentStart..conclusionsStart].Trim();

        // Extract JSON text after the conclusions marker
        var jsonContentStart = conclusionsStart + conclusionsMarker.Length;
        var jsonText = responseText[jsonContentStart..].Trim();

        var conclusions = ParseKeyConclusions(jsonText);

        return (narrativeText, conclusions);
    }

    /// <summary>
    /// Deserializes the JSON array of key conclusions from the model response.
    /// Returns an empty list on deserialization failure to ensure partial data is preserved.
    /// </summary>
    private List<KeyConclusion> ParseKeyConclusions(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return [];
        }

        // Strip markdown code fence if the model wrapped the JSON (e.g., ```json ... ```)
        if (jsonText.StartsWith("```", StringComparison.Ordinal))
        {
            var fenceEnd = jsonText.IndexOf('\n');
            if (fenceEnd > 0)
            {
                jsonText = jsonText[(fenceEnd + 1)..];
            }

            var closingFence = jsonText.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence > 0)
            {
                jsonText = jsonText[..closingFence].Trim();
            }
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var items = JsonSerializer.Deserialize<List<KeyConclusionDto>>(jsonText, options);
            if (items is null)
            {
                return [];
            }

            return items
                .Select(dto => new KeyConclusion(
                    Topic: dto.Topic ?? string.Empty,
                    Conclusion: dto.Conclusion ?? string.Empty,
                    Confidence: NormalizeConfidence(dto.Confidence),
                    SourceReference: string.IsNullOrWhiteSpace(dto.SourceReference)
                        ? null
                        : dto.SourceReference))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "SessionSummarizationService: Failed to parse KEY_CONCLUSIONS_JSON — returning empty conclusions list.");
            return [];
        }
    }

    private static string NormalizeConfidence(string? raw)
        => raw?.ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "medium"    // safe default when model returns an unexpected value
        };

    // =========================================================================
    // Private DTO — deserialization target for the model's JSON array items
    // =========================================================================

    /// <summary>
    /// Intermediate DTO used during JSON deserialization of the model's conclusions array.
    /// All fields are nullable to handle missing or null values from the model gracefully.
    /// </summary>
    private sealed class KeyConclusionDto
    {
        public string? Topic { get; set; }
        public string? Conclusion { get; set; }
        public string? Confidence { get; set; }
        public string? SourceReference { get; set; }
    }
}
