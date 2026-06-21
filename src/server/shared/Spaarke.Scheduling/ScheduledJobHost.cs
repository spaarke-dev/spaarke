using System.Collections.Concurrent;
using System.Diagnostics;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Spaarke.Scheduling;

/// <summary>
/// In-process <see cref="BackgroundService"/> that owns scheduled-job dispatch for every
/// Spaarke service consuming <c>Spaarke.Scheduling</c>. Reads job definitions from
/// <see cref="IBackgroundJobStore"/> on startup, parses each definition's cron expression via
/// Cronos, dispatches the registered <see cref="IScheduledJob"/> handler on schedule, and
/// records each run via <see cref="IBackgroundJobStore.RecordRunStartAsync"/> /
/// <see cref="IBackgroundJobStore.RecordRunCompleteAsync"/>.
/// </summary>
/// <remarks>
/// <para><b>Spec coverage:</b></para>
/// <list type="bullet">
///   <item>FR-2.1 — Lives in <c>Spaarke.Scheduling</c>; depends only on Spaarke.Core (transitively via project ref).</item>
///   <item>FR-2.2 — Cron parsing delegated to <see cref="CronExpression"/> from the Cronos NuGet (added in task 010).</item>
///   <item>FR-2.3 — Loads job rows on startup, refreshes every <see cref="ScheduledJobHostOptions.RefreshInterval"/>
///     (default 1h), dispatches handlers per schedule, writes a run row per execution.</item>
///   <item>NFR-07 — <see cref="StopAsync"/> waits up to <see cref="ScheduledJobHostOptions.ShutdownDrainTimeout"/>
///     (default 30s) for in-flight jobs to observe cancellation and complete.</item>
///   <item>NFR-08 — Every run carries a fresh GUID-derived correlation id passed to the handler
///     via <see cref="JobRunContext.CorrelationId"/>.</item>
///   <item>ADR-001 — Pure in-process <see cref="BackgroundService"/>; no Azure Functions / external scheduler.</item>
///   <item>ADR-010 — Registered as Singleton via <c>AddHostedService</c>; constructor takes concretes / minimal interfaces.</item>
/// </list>
/// <para><b>Design choices (departures from the POML wording):</b></para>
/// <list type="bullet">
///   <item><i>No per-job <see cref="PeriodicTimer"/>.</i> The POML described "per-job PeriodicTimer";
///     <see cref="PeriodicTimer"/> takes a fixed <see cref="TimeSpan"/>, but cron schedules are not
///     fixed periods (e.g., daily at 02:00 = ~24h delay normally, but ~25h on the spring DST shift).
///     A single scheduling loop that computes the next fire across all enabled jobs via Cronos
///     and sleeps until the earliest is correct for arbitrary cron expressions, simpler, and
///     uses one timer instead of N.</item>
///   <item><i>Dispatches happen on background tasks tracked in a thread-safe set.</i>
///     <see cref="StopAsync"/> awaits the set with the drain timeout so cancellation propagates
///     to in-flight jobs without blocking host shutdown beyond NFR-07's 30s ceiling.</item>
///   <item><i>Jobs registered in <see cref="ScheduledJobRegistry"/> but absent from
///     <see cref="IBackgroundJobStore"/> are skipped with a warning</i> (and vice versa) —
///     the host is the consistency observer, not the persistence enforcer.</item>
/// </list>
/// </remarks>
public sealed class ScheduledJobHost : BackgroundService
{
    // Reasonable default cap on cron-parse-failure backoff. Prevents tight loops if a job
    // definition has an invalid cron expression but is still flagged Enabled. The job is
    // logged + skipped; the host continues serving other jobs.
    private static readonly TimeSpan MinLoopSleep = TimeSpan.FromMilliseconds(50);

    private readonly ScheduledJobRegistry _registry;
    private readonly IBackgroundJobStore _store;
    private readonly ScheduledJobHostOptions _options;
    private readonly ILogger<ScheduledJobHost> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();
    private DateTimeOffset _lastRefreshUtc = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<string, ScheduledJobState> _state =
        new Dictionary<string, ScheduledJobState>(StringComparer.Ordinal);

