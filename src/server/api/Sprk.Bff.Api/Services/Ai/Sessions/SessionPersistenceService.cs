using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Redis + Cosmos DB write-through persistence for AI chat sessions (ADR-015 Tier 3, decision D-06).
///
/// Storage Tiers:
///   - Hot:  Redis via <see cref="IDistributedCache"/> — 24-hour sliding TTL. Key: <c>sessions:{tenantId}:{sessionId}</c>.
///   - Warm: Cosmos DB <c>sessions</c> container, partition key <c>/tenantId</c> — 90-day retention.
///
/// Write-through semantics:
///   1. Read the current session document from Redis (or an empty shell if new).
///   2. Append the message, update LastActivity.
///   3. Write updated document to Redis (non-blocking on failure).
///   4. Upsert into Cosmos DB (fire-and-forget background task; non-blocking on failure).
///
/// Failure policy: either store failing is caught, logged at Warning, and swallowed.
/// The SSE streaming pipeline is never blocked by a persistence failure.
///
/// Tenant isolation: all keys and Cosmos partition values are scoped by tenantId (ADR-015, NFR-09).
/// GDPR: <see cref="DeleteSessionAsync"/> removes data from both stores (Art. 17).
///
/// Lifetime: Scoped — one instance per HTTP request. CosmosClient is singleton (injected).
/// </summary>
public class SessionPersistenceService : ISessionPersistenceService
{
    /// <summary>Redis sliding TTL for hot session cache (NFR-07: 24-hour idle expiry, ADR-009).</summary>
    internal static readonly TimeSpan RedisTtl = TimeSpan.FromHours(24);

    /// <summary>Redis key prefix — distinct from ChatSessionManager's "chat:session:" prefix (ADR-014).</summary>
    private const string RedisKeyPrefix = "sessions";

    /// <summary>Cosmos DB container name (ADR-015 Tier 3 container mapping).</summary>
    private const string ContainerName = "sessions";

    private readonly IDistributedCache _cache;
    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<SessionPersistenceService> _logger;

    public SessionPersistenceService(
        IDistributedCache cache,
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<SessionPersistenceService> logger)
    {
        _cache = cache;
        _cosmosClient = cosmosClient;
        _databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosPersistence:DatabaseName is not configured.");
        _logger = logger;
    }

    // =========================================================================
    // ISessionPersistenceService
    // =========================================================================

    /// <inheritdoc/>
    public async Task PersistMessageAsync(
        string tenantId,
        string sessionId,
        SessionMessage message,
        CancellationToken ct = default)
    {
        // Load existing session from Redis (or initialise an empty document)
        var session = await LoadFromRedisAsync(tenantId, sessionId, ct)
            ?? CreateEmptySession(tenantId, sessionId);

        session.Messages.Add(message);
        session.LastActivity = DateTimeOffset.UtcNow;

        // Write to Redis (hot tier) — non-blocking on failure
        await WriteToRedisAsync(tenantId, sessionId, session, ct);

        // Upsert to Cosmos DB (warm tier) — fire-and-forget, non-blocking on failure
        // We do NOT pass the request CancellationToken to the background task because
        // the HTTP request may complete (or be cancelled) before the Cosmos write finishes.
        _ = UpsertToCosmosAsync(session, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task<StoredSession?> LoadSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        // Hot path: Redis
        var cached = await LoadFromRedisAsync(tenantId, sessionId, ct);
        if (cached is not null)
        {
            _logger.LogDebug(
                "SessionPersistenceService: Redis HIT for session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
            return cached;
        }

        // Warm path: Cosmos DB fallback
        _logger.LogDebug(
            "SessionPersistenceService: Redis MISS for session {SessionId} — loading from Cosmos DB (tenant={TenantId})",
            sessionId, tenantId);

        var fromCosmos = await LoadFromCosmosAsync(tenantId, sessionId, ct);
        if (fromCosmos is null)
        {
            return null;
        }

        // Re-warm Redis so subsequent requests hit the hot path
        await WriteToRedisAsync(tenantId, sessionId, fromCosmos, ct);
        return fromCosmos;
    }

    /// <inheritdoc/>
    public async Task PersistSummaryAsync(
        string tenantId,
        string sessionId,
        SessionSummary summary,
        CancellationToken ct = default)
    {
        // Load existing session from Redis (or Cosmos on cache miss)
        var session = await LoadFromRedisAsync(tenantId, sessionId, ct)
            ?? await LoadFromCosmosAsync(tenantId, sessionId, ct)
            ?? CreateEmptySession(tenantId, sessionId);

        // Write the structured summary alongside the verbatim messages (never remove messages).
        session.Summary = summary;
        session.LastActivity = DateTimeOffset.UtcNow;

        // Write updated document to Redis (hot tier) — non-blocking on failure
        await WriteToRedisAsync(tenantId, sessionId, session, ct);

        // Upsert to Cosmos DB (warm tier) — fire-and-forget, non-blocking on failure.
        // We do NOT pass the request CancellationToken because the HTTP request may have
        // completed (or been cancelled) before the Cosmos write finishes (same pattern as
        // PersistMessageAsync — ADR-015 D-06).
        _ = UpsertToCosmosAsync(session, CancellationToken.None);
    }

    /// <inheritdoc/>
    public async Task DeleteSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SessionPersistenceService: Deleting session {SessionId} from both stores (tenant={TenantId})",
            sessionId, tenantId);

        // Delete from Redis
        try
        {
            var key = BuildRedisKey(tenantId, sessionId);
            await _cache.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionPersistenceService: Redis delete failed for session {SessionId} (tenant={TenantId}) — continuing",
                sessionId, tenantId);
        }

