using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages the lifecycle of chat sessions: create, retrieve, and delete.
///
/// Storage Strategy (ADR-009, ADR-014, NFR-07, D-06):
///   - Hot path: Redis via <see cref="ITenantCache"/> with 24-hour sliding TTL.
///   - Warm path: Cosmos DB via <see cref="ISessionPersistenceService"/> — write-through on every
///     Redis write; consulted on Redis miss before falling back to Dataverse (decision D-06).
///   - Cold path: Dataverse <c>sprk_aichatsummary</c> for persistent storage and audit.
///   - On cache miss: checks Cosmos DB first (fast warm path), then reconstructs from Dataverse.
///
/// Write-through pattern (D-06): every Redis write is immediately followed by a Cosmos DB upsert
/// executed as a fire-and-forget background task. Cosmos failures are logged at Warning and never
/// propagate to the caller — the Redis/response path is never blocked.
///
/// Cache Key Pattern (spaarke-redis-cache-remediation-r1 FR-05): the wrapper produces
/// <c>tenant:{tenantId}:session:{sessionId}:v1</c> (resource = <c>"session"</c>). Combined with the
/// configured InstanceName (<c>spaarke:</c>), the on-wire key matches the FR-14 smoke-test format
/// <c>spaarke:tenant:{tenantId}:session:{sessionId}:v1</c>.
///
/// Lifetime: Scoped — one instance per HTTP request. <see cref="ITenantCache"/> and
/// <see cref="IChatDataverseRepository"/> are injected and handle their own thread-safety.
/// </summary>
public class ChatSessionManager
{
    /// <summary>Sliding TTL for the Redis hot cache (NFR-07: 24-hour idle expiry).</summary>
    internal static readonly TimeSpan SessionCacheTtl = TimeSpan.FromHours(24);

    /// <summary>Tenant-cache resource name for session payloads (FR-05 / FR-14 smoke test).</summary>
    internal const string CacheResource = "session";

    /// <summary>Tenant-cache schema version for session payloads.</summary>
    internal const int CacheVersion = 1;

    private readonly ITenantCache _cache;
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

    /// <summary>
    /// spaarkeai-compose-r8 FR-B06 (task 063) — the durable byte store whose copies
    /// <see cref="DeleteSessionAsync"/> must erase. Optional for the same reason
    /// <see cref="_persistence"/> is: this type is constructed directly by a number of tests and by
    /// the <c>AnalysisServicesModule</c> factory, and a null store degrades to
    /// <see cref="SessionFileErasureState.StoreDisabled"/> — the same outcome an unconfigured
    /// deployment produces — rather than to a crash. In the composed host it is always present:
    /// <c>AiPersistenceModule</c> registers <see cref="SessionFileBlobStore"/> UNCONDITIONALLY
    /// (ADR-032 P1), so the DI graph is symmetric in every configuration.
    /// </summary>
    private readonly SessionFileBlobStore? _durableFileStore;

    // FR-05 / FR-14: key construction is now centralised in ITenantCache. Producing
    // (tenantId, "session", sessionId, v1) yields the on-wire key
    // "spaarke:tenant:{tenantId}:session:{sessionId}:v1" once the StackExchangeRedisCache
    // instance-name prefix is prepended.

    /// <summary>
    /// Reproduces the cache key form used by <see cref="ITenantCache"/> so external
    /// pub-sub / SCAN consumers (e.g., <see cref="SessionFilesCleanupJob"/>) that go
    /// around <c>IDistributedCache</c> via raw <c>IConnectionMultiplexer.GetDatabase()</c>
    /// can probe for live session keys directly. Format:
    /// <c>tenant:{tenantId}:session:{sessionId}:v1</c> — the StackExchangeRedisCache
    /// <c>InstanceName</c> is prepended on-wire (e.g., <c>spaarke:</c>).
    /// </summary>
    internal static string BuildCacheKey(string tenantId, string sessionId)
        => $"tenant:{tenantId}:{CacheResource}:{sessionId}:v{CacheVersion}";

    /// <summary>
    /// The pre-FR-B06 constructor, preserved verbatim. Equivalent to the full constructor below with
    /// <c>durableFileStore: null</c>, i.e. a manager whose durable erasure reports
    /// <see cref="SessionFileErasureState.StoreDisabled"/>.
    /// </summary>
    /// <remarks>
    /// Kept as a real overload rather than folded into a trailing optional parameter because
    /// <b>Castle DynamicProxy and <c>Activator.CreateInstance</c> bind constructors by exact arity and
    /// do NOT apply optional-parameter defaults</b>. 59 existing construction sites across 51 test
    /// files (many of them <c>new Mock&lt;ChatSessionManager&gt;(…five arguments…)</c>) would fail at
    /// runtime with "Constructor on type 'Castle.Proxies.ChatSessionManagerProxy' not found" — observed,
    /// 123 failures, before this overload was added. Every one of those call sites is a mock of a
    /// collaborator that has nothing to do with session-file erasure, so churning them would be pure
    /// blast radius on files several concurrent Compose tasks are editing.
    /// </remarks>
    public ChatSessionManager(
        ITenantCache cache,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatSessionManager> logger,
        ISessionPersistenceService? persistence = null,
        ISessionFilesCleanupSignal? cleanupSignal = null)
        : this(cache, dataverseRepository, logger, persistence, cleanupSignal, durableFileStore: null)
    {
    }

