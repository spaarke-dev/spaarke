using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Intake.Engine;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Intake;

/// <summary>
/// The intake engine plumbing as one slice (spaarke-intake-pipeline-r1 task 014, FR-07, FR-09, NFR-03, NFR-05, NFR-06; ADR-004 A2,
/// ADR-028): the REAL orchestrator, dispatcher, staging store, completion handler, timeout handler and completion processor over an
/// in-memory Dataverse and blob container. Only the boundaries are doubles: Dataverse, blob storage, Service Bus (what is sent is
/// captured) and the AI facade (ADR-038, ADR-043 seam). The harness double calls the real dispatcher from its external steps, the way
/// the NDA harness (task 015) will.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the claim-check contract with an engine that parses untrusted documents. The terminal-status guard of
/// the completion handler carries a seeding proof (see <see cref="Completion_ForAFailedRunThatStillHasItsWaitSet_IsSettledAsANoChange"/>).
/// </remarks>
public sealed class IntakeEngineDispatchSeamTests
{
    private const string Segment = IntakeEngineOperations.Segment;
    private const string Redline = IntakeEngineOperations.Redline;

    // ---------------------------------------------------------------------------------------------------------------------------
    // Dispatch: staging, the ids-only envelope, repeat
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_StagesInputsAtDeterministicPathsAndSendsOneIdsOnlyEnvelope()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        var id = new IntakeEngineDispatchId(runId, Segment, 1);
        bed.Blobs.Get(EngineBed.Container, $"{runId:D}/in/source.docx").Should().NotBeNull("the draft is staged at {runId}/in/source.docx");
        bed.Blobs.Get(EngineBed.Container, id.DispatchMarker).Should().NotBeNull("the dispatch marker is written after the send");
        bed.Blobs.Names.Select(n => n.Name).Should().OnlyContain(n => n.StartsWith($"{runId:D}/in/"), "the BFF writes under in/ only");

        bed.Requests.Should().ContainSingle();
        var (job, queue) = bed.Requests[0];
        queue.Should().Be("intake-engine-requests");
        job.JobType.Should().Be("IntakeEngineRun");
        job.IdempotencyKey.Should().Be($"IntakeEngine:{runId:D}:segment:1");
        job.SubjectId.Should().Be(runId.ToString("D"));

