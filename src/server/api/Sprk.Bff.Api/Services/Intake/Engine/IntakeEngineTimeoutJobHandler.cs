using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake.Engine;

/// <summary>
/// The hard timeout of an engine dispatch (spaarke-intake-pipeline-r1 task 014, FR-09, NFR-05; ADR-052: a scheduled Service Bus
/// message handled by an <see cref="IJobHandler"/>, no timer service). The dispatcher schedules one per request, before it sends the
/// request; when it fires and the run is STILL awaiting that exact dispatch, the run is Failed with <see cref="IntakeEngineCodes.Timeout"/>
/// ("engine timeout") and its request moves to Needs triage.
/// </summary>
/// <remarks>
/// <para><b>Only a waiting run times out.</b> A completion that was handled first cleared the wait, so the timeout of a dispatch that
/// finished, was superseded by a later attempt or operation, or belongs to a run that is already terminal or held changes nothing:
/// <see cref="IntakeRunStore.StopAwaitedStepAsync"/> applies only while the run awaits the dispatch named by the message. The reverse
/// race (a completion arriving after the timeout) finds a terminal run and is a no-op in the completion handler.</para>
/// <para>Registered as an <see cref="IJobHandler"/> on the BFF jobs queue (<c>sdap-jobs</c>): the timeout is the BFF's own message.
/// A run a live delivery holds is retried by the normal back-off; the message never writes under another delivery's claim.</para>
/// <para>ADR-015: ids and closed codes only; the stored message is "engine timeout".</para>
/// </remarks>
public sealed class IntakeEngineTimeoutJobHandler : IJobHandler
{
    private const string TimeoutMessage = "engine timeout";

    private static readonly JsonSerializerOptions PayloadJson = IntakeEngineJson.Options;

    private readonly IntakeRunStore _store;
    private readonly ILogger<IntakeEngineTimeoutJobHandler> _logger;

    public IntakeEngineTimeoutJobHandler(IntakeRunStore store, ILogger<IntakeEngineTimeoutJobHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => IntakeEngineJobTypes.Timeout;

    /// <summary>The scheduled message of one dispatch: ids only, the run's correlation id carried end to end (NFR-06).</summary>
    public static JobContract CreateJob(IntakeEngineDispatchId id, string correlationId, DateTimeOffset createdAt) => new()
    {
        JobId = id.JobIdFor(IntakeEngineJobTypes.Timeout),
        JobType = IntakeEngineJobTypes.Timeout,
        SubjectId = id.RunId.ToString("D"),
        CorrelationId = correlationId,
        IdempotencyKey = id.TimeoutKey,
        CreatedAt = createdAt,
        Payload = JsonSerializer.SerializeToDocument(new IntakeEngineTimeoutPayload(id.Operation, id.RunId, id.DispatchAttempt), PayloadJson),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var id = ParsePayload(job.Payload);
        if (id is null)
        {
            _logger.LogError("Engine timeout job {JobId}: the payload is not a valid dispatch", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: a dispatch (operation, runId, dispatchAttempt) is required", job.Attempt, stopwatch.Elapsed);
        }

        var dispatch = id.Value;
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = job.CorrelationId,
            ["JobId"] = job.JobId,
            ["RunId"] = dispatch.RunId,
            ["Operation"] = dispatch.Operation,
            ["DispatchAttempt"] = dispatch.DispatchAttempt,
        });

        var run = await _store.ReadRunAsync(dispatch.RunId, ct).ConfigureAwait(false);
        if (run is null || run.JobType != (int)ProcessingJobType.IntakeRun)
        {
            _logger.LogInformation("Intake run {RunId}: not an existing intake run; the engine timeout changes nothing", dispatch.RunId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var stop = new IntakeExternalStop(
            IntakeExternalStopKind.Fail, IntakeEngineOperations.StageOf(dispatch.Operation), IntakeEngineCodes.Timeout, TimeoutMessage);
        var result = await _store.StopAwaitedStepAsync(dispatch.RunId, dispatch.Key, acceptUnrecordedExecution: true, stop, ct).ConfigureAwait(false);
        switch (result)
        {
            case IntakeExternalWriteResult.Written:
                _logger.LogWarning(
                    "Intake run {RunId}: no completion for {Operation} attempt {DispatchAttempt} within the hard timeout; the run is Failed ({Code})",
                    dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt, IntakeEngineCodes.Timeout);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);

            case IntakeExternalWriteResult.Busy:
                // Another delivery holds the run (the orchestrator is mid-step): the normal back-off tries again.
                _logger.LogInformation("Intake run {RunId}: held by a live delivery; the engine timeout is retried", dispatch.RunId);
                return JobOutcome.Failure(job.JobId, JobType, "the run is held by another delivery", job.Attempt, stopwatch.Elapsed);

            default:
                _logger.LogInformation(
                    "Intake run {RunId}: not awaiting {Operation} attempt {DispatchAttempt}; the engine timeout changes nothing",
                    dispatch.RunId, dispatch.Operation, dispatch.DispatchAttempt);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
    }

    private static IntakeEngineDispatchId? ParsePayload(JsonDocument? payload)
    {
        if (payload is null || payload.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var parsed = payload.RootElement.Deserialize<IntakeEngineTimeoutPayload>(PayloadJson);
            if (parsed is null || parsed.Operation is null)
            {
                return null;
            }

            var id = new IntakeEngineDispatchId(parsed.RunId, parsed.Operation, parsed.DispatchAttempt);
            return id.IsValid ? id : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
