using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages the lifecycle of chat sessions: create, retrieve, and delete.
///
/// Storage Strategy (ADR-009, ADR-014, NFR-07, D-06):
///   - Hot path: Redis <see cref="IDistributedCache"/> with 24-hour sliding TTL.
///   - Warm path: Cosmos DB via <see cref="ISessionPersistenceService"/> — write-through on every
///     Redis write; consulted on Redis miss before falling back to Dataverse (decision D-06).
///   - Cold path: Dataverse <c>sprk_aichatsummary</c> for persistent storage and audit.
///   - On cache miss: checks Cosmos DB first (fast warm path), then reconstructs from Dataverse.
///
/// Write-through pattern (D-06): every Redis write is immediately followed by a Cosmos DB upsert
/// executed as a fire-and-forget background task. Cosmos failures are logged at Warning and never
/// propagate to the caller — the Redis/response path is never blocked.
///
/// Cache Key Pattern (ADR-014): <c>"chat:session:{tenantId}:{sessionId}"</c>
/// The key is tenant-scoped to enforce multi-tenant isolation (NFR-09).
///
/// Lifetime: Scoped — one instance per HTTP request. <see cref="IDistributedCache"/> and
/// <see cref="IChatDataverseRepository"/> are injected and handle their own thread-safety.
/// </summary>
public class ChatSessionManager
{
    /// <summary>Sliding TTL for the Redis hot cache (NFR-07: 24-hour idle expiry).</summary>
    internal static readonly TimeSpan SessionCacheTtl = TimeSpan.FromHours(24);

    private readonly IDistributedCache _cache;
    private readonly IChatDataverseRepository _dataverseRepository;
    private readonly ILogger<ChatSessionManager> _logger;

    /// <summary>
    /// Optional Cosmos DB write-through persistence (decision D-06).
    /// Null when Cosmos DB is not configured (backward-compatible: existing behaviour is preserved).
    /// </summary>
    private readonly ISessionPersistenceService? _persistence;

    /// <summary>
    /// R5 task 007 (D1-07) — optional fire-and-forget signal to the session-files cleanup
    /// <see cref="SessionFilesCleanupJob"/>. Null when AI is disabled (compound gate OFF) so
    /// pre-R5 callers + the AI-OFF code path continue to work unchanged. When non-null,
    /// <see cref="DeleteSessionAsync"/> raises a session-end signal at the end of its
    /// existing logic so the cleanup job evicts session-files index documents immediately
    /// (spec NFR-02 "Aggressive cleanup on session-end").
    /// </summary>
    private readonly ISessionFilesCleanupSignal? _cleanupSignal;

    // ADR-014: centralise key pattern in one place
    internal static string BuildCacheKey(string tenantId, string sessionId)
        => $"chat:session:{tenantId}:{sessionId}";

    public ChatSessionManager(
        IDistributedCache cache,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatSessionManager> logger,
        ISessionPersistenceService? persistence = null,
        ISessionFilesCleanupSignal? cleanupSignal = null)
    {
        _cache = cache;
        _dataverseRepository = dataverseRepository;
        _logger = logger;
        _persistence = persistence;
        _cleanupSignal = cleanupSignal;
    }

