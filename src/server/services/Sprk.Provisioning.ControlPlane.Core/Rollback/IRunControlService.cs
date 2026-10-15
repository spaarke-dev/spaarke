// -----------------------------------------------------------------------------
// IRunControlService.cs
//
// L2 CONTROL-PLANE operator run-control seam. Owns the three operator-driven
// run state transitions that POST /api/runs/{id}/resume, /cancel and
// /gates/{gateId}/advance request:
//
//   Resume      Failed        -> Running   + the failed handler re-dispatched
//   Cancel      (non-final)   -> Cancelled + the I5 customer guard released
//   AdvanceGate WaitingOnGate -> (unchanged) + the gate's verifier re-dispatched
//
// WHY A SERVICE AND NOT CONTROL ENVELOPES:
//   These endpoints used to enqueue a HandlerEnvelope whose HandlerId was a
//   control tag ("Resume" / "Cancel" / "GateAdvance"). No keyed
//   IProvisioningHandler exists for those ids, so the Worker's dispatcher
//   dead-lettered every one as NoHandler and nothing changed (verified live
//   2026-10-15). State transitions are made here, in Cosmos, the way
//   IQuarantineClearService makes the clear-quarantine one; only REAL handler
//   ids (HandlerIds.Dispatchable) are ever enqueued.
//
// WRITE BEFORE ENQUEUE:
//   Resume persists Status=Running BEFORE it enqueues the handler. The
//   dispatcher completes-without-invoke an envelope whose run is not yet
//   active, so the reverse order would lose the dispatch.
//
// FRESH MESSAGE ID:
//   Re-dispatching a handler with Attempt=0 yields the MessageId of its first
//   dispatch, which Service Bus duplicate detection (PT1H) drops. Both
//   re-dispatching operations therefore bump run.HandlerRetryAttempts[handler]
//   in the same write and enqueue with that attempt (the HandlerOutcomeApplier
//   RetryableWithCleanup pattern).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// Operator run-control transitions behind <c>POST /api/runs/{id}/resume</c>,
/// <c>/cancel</c> and <c>/gates/{gateId}/advance</c>. Result unions are
/// exhaustive; the service never throws on domain-outcome paths.
/// </summary>
public interface IRunControlService
{
    /// <summary>
    /// Resumes a <see cref="RunStatus.Failed"/> run at its failure point: re-claims the I5 customer guard,
    /// sets Status=Running / clears ErrorDetail and CompletedOn (ETag-safe), then re-enqueues
    /// <see cref="ProvisioningRun.CurrentPhase"/> with a bumped attempt so the MessageId is fresh. If the enqueue
    /// fails the run is reverted to Failed and <see cref="RunResumeResult.EnqueueFailed"/> is returned.
    /// </summary>
    Task<RunResumeResult> ResumeAsync(string customerId, string runId, string? actorObjectId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a run that is not already final (Completed / Cancelled) or Quarantined: Status=Cancelled,
    /// CompletedOn=now (ETag-safe), then releases the I5 customer guard.
    /// </summary>
    Task<RunCancelResult> CancelAsync(string customerId, string runId, string? actorObjectId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks a Pending gate's verifier handler to re-check now, instead of waiting for the reconciler's next
    /// (hourly, duplicate-detection-limited) dispatch: bumps the verifier's attempt counter (ETag-safe), then
    /// enqueues the verifier with that attempt. The verifier itself re-checks the gate and transitions the run.
    /// </summary>
    Task<GateAdvanceResult> AdvanceGateAsync(string customerId, string runId, string gateId, CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="IRunControlService.ResumeAsync"/>.</summary>
public abstract record RunResumeResult
{
    private RunResumeResult() { }

    /// <summary>The run is Running again and <paramref name="HandlerId"/> was enqueued with <paramref name="Attempt"/>.</summary>
    public sealed record Success(ProvisioningRun Run, string HandlerId, int Attempt) : RunResumeResult;

    /// <summary>No run with the given id exists in the customer's partition (404).</summary>
    public sealed record NotFound() : RunResumeResult;

    /// <summary>The run cannot be resumed: not Failed, or no dispatchable phase to resume (409).</summary>
    public sealed record Conflict(RunStatus CurrentStatus, string Detail) : RunResumeResult;

    /// <summary>A different run holds the customer's I5 guard (409).</summary>
    public sealed record GuardConflict(string WinningRunId, string ReasonCode) : RunResumeResult;

    /// <summary>The I5 guard could not be evaluated (502).</summary>
    public sealed record GuardUnavailable(string Diagnostic) : RunResumeResult;

    /// <summary>ETag mismatch — a concurrent writer advanced the run between read and write (409).</summary>
    public sealed record ConcurrencyConflict(ProvisioningRun Current) : RunResumeResult;

    /// <summary>The handler could not be enqueued; the run was reverted to Failed so the operator can retry (503).</summary>
    public sealed record EnqueueFailed(string Diagnostic) : RunResumeResult;
}

/// <summary>Outcome of <see cref="IRunControlService.CancelAsync"/>.</summary>
public abstract record RunCancelResult
{
    private RunCancelResult() { }

    /// <summary>The run is Cancelled.</summary>
    public sealed record Success(ProvisioningRun Run) : RunCancelResult;

    /// <summary>No run with the given id exists in the customer's partition (404).</summary>
    public sealed record NotFound() : RunCancelResult;

    /// <summary>The run is Completed, Cancelled or Quarantined (clear the quarantine first) (409).</summary>
    public sealed record Conflict(RunStatus CurrentStatus) : RunCancelResult;

    /// <summary>ETag mismatch — a concurrent writer advanced the run between read and write (409).</summary>
    public sealed record ConcurrencyConflict(ProvisioningRun Current) : RunCancelResult;
}

/// <summary>Outcome of <see cref="IRunControlService.AdvanceGateAsync"/>.</summary>
public abstract record GateAdvanceResult
{
    private GateAdvanceResult() { }

    /// <summary>The gate's verifier <paramref name="VerifierHandler"/> was enqueued with <paramref name="Attempt"/>.</summary>
    public sealed record Success(ProvisioningRun Run, string VerifierHandler, int Attempt) : GateAdvanceResult;

    /// <summary>No run with the given id exists in the customer's partition (404).</summary>
    public sealed record NotFound() : GateAdvanceResult;

    /// <summary>The run has no gate with the requested id (404).</summary>
    public sealed record GateNotFound() : GateAdvanceResult;

    /// <summary>The run is not WaitingOnGate, or the gate is not Pending / has no dispatchable verifier (409).</summary>
    public sealed record Conflict(RunStatus CurrentStatus, string Detail) : GateAdvanceResult;

    /// <summary>ETag mismatch — a concurrent writer advanced the run between read and write (409).</summary>
    public sealed record ConcurrencyConflict(ProvisioningRun Current) : GateAdvanceResult;

    /// <summary>The verifier could not be enqueued (503).</summary>
    public sealed record EnqueueFailed(string Diagnostic) : GateAdvanceResult;
}
