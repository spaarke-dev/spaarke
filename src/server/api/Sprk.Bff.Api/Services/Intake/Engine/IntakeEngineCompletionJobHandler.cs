using System.Diagnostics;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake.Engine;

/// <summary>
/// Handles one engine completion (spaarke-intake-pipeline-r1 task 014, FR-07, FR-09, NFR-03, NFR-06; ADR-004 A2, ADR-028): records the
/// output of the dispatch the run is awaiting and hands the run back to the orchestrator, or holds or fails the run.
/// Run only by <see cref="IntakeEngineCompletionProcessor"/> from <c>intake-engine-completions</c>; it is deliberately NOT an
/// <see cref="IJobHandler"/> registration, so a completion arriving on <c>sdap-jobs</c> finds no handler.
/// </summary>
/// <remarks>
/// <para><b>Untrusted input.</b> A completion comes from the container that parses untrusted documents. Only the operation, run id,
/// dispatch attempt, outcome and check outcome are read (<see cref="IntakeEngineCompletion.TryParse"/>), each against a closed set; no
/// path, text or id other than those is read, and the output paths stored on the run are DERIVED from the run id
/// (<see cref="IntakeEngineDispatchId.OutPrefix"/>). The run must exist, be an intake run, be awaiting the step this operation names
/// AS A DISPATCHED STEP, and be awaiting exactly this dispatch (the run's <c>sprk_engineexecutionid</c> equals the request's key; an
/// engine step is named by its operation): anything else changes nothing and completes the message.</para>
/// <para><b>Classification of a completion against the run's recorded dispatch</b> (by position: attempt, then operation order):
/// equal is acted on; a LATER position than the recorded one means the orchestrator has not recorded the dispatch yet (it writes
/// the execution id after the send), so the message is retried; an EARLIER one is superseded and is a no-op.</para>
/// <para><b>Duplicates.</b> The result is recorded once (<see cref="IntakeRunStore.RecordExternalStepResultAsync"/> is conditional on
/// the run still awaiting that dispatch). The continuation (<c>IntakeRun</c> job, key <c>intake-run-{runId}</c>) is sent after the
/// record and a marker <c>engine-continued:{operation}:{attempt}</c> is written after the send: a redelivery that finds the result
/// recorded but no marker (the earlier delivery died in between) sends the continuation again, one that finds the marker sends
/// nothing. A late completion after the timeout finds a held run that no longer awaits the dispatch and changes nothing.</para>
/// <para><b>Outcomes.</b> <c>completed</c> segment: result stored, continue. <c>completed</c> redline: check <c>passed</c> stores the
/// result and continues; <c>failed</c> (FR-09c) or no valid check outcome holds the run (nothing is sent for approval).
/// <c>plan_does_not_fit</c> and <c>contract_error</c> hold; <c>input_invalid</c> fails the run, because the draft can never be read.</para>
/// <para>ADR-015: run id, operation, attempt, closed codes and counts only. Never engine stderr, document text or a path from the message.</para>
/// </remarks>
public sealed class IntakeEngineCompletionJobHandler : IJobHandler
{
    private readonly IntakeRunStore _store;
    private readonly JobSubmissionService _jobs;
    private readonly ILogger<IntakeEngineCompletionJobHandler> _logger;

