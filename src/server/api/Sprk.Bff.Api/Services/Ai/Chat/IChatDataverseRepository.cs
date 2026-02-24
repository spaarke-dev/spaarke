using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Dataverse persistence seam for chat sessions and message history.
///
/// Justification for interface (ADR-010 seam requirement):
/// The production implementation calls Dataverse via <c>IDataverseService</c> / Web API.
/// Unit tests inject an in-memory stub to avoid real network calls.
/// This is a genuine testability seam — not a speculative abstraction.
///
/// Entities:
///   - <c>sprk_aichatsummary</c> — session metadata (one per session).
///   - <c>sprk_aichatmessage</c> — individual messages (many per session).
/// </summary>
public interface IChatDataverseRepository
{
    /// <summary>
    /// Creates a new session record in Dataverse (<c>sprk_aichatsummary</c>).
    /// </summary>
    /// <param name="session">Session data to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateSessionAsync(ChatSession session, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a session from Dataverse by tenant + session ID, including all
    /// associated messages (<c>sprk_aichatmessage</c>) ordered by sequence number.
    ///
    /// Returns <c>null</c> if no matching session exists.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation.</param>
    /// <param name="sessionId">Session ID to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChatSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Marks a session as archived in Dataverse (<c>sprk_isarchived = true</c>).
    /// The session record is retained for audit purposes.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation.</param>
    /// <param name="sessionId">Session ID to archive.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ArchiveSessionAsync(string tenantId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists a new message to Dataverse (<c>sprk_aichatmessage</c>).
    /// </summary>
    /// <param name="message">Message to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>
    /// Retrieves ordered message history for a session from Dataverse.
    /// Results are ordered by <c>sprk_sequencenumber asc</c>.
    /// </summary>
    /// <param name="sessionId">Session ID to query.</param>
    /// <param name="maxMessages">Maximum number of messages to return (most recent N).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int maxMessages, CancellationToken ct = default);

    /// <summary>
    /// Gets the total message count for a session from <c>sprk_aichatsummary.sprk_messagecount</c>.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> GetMessageCountAsync(string tenantId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Updates the message count and last-activity timestamp on the session record.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="messageCount">Updated total message count.</param>
    /// <param name="lastActivity">Updated last-activity timestamp.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateSessionActivityAsync(
        string tenantId,
        string sessionId,
        int messageCount,
        DateTimeOffset lastActivity,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a conversation summary to <c>sprk_aichatsummary.sprk_summary</c>.
    /// Called by <see cref="ChatHistoryManager"/> after summarisation.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="summary">Summary text to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateSessionSummaryAsync(
        string tenantId,
        string sessionId,
        string summary,
        CancellationToken ct = default);
}