    /// <summary>
    /// Creates a new chat session, persists the session record to Dataverse, and caches
    /// the empty session in Redis.
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation).</param>
    /// <param name="documentId">SPE document ID for the session context (may be null).</param>
    /// <param name="playbookId">Playbook that governs the agent's system prompt and tools.</param>
    /// <param name="hostContext">Optional host context describing where SprkChat is embedded.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created <see cref="ChatSession"/>.</returns>
    public async Task<ChatSession> CreateSessionAsync(
        string tenantId,
        string? documentId,
        Guid? playbookId = null,
        ChatHostContext? hostContext = null,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var session = new ChatSession(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: documentId,
            PlaybookId: playbookId,
            CreatedAt: now,
            LastActivity: now,
            Messages: [],
            HostContext: hostContext);

        _logger.LogInformation(
            "Creating chat session {SessionId} for tenant={TenantId}, document={DocumentId}, playbook={PlaybookId}",
            sessionId, tenantId, documentId, playbookId);

        // 1. Persist to Dataverse (cold storage — authoritative record)
        // Non-fatal: if the sprk_aichatsummary entity is not yet deployed, log and continue.
        // Redis is the hot path and sufficient for session functionality (Phase 1).
        try
        {
            await _dataverseRepository.CreateSessionAsync(session, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Dataverse persistence failed for session {SessionId} — continuing with Redis only. " +
                "Ensure sprk_aichatsummary entity exists in the environment.",
                sessionId);
        }

        // 2. Warm the Redis cache (hot path — fast lookup)
        await CacheSessionAsync(session, ct);

        // 3. Write-through to Cosmos DB (warm path, decision D-06) — fire-and-forget.
        // Cosmos failure must not block the session create response or fail the request.
        FireAndForgetCosmosPersist(session);

        return session;
    }

    /// <summary>
    /// Retrieves a chat session by tenant and session ID.
    ///
    /// Cache Strategy:
    ///   - Redis hit: returns cached session immediately (no Dataverse call).
    ///   - Redis miss: loads session metadata from <c>sprk_aichatsummary</c> and
    ///     message history from <c>sprk_aichatmessage</c>, then re-warms the cache.
    ///
    /// Returns <c>null</c> if the session does not exist in Redis or Dataverse.
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation).</param>
    /// <param name="sessionId">Session ID to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="ChatSession"/> or <c>null</c> if not found.</returns>
    public virtual async Task<ChatSession?> GetSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        var key = BuildCacheKey(tenantId, sessionId);

        // Hot path: Redis cache (ADR-009 — Redis first)
        var cachedBytes = await _cache.GetAsync(key, ct);
        if (cachedBytes is not null)
        {
            _logger.LogDebug("Cache HIT for session {SessionId} (tenant={TenantId})", sessionId, tenantId);
            var cached = JsonSerializer.Deserialize<ChatSession>(cachedBytes);
            if (cached is not null)
            {
                // Refresh sliding TTL on access
                await _cache.RefreshAsync(key, ct);
                return cached;
            }
        }

        // Warm path: Cosmos DB fallback (decision D-06 — checked before Dataverse on Redis miss)
        if (_persistence is not null)
        {
            _logger.LogDebug(
                "Cache MISS for session {SessionId} — checking Cosmos DB before Dataverse (tenant={TenantId})",
                sessionId, tenantId);

            var storedSession = await _persistence.LoadSessionAsync(tenantId, sessionId, ct);
            if (storedSession is not null)
            {
                // Map StoredSession back to ChatSession and re-warm the Redis hot cache
                var cosmosSession = MapStoredSessionToChatSession(storedSession);
                await CacheSessionAsync(cosmosSession, ct);

                _logger.LogDebug(
                    "Cosmos DB HIT for session {SessionId} — Redis re-warmed (tenant={TenantId})",
                    sessionId, tenantId);

                return cosmosSession;
            }
        }

        // Cold path: Dataverse fallback
        _logger.LogDebug("Cache MISS for session {SessionId} — loading from Dataverse (tenant={TenantId})", sessionId, tenantId);
        var session = await _dataverseRepository.GetSessionAsync(tenantId, sessionId, ct);
        if (session is null)
        {
            return null;
        }

        // Re-warm the cache so subsequent requests hit Redis
        await CacheSessionAsync(session, ct);
        return session;
    }

    /// <summary>
    /// Deletes a chat session from Redis and marks it as archived in Dataverse.
    ///
    /// Does not delete Dataverse records — the <c>sprk_aichatsummary</c> and associated
    /// <c>sprk_aichatmessage</c> records are retained as an audit trail.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleting chat session {SessionId} (tenant={TenantId})",
            sessionId, tenantId);

        var key = BuildCacheKey(tenantId, sessionId);

        // Remove from Redis hot cache
        await _cache.RemoveAsync(key, ct);

        // Mark as archived in Dataverse (preserves audit trail — archive-not-delete pattern)
        await _dataverseRepository.ArchiveSessionAsync(tenantId, sessionId, ct);

        // Remove from Cosmos DB (warm path, D-06) — failure is non-fatal, logged at Warning.
        if (_persistence is not null)
        {
            try
            {
                await _persistence.DeleteSessionAsync(tenantId, sessionId, ct);
                _logger.LogDebug(
                    "Cosmos DB session {SessionId} deleted successfully (tenant={TenantId})",
                    sessionId, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cosmos DB delete failed for session {SessionId} (tenant={TenantId}) — Dataverse archive still succeeded",
                    sessionId, tenantId);
            }
        }

        // R5 task 007 — fire-and-forget signal to the session-files cleanup hosted service
        // (spec NFR-02 "Aggressive cleanup on session-end"). Mirrors the Cosmos
        // fire-and-forget convention above: log-and-swallow on failure; never throws.
        // Null when AI compound gate is OFF — existing behaviour preserved.
        try
        {
            _cleanupSignal?.SignalSessionEnded(tenantId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Session-files cleanup signal failed for session {SessionId} (tenant={TenantId}) — eviction will run on the next scheduled scan",
                sessionId, tenantId);
        }
    }

    /// <summary>
    /// Updates the cached session to reflect the latest message state, then writes through to
    /// Cosmos DB (decision D-06). Called by <see cref="ChatHistoryManager"/> after adding a message.
    /// </summary>
    /// <param name="session">The updated session to cache.</param>
    /// <param name="ct">Cancellation token.</param>
    internal virtual async Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
    {
        // 1. Refresh Redis hot cache
        await CacheSessionAsync(session, ct);

        // 2. Write-through to Cosmos DB (warm path, D-06) — fire-and-forget.
        // Cosmos failure must not block the message add path or affect the streaming response.
        FireAndForgetCosmosPersist(session);
    }

    // === Private helpers ===

    /// <summary>
    /// Serialises the session to JSON and stores it in Redis with the configured sliding TTL.
    /// </summary>
    private async Task CacheSessionAsync(ChatSession session, CancellationToken ct)
    {
        var key = BuildCacheKey(session.TenantId, session.SessionId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        var options = new DistributedCacheEntryOptions
        {
            // Sliding expiry: resets on every RefreshAsync/GetAsync access (ADR-009, NFR-07)
            SlidingExpiration = SessionCacheTtl
        };
        await _cache.SetAsync(key, bytes, options, ct);
    }

    /// <summary>
    /// Launches a fire-and-forget Cosmos DB upsert for <paramref name="session"/>.
    ///
    /// Uses <c>CancellationToken.None</c> so the Cosmos write survives HTTP request completion.
    /// Any exception is caught inside <see cref="ISessionPersistenceService.PersistSessionAsync"/>
    /// and logged at Warning — it is never re-thrown here (ADR-015, D-06).
    ///
    /// When <see cref="_persistence"/> is null (Cosmos not configured), this is a no-op.
    /// </summary>
    private void FireAndForgetCosmosPersist(ChatSession session)
    {
        if (_persistence is null)
        {
            return;
        }

        var stored = MapChatSessionToStoredSession(session);

        // We intentionally do NOT await this. The fire-and-forget pattern (D-06) means
        // Cosmos writes never block the Redis/response path. CancellationToken.None ensures
        // the write survives HTTP request cancellation.
        _ = _persistence.PersistSessionAsync(stored, CancellationToken.None);
    }

    /// <summary>
    /// Maps a <see cref="ChatSession"/> (hot Redis model) to a <see cref="StoredSession"/>
    /// (Cosmos DB warm document). Preserves all message content and metadata.
    ///
    /// Content is permitted at ADR-015 Tier 3 (user-owned work history, Cosmos warm store).
    /// </summary>
    private static StoredSession MapChatSessionToStoredSession(ChatSession session)
    {
        var messages = session.Messages
            .Select(m => new SessionMessage
            {
                MessageId = m.MessageId,
                Role = m.Role.ToString().ToLowerInvariant(),
                Content = m.Content,
                Timestamp = m.CreatedAt,
                Metadata = new Dictionary<string, string>
                {
                    ["tokenCount"] = m.TokenCount.ToString(),
                    ["sequenceNumber"] = m.SequenceNumber.ToString()
                }
            })
            .ToList();

        return new StoredSession
        {
            Id = session.SessionId,
            SessionId = session.SessionId,
            TenantId = session.TenantId,
            PlaybookId = session.PlaybookId,
            Messages = messages,
            WidgetStates = [],
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity
        };
    }

    /// <summary>
    /// Maps a <see cref="StoredSession"/> (Cosmos warm document) back to a <see cref="ChatSession"/>
    /// (hot Redis model) for Cosmos-fallback scenarios.
    ///
    /// Message content round-trips faithfully. Fields present only on <see cref="StoredSession"/>
    /// (widget states, entity refs, summary) have no equivalent on <see cref="ChatSession"/> and
    /// are discarded — they remain in Cosmos and are accessible via <see cref="ISessionPersistenceService"/>.
    /// </summary>
    private static ChatSession MapStoredSessionToChatSession(StoredSession stored)
    {
        var messages = stored.Messages
            .Select((m, index) =>
            {
                var role = Enum.TryParse<ChatMessageRole>(m.Role, ignoreCase: true, out var parsed)
                    ? parsed
                    : ChatMessageRole.User;

                _ = int.TryParse(
                    m.Metadata.GetValueOrDefault("tokenCount", "0"), out var tokenCount);
                _ = int.TryParse(
                    m.Metadata.GetValueOrDefault("sequenceNumber", index.ToString()), out var seqNum);

                return new ChatMessage(
                    MessageId: m.MessageId,
                    SessionId: stored.SessionId,
                    Role: role,
                    Content: m.Content,
                    TokenCount: tokenCount,
                    CreatedAt: m.Timestamp,
                    SequenceNumber: seqNum);
            })
            .ToList()
            .AsReadOnly();

        return new ChatSession(
            SessionId: stored.SessionId,
            TenantId: stored.TenantId,
            DocumentId: null,          // Not stored in Cosmos — Dataverse is authoritative for document associations
            PlaybookId: stored.PlaybookId,
            CreatedAt: stored.CreatedAt,
            LastActivity: stored.LastActivity,
            Messages: messages);
    }
}
