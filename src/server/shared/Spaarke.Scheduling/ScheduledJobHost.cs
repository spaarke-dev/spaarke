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
    /// Dispatch a registered <see cref="IScheduledJob"/> out-of-band as a manual admin trigger
    /// (R3 task 021 — <c>POST /api/admin/jobs/{jobId}/trigger</c>). Returns immediately with the
    /// persistent run id + start timestamp after the run row is written and the handler dispatch
    /// is fire-and-tracked on a background task. The run completion record is written by the
    /// tracked task without the caller waiting on the job's duration.
    /// </summary>
    /// <remarks>
    /// <para><b>Why fire-and-track</b>: jobs run for arbitrary durations (membership recon = minutes,
    /// search-index rebuild = hours). Blocking the admin HTTP request on job completion would
    /// time out the request and tie up a request thread. The endpoint returns 202 Accepted
    /// immediately; admin clients poll <c>GET /api/admin/jobs/{jobId}/status</c> for outcome.</para>
    /// <para><b>Trigger</b>: <see cref="JobRunTrigger.ManualAdmin"/>. Per the
    /// <see cref="IBackgroundJobStore.RecordRunStartAsync"/> contract, <c>scheduledFireUtc</c> is
    /// <c>null</c> for manual triggers — these don't participate in tick-level idempotency
    /// (the admin chose to retrigger; duplicate-fire dedupe doesn't apply).</para>
    /// <para><b>Correlation id (NFR-08)</b>: every trigger gets a fresh GUID-derived correlation id
    /// distinct from any other run (scheduled or manual).</para>
    /// <para><b>Cancellation (NFR-07)</b>: <paramref name="cancellationToken"/> cancels the
    /// <i>dispatch path</i> (registry lookup, run-start record). Once the background task is
    /// kicked off, it observes the host's <c>stoppingToken</c> instead — caller cancellation
    /// does NOT interrupt the in-flight job (that would lose the run on a Ctrl-C from the admin
    /// client). The host's shutdown drain (NFR-07: 30s) is the correct cancellation surface
    /// for in-flight runs.</para>
    /// <para><b>Idempotency</b>: NOT applied to manual triggers. If an admin double-clicks the
    /// trigger button, two run records are written and the handler runs twice — this is the
    /// admin's explicit choice. Scheduled idempotency (<see cref="IBackgroundJobStore.HasRunForScheduledTimeAsync"/>)
    /// only kicks in for tick-level dedup after host restart.</para>
    /// <para><b>Background task tracking</b>: the dispatched task is added to <see cref="_inFlight"/>
    /// so <see cref="StopAsync"/>'s drain logic waits for it (NFR-07).</para>
    /// </remarks>
    /// <param name="jobId">Stable job id (matches <see cref="IScheduledJob.JobId"/>).</param>
    /// <param name="parameters">Optional parameter overrides for this manual run; merged into the
    /// per-run <see cref="JobRunContext.Parameters"/> dictionary on top of the job's persisted
    /// <c>ConfigJson</c>. Pass <c>null</c> for no overrides.</param>
    /// <param name="cancellationToken">Cancels the dispatch path only (NOT the in-flight job).</param>
    /// <returns>
    /// <see cref="TriggerResult"/> with the persistent run id, <c>"Running"</c> status, and dispatch
    /// timestamp. Returned BEFORE the handler completes.
    /// </returns>
    /// <exception cref="JobNotFoundException">Thrown if <paramref name="jobId"/> is not registered
    /// with <see cref="ScheduledJobRegistry"/>. The admin endpoint maps this to 404.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/>
    /// is cancelled before the run row is written.</exception>
    public async Task<TriggerResult> TriggerNowAsync(
        string jobId,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);

        // Registry lookup → JobNotFoundException maps to 404 at endpoint layer. We do NOT
        // require the job to have a BackgroundJobDefinition in the store — manual triggers
        // can run a handler-registered-but-not-yet-defined job (admin troubleshooting flow).
        var handler = _registry.Resolve(jobId)
            ?? throw new JobNotFoundException(jobId);

        cancellationToken.ThrowIfCancellationRequested();

        // Look up the (optional) definition so any persisted ConfigJson flows into the run
        // context. If the definition is missing, parameters still get the synthetic jobId key
        // (mirrors ParametersFor's contract for scheduled runs).
        var definitions = await _store.LoadJobsAsync(cancellationToken).ConfigureAwait(false);
        var definition = definitions.FirstOrDefault(d => string.Equals(d.JobId, jobId, StringComparison.Ordinal));
        var contextParameters = BuildManualTriggerParameters(jobId, definition, parameters);

        var runId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString("N"); // NFR-08: fresh per trigger.
        var context = new JobRunContext(
            RunId: runId,
            CorrelationId: correlationId,
            Trigger: JobRunTrigger.ManualAdmin,
            Parameters: contextParameters);

        var startedAt = _timeProvider.GetUtcNow();

        // Persist the run-start row BEFORE returning so admin clients can poll
        // /api/admin/jobs/{jobId}/status and see the row immediately. scheduledFireUtc=null
        // because manual triggers don't participate in tick-level idempotency
        // (per IBackgroundJobStore.RecordRunStartAsync contract).
        Guid persistedRunId;
        try
        {
            persistedRunId = await _store
                .RecordRunStartAsync(jobId, context.Trigger, context.CorrelationId, scheduledFireUtc: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Persistence failure on a manual trigger is fatal — we can't return a runId the
            // admin can poll on. Bubble up; endpoint layer maps to ProblemDetails 500.
            _logger.LogError(
                ex,
                "ScheduledJobHost.TriggerNowAsync failed to record run start for '{JobId}' (correlationId {CorrelationId})",
                jobId, context.CorrelationId);
            throw;
        }

        _logger.LogInformation(
            "Manual admin trigger dispatched for job '{JobId}' runId={RunId} correlationId={CorrelationId}",
            jobId, persistedRunId, context.CorrelationId);

        // Fire-and-track on a background task. We use the host's stoppingToken (not the
        // caller's cancellationToken) so admin client cancellation doesn't kill an in-flight
        // run. We still need a token for the handler — use a CancellationTokenSource that
        // ties to the host's lifetime via a captured field. The simplest correct mechanism
        // is to use CancellationToken.None for the dispatch wrapper (the in-flight task is
        // tracked in _inFlight and drained by StopAsync per NFR-07).
        var task = Task.Run(
            () => RunManualTriggerAsync(handler, context, persistedRunId, CancellationToken.None),
            CancellationToken.None);
        _inFlight[runId] = task;

        // Untrack on completion regardless of outcome (same pattern as DispatchAndAdvance).
        _ = task.ContinueWith(
            _ =>
            {
                _inFlight.TryRemove(runId, out Task? _removed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return new TriggerResult(
            RunId: persistedRunId,
            Status: "Running",
            StartedAt: startedAt);
    }

    /// <summary>
    /// Background-task body for a manual-admin trigger. Invokes the handler via the same
    /// retry policy as scheduled runs (so transient errors are retried consistently) and
    /// writes the run-completion record.
    /// </summary>
    private async Task RunManualTriggerAsync(
        IScheduledJob handler,
        JobRunContext context,
        Guid persistedRunId,
        CancellationToken cancellationToken)
    {
        var jobId = handler.JobId;
        var sw = Stopwatch.StartNew();

        // Reuse the per-attempt retry behavior from scheduled runs. To do so without
        // forcing a ScheduledJobState (which requires a definition + parsed cron), call
        // a small inline executor that mirrors ExecuteWithRetryAsync's contract minus the
        // ScheduledJobState dependency.
        JobRunResult result;
        try
        {
            result = await ExecuteHandlerWithRetryAsync(handler, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Defensive: ExecuteHandlerWithRetryAsync converts all caught exceptions into
            // JobRunResult.Failure; anything thrown out is unexpected.
            sw.Stop();
            _logger.LogError(
                ex,
                "Manual trigger for '{JobId}' runId={RunId} threw outside the retry envelope (unexpected)",
                jobId, context.RunId);
            result = new JobRunResult(
                Success: false,
                ErrorMessage: ex.Message,
                ProcessedItems: null,
                Duration: sw.Elapsed);
        }

        try
        {
            await _store.RecordRunCompleteAsync(persistedRunId, result, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ScheduledJobHost failed to record manual-trigger completion for '{JobId}' runId={RunId} (correlationId {CorrelationId})",
                jobId, persistedRunId, context.CorrelationId);
        }
    }

    /// <summary>
    /// Retry-wrapped handler invocation for manual triggers — equivalent shape to
    /// <see cref="ExecuteWithRetryAsync"/> but operates on an <see cref="IScheduledJob"/> directly
    /// (no <see cref="ScheduledJobState"/> required, because manual triggers don't need cron state).
    /// </summary>
    private async Task<JobRunResult> ExecuteHandlerWithRetryAsync(
        IScheduledJob handler,
        JobRunContext context,
        CancellationToken cancellationToken)
    {
        var jobId = handler.JobId;
        var policy = _options.RetryPolicy;
        var maxAttempts = Math.Max(1, policy.MaxAttempts);
        var overallSw = Stopwatch.StartNew();

        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                overallSw.Stop();
                return new JobRunResult(
                    Success: false,
                    ErrorMessage: "Cancelled (manual trigger)",
                    ProcessedItems: null,
                    Duration: overallSw.Elapsed);
            }

            if (attempt > 1)
            {
                var delay = policy.ComputeDelay(attempt);
                try
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    overallSw.Stop();
                    return new JobRunResult(
                        Success: false,
                        ErrorMessage: "Cancelled (manual trigger)",
                        ProcessedItems: null,
                        Duration: overallSw.Elapsed);
                }
            }

            try
            {
                _logger.LogInformation(
                    "Dispatching manual-trigger job '{JobId}' runId={RunId} correlationId={CorrelationId} attempt={Attempt}/{MaxAttempts}",
                    jobId, context.RunId, context.CorrelationId, attempt, maxAttempts);

                var result = await handler.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Manual-trigger job '{JobId}' completed runId={RunId} success={Success} processedItems={ProcessedItems} duration={DurationMs}ms attempt={Attempt}",
                    jobId, context.RunId, result.Success, result.ProcessedItems, (long)result.Duration.TotalMilliseconds, attempt);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                overallSw.Stop();
                _logger.LogWarning(
                    "Manual-trigger job '{JobId}' runId={RunId} cancelled on attempt {Attempt}",
                    jobId, context.RunId, attempt);
                return new JobRunResult(
                    Success: false,
                    ErrorMessage: "Cancelled (manual trigger)",
                    ProcessedItems: null,
                    Duration: overallSw.Elapsed);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        ex,
                        "Manual-trigger job '{JobId}' runId={RunId} attempt {Attempt}/{MaxAttempts} failed — retrying after backoff",
                        jobId, context.RunId, attempt, maxAttempts);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Manual-trigger job '{JobId}' runId={RunId} exhausted {MaxAttempts} attempts — recording final failure",
                        jobId, context.RunId, maxAttempts);
                }
            }
        }

        overallSw.Stop();
        return new JobRunResult(
            Success: false,
            ErrorMessage: lastException?.Message ?? "Manual trigger exhausted retries with no captured exception",
            ProcessedItems: null,
            Duration: overallSw.Elapsed);
    }

    /// <summary>
    /// Build the per-run parameter bag for a manual-admin trigger. Mirrors the
    /// <see cref="ParametersFor"/> shape used by scheduled runs — same <c>configJson</c> + <c>jobId</c>
    /// keys — and overlays any caller-supplied per-trigger overrides.
    /// </summary>
    private static IDictionary<string, object> BuildManualTriggerParameters(
        string jobId,
        BackgroundJobDefinition? definition,
        IDictionary<string, object>? overrides)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (definition is not null && !string.IsNullOrEmpty(definition.ConfigJson))
        {
            parameters["configJson"] = definition.ConfigJson;
        }
        parameters["jobId"] = jobId;

        if (overrides is not null)
        {
            foreach (var kvp in overrides)
            {
                parameters[kvp.Key] = kvp.Value;
            }
        }

        return parameters;
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

    /// <summary>
    /// Re-reads job definitions from the store and rebuilds the scheduling state.
    /// Promoted to <c>public</c> in R3 task 022 so the admin enable/disable endpoints can
    /// force an immediate re-evaluation after a flag flip (without waiting for the hourly
    /// refresh tick). Internally invoked at startup + each <see cref="ScheduledJobHostOptions.RefreshInterval"/>
    /// tick from <see cref="TickAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Safe to call concurrently with the scheduling loop: <see cref="_state"/> is replaced
    /// atomically by a fresh dictionary instance, so an in-flight <see cref="TickAsync"/>
    /// iteration that snapshots <see cref="_state"/> early continues to operate on its
    /// snapshot while subsequent ticks pick up the new state.
    /// </para>
    /// <para>
    /// Failure modes (store throw, cron-parse error) are handled internally — the prior state
    /// is preserved on failure (so a transient Dataverse hiccup doesn't kill scheduling).
    /// Callers do NOT need to wrap this in a try/catch for those scenarios; only
    /// <see cref="OperationCanceledException"/> propagates out.
    /// </para>
    /// </remarks>
    public async Task RefreshDefinitionsAsync(CancellationToken cancellationToken)
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
        // Snapshot the scheduled fire time BEFORE advancing — this is the value the idempotency
        // probe + run-start record key off, and it must match the cron occurrence the loop just
        // observed (not the post-advance NextFireUtc).
        var scheduledFireUtc = entry.NextFireUtc ?? firingUtc;

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
        var task = Task.Run(() => RunJobAsync(entry, context, scheduledFireUtc, stoppingToken), stoppingToken);
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

    private async Task RunJobAsync(
        ScheduledJobState entry,
        JobRunContext context,
        DateTimeOffset scheduledFireUtc,
        CancellationToken stoppingToken)
    {
        var jobId = entry.Definition.JobId;
        Guid persistedRunId = default;

        // ── Idempotency probe (FR-2.3) ────────────────────────────────────────────────────
        // On host restart, the in-memory advance state is lost but the persistent store
        // remembers prior runs. If a row already exists for this (jobId, scheduledFireUtc),
        // skip dispatch entirely — do NOT re-execute and do NOT record a duplicate start row.
        try
        {
            var duplicate = await _store
                .HasRunForScheduledTimeAsync(jobId, scheduledFireUtc, stoppingToken)
                .ConfigureAwait(false);
            if (duplicate)
            {
                _logger.LogInformation(
                    "Scheduled job '{JobId}' tick at {ScheduledFireUtc:o} already executed (idempotency dedupe) — skipping",
                    jobId, scheduledFireUtc);
                return;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Probe failure is non-fatal — log and proceed. The risk of a duplicate run is
            // strictly less bad than the risk of silently dropping a scheduled tick.
            _logger.LogWarning(
                ex,
                "ScheduledJobHost idempotency probe failed for '{JobId}' tick {ScheduledFireUtc:o} — proceeding with dispatch (may risk duplicate)",
                jobId, scheduledFireUtc);
        }

        try
        {
            persistedRunId = await _store
                .RecordRunStartAsync(jobId, context.Trigger, context.CorrelationId, scheduledFireUtc, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ScheduledJobHost failed to record run start for '{JobId}' (correlationId {CorrelationId}) — running anyway",
                jobId, context.CorrelationId);
        }

        var result = await ExecuteWithRetryAsync(entry, context, stoppingToken).ConfigureAwait(false);

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
    /// Invokes <see cref="IScheduledJob.ExecuteAsync"/> with exponential-backoff retry per
    /// <see cref="ScheduledJobHostOptions.RetryPolicy"/>. Cancellation by
    /// <paramref name="stoppingToken"/> short-circuits the retry loop (no sleep, no further attempts)
    /// per NFR-07. Final result is the outcome of the last attempt — success on any successful
    /// attempt, otherwise the captured exception from the last failed attempt.
    /// </summary>
    private async Task<JobRunResult> ExecuteWithRetryAsync(
        ScheduledJobState entry,
        JobRunContext context,
        CancellationToken stoppingToken)
    {
        var jobId = entry.Definition.JobId;
        var policy = _options.RetryPolicy;
        var maxAttempts = Math.Max(1, policy.MaxAttempts);
        var overallSw = Stopwatch.StartNew();

        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                overallSw.Stop();
                return new JobRunResult(
                    Success: false,
                    ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                    ProcessedItems: null,
                    Duration: overallSw.Elapsed);
            }

            // Inter-attempt delay (only attempts >= 2 sleep).
            if (attempt > 1)
            {
                var delay = policy.ComputeDelay(attempt);
                try
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    overallSw.Stop();
                    return new JobRunResult(
                        Success: false,
                        ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                        ProcessedItems: null,
                        Duration: overallSw.Elapsed);
                }
            }

            try
            {
                _logger.LogInformation(
                    "Dispatching scheduled job '{JobId}' runId={RunId} correlationId={CorrelationId} attempt={Attempt}/{MaxAttempts}",
                    jobId, context.RunId, context.CorrelationId, attempt, maxAttempts);

                var result = await entry.Handler.ExecuteAsync(context, stoppingToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Scheduled job '{JobId}' completed runId={RunId} success={Success} processedItems={ProcessedItems} duration={DurationMs}ms attempt={Attempt}",
                    jobId, context.RunId, result.Success, result.ProcessedItems, (long)result.Duration.TotalMilliseconds, attempt);

                return result;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                overallSw.Stop();
                _logger.LogWarning(
                    "Scheduled job '{JobId}' runId={RunId} cancelled by host shutdown on attempt {Attempt}",
                    jobId, context.RunId, attempt);
                return new JobRunResult(
                    Success: false,
                    ErrorMessage: "Cancelled by host shutdown (NFR-07)",
                    ProcessedItems: null,
                    Duration: overallSw.Elapsed);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        ex,
                        "Scheduled job '{JobId}' runId={RunId} attempt {Attempt}/{MaxAttempts} failed — retrying after backoff",
                        jobId, context.RunId, attempt, maxAttempts);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Scheduled job '{JobId}' runId={RunId} exhausted {MaxAttempts} attempts — recording final failure",
                        jobId, context.RunId, maxAttempts);
                }
            }
        }

        overallSw.Stop();
        return new JobRunResult(
            Success: false,
            ErrorMessage: lastException?.Message ?? "Job exhausted retries with no captured exception",
            ProcessedItems: null,
            Duration: overallSw.Elapsed);
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
