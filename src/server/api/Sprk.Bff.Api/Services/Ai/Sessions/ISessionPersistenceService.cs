using Sprk.Bff.Api.Models.Ai.Chat;

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
    /// spaarkeai-compose-r8 FR-B04 (task 062) — answers "does this session document still exist, and
    /// under what retention?" WITHOUT collapsing a read failure into "it is gone".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not <see cref="LoadSessionAsync"/>.</b> That method returns <c>null</c> for both
    /// "not found" and "the read failed" — its Cosmos path catches every exception and returns null by
    /// design, because a restore that cannot read must degrade to the new-session path rather than 500.
    /// For retention the same collapse is catastrophic in the opposite direction: a Cosmos outage would
    /// present as "every session has expired" and a sweep built on it would delete every durable byte
    /// in the account. This probe therefore distinguishes
    /// <see cref="SessionRetentionState.Absent"/> (a real 404 on a point read) from
    /// <see cref="SessionRetentionState.Indeterminate"/> (anything else), and callers must treat
    /// Indeterminate as RETAIN.
    /// </para>
    /// <para>
    /// <b>Cosmos only — Redis is deliberately not consulted.</b> The hot tier's 24h sliding TTL means a
    /// Redis miss carries no information about retention at all, and a Redis hit would only re-confirm
    /// what the warm tier already knows. A point read on <c>(id, /tenantId)</c> is ~1 RU and is the
    /// authoritative answer. It also avoids the re-warm side effect <see cref="LoadSessionAsync"/> has,
    /// which a background sweep must not trigger for thousands of long-dead sessions.
    /// </para>
    /// <para>
    /// Extends this existing service rather than adding a new one (root CLAUDE.md §11): the Cosmos
    /// container handle, the partition-key convention and the tenant scoping all already live here, and
    /// task 063's erasure path needs the same question answered.
    /// </para>
    /// </remarks>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="sessionId">Session identifier (document id).</param>
    /// <param name="ct">Cancellation token. Cancellation propagates — it is not an Indeterminate.</param>
    Task<SessionRetentionProbe> ProbeSessionRetentionAsync(string tenantId, string sessionId, CancellationToken ct = default);

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
    /// <param name="awaitCosmosWrite">
    /// FR-D2 (task 030): when <c>true</c>, the Cosmos upsert is CONFIRMED — this method does not
    /// return until the write completes — so the caller's "before request completes" durability
    /// contract holds even under a Redis eviction moments later. Reserved for the FIRST message of
    /// a session (<c>messages[0]</c>), which also seeds the History title (FR-D4). Defaults to
    /// <c>false</c>, which preserves the original D-06 fire-and-forget contract for every other
    /// call site — remaining turns are NOT slowed down by this change (NFR-03).
    /// </param>
    Task PersistSessionAsync(StoredSession session, CancellationToken ct = default, bool awaitCosmosWrite = false);

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

    /// <summary>
    /// Persists the session's uploaded-file manifest (with enrichment fields) to both
    /// Redis (hot) and Cosmos DB (warm). Called by the upload-pipeline orchestrator after
    /// parallel enrichment (classify + summarize + manifest-extraction) completes for a file
    /// (chat-routing-redesign-r1 architecture §6.1 / §7.1, task 072).
    ///
    /// <para>Strategy: REPLACE — the orchestrator supplies the complete enriched snapshot.
    /// The existing <see cref="StoredSession.UploadedFiles"/> collection is replaced wholesale,
    /// avoiding per-FileId merge complexity and stale-data risk.</para>
    ///
    /// <para>Concurrency: matches the <see cref="SaveTabsAsync"/> precedent — fire-and-forget
    /// Cosmos upsert, last-writer-wins. No ETag conflict surfaced to the caller.</para>
    ///
    /// <para>Logging: ADR-015 Tier-1 safe — emits <c>sessionId</c>, <c>tenantId</c>,
    /// <c>fileCount</c>, <c>durationMs</c> only. NEVER logs per-file summary text,
    /// classification text, section names, or file names.</para>
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="tenantId">Tenant identifier (Cosmos partition key /tenantId per ADR-015).</param>
    /// <param name="enrichedFiles">
    /// Complete enriched-state snapshot of all uploaded files for the session.
    /// Caller is responsible for the 20-file cap (mirrors <c>ChatSession.MaxUploadedFiles</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the session was found and updated; <c>false</c> if the session does not exist.</returns>
    Task<bool> UpdateUploadedFilesAsync(
        string sessionId,
        string tenantId,
        IReadOnlyList<ChatSessionFile> enrichedFiles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists <paramref name="ownerOid"/>'s most-recently-active sessions (History dropdown; R4-8).
    ///
    /// Queries the Cosmos <c>sessions</c> container within the <paramref name="tenantId"/> partition,
    /// ordered by <see cref="StoredSession.LastActivity"/> descending, projecting only the fields the
    /// History list needs (never the full message history). Failures are logged at Warning and yield an
    /// empty list — the History surface degrades gracefully (ADR-015 D-06).
    /// </summary>
    /// <remarks>
    /// <b>Issue #863.</b> This was tenant-scoped only until 2026-08-28, so it returned every user's
    /// sessions to every user in the tenant — including each row's session id, title and a content
    /// preview. That made it both the disclosure itself and the delivery mechanism for the
    /// unauthenticated-delete gap, since the ids it handed out were the ids
    /// <c>DELETE /api/ai/chat/sessions/{id}</c> accepted without an owner check.
    /// <paramref name="ownerOid"/> is therefore <b>required</b>: a caller that cannot be identified
    /// gets a <c>401</c> at the endpoint, never an unfiltered list.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier (partition key).</param>
    /// <param name="ownerOid">
    /// Entra <c>oid</c> of the caller — the only sessions returned. Sessions written before #863
    /// carry no owner and therefore match nobody (fail closed; see <c>ChatSession.OwnerOid</c>).
    /// </param>
    /// <param name="limit">Maximum number of sessions to return (clamped to 1..50).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<RecentSessionInfo>> ListRecentSessionsAsync(
        string tenantId,
        string ownerOid,
        int limit,
        CancellationToken ct = default);
}

/// <summary>
/// Lightweight projection of a session for the History list (R4-8). Never carries the message body —
/// only what the dropdown renders (a display title + last-activity timestamp + optional entity/playbook
/// labels). Maps 1:1 to the client's history row contract.
///
/// <para><b>FR-D7 (spaarkeai-assistant-enhancements-r2, DI-01)</b> adds <see cref="Preview"/>,
/// <see cref="MessageCount"/>, and <see cref="TabSummary"/> so the History row can render a
/// last-message preview + message count + tab summary ("Email · Compose"), completing the
/// client-side rendering `HistoryOverlay.tsx` shipped forward-compatible in task 037. All three
/// are bounded/optional — a session that predates these fields (or has none of the underlying
/// content) simply omits them; the client already renders that state gracefully.</para>
/// </summary>
public record RecentSessionInfo(
    string SessionId,
    string Title,
    string? EntityType,
    string? EntityName,
    string? PlaybookName,
    DateTimeOffset UpdatedAt,
    string? Preview,
    int? MessageCount,
    string? TabSummary);
