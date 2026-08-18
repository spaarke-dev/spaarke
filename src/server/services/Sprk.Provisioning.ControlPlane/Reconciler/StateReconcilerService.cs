// -----------------------------------------------------------------------------
// StateReconcilerService.cs
//
// L2 CONTROL-PLANE state-reconciler BackgroundService (task 058, Wave C5).
//
// SPEC / DESIGN references:
//   - spec.md FR-22:      Handler execution model — L2 REST endpoints ENQUEUE
//                         via Service Bus + return 202 <100ms; state-reconciler
//                         BackgroundService polls Cosmos every 5s to advance
//                         DAG; handlers run in BFF's IJobHandler infrastructure
//                         (ADR-004) with 3-level idempotency.
//   - spec.md §4.2:       Custom state-machine over Cosmos; reconciler is
//                         the beating heart of L2.
//   - spec.md §4D I3/FR-30: Every Cosmos operation includes partition-key
//                         predicate. The cross-partition scan intentionally
//                         breaks this rule for orchestration — waived
//                         through IActiveRunScanner + AllowCrossPartitionScan.
//   - design.md §4.2:     Handler-execution model + concurrency safety —
//                         "multiple L2 instances may run reconciler concurrently;
//                         Cosmos optimistic-concurrency on sprk_currentrunid
//                         prevents duplicate enqueuing; the reconciler is
//                         idempotent: enqueuing the same handler twice results
//                         in Service Bus MessageId dedup + Redis idempotency
//                         lock catching the duplicate."
//
// ADR references:
//   - ADR-004 (Path A per CLAUDE.md §6.5 + spec.md ADR Tensions row 1): the
//                         reconciler is orchestration infrastructure, NOT
//                         itself an IJobHandler. Path A exception at L2 scope
//                         only.
//   - ADR-010:            Registered via ReconcilerModule extension method;
//                         no god-class DI expansion in Program.cs.
//   - ADR-032:            All service dependencies UNCONDITIONAL — no
//                         feature-gate branches for reconciler consumers.
//   - ADR-036:            3-level idempotency:
//                           Level 1 — Service Bus MessageId dedup (this
//                                     service's PRIMARY guard against
//                                     double-enqueue on concurrent L2
//                                     instances; enqueuer's ComputeMessageId
//                                     is deterministic per (HandlerId, RunId,
//                                     CustomerId, paramHash) so two ticks
//                                     produce IDENTICAL MessageId which SB
//                                     dedup retains as ONE message).
//                           Level 2 — BFF Redis IdempotencyService lock
//                                     (per-process guard on dequeue).
//                           Level 3 — Cosmos ETag on run doc + Dataverse
//                                     alt-key upsert (per-handler durable
//                                     dedup; owned by each handler).
//                         The reconciler itself DOES NOT write to Cosmos —
//                         handlers own state-transition writes. This keeps
//                         the reconciler ETag-race-free: two concurrent
//                         reconciler instances read the same ProvisioningRun
//                         snapshot, compute the same ready-handler set (pure
//                         function of CompletedPhases), enqueue identical
//                         envelopes with identical MessageIds, and Service
//                         Bus dedup collapses them into one dispatch.
//
// TIME DISCIPLINE (docs/standards/TEST-ARCHITECTURE.md §4):
//   MUST use TimeProvider (injected) — NEVER Stopwatch or DateTime.UtcNow.
//   Verified by the POML step 8 grep gate: 0 hits for DateTime.UtcNow /
//   Stopwatch inside src/server/services/Sprk.Provisioning.ControlPlane/
//   Reconciler/. The <see cref="TimeProvider.CreatePeriodicTimer"/> factory
//   returns a PeriodicTimer that Advances on the TimeProvider's clock, so
//   FakeTimeProvider-style tests can Advance(interval) to force a tick
//   without wall-clock delay.
//
// CRASH-SAFETY (POML acceptance #6 + design.md §4.2):
//   The reconciler MUST NOT crash the L2 App Service on Cosmos outage. Each
//   tick catches Exception, logs a warning, and continues on the next
//   interval. CancellationToken from BackgroundService's stoppingToken is
//   the ONE observable OperationCanceledException that propagates —
//   graceful shutdown.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// BackgroundService that polls Cosmos every <see cref="ReconcilerOptions.PollInterval"/>
/// (default 5s) for active runs and enqueues the next ready-to-dispatch
/// handlers via Service Bus. Load-bearing concurrent-safe state machine —
/// see file header for the 3-level idempotency contract and the rationale
/// for NOT writing to Cosmos from this service.
/// </summary>
public sealed class StateReconcilerService : BackgroundService
{
    /// <summary>
    /// Stable log-event prefix on the reconciler's dispatch record. Kusto
    /// queries in App Insights `traces` pivot on this to isolate reconciler
    /// enqueues from endpoint-layer enqueues.
    /// </summary>
    public const string DispatchedEventName = "ReconcilerDispatched";

    /// <summary>
    /// Serializer for the enqueue payload. camelCase parity with
    /// <see cref="ServiceBusHandlerEnqueuer"/> + Cosmos wire — required so the
    /// deterministic ParametersJson (an ingredient of the MessageId hash) is
    /// stable across L2 instances.
    /// </summary>
    private static readonly JsonSerializerOptions ParametersJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReconcilerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StateReconcilerService> _logger;

