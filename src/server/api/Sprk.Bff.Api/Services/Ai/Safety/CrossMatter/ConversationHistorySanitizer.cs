using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// Strips retrieved document content from conversation history on a matter pivot.
///
/// FR-408: when the user switches from Matter A to Matter B, any tool_result messages that
/// contain retrieved document passages from Matter A are replaced with a privacy placeholder.
/// AI-generated assistant conclusions and all user messages are always retained.
///
/// Retrieval messages are identified by the <see cref="RetrievalContentMarker"/> prefix
/// embedded in the content by the BFF's DocumentSearchTools and KnowledgeRetrievalTools
/// when they store search results in the conversation history.
///
/// ADR-015: document content MUST NOT appear in Tier 1 app logs.  This service only logs
/// identifiers (message ID, sequence number) and counts — never the content being stripped.
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// </summary>
public sealed class ConversationHistorySanitizer : IConversationHistorySanitizer
{
    /// <summary>
    /// Prefix embedded by <c>DocumentSearchTools</c> and <c>KnowledgeRetrievalTools</c> in
    /// System-role messages that carry retrieved document passages.
    ///
    /// Format: <c>__retrieval_result__\n{passage text}</c>
    ///
    /// The BFF embeds this marker when it stores tool results in the conversation history so
    /// that the sanitizer can reliably distinguish retrieved content from AI conclusions.
    /// </summary>
    internal const string RetrievalContentMarker = "__retrieval_result__";

    /// <summary>
    /// Replacement text written into stripped retrieval messages.
    /// Visible to the LLM in subsequent turns to explain why the content is absent.
    /// </summary>
    internal const string PrivacyPlaceholder =
        "[Document content from previous matter removed for privilege protection]";

    /// <summary>
    /// Human-readable notification surfaced to the user via SSE after sanitization.
    /// </summary>
    internal const string UserNotificationMessage =
        "Matter context changed. Prior document details cleared from context for privilege protection.";

    private readonly ILogger<ConversationHistorySanitizer> _logger;

    public ConversationHistorySanitizer(ILogger<ConversationHistorySanitizer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public SanitizedHistory StripRetrievedContent(
        IReadOnlyList<ChatMessage> history,
        int fromTurnIndex)
    {
        var sanitized = new List<ChatMessage>(history.Count);
        var strippedCount = 0;

        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];

            // Messages beyond the pivot turn index are passed through unchanged —
            // they belong to the new matter context that has not yet generated content.
            if (i > fromTurnIndex)
            {
                sanitized.Add(message);
                continue;
            }

            // Within the pivot window: check whether this is a retrieval result message.
            if (IsRetrievalMessage(message))
            {
                // ADR-015: log only the message identifier and sequence number, never the content.
                _logger.LogDebug(
                    "ConversationHistorySanitizer: stripping retrieval message seq={SeqNum}, msgId={MessageId}",
                    message.SequenceNumber, message.MessageId);

                sanitized.Add(message with { Content = PrivacyPlaceholder });
                strippedCount++;
            }
            else
            {
                // User messages and AI assistant conclusions are always retained.
                sanitized.Add(message);
            }
        }

        var wasModified = strippedCount > 0;

        _logger.LogInformation(
            "ConversationHistorySanitizer: sanitization complete — stripped={StrippedCount}, total={Total}, modified={Modified}",
            strippedCount, history.Count, wasModified);

        return new SanitizedHistory(
            Messages: sanitized.AsReadOnly(),
            WasModified: wasModified,
            RemovedDocumentCount: strippedCount,
            NotificationMessage: UserNotificationMessage);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="message"/> is a System-role retrieval result
    /// message identified by the <see cref="RetrievalContentMarker"/> prefix.
    /// </summary>
    private static bool IsRetrievalMessage(ChatMessage message)
        => message.Role == ChatMessageRole.System
           && message.Content.StartsWith(RetrievalContentMarker, StringComparison.Ordinal);
}
