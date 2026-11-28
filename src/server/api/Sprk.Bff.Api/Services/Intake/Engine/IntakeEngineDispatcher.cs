using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake.Engine;

/// <summary>
/// One request to the engine job (spaarke-intake-pipeline-r1 task 014): which run, which operation, which attempt, and where the
/// bytes come from. The bytes are fetched only when they are not staged yet.
/// </summary>
public sealed record IntakeEngineDispatchRequest
{
    public required Guid RunId { get; init; }

    /// <summary><see cref="IntakeEngineOperations.Segment"/> or <see cref="IntakeEngineOperations.Redline"/>.</summary>
    public required string Operation { get; init; }

    /// <summary>
    /// 1 for the first pass. A second pass over the same operation (the refused-edit rewrite, a re-drive after a held check) MUST
    /// use a higher attempt: attempt N is spent once its completion was handled, and the engine and the dispatch marker both treat a
    /// repeat of N as a duplicate.
    /// </summary>
    public int DispatchAttempt { get; init; } = 1;

    /// <summary>The run's correlation id (NFR-06), carried on the request, the timeout message and the engine's logs.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Opens the submitted draft (read from SharePoint Embedded by the harness). Called only when <c>{runId}/in/source.docx</c> is absent.</summary>
    public required Func<CancellationToken, Task<Stream>> OpenSource { get; init; }

    /// <summary>Opens the edit plan JSON of a redline (required for it). Called only when this attempt's plan blob is absent.</summary>
    public Func<CancellationToken, Task<Stream>>? OpenPlan { get; init; }
}

/// <summary>
/// Stages the engine's inputs and sends its request (spaarke-intake-pipeline-r1 task 014, FR-07, FR-09, NFR-03, NFR-05, NFR-06):
/// the claim-check half of ADR-004 A2. Called by a harness step (the NDA harness, task 015) that returns its outcome as the step's.
/// </summary>
/// <remarks>
/// <para><b>Order.</b> (1) stage the draft and, for a redline, the plan, each with create-if-not-exists; (2) a repeat of an attempt
/// that was already sent stops here (the dispatch marker); (3) schedule the hard-timeout message; (4) send the request, ids only;
/// (5) write the dispatch marker. The timeout is scheduled BEFORE the send, so a request can never be in flight without one; a crash
/// after the send repeats (3) and (4) with the same message ids, which the engine's atomic marker turns into a no-op, and a second
/// timeout message does nothing once the first has run (the timeout handler checks the run).</para>
/// <para><b>The execution id</b> returned to the orchestrator is the request's idempotency key
/// (<c>IntakeEngine:{runId}:{operation}:{attempt}</c>): the orchestrator records it on <c>sprk_engineexecutionid</c>, and the
/// completion handler accepts only a completion naming exactly that dispatch.</para>
/// <para><b>Logging (ADR-015).</b> Run id, operation, attempt and counts only; never bytes, plan content or exception messages.</para>
/// <para>§11: <i>Existing</i> - <c>JobSubmissionService</c> (extended by two methods) and <c>BlobGateway</c> through
/// <see cref="IntakeEngineStaging"/>. No BFF code talks to an out-of-process engine. <i>Extension</i> - no existing service stages a
/// claim-check input and waits on a consumer's completion. <i>Cost of doing nothing</i> - the harness cannot hand the engine its
/// input, and a hung engine leaves a run waiting forever.</para>
/// </remarks>
public sealed class IntakeEngineDispatcher
{
    private readonly IntakeEngineStaging _staging;
    private readonly JobSubmissionService _jobs;
    private readonly IOptionsMonitor<IntakePipelineOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IntakeEngineDispatcher> _logger;

