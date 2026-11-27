using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake;

/// <summary>
/// ADR-004 job handler for <see cref="JobTypeName"/> (spaarke-intake-pipeline-r1 task 013, spec FR-07, NFR-01, NFR-06,
/// NFR-08): drives one intake run (<c>sprk_processingjob</c>, job type 7) through Receive, Store, Classify, Route, Analyse,
/// Build, Persist, Approve, Return and Record, with the run row as the state machine (design §4, ADR-052 written reason).
/// </summary>
/// <remarks>
/// <para><b>Step 0, the invoice pipeline's rules copied (design §3.3; <c>InvoiceSubmissionProcessingJobHandler</c> is not
/// edited).</b> (a) the payload <c>{requestId, runId}</c>, ids only (invalid: Poisoned); (b) the re-read: a missing run is a
/// no-op Success, a row that is not this request's intake run is Poisoned without a write; (c) <b>terminal first</b>: a
/// Completed, Failed or Cancelled run is a no-op Success; (d) the <b>atomic claim</b> (<see cref="IntakeRunStore.TryClaimAsync"/>,
/// a write conditional on the row version read; never <c>IIdempotencyService</c> alone, #984): a delivery that cannot take
/// it writes nothing and returns Failure (Poisoned on the final delivery); (e) the kill switch is read after the terminal
/// rule, before the harness is called; (f) <b>resume from stored state</b>: each stage's result is stored on the run
/// before the run advances, and a stage or harness step whose result is stored is never run again (FR-07a).</para>
/// <para><b>Write order.</b> Status In Progress ("Processing") is written at Receive, before any AI call. A marker is
/// written before every create or send another task plugs in (triage Work Item 016, approval Work Item 017, door return
/// 021/043, status email 020), with a deterministic key the step is idempotent on. The terminal status (Completed or
/// Failed) is the LAST write of a run, in the same write that releases the claim.</para>
/// <para><b>Held.</b> A run that needs an administrator (harness switched off, AI switched off, no door return, the final
/// delivery failed) keeps status In Progress, gets admin-attention text in <c>sprk_errorcode</c>/<c>sprk_errormessage</c>
/// (codes and ids only) and the job is Poisoned (dead-lettered, monitored). It is never Completed or Failed; replaying or
/// re-driving it resumes from the stored state (FR-07d, NFR-08).</para>
/// <para><b>Waiting.</b> An external harness step (engine job) and the approval are waits, not deliveries held open: the
/// run records what it waits for and the job ends Success. The engine completion (task 014) and the approval endpoint
/// (task 017) store their result and enqueue <see cref="CreateJob"/> to continue the run.</para>
/// <para><b>Logging (ADR-015, NFR-06).</b> Every log line is inside a scope carrying the job's <c>CorrelationId</c>, the run
/// and request ids; lines carry ids, stage names and codes only — never document text, classification reasoning or plan
/// content.</para>
/// <para>§11: <i>Existing</i> - <c>InvoiceSubmissionProcessingJobHandler</c> / <c>InvoiceSubmissionRerunJobHandler</c>
/// (reuse-scan row 1). <i>Extension</i> - invoice-typed throughout and frozen until eBilling UAT; the generic framework
/// (<see cref="IJobHandler"/>, <see cref="JobContract"/>) is reused as is. <i>Cost of doing nothing</i> - Front Door,
/// Console and email requests are never processed: no stage advances and nothing reaches Awaiting approval (FR-07,
/// success criterion 4).</para>
/// </remarks>
public sealed class IntakeRunJobHandler : IJobHandler
{
    public const string JobTypeName = "IntakeRun";

    /// <summary>Below this classification confidence the request goes to manual triage (spec Assumptions, FR-10).</summary>
    internal const double TriageConfidenceThreshold = 0.85;