    /// <summary>
    /// Constructs the reconciler. Every collaborator is resolved from a
    /// per-tick DI scope (see <see cref="ExecuteAsync"/>) so scoped
    /// registrations (<see cref="IHandlerEnqueuer"/> is Scoped per
    /// ServiceBusModule; <see cref="IActiveRunScanner"/> is Scoped per
    /// ReconcilerModule) resolve correctly under the BackgroundService's
    /// singleton lifetime.
    /// </summary>
    public StateReconcilerService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReconcilerOptions> options,
        TimeProvider timeProvider,
        ILogger<StateReconcilerService> logger)
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "StateReconcilerService is disabled (Reconciler:Enabled=false); ExecuteAsync exiting cleanly.");
            return;
        }

        _logger.LogInformation(
            "StateReconcilerService starting. PollIntervalSeconds={PollIntervalSeconds}.",
            (int)_options.PollInterval.TotalSeconds);

        // TimeProvider-derived PeriodicTimer — deterministic in FakeTimeProvider
        // tests via time.Advance(interval); production uses TimeProvider.System.
        // The first WaitForNextTickAsync waits one full interval so the app has
        // time to complete startup + register scoped services cleanly before
        // the first tick fires.
        using var timer = new PeriodicTimer(_options.PollInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunTickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — do NOT log as error. Fall through to the
            // stopping log below.
        }

        _logger.LogInformation("StateReconcilerService stopping.");
    }

    /// <summary>
    /// Executes a single reconciler tick. Exposed as <c>internal</c> so unit
    /// tests can drive it directly without spinning a PeriodicTimer, which
    /// makes deterministic assertion of the enqueue-side effect trivial.
    /// </summary>
    /// <remarks>
    /// Wraps EVERY tick in a broad try/catch so a Cosmos outage, Service Bus
    /// throttle, or any transient failure does NOT crash the L2 App Service
    /// (POML acceptance criterion 6). The next tick retries.
    /// </remarks>
    internal async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<IActiveRunScanner>();
            var advancer = scope.ServiceProvider.GetRequiredService<IDagAdvancer>();
            var enqueuer = scope.ServiceProvider.GetRequiredService<IHandlerEnqueuer>();

            var activeRuns = await scanner.QueryActiveRunsAsync(cancellationToken).ConfigureAwait(false);
            if (activeRuns.Count == 0)
            {
                _logger.LogDebug("Reconciler tick: no active runs.");
                return;
            }

            _logger.LogDebug(
                "Reconciler tick: {ActiveRunCount} active run(s) under evaluation.",
                activeRuns.Count);

            foreach (var run in activeRuns)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                await AdvanceRunAsync(run, advancer, enqueuer, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NEVER let a tick crash the L2 App Service.
            _logger.LogWarning(
                ex,
                "Reconciler tick failed; will retry on next {PollIntervalSeconds}s interval.",
                (int)_options.PollInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// Enqueues each ready-to-dispatch handler for the given run. Per-handler
    /// enqueue failures are logged and skipped so one bad handler doesn't
    /// stall the tick for its siblings.
    /// </summary>
    private async Task AdvanceRunAsync(
        ProvisioningRun run,
        IDagAdvancer advancer,
        IHandlerEnqueuer enqueuer,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> ready;
        try
        {
            ready = advancer.ComputeReadyHandlers(run);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Reconciler failed to compute ready handlers for run {RunId} (customer {CustomerId}); skipping this tick.",
                run.RunId, run.CustomerId);
            return;
        }

        if (ready.Count == 0)
        {
            _logger.LogDebug(
                "Reconciler: no ready-to-dispatch handlers for run {RunId} (currentPhase={CurrentPhase}, completed={CompletedCount}).",
                run.RunId, run.CurrentPhase ?? "(none)", run.CompletedPhases.Count);
            return;
        }

        foreach (var handlerId in ready)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var envelope = BuildEnvelope(handlerId, run);
                await enqueuer.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    DispatchedEventName +
                    ": HandlerId={HandlerId} RunId={RunId} CustomerId={CustomerId} " +
                    "CompletedPhaseCount={CompletedPhaseCount}",
                    handlerId, run.RunId, run.CustomerId, run.CompletedPhases.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Reconciler failed to enqueue {HandlerId} for run {RunId} (customer {CustomerId}); will retry next tick.",
                    handlerId, run.RunId, run.CustomerId);
            }
        }
    }

    /// <summary>
    /// Builds the dispatch envelope for a reconciler-initiated handler
    /// enqueue. The payload is deterministic: same run + same handler ID
    /// produces identical ParametersJson bytes, so
    /// <see cref="ServiceBusHandlerEnqueuer.ComputeMessageId"/> yields the
    /// same MessageId across concurrent reconciler instances — Service Bus
    /// dedup collapses them into one message (level-1 idempotency).
    /// </summary>
    internal HandlerEnvelope BuildEnvelope(string handlerId, ProvisioningRun run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        ArgumentNullException.ThrowIfNull(run);

        var payload = new ReconcilerEnqueuePayload
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
    /// Deterministic envelope payload for reconciler-initiated dispatches.
    /// Handlers read run parameters from Cosmos via <see cref="Repositories.IProvisioningRunRepository"/>
    /// (not from the envelope) — this payload is only routing/observability
    /// metadata. Byte-stable so the derived MessageId is stable.
    /// </summary>
    internal sealed record ReconcilerEnqueuePayload
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