    /// <summary>
    /// The full constructor. <paramref name="durableFileStore"/> is what
    /// <see cref="DeleteSessionAsync"/> erases (spaarkeai-compose-r8 FR-B06); the composed host always
    /// supplies it, because <c>AiPersistenceModule</c> registers the store unconditionally.
    /// </summary>
    public ChatSessionManager(
        ITenantCache cache,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatSessionManager> logger,
        ISessionPersistenceService? persistence,
        ISessionFilesCleanupSignal? cleanupSignal,
        SessionFileBlobStore? durableFileStore)
    {
        _cache = cache;
        _dataverseRepository = dataverseRepository;
        _logger = logger;
        _persistence = persistence;
        _cleanupSignal = cleanupSignal;
        _durableFileStore = durableFileStore;
    }

    /// <summary>
    /// Creates a new chat session, persists the session record to Dataverse, and caches
    /// the empty session in Redis.
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation).</param>
    /// <param name="ownerOid">
    /// Entra <c>oid</c> of the user the session belongs to (issue #863). <b>Required, and
    /// deliberately positional rather than an optional trailing parameter</b> — an owner that a
    /// call site can forget to pass is the same defect shape as the 21 tenant-resolution sites
    /// task 059 removed. Resolve it with
    /// <see cref="Infrastructure.Authentication.CallerResolution.ResolveObjectId"/> and answer
    /// <c>401</c> when it is null; never substitute a placeholder, and never mint an unowned
    /// session, because unowned sessions fail closed for everyone including their creator.
    /// </param>
    /// <param name="documentId">SPE document ID for the session context (may be null).</param>
    /// <param name="playbookId">Playbook that governs the agent's system prompt and tools.</param>
    /// <param name="hostContext">Optional host context describing where SprkChat is embedded.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created <see cref="ChatSession"/>.</returns>
    public async Task<ChatSession> CreateSessionAsync(
        string tenantId,
        string ownerOid,
        string? documentId,
        Guid? playbookId = null,
        ChatHostContext? hostContext = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerOid);

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
            HostContext: hostContext)
        {
            OwnerOid = ownerOid
        };

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
        // Hot path: Redis cache (ADR-009 — Redis first). Tenant-scoped key produced by
        // ITenantCache: tenant:{tenantId}:session:{sessionId}:v1 (spaarke-redis-cache-remediation-r1 FR-05).
        var cached = await _cache.GetAsync<ChatSession>(tenantId, CacheResource, sessionId, CacheVersion, ct: ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache HIT for session {SessionId} (tenant={TenantId})", sessionId, tenantId);
            // Refresh sliding TTL on access
            await _cache.RefreshAsync(tenantId, CacheResource, sessionId, CacheVersion, ct: ct);
            return cached;
        }

        // Warm path: Cosmos DB fallback (decision D-06 — checked before Dataverse on Redis miss).
        // AIPL-054 hardening (task 025 / spec FR-09): a stale/expired Redis session MUST resolve
        // against the durable Cosmos transcript (ADR-040 Path A store-of-record) rather than
        // silently falling through to an empty/partial result. The Cosmos read is wrapped so a
        // transient Cosmos failure (throttling, timeout, transient network error) does NOT abort
        // the whole lookup — it degrades to the Dataverse cold path instead of surfacing a 500 or
        // (worse) letting a caller interpret the exception as "session not found" and mint a new
        // empty session on top of a durable transcript that still exists.
        if (_persistence is not null)
        {
            _logger.LogDebug(
                "Cache MISS for session {SessionId} — checking Cosmos DB before Dataverse (tenant={TenantId})",
                sessionId, tenantId);

            StoredSession? storedSession = null;
            try
            {
                storedSession = await _persistence.LoadSessionAsync(tenantId, sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Cosmos DB read failed for session {SessionId} (tenant={TenantId}) — " +
                    "falling back to Dataverse rather than losing the recovery attempt (task 025 hardening)",
                    sessionId, tenantId);
            }

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
    /// Deletes a chat session: erases every copy of its uploaded files' BYTES, then removes the
    /// session from Redis and marks it as archived in Dataverse.
    ///
    /// Does not delete Dataverse records — the <c>sprk_aichatsummary</c> and associated
    /// <c>sprk_aichatmessage</c> records are retained as an audit trail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>spaarkeai-compose-r8 FR-B06 (task 063) — erasure ordering, and why it is this way round.</b>
    /// The durable bytes are erased FIRST, and if they cannot be confirmed gone the session RECORD is
    /// left completely intact and this returns
    /// <see cref="SessionFileErasureState.Incomplete"/>. Two reasons:
    /// </para>
    /// <list type="number">
    ///   <item>A session that is still listed in History is a VISIBLE, retryable state. A session that
    ///   vanished while its documents stayed in blob storage is an invisible compliance failure that
    ///   looks exactly like a completed deletion — nothing in the product would ever surface it.</item>
    ///   <item>Failing closed is cheap here precisely because the erasure does not need the record:
    ///   <see cref="SessionFileEraser"/> enumerates the blob PREFIX, so the natural retry finds and
    ///   removes the residue whether or not the manifest still exists.</item>
    /// </list>
    /// <para>
    /// The erasure locations, in the order this method visits them, are enumerated in
    /// <c>projects/spaarkeai-compose-r8/notes/track-b-erasure-surface.md</c>: durable blob (must
    /// succeed), the four 4-hour <c>doc-upload-*</c> Redis copies (best-effort, TTL-bounded), the
    /// session's own Redis key and Cosmos document (which carry the manifest and each file's extracted
    /// text), and the AI-Search hot chunks (asynchronous, via the existing cleanup signal). Dataverse
    /// transcript records are deliberately RETAINED — Tier-2 audit trail, ADR-015 independent
    /// governance tiers, exactly as <c>memory-items</c> erasure never touches the audit container.
    /// </para>
    /// </remarks>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The durable-byte erasure outcome. <see cref="SessionFileErasureState.Incomplete"/> means bytes
    /// may remain AND the session record was not deleted — callers MUST NOT report success.
    /// </returns>
    public async Task<SessionFileErasureResult> DeleteSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Deleting chat session {SessionId} (tenant={TenantId})",
            sessionId, tenantId);

        // 0. FR-B06 — read the manifest BEFORE anything is removed. Files uploaded while the durable
        //    store was disabled (i.e. every deployment until the operator arms it) have no blob to
        //    enumerate, so the manifest is the only place their fileIds exist. Best-effort: a session
        //    that cannot be loaded still gets its durable prefix erased below, because that path needs
        //    no manifest at all.
        var manifestFileIds = await TryReadManifestFileIdsAsync(tenantId, sessionId, ct);

        // 1. FR-B06 — the durable byte copies. This is the only step that can ABORT the delete: an
        //    erasure that quietly skipped bytes is indistinguishable from a completed one, so
        //    "Incomplete" stops here with the record intact rather than reporting a success it cannot
        //    substantiate.
        var erasure = await SessionFileEraser.EraseSessionFilesAsync(
            _durableFileStore, tenantId, sessionId, _logger, ct);

        if (erasure.State == SessionFileErasureState.Incomplete)
        {
            _logger.LogError(
                "Session {SessionId} (tenant={TenantId}) was NOT deleted: its durable file bytes could " +
                "not be confirmed erased (reason={Reason}, remaining={Remaining}, failures={Failures}). " +
                "The session record is intact so the deletion can be retried and will complete.",
                sessionId, tenantId, erasure.Reason, erasure.BlobsRemaining, erasure.Failures);

            return erasure;
        }

        // 2. FR-B06 — the same bytes' 4-hour hot copies (doc-upload-text/binary/meta/persist). Both id
        //    sources are used: the blob prefix names files with a durable copy, the manifest names
        //    files uploaded while the store was disabled. Best-effort by design — see
        //    SessionFileEraser.EvictUploadCachesAsync.
        await SessionFileEraser.EvictUploadCachesAsync(
            _cache, tenantId, sessionId, manifestFileIds.Concat(erasure.FileIds), _logger, ct);

        // Remove from Redis hot cache (tenant-scoped key per FR-05).
        await _cache.RemoveAsync(tenantId, CacheResource, sessionId, CacheVersion, ct: ct);

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

        return erasure;
    }

    /// <summary>
    /// Best-effort read of the session's uploaded-file ids, used only to evict those files' 4-hour
    /// <c>doc-upload-*</c> cache entries (FR-B06). Never throws: a session that cannot be loaded must
    /// not block its own deletion, and the durable erasure — the compliance-bearing half — does not
    /// depend on this at all.
    /// </summary>
    private async Task<IReadOnlyList<string>> TryReadManifestFileIdsAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var session = await GetSessionAsync(tenantId, sessionId, ct);
            var files = session?.UploadedFiles;

            if (files is null || files.Count == 0)
            {
                return Array.Empty<string>();
            }

            return files.Select(f => f.FileId).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not read the uploaded-file manifest for session {SessionId} (tenant={TenantId}) " +
                "before deletion. The durable byte erasure is unaffected (it enumerates by blob prefix); " +
                "only the 4-hour doc-upload cache entries of files with no durable copy may be left to " +
                "expire on their own TTL.",
                sessionId, tenantId);

            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Updates the cached session to reflect the latest message state, then writes through to
    /// Cosmos DB (decision D-06). Called by <see cref="ChatHistoryManager"/> after adding a message.
    /// </summary>
    /// <param name="session">The updated session to cache.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="awaitCosmosWrite">
    /// FR-D2 (task 030): when <c>true</c>, the Cosmos write-through is CONFIRMED — this method does
    /// not return until the Cosmos upsert completes — instead of the default fire-and-forget (D-06).
    /// <see cref="ChatHistoryManager.AddMessageAsync"/> sets this for the FIRST message of a session
    /// (<c>messages[0]</c>) only, so the transcript + title seed (FR-D4) survive a Redis eviction.
    /// Every other caller (and every later turn) keeps the default <c>false</c> — no latency
    /// regression on turns 2+ (NFR-03).
    /// </param>
    internal virtual async Task UpdateSessionCacheAsync(
        ChatSession session,
        CancellationToken ct = default,
        bool awaitCosmosWrite = false)
    {
        // 1. Refresh Redis hot cache
        await CacheSessionAsync(session, ct);

        // 2. Write-through to Cosmos DB (warm path, D-06).
        if (awaitCosmosWrite)
        {
            // FR-D2: confirmed write for messages[0] — awaited so the caller (and, transitively,
            // the HTTP request handler) does not complete until the durable warm-tier copy exists.
            await AwaitedCosmosPersistAsync(session);
        }
        else
        {
            // Fire-and-forget (D-06 default). Cosmos failure must not block the message add path
            // or affect the streaming response.
            FireAndForgetCosmosPersist(session);
        }
    }

    /// <summary>
    /// Appends one <see cref="SessionToolChain"/> ledger entry to the session and
    /// persists through the existing 3-tier pipeline (FR-P2-01 step 5 / ADR-040:
    /// the text-turn tool chain is ledger-written BEFORE the turn's output renders).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session is RE-FETCHED before appending so concurrent ledger writes made
    /// during the same turn (e.g. capability dispatch appending an Output entry via
    /// <see cref="Sprk.Bff.Api.Services.Ai.OutputRouter"/>) are not clobbered by a
    /// stale snapshot held by the caller. Append-only: existing entries are never
    /// mutated (ADR-040).
    /// </para>
    /// <para>
    /// <b>NFR-07</b>: the entry carries identifiers/filters/counts only (enforced at
    /// construction by <see cref="AgentTurnContract"/>); this method logs counts +
    /// identifiers only. Ledger entries are Tier 3 — erased with the session.
    /// </para>
    /// </remarks>
    /// <returns>The updated session carrying the appended entry (null when the session no longer exists).</returns>
    public virtual async Task<ChatSession?> AppendToolChainAsync(
        string tenantId,
        string sessionId,
        SessionToolChain entry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entry);

        var session = await GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "AppendToolChainAsync: session not found — tenant={TenantId} session={SessionId}; ToolChain entry dropped.",
                tenantId, sessionId);
            return null;
        }

        var appended = new List<SessionToolChain>(session.ToolChains ?? Array.Empty<SessionToolChain>()) { entry };
        var updated = session with { ToolChains = appended };
        await UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ledger ToolChain stored: turn={Turn} calls={CallCount} tenant={TenantId} session={SessionId}",
            entry.Turn, entry.Calls.Count, tenantId, sessionId);

        return updated;
    }

    /// <summary>
    /// Appends one <see cref="SessionGate"/> ledger entry to the session and persists
    /// through the existing 3-tier pipeline (FR-P2-02 / ADR-040: gate markers are
    /// ledger-written BEFORE any gate UI renders; resolutions are NEW entries with the
    /// same <see cref="SessionGate.GateId"/> — append-only).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="AppendToolChainAsync"/>: the session is RE-FETCHED before
    /// appending so concurrent ledger writes in the same turn are not clobbered by a
    /// stale caller snapshot. Written exclusively via <see cref="PendingPlanManager"/> —
    /// the ONE confirmation gate (D12) — so every gate transition flows through one
    /// append implementation.
    /// </para>
    /// <para>
    /// Turn allocation: when <paramref name="entry"/>.Turn is not positive, the next
    /// per-session gate ordinal (<c>max(existing Gates[].Turn, 0) + 1</c>) is allocated —
    /// the same monotonic-ordinal scheme <see cref="Sprk.Bff.Api.Services.Ai.OutputRouter"/>
    /// documents for Output entries. Resolution entries SHOULD pass the pending entry's
    /// turn so the pending/resolved pair correlates.
    /// </para>
    /// <para><b>NFR-07</b>: gate entries and these logs carry identifiers only.</para>
    /// </remarks>
    /// <returns>
    /// The updated session and the stored entry (with allocated turn), or null when the
    /// session no longer exists (entry dropped, warning logged).
    /// </returns>
    public virtual async Task<(ChatSession Session, SessionGate Entry)?> AppendGateAsync(
        string tenantId,
        string sessionId,
        SessionGate entry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entry);

        var session = await GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "AppendGateAsync: session not found — tenant={TenantId} session={SessionId}; Gate entry dropped.",
                tenantId, sessionId);
            return null;
        }

        var stored = entry.Turn > 0
            ? entry
            : entry with { Turn = (session.Gates is { Count: > 0 } ? session.Gates.Max(g => g.Turn) : 0) + 1 };

        var appended = new List<SessionGate>(session.Gates ?? Array.Empty<SessionGate>()) { stored };
        var updated = session with { Gates = appended };
        await UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ledger Gate stored: gateId={GateId} kind={Kind} status={Status} turn={Turn} " +
            "sideEffectClass={SideEffectClass} bindingId={BindingId} tenant={TenantId} session={SessionId}",
            stored.GateId, stored.Kind, stored.Status, stored.Turn,
            stored.SideEffectClass, stored.BindingId, tenantId, sessionId);

        return (updated, stored);
    }

    /// <summary>
    /// Appends one <see cref="SessionContextFingerprint"/> ledger entry (the context-selection
    /// leg of the decision-traceability trace — AIR2-038 / FR-A1-09) and persists through the
    /// existing 3-tier pipeline (ADR-040: store before render; append-only).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="AppendToolChainAsync"/>: the session is RE-FETCHED before appending
    /// so concurrent ledger writes in the same turn are not clobbered. This is the write seam
    /// the Context Binder (FR-B-04 / task 053) calls once per-turn ContextEnvelope assembly
    /// lands; the server trace read surface (<see cref="Services.Ai.PublicContracts.ISessionTraceReader"/>)
    /// projects these entries into <see cref="Services.Ai.PublicContracts.TraceEventKind.Context"/> events.
    /// </para>
    /// <para><b>NFR-07</b>: the entry and this log carry the fingerprint id + slice COUNT only — never content.</para>
    /// </remarks>
    /// <returns>The updated session carrying the appended entry, or null when the session no longer exists.</returns>
    public virtual async Task<ChatSession?> AppendContextFingerprintAsync(
        string tenantId,
        string sessionId,
        SessionContextFingerprint entry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(entry);

        var session = await GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "AppendContextFingerprintAsync: session not found — tenant={TenantId} session={SessionId}; ContextFingerprint entry dropped.",
                tenantId, sessionId);
            return null;
        }

        var appended = new List<SessionContextFingerprint>(
            session.ContextFingerprints ?? Array.Empty<SessionContextFingerprint>()) { entry };
        var updated = session with { ContextFingerprints = appended };
        await UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ledger ContextFingerprint stored: turn={Turn} fingerprintId={FingerprintId} sliceCount={SliceCount} tenant={TenantId} session={SessionId}",
            entry.Turn, entry.FingerprintId, entry.SliceCount, tenantId, sessionId);

        return updated;
    }

    /// <summary>
    /// HostContext.EntityType sentinel identifying an Analysis-owned chat session (task 020's
    /// existing "sprk_analysisoutput" convention — see <c>ChatDataverseRepository.AnalysisHostContextEntityType</c>
    /// and <c>AnalysisEndpoints.AnalysisHostContextEntityType</c>). MUST match both.
    /// </summary>
    internal const string AnalysisHostContextEntityType = "sprk_analysisoutput";

    /// <summary>
    /// Explicit promotion (task 023, spec FR-07): binds an EXISTING loose session to a NEW
    /// <c>sprk_analysis</c> — a bind, NOT a mint. Distinct from
    /// <c>AnalysisEndpoints.ForkAnalysis</c> (task 021), which mints a brand-new session bound to a
    /// brand-new Analysis and archives the prior one. Promotion instead:
    /// <list type="number">
    ///   <item>Re-fetches the session (concurrent-write safety, mirrors <see cref="AppendToolChainAsync"/>).</item>
    ///   <item>Writes the <c>sprk_analysis</c> FK onto the session's EXISTING <c>sprk_aichatsummary</c>
    ///     row (<see cref="IChatDataverseRepository.BindSessionToAnalysisAsync"/> — no new Dataverse row).</item>
    ///   <item>Updates the session's <see cref="ChatHostContext"/> in place to the Analysis-owned
    ///     convention (<c>EntityType="sprk_analysisoutput"</c>, <c>EntityId=analysisId</c>) and writes
    ///     through Redis + Cosmos via <see cref="UpdateSessionCacheAsync"/> — the SAME session id
    ///     persists; no second session is created (ADR-040: no second session-state store).</item>
    /// </list>
    /// A session already Analysis-owned (HostContext.EntityType already the Analysis convention) MUST
    /// be rejected by the caller (<c>AnalysisEndpoints.PromoteSession</c>) BEFORE this method runs —
    /// promotion is a ONE-TIME bind, not an idempotent re-bind to a different Analysis.
    /// </summary>
    /// <param name="tenantId">Tenant ID (multi-tenant isolation).</param>
    /// <param name="sessionId">The loose session's ID to promote.</param>
    /// <param name="analysisId">The newly-created <c>sprk_analysis</c> record ID to bind to.</param>
    /// <param name="analysisName">Display name of the Analysis — stored as the HostContext's EntityName.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated (now Analysis-owned) session, or <c>null</c> when the session no longer exists.</returns>
    public virtual async Task<ChatSession?> PromoteSessionToAnalysisAsync(
        string tenantId,
        string sessionId,
        Guid analysisId,
        string analysisName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisName);

        var session = await GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "PromoteSessionToAnalysisAsync: session not found — tenant={TenantId} session={SessionId}; promotion aborted.",
                tenantId, sessionId);
            return null;
        }

        // Durable FK bind on the EXISTING sprk_aichatsummary row (no new row, no new session).
        // UNLIKE CreateSessionAsync's tolerant Dataverse-write posture, a bind failure here MUST
        // propagate: the durable FK is promotion's entire deliverable (it is what makes the Analysis
        // queryable via GetSessionsByAnalysisAsync / visible in the Analysis hub grid) — silently
        // continuing would leave the caller believing promotion succeeded while the Analysis is
        // orphaned with no bound session. The caller (AnalysisEndpoints.PromoteSession) catches this
        // and compensates by deleting the just-created Analysis (same no-orphan discipline as
        // ForkAnalysis's mint-failure compensation) — no partial-promotion state is left behind.
        await _dataverseRepository.BindSessionToAnalysisAsync(tenantId, sessionId, analysisId, ct);

        var analysisHostContext = new ChatHostContext(
            EntityType: AnalysisHostContextEntityType,
            EntityId: analysisId.ToString(),
            EntityName: analysisName,
            WorkspaceType: session.HostContext?.WorkspaceType,
            PageType: session.HostContext?.PageType);

        var updated = session with { HostContext = analysisHostContext };
        await UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Session {SessionId} promoted to analysis {AnalysisId} (tenant={TenantId}) — loose session bound in place (no new session minted).",
            sessionId, analysisId, tenantId);

        return updated;
    }

    // === Private helpers ===

    /// <summary>
    /// Serialises the session to JSON and stores it in Redis with the configured sliding TTL.
    /// </summary>
    private async Task CacheSessionAsync(ChatSession session, CancellationToken ct)
    {
        // Sliding expiry: resets on every RefreshAsync/GetAsync access (ADR-009, NFR-07).
        // Tenant-scoped key produced by ITenantCache per FR-05.
        await _cache.SetSlidingAsync(
            session.TenantId,
            CacheResource,
            session.SessionId,
            CacheVersion,
            session,
            SessionCacheTtl,
            ct: ct);
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
    /// Confirmed (awaited) Cosmos DB upsert for <paramref name="session"/> — the FR-D2 (task 030)
    /// counterpart to <see cref="FireAndForgetCosmosPersist"/>. Reserved for the FIRST message of a
    /// session (<c>messages[0]</c>) so the transcript + title seed (FR-D4) are durable in Cosmos
    /// before the caller (ultimately the SendMessage request handler) returns, closing the gap where
    /// a Redis eviction between response-completion and the fire-and-forget write finishing would
    /// otherwise strand a brand-new session with a blank transcript on reopen.
    ///
    /// Still routes the underlying Cosmos call through <see cref="CancellationToken.None"/> —
    /// mirroring <see cref="FireAndForgetCosmosPersist"/>: a client mid-request disconnect must not
    /// abort a write whose entire purpose is to outlive the request. Only the AWAIT boundary differs
    /// between the two methods; the write itself is identical (same mapper, same non-throwing
    /// failure policy inside <see cref="SessionPersistenceService"/>).
    /// </summary>
    private async Task AwaitedCosmosPersistAsync(ChatSession session)
    {
        if (_persistence is null)
        {
            return;
        }

        var stored = MapChatSessionToStoredSession(session);
        await _persistence.PersistSessionAsync(stored, CancellationToken.None, awaitCosmosWrite: true);
    }

    /// <summary>
    /// Maps a <see cref="ChatSession"/> (hot Redis model) to a <see cref="StoredSession"/>
    /// (Cosmos DB warm document). Preserves all message content and metadata, document
    /// references (DocumentId, AdditionalDocumentIds, UploadedFiles manifest), and the
    /// typed session-ledger collections (Outputs, ToolChains, WidgetEvents, Gates).
    ///
    /// ADR-040 / FR-P0-01: prior to the ledger work this mapper dropped DocumentId, the
    /// pinned AdditionalDocumentIds, and the UploadedFiles manifest — every write-through
    /// clobbered the Cosmos document's file references (the audited P2 violation).
    /// Document references now round-trip through the warm store in both directions.
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

            // Issue #863 — ownership must survive the warm tier. If this mapping is dropped, a
            // session that falls out of Redis reloads from Cosmos with OwnerOid == null and then
            // fails closed for its own owner. Its counterpart is in MapStoredSessionToChatSession;
            // the pair is asserted by SessionOwnershipTests.
            OwnerOid = session.OwnerOid,

            PlaybookId = session.PlaybookId,
            Messages = messages,
            WidgetStates = [],
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity,

            // FR-D4 (task 032) — stored session title round-trips through the warm tier.
            Title = session.Title,

            // FR-D10 (task 033) — persist the host context through the warm tier so filed-state SURVIVES
            // a Redis eviction + Cosmos reload. This is what makes the ttl re-assertion below correct on
            // every turn: without it, a reopened filed session would restore with a null HostContext and
            // the next turn would re-derive ttl = null, silently reverting the doc to the 90-day default.
            // Same "must survive warm-store restore" class as the document references above (ADR-040).
            HostContext = session.HostContext,

            // FR-D10 (task 033) — per-item Cosmos retention override, DERIVED from live filed-state so
            // every write-through re-asserts it (a later turn's upsert must never reset a filed doc to
            // the 90-day container default — filed-state now round-trips via HostContext above, so this
            // holds even across a warm-tier reload). A session is "filed" once promoted to an Analysis —
            // its HostContext.EntityType becomes AnalysisHostContextEntityType ("sprk_analysisoutput",
            // set by PromoteSessionToAnalysisAsync). Filed → ttl = -1 (never expire, FR-D10); unfiled →
            // null (rides the 90-day default, unchanged). No cleanup job, no container change — the spike
            // (notes/d10-ttl-spike.md) confirmed the container DefaultTimeToLive is non-null so per-item
            // overrides take effect.
            Ttl = string.Equals(session.HostContext?.EntityType, AnalysisHostContextEntityType, StringComparison.Ordinal)
                ? StoredSession.NeverExpireTtl
                : null,

            // Document references (ADR-040 fix — file refs survive the warm store)
            DocumentId = session.DocumentId,
            AdditionalDocumentIds = session.AdditionalDocumentIds?.ToList() ?? [],
            UploadedFiles = session.UploadedFiles is { Count: > 0 }
                ? SessionPersistenceService.MapToStored(session.UploadedFiles)
                : [],

            // Session ledger (ADR-040 / FR-P0-01 — persisted DARK at P0, zero readers)
            Outputs = SessionPersistenceService.MapOutputsToStored(session.Outputs),
            ToolChains = SessionPersistenceService.MapToolChainsToStored(session.ToolChains),
            WidgetEvents = SessionPersistenceService.MapWidgetEventsToStored(session.WidgetEvents),
            Gates = SessionPersistenceService.MapGatesToStored(session.Gates),
            ContextFingerprints = SessionPersistenceService.MapContextFingerprintsToStored(session.ContextFingerprints),

            // R2 Compose-domain annotations (FR-29 / task 102 gap 4.5 / DEF-05): carry the two
            // MUTABLE Compose collections through the warm tier so they survive a Redis eviction /
            // cold restore. Null (no annotations yet) maps to an empty list. Domain records stored
            // directly (see StoredSession remarks). Action history is NOT stored — it projects over
            // Outputs (above), which already round-trip.
            AnchoredAnnotations = session.AnchoredAnnotations?.ToList() ?? [],
            DefinedTermsTracking = session.DefinedTermsTracking?.ToList() ?? [],

            // R2 session-scoped active-document pointer (task 113): carry it through the warm tier
            // so a Redis eviction / cold restore does not silently drop the active document (the
            // ADR-040 document-reference-survival MUST — the same P2 class this mapper documents).
            ActiveDocument = session.ActiveDocument,

            // R4.5 WS-4 paraId -> legal-number reference map (task 041, spec FR-17): carry it
            // through the warm tier for the same reason as ActiveDocument above — a Redis
            // eviction / cold restore must not silently drop it. Null (no map yet) maps to an
            // empty list, mirroring AnchoredAnnotations/DefinedTermsTracking.
            ReferenceMap = session.ReferenceMap?.ToList() ?? []
        };
    }

    /// <summary>
    /// Maps a <see cref="StoredSession"/> (Cosmos warm document) back to a <see cref="ChatSession"/>
    /// (hot Redis model) for Cosmos-fallback scenarios.
    ///
    /// Message content, document references (DocumentId, AdditionalDocumentIds, the
    /// UploadedFiles manifest — ADR-040: document references MUST survive warm-store
    /// restore), and the typed session-ledger collections all round-trip faithfully.
    /// Fields present only on <see cref="StoredSession"/> (widget states, entity refs,
    /// summary, tabs) have no equivalent on <see cref="ChatSession"/> and are not mapped —
    /// they remain in Cosmos and are accessible via <see cref="ISessionPersistenceService"/>.
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
            // ADR-040 fix: document references now persist to (and restore from) Cosmos.
            // Older warm documents that pre-date the fields restore as null/empty —
            // Dataverse remains the cold-tier authority for those sessions.
            DocumentId: stored.DocumentId,
            PlaybookId: stored.PlaybookId,
            CreatedAt: stored.CreatedAt,
            LastActivity: stored.LastActivity,
            Messages: messages,
            AdditionalDocumentIds: stored.AdditionalDocumentIds is { Count: > 0 }
                ? stored.AdditionalDocumentIds
                : null,
            UploadedFiles: stored.UploadedFiles is { Count: > 0 }
                ? SessionPersistenceService.MapFromStored(stored.UploadedFiles)
                : null)
        {
            // Issue #863 — restore ownership from the warm tier. Without this the owner of a
            // Redis-evicted session is denied access to their own conversation on reload. Null for
            // Cosmos documents written before #863: those fail closed for everyone, by design.
            OwnerOid = stored.OwnerOid,

            // FR-D4 (task 032) — restore the stored session title from the warm tier.
            Title = stored.Title,

            // FR-D10 (task 033) — restore the host context (filed-state) from the warm tier so a
            // reopened filed session is still recognized as filed. This keeps the ttl re-derivation in
            // MapChatSessionToStoredSession correct across a Redis-eviction + Cosmos-reload: the next
            // turn re-asserts ttl = -1 instead of silently reverting to the 90-day default. Null for
            // knowledge-only sessions or warm documents that pre-date the field (Dataverse remains the
            // cold-tier authority for those). The ChatHostContext normalizer is idempotent on restore.
            HostContext = stored.HostContext,

            // Session ledger (ADR-040 / FR-P0-01 — restored DARK at P0, zero readers).
            // Empty stored lists map to null ("no ledger entries yet").
            Outputs = SessionPersistenceService.MapOutputsFromStored(stored.Outputs),
            ToolChains = SessionPersistenceService.MapToolChainsFromStored(stored.ToolChains),
            WidgetEvents = SessionPersistenceService.MapWidgetEventsFromStored(stored.WidgetEvents),
            Gates = SessionPersistenceService.MapGatesFromStored(stored.Gates),
            ContextFingerprints = SessionPersistenceService.MapContextFingerprintsFromStored(stored.ContextFingerprints),

            // R2 Compose-domain annotations (FR-29 / task 102 gap 4.5 / DEF-05): restore the two
            // MUTABLE Compose collections from the warm tier. Empty stored lists map to null ("none
            // yet") — matching the sibling ledger collections' convention.
            AnchoredAnnotations = stored.AnchoredAnnotations is { Count: > 0 }
                ? stored.AnchoredAnnotations
                : null,
            DefinedTermsTracking = stored.DefinedTermsTracking is { Count: > 0 }
                ? stored.DefinedTermsTracking
                : null,

            // R2 session-scoped active-document pointer (task 113) — restore from the warm tier
            // (null when the Cosmos document pre-dates the field). Matches the sibling collections'
            // convention: absent → null ("no active document yet").
            ActiveDocument = stored.ActiveDocument,

            // R4.5 WS-4 paraId -> legal-number reference map (task 041, spec FR-17) — restore from
            // the warm tier. Empty stored list maps to null ("no reference map yet"), matching the
            // sibling collections' convention.
            ReferenceMap = stored.ReferenceMap is { Count: > 0 }
                ? stored.ReferenceMap
                : null
        };
    }
}
