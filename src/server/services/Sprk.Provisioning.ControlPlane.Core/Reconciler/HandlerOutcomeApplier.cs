// -----------------------------------------------------------------------------
// HandlerOutcomeApplier.cs
//
// L2 CONTROL-PLANE §4C rollback + guard-release orchestration (task 104,
// Phase C'' Wave G-1). Verbatim-moved body of the pre-extraction
// StateReconcilerService.ApplyHandlerOutcomeAsync (task 061), PLUS the task
// 104 gap closure wiring ICustomerRunGuard.ReleaseAsync on guard-releasing
// §4C transitions (DS-2 §5 flagged this as a gap to close, not defer).
//
// EXHAUSTIVE-SWITCH GUARD: routes through <see cref="RollbackTransitions"/>
// exclusively -- a new FailureClass value added to the enum WITHOUT a
// corresponding branch in RollbackTransitions fails the build at CS8524
// (warning-as-error via TreatWarningsAsErrors inherited from
// Directory.Build.props). See RollbackTransitions.cs file header for the
// full rationale. This applier MUST remain the ONLY place §4C
// RollbackTransitions logic runs (DS-2 §5) -- no re-implementation in the
// dispatcher (task 102) or any other consumer.
//
// USAGE:
//   Resolved Scoped from ReconcilerModule. StateReconcilerService's own
//   internal ApplyHandlerOutcomeAsync is now a thin delegating shim over
//   this class (per-tick fresh scope, matching the pre-extraction Scoped
//   collaborator resolution). Task 102's ProvisioningHandlerDispatcher
//   resolves IHandlerOutcomeApplier directly from its own per-message scope
//   -- no reconciler round-trip needed.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <inheritdoc cref="IHandlerOutcomeApplier"/>
public sealed class HandlerOutcomeApplier : IHandlerOutcomeApplier
{
    private readonly IFailureClassifier _classifier;
    private readonly IProvisioningRunRepository _repository;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly ICustomerRunGuard _runGuard;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HandlerOutcomeApplier> _logger;

