// -----------------------------------------------------------------------------
// CrashRecoveryStartupService.cs
//
// L2 CONTROL-PLANE I6 crash-recovery startup scan (task 060, Wave C5).
//
// SPEC / DESIGN references:
//   - spec.md FR-23:      "on startup L2 scans Cosmos for status ∈ {Running,
//                         WaitingOnGate} older than 2× median-handler-duration
//                         + re-schedules from currentPhase."
//   - design.md §4.2:     "Crash recovery (I6 resolved v3): On startup, L2
//                         scans Cosmos for status ∈ {Running, WaitingOnGate}
//                         runs older than 2× median-handler-duration. For each
//                         orphaned run, L2 emits an IJobHandler job to resume
//                         from currentPhase. Handlers are idempotent (three-
//                         level: MessageId dedup + Redis idempotency lock +
//                         deterministic idempotency key per §4.1), so a
//                         duplicate-resume post-crash is safe."
//   - design.md §16 I6:   Invariant row — "On startup, scan Cosmos for
//                         orphaned Running/WaitingOnGate runs older than 2×
//                         median handler duration; re-schedule from
//                         currentPhase".
//
// ADR references:
//   - ADR-004 (Path A per CLAUDE.md §6.5 + spec.md ADR Tensions row 1): the
//                         crash-recovery scan is orchestration infrastructure,
//                         NOT itself an IJobHandler. Path A exception at L2
//                         scope only, sibling of the state-reconciler.
//   - ADR-010:            Registered directly in Program.cs alongside the
//                         reconciler; no god-class DI expansion.
//   - ADR-032:            All service dependencies UNCONDITIONAL — the kill-
//                         switch is a config flag (CrashRecovery:Enabled)
//                         that suppresses the SCAN, not the DI registration.
//   - ADR-036:            3-level idempotency:
//                           Level 1 — Service Bus MessageId dedup. This
//                                     service's re-enqueue envelope has
//                                     IDENTICAL wire bytes to what the
//                                     reconciler would produce for the same
//                                     (HandlerId, RunId, CustomerId) — the
//                                     ParametersJson payload record has the
//                                     same Action = "reconciler-advance" tag
//                                     and camelCase policy, so
//                                     ServiceBusHandlerEnqueuer.ComputeMessageId
//                                     yields the SAME MessageId whether the
//                                     dispatch came from the startup scan or
//                                     the ongoing 5s reconciler tick. Service
//                                     Bus dedup collapses them to one message
//                                     in the (production-configured) dedup
//                                     window.
//                           Level 2 — BFF Redis IdempotencyService lock
//                                     (per-process guard on dequeue) — safe
//                                     against a duplicate that slips outside
//                                     the SB dedup window.
//                           Level 3 — Cosmos ETag on run doc + Dataverse
//                                     alt-key upsert (per-handler durable
//                                     dedup owned by each handler) — safe
//                                     even if L1 + L2 both miss.
//                         This layered safety net is why re-enqueueing
//                         currentPhase is safe under ALL crash timings —
//                         mid-flight, post-write, or immediately after
//                         message-ack.
//
// SEQUENCING vs THE RECONCILER:
//   The reconciler (StateReconcilerService, task 058) polls every 5s and
//   dispatches handlers whose upstream deps are satisfied but which are not
//   in CompletedPhases. For an orphan run whose currentPhase has NOT yet
//   completed (the typical crash case), the reconciler would ALSO enqueue
//   that handler on its first tick — so this startup scan is fundamentally a
//   FASTER (and observability-explicit) resume that catches orphans in the
//   5-second window before the first reconciler tick fires. The dedup
//   contract above ensures the redundant dispatch is a no-op at the SB
//   wire, not a duplicate handler execution.
//
// AGE PROXY — "lastUpdated":
//   The ProvisioningRun POCO does NOT surface Cosmos's system _ts field, and
//   the L2 project explicitly does not write partial state from any
//   orchestration path — only handlers write to Cosmos on completion (per
//   StateReconcilerService file header). Therefore the "last-updated" moment
//   for a run is the greater of:
//     - MAX(run.CompletedPhases[*].CompletedAt) — the last successful phase
//       transition, OR
//     - run.CreatedOn                          — for a run whose first
//       handler crashed before completing (CompletedPhases is empty).
//   If neither indicates age > threshold, the run is NOT considered orphaned
//   even if scanner returned it (the reconciler already owns advancement).
//
// TIME DISCIPLINE (docs/standards/TEST-ARCHITECTURE.md §4):
//   MUST use TimeProvider (injected) — NEVER Stopwatch or DateTime.UtcNow.
//   Verified by the POML step 8 grep gate: 0 hits for DateTime.UtcNow /
//   Stopwatch inside src/server/services/Sprk.Provisioning.ControlPlane/
//   Reconciler/CrashRecoveryStartupService.cs.
//
// CRASH-SAFETY (POML acceptance #6 + design.md §4.2):
//   The startup scan MUST NOT crash the L2 App Service on Cosmos outage or
//   Service Bus throttle. StartAsync catches Exception, logs a warning, and
//   returns — the reconciler's ongoing 5s tick loop will pick up any
//   still-orphaned runs on the first tick. CancellationToken from
//   IHostedService's stoppingToken IS observed and propagates cancellation
//   cleanly.
//
// TASK 059 (I5) INTEGRATION:
//   Per the parent brief, once task 059 wires ICustomerRunGuard into the
//   dispatch path (typically as a decorator on IHandlerEnqueuer), this
//   service's re-enqueues will transitively respect the same-customer
//   serialization guard because they route through the SAME IHandlerEnqueuer
//   the reconciler uses. This service does NOT directly consume
//   ICustomerRunGuard.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// One-shot startup scan that re-enqueues currentPhase for any Cosmos run in
/// <see cref="RunStatus.Running"/> or <see cref="RunStatus.WaitingOnGate"/>
/// whose last-activity age exceeds
/// <c>MAX(2× MedianHandlerDuration, FloorAge)</c>. Implements FR-23 I6 crash
/// recovery — the safety net that resumes orphaned runs when the L2 App
/// Service crashes, slot-swaps, or is autoscaled mid-handler.
/// </summary>
public sealed class CrashRecoveryStartupService : IHostedService
{
    /// <summary>
    /// Stable log-event prefix on the crash-recovery re-enqueue record. Kusto
    /// queries in App Insights <c>traces</c> pivot on this to isolate startup-
    /// recovery dispatches from ongoing reconciler dispatches
    /// (<see cref="StateReconcilerService.DispatchedEventName"/>).
    /// </summary>
    public const string ReEnqueuedEventName = "CrashRecoveryReEnqueued";