    public IntakeEngineCompletionJobHandler(
        IntakeRunStore store, JobSubmissionService jobs, ILogger<IntakeEngineCompletionJobHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => IntakeEngineJobTypes.Completed;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!IntakeEngineCompletion.TryParse(job.Payload, out var parsed) || parsed is null)
        {
            _logger.LogError("Engine completion {JobId}: the payload is not a valid completion; it is dropped", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid completion: a dispatch and an outcome are required", job.Attempt, stopwatch.Elapsed);
        }

        var completion = parsed;
        var dispatch = completion.Dispatch;
        var run = await _store.ReadRunAsync(dispatch.RunId, ct).ConfigureAwait(false);

        // The run's own correlation id is the one logged (the message's is untrusted text); the engine echoes the request's.
        var correlationId = run?.CorrelationId;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId ?? (IntakeEngineJson.IsEngineSafeCorrelationId(job.CorrelationId) ? job.CorrelationId : null),
            ["JobId"] = job.JobId,
            ["RunId"] = dispatch.RunId,
            ["Operation"] = dispatch.Operation,
            ["DispatchAttempt"] = dispatch.DispatchAttempt,
        });

        JobOutcome Done() => JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        JobOutcome Retry(string reason) => JobOutcome.Failure(job.JobId, JobType, reason, job.Attempt, stopwatch.Elapsed);
        JobOutcome NoChange(string reason)
        {
            _logger.LogInformation("Intake run {RunId}: engine completion of {Operation} attempt {DispatchAttempt} changes nothing ({Reason})",
                dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt, reason);
            return Done();
        }

        // 1. The run must exist, be an intake run of a request, and still be live.
        if (run is null || run.JobType != (int)ProcessingJobType.IntakeRun || run.RequestId is null)
        {
            return NoChange("no such intake run");
        }

        if (IntakeRunStore.IsTerminalStatus(run.Status))
        {
            return NoChange("the run is finished"); // a run failed by an earlier completion (input_invalid), or completed
        }

        // 2. Which dispatch has the orchestrator recorded?
        var recorded = ClassifyRecordedDispatch(run.EngineExecutionId, dispatch);
        if (recorded == RecordedDispatch.Unrelated || recorded == RecordedDispatch.Superseded)
        {
            return NoChange(recorded == RecordedDispatch.Superseded ? "an earlier dispatch" : "not an engine dispatch of this run");
        }

        if (recorded == RecordedDispatch.NotYetRecorded)
        {
            // The orchestrator marks the run awaiting, sends, then records the execution id: this completion is ahead of that write.
            return Retry("the run has not recorded this dispatch yet");
        }

        // 3. The run's wait. A completion is accepted only for the step the run awaits as a DISPATCHED step: the orchestrator marks
        // the next step awaiting (undispatched) BEFORE it sends, and records the dispatch after, so in that window the execution id
        // still names the previous dispatch and a redelivered completion of it must not be taken for the new step's (review F1).
        var key = dispatch.Key;
        if (run.State.Awaiting is not { Dispatched: true } awaiting || awaiting.Step != dispatch.Operation)
        {
            // Not waiting for this dispatch: either an earlier delivery of THIS completion recorded it (finish its continuation when
            // that died before the send), or the wait ended or moved on some other way (held, failed, the next step): nothing.
            var recordedHere = run.State.Steps.Values.Any(r => r.Reference == dispatch.OutPrefix);
            if (recordedHere && !run.State.Markers.ContainsKey(dispatch.ContinuationMarker))
            {
                await ContinueAsync(run, dispatch, ct).ConfigureAwait(false);
                return Done();
            }

            return NoChange(recordedHere ? "already handled" : "the run is not awaiting this dispatch");
        }

        // 4. What the engine said.
        var decision = Decide(completion);
        switch (decision.Disposition)
        {
            case Disposition.Record:
                var result = new IntakeHarnessResultReference { Kind = decision.ResultKind!, Reference = dispatch.OutPrefix };
                if (!await _store.RecordExternalStepResultAsync(dispatch.RunId, dispatch.Operation, result, ct, expectedExecutionId: key).ConfigureAwait(false))
                {
                    // Changed since the read (a timeout, a hold, another delivery): decide again on the stored state.
                    return Retry("the run changed while its completion was recorded");
                }

                _logger.LogInformation(
                    "Intake run {RunId}: {Operation} attempt {DispatchAttempt} completed (check {CheckOutcome}, {RefusedEdits} refused edits); the result is stored",
                    dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt, completion.CheckOutcome ?? "none", completion.RefusedEdits);
                await ContinueAsync(run, dispatch, ct).ConfigureAwait(false);
                return Done();

            default:
                var stop = new IntakeExternalStop(
                    decision.Disposition == Disposition.Fail ? IntakeExternalStopKind.Fail : IntakeExternalStopKind.Hold,
                    IntakeEngineOperations.StageOf(dispatch.Operation), decision.Code!, decision.Message!);
                var written = await _store.StopAwaitedStepAsync(dispatch.RunId, dispatch.Operation, key, acceptUnrecordedExecution: false, stop, ct).ConfigureAwait(false);
                switch (written)
                {
                    case IntakeExternalWriteResult.Written:
                        _logger.LogWarning(
                            "Intake run {RunId}: {Operation} attempt {DispatchAttempt} ended with {Outcome}; the run is {Action} ({Code}); nothing continues",
                            dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt, completion.Outcome,
                            decision.Disposition == Disposition.Fail ? "failed" : "held for an administrator", decision.Code);
                        return Done();
                    case IntakeExternalWriteResult.Busy:
                        return Retry("the run is held by another delivery");
                    default:
                        return NoChange("the run is no longer awaiting this dispatch");
                }
        }
    }

    /// <summary>Hands the run back to the orchestrator: an <c>IntakeRun</c> job with its deterministic key, then the marker.</summary>
    private async Task ContinueAsync(IntakeRunSnapshot run, IntakeEngineDispatchId dispatch, CancellationToken ct)
    {
        var job = IntakeRunJobHandler.CreateJob(run.RequestId!.Value, run.RunId, run.CorrelationId ?? dispatch.RunId.ToString("D"));
        await _jobs.SubmitJobAsync(job, ct).ConfigureAwait(false);
        await _store.MarkContinuationEnqueuedAsync(run.RunId, dispatch.ContinuationMarker, job.IdempotencyKey, ct).ConfigureAwait(false);
        _logger.LogInformation("Intake run {RunId}: continuation enqueued after {Operation} attempt {DispatchAttempt}", dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt);
    }

    private enum Disposition { Record, Hold, Fail }

    private readonly record struct Decision(Disposition Disposition, string? ResultKind = null, string? Code = null, string? Message = null);

    private static Decision Decide(IntakeEngineCompletion completion)
    {
        var operation = completion.Dispatch.Operation;
        switch (completion.Outcome)
        {
            case IntakeEngineOutcomes.Completed when operation == IntakeEngineOperations.Segment:
                return new Decision(Disposition.Record, IntakeEngineResultKinds.Segment);

            case IntakeEngineOutcomes.Completed:
                return completion.CheckOutcome switch
                {
                    IntakeEngineOutcomes.CheckPassed => new Decision(Disposition.Record, IntakeEngineResultKinds.RedlinePassed),
                    IntakeEngineOutcomes.CheckFailed => new Decision(Disposition.Hold, Code: IntakeEngineCodes.CheckFailed, Message: "the redline's output check failed; an administrator must review the run"),
                    _ => new Decision(Disposition.Hold, Code: IntakeEngineCodes.ContractError, Message: "the engine reported no check outcome; an administrator must review the run"),
                };

            case IntakeEngineOutcomes.PlanDoesNotFit:
                return new Decision(Disposition.Hold, Code: IntakeEngineCodes.PlanDoesNotFit, Message: "the edit plan does not fit the draft; an administrator must review the run");

            case IntakeEngineOutcomes.InputInvalid:
                return new Decision(Disposition.Fail, Code: IntakeEngineCodes.InputInvalid, Message: "the engine could not read the draft");

            default:
                return new Decision(Disposition.Hold, Code: IntakeEngineCodes.ContractError, Message: "the engine broke its contract; an administrator must review the run");
        }
    }

    private enum RecordedDispatch { Exact, NotYetRecorded, Superseded, Unrelated }

    private static RecordedDispatch ClassifyRecordedDispatch(string? recordedKey, IntakeEngineDispatchId dispatch)
    {
        if (string.IsNullOrEmpty(recordedKey))
        {
            return RecordedDispatch.NotYetRecorded;
        }

        if (!IntakeEngineDispatchId.TryParseKey(recordedKey, out var recorded) || recorded.RunId != dispatch.RunId)
        {
            return RecordedDispatch.Unrelated;
        }

        var comparison = dispatch.Position.CompareTo(recorded.Position);
        return comparison == 0 ? RecordedDispatch.Exact : comparison > 0 ? RecordedDispatch.NotYetRecorded : RecordedDispatch.Superseded;
    }
}
