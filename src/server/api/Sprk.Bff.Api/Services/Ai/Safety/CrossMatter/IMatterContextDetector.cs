using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// Detects when a user pivots from one matter to another within the same conversation session.
///
/// FR-408: when the incoming matter ID differs from the matter recorded on the most recent
/// conversation turn, a <see cref="MatterContextChange"/> is returned so that the caller can
/// strip prior retrieved document content before sending history to the LLM.
/// </summary>
public interface IMatterContextDetector
{
    /// <summary>
    /// Compares <paramref name="incomingMatterId"/> against the matter recorded on the most
    /// recent turn in <paramref name="history"/>.
    /// </summary>
    /// <param name="history">The current conversation history (ordered oldest-first).</param>
    /// <param name="incomingMatterId">
    /// The matter ID associated with the incoming turn.
    /// Derived from <c>ChatHostContext.EntityId</c> when <c>EntityType == "matter"</c>.
    /// An empty or null value is treated as "no matter context" and will not trigger a change.
    /// </param>
    /// <returns>
    /// A <see cref="MatterContextChange"/> when a pivot is detected; <c>null</c> when the
    /// matter is unchanged, the history carries no matter metadata, or either ID is empty.
    /// </returns>
    MatterContextChange? DetectChange(IReadOnlyList<ChatMessage> history, string incomingMatterId);
}