        // The wire shape is the closed set the engine accepts (envelope.py _REQUIRED + _OPTIONAL), the payload the closed {operation, runId, dispatchAttempt}.
        using var body = JsonDocument.Parse(EngineBed.Wire(job));
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "jobId", "jobType", "subjectId", "correlationId", "idempotencyKey", "attempt", "maxAttempts", "payload", "createdAt", "isAtMaxAttempts");
        body.RootElement.GetProperty("payload").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("operation", "runId", "dispatchAttempt");
        body.RootElement.GetProperty("payload").GetProperty("runId").GetString().Should().Be(runId.ToString("D"));
        EngineBed.Wire(job).Should().NotContain(EngineBed.DraftText, "document content is never in a message (NFR-03)");

        bed.Run.Run(runId).EngineExecutionId.Should().Be(job.IdempotencyKey, "the run records the dispatch it awaits");
    }

    [Fact]
    public async Task Dispatch_ARepeatOfTheSameAttemptSendsNothingAndStagesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var storageCallsBefore = bed.Blobs.Calls.Count(c => c.StartsWith("create "));

        var again = await bed.Dispatcher.DispatchAsync(bed.Request(runId, Segment, attempt: 1), CancellationToken.None);

        again.Kind.Should().Be(IntakeHarnessStepOutcomeKind.Dispatched);
        again.ExecutionId.Should().Be($"IntakeEngine:{runId:D}:segment:1");
        bed.Requests.Should().ContainSingle("a repeat dispatch sends no second request");
        bed.Timeouts.Should().ContainSingle("and schedules no second timeout");
        bed.Blobs.Calls.Count(c => c.StartsWith("create ")).Should().Be(storageCallsBefore, "the staged inputs are not rewritten");
        bed.SourceOpens.Should().Be(1, "the draft is read from SharePoint Embedded only when it is not staged yet");
    }

    [Fact]
    public async Task Dispatch_ANewAttemptIsANewRequestOverTheSameStagedDraft()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        await bed.Dispatcher.DispatchAsync(bed.Request(runId, Segment, attempt: 2), CancellationToken.None);

        bed.Requests.Select(r => r.Job.IdempotencyKey).Should().Equal($"IntakeEngine:{runId:D}:segment:1", $"IntakeEngine:{runId:D}:segment:2");
        bed.SourceOpens.Should().Be(1, "one draft per run: staged once");
    }

    [Fact]
    public async Task Dispatch_ARedlineStagesItsPlanPerAttempt()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        await bed.Dispatcher.DispatchAsync(bed.Request(runId, Redline, attempt: 1), CancellationToken.None);

        var plan = bed.Blobs.Get(EngineBed.Container, $"{runId:D}/in/redline/1/plan.json");
        plan.Should().NotBeNull();
        Encoding.UTF8.GetString(plan!.Content).Should().Be(EngineBed.PlanText);
        bed.Requests.Last().Job.IdempotencyKey.Should().Be($"IntakeEngine:{runId:D}:redline:1");
    }

    [Fact]
    public async Task Dispatch_WithNoEngineConfigured_HoldsAndSendsNothing()
    {
        var bed = new EngineBed(engineConfigured: false);
        var runId = await bed.Run.CreateRunAsync();

        var outcome = await bed.Dispatcher.DispatchAsync(bed.Request(runId, Segment, attempt: 1), CancellationToken.None);

        outcome.Kind.Should().Be(IntakeHarnessStepOutcomeKind.Held);
        outcome.HoldCode.Should().Be(IntakeEngineCodes.NotConfigured);
        bed.Requests.Should().BeEmpty();
        bed.Timeouts.Should().BeEmpty();
        bed.Blobs.Names.Should().BeEmpty("nothing is staged on a stamp without an engine");
    }

    [Fact]
    public async Task Dispatch_SchedulesTheHardTimeoutBeforeTheRequestAndAfterTheEngineRetryWindow()
    {
        var bed = new EngineBed();
        var order = new List<string>();
        bed.OnSchedule = () => order.Add("timeout");
        bed.OnRequest = () => order.Add("request");

        var runId = await bed.StartAsync();

        order.Should().Equal("timeout", "request");
        var (timeout, dueAt) = bed.Timeouts.Single();
        timeout.JobType.Should().Be("IntakeEngineTimeout");
        timeout.IdempotencyKey.Should().Be($"IntakeEngineTimeout:{runId:D}:segment:1");
        dueAt.Should().Be(bed.Run.Time.GetUtcNow() + TimeSpan.FromMinutes(30));
        (dueAt - bed.Run.Time.GetUtcNow()).Should().BeGreaterThan(JobContract.RetryWindow, "a shorter timeout holds runs the engine job is still retrying");
        (dueAt - bed.Run.Time.GetUtcNow()).Should().BeGreaterThanOrEqualTo(IntakeEngineOptions.MinimumTimeout);
        new IntakeEngineOptions { TimeoutMinutes = 1 }.EffectiveTimeout().Should().Be(IntakeEngineOptions.MinimumTimeout, "a too-small setting is raised to the floor");
    }

    [Fact]
    public void Timeout_FloorIsTwoReplicaTimeoutsPlusTheFirstBackoff_AndTheDefaultIsNotBelowIt()
    {
        // infrastructure/bicep/modules/intake-engine-job.bicep: replicaTimeoutSeconds 600. One execution that hangs to its hard stop,
        // the first Service Bus back-off (1 minute) and a second full execution: 2 x 10 + 1 = 21 minutes. The old floor (the 15-minute
        // retry window + 1) fell short of that by five minutes.
        IntakeEngineOptions.EngineReplicaTimeout.Should().Be(TimeSpan.FromMinutes(10));
        IntakeEngineOptions.MinimumTimeout.Should().Be(TimeSpan.FromMinutes(21));
        new IntakeEngineOptions { TimeoutMinutes = 20 }.EffectiveTimeout().Should().Be(TimeSpan.FromMinutes(21), "20 minutes is below one hang plus one retry");
        new IntakeEngineOptions { TimeoutMinutes = 45 }.EffectiveTimeout().Should().Be(TimeSpan.FromMinutes(45), "a larger setting is kept");
        new IntakeEngineOptions().EffectiveTimeout().Should().BeGreaterThanOrEqualTo(IntakeEngineOptions.MinimumTimeout);
        TimeSpan.FromMinutes(IntakeEngineOptions.DefaultTimeoutMinutes).Should().BeGreaterThanOrEqualTo(IntakeEngineOptions.MinimumTimeout);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Completion: record, advance, duplicate
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Completion_ASegmentIsRecordedAndTheRunIsContinued()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var run = bed.Run.Run(runId);
        run.State.Steps["segment"].Should().BeEquivalentTo(new IntakeHarnessResultReference
        {
            Kind = IntakeEngineResultKinds.Segment,
            Reference = $"{runId:D}/out/segment/1/",
        });
        run.State.Awaiting.Should().BeNull("the wait is over");
        bed.Continuations.Should().ContainSingle();
        var next = bed.Continuations[0];
        next.JobType.Should().Be(IntakeRunJobHandler.JobTypeName);
        next.IdempotencyKey.Should().Be($"intake-run-{runId:N}", "the continuation is the run's deterministic IntakeRun job");
        next.CorrelationId.Should().Be(IntakeRunTestBed.CorrelationId);
        run.State.Markers.Should().ContainKey("engine-continued:segment:1");
    }

    [Fact]
    public async Task Completion_ADuplicateChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        var writes = bed.Run.Dataverse.Writes.Count;

        var duplicate = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        duplicate.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "a duplicate completion writes nothing");
        bed.Continuations.Should().ContainSingle("and continues the run once");
    }

    [Fact]
    public async Task Completion_ARedeliveryAfterTheRecordButBeforeTheContinuationFinishesTheContinuation()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        bed.ContinuationFault = new InvalidOperationException("the queue is down");

        var first = async () => await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();
        bed.Run.Run(runId).State.Steps.Should().ContainKey("segment", "the result was recorded before the send failed");
        bed.Continuations.Should().BeEmpty();

        bed.ContinuationFault = null;
        var redelivered = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        redelivered.Status.Should().Be(JobStatus.Completed);
        bed.Continuations.Should().ContainSingle("a run must never be lost between the record and the continuation");
        var again = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        again.Status.Should().Be(JobStatus.Completed);
        bed.Continuations.Should().ContainSingle("and a further duplicate sends no second one");
    }

    [Fact]
    public async Task Completion_TheWholeRunThroughSegmentAndRedlineReachesApproval()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        (await bed.Advance(runId)).Status.Should().Be(JobStatus.Completed);
        bed.Requests.Select(r => r.Job.IdempotencyKey).Should().Equal($"IntakeEngine:{runId:D}:segment:1", $"IntakeEngine:{runId:D}:redline:1");

        var redline = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "passed"), CancellationToken.None);
        redline.Status.Should().Be(JobStatus.Completed);
        var run = bed.Run.Run(runId);
        run.State.Steps["redline"].Kind.Should().Be(IntakeEngineResultKinds.RedlinePassed, "the check outcome is recorded with the output");
        run.State.Steps["redline"].Reference.Should().Be($"{runId:D}/out/redline/1/");

        (await bed.Advance(runId)).Status.Should().Be(JobStatus.Completed);
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval, "a passed check advances the run to the approval stage");
        bed.Harness.PersistCalls.Should().Be(1);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Completion: outcomes that stop the run
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Completion_AFailedCheckHoldsTheRunForAnAdministratorAndSendsNothingForApproval()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);
        var continuations = bed.Continuations.Count;

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "failed"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var run = bed.Run.Run(runId);
        run.State.Held.Should().NotBeNull();
        run.State.Held!.Code.Should().Be(IntakeEngineCodes.CheckFailed);
        run.State.Held.Stage.Should().Be(nameof(IntakeRunStage.Build));
        run.State.Awaiting.Should().BeNull();
        run.State.Steps.Should().NotContainKey("redline", "a failed check stores no output");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress, "held, not failed: an administrator re-drives it");
        bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeEngineCodes.CheckFailed);
        bed.Continuations.Count.Should().Be(continuations, "nothing continues the run");
        bed.Run.RequestStage().Should().NotBe(IntakeRequestStages.AwaitingApproval, "nothing is sent for approval (FR-09c)");
        bed.Harness.PersistCalls.Should().Be(0);
    }

    [Fact]
    public async Task Completion_ARedlineWithNoCheckOutcomeIsNeverTreatedAsAPass()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);

        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: null), CancellationToken.None);

        var run = bed.Run.Run(runId);
        run.State.Held!.Code.Should().Be(IntakeEngineCodes.ContractError);
        run.State.Steps.Should().NotContainKey("redline");
    }

    [Theory]
    [InlineData("plan_does_not_fit", IntakeEngineCodes.PlanDoesNotFit)]
    [InlineData("contract_error", IntakeEngineCodes.ContractError)]
    public async Task Completion_APlanOrContractFailureHoldsTheRun(string engineOutcome, string code)
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);

        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, outcome: engineOutcome), CancellationToken.None);

        var run = bed.Run.Run(runId);
        run.State.Held!.Code.Should().Be(code);
        run.State.Steps.Should().NotContainKey("redline");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task Completion_AnUnreadableDraftFailsTheRunAndSendsTheRequestToTriage()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();

        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);

        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Failed, "the draft can never be read: retrying is pointless");
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.NeedsTriage, "a person sees it");
        bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeEngineCodes.InputInvalid);
        bed.Continuations.Should().BeEmpty();
        bed.Run.Run(runId).State.Held.Should().BeNull("a failed run is not also held");
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Completion: untrusted input changes nothing
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Completion_ForAnUnknownRun_ChangesNothing()
    {
        var bed = new EngineBed();
        await bed.StartAsync();
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(Guid.NewGuid(), Segment, 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "the message is settled, not retried");
        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
        bed.Continuations.Should().BeEmpty();
    }

    [Fact]
    public async Task Completion_ForARunThatHasRecordedNoDispatch_ChangesNothingAndIsRetried()
    {
        var bed = new EngineBed();
        var runId = await bed.Run.CreateRunAsync(); // created, never processed: no wait, no recorded dispatch
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "no dispatch is recorded yet: retried until one is, then dead-lettered");
        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
        bed.Run.Run(runId).State.Steps.Should().BeEmpty();
        bed.Continuations.Should().BeEmpty();
    }

    [Fact]
    public async Task Completion_ForAnEarlierAttempt_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.RecordDispatchAsync(runId, Segment, attempt: 2); // the orchestrator re-dispatched under attempt 2
        var writes = bed.Run.Dataverse.Writes.Count;

        var stale = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        stale.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "an attempt the run has moved past never records its result");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty();
        bed.Continuations.Should().BeEmpty();

        var current = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 2), CancellationToken.None);
        current.Status.Should().Be(JobStatus.Completed);
        bed.Run.Run(runId).State.Steps["segment"].Reference.Should().Be($"{runId:D}/out/segment/2/");
    }

    [Fact]
    public async Task Completion_ForARunThatIsNoLongerAwaiting_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "failed"), CancellationToken.None); // held: the wait is over
        var writes = bed.Run.Dataverse.Writes.Count;

        // A second, different completion of the same dispatch (the engine re-ran it): the run is no longer waiting for one.
        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "passed"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "a held run is not resumed by a completion");
        bed.Run.Run(runId).State.Steps.Should().NotContainKey("redline");
        bed.Run.Run(runId).State.Held!.Code.Should().Be(IntakeEngineCodes.CheckFailed);
    }

    [Fact]
    public async Task Completion_AheadOfTheRecordedDispatch_IsRetriedNotDropped()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync(); // the run records segment/1

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "passed"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "the orchestrator records the execution id after the send: the completion may simply be early");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty("a step the run is not awaiting is never recorded");
        bed.Continuations.Should().BeEmpty();
    }

    [Fact]
    public async Task Completion_ABlobPathInTheMessageIsIgnored()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var job = bed.CompletionJob(runId, Segment, 1, extra: new Dictionary<string, object?>
        {
            ["outPrefix"] = "someone-elses-run/out/",
            ["outputPath"] = "../../other/clauses.json",
            ["blobPath"] = "https://evil.example/x",
            ["reference"] = "evil",
        });

        await bed.Completion.ProcessAsync(job, CancellationToken.None);

        bed.Run.Run(runId).State.Steps["segment"].Reference.Should().Be($"{runId:D}/out/segment/1/", "the output path is derived from the run id, never taken from a message");
        bed.Run.Logs.AllText().Should().NotContain("evil").And.NotContain("someone-elses-run");
    }

    [Theory]
    [InlineData("""{"operation":"segment","runId":"not-a-guid","dispatchAttempt":1,"outcome":"completed"}""")]
    [InlineData("""{"operation":"explode","runId":"01300000-0000-4000-8000-0000000000a9","dispatchAttempt":1,"outcome":"completed"}""")]
    [InlineData("""{"operation":"segment","runId":"01300000-0000-4000-8000-0000000000a9","dispatchAttempt":0,"outcome":"completed"}""")]
    [InlineData("""{"operation":"segment","runId":"01300000-0000-4000-8000-0000000000a9","dispatchAttempt":"1","outcome":"completed"}""")]
    [InlineData("""{"operation":"segment","runId":"01300000-0000-4000-8000-0000000000a9","dispatchAttempt":1,"outcome":"all-good"}""")]
    [InlineData("""{"operation":"segment","runId":"01300000-0000-4000-8000-0000000000a9","dispatchAttempt":1}""")]
    [InlineData("""[1,2,3]""")]
    public async Task Completion_AMalformedPayloadIsPoisonedAndTouchesNoRun(string payload)
    {
        var bed = new EngineBed();
        await bed.StartAsync();
        var writes = bed.Run.Dataverse.Writes.Count;
        var job = bed.CompletionJob(Guid.NewGuid(), Segment, 1);
        job.Payload = JsonDocument.Parse(payload);

        var outcome = await bed.Completion.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Hard timeout
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Timeout_WithNoCompletionHoldsTheRunWithTheEngineTimeoutReasonAndLeavesTheRequestAlone()
    {
        // Spec NFR-05 governs (not the POML's "Failed"): an engine timeout is HELD with admin attention, re-drivable.
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var requestStage = bed.Run.RequestStage();

        bed.Run.Time.Advance(TimeSpan.FromMinutes(31));
        var outcome = await bed.Timeout.ProcessAsync(bed.Timeouts.Single().Job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress, "held, not failed: an administrator re-drives it");
        var row = bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be("intake.engine_timeout");
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorMessage).Should().Be("engine timeout");
        var run = bed.Run.Run(runId);
        run.State.Held.Should().NotBeNull();
        run.State.Held!.Code.Should().Be(IntakeEngineCodes.Timeout);
        run.State.Held.Stage.Should().Be(nameof(IntakeRunStage.Analyse));
        run.State.Awaiting.Should().BeNull();
        run.State.Steps.Should().BeEmpty();
        bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId).Contains(IntakeRunStore.RunCompleted).Should().BeFalse("not terminal");
        bed.Run.RequestStage().Should().Be(requestStage, "the request stage is left as it was; only a Failed run moves it to Needs triage");
    }

    [Fact]
    public async Task Timeout_OfAFirstDispatchThatWasNeverRecorded_HoldsTheRun()
    {
        // The orchestrator marked segment awaiting and the send threw before the dispatch was recorded: the execution column is
        // EMPTY, so the timeout of that first dispatch is the run's own and still applies.
        var bed = new EngineBed();
        bed.OnRequest = () => throw new InvalidOperationException("the queue is down");
        var runId = await bed.Run.CreateRunAsync();
        (await bed.Advance(runId)).Status.Should().Be(JobStatus.Failed);
        bed.Run.Run(runId).EngineExecutionId.Should().BeNullOrEmpty();
        bed.Run.Run(runId).State.Awaiting.Should().BeEquivalentTo(new { Step = "segment", Dispatched = false });

        await bed.Timeout.ProcessAsync(bed.Timeouts.Single().Job, CancellationToken.None);

        bed.Run.Run(runId).State.Held!.Code.Should().Be(IntakeEngineCodes.Timeout);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task LateCompletionAfterTimeout_ChangesNothingAndContinuesNothing()
    {
        // The guard that stops a late completion from resurrecting a held run is "the run no longer awaits this dispatch" (the
        // timeout cleared the wait). The terminal-status guard has its own seeding-proof test below.
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Timeout.ProcessAsync(bed.Timeouts.Single().Job, CancellationToken.None);
        var writes = bed.Run.Dataverse.Writes.Count;

        var late = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        late.Status.Should().Be(JobStatus.Completed, "a late completion is settled as a no-op, not retried");
        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
        bed.Run.Run(runId).State.Held!.Code.Should().Be(IntakeEngineCodes.Timeout, "a held run stays held until an administrator re-drives it");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty();
        bed.Continuations.Should().BeEmpty("the run is not continued");
    }

    [Fact(DisplayName = "Seeding proof: remove the terminal-status check of the completion handler and this fails (the completion would be retried, not settled)")]
    public async Task Completion_ForAFailedRunThatStillHasItsWaitSet_IsSettledAsANoChange()
    {
        // A run that is Failed yet still carries Awaiting {segment, Dispatched} and the segment/1 execution id: the only thing that
        // stops its completion is the handler's terminal-status check (the store refuses too, which would make the handler retry it).
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var row = bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        row[IntakeRunStore.RunStatus] = new OptionSetValue((int)ProcessingJobStatus.Failed);
        bed.Run.Dataverse.Seed(row);
        bed.Run.Run(runId).State.Awaiting.Should().NotBeNull("the seeded run still waits");
        var writes = bed.Run.Dataverse.Writes.Count;

        var late = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        late.Status.Should().Be(JobStatus.Completed, "a finished run's completion is settled, never retried into a dead-letter");
        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Failed, "a failed run stays failed");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty();
        bed.Continuations.Should().BeEmpty("the run is not continued");
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Binding to the dispatch (review F1): the window between "redline marked awaiting" and "redline dispatch recorded"
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Completion_OfThePreviousDispatchWhileTheNextIsBeingSent_IsNotRecordedAsTheNextStep()
    {
        var bed = new EngineBed();
        var runId = await bed.StartWithFaultedRedlineDispatchAsync();
        var writes = bed.Run.Dataverse.Writes.Count;
        var continuations = bed.Continuations.Count;

        // A redelivered segment completion: the run still records segment/1 as its dispatch, but it awaits an undispatched redline.
        var duplicate = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        duplicate.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "the segment's completion changes nothing while the redline is being sent");
        var run = bed.Run.Run(runId);
        run.State.Steps.Keys.Should().Equal("segment");
        run.State.Steps.Should().NotContainKey("redline", "the segment's result is never recorded as the redline step");
        run.State.Awaiting.Should().BeEquivalentTo(new { Step = "redline", Dispatched = false });
        bed.Continuations.Count.Should().Be(continuations);
    }

    [Fact]
    public async Task Completion_OfARedlineWhoseDispatchIsNotRecordedYet_IsRetriedNotRecorded()
    {
        var bed = new EngineBed();
        var runId = await bed.StartWithFaultedRedlineDispatchAsync();

        var early = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "passed"), CancellationToken.None);

        early.Status.Should().Be(JobStatus.Failed, "ahead of the recorded dispatch: retried until the orchestrator records it");
        bed.Run.Run(runId).State.Steps.Should().NotContainKey("redline");
    }

    [Fact]
    public async Task Timeout_OfTheSegmentWhileTheRunAwaitsAnUndispatchedRedline_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartWithFaultedRedlineDispatchAsync();
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Timeout.ProcessAsync(bed.Timeouts.First().Job, CancellationToken.None); // segment/1's

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "a finished dispatch's timeout never stops the step now being sent");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
        bed.Run.Run(runId).State.Held.Should().BeNull();
        bed.Run.Run(runId).State.Awaiting!.Step.Should().Be("redline");
        bed.Run.RequestStage().Should().NotBe(IntakeRequestStages.NeedsTriage);
    }

    [Fact]
    public async Task Timeout_OfTheSegmentAfterTheRunWasHeldExhaustedOnTheRedline_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartWithFaultedRedlineDispatchAsync(finalDelivery: true);
        var held = bed.Run.Run(runId).State.Held;
        held.Should().NotBeNull();
        held!.Code.Should().Be(IntakeRunCodes.Exhausted);
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Timeout.ProcessAsync(bed.Timeouts.First().Job, CancellationToken.None); // segment/1's

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "a held run is the administrator's, not a stale timeout's");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
        bed.Run.Run(runId).State.Held!.Code.Should().Be(IntakeRunCodes.Exhausted);
        bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeRunCodes.Exhausted);
    }

    [Fact]
    public async Task Timeout_OfTheRedlineWhileTheExecutionColumnStillNamesTheSegment_ChangesNothing()
    {
        // The run's execution column names segment/1: only a segment/1 timeout can match it, never the redline/1 one, even though
        // the run awaits "redline" (an undispatched one).
        var bed = new EngineBed();
        var runId = await bed.StartWithFaultedRedlineDispatchAsync();
        var writes = bed.Run.Dataverse.Writes.Count;

        await bed.Timeout.ProcessAsync(bed.Timeouts.Last().Job, CancellationToken.None); // redline/1's

        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "the unrecorded case is allowed only when the execution column is empty");
        bed.Run.Run(runId).State.Held.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Stop order (review F1): the fence first, the request next, the terminal status last
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_AFailHoldsTheRunFirstThenMovesTheRequestThenWritesTheTerminalStatusLast()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var before = bed.Run.Dataverse.Writes.Count;

        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);

        var writes = bed.Run.Dataverse.Writes.Skip(before).ToList();
        writes.Select(w => w.Entity).Should().Equal(IntakeRunStore.RunEntity, IntakeRunStore.RequestEntity, IntakeRunStore.RunEntity);
        writes[0].Fields.Should().NotContainKey(IntakeRunStore.RunStatus, "the first run write is the fence, not the terminal status");
        IntakeRunState.Parse((string?)writes[0].Fields[IntakeRunStore.RunState]).Held!.Code.Should().Be(IntakeEngineCodes.InputInvalid);
        ((OptionSetValue)writes[1].Fields[IntakeRunStore.RequestIntakeStage]!).Value.Should().Be(IntakeRequestStages.NeedsTriage);
        ((OptionSetValue)writes[2].Fields[IntakeRunStore.RunStatus]!).Value.Should().Be((int)ProcessingJobStatus.Failed, "the terminal status is the LAST write");
        bed.Run.Run(runId).State.Held.Should().BeNull("a failed run is not also held");
    }

    [Fact]
    public async Task Stop_AFailThatFaultedAfterItsFence_IsFinishedByTheRedelivery()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var faulted = false;
        bed.Run.Dataverse.RunWriteHook = (_, fields) =>
        {
            // The terminal write of the first delivery faults (a transient Dataverse error), after the fence held the run.
            if (!faulted && fields.ContainsKey(IntakeRunStore.RunStatus))
            {
                faulted = true;
                return new InvalidOperationException("dataverse is unavailable");
            }

            return null;
        };

        var first = () => bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();
        bed.Run.Run(runId).State.Held!.Code.Should().Be(IntakeEngineCodes.InputInvalid, "the fence was written");

        var redelivered = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);

        redelivered.Status.Should().Be(JobStatus.Completed);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Failed, "the redelivery finishes the fail instead of leaving the run held");
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.NeedsTriage);
        bed.Run.Run(runId).State.Held.Should().BeNull();
    }

    [Fact]
    public async Task Stop_AHeldRunWithTheSameCodeAfterAReDriveClaimedIt_IsNotFailedByALateDuplicate()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        bed.Run.Dataverse.RunWriteHook = (_, fields) =>
            fields.ContainsKey(IntakeRunStore.RunStatus) ? new InvalidOperationException("dataverse is unavailable") : null;
        var first = () => bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>();
        bed.Run.Dataverse.RunWriteHook = null;

        // A re-drive claims the held run before the duplicate arrives.
        var row = bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        var state = IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState));
        state.Claim = new IntakeRunClaim("redrive", new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero));
        row[IntakeRunStore.RunState] = state.Serialize();
        bed.Run.Dataverse.Seed(row);

        var duplicate = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);

        duplicate.Status.Should().Be(JobStatus.Completed);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress, "a claimed (re-driven) run is not failed by a late duplicate");
    }

    [Fact]
    public async Task Stop_ACompletionRecordedBetweenTheReadAndTheWrite_LeavesTheRequestStageUnchanged()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var requestStage = bed.Run.RequestStage();
        var reads = 0;
        bed.Run.Dataverse.RunReadGate = async () =>
        {
            // Read 1 is the handler's; read 2 is the stop's. A concurrent delivery records the segment's result right after it.
            if (Interlocked.Increment(ref reads) == 2)
            {
                var recorded = await bed.Run.Store.RecordExternalStepResultAsync(
                    runId, Segment, new IntakeHarnessResultReference { Kind = IntakeEngineResultKinds.Segment, Reference = $"{runId:D}/out/segment/1/" },
                    CancellationToken.None, expectedExecutionId: $"IntakeEngine:{runId:D}:segment:1");
                recorded.Should().BeTrue();
            }
        };

        var outcome = await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1, outcome: "input_invalid"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        reads.Should().BeGreaterThanOrEqualTo(2);
        bed.Run.RequestStage().Should().Be(requestStage, "a stop whose fence lost writes nothing to the request");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
        bed.Run.Run(runId).State.Steps.Should().ContainKey("segment");
        bed.Run.Run(runId).State.Held.Should().BeNull();
    }

    [Fact]
    public async Task Timeout_AfterTheCompletionWasHandled_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId); // the run now awaits redline/1
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Timeout.ProcessAsync(bed.Timeouts.First().Job, CancellationToken.None); // segment/1's timeout

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "the timeout of a dispatch that finished never fails the run waiting on the next one");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
        bed.Run.Run(runId).State.Awaiting!.Step.Should().Be("redline");
    }

    [Fact]
    public async Task Timeout_OfASupersededAttempt_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.RecordDispatchAsync(runId, Segment, attempt: 2);
        var writes = bed.Run.Dataverse.Writes.Count;

        await bed.Timeout.ProcessAsync(bed.Timeouts.First().Job, CancellationToken.None); // attempt 1's

        bed.Run.Dataverse.Writes.Count.Should().Be(writes);
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task Timeout_OfAHeldRun_ChangesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "failed"), CancellationToken.None);
        var writes = bed.Run.Dataverse.Writes.Count;

        await bed.Timeout.ProcessAsync(bed.Timeouts.Last().Job, CancellationToken.None);

        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "a held run is the administrator's, not the timeout's");
        bed.Run.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task Timeout_WhileALiveDeliveryHoldsTheRun_IsRetriedAndWritesNothing()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var row = bed.Run.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        var state = IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState));
        state.Claim = new IntakeRunClaim("someone.else", bed.Run.Time.GetUtcNow());
        await bed.Run.Dataverse.Object.UpdateAsync(IntakeRunStore.RunEntity, runId, new Dictionary<string, object> { [IntakeRunStore.RunState] = state.Serialize() }, CancellationToken.None);
        var writes = bed.Run.Dataverse.Writes.Count;

        var outcome = await bed.Timeout.ProcessAsync(bed.Timeouts.Single().Job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "the normal back-off tries again later");
        bed.Run.Dataverse.Writes.Count.Should().Be(writes, "nothing is written under another delivery's claim");
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Correlation and content hygiene
    // ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CorrelationId_IsTheSameOnTheRunTheRequestTheTimeoutAndTheCompletionLogs()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);

        var correlation = IntakeRunTestBed.CorrelationId;
        bed.Run.Run(runId).CorrelationId.Should().Be(correlation);
        bed.Requests.Single().Job.CorrelationId.Should().Be(correlation);
        bed.Timeouts.Single().Job.CorrelationId.Should().Be(correlation);
        bed.Continuations.Single().CorrelationId.Should().Be(correlation);
        var completionLogs = bed.Run.Logs.Entries.Where(e => e.Message.Contains("completed (check") || e.Message.Contains("continuation enqueued")).ToList();
        completionLogs.Should().NotBeEmpty();
        completionLogs.Should().OnlyContain(e => IntakeAssertions.HasCorrelation(e.Scopes, correlation));
        var dispatchLogs = bed.Run.Logs.Entries.Where(e => e.Message.Contains("dispatched (staged")).ToList();
        dispatchLogs.Should().NotBeEmpty();
        dispatchLogs.Should().OnlyContain(e => IntakeAssertions.HasCorrelation(e.Scopes, correlation));
    }

    [Fact]
    public async Task NoDocumentOrPlanContentIsInAnyMessageOrLog()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Segment, 1), CancellationToken.None);
        await bed.Advance(runId);
        await bed.Completion.ProcessAsync(bed.CompletionJob(runId, Redline, 1, check: "failed"), CancellationToken.None);
        await bed.Timeout.ProcessAsync(bed.Timeouts.Last().Job, CancellationToken.None);

        var messages = bed.Requests.Select(r => EngineBed.Wire(r.Job))
            .Concat(bed.Timeouts.Select(t => EngineBed.Wire(t.Job)))
            .Concat(bed.Continuations.Select(EngineBed.Wire));
        foreach (var text in messages.Append(bed.Run.Logs.AllText()))
        {
            text.Should().NotContain("CONFIDENTIAL").And.NotContain(EngineBed.DraftText).And.NotContain(EngineBed.PlanText);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The completion processor: a closed switch on its own queue
    // ---------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("IntakeRun")]
    [InlineData("IntakeEngineRun")]
    [InlineData("IntakeEngineTimeout")]
    [InlineData("IncomingCommunication")]
    [InlineData("")]
    public async Task Processor_AnyJobTypeButACompletion_IsDeadLetteredWrongQueueAndNoHandlerRuns(string jobType)
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var job = bed.CompletionJob(runId, Segment, 1);
        job.JobType = jobType;
        var handlerResolved = 0;
        var provider = new ServiceCollection()
            .AddScoped<IntakeEngineCompletionJobHandler>(_ =>
            {
                handlerResolved++;
                return bed.Completion;
            })
            .BuildServiceProvider();

        var args = await ProcessAsync(provider, job);

        args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "WrongQueue", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        args.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        handlerResolved.Should().Be(0, "no handler is even constructed for another job type");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty("a message of another type changes no run");
        bed.Continuations.Should().BeEmpty();
    }

    [Fact]
    public async Task Processor_ACompletionReachesItsHandlerAndIsCompleted()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var provider = new ServiceCollection().AddScoped<IntakeEngineCompletionJobHandler>(_ => bed.Completion).BuildServiceProvider();

        var args = await ProcessAsync(provider, bed.CompletionJob(runId, Segment, 1));

        args.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        bed.Run.Run(runId).State.Steps.Should().ContainKey("segment");
        bed.Continuations.Should().ContainSingle();
    }

    [Fact]
    public async Task Processor_ARetryableOutcomeOnTheFirstDeliveryIsRetriedNotDeadLettered()
    {
        // The engine writes attempt 1 of maxAttempts 1 on every completion. Taken as sent, that is a "final delivery" at once, and a
        // completion that is ahead of the orchestrator's record would be dead-lettered instead of retried.
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var provider = new ServiceCollection().AddScoped<IntakeEngineCompletionJobHandler>(_ => bed.Completion).BuildServiceProvider();
        var scheduled = new List<ServiceBusMessage>();

        var args = await ProcessAsync(provider, bed.CompletionJob(runId, Redline, 1, check: "passed"), scheduled); // ahead of the recorded segment/1

        args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        scheduled.Should().ContainSingle("a copy is scheduled for the back-off");
        args.Verify(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()), Times.Once, "and the original is settled once the copy exists");
        bed.Run.Run(runId).State.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Processor_AMalformedCompletionIsDeadLetteredPoisonedWithNoContent()
    {
        var bed = new EngineBed();
        var runId = await bed.StartAsync();
        var job = bed.CompletionJob(runId, Segment, 1);
        job.Payload = JsonDocument.Parse("""{"operation":"segment","note":"CONFIDENTIAL-ENGINE-STDERR"}""");
        var provider = new ServiceCollection().AddScoped<IntakeEngineCompletionJobHandler>(_ => bed.Completion).BuildServiceProvider();

        var args = await ProcessAsync(provider, job);

        args.Verify(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), "Poisoned", It.Is<string>(d => !d.Contains("CONFIDENTIAL")), It.IsAny<CancellationToken>()), Times.Once);
        bed.Run.Logs.AllText().Should().NotContain("CONFIDENTIAL");
    }

    [Fact]
    public void Processor_IsRegisteredOnlyWhenTheCompletionQueueNameIsSet()
    {
        static bool HostsProcessor(string? queueName)
        {
            var settings = new Dictionary<string, string?> { ["ServiceBus:IntakeEngineCompletionQueueName"] = queueName };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            var logging = new Mock<ILoggingBuilder>();
            logging.SetupGet(l => l.Services).Returns(services);
            Sprk.Bff.Api.Infrastructure.DI.JobProcessingModule.AddJobProcessingModule(services, configuration, logging.Object);
            // Never an IJobHandler: a completion arriving on sdap-jobs must find no handler (the engine identity is Sender on its own queue only).
            services.Should().NotContain(d => d.ServiceType == typeof(IJobHandler) && d.ImplementationType == typeof(IntakeEngineCompletionJobHandler));
            services.Should().Contain(d => d.ServiceType == typeof(IJobHandler) && d.ImplementationType == typeof(IntakeEngineTimeoutJobHandler));
            return services.Any(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && d.ImplementationType == typeof(IntakeEngineCompletionProcessor));
        }

        HostsProcessor("intake-engine-completions").Should().BeTrue("a stamp with an engine consumes its completions");
        HostsProcessor(null).Should().BeFalse("no queue name, no consumer");
        HostsProcessor("  ").Should().BeFalse();
    }

    private static async Task<Mock<ProcessMessageEventArgs>> ProcessAsync(IServiceProvider provider, JobContract job, List<ServiceBusMessage>? scheduled = null)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(EngineBed.Wire(job)),
            messageId: job.IdempotencyKey,
            correlationId: job.CorrelationId,
            subject: job.JobType,
            contentType: "application/json",
            deliveryCount: 1);
        var args = new Mock<ProcessMessageEventArgs>(message, new Mock<ServiceBusReceiver>().Object, CancellationToken.None);
        args.Setup(a => a.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        args.Setup(a => a.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sender = new Mock<ServiceBusSender>();
        sender.Setup(s => s.ScheduleMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns((ServiceBusMessage m, DateTimeOffset _, CancellationToken _) =>
            {
                scheduled?.Add(m);
                return Task.FromResult(42L);
            });
        var client = new Mock<ServiceBusClient>();
        client.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(sender.Object);
        var processor = new IntakeEngineCompletionProcessor(
            client.Object,
            provider,
            NullLogger<IntakeEngineCompletionProcessor>.Instance,
            Options.Create(new ServiceBusOptions { IntakeEngineCompletionQueueName = "intake-engine-completions", MaxConcurrentCalls = 2 }));
        await processor.ProcessMessageAsync(args.Object);
        return args;
    }
}

/// <summary>The arrangement: the real slice over in-memory boundaries; what the BFF sends to Service Bus is captured.</summary>
internal sealed class EngineBed
{
    public const string Container = "intake-staging";
    public const string DraftText = "CONFIDENTIAL-DRAFT-the-parties-agree-to-keep-secret";
    public const string PlanText = """{"edits":[{"para":"p1","text":"CONFIDENTIAL-PLAN-TEXT"}]}""";

    private readonly NdaLikeHarness _harness;

    public IntakeRunTestBed Run { get; } = new();
    public InMemoryBlobGateway Blobs { get; } = new();
    public IntakeEngineStaging Staging { get; }
    public IntakeEngineDispatcher Dispatcher { get; }
    public IntakeEngineCompletionJobHandler Completion { get; }
    public IntakeEngineTimeoutJobHandler Timeout { get; }
    public NdaLikeHarness Harness => _harness;

    public List<(JobContract Job, string Queue)> Requests { get; } = new();
    public List<(JobContract Job, DateTimeOffset DueAt)> Timeouts { get; } = new();
    public List<JobContract> Continuations { get; } = new();
    public Exception? ContinuationFault { get; set; }
    public Action? OnSchedule { get; set; }
    public Action? OnRequest { get; set; }
    public int SourceOpens;
    public int Attempt = 1;

    public EngineBed(bool engineConfigured = true)
    {
        var jobs = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
            NullLogger<JobSubmissionService>.Instance,
            new Mock<ServiceBusClient>().Object);
        jobs.Setup(j => j.SubmitToNamedQueueAsync(It.IsAny<JobContract>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, string queue, CancellationToken _) =>
            {
                OnRequest?.Invoke();
                Requests.Add((job, queue));
                return Task.CompletedTask;
            });
        jobs.Setup(j => j.ScheduleJobAsync(It.IsAny<JobContract>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, DateTimeOffset dueAt, CancellationToken _) =>
            {
                OnSchedule?.Invoke();
                Timeouts.Add((job, dueAt));
                return Task.CompletedTask;
            });
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns((JobContract job, CancellationToken _) =>
            {
                if (ContinuationFault is { } fault)
                {
                    return Task.FromException(fault);
                }

                Continuations.Add(job);
                return Task.CompletedTask;
            });

        Staging = engineConfigured
            ? new IntakeEngineStaging(Blobs, Container, Run.Logs.CreateLogger<IntakeEngineStaging>())
            : new UnconfiguredStaging();
        Dispatcher = new IntakeEngineDispatcher(Staging, jobs.Object, Run.Options, Run.Time, Run.Logs.CreateLogger<IntakeEngineDispatcher>());
        Completion = new IntakeEngineCompletionJobHandler(Run.Store, jobs.Object, Run.Logs.CreateLogger<IntakeEngineCompletionJobHandler>());
        Timeout = new IntakeEngineTimeoutJobHandler(Run.Store, Run.Logs.CreateLogger<IntakeEngineTimeoutJobHandler>());
        _harness = new NdaLikeHarness(this);
    }

    /// <summary>The Job Contract as <c>JobSubmissionService</c> puts it on the wire.</summary>
    public static string Wire(JobContract job) => JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    /// <summary>Creates the run and delivers its job once: the segment step is dispatched and the run waits.</summary>
    public async Task<Guid> StartAsync()
    {
        var runId = await Run.CreateRunAsync();
        var outcome = await Advance(runId);
        outcome.Status.Should().Be(JobStatus.Completed);
        Run.Run(runId).State.Awaiting.Should().NotBeNull("the run waits for the engine");
        return runId;
    }

    /// <summary>Delivers the run's IntakeRun job (the orchestrator).</summary>
    public Task<JobOutcome> Advance(Guid runId, int deliveryCount = 1) =>
        CreateRunHandler().ProcessAsync(IntakeRunTestBed.Job(runId, deliveryCount: deliveryCount), CancellationToken.None);

    /// <summary>
    /// The run after the redline dispatch faulted: segment is recorded and its continuation delivered, the orchestrator marked
    /// <c>redline</c> awaiting and its request send threw, so the run awaits an UNDISPATCHED redline while its execution id still
    /// names the segment (the window of review F1). <paramref name="finalDelivery"/> makes that delivery the last one: the run is held Exhausted.
    /// </summary>
    public async Task<Guid> StartWithFaultedRedlineDispatchAsync(bool finalDelivery = false)
    {
        var runId = await StartAsync();
        await Completion.ProcessAsync(CompletionJob(runId, IntakeEngineOperations.Segment, 1), CancellationToken.None);
        OnRequest = () => throw new InvalidOperationException("the queue is down");
        var outcome = await Advance(runId, deliveryCount: finalDelivery ? JobContract.MaxDeliveryCount : 1);
        outcome.Status.Should().Be(finalDelivery ? JobStatus.Poisoned : JobStatus.Failed);
        OnRequest = null;

        var run = Run.Run(runId);
        run.State.Awaiting.Should().BeEquivalentTo(new { Step = "redline", Dispatched = false }, "marked awaiting before the send, never recorded as sent");
        run.EngineExecutionId.Should().Be($"IntakeEngine:{runId:D}:segment:1", "the previous dispatch is still the recorded one");
        return runId;
    }

    public IntakeEngineDispatchRequest Request(Guid runId, string operation, int attempt) => new()
    {
        RunId = runId,
        Operation = operation,
        DispatchAttempt = attempt,
        CorrelationId = IntakeRunTestBed.CorrelationId,
        OpenSource = _ =>
        {
            SourceOpens++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(DraftText)));
        },
        OpenPlan = operation == IntakeEngineOperations.Redline
            ? _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(PlanText)))
            : null,
    };

    /// <summary>What the orchestrator does on a re-dispatch: the dispatcher sends, the run records the new execution id.</summary>
    public async Task RecordDispatchAsync(Guid runId, string operation, int attempt)
    {
        var outcome = await Dispatcher.DispatchAsync(Request(runId, operation, attempt), CancellationToken.None);
        await Run.Dataverse.Object.UpdateAsync(
            IntakeRunStore.RunEntity, runId, new Dictionary<string, object> { [IntakeRunStore.RunEngineExecution] = outcome.ExecutionId! }, CancellationToken.None);
    }

    /// <summary>A completion as the engine writes it (envelope.py): the same Job Contract shape on the completion queue.</summary>
    public JobContract CompletionJob(
        Guid runId, string operation, int attempt, string outcome = "completed", string? check = null, IDictionary<string, object?>? extra = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["runId"] = runId.ToString("D"),
            ["dispatchAttempt"] = attempt,
            ["outcome"] = outcome,
        };
        if (check is not null)
        {
            payload["checkOutcome"] = check;
        }

        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            payload[key] = value;
        }

        return new JobContract
        {
            JobType = IntakeEngineJobTypes.Completed,
            SubjectId = runId.ToString("D"),
            CorrelationId = IntakeRunTestBed.CorrelationId,
            IdempotencyKey = $"IntakeEngine:{runId:D}:{operation}:{attempt}",
            Payload = JsonSerializer.SerializeToDocument(payload),
            Attempt = 1,
            MaxAttempts = 1, // as the engine writes every completion (envelope.py): attempt 1 of 1
            DeliveryCount = 1,
        };
    }

    private IntakeRunJobHandler CreateRunHandler()
    {
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.ResolveBindingAsync(ConsumerTypes.IntakeHarness, It.IsAny<string?>(), It.IsAny<IRoutingContext?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? code, IRoutingContext? _, string? _, CancellationToken _) =>
                code == "nda" ? new Binding { BindingId = Guid.NewGuid(), ConsumerType = ConsumerTypes.IntakeHarness, ConsumerCode = "nda" } : null);
        var registry = new IntakeHarnessRegistry([_harness], routing.Object, NullLogger<IntakeHarnessRegistry>.Instance);

        return new IntakeRunJobHandler(
            Run.Store, registry, Run.AgreementAi.Object, Run.TextReader.Object, [Run.Door], Run.Options, Run.Signals.Object, Run.Time,
            Run.Logs.CreateLogger<IntakeRunJobHandler>());
    }

    private sealed class UnconfiguredStaging : IntakeEngineStaging
    {
        public UnconfiguredStaging()
            : base(new TestOptionsMonitor(new IntakePipelineOptions()), new Mock<Azure.Core.TokenCredential>().Object, NullLogger<IntakeEngineStaging>.Instance)
        {
        }
    }
}

