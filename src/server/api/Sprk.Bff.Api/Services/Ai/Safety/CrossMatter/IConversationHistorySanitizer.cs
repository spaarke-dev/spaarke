using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// Sanitizes conversation history on a matter pivot by replacing retrieved document passages
/// with a privilege-protection placeholder.
///
/// FR-408: when a matter context change is detected, any <c>tool_result</c> messages that
/// carried retrieved document passages from the previous matter must be stripped before the
/// history is forwarded to the LLM.  AI-generated assistant conclusions and all user messages
/// are always retained.
/// </summary>
public interface IConversationHistorySanitizer
{
    /// <summary>
    /// Walks the conversation history and replaces the content of retrieval tool_result messages
    /// up to and including <paramref name="fromTurnIndex"/> with a privacy placeholder.
    /// </summary>
    /// <param name="history">The full ordered conversation history (oldest-first).</param>
    /// <param name="fromTurnIndex">
    /// The zero-based turn index at which the matter change was detected.
    /// Messages at indices 0 through <paramref name="fromTurnIndex"/> (inclusive) are candidates
    /// for stripping.  Messages beyond this index are passed through unchanged.
    /// </param>
    /// <returns>
    /// A <see cref="SanitizedHistory"/> containing the sanitized message list, modification
    /// flag, count of stripped retrieval messages, and the notification message to surface to
    /// the user.
    /// </returns>
    SanitizedHistory StripRetrievedContent(IReadOnlyList<ChatMessage> history, int fromTurnIndex);
}
