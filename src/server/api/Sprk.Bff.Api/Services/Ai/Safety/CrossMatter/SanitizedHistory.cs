using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// The result of a <see cref="IConversationHistorySanitizer.StripRetrievedContent"/> call.
///
/// Carries the sanitized message list plus audit fields that the caller uses to decide
/// whether to emit a <c>matter_context_change</c> SSE event and to populate OTEL counters.
/// </summary>
/// <param name="Messages">
/// The sanitized conversation history.  Retrieval tool_result messages that contained
/// previous-matter document passages have had their content replaced with a placeholder.
/// User messages and AI-generated assistant conclusions are unchanged.
/// </param>
/// <param name="WasModified">
/// <c>true</c> when at least one retrieval message was stripped; <c>false</c> when the history
/// contained no retrieval messages (no actual modification was performed).
/// </param>
/// <param name="RemovedDocumentCount">
/// Number of retrieval tool_result messages whose content was replaced.
/// Used for the <c>ai_safety_cross_matter_content_stripped_total</c> OTEL counter.
/// </param>
/// <param name="NotificationMessage">
/// Human-readable message to surface to the user explaining that the matter context changed
/// and that prior document references are no longer available.
/// Populated regardless of whether any messages were actually stripped.
/// </param>
public record SanitizedHistory(
    IReadOnlyList<ChatMessage> Messages,
    bool WasModified,
    int RemovedDocumentCount,
    string NotificationMessage);
