// -----------------------------------------------------------------------------
// ProvisioningHandlerDispatcher.cs
//
// L2 CONTROL-PLANE session-serialized handler dispatch BackgroundService
// (task 102, Phase C'' Wave G-1 -- THE load-bearing execution engine that
// r1's original shipping "completed" without).
//
// PURPOSE:
//   Drains the fleet-scoped SESSION-ENABLED queue `sprk-provisioning-jobs`,
//   resolves the IProvisioningHandler by envelope HandlerId via KEYED DI
//   (task 103), invokes it, and applies the outcome via IHandlerOutcomeApplier
//   (task 104). Session serialization gives single-writer-per-customer
//   Cosmos semantics across the 41 ETag-guarded ReplaceRunAsync call sites
//   -- see DS-2b Sec.0-1 for the write-shape safety argument.
//
// MIRROR-AND-DIVERGE FROM BFF ServiceBusJobProcessor (DS-2 §1.5 table):
//   MIRROR:
//     - BackgroundService + processor + events shape.
//     - Per-message DI scope (using var scope = ...CreateScope()) so Scoped
//       IProvisioningRunRepository / IHandlerOutcomeApplier / keyed
//       IProvisioningHandler resolve correctly under the Singleton
//       BackgroundService lifetime.
//     - AutoCompleteMessages = false, PeekLock, unhandled-exception
//       dead-letter chain, defensive StopAsync + Dispose in finally with
//       ObjectDisposedException tolerance.
//
//   DIVERGE (DS-2 §1.5 explicit deltas):
//     - Session-aware processor (see ExecuteAsync) instead of BFF's plain
//       non-session processor -- per-customer FIFO + single-writer-per-run
//       Cosmos safety (SessionId = CustomerId set by
//       ServiceBusHandlerEnqueuer.cs:140).
//     - MaxConcurrentCallsPerSession = 1 HARD-CODED (see FreezeMe const
//       below). This is a CORRECTNESS invariant per DS-2b R3, protected by
//       ProvisioningHandlerDispatcherInvariantTests. NOT a tuning knob.
//     - MaxAutoLockRenewalDuration = DispatcherOptions.MaxHandlerDuration
//       (default 65 min for the H6/H9 poles) -- BFF's 10 min is
//       structurally insufficient for a 30-min handler.
//     - Keyed DI handler resolution (GetKeyedService<IProvisioningHandler>
//       by envelope.HandlerId) -- avoids constructing all 19 handler graphs
//       on every message + isolates DI faults per handler (BFF's
//       enumerate-and-match poisons every dispatch on one broken graph;
//       DS-2 §3.2 rejected alternative).
//     - Retry authority is §4C RollbackTransitions inside IHandlerOutcomeApplier,
//       NOT the SB Abandon-loop. The dispatcher COMPLETES the message once
//       the outcome (Success OR applied-Failure) has landed in Cosmos; the
//       re-dispatch is a fresh enqueue with incremented HandlerEnvelope.Attempt
//       (task 107) -- an Abandon+§4C-re-enqueue combo would double-retry.
//     - Level-2 idempotency (IDispatchIdempotencyService) LIVES IN THE
//       DEQUEUE PATH here -- the BFF's IdempotencyService lives inside
//       handler bodies. See DS-2 §4-L2.
//     - PrefetchCount = 0 -- never prefetch for long handlers; a prefetched
//       message whose lock renewal fails during a 30-min body is a
//       redelivery for no reason (BFF processor allows prefetching
//       implicitly via SDK defaults for short handlers).
//
// EIGHT-STEP FLOW (DS-2 §2.5 verbatim; encoded in DispatchCoreAsync):
//   1  Deserialize envelope; null / exception -> DeadLetter(InvalidFormat).
//   2  Resolve keyed handler; null -> DeadLetter(NoHandler);
//      DI construction throws -> DeadLetter(HandlerResolutionFailed).
//   3  Level-2 idempotency gate (deterministic messageId = recomputed via
//      ServiceBusHandlerEnqueuer.ComputeMessageId): IsProcessed -> Complete;
//      !TryAcquireLock -> Abandon.
//   4  Read run (partitionKey = envelope.CustomerId); null -> DeadLetter(OrphanRun);
//      terminal-status run (Cancelled | Quarantined | Completed) -> Complete-without-invoke.
//   5  Invoke handler; unhandled exception -> HandlerResult.Failure via
//      FailureClassifier.ClassifyException.
//   6  Re-read run (fresh ETag), then apply outcome via IHandlerOutcomeApplier.
//      Cosmos unreachable -> Abandon; DeliveryCount >= MaxDeliveryCount ->
//      DeadLetter(OutcomeApplyFailed).
//   7  MarkProcessed(messageId); ReleaseLock(messageId).
//   8  Complete the message -- for BOTH Success and applied-Failure (see
//      MIRROR-AND-DIVERGE retry-authority note above).
//
// CANCELLATION + SHUTDOWN (DS-2 §2.6):
//   App Service graceful shutdown (~30s, extendable via
//   WEBSITES_CONTAINER_STOP_TIME_LIMIT) can NEVER drain a 30-min handler.
//   Design does not pretend to: interrupted handler's message lock expires
//   -> SB redelivers -> Level-2 Redis lock expired too -> re-invoke ->
//   Level-3 CompletedPhases dedup no-ops the completed portion.
//   CrashRecoveryStartupService (task 060) independently re-enqueues
//   currentPhase for orphaned Running runs on next boot.
//
// PROJECT PLACEMENT (CLAUDE.md §10 / §11):
//   .Worker only -- shared-infra types (options, decision record, seams,
//   registration extension) all live in .Core/Dispatch/. This class is
//   pure hosting code (BackgroundService lifecycle + wire wrapping) that
//   the Worker's Program.cs registers via
//   builder.Services.AddHostedService<ProvisioningHandlerDispatcher>()
//   immediately after AddDispatchModule. NO reference to Sprk.Bff.Api
//   types (project MUST rule; L2 is a PEER service, not a BFF extension).
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Worker.Dispatch;

