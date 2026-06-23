using System.Diagnostics;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Telemetry;

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
    private readonly IContextEventEmitter _contextEventEmitter;

    public SessionPersistenceService(
        IDistributedCache cache,
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<SessionPersistenceService> logger,
        IContextEventEmitter contextEventEmitter)
    {
        _cache = cache;
        _cosmosClient = cosmosClient;
        _databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosPersistence:DatabaseName is not configured.");
        _logger = logger;
        _contextEventEmitter = contextEventEmitter ?? throw new ArgumentNullException(nameof(contextEventEmitter));
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
    // SaveTabsAsync (NFR-09 — task 065)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene + ADR-013):
    //   - In-process extension of the existing AI session persistence pipeline.
    //   - NO new DI feature module (registration handled by existing AiPersistenceModule
    //     via the ISessionPersistenceService interface — ADR-010).
    //   - NO new service, NO new NuGet packages.
    //   - Reuses the same Redis-hot + Cosmos-warm write-through pattern as PersistMessageAsync
    //     and PersistSummaryAsync (D-06). Latency profile is identical to existing methods.
    //   - Additive Cosmos schema change (StoredSession.Tabs + ActiveTabId) — backwards
    //     compatible with older documents (ADR-015 partition key /tenantId unchanged).
    //   - All four BFF decision criteria from ADR-013 answer "BFF" → stays here.

    /// <inheritdoc/>
    public async Task<bool> SaveTabsAsync(
        string sessionId,
        string tenantId,
        IReadOnlyList<StoredWorkspaceTab> tabs,
        string? activeTabId,
        CancellationToken cancellationToken = default)
    {
        // Load existing session: try Redis first, fall back to Cosmos. Mirrors LoadSessionAsync
        // but without re-warming Redis (we'll write the full session back below anyway).
        var session = await LoadFromRedisAsync(tenantId, sessionId, cancellationToken)
            ?? await LoadFromCosmosAsync(tenantId, sessionId, cancellationToken);

        if (session is null)
        {
            _logger.LogDebug(
                "SessionPersistenceService.SaveTabsAsync: session {SessionId} not found (tenant={TenantId}) — returning false",
                sessionId, tenantId);
            return false;
        }

        // Mutate only tab-related fields + LastActivity. All other state (Messages, WidgetStates,
        // Summary, EntityRefs) is preserved verbatim.
        session.Tabs = tabs.ToList();
        session.ActiveTabId = activeTabId;
        session.LastActivity = DateTimeOffset.UtcNow;

        // Write-through (D-06): Redis hot tier first, then Cosmos warm tier (fire-and-forget).
        // Neither failure surfaces to the caller — matches the existing PersistMessageAsync contract.
        await WriteToRedisAsync(tenantId, sessionId, session, cancellationToken);
        _ = UpsertToCosmosAsync(session, CancellationToken.None);

        return true;
    }

    // =========================================================================
    // UpdateUploadedFilesAsync (chat-routing-redesign-r1 task 072 — architecture §6.1 + §7.1)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene + ADR-013):
    //   - In-process extension of the existing AI session persistence pipeline.
    //   - NO new DI feature module — registration handled by existing AiPersistenceModule
    //     via the ISessionPersistenceService interface (ADR-010).
    //   - NO new service, NO new NuGet packages, NO new Cosmos container / doc-type.
    //     Architecture §7.1 explicitly reuses the existing `sessions` container.
    //   - Reuses the same Redis-hot + Cosmos-warm write-through pattern as SaveTabsAsync (D-06).
    //   - Additive Cosmos schema change (StoredSession.UploadedFiles) — backwards
    //     compatible with older documents (ADR-015 partition key /tenantId unchanged).
    //
    // STRATEGY: REPLACE (not merge).
    //   The upload-pipeline orchestrator (SessionFileEnrichmentService, task 066) returns
    //   the complete enriched-state snapshot for the session's uploaded files. Replacing the
    //   collection wholesale is simpler than per-FileId merge and avoids stale-data risk
    //   (e.g., a file deleted upstream lingering in storage). See architecture §6.1.
    //
    // ETAG / OPTIMISTIC CONCURRENCY:
    //   The peer SaveTabsAsync precedent does NOT use ETag (fire-and-forget UpsertItemAsync
    //   without IfMatchEtag). Matching that precedent: this method swallows Cosmos write
    //   failures at Warning level rather than surfacing concurrency exceptions to the caller.
    //   The session-document write rate is low (one write per uploaded file's enrichment
    //   completion) so last-writer-wins is acceptable per architecture §6.1.

    /// <inheritdoc/>
    public async Task<bool> UpdateUploadedFilesAsync(
        string sessionId,
        string tenantId,
        IReadOnlyList<ChatSessionFile> enrichedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrichedFiles);

        var stopwatch = Stopwatch.StartNew();

        // Load existing session: try Redis first, fall back to Cosmos. Mirrors SaveTabsAsync.
        var session = await LoadFromRedisAsync(tenantId, sessionId, cancellationToken)
            ?? await LoadFromCosmosAsync(tenantId, sessionId, cancellationToken);

        if (session is null)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "SessionPersistenceService.UpdateUploadedFilesAsync: session {SessionId} not found (tenant={TenantId}) — durationMs={DurationMs}",
                sessionId, tenantId, stopwatch.ElapsedMilliseconds);
            return false;
        }

        // REPLACE strategy (per architecture §6.1) — wholesale swap of the manifest with the
        // orchestrator's complete enrichment snapshot.
        session.UploadedFiles = MapToStored(enrichedFiles);
        session.LastActivity = DateTimeOffset.UtcNow;

        // Write-through (D-06): Redis hot tier first, then Cosmos warm tier (fire-and-forget).
        await WriteToRedisAsync(tenantId, sessionId, session, cancellationToken);
        _ = UpsertToCosmosAsync(session, CancellationToken.None);

        stopwatch.Stop();

        // ADR-015 Tier-1 logging: sessionId + fileCount + durationMs ONLY.
        // NEVER log per-file SummaryText / ClassifiedDocType / Sections content / FileName.
        _logger.LogInformation(
            "SessionPersistenceService.UpdateUploadedFilesAsync: persisted manifest for session {SessionId} (tenant={TenantId}, fileCount={FileCount}, durationMs={DurationMs})",
            sessionId, tenantId, enrichedFiles.Count, stopwatch.ElapsedMilliseconds);

        // chat-routing-redesign-r1 task 074 — emit context.upload_persisted (manifest write-through done).
        // ADR-015 Tier 1 SAFE: durationMs + IDs only. The fileId field is the MOST RECENT enriched
        // file (or empty if the manifest is empty — should not happen in practice but defensive).
        // The per-file emission contract is "one event per pipeline" — bulk persists carry the last
        // file as the representative anchor. Future per-file granular events can be added by
        // emitting inside the orchestrator's per-file enrichment loop.
        var sessionGuid = Guid.TryParse(sessionId, out var parsedSessionGuid) ? parsedSessionGuid : (Guid?)null;
        var representativeFileId = enrichedFiles.Count > 0 ? enrichedFiles[enrichedFiles.Count - 1].FileId : string.Empty;
        _contextEventEmitter.UploadPersisted(
            sessionId: sessionGuid,
            fileId: representativeFileId,
            durationMs: stopwatch.ElapsedMilliseconds,
            tenantId: tenantId);

        return true;
    }

    // =========================================================================
    // Private helpers — ChatSessionFile <-> StoredUploadedFile bridge (task 072)
    // =========================================================================
    //
    // ChatSessionFile (Models.Ai.Chat) uses PascalCase + no JsonPropertyName attributes.
    // StoredUploadedFile (Services.Ai.Sessions) uses camelCase via [JsonPropertyName].
    // These mappers bridge the two shapes. Kept private + focused: no generic mapper because
    // the property surface is small + stable + auditable.

    internal static List<StoredUploadedFile> MapToStored(IReadOnlyList<ChatSessionFile> files)
    {
        var result = new List<StoredUploadedFile>(files.Count);
        foreach (var file in files)
        {
            result.Add(new StoredUploadedFile
            {
                FileId = file.FileId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                SearchDocumentIdsCsv = file.SearchDocumentIdsCsv,
                UploadedAt = file.UploadedAt,
                SummaryText = file.SummaryText,
                ClassifiedDocType = file.ClassifiedDocType,
                ClassifiedConfidence = file.ClassifiedConfidence,
                Sections = file.Sections
                    .Select(s => new StoredSectionInfo
                    {
                        Name = s.Name,
                        StartCharOffset = s.StartCharOffset,
                        EndCharOffset = s.EndCharOffset,
                        StartPage = s.StartPage,
                        EndPage = s.EndPage
                    })
                    .ToList(),
                TableMetadata = file.TableMetadata
                    .Select(t => new StoredTableInfo
                    {
                        Name = t.Name,
                        StartCharOffset = t.StartCharOffset,
                        Page = t.Page
                    })
                    .ToList(),
                Citations = file.Citations
                    .Select(c => new StoredCitationReference
                    {
                        SourceId = c.SourceId,
                        Quote = c.Quote,
                        Page = c.Page
                    })
                    .ToList(),
                PageCount = file.PageCount,
                Language = file.Language
            });
        }
        return result;
    }

    internal static List<ChatSessionFile> MapFromStored(IReadOnlyList<StoredUploadedFile> stored)
    {
        var result = new List<ChatSessionFile>(stored.Count);
        foreach (var s in stored)
        {
            result.Add(new ChatSessionFile(
                FileId: s.FileId,
                FileName: s.FileName,
                ContentType: s.ContentType,
                SizeBytes: s.SizeBytes,
                SearchDocumentIdsCsv: s.SearchDocumentIdsCsv,
                UploadedAt: s.UploadedAt)
            {
                SummaryText = s.SummaryText,
                ClassifiedDocType = s.ClassifiedDocType,
                ClassifiedConfidence = s.ClassifiedConfidence,
                Sections = s.Sections
                    .Select(x => new SectionInfo(
                        Name: x.Name,
                        StartCharOffset: x.StartCharOffset,
                        EndCharOffset: x.EndCharOffset,
                        StartPage: x.StartPage,
                        EndPage: x.EndPage))
                    .ToList(),
                TableMetadata = s.TableMetadata
                    .Select(x => new TableInfo(
                        Name: x.Name,
                        StartCharOffset: x.StartCharOffset,
                        Page: x.Page))
                    .ToList(),
                Citations = s.Citations
                    .Select(x => new CitationReference(
                        SourceId: x.SourceId,
                        Quote: x.Quote,
                        Page: x.Page))
                    .ToList(),
                PageCount = s.PageCount,
                Language = s.Language
            });
        }
        return result;
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
