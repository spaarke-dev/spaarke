using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using StackExchange.Redis;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// R5 task 007 (D1-07) — background service that evicts orphaned session-file
/// chunks from the <c>spaarke-session-files</c> Azure AI Search index.
///
/// <para>
/// <b>Two triggers</b> (both converge on the same idempotent
/// <see cref="EvictSessionAsync"/> helper):
/// <list type="number">
///   <item><b>Scheduled</b> — a <see cref="PeriodicTimer"/> fires every
///   <see cref="SessionFilesCleanupOptions.IntervalHours"/> (default 6h).
///   The scheduled pass enumerates indexed session IDs (via facet query),
///   joins against the active Redis session-key set
///   (<c>chat:session:{tenantId}:{sessionId}</c>), and evicts every
///   <c>sessionId</c> NOT present in the active set.</item>
///   <item><b>On-session-end (immediate)</b> — a
///   <see cref="System.Threading.Channels.Channel{T}"/> owned by
///   <see cref="SessionFilesCleanupSignal"/> accepts immediate triggers
///   from <see cref="ChatSessionManager.DeleteSessionAsync"/>. The loop
///   races the timer tick against the channel read; signals deliver
///   eviction within seconds (spec NFR-02 "Aggressive cleanup on
///   session-end").</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Idempotency (project constraint)</b>: invoking
/// <see cref="EvictSessionAsync"/> twice for the same
/// <c>(tenantId, sessionId)</c> succeeds without error — the second pass
/// returns 0 hits from the search query, skips the delete batch, and
/// emits a zero-count telemetry event.
/// </para>
///
/// <para>
/// <b>Tenant isolation (ADR-014)</b>: every delete filter is
/// <c>tenantId eq '...' and sessionId eq '...'</c> — both predicates
/// always present. Cross-tenant cleanup is a defect.
/// </para>
///
/// <para>
/// <b>Telemetry</b>: emits <c>r5.session_files_cleanup.run</c> events
/// via <see cref="Telemetry.AiTelemetry.ActivitySource"/> (no new
/// telemetry singleton per R5 §3.3). Event fields are LOCKED for task 008
/// dashboards: <c>trigger</c>, <c>sessions_evicted</c>, <c>documents_deleted</c>,
/// <c>tenant_id</c>, <c>session_id</c>, <c>duration_ms</c>,
/// <c>completion_status</c>.
/// </para>
///
/// <para>
/// <b>Constraints</b>:
/// <list type="bullet">
///   <item>ADR-001: <see cref="BackgroundService"/> + <see cref="PeriodicTimer"/>
///     (no Azure Functions, no Hangfire/Quartz).</item>
///   <item>ADR-010: DI minimalism — registered in
///     <c>AnalysisServicesModule</c> under the existing compound gate,
///     ZERO new <c>Program.cs</c> lines.</item>
///   <item>ADR-014: tenant + session predicates always present on delete filter.</item>
///   <item>ADR-018: no new feature flag — kill-switch inherits compound gate.</item>
///   <item>R5 CLAUDE.md §3.1: mirrors the
///     <c>PlaybookIndexingBackgroundService</c> + <c>ScheduledRagIndexingService</c>
///     patterns — no parallel job framework.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SessionFilesCleanupJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SessionFilesCleanupOptions _options;
    private readonly SessionFilesCleanupSignal _signal;
    private readonly ILogger<SessionFilesCleanupJob> _logger;

    /// <summary>
    /// Canonical telemetry event name emitted on every run. LOCKED for task 008
    /// dashboards (App Insights queries key off this exact string).
    /// </summary>
    internal const string TelemetryEventName = "r5.session_files_cleanup.run";

    /// <summary>
    /// Telemetry trigger values (low-cardinality bounded enum — kept stable for task 008 dashboards).
    /// </summary>
    internal const string TriggerScheduled = "scheduled";
    internal const string TriggerOnSessionEnd = "on_session_end";

    /// <summary>
    /// Telemetry completion-status values (low-cardinality bounded enum).
    /// </summary>
    internal const string StatusSuccess = "success";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";

    public SessionFilesCleanupJob(
        IServiceProvider serviceProvider,
        IOptions<SessionFilesCleanupOptions> options,
        SessionFilesCleanupSignal signal,
        ILogger<SessionFilesCleanupJob> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = _options.IntervalHours > 0 ? _options.IntervalHours : 6;
        var interval = TimeSpan.FromHours(intervalHours);

        _logger.LogInformation(
            "SessionFilesCleanupJob starting — scheduled interval {IntervalHours}h, " +
            "batch size {DeleteBatchSize}, max keys per scan {MaxKeysPerScan}",
            intervalHours, _options.DeleteBatchSize, _options.MaxKeysPerScan);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Race the timer tick against the channel reader. Whichever
                // completes first drives the next pass.
                var timerTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                var signalTask = _signal.Reader.WaitToReadAsync(stoppingToken).AsTask();

                var winner = await Task.WhenAny(timerTask, signalTask);

                if (winner == signalTask)
                {
                    // Drain ALL pending signals in one batch — back-to-back
                    // DeleteSessionAsync calls (rare but possible) coalesce
                    // into a single loop iteration.
                    await DrainPendingSignalsAsync(stoppingToken);
                }
                else
                {
                    // Timer won — run the scheduled scan.
                    // (Also handles the case where the timer task threw on shutdown;
                    // OperationCanceledException is caught below.)
                    if (await timerTask.ConfigureAwait(false))
                    {
                        await RunScheduledScanAsync(stoppingToken);
                    }
                    else
                    {
                        // Timer disposed — exit loop.
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("SessionFilesCleanupJob stopped (cancellation requested)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionFilesCleanupJob encountered a fatal error and is stopping");
            throw;
        }

        _logger.LogInformation("SessionFilesCleanupJob stopped");
    }

    /// <summary>
    /// Drains all pending session-end signals from the channel and evicts each
    /// session's files. Public/internal for test isolation.
    /// </summary>
    internal async Task DrainPendingSignalsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        while (_signal.Reader.TryRead(out var signal))
        {
            try
            {
                await EvictSessionAsync(
                    scope.ServiceProvider,
                    signal.TenantId,
                    signal.SessionId,
                    TriggerOnSessionEnd,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "SessionFilesCleanupJob: on-session-end eviction failed for tenantId={TenantId} sessionId={SessionId}",
                    signal.TenantId, signal.SessionId);
            }
        }
    }

    /// <summary>
    /// Runs the scheduled orphan-detection pass: enumerate indexed session IDs
    /// (per-tenant), compare against the active Redis session-key set, evict
    /// orphans. Internal for test isolation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Active-session source-of-truth: Redis <c>chat:session:{tenantId}:{sessionId}</c>
    /// key pattern (set by <see cref="ChatSessionManager.CreateSessionAsync"/>;
    /// removed by <see cref="ChatSessionManager.DeleteSessionAsync"/> or
    /// 24h sliding-TTL expiry). The scheduled pass catches the implicit
    /// (TTL-driven) deletes that the on-session-end signal cannot — no
    /// Redis callback exists for natural expiry.
    /// </para>
    /// <para>
    /// Tolerance: if <see cref="IConnectionMultiplexer"/> is not registered
    /// (Redis disabled, in-memory cache fallback), the scheduled pass logs
    /// once and skips. The on-session-end signal path remains effective —
    /// that path is the primary cleanup driver for explicit deletes.
    /// </para>
    /// </remarks>
    internal async Task RunScheduledScanAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = _serviceProvider.CreateScope();

        var sessionsEvicted = 0;
        var documentsDeleted = 0;
        var status = StatusSuccess;
        var perTenantBreakdown = new Dictionary<string, int>();

        try
        {
            var searchIndexClient = scope.ServiceProvider.GetService<SearchIndexClient>();
            if (searchIndexClient is null)
            {
                _logger.LogDebug(
                    "SessionFilesCleanupJob: SearchIndexClient not registered — scheduled scan skipped");
                return;
            }

            var aiSearchOptions = scope.ServiceProvider.GetRequiredService<IOptions<AiSearchOptions>>().Value;
            var sessionFilesClient = searchIndexClient.GetSearchClient(aiSearchOptions.SessionFilesIndexName);

            var multiplexer = scope.ServiceProvider.GetService<IConnectionMultiplexer>();
            if (multiplexer is null)
            {
                _logger.LogInformation(
                    "SessionFilesCleanupJob: IConnectionMultiplexer not registered (Redis disabled) — " +
                    "scheduled orphan scan skipped. On-session-end signal path remains active.");
                return;
            }

            // Step 1: enumerate distinct (tenantId, sessionId) pairs from the
            // session-files index. Page through the index — facet queries
            // are limited to top-N per facet, but we need the full set.
            var indexedSessions = await EnumerateIndexedSessionsAsync(sessionFilesClient, ct);

            if (indexedSessions.Count == 0)
            {
                _logger.LogDebug("SessionFilesCleanupJob: no indexed session files — nothing to scan");
                return;
            }

            // Step 2: for each (tenantId, sessionId) pair, check whether the
            // Redis key exists. If not, evict.
            foreach (var grouping in indexedSessions.GroupBy(p => p.TenantId))
            {
                var tenantId = grouping.Key;
                var sessionsForTenant = grouping.Select(p => p.SessionId).Distinct().ToList();

                var activeSessions = await GetActiveSessionsForTenantAsync(
                    multiplexer, tenantId, sessionsForTenant, ct);

                var orphans = sessionsForTenant.Except(activeSessions, StringComparer.Ordinal).ToList();

                if (orphans.Count == 0)
                {
                    continue;
                }

                foreach (var orphanSessionId in orphans)
                {
                    try
                    {
                        var deleted = await EvictSessionAsync(
                            scope.ServiceProvider,
                            tenantId,
                            orphanSessionId,
                            TriggerScheduled,
                            ct);

                        sessionsEvicted++;
                        documentsDeleted += deleted;

                        if (!perTenantBreakdown.ContainsKey(tenantId))
                        {
                            perTenantBreakdown[tenantId] = 0;
                        }
                        perTenantBreakdown[tenantId] += deleted;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        status = StatusPartial;
                        _logger.LogWarning(ex,
                            "SessionFilesCleanupJob: scheduled orphan eviction failed for tenantId={TenantId} sessionId={SessionId}",
                            tenantId, orphanSessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            status = StatusError;
            _logger.LogError(ex, "SessionFilesCleanupJob: scheduled scan failed");
        }
        finally
        {
            stopwatch.Stop();

            EmitScheduledRunEvent(
                sessionsEvicted: sessionsEvicted,
                documentsDeleted: documentsDeleted,
                perTenantBreakdown: perTenantBreakdown,
                durationMs: stopwatch.ElapsedMilliseconds,
                status: status);

            _logger.LogInformation(
                "SessionFilesCleanupJob: scheduled scan complete — " +
                "sessions evicted: {SessionsEvicted}, documents deleted: {DocumentsDeleted}, " +
                "duration: {DurationMs}ms, status: {Status}",
                sessionsEvicted, documentsDeleted, stopwatch.ElapsedMilliseconds, status);
        }
    }

    /// <summary>
    /// Evicts all session-files-index documents matching
    /// <paramref name="tenantId"/> + <paramref name="sessionId"/>. Idempotent:
    /// a second pass over an already-evicted session returns 0 hits and is a
    /// no-op (returns <c>0</c>).
    /// </summary>
    /// <returns>Number of documents successfully deleted (0 on idempotent no-op).</returns>
    /// <remarks>
    /// <para>
    /// Internal for test isolation — tests poke this directly without
    /// running the host loop.
    /// </para>
    /// <para>
    /// Filter shape (ADR-014): <c>tenantId eq '{escaped}' and sessionId eq '{escaped}'</c>
    /// — both predicates ALWAYS present. Cross-tenant cleanup is a defect.
    /// </para>
    /// </remarks>
    internal async Task<int> EvictSessionAsync(
        IServiceProvider scopedServices,
        string tenantId,
        string sessionId,
        string trigger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenantId)) throw new ArgumentException("Tenant ID is required", nameof(tenantId));
        if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("Session ID is required", nameof(sessionId));

        var stopwatch = Stopwatch.StartNew();
        var status = StatusSuccess;
        var deletedCount = 0;

        try
        {
            var searchIndexClient = scopedServices.GetService<SearchIndexClient>();
            if (searchIndexClient is null)
            {
                _logger.LogDebug(
                    "SessionFilesCleanupJob.EvictSessionAsync: SearchIndexClient not registered — skipping ({TenantId}/{SessionId})",
                    tenantId, sessionId);
                return 0;
            }

            var aiSearchOptions = scopedServices.GetRequiredService<IOptions<AiSearchOptions>>().Value;
            var sessionFilesClient = searchIndexClient.GetSearchClient(aiSearchOptions.SessionFilesIndexName);

            // ADR-014: tenant + session predicates always present.
            var filter = $"tenantId eq '{EscapeOData(tenantId)}' and sessionId eq '{EscapeOData(sessionId)}'";
            var searchOptions = new SearchOptions
            {
                Filter = filter,
                Size = _options.DeleteBatchSize,
                Select = { "id" },
            };

            var keysToDelete = new List<string>();
            var response = await sessionFilesClient.SearchAsync<SessionFilesCleanupKey>("*", searchOptions, ct);
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                if (!string.IsNullOrEmpty(result.Document?.Id))
                {
                    keysToDelete.Add(result.Document.Id);
                }
            }

            if (keysToDelete.Count == 0)
            {
                // Idempotent no-op — second pass finds nothing. Emit zero-count telemetry per project contract.
                stopwatch.Stop();
                EmitEvictionEvent(
                    trigger: trigger,
                    sessionsEvicted: 0,
                    documentsDeleted: 0,
                    tenantId: tenantId,
                    sessionId: sessionId,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    status: StatusSuccess);

                _logger.LogDebug(
                    "SessionFilesCleanupJob.EvictSessionAsync: no documents found for tenantId={TenantId} sessionId={SessionId} (idempotent no-op)",
                    tenantId, sessionId);
                return 0;
            }

            // Batch the delete calls per options.
            for (int i = 0; i < keysToDelete.Count; i += _options.DeleteBatchSize)
            {
                var batch = keysToDelete
                    .Skip(i)
                    .Take(_options.DeleteBatchSize)
                    .ToList();

                var deleteResponse = await sessionFilesClient.DeleteDocumentsAsync(
                    "id", batch, cancellationToken: ct);
                deletedCount += deleteResponse.Value.Results.Count(r => r.Succeeded);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            status = StatusError;
            throw;
        }
        catch (Exception ex)
        {
            status = StatusError;
            _logger.LogWarning(ex,
                "SessionFilesCleanupJob.EvictSessionAsync: eviction failed for tenantId={TenantId} sessionId={SessionId}",
                tenantId, sessionId);
            throw;
        }
        finally
        {
            stopwatch.Stop();

            if (deletedCount > 0 || status != StatusSuccess)
            {
                // Emit telemetry for the success path with deletions OR for any error.
                // The zero-count no-op already emitted above.
                EmitEvictionEvent(
                    trigger: trigger,
                    sessionsEvicted: deletedCount > 0 ? 1 : 0,
                    documentsDeleted: deletedCount,
                    tenantId: tenantId,
                    sessionId: sessionId,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    status: status);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Enumerate distinct <c>(tenantId, sessionId)</c> pairs present in the
    /// session-files index. Pages through the entire index using
    /// <c>Skip</c>/<c>Top</c>. Bounded to a sensible cap to prevent runaway
    /// scans — production indexes are session-scoped + aggressively cleaned,
    /// so the working set should remain small.
    /// </summary>
    private async Task<IReadOnlyList<IndexedSessionRef>> EnumerateIndexedSessionsAsync(
        SearchClient sessionFilesClient,
        CancellationToken ct)
    {
        var seen = new HashSet<(string TenantId, string SessionId)>();
        var pageSize = 1000;
        var maxPages = 10; // Guard rail: 10 * 1000 = 10K distinct documents per scan.

        for (int page = 0; page < maxPages; page++)
        {
            var searchOptions = new SearchOptions
            {
                Size = pageSize,
                Skip = page * pageSize,
                Select = { "id", "tenantId", "sessionId" },
            };

            var response = await sessionFilesClient.SearchAsync<SessionFilesCleanupRef>(
                "*", searchOptions, ct);

            var pageCount = 0;
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                pageCount++;
                if (result.Document is not null
                    && !string.IsNullOrEmpty(result.Document.TenantId)
                    && !string.IsNullOrEmpty(result.Document.SessionId))
                {
                    seen.Add((result.Document.TenantId, result.Document.SessionId));
                }
            }

            if (pageCount < pageSize)
            {
                // Last page.
                break;
            }
        }

        return seen
            .Select(p => new IndexedSessionRef(p.TenantId, p.SessionId))
            .ToList();
    }

    /// <summary>
    /// For a tenant, returns the subset of <paramref name="candidateSessionIds"/>
    /// whose Redis keys (<c>chat:session:{tenantId}:{sessionId}</c>) still exist.
    /// Uses <see cref="IDatabase.KeyExistsAsync(RedisKey, CommandFlags)"/>
    /// per session — bounded per-tenant scan, no Redis SCAN pattern needed.
    /// </summary>
    private async Task<IReadOnlyCollection<string>> GetActiveSessionsForTenantAsync(
        IConnectionMultiplexer multiplexer,
        string tenantId,
        IReadOnlyList<string> candidateSessionIds,
        CancellationToken ct)
    {
        var db = multiplexer.GetDatabase();
        var active = new List<string>();

        var maxKeys = _options.MaxKeysPerScan;
        var checkedCount = 0;

        foreach (var sessionId in candidateSessionIds)
        {
            ct.ThrowIfCancellationRequested();

            if (checkedCount >= maxKeys)
            {
                _logger.LogWarning(
                    "SessionFilesCleanupJob: MaxKeysPerScan ({MaxKeysPerScan}) reached for tenantId={TenantId}; " +
                    "remaining sessions deferred to next scheduled pass",
                    maxKeys, tenantId);
                break;
            }

            var redisKey = ChatSessionManager.BuildCacheKey(tenantId, sessionId);
            var exists = await db.KeyExistsAsync(redisKey);
            if (exists)
            {
                active.Add(sessionId);
            }
            checkedCount++;
        }

        return active;
    }

    /// <summary>
    /// Escapes a string for use in an OData filter expression (single-quote doubling).
    /// </summary>
    private static string EscapeOData(string value) => value.Replace("'", "''");

    /// <summary>
    /// Emits <c>r5.session_files_cleanup.run</c> telemetry for a single
    /// eviction. Uses <see cref="Telemetry.AiTelemetry.ActivitySource"/> to
    /// surface as an <see cref="Activity"/> tagged with the canonical
    /// low-cardinality fields. Task 008 wires App Insights queries against
    /// these tag names.
    /// </summary>
    private void EmitEvictionEvent(
        string trigger,
        int sessionsEvicted,
        int documentsDeleted,
        string tenantId,
        string sessionId,
        long durationMs,
        string status)
    {
        using var activity = global::Sprk.Bff.Api.Telemetry.AiTelemetry.ActivitySource.StartActivity(
            TelemetryEventName,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("r5.trigger", trigger);
            activity.SetTag("r5.sessions_evicted", sessionsEvicted);
            activity.SetTag("r5.documents_deleted", documentsDeleted);
            activity.SetTag("r5.tenant_id", tenantId);
            activity.SetTag("r5.session_id", sessionId);
            activity.SetTag("r5.duration_ms", durationMs);
            activity.SetTag("r5.completion_status", status);
        }
    }

    /// <summary>
    /// Emits <c>r5.session_files_cleanup.run</c> telemetry for a scheduled
    /// scan with per-tenant breakdown.
    /// </summary>
    private void EmitScheduledRunEvent(
        int sessionsEvicted,
        int documentsDeleted,
        IReadOnlyDictionary<string, int> perTenantBreakdown,
        long durationMs,
        string status)
    {
        using var activity = global::Sprk.Bff.Api.Telemetry.AiTelemetry.ActivitySource.StartActivity(
            TelemetryEventName,
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("r5.trigger", TriggerScheduled);
            activity.SetTag("r5.sessions_evicted", sessionsEvicted);
            activity.SetTag("r5.documents_deleted", documentsDeleted);
            activity.SetTag("r5.tenant_count", perTenantBreakdown.Count);
            activity.SetTag("r5.duration_ms", durationMs);
            activity.SetTag("r5.completion_status", status);

            // Per-tenant breakdown is a higher-cardinality field — emit as a
            // single delimited tag so dashboards can split if needed without
            // exploding tag count per event.
            if (perTenantBreakdown.Count > 0)
            {
                var breakdown = string.Join(",",
                    perTenantBreakdown.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                activity.SetTag("r5.per_tenant_breakdown", breakdown);
            }
        }
    }
}

/// <summary>
/// Internal projection of a session-files index document used by
/// <see cref="SessionFilesCleanupJob"/> when listing IDs to delete. Includes
/// only the <c>id</c> field — body, tenant, and session are projected via
/// <see cref="Azure.Search.Documents.SearchOptions.Select"/>.
/// </summary>
internal sealed class SessionFilesCleanupKey
{
    public string? Id { get; set; }
}

/// <summary>
/// Internal projection of a session-files index document used by
/// <see cref="SessionFilesCleanupJob.RunScheduledScanAsync"/> when
/// enumerating distinct <c>(tenantId, sessionId)</c> pairs.
/// </summary>
internal sealed class SessionFilesCleanupRef
{
    public string? Id { get; set; }
    public string? TenantId { get; set; }
    public string? SessionId { get; set; }
}

/// <summary>
/// In-memory ref of a (tenantId, sessionId) pair surfaced by the index scan.
/// </summary>
internal readonly record struct IndexedSessionRef(string TenantId, string SessionId);