    /// <summary><c>sprk_servicerequest.sprk_requesttype</c> option → routing token (the <c>intake-harness</c> Binding's consumer code).</summary>
    internal static readonly IReadOnlyDictionary<int, string> RequestTypeTokens = new Dictionary<int, string>
    {
        [100000000] = "nda",
        [100000001] = "invention-submission",
        [100000002] = "policy-question",
        [100000003] = "trademark-search",
    };

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private readonly IntakeRunStore _store;
    private readonly IntakeHarnessRegistry _registry;
    private readonly IAgreementAi _agreementAi;
    private readonly IntakeContentTextReader _text;
    private readonly IEnumerable<IIntakeDoorReturn> _doors;
    private readonly IOptionsMonitor<IntakePipelineOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IntakeRunJobHandler> _logger;

    public IntakeRunJobHandler(
        IntakeRunStore store,
        IntakeHarnessRegistry registry,
        IAgreementAi agreementAi,
        IntakeContentTextReader text,
        IEnumerable<IIntakeDoorReturn> doors,
        IOptionsMonitor<IntakePipelineOptions> options,
        TimeProvider time,
        ILogger<IntakeRunJobHandler> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _agreementAi = agreementAi ?? throw new ArgumentNullException(nameof(agreementAi));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _doors = doors ?? throw new ArgumentNullException(nameof(doors));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    /// <summary>
    /// The job that starts or continues a run: ids only, the request's correlation id carried end to end (NFR-06). Used by
    /// the doors, the engine completion (014) and the approval endpoint (017). Each enqueue is a new job (a new claim
    /// owner); the run, not the message, is what is idempotent.
    /// </summary>
    public static JobContract CreateJob(Guid requestId, Guid runId, string correlationId) => new()
    {
        JobType = JobTypeName,
        SubjectId = runId.ToString("D"),
        CorrelationId = correlationId,
        IdempotencyKey = $"intake-run-{runId:N}",
        Payload = JsonSerializer.SerializeToDocument(new IntakeRunJobPayload(requestId, runId), PayloadJson),
    };

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = ParsePayload(job.Payload);
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = job.CorrelationId,
            ["JobId"] = job.JobId,
            ["RunId"] = payload?.RunId,
            ["RequestId"] = payload?.RequestId,
        });

        if (payload is null)
        {
            _logger.LogError("Intake run job {JobId}: the payload is missing or has no requestId/runId", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload: requestId and runId are required", job.Attempt, stopwatch.Elapsed);
        }

        // The claim owner is THIS delivery, not the job: a redelivery of the same message (lock lost while this delivery
        // still runs) must not pass this delivery's fence. IntakeRunStore.ClaimOwner keeps the job id for the takeover rule.
        var delivery = new Delivery(job, payload.RequestId, payload.RunId, IntakeRunStore.ClaimOwner(job.JobId), stopwatch);
        var claimed = false;
        try
        {
            // 0b. Re-read. A missing run (deleted by an administrator) is not a failure.
            var run = await _store.ReadRunAsync(payload.RunId, ct).ConfigureAwait(false);
            if (run is null)
            {
                _logger.LogInformation("Intake run {RunId} no longer exists; nothing to do", payload.RunId);
                return Done(delivery);
            }

            if (run.JobType != (int)ProcessingJobType.IntakeRun || run.RequestId != payload.RequestId)
            {
                // Never touch a row that is not this request's intake run (FR-06b: Office rows share the table).
                _logger.LogError("Intake run job {JobId}: row {RunId} is not an intake run of request {RequestId}", job.JobId, payload.RunId, payload.RequestId);
                return JobOutcome.Poisoned(job.JobId, JobType, "the run is not an intake run of the request", job.Attempt, stopwatch.Elapsed);
            }

            // 0c. Terminal FIRST: Completed / Failed / Cancelled is written last, so it means the run is finished.
            if (IntakeRunStore.IsTerminalStatus(run.Status))
            {
                _logger.LogInformation("Intake run {RunId} is already terminal ({Status}); nothing to do", payload.RunId, run.Status);
                return Done(delivery);
            }

            // 0d. The atomic claim (ADR-004 A1 §4).
            var state = await _store.TryClaimAsync(run, delivery.Owner, ct).ConfigureAwait(false);
            if (state is null)
            {
                return Busy(delivery);
            }

            claimed = true;
            return await RunStagesAsync(delivery, state, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (IntakeClaimLostException)
        {
            // Another delivery took the run over: write nothing more.
            claimed = false;
            return Busy(delivery);
        }
        catch (Exception ex)
        {
            var code = ex switch
            {
                AgreementAiException ai => ai.ReasonCode,
                _ => IntakeRunCodes.ProcessingFailed,
            };
            _logger.LogError("Intake run {RunId} failed at a transient step ({ExceptionType}, {ReasonCode})", payload.RunId, ex.GetType().Name, code);
            if (!claimed || !job.IsFinalDelivery)
            {
                return JobOutcome.Failure(job.JobId, JobType, code, job.Attempt, stopwatch.Elapsed);
            }

            // The final delivery: hold with admin-attention text and dead-letter; never Completed, never Failed (FR-07d).
            try
            {
                return await HoldAsync(delivery, StageOf(await _store.ReadRunAsync(payload.RunId, ct).ConfigureAwait(false)), IntakeRunCodes.Exhausted, code, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception writeFault) when (writeFault is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError("Intake run {RunId}: the hold could not be recorded ({ExceptionType})", payload.RunId, writeFault.GetType().Name);
                return JobOutcome.Poisoned(job.JobId, JobType, IntakeRunCodes.Exhausted, job.Attempt, stopwatch.Elapsed);
            }
        }
        finally
        {
            if (claimed)
            {
                try
                {
                    // A no-op when the delivery's last write already released it.
                    await _store.ReleaseClaimAsync(payload.RunId, delivery.Owner, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Intake run {RunId}: the claim could not be released ({ExceptionType}); it expires on its own", payload.RunId, ex.GetType().Name);
                }
            }
        }
    }

    private async Task<JobOutcome> RunStagesAsync(Delivery d, IntakeRunState state, CancellationToken ct)
    {
        var request = await _store.ReadRequestAsync(d.RequestId, ct).ConfigureAwait(false);
        if (request?.IntakeStage is null)
        {
            // Not an intake request (missing, or an ontology outbound row with no stage): never processed.
            return await FailAsync(d, IntakeRunStage.Receive, IntakeRunCodes.NotAnIntakeRequest, ct).ConfigureAwait(false);
        }

        // 1. Receive: "Processing" before any AI call, then the request points at this run.
        if (!state.IsCompleted(IntakeRunStage.Receive))
        {
            await WriteAsync(d, IntakeRunStage.Receive, null, ct, new Dictionary<string, object>
            {
                [IntakeRunStore.RunStatus] = new OptionSetValue((int)ProcessingJobStatus.InProgress),
                [IntakeRunStore.RunStarted] = _time.GetUtcNow().UtcDateTime,
                [IntakeRunStore.RunStages] = StagesJson,
            }).ConfigureAwait(false);
            await _store.WriteRequestStageAsync(d.RequestId, d.RunId, IntakeRequestStages.InReview, makeCurrentRun: true, ct).ConfigureAwait(false);
            state = await WriteAsync(d, IntakeRunStage.Receive, s => s.MarkCompleted(IntakeRunStage.Receive), ct).ConfigureAwait(false);
        }

        // 2. Store: the door filed the content and named it on the run; every id must be a live document of THIS request.
        if (!state.IsCompleted(IntakeRunStage.Store))
        {
            foreach (var documentId in state.ContentDocumentIds)
            {
                if (!await _store.IsRequestContentAsync(d.RequestId, documentId, ct).ConfigureAwait(false))
                {
                    _logger.LogWarning("Intake run {RunId}: content {DocumentId} is not a live document of the request", d.RunId, documentId);
                    return await FailAsync(d, IntakeRunStage.Store, IntakeRunCodes.ContentInvalid, ct).ConfigureAwait(false);
                }
            }

            state = await WriteAsync(d, IntakeRunStage.Store, s => s.MarkCompleted(IntakeRunStage.Store), ct).ConfigureAwait(false);
        }

        // 3. Classify (AI). Stored before Route; a stored classification is never computed again (FR-07a).
        if (!state.IsCompleted(IntakeRunStage.Classify))
        {
            IntakeClassification? classification = null;
            if (state.ContentDocumentIds.Count > 0)
            {
                await WriteAsync(d, IntakeRunStage.Classify, null, ct).ConfigureAwait(false);
                var text = await _text.ReadTextAsync(state.ContentDocumentIds[0], ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AgreementClassifyResult result;
                    try
                    {
                        result = await _agreementAi.ClassifyAsync(new AgreementClassifyRequest { DocumentText = text, SubjectId = d.RunId }, ct)
                            .ConfigureAwait(false);
                    }
                    catch (FeatureDisabledException)
                    {
                        return await HoldAsync(d, IntakeRunStage.Classify, IntakeRunCodes.AiDisabled, null, ct).ConfigureAwait(false);
                    }

                    classification = new IntakeClassification(result.IsAgreement, result.Top?.SubDomainKey, result.Top?.Confidence ?? 0);
                }
            }

            state = await WriteAsync(d, IntakeRunStage.Classify, s =>
            {
                s.Classification = classification;
                s.MarkCompleted(IntakeRunStage.Classify);
            }, ct).ConfigureAwait(false);
        }

        // 4. Route: request type + classified document type → harness, or manual triage (FR-08c, FR-10). Never re-routed.
        if (!state.IsCompleted(IntakeRunStage.Route))
        {
            var decision = await RouteAsync(request, state, ct).ConfigureAwait(false);
            var fields = decision.HarnessKey is { } key ? new Dictionary<string, object> { [IntakeRunStore.RunHarnessKey] = key } : null;
            state = await WriteAsync(d, IntakeRunStage.Route, s =>
            {
                s.Route = decision;
                s.MarkCompleted(IntakeRunStage.Route);
            }, ct, fields).ConfigureAwait(false);
        }

        if (state.Route is not { HarnessKey: { } harnessKey } route)
        {
            return await TriageAsync(d, state.Route?.TriageCode ?? IntakeRunCodes.TriageNoHarness, ct).ConfigureAwait(false);
        }

        var harnessRoute = new IntakeHarnessRoute(route.RequestType ?? string.Empty, route.DocumentType);
        var harness = _registry.Resolve(harnessKey, harnessRoute).Harness;
        if (harness is null || !HasValidSteps(harness))
        {
            return await HoldAsync(d, IntakeRunStage.Route, IntakeRunCodes.HarnessContract, harnessKey, ct).ConfigureAwait(false);
        }

        var harnessRequest = new IntakeHarnessRequest
        {
            RequestId = d.RequestId,
            RunId = d.RunId,
            Route = harnessRoute,
            Content = state.ContentDocumentIds.Select(id => new IntakeContentReference { DocumentId = id }).ToList(),
            CorrelationId = d.Job.CorrelationId,
        };

        // 5–6. Analyse and Build, one harness step at a time; each result stored before the next step runs.
        foreach (var step in harness.Steps)
        {
            if (state.Steps.ContainsKey(step.Name))
            {
                continue;
            }

            var stage = (IntakeRunStage)(int)step.Stage;
            if (state.Awaiting is { Dispatched: true } awaiting && awaiting.Step == step.Name)
            {
                // The engine job is running; its completion continues the run (a duplicate delivery does not re-dispatch).
                _logger.LogInformation("Intake run {RunId}: waiting for step {Step}", d.RunId, step.Name);
                await WriteAsync(d, stage, null, ct, release: true).ConfigureAwait(false);
                return Done(d);
            }

            if (!_options.CurrentValue.IsHarnessEnabled(harness.Key))
            {
                return await HoldAsync(d, stage, IntakeRunCodes.HarnessDisabled, harness.Key, ct).ConfigureAwait(false);
            }

            // The stage is shown before the step runs; an external step is marked awaiting BEFORE it is sent.
            state = await WriteAsync(d, stage, s =>
            {
                if (step.External)
                {
                    s.Awaiting = new IntakeAwaitingStep(step.Name, _time.GetUtcNow());
                }
            }, ct).ConfigureAwait(false);

            var outcome = await harness.RunStepAsync(step, harnessRequest, state.Steps, ct).ConfigureAwait(false);
            switch (outcome.Kind)
            {
                case IntakeHarnessStepOutcomeKind.Completed when outcome.Result is not null:
                    state = await WriteAsync(d, stage, s =>
                    {
                        s.Steps[step.Name] = outcome.Result;
                        s.Awaiting = null;
                        if (harness.Steps.Where(x => x.Stage == step.Stage).All(x => s.Steps.ContainsKey(x.Name)))
                        {
                            s.MarkCompleted(stage);
                        }
                    }, ct).ConfigureAwait(false);
                    break;

                case IntakeHarnessStepOutcomeKind.Dispatched when step.External:
                    var fields = outcome.ExecutionId is { Length: > 0 } execution
                        ? new Dictionary<string, object> { [IntakeRunStore.RunEngineExecution] = execution }
                        : null;
                    await WriteAsync(d, stage, s =>
                    {
                        // The completion may already have recorded the result: keep it.
                        if (!s.Steps.ContainsKey(step.Name))
                        {
                            s.Awaiting = new IntakeAwaitingStep(step.Name, s.Awaiting?.Since ?? _time.GetUtcNow(), Dispatched: true);
                        }
                    }, ct, fields, release: true).ConfigureAwait(false);
                    _logger.LogInformation("Intake run {RunId}: step {Step} dispatched; the run waits for its completion", d.RunId, step.Name);
                    return Done(d);

                case IntakeHarnessStepOutcomeKind.Held:
                    return await HoldAsync(d, stage, outcome.HoldCode ?? IntakeRunCodes.HarnessContract, harness.Key, ct).ConfigureAwait(false);

                default:
                    // A result without its reference, or Dispatched from a step not declared external: a harness bug.
                    return await HoldAsync(d, stage, IntakeRunCodes.HarnessContract, harness.Key, ct).ConfigureAwait(false);
            }
        }

        // 7. Persist: the harness files the outputs in one idempotent step; the ids are stored before Approve.
        if (!state.IsCompleted(IntakeRunStage.Persist))
        {
            if (!_options.CurrentValue.IsHarnessEnabled(harness.Key))
            {
                return await HoldAsync(d, IntakeRunStage.Persist, IntakeRunCodes.HarnessDisabled, harness.Key, ct).ConfigureAwait(false);
            }

            await WriteAsync(d, IntakeRunStage.Persist, null, ct).ConfigureAwait(false);
            var persisted = await harness.PersistAsync(harnessRequest, state.Steps, ct).ConfigureAwait(false);
            var fields = persisted.OutputDocumentId is { } output && output != Guid.Empty
                ? new Dictionary<string, object> { [IntakeRunStore.RunOutputDocument] = new EntityReference(IntakeRunStore.DocumentEntity, output) }
                : null;
            state = await WriteAsync(d, IntakeRunStage.Persist, s =>
            {
                s.Outputs = new IntakePersistedOutputs(
                    new Dictionary<string, Guid>(persisted.DocumentIdsByRole, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, Guid>(persisted.ResultIdsByKind, StringComparer.OrdinalIgnoreCase),
                    persisted.OutputDocumentId,
                    persisted.ApprovalRequired,
                    persisted.ReturnPayload.DocumentRoles.ToList());
                s.MarkCompleted(IntakeRunStage.Persist);
            }, ct, fields).ConfigureAwait(false);
        }

        // 8. Approve: a wait as a status. AI output never leaves without approval unless the harness says so (FR-11).
        if (!state.IsCompleted(IntakeRunStage.Approve))
        {
            if (state.Outputs?.ApprovalRequired != false && state.ApprovedOn is null)
            {
                return await AwaitApprovalAsync(d, state, ct).ConfigureAwait(false);
            }

            state = await WriteAsync(d, IntakeRunStage.Approve, s => s.MarkCompleted(IntakeRunStage.Approve), ct).ConfigureAwait(false);
        }

        // 9. Return through the door the request came in by; the marker is written BEFORE the door is called.
        if (!state.IsCompleted(IntakeRunStage.Return))
        {
            var door = _doors.FirstOrDefault(x => x.Channel == request.Channel);
            if (door is null)
            {
                return await HoldAsync(d, IntakeRunStage.Return, IntakeRunCodes.ReturnUnavailable, null, ct).ConfigureAwait(false);
            }

            var returnKey = MarkerKey("return", d.RunId);
            state = await WriteAsync(d, IntakeRunStage.Return, s => s.Markers["return"] = returnKey, ct).ConfigureAwait(false);
            await door.ReturnAsync(new IntakeReturnContext
            {
                RequestId = d.RequestId,
                RunId = d.RunId,
                IdempotencyKey = returnKey,
                DocumentIdsByRole = state.Outputs?.DocumentIdsByRole ?? new Dictionary<string, Guid>(),
                ReturnDocumentRoles = state.Outputs?.ReturnDocumentRoles ?? Array.Empty<string>(),
                CorrelationId = d.Job.CorrelationId,
            }, ct).ConfigureAwait(false);
            state = await WriteAsync(d, IntakeRunStage.Return, s => s.MarkCompleted(IntakeRunStage.Return), ct).ConfigureAwait(false);
        }

        // 10. Record. [Task 020 slot: the requester's status email, once, under the "record" marker key.] The approval's
        // Decision Record is written by the approval endpoint (task 017). The terminal status is the LAST write.
        await WriteAsync(d, IntakeRunStage.Record, s => s.Markers["record"] = MarkerKey("record", d.RunId), ct).ConfigureAwait(false);
        await _store.WriteRequestStageAsync(d.RequestId, d.RunId, IntakeRequestStages.Returned, makeCurrentRun: false, ct).ConfigureAwait(false);
        return await CompleteAsync(d, IntakeRunStage.Record, ct).ConfigureAwait(false);
    }

    private async Task<IntakeRouteDecision> RouteAsync(IntakeRequestSnapshot request, IntakeRunState state, CancellationToken ct)
    {
        var requestType = request.RequestType is { } option && RequestTypeTokens.TryGetValue(option, out var token) ? token : null;
        var classification = state.Classification;

        string? triage = null;
        if (state.ContentDocumentIds.Count == 0)
        {
            triage = IntakeRunCodes.TriageNoContent;
        }
        else if (classification is null)
        {
            triage = IntakeRunCodes.TriageUnreadable;
        }
        else if (!classification.IsAgreement || classification.SubDomain is null)
        {
            triage = IntakeRunCodes.TriageNotAgreement;
        }
        else if (classification.Confidence < TriageConfidenceThreshold)
        {
            triage = IntakeRunCodes.TriageLowConfidence;
        }

        if (triage is not null)
        {
            return new IntakeRouteDecision(requestType, classification?.SubDomain, HarnessKey: null, triage);
        }

        var resolution = await _registry.ResolveAsync(requestType, classification!.SubDomain, ct).ConfigureAwait(false);
        _logger.LogInformation("Intake request {RequestId}: routing outcome {Outcome}, harness {HarnessKey}", request.RequestId, resolution.Outcome, resolution.HarnessKey);
        return resolution.HasHarness
            ? new IntakeRouteDecision(requestType, classification.SubDomain, resolution.Harness!.Key, TriageCode: null)
            : new IntakeRouteDecision(requestType, classification.SubDomain, HarnessKey: null, IntakeRunCodes.TriageNoHarness);
    }

    /// <summary>
    /// The triage slot (FR-10): the request moves to Needs triage, no AI output and no reply, and the run ends Completed
    /// (a no-op, never Failed). [Task 016 slot: <c>IntakeTriageStep</c> creates the team's one Work Item under the
    /// <c>triage</c> marker key, between the marker and the terminal write.]
    /// </summary>
    private async Task<JobOutcome> TriageAsync(Delivery d, string reasonCode, CancellationToken ct)
    {
        _logger.LogInformation("Intake run {RunId}: manual triage ({ReasonCode})", d.RunId, reasonCode);
        await WriteAsync(d, IntakeRunStage.Route, s => s.Markers["triage"] = MarkerKey("triage", d.RunId), ct).ConfigureAwait(false);
        await _store.WriteRequestStageAsync(d.RequestId, d.RunId, IntakeRequestStages.NeedsTriage, makeCurrentRun: false, ct).ConfigureAwait(false);
        return await CompleteAsync(d, IntakeRunStage.Route, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The approval slot (stage 8): the request moves to Awaiting approval and the run waits (status In Progress, stage
    /// Approve). [Task 017 slot: the team's approval Work Item under the <c>approve</c> marker key; the decision endpoint
    /// sets <see cref="IntakeRunState.ApprovedOn"/> and enqueues <see cref="CreateJob"/>, which continues at Return.] A
    /// redelivery while waiting changes nothing.
    /// </summary>
    private async Task<JobOutcome> AwaitApprovalAsync(Delivery d, IntakeRunState state, CancellationToken ct)
    {
        if (!state.Markers.ContainsKey("approve"))
        {
            await WriteAsync(d, IntakeRunStage.Approve, s => s.Markers["approve"] = MarkerKey("approve", d.RunId), ct).ConfigureAwait(false);
        }

        await _store.WriteRequestStageAsync(d.RequestId, d.RunId, IntakeRequestStages.AwaitingApproval, makeCurrentRun: false, ct).ConfigureAwait(false);
        await WriteAsync(d, IntakeRunStage.Approve, null, ct, release: true).ConfigureAwait(false);
        _logger.LogInformation("Intake run {RunId}: awaiting approval", d.RunId);
        return Done(d);
    }

    /// <summary>The terminal Completed write: the LAST write of the run, in the same write that releases the claim.</summary>
    private async Task<JobOutcome> CompleteAsync(Delivery d, IntakeRunStage stage, CancellationToken ct)
    {
        await WriteAsync(d, stage, s =>
        {
            s.MarkCompleted(stage);
            s.Held = null;
        }, ct, new Dictionary<string, object>
        {
            [IntakeRunStore.RunStatus] = new OptionSetValue((int)ProcessingJobStatus.Completed),
            [IntakeRunStore.RunCompleted] = _time.GetUtcNow().UtcDateTime,
            [IntakeRunStore.RunErrorCode] = DBNull.Value,
            [IntakeRunStore.RunErrorMessage] = DBNull.Value,
        }, release: true).ConfigureAwait(false);
        _logger.LogInformation("Intake run {RunId} completed at {Stage}", d.RunId, stage);
        return Done(d);
    }

    /// <summary>A permanent failure (the run can never succeed): terminal Failed with the code, written last; Poisoned.</summary>
    private async Task<JobOutcome> FailAsync(Delivery d, IntakeRunStage stage, string code, CancellationToken ct)
    {
        await WriteAsync(d, stage, null, ct, new Dictionary<string, object>
        {
            [IntakeRunStore.RunStatus] = new OptionSetValue((int)ProcessingJobStatus.Failed),
            [IntakeRunStore.RunCompleted] = _time.GetUtcNow().UtcDateTime,
            [IntakeRunStore.RunErrorCode] = code,
            [IntakeRunStore.RunErrorMessage] = AttentionText(d.RunId, stage, code, null),
        }, release: true).ConfigureAwait(false);
        _logger.LogError("Intake run {RunId} failed at {Stage} ({ReasonCode})", d.RunId, stage, code);
        return JobOutcome.Poisoned(d.Job.JobId, JobType, code, d.Job.Attempt, d.Stopwatch.Elapsed);
    }

    /// <summary>
    /// Held for an administrator: status stays In Progress, admin-attention text is written, the claim is released in the
    /// same write, and the job is Poisoned (dead-lettered). Not terminal: a replay or re-drive resumes from stored state.
    /// </summary>
    private async Task<JobOutcome> HoldAsync(Delivery d, IntakeRunStage stage, string code, string? detail, CancellationToken ct)
    {
        await WriteAsync(d, stage, s => s.Held = new IntakeRunHold(stage.ToString(), code, _time.GetUtcNow()), ct, new Dictionary<string, object>
        {
            [IntakeRunStore.RunErrorCode] = code,
            [IntakeRunStore.RunErrorMessage] = AttentionText(d.RunId, stage, code, detail),
        }, release: true).ConfigureAwait(false);
        _logger.LogWarning("Intake run {RunId} held at {Stage} ({ReasonCode})", d.RunId, stage, code);
        return JobOutcome.Poisoned(d.Job.JobId, JobType, code, d.Job.Attempt, d.Stopwatch.Elapsed);
    }

    private Task<IntakeRunState> WriteAsync(
        Delivery d,
        IntakeRunStage stage,
        Action<IntakeRunState>? mutate,
        CancellationToken ct,
        Dictionary<string, object>? fields = null,
        bool release = false)
    {
        fields ??= new Dictionary<string, object>();
        fields[IntakeRunStore.RunCurrentStage] = stage.ToString();
        return _store.WriteRunAsync(d.RunId, d.Owner, mutate, fields, release, ct);
    }

    /// <summary>Another delivery holds the run: write nothing, retry later (Poisoned on the final delivery, as the invoice lock).</summary>
    private JobOutcome Busy(Delivery d)
    {
        _logger.LogWarning("Intake run job {JobId}: another delivery holds run {RunId}", d.Job.JobId, d.RunId);
        return d.Job.IsFinalDelivery
            ? JobOutcome.Poisoned(d.Job.JobId, JobType, "another delivery is processing this run", d.Job.Attempt, d.Stopwatch.Elapsed)
            : JobOutcome.Failure(d.Job.JobId, JobType, "another delivery is processing this run", d.Job.Attempt, d.Stopwatch.Elapsed);
    }

    private JobOutcome Done(Delivery d) => JobOutcome.Success(d.Job.JobId, JobType, d.Stopwatch.Elapsed);

    /// <summary>Admin-attention text: ids, the stage, closed codes and a harness key; never content.</summary>
    internal static string AttentionText(Guid runId, IntakeRunStage stage, string code, string? detail) =>
        $"Intake run {runId:D} stopped at {stage} ({code}{(detail is null ? string.Empty : $": {detail}")}). "
        + "Fix the cause, then re-drive the run; it resumes from its stored state.";

    internal static string MarkerKey(string marker, Guid runId) => $"intake-{marker}:{runId:N}";

    private static IntakeRunStage StageOf(IntakeRunSnapshot? run) =>
        run?.CurrentStage is { } name && Enum.TryParse<IntakeRunStage>(name, out var stage) ? stage : IntakeRunStage.Receive;

    private static bool HasValidSteps(IIntakeHarness harness)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in harness.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name) || !names.Add(step.Name) || !Enum.IsDefined(step.Stage))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string StagesJson = JsonSerializer.Serialize(Enum.GetNames<IntakeRunStage>());

    private static IntakeRunJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            var parsed = payload.Deserialize<IntakeRunJobPayload>(PayloadJson);
            return parsed is { RequestId: var r, RunId: var run } && r != Guid.Empty && run != Guid.Empty ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Delivery(JobContract Job, Guid RequestId, Guid RunId, string Owner, Stopwatch Stopwatch);
}