    /// <summary>
    /// Stable log-event prefix on the per-startup summary line. One emission
    /// per L2 boot per Enabled configuration.
    /// </summary>
    public const string ScanCompleteEventName = "CrashRecoveryScanComplete";

    /// <summary>
    /// Serializer for the re-enqueue payload. camelCase parity with
    /// <see cref="ServiceBusHandlerEnqueuer"/> + Cosmos wire + the
    /// reconciler's payload serializer — required so the deterministic
    /// ParametersJson (an ingredient of the MessageId hash) is byte-for-byte
    /// identical to what the reconciler would produce for the same handler,
    /// which is what makes SB MessageId dedup collapse a startup-scan
    /// re-enqueue and a subsequent reconciler tick into one message.
    /// </summary>
    private static readonly JsonSerializerOptions ParametersJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CrashRecoveryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CrashRecoveryStartupService> _logger;

    /// <summary>
    /// Constructs the crash-recovery startup service. Every collaborator is
    /// resolved from a per-invocation DI scope (see <see cref="RunOnceAsync"/>)
    /// so scoped registrations (<see cref="IHandlerEnqueuer"/> is Scoped per
    /// ServiceBusModule; <see cref="IActiveRunScanner"/> is Scoped per
    /// ReconcilerModule) resolve correctly under the IHostedService's
    /// singleton lifetime.
    /// </summary>
    public CrashRecoveryStartupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CrashRecoveryOptions> options,
        TimeProvider timeProvider,
        ILogger<CrashRecoveryStartupService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "CrashRecoveryStartupService is disabled (CrashRecovery:Enabled=false); startup scan skipped.");
            return;
        }

        _logger.LogInformation(
            "CrashRecoveryStartupService starting one-shot scan. FloorAgeSeconds={FloorAgeSeconds} MedianHandlerDurationSeconds={MedianHandlerDurationSeconds} ThresholdSeconds={ThresholdSeconds}.",
            (long)_options.FloorAge.TotalSeconds,
            (long)_options.MedianHandlerDuration.TotalSeconds,
            (long)ComputeThreshold().TotalSeconds);

        try
        {
            await RunOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host is shutting down mid-startup — propagate to abort further hosted-service start.
            throw;
        }
        catch (Exception ex)
        {
            // NEVER crash the L2 App Service on Cosmos outage or SB throttle.
            // The reconciler's ongoing 5s tick loop is the fallback — it will
            // pick up any still-orphaned runs on its first successful tick.
            _logger.LogWarning(
                ex,
                "Crash-recovery startup scan failed; the state-reconciler will re-attempt DAG advancement on its next {PollIntervalSeconds}s tick.",
                5);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Executes a single crash-recovery pass. Exposed as <c>internal</c> so
    /// unit tests can drive it directly without going through
    /// <see cref="StartAsync"/> — parity with
    /// <see cref="StateReconcilerService.RunTickAsync"/>.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<IActiveRunScanner>();
        var enqueuer = scope.ServiceProvider.GetRequiredService<IHandlerEnqueuer>();

        var activeRuns = await scanner.QueryActiveRunsAsync(cancellationToken).ConfigureAwait(false);
        if (activeRuns.Count == 0)
        {
            _logger.LogInformation(
                ScanCompleteEventName +
                ": ActiveRunCount=0 OrphanCount=0 ThresholdSeconds={ThresholdSeconds}.",
                (long)ComputeThreshold().TotalSeconds);
            return;
        }

        var threshold = ComputeThreshold();
        var nowUtc = _timeProvider.GetUtcNow();
        var orphanCount = 0;
        var reEnqueuedCount = 0;

        foreach (var run in activeRuns)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Defense-in-depth: the scanner filter should already exclude
            // terminal statuses, but we double-check here so a race where
            // status advanced between projection and read cannot re-dispatch
            // a completed pipeline. Mirrors the reconciler's own defense-in-
            // depth check in DagAdvancer.ComputeReadyHandlers.
            if (run.Status is RunStatus.Completed
                or RunStatus.Failed
                or RunStatus.Cancelled
                or RunStatus.Quarantined)
            {
                continue;
            }

            var lastActivity = GetLastActivity(run);
            var age = nowUtc - lastActivity;
            if (age < threshold)
            {
                continue; // Still within the "active reconciler owns it" window.
            }

            orphanCount++;

            if (string.IsNullOrWhiteSpace(run.CurrentPhase))
            {
                // Rare: run reached Running/WaitingOnGate but currentPhase was
                // never populated (e.g. initial-provision hand-off crashed
                // before the endpoint layer set it). Skip — the reconciler
                // will re-derive readiness from CompletedPhases on its next
                // tick. Log at WARNING so operators can spot the anomaly.
                _logger.LogWarning(
                    "Crash recovery: orphan run {RunId} (customer {CustomerId}) has null CurrentPhase after {AgeSeconds}s; skipping re-enqueue — reconciler will re-derive readiness from CompletedPhases.",
                    run.RunId, run.CustomerId, (long)age.TotalSeconds);
                continue;
            }

            try
            {
                var envelope = BuildEnvelope(run.CurrentPhase, run);
                await enqueuer.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
                reEnqueuedCount++;

                _logger.LogInformation(
                    ReEnqueuedEventName +
                    ": HandlerId={HandlerId} RunId={RunId} CustomerId={CustomerId} " +
                    "AgeSeconds={AgeSeconds} CompletedPhaseCount={CompletedPhaseCount}",
                    run.CurrentPhase, run.RunId, run.CustomerId,
                    (long)age.TotalSeconds, run.CompletedPhases.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Per-run failure is contained so one bad orphan doesn't
                // block resumption of its siblings — parity with the
                // reconciler's per-handler try/catch in AdvanceRunAsync.
                _logger.LogWarning(
                    ex,
                    "Crash recovery failed to re-enqueue phase {PhaseId} for run {RunId} (customer {CustomerId}); reconciler will retry on next tick.",
                    run.CurrentPhase, run.RunId, run.CustomerId);
            }
        }

        _logger.LogInformation(
            ScanCompleteEventName +
            ": ActiveRunCount={ActiveRunCount} OrphanCount={OrphanCount} ReEnqueuedCount={ReEnqueuedCount} ThresholdSeconds={ThresholdSeconds}.",
            activeRuns.Count, orphanCount, reEnqueuedCount, (long)threshold.TotalSeconds);
    }

    /// <summary>
    /// Effective orphan-age threshold — <c>MAX(2× MedianHandlerDuration, FloorAge)</c>.
    /// Exposed as <c>internal</c> for unit-test coverage of the max-clause.
    /// </summary>
    internal TimeSpan ComputeThreshold()
    {
        // Overflow-safe: MedianHandlerDuration is validated >= 1s, so doubling
        // stays well under TimeSpan.MaxValue for any realistic operator input.
        var twiceMedian = TimeSpan.FromTicks(_options.MedianHandlerDuration.Ticks * 2L);
        return twiceMedian > _options.FloorAge ? twiceMedian : _options.FloorAge;
    }

    /// <summary>
    /// Age proxy for a run — the greatest of the last completed-phase timestamp
    /// (if any) and <see cref="ProvisioningRun.CreatedOn"/>. Exposed as
    /// <c>internal</c> so the age-boundary test cases can construct concrete
    /// runs and assert on the exact proxy the service uses.
    /// </summary>
    internal static DateTimeOffset GetLastActivity(ProvisioningRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.CompletedPhases.Count == 0)
        {
            return run.CreatedOn;
        }

        // Use Max rather than Last to be defensive against out-of-order writes
        // (should never happen given the reconciler's append-only contract on
        // CompletedPhases, but the cost is trivial and it makes the age proxy
        // provably a lower bound on document last-updated time).
        DateTimeOffset latest = run.CreatedOn;
        foreach (var phase in run.CompletedPhases)
        {
            if (phase.CompletedAt > latest)
            {
                latest = phase.CompletedAt;
            }
        }
        return latest;
    }

    /// <summary>
    /// Builds the dispatch envelope for a crash-recovery re-enqueue. Payload
    /// bytes are DELIBERATELY byte-for-byte identical to what
    /// <c>StateReconcilerService.BuildEnvelope</c> would produce for the same
    /// (handlerId, run) — same camelCase policy, same Action tag
    /// ("reconciler-advance"), same field ordering. This is what makes
    /// <see cref="ServiceBusHandlerEnqueuer.ComputeMessageId"/> yield an
    /// IDENTICAL MessageId between a startup-scan re-enqueue and the
    /// reconciler's next-tick dispatch, so Service Bus dedup collapses them
    /// (level-1 idempotency per ADR-036).
    /// </summary>
    /// <remarks>
    /// The <c>Action = "reconciler-advance"</c> tag is intentional (NOT
    /// "crash-recovery-resume"): a distinct action tag would produce a
    /// distinct MessageId and defeat the SB-dedup collapse. Observability of
    /// the crash-recovery origin is preserved by the
    /// <see cref="ReEnqueuedEventName"/> log line above — that emits a
    /// distinct event whose Kusto lookup does not depend on the envelope
    /// payload.
    /// </remarks>
    internal HandlerEnvelope BuildEnvelope(string handlerId, ProvisioningRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        ArgumentNullException.ThrowIfNull(run);

        var payload = new ReconcilerAlignedPayload
        {
            CustomerId = run.CustomerId,
            RunId = run.RunId,
            Action = "reconciler-advance",
            HandlerId = handlerId,
        };

        return new HandlerEnvelope
        {
            HandlerId = handlerId,
            RunId = run.RunId,
            CustomerId = run.CustomerId,
            ParametersJson = JsonSerializer.Serialize(payload, ParametersJsonOptions),
            EnqueuedAt = _timeProvider.GetUtcNow(),
        };
    }

    /// <summary>
    /// Payload record that mirrors the reconciler's internal
    /// <c>ReconcilerEnqueuePayload</c> byte-for-byte. Duplicated here (rather
    /// than shared) because the task 060 scope is "add alongside, don't touch
    /// existing reconciler files" and the record is 4 fields of JSON metadata
    /// — the duplication cost is trivial and the extraction cost would be a
    /// coordinated PR with sibling tasks 059 + 061 also modifying Reconciler/.
    /// </summary>
    internal sealed record ReconcilerAlignedPayload
    {
        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("runId")]
        public string RunId { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; init; } = string.Empty;

        [JsonPropertyName("handlerId")]
        public string HandlerId { get; init; } = string.Empty;
    }
}