    public ScheduledJobHost(
        ScheduledJobRegistry registry,
        IBackgroundJobStore store,
        ScheduledJobHostOptions options,
        ILogger<ScheduledJobHost> logger,
        TimeProvider? timeProvider = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ScheduledJobHost starting — refresh interval {RefreshInterval}, drain timeout {DrainTimeout}",
            _options.RefreshInterval, _options.ShutdownDrainTimeout);

        // Initial load. If this fails the host still keeps running so subsequent refresh ticks
        // can recover (e.g., transient Dataverse hiccup at boot). Empty registry / empty store
        // = empty loop, which is a valid steady state.
        await RefreshDefinitionsAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown path — fall through to drain in StopAsync.
                break;
            }
            catch (Exception ex)
            {
                // Defensive: an exception thrown out of TickAsync (rather than per-job) is
                // unexpected — log and back off briefly so a poison condition doesn't pin a CPU.
                _logger.LogError(ex, "ScheduledJobHost tick failed unexpectedly; backing off 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("ScheduledJobHost ExecuteAsync exiting (stoppingToken cancelled)");
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ScheduledJobHost stopping — waiting up to {DrainTimeout} for {InFlightCount} in-flight job(s)",
            _options.ShutdownDrainTimeout, _inFlight.Count);

        // Triggers ExecuteAsync to exit; the per-job tokens are linked to it, so in-flight jobs
        // observe cancellation through their context tokens.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        var inFlightSnapshot = _inFlight.Values.ToArray();
        if (inFlightSnapshot.Length == 0)
        {
            _logger.LogInformation("ScheduledJobHost stopped — no in-flight jobs");
            return;
        }

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drainCts.CancelAfter(_options.ShutdownDrainTimeout);

        try
        {
            await Task.WhenAll(inFlightSnapshot).WaitAsync(drainCts.Token).ConfigureAwait(false);
            _logger.LogInformation("ScheduledJobHost stopped — all in-flight jobs drained");
        }
        catch (OperationCanceledException)
        {
            var stillRunning = _inFlight.Count;
            _logger.LogWarning(
                "ScheduledJobHost shutdown drain timed out after {DrainTimeout} — {StillRunning} job(s) still running (NFR-07 ceiling reached)",
                _options.ShutdownDrainTimeout, stillRunning);
        }
    }

    /// <summary>
    /// One iteration of the scheduling loop. Refreshes definitions if due, computes the next
    /// fire time across all enabled jobs, and either dispatches due jobs or sleeps until the
    /// earliest upcoming fire. Visible to tests via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal async Task TickAsync(CancellationToken stoppingToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (now - _lastRefreshUtc >= _options.RefreshInterval)
        {
            await RefreshDefinitionsAsync(stoppingToken).ConfigureAwait(false);
        }

        // Snapshot the current state so we don't fight a concurrent refresh mid-loop.
        var state = _state;
        if (state.Count == 0)
        {
            // Nothing scheduled — wait until next refresh or shutdown.
            var idleSleep = TimeSpan.FromTicks(Math.Min(_options.MaxLoopSleep.Ticks, _options.RefreshInterval.Ticks));
            await Task.Delay(idleSleep, stoppingToken).ConfigureAwait(false);
            return;
        }

        // Pass 1: collect jobs whose next fire is at or before "now"; dispatch them.
        // Pass 2: compute the soonest future fire across remaining jobs; sleep until then (capped).
        var dispatched = 0;
        DateTimeOffset? earliestFuture = null;

        foreach (var entry in state.Values)
        {
            if (!entry.Definition.Enabled || entry.NextFireUtc is null)
            {
                continue;
            }

            if (entry.NextFireUtc.Value <= now)
            {
                DispatchAndAdvance(entry, now, stoppingToken);
                dispatched++;
            }
            else if (earliestFuture is null || entry.NextFireUtc.Value < earliestFuture.Value)
            {
                earliestFuture = entry.NextFireUtc.Value;
            }
        }

        if (dispatched > 0)
        {
            // After firing, re-evaluate earliestFuture against the advanced NextFireUtc values.
            foreach (var entry in state.Values)
            {
                if (!entry.Definition.Enabled || entry.NextFireUtc is null) continue;
                if (earliestFuture is null || entry.NextFireUtc.Value < earliestFuture.Value)
                {
                    earliestFuture = entry.NextFireUtc.Value;
                }
            }
        }

        // Sleep until the next fire — but never beyond MaxLoopSleep so the refresh interval
        // remains responsive, and never less than MinLoopSleep to avoid a tight loop on the
        // boundary of a 1-second cron schedule.
        TimeSpan sleep;
        if (earliestFuture is null)
        {
            sleep = _options.MaxLoopSleep;
        }
        else
        {
            var nextDelta = earliestFuture.Value - _timeProvider.GetUtcNow();
            sleep = nextDelta < MinLoopSleep ? MinLoopSleep :
                    nextDelta > _options.MaxLoopSleep ? _options.MaxLoopSleep :
                    nextDelta;
        }

        try
        {
            await Task.Delay(sleep, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
    }

    /// <summary>Re-reads job definitions from the store and rebuilds the scheduling state.</summary>
    private async Task RefreshDefinitionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await _store.LoadJobsAsync(cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            var next = new Dictionary<string, ScheduledJobState>(StringComparer.Ordinal);

            foreach (var def in definitions)
            {
                var handler = _registry.Resolve(def.JobId);
                if (handler is null)
                {
                    _logger.LogWarning(
                        "Background job definition '{JobId}' has no registered IScheduledJob handler — skipping",
                        def.JobId);
                    continue;
                }

                CronExpression cron;
                try
                {
                    cron = ParseCron(def.CronSchedule);
                }
                catch (CronFormatException ex)
                {
                    _logger.LogError(
                        ex,
                        "Background job '{JobId}' has invalid cron expression '{Cron}' — disabling for this load cycle",
                        def.JobId, def.CronSchedule);
                    continue;
                }

                var nextFire = def.Enabled
                    ? cron.GetNextOccurrence(now.UtcDateTime, TimeZoneInfo.Utc) is DateTime nf
                        ? new DateTimeOffset(nf, TimeSpan.Zero)
                        : (DateTimeOffset?)null
                    : null;

                next[def.JobId] = new ScheduledJobState(def, handler, cron, nextFire);
            }

            // Surface registered jobs that have no definition row — useful in early-wave usage
            // where ops forgot to seed the store after registering a handler.
            foreach (var registered in _registry.EnumerateAll())
            {
                if (!next.ContainsKey(registered.JobId))
                {
                    _logger.LogDebug(
                        "Registered IScheduledJob '{JobId}' has no BackgroundJobDefinition — not scheduled until a definition is added",
                        registered.JobId);
                }
            }

            _state = next;
            _lastRefreshUtc = _timeProvider.GetUtcNow();

            _logger.LogInformation(
                "ScheduledJobHost refreshed — {ScheduledCount} job(s) scheduled, {TotalDefinitions} definition(s), {RegisteredHandlers} handler(s)",
                next.Values.Count(s => s.NextFireUtc is not null),
                definitions.Count,
                _registry.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScheduledJobHost definition refresh failed — keeping prior state");
        }
    }

    /// <summary>Dispatches the handler on a background task and advances <c>NextFireUtc</c>.</summary>
    private void DispatchAndAdvance(ScheduledJobState entry, DateTimeOffset firingUtc, CancellationToken stoppingToken)
    {
        // Advance the next-fire time eagerly so we don't double-fire if the dispatch task
        // hasn't observed cancellation by the time the loop comes back around.
        entry.AdvanceNextFire(firingUtc);

        var runId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N"); // NFR-08: fresh per run.
        var context = new JobRunContext(
            RunId: runId,
            CorrelationId: correlationId,
            Trigger: JobRunTrigger.Scheduled,
            Parameters: ParametersFor(entry.Definition));

        // Track the dispatch task so StopAsync can drain it (NFR-07).
        var task = Task.Run(() => RunJobAsync(entry, context, stoppingToken), stoppingToken);
        _inFlight[runId] = task;

        // Untrack on completion regardless of outcome.
        _ = task.ContinueWith(
            _ =>
            {
                _inFlight.TryRemove(runId, out Task? _removed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunJobAsync(ScheduledJobState entry, JobRunContext context, CancellationToken stoppingToken)
    {
        var jobId = entry.Definition.JobId;
        Guid persistedRunId = default;
        var sw = Stopwatch.StartNew();

        try
        {
            persistedRunId = await _store
                .RecordRunStartAsync(jobId, context.Trigger, context.CorrelationId, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ScheduledJobHost failed to record run start for '{JobId}' (correlationId {CorrelationId}) — running anyway",
                jobId, context.CorrelationId);
        }

        JobRunResult result;
        try
        {
            _logger.LogInformation(
                "Dispatching scheduled job '{JobId}' runId={RunId} correlationId={CorrelationId}",
                jobId, context.RunId, context.CorrelationId);

            result = await entry.Handler.ExecuteAsync(context, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Scheduled job '{JobId}' completed runId={RunId} success={Success} processedItems={ProcessedItems} duration={DurationMs}ms",
                jobId, context.RunId, result.Success, result.ProcessedItems, (long)result.Duration.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            sw.Stop();
            result = new JobRunResult(
                Success: false,
                ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                ProcessedItems: null,
                Duration: sw.Elapsed);
            _logger.LogWarning(
                "Scheduled job '{JobId}' runId={RunId} cancelled by host shutdown",
                jobId, context.RunId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            result = new JobRunResult(
                Success: false,
                ErrorMessage: ex.Message,
                ProcessedItems: null,
                Duration: sw.Elapsed);
            _logger.LogError(
                ex,
                "Scheduled job '{JobId}' runId={RunId} threw — recorded as failure",
                jobId, context.RunId);
        }

        if (persistedRunId != default)
        {
            try
            {
                // Use CancellationToken.None so we still persist completion even if host is shutting down.
                await _store.RecordRunCompleteAsync(persistedRunId, result, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ScheduledJobHost failed to record run completion for '{JobId}' runId={RunId} (correlationId {CorrelationId})",
                    jobId, context.RunId, context.CorrelationId);
            }
        }
    }

    /// <summary>
    /// Parse a cron expression supporting both 5-field (minute-precision; the spec.md default
    /// for <c>sprk_cronschedule</c>) and 6-field (second-precision) syntaxes. Token count is the
    /// disambiguator; whitespace-collapsed before split so "0 2 * * *" and "0  2  *  *  *" parse
    /// identically. 6-field is reserved for high-frequency internal jobs (tests, watchdogs) —
    /// production jobs are expected to use 5-field minute-precision per FR-2.4.
    /// </summary>
    internal static CronExpression ParseCron(string expression)
    {
        ArgumentException.ThrowIfNullOrEmpty(expression);
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return fields.Length == 6
            ? CronExpression.Parse(expression, CronFormat.IncludeSeconds)
            : CronExpression.Parse(expression);
    }

    private static IDictionary<string, object> ParametersFor(BackgroundJobDefinition def)
    {
        // Configuration parameters are flowed verbatim; richer parsing is the handler's
        // responsibility (it knows its own schema).
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(def.ConfigJson))
        {
            parameters["configJson"] = def.ConfigJson;
        }
        parameters["jobId"] = def.JobId;
        return parameters;
    }

    /// <summary>Per-job scheduling state. Mutable <see cref="NextFireUtc"/> via <see cref="AdvanceNextFire"/>.</summary>
    internal sealed class ScheduledJobState
    {
        public BackgroundJobDefinition Definition { get; }
        public IScheduledJob Handler { get; }
        public CronExpression Cron { get; }
        public DateTimeOffset? NextFireUtc { get; private set; }

        public ScheduledJobState(
            BackgroundJobDefinition definition,
            IScheduledJob handler,
            CronExpression cron,
            DateTimeOffset? nextFireUtc)
        {
            Definition = definition;
            Handler = handler;
            Cron = cron;
            NextFireUtc = nextFireUtc;
        }

        /// <summary>Advance <see cref="NextFireUtc"/> to the next cron occurrence strictly after the given firing time.</summary>
        public void AdvanceNextFire(DateTimeOffset firingUtc)
        {
            // GetNextOccurrence is exclusive of the input by default — pass the firing time as
            // the "from" so the next occurrence is the next future fire.
            var next = Cron.GetNextOccurrence(firingUtc.UtcDateTime, TimeZoneInfo.Utc);
            NextFireUtc = next.HasValue ? new DateTimeOffset(next.Value, TimeSpan.Zero) : null;
        }
    }
}
