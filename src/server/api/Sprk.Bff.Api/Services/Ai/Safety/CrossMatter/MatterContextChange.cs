namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// Describes a detected matter context change within a conversation turn.
///
/// Created by <see cref="MatterContextDetector"/> when the incoming matter ID differs from
/// the matter recorded on the most recent conversation turn.
///
/// Used by <see cref="ConversationHistorySanitizer"/> to determine which turns to strip
/// retrieved document content from, and by <see cref="ChatEndpoints"/> to emit the
/// <c>matter_context_change</c> SSE event.
/// </summary>
/// <param name="PreviousMatterId">
/// The matter ID from the last recorded conversation turn (the "old" context).
/// </param>
/// <param name="NewMatterId">
/// The incoming matter ID that triggered the context change (the "new" context).
/// </param>
/// <param name="ChangeDetectedAtTurnIndex">
/// Zero-based index into the conversation history at which the change was detected.
/// All tool_result retrieval messages at or before this index will be stripped.
/// </param>
public record MatterContextChange(
    string PreviousMatterId,
    string NewMatterId,
    int ChangeDetectedAtTurnIndex);