/// <summary>
/// Session-serialized handler-dispatch BackgroundService. See file header
/// for the mirror-and-diverge relationship to BFF's ServiceBusJobProcessor
/// and the DS-2 §2.5 eight-step flow this class encodes.
/// </summary>
public sealed class ProvisioningHandlerDispatcher : BackgroundService
{
    /// <summary>
    /// Stable log-event prefix on the dispatcher's success record. Kusto
    /// queries in App Insights `traces` pivot on this to isolate dispatcher
    /// outcomes from reconciler enqueues + endpoint enqueues.
    /// </summary>
    public const string DispatchOutcomeEventName = "DispatcherOutcome";

    /// <summary>
    /// DS-2b Sec.9 R3 HARD-CODED correctness invariant:
    /// <c>ServiceBusSessionProcessorOptions.MaxConcurrentCallsPerSession</c>
    /// MUST be 1 so a customer's DAG-parallel branches execute strictly
    /// one-at-a-time and the 41 ETag-guarded Cosmos write sites never
    /// race intra-run. NOT a tuning parameter. Any config-driven attempt
    /// to widen this is a design violation -- see DispatcherOptions file
    /// header. Protected by ProvisioningHandlerDispatcherInvariantTests.
    /// </summary>
    internal const int MaxConcurrentCallsPerSessionInvariant = 1;

    private static readonly JsonSerializerOptions BodyDeserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatcherOptions _options;
    private readonly ServiceBusModuleOptions _sbOptions;

    // TimeProvider is injected for TEST-ARCHITECTURE §4 discipline parity with
    // StateReconcilerService / HandlerOutcomeApplier / CrashRecoveryStartupService
    // -- currently unused inside DispatchCoreAsync (Cosmos writes use TimeProvider
    // via HandlerOutcomeApplier; the dispatcher itself has no wall-clock decision
    // point), but retained on the ctor per DS-2 §2.2 shape so any future latency
    // metric / handler-duration measurement is a one-line change without a ctor
    // rev.
#pragma warning disable IDE0052 // Remove unread private members - retained per DS-2 §2.2 shape
    private readonly TimeProvider _timeProvider;
#pragma warning restore IDE0052

    private readonly ILogger<ProvisioningHandlerDispatcher> _logger;

