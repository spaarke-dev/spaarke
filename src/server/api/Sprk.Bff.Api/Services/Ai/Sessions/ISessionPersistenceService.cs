namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Dual-store persistence for AI chat sessions: Redis (hot, 24h TTL) and Cosmos DB (warm, 90-day retention).
///
/// Write-through pattern (decision D-06): every message is written to both stores within the request lifecycle.
/// A failure in either store is logged at Warning level but never re-thrown — streaming is never blocked.
///
/// Tenant isolation: all operations are scoped by <paramref name="tenantId"/> to enforce multi-tenant
/// isolation (ADR-015, NFR-09). Cosmos documents are partitioned by <c>/tenantId</c>.
///
/// GDPR: <see cref="DeleteSessionAsync"/> removes data from both stores (ADR-015 Art. 17 support).
/// </summary>
public interface ISessionPersistenceService
{
    /// <summary>
    /// Persists a single message to the session in both Redis (hot) and Cosmos DB (warm).
    ///
    /// Write order: Redis first, then Cosmos DB fire-and-forget with retry.
    /// Either write failing is non-fatal — logged at Warning, streaming continues.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="message">The message to append.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PersistMessageAsync(string tenantId, string sessionId, SessionMessage message, CancellationToken ct = default);

    /// <summary>
    /// Loads a session. Tries Redis first; falls back to Cosmos DB on cache miss.
    /// On Cosmos fallback, re-warms the Redis cache for subsequent requests.
    /// Returns <c>null</c> if the session does not exist in either store.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StoredSession?> LoadSessionAsync(string tenantId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the session from both Redis and Cosmos DB.
    /// Supports GDPR right to erasure (ADR-015 Tier 3, Art. 17).
    /// Failures in either store are logged but not re-thrown.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteSessionAsync(string tenantId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists a <see cref="SessionSummary"/> to the session document in both Redis and Cosmos DB.
    ///
    /// Merges the summary into the existing session document — full message history is never deleted.
    /// After persisting the summary, the in-memory session's Messages list is trimmed to the last
    /// <see cref="ISessionSummarizationService.TailMessageCount"/> messages (AIPU2-032).
    ///
    /// Either store failing is non-fatal — logged at Warning, streaming continues.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="summary">The completed summary to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PersistSummaryAsync(
        string tenantId,
        string sessionId,
        SessionSummary summary,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts a complete <see cref="StoredSession"/> document to Cosmos DB (warm tier).
    ///
    /// Used by <see cref="Sprk.Bff.Api.Services.Ai.Chat.ChatSessionManager"/> for write-through
    /// on session create and cache updates (decision D-06). The session document replaces any
    /// existing document with the same id and partition key.
    ///
    /// Failure is non-fatal — logged at Warning, streaming continues.
    /// </summary>
    /// <param name="session">The session document to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PersistSessionAsync(StoredSession session, CancellationToken ct = default);

    /// <summary>
    /// Persists workspace tabs[] + activeTabId for a session (NFR-09 write-through).
    ///
    /// Loads the existing <see cref="StoredSession"/> by (sessionId, tenantId), updates only
    /// <see cref="StoredSession.Tabs"/>, <see cref="StoredSession.ActiveTabId"/>, and
    /// <see cref="StoredSession.LastActivity"/>, then writes the document back through both
    /// stores (Redis hot + Cosmos warm) using the same write-through pattern as
    /// <see cref="PersistMessageAsync"/> (D-06).
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="tenantId">Tenant identifier (Cosmos partition key /tenantId per ADR-015).</param>
    /// <param name="tabs">The new tab list (replaces existing).</param>
    /// <param name="activeTabId">The active tab id at the time of save; may be the Home tab id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the session was found and updated; <c>false</c> if the session does not exist.</returns>
    Task<bool> SaveTabsAsync(
        string sessionId,
        string tenantId,
        IReadOnlyList<StoredWorkspaceTab> tabs,
        string? activeTabId,
        CancellationToken cancellationToken = default);
}
