using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages the lifecycle of chat sessions: create, retrieve, and delete.
///
/// Storage Strategy (ADR-009, ADR-014, NFR-07):
///   - Hot path: Redis <see cref="IDistributedCache"/> with 24-hour sliding TTL.
///   - Cold path: Dataverse <c>sprk_aichatsummary</c> for persistent storage and audit.
///   - On cache miss: reconstructs session from Dataverse and messages from <c>sprk_aichatmessage</c>.
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

    // ADR-014: centralise key pattern in one place
    internal static string BuildCacheKey(string tenantId, string sessionId)
        => $"chat:session:{tenantId}:{sessionId}";

    public ChatSessionManager(
        IDistributedCache cache,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatSessionManager> logger)
    {
        _cache = cache;
        _dataverseRepository = dataverseRepository;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new chat session, persists the session record to Dataverse, and caches
    /// the empty session in Redis.
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation).</param>
    /// <param name="documentId">SPE document ID for the session context (may be null).</param>
    /// <param name="playbookId">Playbook that governs the agent's system prompt and tools.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created <see cref="ChatSession"/>.</returns>
    public async Task<ChatSession> CreateSessionAsync(
        string tenantId,
        string? documentId,
        Guid playbookId,
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
            Messages: []);

        _logger.LogInformation(
            "Creating chat session {SessionId} for tenant={TenantId}, document={DocumentId}, playbook={PlaybookId}",
            sessionId, tenantId, documentId, playbookId);

        // 1. Persist to Dataverse (cold storage — authoritative record)
        await _dataverseRepository.CreateSessionAsync(session, ct);

        // 2. Warm the Redis cache (hot path — fast lookup)
        await CacheSessionAsync(session, ct);

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

        // Mark as archived in Dataverse (preserves audit trail)
        await _dataverseRepository.ArchiveSessionAsync(tenantId, sessionId, ct);
    }

    /// <summary>
    /// Updates the cached session to reflect the latest message state.
    /// Called by <see cref="ChatHistoryManager"/> after adding a message.
    /// </summary>
    /// <param name="session">The updated session to cache.</param>
    /// <param name="ct">Cancellation token.</param>
    internal virtual async Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
    {
        await CacheSessionAsync(session, ct);
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
}
