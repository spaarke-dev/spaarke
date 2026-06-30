namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Background sweeper that periodically releases SPE checkouts whose client-side
/// heartbeat has gone stale (Spaarke Compose R1 — Spike #3 §4.3).
///
/// <para>
/// <b>Algorithm</b> (locked by Spike #3 §1 + §4):
/// <list type="bullet">
///   <item>Scan interval: 2 minutes.</item>
///   <item>Stale threshold: 15 minutes since last heartbeat.</item>
///   <item>Max orphan lifetime ceiling: 15 + 2 = ≤17 minutes.</item>
/// </list>
/// </para>
///
/// <para>
/// On each iteration the sweeper asks <see cref="DocumentCheckoutService"/> for any
/// <c>sprk_document</c> rows where <c>sprk_checkedoutdate</c> is set AND
/// <c>sprk_lastheartbeatutc</c> is either NULL or older than (UtcNow − 15 min). For each
/// match it calls <see cref="DocumentCheckoutService.ReleaseCheckoutSystemAsync"/> —
/// a sweeper-specific release path that bypasses the same-user check (the sweeper runs
/// under the BFF's managed identity, not as the lock holder; it is implicitly authorized
/// to release any lock whose heartbeat has expired).
/// </para>
///
/// <para>
/// <b>Why a sweeper instead of just-in-time release on next acquisition</b> (Spike #3
/// §4.3 rationale): a sweeper guarantees release regardless of whether anyone tries to
/// claim the doc again. If user A locks a document and never returns, and no other user
/// ever attempts to open it, a JIT-only release path would let the lock linger
/// indefinitely. The sweeper bounds the max orphan-lock lifetime to
/// (stale-threshold + scan-interval) = ≤17 minutes.
/// </para>
///
/// <para>
/// <b>ADR-001</b> BackgroundService pattern (in-process async work; no Azure Functions).
/// <b>ADR-010</b> DI minimalism: registered once via
/// <c>services.AddHostedService&lt;StaleCheckoutSweeperHostedService&gt;()</c> in
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.ComposeModule"/>. Uses
/// <see cref="IServiceProvider"/> to create a per-iteration scope so the scoped
/// <see cref="DocumentCheckoutService"/> can be resolved cleanly.
/// </para>
///
/// <para>
/// <b>Resilience</b>: each iteration is wrapped in try/catch — a transient Dataverse
/// failure or HttpRequestException does NOT crash the host or stop the loop. After an
/// error the sweeper logs and waits the normal scan interval before retrying. Each
/// individual stale-row release is also try-wrapped so a single bad row doesn't abort
/// the whole iteration.
/// </para>
///
/// <para>
/// <b>Cost</b>: one filtered Dataverse query every 2 minutes. The filter
/// (<c>sprk_checkedoutdate ne null</c>) restricts the scan to actively-locked documents
/// — a small subset of <c>sprk_documents</c>. Expected steady-state result count: low
/// single digits. Well within Dataverse query budget.
/// </para>
/// </summary>
public sealed class StaleCheckoutSweeperHostedService : BackgroundService
{
    /// <summary>
    /// How often the sweeper scans for stale locks. Locked at 2 minutes per Spike #3 §4.3.
    /// </summary>
    internal static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Threshold beyond which a checkout's last heartbeat is considered stale. Locked
    /// at 15 minutes per Spike #3 §1 + design.md §14 row 4 + spec FR-17.
    /// </summary>
    internal static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Per-iteration cap on stale candidates released. Bounds the cost of a single sweep
    /// pass if a large backlog accumulates (e.g. after a long BFF outage). Subsequent
    /// iterations pick up the remainder.
    /// </summary>
    internal const int MaxRowsPerIteration = 100;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StaleCheckoutSweeperHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public StaleCheckoutSweeperHostedService(
        IServiceProvider serviceProvider,
        ILogger<StaleCheckoutSweeperHostedService> logger,
        TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "StaleCheckoutSweeperHostedService started — scan every {ScanIntervalMinutes}min, stale threshold {StaleThresholdMinutes}min (max orphan lifetime ≤{MaxOrphanMinutes}min)",
            ScanInterval.TotalMinutes,
            StaleThreshold.TotalMinutes,
            ScanInterval.TotalMinutes + StaleThreshold.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndReleaseStaleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Per-iteration safety: never let a transient failure kill the host.
                _logger.LogError(ex, "StaleCheckoutSweeperHostedService scan iteration failed; will retry next interval");
            }

            try
            {
                await Task.Delay(ScanInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("StaleCheckoutSweeperHostedService stopped");
    }

    /// <summary>
    /// Single scan iteration: probe for stale candidates, release each one.
    /// Internal for unit-test access — tests drive a single iteration via
    /// <see cref="ScanAndReleaseStaleOnceAsync"/>.
    /// </summary>
    internal async Task ScanAndReleaseStaleAsync(CancellationToken ct)
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime - StaleThreshold;

        using var scope = _serviceProvider.CreateScope();
        var checkoutService = scope.ServiceProvider.GetRequiredService<Sprk.Bff.Api.Services.DocumentCheckoutService>();

        await ScanAndReleaseStaleOnceAsync(checkoutService, cutoffUtc, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure variant exposed for unit tests — takes the resolved
    /// <see cref="Sprk.Bff.Api.Services.DocumentCheckoutService"/> directly so tests
    /// can inject a mock without touching the DI container.
    /// </summary>
    internal async Task ScanAndReleaseStaleOnceAsync(
        Sprk.Bff.Api.Services.DocumentCheckoutService checkoutService,
        DateTime cutoffUtc,
        CancellationToken ct)
    {
        var staleIds = await checkoutService
            .GetStaleCheckedOutDocumentsAsync(cutoffUtc, MaxRowsPerIteration, ct)
            .ConfigureAwait(false);

        if (staleIds.Count == 0)
        {
            _logger.LogDebug("Stale-checkout scan: no stale checkouts found (cutoff {CutoffUtc:O})", cutoffUtc);
            return;
        }

        _logger.LogInformation(
            "Stale-checkout scan: found {StaleCount} stale checkouts (cutoff {CutoffUtc:O}) — releasing now",
            staleIds.Count, cutoffUtc);

        var releasedCount = 0;
        var failedCount = 0;

        foreach (var docId in staleIds)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var released = await checkoutService
                    .ReleaseCheckoutSystemAsync(docId, ct)
                    .ConfigureAwait(false);

                if (released)
                {
                    releasedCount++;
                }
                else
                {
                    // Document is no longer in stale-checkout state — likely already
                    // released or check-in raced with the sweeper. Not an error.
                    _logger.LogDebug(
                        "Stale-checkout release skipped: document {DocumentId} no longer checked out",
                        docId);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex,
                    "Stale-checkout release failed for document {DocumentId}; continuing to next candidate",
                    docId);
            }
        }

        _logger.LogInformation(
            "Stale-checkout scan complete: released {ReleasedCount}, failed {FailedCount}, candidates {CandidateCount}",
            releasedCount, failedCount, staleIds.Count);
    }
}
