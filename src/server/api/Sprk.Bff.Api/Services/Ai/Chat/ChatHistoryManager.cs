using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages chat message history: addition, retrieval, summarisation, and archiving.
///
/// Design:
///   - Messages are written to Dataverse (audit trail) then the Redis cache is refreshed.
///   - After adding a message, summarisation is triggered if the session has &gt;= 15 messages.
///   - Archive is triggered when approaching the 50-message limit (NFR-12).
///   - History retrieval returns the most recent N messages from Redis (hot path).
///
/// Summarisation (NFR / spec):
///   - Trigger: <c>session.Messages.Count &gt;= <see cref="SummarisationThreshold"/></c>
///   - Action: Condenses older messages into a single summary text stored in
///     <c>sprk_aichatsummary.sprk_summary</c>.
///   - In Phase 1 (AIPL-052), summarisation generates a placeholder summary.
///     The real LLM-based summarisation is implemented in AIPL-054 (ChatEndpoints).
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// </summary>
public sealed class ChatHistoryManager
{
    /// <summary>
    /// Number of messages that triggers conversation summarisation.
    /// Matches the spec constraint: "Summarize after 15 messages".
    /// </summary>
    public const int SummarisationThreshold = 15;

    /// <summary>
    /// Maximum messages per session before the history is archived (NFR-12).
    /// </summary>
    public const int ArchiveThreshold = 50;

    /// <summary>
    /// Default maximum number of messages to return from <see cref="GetHistoryAsync"/>.
    /// </summary>
    public const int DefaultMaxMessages = 50;

    private readonly ChatSessionManager _sessionManager;
    private readonly IChatDataverseRepository _dataverseRepository;
    private readonly ILogger<ChatHistoryManager> _logger;

    public ChatHistoryManager(
        ChatSessionManager sessionManager,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatHistoryManager> logger)
    {
        _sessionManager = sessionManager;
        _dataverseRepository = dataverseRepository;
        _logger = logger;
    }

    /// <summary>
    /// Adds a message to the session history.
    ///
    /// Execution order:
    ///   1. Persist message to Dataverse (<c>sprk_aichatmessage</c>).
    ///   2. Append message to the session's in-memory list.
    ///   3. Update the Redis cache with the new message and last-activity timestamp.
    ///   4. Update session activity in Dataverse (message count).
    ///   5. Trigger summarisation if threshold reached.
    ///   6. Trigger archive if approaching max.
    /// </summary>
    /// <param name="session">The session to add the message to.  Passed by reference — the
    /// updated session (with the new message) is returned.</param>
    /// <param name="message">The message to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="ChatSession"/> with the new message appended.</returns>
    public async Task<ChatSession> AddMessageAsync(
        ChatSession session,
        ChatMessage message,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Adding {Role} message to session {SessionId} (seq={SeqNum}, tenant={TenantId})",
            message.Role, session.SessionId, message.SequenceNumber, session.TenantId);

        // 1. Persist to Dataverse (audit trail — cold storage)
        await _dataverseRepository.AddMessageAsync(message, ct);

        // 2. Rebuild session with new message appended
        var updatedMessages = new List<ChatMessage>(session.Messages) { message };
        var updatedSession = session with
        {
            Messages = updatedMessages.AsReadOnly(),
            LastActivity = DateTimeOffset.UtcNow
        };

        // 3. Refresh the Redis hot cache with the updated session
        await _sessionManager.UpdateSessionCacheAsync(updatedSession, ct);

        // 4. Update session activity in Dataverse (fire-and-forget acceptable for counters)
        _ = _dataverseRepository.UpdateSessionActivityAsync(
            session.TenantId,
            session.SessionId,
            updatedMessages.Count,
            updatedSession.LastActivity,
            ct);

        // 5. Trigger summarisation if threshold reached
        if (updatedMessages.Count >= SummarisationThreshold)
        {
            await TriggerSummarisationAsync(updatedSession, ct);
        }

