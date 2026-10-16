// -----------------------------------------------------------------------------
// RunControlService.cs
//
// Default <see cref="IRunControlService"/> impl over
// <see cref="IProvisioningRunRepository"/> + <see cref="ICustomerRunGuard"/> +
// <see cref="IHandlerEnqueuer"/>. Same shape as QuarantineClearService:
// point-read -> state check -> ETag-safe ReplaceRunAsync -> guard call, with a
// discriminated result the endpoint maps to HTTP. See IRunControlService.cs for
// why these transitions are made here and not by enqueueing control envelopes.
//
// RESUME FLOW
//   1. Read; null -> NotFound. Status != Failed -> Conflict (a Quarantined run
//      is cleared first, which leaves it Failed).
//   2. CurrentPhase must be a HandlerIds.Dispatchable id -> else Conflict
//      (checked before the guard so a refusal has no side effect).
//   3. TryAcquireAsync(customerId, runId): idempotent when this run still holds
//      the guard; re-claims it after the Failed transition released it
//      (RollbackTransitions.ShouldReleaseCustomerGuard(Resumable) == true, EXEC-07)
//      or a clear-quarantine / cancel did. Another run holding it ->
//      GuardConflict; transport failure -> GuardUnavailable.
//   4. Bump HandlerRetryAttempts for the current phase AND every other handler in
//      run.FailedHandlers (T186 L41: a later sibling failure used to hide an earlier one,
//      which then never ran again), Status=Running, ErrorDetail=null, CompletedOn=null,
//      ReplaceRunAsync(ETag).
//      The first resume after a clear-quarantine also adds every DAG-ready handler (T186 L55) and stamps
//      Quarantine.ResumedAt.
//   5. THEN enqueue ReconcilerEnvelopeBuilder.Build(id, run, time, attempt) for each, current phase first.
//      If the enqueue throws, revert the run to Failed (previous ErrorDetail,
//      CompletedOn=now, new ETag) and return EnqueueFailed.
//   Entry-point handlers H0 / H0.5 are covered too: the reconciler never
//   dispatches them (DagAdvancer.EntryPointHandlers), so this enqueue is the
//   only thing that can re-run them.
//
// CANCEL NOTE
//   A handler already in flight when the run is cancelled is not interrupted:
//   its later write loses the ETag race against the Cancelled write, and the
//   dispatcher completes-without-invoke any envelope for a terminal run.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <inheritdoc/>
public sealed class RunControlService : IRunControlService
{
    // Stateless (the DAG is static data); not injected because the API host does not register the reconciler.
    private static readonly DagAdvancer ReadyHandlers = new();

    private readonly IProvisioningRunRepository _repository;
    private readonly ICustomerRunGuard _customerRunGuard;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RunControlService> _logger;

