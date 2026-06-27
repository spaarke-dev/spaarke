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
    /// belonging to the previous-matter window with a privacy placeholder.
    /// </summary>
    /// <param name="history">The full ordered conversation history (oldest-first).</param>
    /// <param name="fromTurnIndex">
    /// Zero-based turn index that anchors the strip window. Semantics depend on the message at
    /// this index:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Matter-pivot mode</b> — when <paramref name="fromTurnIndex"/> identifies a
    ///       System-role matter marker (the typical caller is
    ///       <see cref="MatterContextDetector.DetectChange"/>): messages at indices
    ///       <c>0</c> through <paramref name="fromTurnIndex"/>&#160;-&#160;1 pass through
    ///       unchanged; from <paramref name="fromTurnIndex"/> onward, retrieval tool_result
    ///       messages are replaced with the privacy placeholder UNTIL a different matter marker
    ///       is encountered (signalling entry into the new-matter zone). Messages after that
    ///       new marker pass through unchanged.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Legacy mode</b> — when <paramref name="fromTurnIndex"/> does not identify a
    ///       matter marker: messages at indices <c>0</c> through
    ///       <paramref name="fromTurnIndex"/> (inclusive) are candidates for stripping;
    ///       messages beyond this index pass through unchanged.
    ///     </description>
    ///   </item>
    /// </list>
    /// </param>
    /// <returns>
    /// A <see cref="SanitizedHistory"/> containing the sanitized message list, modification
    /// flag, count of stripped retrieval messages, and the notification message to surface to
    /// the user.
    /// </returns>
    SanitizedHistory StripRetrievedContent(IReadOnlyList<ChatMessage> history, int fromTurnIndex);
}