        // 6. Trigger archive if approaching 50-message limit
        if (updatedMessages.Count >= ArchiveThreshold)
        {
            await ArchiveHistoryAsync(updatedSession, ct);
        }

        return updatedSession;
    }

    /// <summary>
    /// Returns the most recent N messages for a session.
    ///
    /// Hot path: reads from the <see cref="ChatSession.Messages"/> list in Redis.
    /// If the session is not in Redis, falls back to the Dataverse cold path via
    /// <see cref="ChatSessionManager.GetSessionAsync"/>.
    /// </summary>
    /// <param name="tenantId">Tenant ID for multi-tenant isolation.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="maxMessages">Maximum number of messages to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of messages (oldest first), up to <paramref name="maxMessages"/>.</returns>
    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string tenantId,
        string sessionId,
        int maxMessages = DefaultMaxMessages,
        CancellationToken ct = default)
    {
        var session = await _sessionManager.GetSessionAsync(tenantId, sessionId, ct);
        if (session is null)
        {
            _logger.LogWarning(
                "GetHistoryAsync: session {SessionId} not found for tenant {TenantId}",
                sessionId, tenantId);
            return Array.Empty<ChatMessage>();
        }

        var messages = session.Messages;
        if (messages.Count <= maxMessages)
        {
            return messages;
        }

        // Return the most recent N messages (tail of the ordered list)
        return messages
            .Skip(messages.Count - maxMessages)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Triggers conversation summarisation when the message count reaches
    /// <see cref="SummarisationThreshold"/> (15 messages).
    ///
    /// Phase 1 implementation: generates a placeholder summary.
    /// Phase D (AIPL-054+): will call the LLM via SprkChatAgent to produce a real summary.
    ///
    /// The summary is stored in <c>sprk_aichatsummary.sprk_summary</c> (cold storage).
    /// </summary>
    /// <param name="session">The session that has reached the summarisation threshold.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task TriggerSummarisationAsync(ChatSession session, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Summarisation triggered for session {SessionId} (messageCount={Count}, tenant={TenantId})",
            session.SessionId, session.Messages.Count, session.TenantId);

        // Phase 1: Placeholder summary — real LLM summarisation added in AIPL-054.
        // The summary condenses older messages to free context for newer messages.
        var olderMessages = session.Messages
            .Take(session.Messages.Count - 5) // Keep last 5 in full; summarise the rest
            .Select(m => $"[{m.Role}]: {m.Content[..Math.Min(100, m.Content.Length)]}...")
            .ToList();

        var summaryText = $"[Summary of {olderMessages.Count} earlier messages — "
                         + $"session {session.SessionId}, generated {DateTimeOffset.UtcNow:u}]";

        await _dataverseRepository.UpdateSessionSummaryAsync(
            session.TenantId,
            session.SessionId,
            summaryText,
            ct);
    }

    /// <summary>
    /// Archives the session's message history when the 50-message limit is approached (NFR-12).
    ///
    /// In Phase 1 (AIPL-052), archiving logs a warning and persists the current summary.
    /// Full archival strategy (moving older messages to a secondary store) is deferred to Phase D.
    /// </summary>
    /// <param name="session">The session approaching the archive threshold.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ArchiveHistoryAsync(ChatSession session, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Archive threshold reached for session {SessionId} (messageCount={Count}). "
            + "Archiving history (NFR-12). Tenant={TenantId}",
            session.SessionId, session.Messages.Count, session.TenantId);

        // Phase 1: Log and persist final summary.
        // Full archival (moving sprk_aichatmessage records to archive entity) is deferred.
        var archiveSummary = $"[ARCHIVED — session {session.SessionId} reached {session.Messages.Count} messages "
                            + $"at {DateTimeOffset.UtcNow:u}]";

        await _dataverseRepository.UpdateSessionSummaryAsync(
            session.TenantId,
            session.SessionId,
            archiveSummary,
            ct);
    }
}