    public HandlerOutcomeApplier(
        IFailureClassifier classifier,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        ICustomerRunGuard runGuard,
        TimeProvider timeProvider,
        ILogger<HandlerOutcomeApplier> logger)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(runGuard);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _classifier = classifier;
        _repository = repository;
        _enqueuer = enqueuer;
        _runGuard = runGuard;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerOutcomeApplied> ApplyHandlerOutcomeAsync(
        ProvisioningRun run,
        string ifMatchEtag,
        HandlerResult outcome,
        string handlerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatchEtag);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);

        // Success path -- handlers own the CompletedPhases append + Cosmos
        // write. The applier does NOT double-write on Success (parity with
        // the reconciler's "does NOT write to Cosmos" invariant; the write
        // below fires ONLY on the Failure path where Status transitions per
        // §4C).
        if (outcome is HandlerResult.Success)
        {
            return new HandlerOutcomeApplied(
                TargetStatus: run.Status,
                Reenqueued: false,
                FailureClass: null);
        }

        var failure = (HandlerResult.Failure)outcome;
        var failureClass = _classifier.Classify(failure);

        // §4C exhaustive-switch state mapping -- see RollbackTransitions.cs.
        var targetStatus = RollbackTransitions.MapToRunStatus(failureClass);
        var shouldReenqueue = RollbackTransitions.ShouldReEnqueue(failureClass);

        // Task 107 / DS-2 §4-L1: on the auto-retry path (RetryableWithCleanup),
        // increment the per-handler retry counter BEFORE the write below so it
        // persists atomically with the failure transition (no extra Cosmos
        // round trip). This attempt value becomes the re-enqueued envelope's
        // HandlerEnvelope.Attempt, which participates in ComputeMessageId so
        // the retry's MessageId differs from the original dispatch's and
        // survives Service Bus level-1 duplicate detection (task 108). The
        // normal tick-driven ready-set enqueue path never reads this
        // dictionary -- its byte-stability contract is unaffected.
        var attempt = 0;
        if (shouldReenqueue)
        {
            run.HandlerRetryAttempts.TryGetValue(handlerId, out var priorAttempts);
            attempt = priorAttempts + 1;
            run.HandlerRetryAttempts[handlerId] = attempt;
        }

        // Mutate + persist. Quarantine metadata populated on QuarantineRequired
        // per design.md §4C Quarantined row.
        run.Status = targetStatus;
        run.CurrentPhase = handlerId;
        run.ErrorDetail = $"[{failure.RejectionCode}] {failure.Diagnostic}";
        var now = _timeProvider.GetUtcNow();

        if (failureClass == FailureClass.QuarantineRequired)
        {
            run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Quarantined,
                Reason = failure.Diagnostic,
                QuarantinedByHandler = handlerId,
                QuarantinedAt = now,
            };
        }

        // Terminal-transition timestamp for auditability.
        if (targetStatus is RunStatus.Completed
            or RunStatus.Failed
            or RunStatus.Cancelled
            or RunStatus.Quarantined)
        {
            run.CompletedOn = now;
        }

        var replace = await _repository
            .ReplaceRunAsync(run, ifMatchEtag, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "ReconcilerOutcomeApplied: HandlerId={HandlerId} RunId={RunId} CustomerId={CustomerId} " +
            "FailureClass={FailureClass} TargetStatus={TargetStatus} RejectionCode={RejectionCode}",
            handlerId, run.RunId, run.CustomerId,
            failureClass, targetStatus, failure.RejectionCode);

        if (replace is ReplaceRunResult.Conflict)
        {
            // Concurrent writer landed the same/similar transition -- the
            // reconciler is idempotent by design; a next tick picks up the
            // freshly-written state. Do NOT re-enqueue on Conflict -- the
            // sibling writer may already have. Do NOT release the guard here
            // either -- OUR write did not land, so the concurrent winner (or
            // its own ApplyHandlerOutcomeAsync call) owns the release
            // decision for its own committed transition. Releasing here too
            // would be a double-release attempt.
            return new HandlerOutcomeApplied(targetStatus, Reenqueued: false, FailureClass: failureClass);
        }

        // Task 104 / DS-2 §5 gap closure (C2.1 byproduct): release the I5
        // same-customer guard (task 059 ICustomerRunGuard) on guard-releasing
        // §4C transitions. Per RollbackTransitions.ShouldReleaseCustomerGuard,
        // today this fires ONLY for FailureClass.SuccessfulButDrifted (the
        // run reached RunStatus.Completed via the Failure/drift-detected
        // branch) -- QuarantineRequired/Resumable/RetryableWithCleanup all
        // keep the guard held per spec FR-24 SCOPE + operator-resume/retry
        // semantics. This is a NEW call site (not a duplicate of the
        // existing CreateRun id-collision release or the operator-initiated
        // CancelRun release in RunsEndpoints.cs) -- before this task, no
        // caller released the guard on this transition, so wiring it here is
        // a pure gap closure, not a behavior change for any existing
        // endpoint. Best-effort: a transient failure here does not fail the
        // outcome application -- the operator's cancel/clear-quarantine
        // endpoints remain available as a backstop.
        if (RollbackTransitions.ShouldReleaseCustomerGuard(failureClass))
        {
            var release = await _runGuard
                .ReleaseAsync(run.CustomerId, run.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (release is ReleaseResult.TransientFailure releaseFailure)
            {
                _logger.LogWarning(
                    "ReconcilerOutcomeGuardReleaseFailed: HandlerId={HandlerId} RunId={RunId} " +
                    "CustomerId={CustomerId} Diagnostic={Diagnostic} -- guard release is best-effort; " +
                    "transition already landed; operator cancel/clear-quarantine paths remain available.",
                    handlerId, run.RunId, run.CustomerId, releaseFailure.Diagnostic);
            }
        }

        if (!shouldReenqueue)
        {
            return new HandlerOutcomeApplied(targetStatus, Reenqueued: false, FailureClass: failureClass);
        }

        // Auto-retry path (§4C RetryableWithCleanup) -- re-fire the envelope
        // with the incremented Attempt (task 107) so the MessageId differs
        // from the original dispatch's and is not silently dropped by
        // Service Bus level-1 duplicate detection (task 108). Service Bus
        // wire dedup + Redis idempotency continue to own retry-safety per
        // ADR-036.
        var envelope = ReconcilerEnvelopeBuilder.Build(handlerId, run, _timeProvider, attempt);
        try
        {
            await _enqueuer.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "ReconcilerOutcomeReenqueueFailed: HandlerId={HandlerId} RunId={RunId} CustomerId={CustomerId} " +
                "— transition landed but re-enqueue failed; next reconciler tick will retry from state.",
                handlerId, run.RunId, run.CustomerId);
            return new HandlerOutcomeApplied(targetStatus, Reenqueued: false, FailureClass: failureClass);
        }

        return new HandlerOutcomeApplied(targetStatus, Reenqueued: true, FailureClass: failureClass);
    }
}