        // Delete from Cosmos DB (GDPR Art. 17 erasure)
        try
        {
            var container = GetContainer();
            await container.DeleteItemAsync<StoredSession>(
                id: sessionId,
                partitionKey: new PartitionKey(tenantId),
                cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone — idempotent delete is safe
            _logger.LogDebug(
                "SessionPersistenceService: Cosmos delete skipped — session {SessionId} not found (tenant={TenantId})",
                sessionId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionPersistenceService: Cosmos delete failed for session {SessionId} (tenant={TenantId}) — continuing",
                sessionId, tenantId);
        }
    }

    /// <inheritdoc/>
    public async Task PersistSessionAsync(StoredSession session, CancellationToken ct = default)
    {
        // Write to Redis (hot tier) — non-blocking on failure
        await WriteToRedisAsync(session.TenantId, session.SessionId, session, ct);

        // Upsert to Cosmos DB (warm tier) — fire-and-forget, non-blocking on failure.
        // We do NOT pass the request CancellationToken because the HTTP request may complete
        // (or be cancelled) before the Cosmos write finishes (same pattern as PersistMessageAsync).
        _ = UpsertToCosmosAsync(session, CancellationToken.None);
    }

    // =========================================================================
    // Private helpers — Redis
    // =========================================================================

    /// <summary>Builds the Redis key for a session. Pattern: <c>sessions:{tenantId}:{sessionId}</c>.</summary>
    internal static string BuildRedisKey(string tenantId, string sessionId)
        => $"{RedisKeyPrefix}:{tenantId}:{sessionId}";

    private async Task<StoredSession?> LoadFromRedisAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var key = BuildRedisKey(tenantId, sessionId);
            var bytes = await _cache.GetAsync(key, ct);
            if (bytes is null)
            {
                return null;
            }

            var session = JsonSerializer.Deserialize<StoredSession>(bytes);
            if (session is not null)
            {
                // Refresh sliding TTL on every access (ADR-009)
                await _cache.RefreshAsync(key, ct);
            }

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionPersistenceService: Redis read failed for session {SessionId} (tenant={TenantId}) — falling through to Cosmos DB",
                sessionId, tenantId);
            return null;
        }
    }

    private async Task WriteToRedisAsync(
        string tenantId,
        string sessionId,
        StoredSession session,
        CancellationToken ct)
    {
        try
        {
            var key = BuildRedisKey(tenantId, sessionId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = RedisTtl   // ADR-009, NFR-07
            };
            await _cache.SetAsync(key, bytes, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionPersistenceService: Redis write failed for session {SessionId} (tenant={TenantId}) — continuing without hot cache",
                sessionId, tenantId);
        }
    }

    // =========================================================================
    // Private helpers — Cosmos DB
    // =========================================================================

    private Container GetContainer() => _cosmosClient.GetContainer(_databaseName, ContainerName);

    private async Task<StoredSession?> LoadFromCosmosAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var container = GetContainer();
            var response = await container.ReadItemAsync<StoredSession>(
                id: sessionId,
                partitionKey: new PartitionKey(tenantId),
                cancellationToken: ct);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionPersistenceService: Cosmos DB read failed for session {SessionId} (tenant={TenantId}) — returning null",
                sessionId, tenantId);
            return null;
        }
    }

    /// <summary>
    /// Fire-and-forget Cosmos DB upsert. Uses retry via Polly-style backoff is not needed here
    /// because CosmosClient has built-in retry policy. Any remaining failure is logged and swallowed
    /// so it never blocks the streaming SSE response.
    /// </summary>
    private async Task UpsertToCosmosAsync(StoredSession session, CancellationToken ct)
    {
        try
        {
            var container = GetContainer();
            await container.UpsertItemAsync(
                item: session,
                partitionKey: new PartitionKey(session.TenantId),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Logged at Warning — not re-thrown. Cosmos failure must not surface to the user (ADR-015 D-06).
            _logger.LogWarning(ex,
                "SessionPersistenceService: Cosmos DB write failed for session {SessionId} (tenant={TenantId}, store=Cosmos) — streaming continues",
                session.SessionId, session.TenantId);
        }
    }

    // =========================================================================
    // Private helpers — Factory
    // =========================================================================

    private static StoredSession CreateEmptySession(string tenantId, string sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        return new StoredSession
        {
            Id = sessionId,
            SessionId = sessionId,
            TenantId = tenantId,
            Messages = [],
            WidgetStates = [],
            CreatedAt = now,
            LastActivity = now
        };
    }
}