    /// <summary>Constructs the service. All collaborators required (ADR-010 DI minimalism).</summary>
    public RunControlService(
        IProvisioningRunRepository repository,
        ICustomerRunGuard customerRunGuard,
        IHandlerEnqueuer enqueuer,
        TimeProvider timeProvider,
        ILogger<RunControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(customerRunGuard);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _customerRunGuard = customerRunGuard;
        _enqueuer = enqueuer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RunResumeResult> ResumeAsync(
        string customerId,
        string runId,
        string? actorObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var read = await _repository.ReadRunAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogInformation(
                "RunResume: run not found. CustomerId={CustomerId} RunId={RunId}", customerId, runId);
            return new RunResumeResult.NotFound();
        }

        var run = read.Run;
        if (run.Status != RunStatus.Failed)
        {
            _logger.LogInformation(
                "RunResume: wrong state. CustomerId={CustomerId} RunId={RunId} CurrentStatus={CurrentStatus}",
                customerId, runId, run.Status);
            return new RunResumeResult.Conflict(
                run.Status,
                run.Status == RunStatus.Quarantined
                    ? "a Quarantined run must be cleared (clear-quarantine) before it can be resumed"
                    : "only a Failed run can be resumed");
        }

        var handlerId = run.CurrentPhase;
        if (string.IsNullOrWhiteSpace(handlerId) || !HandlerIds.Dispatchable.Contains(handlerId, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "RunResume: no dispatchable phase to resume. CustomerId={CustomerId} RunId={RunId} CurrentPhase={CurrentPhase}",
                customerId, runId, handlerId ?? "<null>");
            return new RunResumeResult.Conflict(
                run.Status,
                $"the run's current phase '{handlerId ?? "<none>"}' is not a dispatchable handler, so there is nothing to resume");
        }

        // Re-claim the I5 guard for THIS run. Idempotent when it still holds it.
        var acquire = await _customerRunGuard.TryAcquireAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        switch (acquire)
        {
            case AcquireResult.Conflict conflict:
                _logger.LogInformation(
                    "RunResume: guard held by another run. CustomerId={CustomerId} RunId={RunId} WinningRunId={WinningRunId} ReasonCode={ReasonCode}",
                    customerId, runId, conflict.WinningRunId, conflict.ReasonCode);
                return new RunResumeResult.GuardConflict(conflict.WinningRunId, conflict.ReasonCode);

            case AcquireResult.TransientFailure txf:
                _logger.LogWarning(
                    "RunResume: guard transient failure. CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                    customerId, runId, txf.Diagnostic);
                return new RunResumeResult.GuardUnavailable(txf.Diagnostic);
        }

        // T186 L41: re-dispatch EVERY handler whose last attempt failed, not only the current phase — a later sibling
        // failure becomes the run's current phase and used to hide the earlier one, which then never ran again.
        var toDispatch = new List<string> { handlerId };
        toDispatch.AddRange(run.FailedHandlers.Where(h =>
            !string.Equals(h, handlerId, StringComparison.Ordinal)
            && HandlerIds.Dispatchable.Contains(h, StringComparer.Ordinal)));

        // T186 L55: while the run was Quarantined the dispatcher completed every arriving envelope without running it
        // (a quarantined run is terminal to it). A sibling dispatched alongside the quarantining handler — H12b next to
        // H12a — was lost that way, and the reconciler's re-sends reuse its MessageId, which Service Bus duplicate
        // detection drops for an hour. So the first resume after a clear also re-dispatches every ready handler with a
        // fresh attempt. Only that resume: a plain Failed run invokes its envelopes, and a ready handler may still be
        // running (a long H6 import) — re-sending it then would start a second copy.
        var previousQuarantineResumedAt = run.Quarantine?.ResumedAt;
        if (run.Quarantine is { State: QuarantineState.Cleared, ResumedAt: null } clearedQuarantine)
        {
            var statusBefore = run.Status;
            run.Status = RunStatus.Running; // ComputeReadyHandlers returns nothing for a Failed run
            foreach (var ready in ReadyHandlers.ComputeReadyHandlers(run))
            {
                if (!toDispatch.Contains(ready, StringComparer.Ordinal))
                {
                    toDispatch.Add(ready);
                }
            }
            run.Status = statusBefore;
            clearedQuarantine.ResumedAt = _timeProvider.GetUtcNow();
        }

        // Fresh MessageId: attempt = prior + 1 for each, persisted in the same write.
        var attempts = new List<(string HandlerId, int Attempt)>();
        foreach (var id in toDispatch)
        {
            run.HandlerRetryAttempts.TryGetValue(id, out var prior);
            attempts.Add((id, prior + 1));
            run.HandlerRetryAttempts[id] = prior + 1;
            run.ResetAutoRetries(id); // T186 L44: an operator re-dispatch restores the auto-retry budget
        }

        var previousErrorDetail = run.ErrorDetail;
        run.Status = RunStatus.Running;
        run.ErrorDetail = null;
        run.CompletedOn = null;

        var replace = await _repository.ReplaceRunAsync(run, read.ETag, cancellationToken).ConfigureAwait(false);
        switch (replace)
        {
            case ReplaceRunResult.Conflict c:
                // The guard was (re)acquired above. A Failed run normally holds no guard (Resumable releases it), so
                // if the concurrent winner left the run inactive (a cancel, or another resume that reverted to Failed)
                // the guard we took would block every later POST /api/runs for this customer. Release it — stale-value
                // safe: Mismatched is a no-op. An active winner (Running / WaitingOnGate) keeps it.
                if (c.Current.Run.Status is not (RunStatus.Running or RunStatus.WaitingOnGate))
                {
                    await ReleaseGuardLogOnlyAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
                }
                return new RunResumeResult.ConcurrencyConflict(c.Current.Run);
            case ReplaceRunResult.NotFound:
                return new RunResumeResult.NotFound();
            case ReplaceRunResult.Success s:
                return await EnqueueResumedHandlersAsync(
                    s, handlerId, attempts, previousErrorDetail, previousQuarantineResumedAt, actorObjectId, cancellationToken).ConfigureAwait(false);
            default:
                throw new System.Diagnostics.UnreachableException(
                    "ReplaceRunResult exhaustive union changed — update RunControlService.");
        }
    }

    private async Task<RunResumeResult> EnqueueResumedHandlersAsync(
        ReplaceRunResult.Success persisted,
        string handlerId,
        IReadOnlyList<(string HandlerId, int Attempt)> attempts,
        string? previousErrorDetail,
        DateTimeOffset? previousQuarantineResumedAt,
        string? actorObjectId,
        CancellationToken cancellationToken)
    {
        var run = persisted.Run;
        var attempt = attempts[0].Attempt;
        try
        {
            // The current phase first (attempts[0]), then the other failed handlers.
            foreach (var (id, idAttempt) in attempts)
            {
                var envelope = ReconcilerEnvelopeBuilder.Build(id, run, _timeProvider, idAttempt);
                await _enqueuer.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(id, handlerId, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "RunResumed (other failed or ready handler, T186 L41/L55): CustomerId={CustomerId} RunId={RunId} HandlerId={HandlerId} Attempt={Attempt}",
                        run.CustomerId, run.RunId, id, idAttempt);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "RunResume: enqueue failed — reverting run to Failed. CustomerId={CustomerId} RunId={RunId} HandlerId={HandlerId} Attempt={Attempt}",
                run.CustomerId, run.RunId, handlerId, attempt);
            if (run.Quarantine is not null)
            {
                run.Quarantine.ResumedAt = previousQuarantineResumedAt; // the next resume must still re-dispatch the ready handlers
            }
            await RevertToFailedAsync(run, persisted.ETag, previousErrorDetail, cancellationToken).ConfigureAwait(false);
            return new RunResumeResult.EnqueueFailed($"{ex.GetType().Name}: {ex.Message}");
        }

        _logger.LogInformation(
            "RunResumed: CustomerId={CustomerId} RunId={RunId} HandlerId={HandlerId} Attempt={Attempt} ActorOid={ActorOid}",
            run.CustomerId, run.RunId, handlerId, attempt, actorObjectId ?? string.Empty);
        return new RunResumeResult.Success(run, handlerId, attempt);
    }

    /// <summary>
    /// Best-effort undo of the Failed -> Running write when the enqueue failed, so the run is not left
    /// Running with nothing dispatched. The attempt counter stays bumped: the retry then uses a fresh MessageId.
    /// </summary>
    private async Task RevertToFailedAsync(
        ProvisioningRun run, string etag, string? previousErrorDetail, CancellationToken cancellationToken)
    {
        run.Status = RunStatus.Failed;
        run.ErrorDetail = previousErrorDetail;
        run.CompletedOn = _timeProvider.GetUtcNow();
        try
        {
            var revert = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
            if (revert is ReplaceRunResult.Success)
            {
                // Back to Failed: give back the guard ResumeAsync took, as the original Resumable failure had.
                await ReleaseGuardLogOnlyAsync(run.CustomerId, run.RunId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogError(
                    "RunResume: revert to Failed lost the ETag race ({Outcome}); the run may be left Running without a dispatched handler. " +
                    "CustomerId={CustomerId} RunId={RunId}",
                    revert.GetType().Name, run.CustomerId, run.RunId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "RunResume: revert to Failed threw; the run may be left Running without a dispatched handler. CustomerId={CustomerId} RunId={RunId}",
                run.CustomerId, run.RunId);
        }
    }

    /// <inheritdoc/>
    public async Task<RunCancelResult> CancelAsync(
        string customerId,
        string runId,
        string? actorObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var read = await _repository.ReadRunAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogInformation(
                "RunCancel: run not found. CustomerId={CustomerId} RunId={RunId}", customerId, runId);
            return new RunCancelResult.NotFound();
        }

        // Completed / Cancelled are final; Quarantined keeps the guard until clear-quarantine (design §4C).
        if (read.Run.Status is RunStatus.Completed or RunStatus.Cancelled or RunStatus.Quarantined)
        {
            _logger.LogInformation(
                "RunCancel: wrong state. CustomerId={CustomerId} RunId={RunId} CurrentStatus={CurrentStatus}",
                customerId, runId, read.Run.Status);
            return new RunCancelResult.Conflict(read.Run.Status);
        }

        read.Run.Status = RunStatus.Cancelled;
        read.Run.CompletedOn = _timeProvider.GetUtcNow();

        var replace = await _repository.ReplaceRunAsync(read.Run, read.ETag, cancellationToken).ConfigureAwait(false);
        switch (replace)
        {
            case ReplaceRunResult.Success s:
                await ReleaseGuardLogOnlyAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "RunCancelled: CustomerId={CustomerId} RunId={RunId} ActorOid={ActorOid}",
                    customerId, runId, actorObjectId ?? string.Empty);
                return new RunCancelResult.Success(s.Run);
            case ReplaceRunResult.Conflict c:
                return new RunCancelResult.ConcurrencyConflict(c.Current.Run);
            case ReplaceRunResult.NotFound:
                return new RunCancelResult.NotFound();
            default:
                throw new System.Diagnostics.UnreachableException(
                    "ReplaceRunResult exhaustive union changed — update RunControlService.");
        }
    }

    private async Task ReleaseGuardLogOnlyAsync(string customerId, string runId, CancellationToken cancellationToken)
    {
        // The Cosmos transition has landed; a release problem is log-only (same stance as QuarantineClearService).
        var release = await _customerRunGuard.ReleaseAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        switch (release)
        {
            case ReleaseResult.Released:
                _logger.LogInformation(
                    "RunCancel: sprk_currentrunid released. CustomerId={CustomerId} RunId={RunId}", customerId, runId);
                break;
            case ReleaseResult.Mismatched mismatched:
                _logger.LogInformation(
                    "RunCancel: sprk_currentrunid release was a no-op (idempotent). CustomerId={CustomerId} RunId={RunId} CurrentValue={CurrentValue}",
                    customerId, runId, mismatched.CurrentValue ?? "<null>");
                break;
            case ReleaseResult.NotFound:
                _logger.LogWarning(
                    "RunCancel: registry row missing during release. CustomerId={CustomerId} RunId={RunId}", customerId, runId);
                break;
            case ReleaseResult.TransientFailure txf:
                _logger.LogWarning(
                    "RunCancel: sprk_currentrunid release transient failure (log-only). CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                    customerId, runId, txf.Diagnostic);
                break;
        }
    }

    /// <inheritdoc/>
    public async Task<GateAdvanceResult> AdvanceGateAsync(
        string customerId,
        string runId,
        string gateId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);

        var read = await _repository.ReadRunAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogInformation(
                "GateAdvance: run not found. CustomerId={CustomerId} RunId={RunId}", customerId, runId);
            return new GateAdvanceResult.NotFound();
        }

        var run = read.Run;
        // T186 L37: also a Running run. A sibling's success could overwrite the gated handler's WaitingOnGate (fixed in
        // ProvisioningRun.MarkPhaseSucceeded, but a run already in that state, or one whose gate a later failure +
        // resume left Pending, would otherwise have no way forward: the reconciler's re-dispatch of the verifier reuses
        // its MessageId and is dropped as a duplicate). Re-dispatching a Pending gate's verifier on a Running run is
        // safe: per-customer messages are serialised by session, and the verifier re-checks the gate idempotently.
        if (run.Status is not (RunStatus.WaitingOnGate or RunStatus.Running))
        {
            return new GateAdvanceResult.Conflict(run.Status, "a gate can only be advanced while the run is WaitingOnGate or Running");
        }

        if (!run.GateStates.TryGetValue(gateId, out var gate))
        {
            return new GateAdvanceResult.GateNotFound();
        }

        if (gate.Status != GateState.Pending)
        {
            return new GateAdvanceResult.Conflict(run.Status, $"gate '{gateId}' is already {gate.Status}, not Pending");
        }

        var verifier = gate.VerifierHandler;
        if (string.IsNullOrWhiteSpace(verifier) || !HandlerIds.Dispatchable.Contains(verifier, StringComparer.Ordinal))
        {
            return new GateAdvanceResult.Conflict(
                run.Status,
                $"gate '{gateId}' has no dispatchable verifier handler ('{verifier ?? "<none>"}'), so there is nothing to re-check");
        }

        // Fresh MessageId: the reconciler's own dispatch of this verifier is Attempt=0 and is dropped by duplicate
        // detection after the first one; a bumped attempt is what makes the operator's request take effect now.
        run.HandlerRetryAttempts.TryGetValue(verifier, out var priorAttempts);
        var attempt = priorAttempts + 1;
        run.HandlerRetryAttempts[verifier] = attempt;
        run.ResetAutoRetries(verifier); // T186 L44

        var replace = await _repository.ReplaceRunAsync(run, read.ETag, cancellationToken).ConfigureAwait(false);
        switch (replace)
        {
            case ReplaceRunResult.Conflict c:
                return new GateAdvanceResult.ConcurrencyConflict(c.Current.Run);
            case ReplaceRunResult.NotFound:
                return new GateAdvanceResult.NotFound();
            case ReplaceRunResult.Success s:
                try
                {
                    var envelope = ReconcilerEnvelopeBuilder.Build(verifier, s.Run, _timeProvider, attempt);
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
                        "GateAdvance: enqueue failed. CustomerId={CustomerId} RunId={RunId} GateId={GateId} HandlerId={HandlerId} Attempt={Attempt}",
                        customerId, runId, gateId, verifier, attempt);
                    return new GateAdvanceResult.EnqueueFailed($"{ex.GetType().Name}: {ex.Message}");
                }

                _logger.LogInformation(
                    "GateAdvanceRequested: CustomerId={CustomerId} RunId={RunId} GateId={GateId} HandlerId={HandlerId} Attempt={Attempt}",
                    customerId, runId, gateId, verifier, attempt);
                return new GateAdvanceResult.Success(s.Run, verifier, attempt);
            default:
                throw new System.Diagnostics.UnreachableException(
                    "ReplaceRunResult exhaustive union changed — update RunControlService.");
        }
    }
}