/// <summary>A harness whose two external steps call the real dispatcher, as the NDA harness (task 015) will.</summary>
internal sealed class NdaLikeHarness(EngineBed bed) : IIntakeHarness
{
    public int PersistCalls;

    public string Key => "nda";

    public bool CanHandle(IntakeHarnessRoute route) => true;

    public IReadOnlyList<IntakeHarnessStep> Steps { get; } =
    [
        new("segment", IntakeHarnessStage.Analyse, External: true),
        new("redline", IntakeHarnessStage.Build, External: true),
    ];

    public Task<IntakeHarnessStepOutcome> RunStepAsync(
        IntakeHarnessStep step,
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken) =>
        bed.Dispatcher.DispatchAsync(
            bed.Request(request.RunId, step.Name, bed.Attempt) with { CorrelationId = request.CorrelationId }, cancellationToken);

    public Task<IntakeHarnessPersistResult> PersistAsync(
        IntakeHarnessRequest request,
        IReadOnlyDictionary<string, IntakeHarnessResultReference> completedSteps,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref PersistCalls);
        return Task.FromResult(new IntakeHarnessPersistResult
        {
            DocumentIdsByRole = new Dictionary<string, Guid> { ["redline"] = IntakeRunTestBed.OutputDocumentId },
            OutputDocumentId = IntakeRunTestBed.OutputDocumentId,
            ReturnPayload = new IntakeHarnessReturnPayload { DocumentRoles = ["redline"] },
        });
    }
}