    private ServiceBusSessionProcessor? _processor;

    /// <summary>
    /// Constructs the dispatcher. All dependencies are DI-owned; the class
    /// owns only the <see cref="ServiceBusSessionProcessor"/> it creates in
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    public ProvisioningHandlerDispatcher(
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        IOptions<DispatcherOptions> options,
        IOptions<ServiceBusModuleOptions> sbOptions,
        TimeProvider timeProvider,
        ILogger<ProvisioningHandlerDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceBusClient);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sbOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceBusClient = serviceBusClient;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _sbOptions = sbOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Builds the ServiceBusSessionProcessorOptions the dispatcher will use.
    /// Extracted as a static helper so
    /// <c>ProvisioningHandlerDispatcherInvariantTests.MaxConcurrentCallsPerSession_IsHardCodedToOne</c>
    /// can assert on <see cref="ServiceBusSessionProcessorOptions.MaxConcurrentCallsPerSession"/>
    /// deterministically WITHOUT constructing a live session processor or
    /// touching a live Service Bus wire (DS-2b R3 forcing function).
    /// </summary>
    /// <param name="options">Bound <see cref="DispatcherOptions"/>.</param>
    /// <returns>The session-processor options block used by <see cref="ExecuteAsync"/>.</returns>
    internal static ServiceBusSessionProcessorOptions BuildSessionProcessorOptions(DispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ServiceBusSessionProcessorOptions
        {
            MaxConcurrentSessions = options.MaxConcurrentCustomers,
            MaxConcurrentCallsPerSession = MaxConcurrentCallsPerSessionInvariant, // HARD-CODED per DS-2b R3.
            AutoCompleteMessages = false,
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            MaxAutoLockRenewalDuration = options.MaxHandlerDuration,
            SessionIdleTimeout = options.SessionIdleTimeout,
            PrefetchCount = 0,
        };
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "ProvisioningHandlerDispatcher is disabled (Dispatcher:Enabled=false); ExecuteAsync exiting cleanly.");
            return;
        }

        _logger.LogInformation(
            "ProvisioningHandlerDispatcher starting. Queue={Queue} MaxConcurrentSessions={MaxSessions} " +
            "MaxHandlerDurationMinutes={MaxHandlerMinutes} SessionIdleTimeoutSeconds={IdleSeconds} " +
            "MaxDeliveryCount={MaxDeliveryCount}",
            _sbOptions.QueueName,
            _options.MaxConcurrentCustomers,
            (int)_options.MaxHandlerDuration.TotalMinutes,
            (int)_options.SessionIdleTimeout.TotalSeconds,
            _options.MaxDeliveryCount);

        try
        {
            _processor = _serviceBusClient.CreateSessionProcessor(
                _sbOptions.QueueName,
                BuildSessionProcessorOptions(_options));

            _processor.ProcessMessageAsync += OnSessionMessageAsync;
            _processor.ProcessErrorAsync += OnErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("ProvisioningHandlerDispatcher started successfully.");

            // Park until shutdown -- the session processor drives all work
            // via the ProcessMessageAsync event.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ProvisioningHandlerDispatcher stopping (host cancellation).");
        }
        catch (Exception ex)
        {
            // A failure to Start (e.g. queue does not exist, queue is not
            // session-enabled) is fatal to the Worker host -- surface it
            // loudly so the App Service logs the boot failure per NFR-05.
            _logger.LogCritical(ex,
                "ProvisioningHandlerDispatcher failed to start: {Error}. " +
                "Common causes: (a) queue '{Queue}' does not exist; " +
                "(b) queue is not session-enabled (task 108 recreates via Bicep with requiresSession=true); " +
                "(c) UAMI missing 'Azure Service Bus Data Receiver' role on the SB namespace (task 110).",
                ex.Message, _sbOptions.QueueName);
            throw;
        }
        finally
        {
            await SafeStopAndDisposeProcessorAsync().ConfigureAwait(false);
        }
    }

    private async Task OnSessionMessageAsync(ProcessSessionMessageEventArgs args)
    {
        // Per-message DI scope -- Scoped consumers (IProvisioningRunRepository,
        // IHandlerOutcomeApplier, keyed IProvisioningHandler, keyed handler's
        // own Scoped dependency graph) resolve correctly under this Singleton
        // BackgroundService lifetime.
        using var scope = _scopeFactory.CreateScope();

        HandlerEnvelope? envelope = null;
        DispatchDecision decision;

        try
        {
            // STEP 1 -- Deserialize envelope. Null / exception is a
            // structural failure: no dispatcher instance can process this
            // message; DeadLetter(InvalidFormat) so operator can inspect.
            envelope = TryDeserializeEnvelope(args.Message);
            if (envelope is null)
            {
                _logger.LogError(
                    "Failed to deserialize HandlerEnvelope from message {MessageId} " +
                    "(session {SessionId}, delivery {DeliveryCount}). Dead-lettering as InvalidFormat.",
                    args.Message.MessageId, args.SessionId, args.Message.DeliveryCount);
                await args.DeadLetterMessageAsync(
                    args.Message,
                    DeadLetterReasons.InvalidFormat,
                    $"Failed to deserialize HandlerEnvelope from message body.",
                    args.CancellationToken).ConfigureAwait(false);
                return;
            }

            // Steps 2-8 -- pure decision function operating on the envelope.
            decision = await DispatchCoreAsync(
                envelope,
                args.Message.DeliveryCount,
                scope.ServiceProvider,
                args.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            // Host shutdown mid-flight. Do NOT settle the message -- the
            // broker will redeliver after lock expiry; L2 + L3 dedup make
            // the re-invoke safe.
            throw;
        }
        catch (Exception ex)
        {
            // Unhandled exception in the DispatchCoreAsync flow itself
            // (not the handler body -- that's classified inside step 5).
            // Mirror BFF's ProcessingError DLQ threshold: dead-letter once
            // DeliveryCount >= MaxDeliveryCount so a bug in the dispatcher
            // does not deadlock the message forever.
            _logger.LogError(ex,
                "Unhandled dispatcher error for message {MessageId} " +
                "(session {SessionId}, delivery {DeliveryCount}, handler {HandlerId}): {Error}",
                args.Message.MessageId, args.SessionId, args.Message.DeliveryCount,
                envelope?.HandlerId ?? "(unresolved)", ex.Message);

            if (args.Message.DeliveryCount >= _options.MaxDeliveryCount)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    DeadLetterReasons.OutcomeApplyFailed,
                    $"Unhandled dispatcher error after {args.Message.DeliveryCount} deliveries: {ex.Message}",
                    args.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                await args.AbandonMessageAsync(
                    args.Message,
                    propertiesToModify: null,
                    args.CancellationToken).ConfigureAwait(false);
            }
            return;
        }

        // Translate the decision into the corresponding wire call.
        await ApplyDispatchDecisionAsync(decision, args).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure decision function -- the DS-2 §2.5 eight-step flow with NO
    /// <see cref="ProcessSessionMessageEventArgs"/> dependency. Returns a
    /// <see cref="DispatchDecision"/> the event wrapper (or a unit test)
    /// translates into the wire call. Testable in isolation.
    /// </summary>
    /// <param name="envelope">Already-deserialized envelope (step 1 completed by caller).</param>
    /// <param name="deliveryCount">Service Bus delivery count for the message being dispatched.</param>
    /// <param name="scopedProvider">Per-message DI scope's <see cref="IServiceProvider"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="DispatchDecision"/> the caller should apply to the wire.</returns>
    internal async Task<DispatchDecision> DispatchCoreAsync(
        HandlerEnvelope envelope,
        int deliveryCount,
        IServiceProvider scopedProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(scopedProvider);

        // STEP 2 -- Resolve keyed handler by envelope.HandlerId. Two failure
        // modes match BFF ServiceBusJobProcessor's dead-letter reasons
        // verbatim: no registration (NoHandler) vs DI graph construction
        // exception (HandlerResolutionFailed).
        IProvisioningHandler? handler;
        try
        {
            handler = scopedProvider.GetKeyedService<IProvisioningHandler>(envelope.HandlerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Keyed IProvisioningHandler resolution threw for HandlerId={HandlerId} " +
                "RunId={RunId} CustomerId={CustomerId} -- typically means the handler's " +
                "dependency graph failed to construct. Dead-lettering as HandlerResolutionFailed.",
                envelope.HandlerId, envelope.RunId, envelope.CustomerId);
            LogInnerExceptionChain(ex);
            return new DispatchDecision.DeadLetter(
                DeadLetterReasons.HandlerResolutionFailed,
                $"Handler DI resolution threw: {ex.GetType().Name}: {ex.Message}");
        }

        if (handler is null)
        {
            _logger.LogError(
                "No keyed IProvisioningHandler registered for HandlerId={HandlerId} " +
                "(RunId={RunId} CustomerId={CustomerId}). Dead-lettering as NoHandler. " +
                "Task 103's HandlerRegistrationCompletenessTests should have prevented this at build time.",
                envelope.HandlerId, envelope.RunId, envelope.CustomerId);
            return new DispatchDecision.DeadLetter(
                DeadLetterReasons.NoHandler,
                $"No keyed IProvisioningHandler registration for '{envelope.HandlerId}'");
        }

        // Deterministic MessageId -- recompute on the receive side so the
        // Level-2 gate works identically even when L1 (SB dedup) is off
        // during Wave-C4 pre-queue-recreate. Matches
        // ServiceBusHandlerEnqueuer.ComputeMessageId byte-for-byte.
        var messageId = ServiceBusHandlerEnqueuer.ComputeMessageId(envelope);

        // STEP 3 -- Level-2 idempotency gate. Processed-marker hit ->
        // Complete-without-invoke; lock-held -> Abandon.
        var idempotency = scopedProvider.GetRequiredService<IDispatchIdempotencyService>();

        if (await idempotency.IsProcessedAsync(messageId, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Level-2 processed-marker hit for MessageId={MessageId} " +
                "HandlerId={HandlerId} RunId={RunId}. Completing without invoke.",
                messageId, envelope.HandlerId, envelope.RunId);
            return new DispatchDecision.Complete();
        }

        if (!await idempotency
                .TryAcquireLockAsync(messageId, _options.MaxHandlerDuration, cancellationToken)
                .ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Level-2 lock held by peer for MessageId={MessageId} " +
                "HandlerId={HandlerId} RunId={RunId}. Abandoning; SB will redeliver.",
                messageId, envelope.HandlerId, envelope.RunId);
            return new DispatchDecision.Abandon();
        }

        // From this point ANY early return MUST release the Level-2 lock.
        // The `finally` block below unconditionally does that.
        try
        {
            // STEP 4 -- Load run at partitionKey = envelope.CustomerId.
            var repository = scopedProvider.GetRequiredService<IProvisioningRunRepository>();
            var read = await repository
                .ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken)
                .ConfigureAwait(false);

            if (read is null)
            {
                _logger.LogError(
                    "Run doc absent for RunId={RunId} CustomerId={CustomerId} HandlerId={HandlerId}. " +
                    "Handler contract forbids mutation on absent run -- dead-lettering as OrphanRun.",
                    envelope.RunId, envelope.CustomerId, envelope.HandlerId);
                return new DispatchDecision.DeadLetter(
                    DeadLetterReasons.OrphanRun,
                    $"runs/{envelope.RunId} absent in customer partition '{envelope.CustomerId}'");
            }

            if (IsTerminalStatus(read.Run.Status))
            {
                _logger.LogInformation(
                    "Run RunId={RunId} CustomerId={CustomerId} is already in terminal status {Status}. " +
                    "Handler {HandlerId} dispatch is stale; completing without invoke.",
                    envelope.RunId, envelope.CustomerId, read.Run.Status, envelope.HandlerId);
                return new DispatchDecision.Complete();
            }

            // STEP 5 -- Invoke handler. IProvisioningHandler contract SAYS
            // handlers SHOULD NOT throw on domain failures (they return
            // HandlerResult.Failure instead) -- but the dispatcher MUST
            // NOT trust that. On escaped exception, classify via
            // FailureClassifier.ClassifyException so the outcome-apply path
            // still runs (§4C-conformant even for buggy handlers).
            HandlerResult outcome;
            try
            {
                outcome = await handler.HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var classifier = scopedProvider.GetRequiredService<Rollback.IFailureClassifier>();
                var cls = classifier.ClassifyException(ex);
                _logger.LogWarning(ex,
                    "Handler {HandlerId} for RunId={RunId} CustomerId={CustomerId} threw " +
                    "(handler contract violation -- should return HandlerResult.Failure). " +
                    "Classified as {FailureClass}; continuing to outcome-apply.",
                    envelope.HandlerId, envelope.RunId, envelope.CustomerId, cls);

                outcome = new HandlerResult.Failure(
                    Class: cls,
                    RejectionCode: $"handler-exception-{ex.GetType().Name}",
                    Diagnostic: $"Handler {envelope.HandlerId} threw {ex.GetType().Name}: {ex.Message}");
            }

            // STEP 6 -- Re-read run (fresh ETag; handler may have written its
            // own success-path append during Step 5) + apply outcome. On
            // transient Cosmos failure, escalate per delivery count.
            var applier = scopedProvider.GetRequiredService<IHandlerOutcomeApplier>();

            ProvisioningRunReadResult? fresh;
            try
            {
                fresh = await repository
                    .ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return HandleOutcomeApplyTransientFailure(ex, deliveryCount, envelope, "re-read");
            }

            if (fresh is null)
            {
                // Rare -- run deleted between step 4 and step 6. Treat as
                // OrphanRun; the handler's write (if any) is unrecoverable
                // but the run doc is gone, so there is nothing to transition.
                _logger.LogError(
                    "Run doc deleted between step-4 read and step-6 re-read for " +
                    "RunId={RunId} CustomerId={CustomerId} HandlerId={HandlerId}. " +
                    "Dead-lettering as OrphanRun.",
                    envelope.RunId, envelope.CustomerId, envelope.HandlerId);
                return new DispatchDecision.DeadLetter(
                    DeadLetterReasons.OrphanRun,
                    $"runs/{envelope.RunId} deleted mid-dispatch in partition '{envelope.CustomerId}'");
            }

            try
            {
                await applier.ApplyHandlerOutcomeAsync(
                    fresh.Run, fresh.ETag, outcome, envelope.HandlerId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return HandleOutcomeApplyTransientFailure(ex, deliveryCount, envelope, "apply");
            }

            // STEP 7 -- Mark processed. The Level-2 lock release runs in
            // the enclosing `finally` block unconditionally.
            try
            {
                await idempotency.MarkProcessedAsync(messageId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail-open on cache write -- transition already landed in
                // Cosmos, next redelivery would L3-dedup. Log + continue.
                _logger.LogWarning(ex,
                    "Failed to record L2 processed-marker for MessageId={MessageId} " +
                    "HandlerId={HandlerId} RunId={RunId} -- Cosmos transition already landed; " +
                    "L3 handler-body dedup backstops any redelivery.",
                    messageId, envelope.HandlerId, envelope.RunId);
            }

            // STEP 8 -- Complete for BOTH Success AND applied-Failure. Retry
            // authority is §4C RollbackTransitions (auto-re-enqueue path)
            // or operator resume/clear-quarantine endpoints (manual path);
            // Abandon is reserved for step-3/step-6 infrastructure faults
            // above.
            _logger.LogInformation(
                DispatchOutcomeEventName +
                ": HandlerId={HandlerId} RunId={RunId} CustomerId={CustomerId} " +
                "OutcomeKind={OutcomeKind} MessageId={MessageId} DeliveryCount={DeliveryCount}",
                envelope.HandlerId, envelope.RunId, envelope.CustomerId,
                outcome is HandlerResult.Success ? "Success" : "Failure",
                messageId, deliveryCount);

            return new DispatchDecision.Complete();
        }
        finally
        {
            // Unconditional lock release (mirrors IdempotencyService's
            // swallow-on-outage posture -- lock self-expires at TTL so a
            // swallowed error here does not deadlock the message).
            try
            {
                await idempotency.ReleaseLockAsync(messageId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host shutdown -- lock will self-expire; nothing more to do.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to release L2 lock for MessageId={MessageId} " +
                    "HandlerId={HandlerId} RunId={RunId} -- lock will self-expire at TTL.",
                    messageId, envelope.HandlerId, envelope.RunId);
            }
        }
    }

    private DispatchDecision HandleOutcomeApplyTransientFailure(
        Exception ex, int deliveryCount, HandlerEnvelope envelope, string stage)
    {
        _logger.LogWarning(ex,
            "Cosmos {Stage} failed at step 6 for HandlerId={HandlerId} RunId={RunId} " +
            "CustomerId={CustomerId} DeliveryCount={DeliveryCount}. " +
            "{Escalation}",
            stage, envelope.HandlerId, envelope.RunId, envelope.CustomerId, deliveryCount,
            deliveryCount >= _options.MaxDeliveryCount
                ? $"DeliveryCount >= MaxDeliveryCount ({_options.MaxDeliveryCount}); dead-lettering as OutcomeApplyFailed."
                : "DeliveryCount < MaxDeliveryCount; abandoning for redelivery.");

        return deliveryCount >= _options.MaxDeliveryCount
            ? new DispatchDecision.DeadLetter(
                DeadLetterReasons.OutcomeApplyFailed,
                $"Cosmos {stage} failed {deliveryCount} times: {ex.GetType().Name}: {ex.Message}")
            : new DispatchDecision.Abandon();
    }

    private async Task ApplyDispatchDecisionAsync(DispatchDecision decision, ProcessSessionMessageEventArgs args)
    {
        switch (decision)
        {
            case DispatchDecision.Complete:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                break;

            case DispatchDecision.DeadLetter dl:
                await args.DeadLetterMessageAsync(
                    args.Message, dl.Reason, dl.Description, args.CancellationToken).ConfigureAwait(false);
                break;

            case DispatchDecision.Abandon:
                await args.AbandonMessageAsync(
                    args.Message, propertiesToModify: null, args.CancellationToken).ConfigureAwait(false);
                break;

            default:
                // DispatchDecision is a sealed abstract record with a private
                // ctor + three sealed subtypes -- this branch is structurally
                // unreachable. Guarded rather than switch-expression to
                // preserve the async wire calls' natural flow.
                throw new InvalidOperationException(
                    $"Unhandled DispatchDecision subtype: {decision.GetType().FullName}. " +
                    "Add a branch here + a subtype test in ProvisioningHandlerDispatcherInvariantTests.");
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        // Transport-level errors from the session processor plumbing
        // (SessionInitializing / SessionRunning / Receive faults). These
        // do NOT surface a specific message; the broker will redeliver or
        // release the session as appropriate. Log only.
        _logger.LogError(args.Exception,
            "ProvisioningHandlerDispatcher transport error in {EntityPath} " +
            "(source: {ErrorSource}): {Error}",
            args.EntityPath, args.ErrorSource, args.Exception.Message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ProvisioningHandlerDispatcher stopping.");
        await SafeStopAndDisposeProcessorAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Defensive stop + dispose mirroring BFF ServiceBusJobProcessor's
    /// finally-block shape verbatim: tolerate <see cref="ObjectDisposedException"/>
    /// so a race between StopAsync and ExecuteAsync's own finally does not
    /// crash the shutdown path.
    /// </summary>
    private async Task SafeStopAndDisposeProcessorAsync()
    {
        var processor = _processor;
        if (processor is null)
        {
            return;
        }

        try
        {
            await processor.StopProcessingAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            _logger.LogDebug("ProvisioningHandlerDispatcher processor already disposed during shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping ProvisioningHandlerDispatcher processor during shutdown.");
        }

        try
        {
            await processor.DisposeAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing ProvisioningHandlerDispatcher processor during shutdown.");
        }

        _processor = null;
    }

    private static HandlerEnvelope? TryDeserializeEnvelope(ServiceBusReceivedMessage message)
    {
        try
        {
            var body = message.Body.ToString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }
            return JsonSerializer.Deserialize<HandlerEnvelope>(body, BodyDeserializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void LogInnerExceptionChain(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            _logger.LogError("  -> Inner exception: {InnerError}", inner.Message);
            inner = inner.InnerException;
        }
    }

    private static bool IsTerminalStatus(Models.RunStatus status) =>
        status is Models.RunStatus.Completed
              or Models.RunStatus.Cancelled
              or Models.RunStatus.Quarantined;
}
