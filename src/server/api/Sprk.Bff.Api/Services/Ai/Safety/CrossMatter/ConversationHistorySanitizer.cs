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

        // RB-T044-01 fix (2026-06-01): the previous implementation inverted the strip window,
        // causing previous-matter retrieval content to leak into new-matter LLM context.
        //
        // The corrected contract distinguishes two operating modes:
        //
        //   Matter-pivot mode — `history[fromTurnIndex]` is a matter marker (per
        //   MatterContextDetector.ExtractMatterId). Messages BEFORE fromTurnIndex pass through
        //   unchanged; from fromTurnIndex onward, retrieval messages are stripped UNTIL a
        //   DIFFERENT matter marker is encountered (signalling entry into the new-matter zone),
        //   after which messages pass through unchanged.
        //
        //   Legacy mode — `history[fromTurnIndex]` is not a matter marker (caller invoked the
        //   sanitizer directly with an arbitrary window endpoint, no matter pivot involved).
        //   Retrieval messages where i <= fromTurnIndex are stripped; messages where
        //   i > fromTurnIndex pass through unchanged. Preserves the historical
        //   Sanitizer_StripsRetrievalBlocks_PreservesConclusions contract.
        var pivotMatterId = GetPivotMatterId(history, fromTurnIndex);
        var inMatterPivotMode = pivotMatterId is not null;
        var hasExitedOldMatterZone = false;

        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];

            bool inStripWindow;
            if (inMatterPivotMode)
            {
                // Detect transition out of the old-matter zone: a System-role message carrying
                // a DIFFERENT matter id terminates stripping for the remainder of the history.
                if (!hasExitedOldMatterZone
                    && i > fromTurnIndex
                    && message.Role == ChatMessageRole.System)
                {
                    var nextMarker = MatterContextDetector.ExtractMatterId(message.Content);
                    if (nextMarker is not null
                        && !string.Equals(nextMarker, pivotMatterId, StringComparison.OrdinalIgnoreCase))
                    {
                        hasExitedOldMatterZone = true;
                    }
                }

                inStripWindow = i >= fromTurnIndex && !hasExitedOldMatterZone;
            }
            else
            {
                inStripWindow = i <= fromTurnIndex;
            }

            if (!inStripWindow)
            {
                sanitized.Add(message);
                continue;
            }

            // Within the strip window: check whether this is a retrieval result message.
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
                // User messages, AI assistant conclusions, non-retrieval System messages
                // (including matter markers themselves) are always retained.
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

    /// <summary>
    /// Returns the matter id at <paramref name="fromTurnIndex"/> if that message is a System-role
    /// matter marker (per <see cref="MatterContextDetector.ExtractMatterId"/>); otherwise
    /// <c>null</c>. Indicates whether the caller is invoking the sanitizer in matter-pivot mode
    /// (non-null) or in legacy single-window mode (null).
    /// </summary>
    private static string? GetPivotMatterId(IReadOnlyList<ChatMessage> history, int fromTurnIndex)
    {
        if (fromTurnIndex < 0 || fromTurnIndex >= history.Count)
        {
            return null;
        }

        var anchor = history[fromTurnIndex];
        return anchor.Role == ChatMessageRole.System
            ? MatterContextDetector.ExtractMatterId(anchor.Content)
            : null;
    }
}