    public IntakeEngineDispatcher(
        IntakeEngineStaging staging,
        JobSubmissionService jobs,
        IOptionsMonitor<IntakePipelineOptions> options,
        TimeProvider time,
        ILogger<IntakeEngineDispatcher> logger)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stages and sends one dispatch. Returns <see cref="IntakeHarnessStepOutcome.Dispatched"/> with the dispatch key (also when the
    /// attempt was already sent), or <see cref="IntakeHarnessStepOutcome.Held"/> with <see cref="IntakeEngineCodes.NotConfigured"/>
    /// when this stamp has no engine. Storage and queue faults throw: the orchestrator retries the delivery, and every step here is
    /// idempotent.
    /// </summary>
    /// <exception cref="ArgumentException">The run id, operation or attempt is not valid, or a redline has no plan.</exception>
    public async Task<IntakeHarnessStepOutcome> DispatchAsync(IntakeEngineDispatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = new IntakeEngineDispatchId(request.RunId, request.Operation, request.DispatchAttempt);
        if (!id.IsValid)
        {
            throw new ArgumentException("The dispatch needs a run id, a known operation and an attempt from 1 to 1000.", nameof(request));
        }

        if (id.Operation == IntakeEngineOperations.Redline && request.OpenPlan is null)
        {
            throw new ArgumentException("A redline dispatch needs the edit plan.", nameof(request));
        }

        var correlationId = request.CorrelationId;
        if (!IntakeEngineJson.IsEngineSafeCorrelationId(correlationId))
        {
            // The engine dead-letters an envelope it cannot accept, which would leave the run waiting for the timeout: the run id is
            // always acceptable and still ties the logs together.
            _logger.LogWarning("Intake run {RunId}: the correlation id is not one the engine accepts; the run id is sent in its place", id.RunId);
            correlationId = id.RunId.ToString("D");
        }

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["RunId"] = id.RunId,
            ["Operation"] = id.Operation,
            ["DispatchAttempt"] = id.DispatchAttempt,
        });

        if (!_staging.IsConfigured)
        {
            _logger.LogWarning("Intake run {RunId}: no engine is configured on this stamp; nothing was staged or sent", id.RunId);
            return IntakeHarnessStepOutcome.Held(IntakeEngineCodes.NotConfigured);
        }

        // 1. Stage the inputs. Create-if-not-exists: a repeat dispatch (or the redline after the segment) rewrites nothing.
        var stagedSource = await _staging.StageIfAbsentAsync(id.Source, request.OpenSource, IntakeEngineStaging.DocxContentType, ct).ConfigureAwait(false);
        var stagedPlan = false;
        if (id.Operation == IntakeEngineOperations.Redline)
        {
            stagedPlan = await _staging.StageIfAbsentAsync(id.Plan, request.OpenPlan!, IntakeEngineStaging.JsonContentType, ct).ConfigureAwait(false);
        }

        // 2. A repeat of an attempt that was sent sends nothing: the request is in flight, or its completion is on its way.
        if (await _staging.ExistsAsync(id.DispatchMarker, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Intake run {RunId}: {Operation} attempt {DispatchAttempt} was already dispatched; nothing sent", id.RunId, id.Operation, id.DispatchAttempt);
            return IntakeHarnessStepOutcome.Dispatched(id.Key);
        }

        var options = _options.CurrentValue.Engine;
        var now = _time.GetUtcNow();
        var dueAt = now + options.EffectiveTimeout();

        // 3. The hard timeout first, so no request is ever in flight without one (NFR-05).
        await _jobs.ScheduleJobAsync(IntakeEngineTimeoutJobHandler.CreateJob(id, correlationId!, now), dueAt, ct).ConfigureAwait(false);

        // 4. The request: ids only. The envelope is the Job Contract JSON the engine accepts (envelope.py), closed field set.
        var job = new JobContract
        {
            JobId = id.JobIdFor(IntakeEngineJobTypes.Request),
            JobType = IntakeEngineJobTypes.Request,
            SubjectId = id.RunId.ToString("D"),
            CorrelationId = correlationId!,
            IdempotencyKey = id.Key,
            Attempt = 1,
            MaxAttempts = JobContract.MaxDeliveryCount,
            CreatedAt = now,
            Payload = JsonSerializer.SerializeToDocument(
                new IntakeEngineRequestPayload(id.Operation, id.RunId, id.DispatchAttempt), IntakeEngineJson.Options),
        };
        await _jobs.SubmitToNamedQueueAsync(job, options.RequestQueueName, ct).ConfigureAwait(false);

        // 5. The marker, after the send: a crash before it repeats the (idempotent) send, never loses one.
        await _staging.CreateIfAbsentAsync(
            id.DispatchMarker,
            BinaryData.FromObjectAsJson(new { idempotencyKey = id.Key, sentAt = now }, IntakeEngineJson.Options),
            IntakeEngineStaging.JsonContentType,
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Intake run {RunId}: {Operation} attempt {DispatchAttempt} dispatched (staged source {StagedSource}, plan {StagedPlan}); the hard timeout is at {TimeoutAt:o}",
            id.RunId, id.Operation, id.DispatchAttempt, stagedSource, stagedPlan, dueAt);
        return IntakeHarnessStepOutcome.Dispatched(id.Key);
    }
}
