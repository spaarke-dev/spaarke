using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;

/// <summary>
/// The intake run orchestrator's write path (spaarke-intake-pipeline-r1 task 013, spec FR-07 b–e, FR-10, NFR-06, NFR-08): what a
/// run writes to <c>sprk_processingjob</c> and <c>sprk_servicerequest</c>, in which order, and what it never writes twice.
/// Real handler, store and registry over the in-memory Dataverse boundary of <see cref="IntakeRunTestBed"/>.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the at-least-once, terminal-last, triage and kill-switch guarantees doors, operators
/// and the approval endpoint rely on.</remarks>
public sealed class IntakeRunJobHandlerTests
{
    // ── The run from Receive to Awaiting approval ─────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_NdaRequest_StoresEveryStageAndWaitsInAwaitingApproval()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        var run = bed.Run(runId);
        run.Status.Should().Be((int)ProcessingJobStatus.InProgress, "Awaiting approval is a wait, not a terminal state");
        run.CurrentStage.Should().Be(nameof(IntakeRunStage.Approve));
        run.State.Completed.Should().Equal("Receive", "Store", "Classify", "Route", "Analyse", "Build", "Persist");
        run.State.Classification.Should().Be(new IntakeClassification(true, "nda", 0.93));
        run.State.Markers.Should().ContainKey("approve");
        run.State.Claim.Should().BeNull("the delivery's last write releases the claim");
        run.HarnessKey.Should().Be("nda");
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);

        // Lookups are written as references (task 011 finding 1): the request's current run and the run's output document.
        bed.Dataverse.Get(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId).GetAttributeValue<EntityReference>(IntakeRunStore.RequestCurrentRun)
            .Should().BeEquivalentTo(new EntityReference(IntakeRunStore.RunEntity, runId), o => o.Including(r => r.LogicalName).Including(r => r.Id));
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<EntityReference>(IntakeRunStore.RunOutputDocument)
            .Should().BeEquivalentTo(new EntityReference(IntakeRunStore.DocumentEntity, IntakeRunTestBed.OutputDocumentId), o => o.Including(r => r.LogicalName).Including(r => r.Id));
        bed.ClassifyCalls.Should().Be(1);
        bed.Harness.PersistCalls.Should().Be(1);
        bed.Door.Keys.Should().BeEmpty("nothing returns before approval");
    }

    [Fact]
    public async Task ProcessAsync_ApprovedRun_ReturnsOnceThroughTheDoorAndWritesCompletedLast()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        bed.Approve(runId);
        var writesBefore = bed.Dataverse.Writes.Count;

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Completed);
        bed.RequestStage().Should().Be(IntakeRequestStages.Returned);
        bed.Door.Keys.Should().Equal($"intake-return:{runId:N}");
        bed.ClassifyCalls.Should().Be(1, "the stored classification is reused");
        bed.Harness.PersistCalls.Should().Be(1);

        // Terminal last: the status Completed write is the last write of the run, after the request's Returned write.
        var delivery = bed.Dataverse.Writes.Skip(writesBefore).ToList();
        var last = delivery[^1];
        last.Entity.Should().Be(IntakeRunStore.RunEntity);
        ((OptionSetValue)last.Fields[IntakeRunStore.RunStatus]!).Value.Should().Be((int)ProcessingJobStatus.Completed);
        delivery.Take(delivery.Count - 1).Should().NotContain(w => w.Fields.ContainsKey(IntakeRunStore.RunStatus),
            "no other write of the delivery touches the status");
        delivery.FindIndex(w => w.Entity == IntakeRunStore.RequestEntity).Should().BeLessThan(delivery.Count - 1);
    }

    // ── FR-07c: terminal first ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_RunAlreadyCompleted_IsANoOpWithNoWrite()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        bed.Approve(runId);
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        var writes = bed.Dataverse.Writes.Count;

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.Dataverse.Writes.Count.Should().Be(writes);
        bed.Door.Keys.Should().HaveCount(1);
    }

    // ── FR-07b: duplicate delivery ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_SameMessageDeliveredTwiceConcurrently_RunsAiAndPersistOnce()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        var job = IntakeRunTestBed.Job(runId);

        // The race that matters: the second delivery reads the run BEFORE the first claims it, and only acts on that read
        // once the first is already inside the AI call. A check-then-set claim lets it in on its stale read.
        var secondHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstIsClassifying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        bed.Dataverse.RunReadGate = () => Interlocked.Increment(ref reads) switch
        {
            1 => secondHasRead.Task.WaitAsync(TimeSpan.FromSeconds(30)),
            2 => SignalThenWait(secondHasRead, firstIsClassifying.Task),
            _ => Task.CompletedTask,
        };
        bed.AgreementAi.Setup(a => a.ClassifyAsync(It.IsAny<AgreementClassifyRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref bed.ClassifyCalls);
                firstIsClassifying.TrySetResult();
            })
            .ReturnsAsync(IntakeRunTestBed.Classified());

        var outcomes = await Task.WhenAll(
            Task.Run(() => bed.CreateHandler().ProcessAsync(job, CancellationToken.None)),
            Task.Run(() => bed.CreateHandler().ProcessAsync(job, CancellationToken.None)));

        outcomes.Select(o => o.Status).Should().BeEquivalentTo([JobStatus.Completed, JobStatus.Failed],
            "exactly one delivery claims the run; the other's claim write is refused on its stale version and it is retried");
        bed.ClassifyCalls.Should().Be(1);
        bed.Harness.StepCalls.Should().OnlyContain(kv => kv.Value == 1);
        bed.Harness.PersistCalls.Should().Be(1);
        bed.Harness.CreatedDocuments.Should().HaveCount(1);
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
    }

    [Fact]
    public async Task ProcessAsync_RedeliveredWhileAwaitingApproval_CreatesNothingAndSendsNothing()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        var markers = bed.Run(runId).State.Markers.ToDictionary(kv => kv.Key, kv => kv.Value);

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.ClassifyCalls.Should().Be(1);
        bed.Harness.StepCalls.Should().OnlyContain(kv => kv.Value == 1);
        bed.Harness.PersistCalls.Should().Be(1);
        bed.Door.Keys.Should().BeEmpty();
        bed.Run(runId).State.Markers.Should().BeEquivalentTo(markers);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    [Fact]
    public async Task ProcessAsync_ClaimHeldByAnotherLiveDelivery_WritesNothingAndIsRetried()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        SetClaim(bed, runId, owner: "another-delivery", bed.Time.GetUtcNow());
        var writes = bed.Dataverse.Writes.Count;

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        var final = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, deliveryCount: JobContract.MaxDeliveryCount), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
        final.Status.Should().Be(JobStatus.Poisoned);
        bed.Dataverse.Writes.Count.Should().Be(writes);
        bed.ClassifyCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_SameJobRedeliveredAfterACrashThatLeftItsClaim_TakesItBackAfterOneMinute()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        var jobId = Guid.NewGuid();
        SetClaim(bed, runId, owner: IntakeRunStore.ClaimOwner(jobId), bed.Time.GetUtcNow());

        var tooSoon = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId), CancellationToken.None);
        bed.Time.Advance(IntakeRunStore.OwnerTakeoverAge);
        var redelivered = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId, deliveryCount: 2), CancellationToken.None);

        tooSoon.Status.Should().Be(JobStatus.Failed, "a duplicate send of the same job arrives within seconds and must not run");
        redelivered.Status.Should().Be(JobStatus.Completed);
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
    }

    [Fact]
    public async Task ProcessAsync_SameMessageRedeliveredWhileTheFirstDeliveryStillRuns_TheOverrunDeliveryWritesNothingMore()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        var jobId = Guid.NewGuid();

        // The first delivery is stuck in the AI call past its message lock; Service Bus redelivers the SAME message.
        var firstInAi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource<AgreementClassifyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondInAi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<AgreementClassifyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        bed.AgreementAi.Setup(a => a.ClassifyAsync(It.IsAny<AgreementClassifyRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstInAi.SetResult();
                    return firstResult.Task;
                }

                secondInAi.SetResult();
                return secondResult.Task;
            });

        var first = Task.Run(() => bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId, deliveryCount: 1), CancellationToken.None));
        await firstInAi.Task.WaitAsync(TimeSpan.FromSeconds(30));
        bed.Time.Advance(TimeSpan.FromMinutes(2));
        var second = Task.Run(() => bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId, deliveryCount: 2), CancellationToken.None));
        await secondInAi.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var writesBeforeTheOverrunResumes = bed.Dataverse.Writes.Count;

        firstResult.SetResult(IntakeRunTestBed.Classified());
        var overrun = await first;

        overrun.Status.Should().Be(JobStatus.Failed, "its claim was taken over by the redelivery");
        bed.Dataverse.Writes.Count.Should().Be(writesBeforeTheOverrunResumes, "the overrun delivery's fence refuses every further write");

        secondResult.SetResult(IntakeRunTestBed.Classified());
        (await second).Status.Should().Be(JobStatus.Completed);
        bed.Harness.StepCalls.Should().OnlyContain(kv => kv.Value == 1);
        bed.Harness.PersistCalls.Should().Be(1);
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
    }

    // ── FR-10: triage slot ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100000002, 0.93, IntakeRunCodes.TriageNoHarness)] // policy question: no intake-harness Binding
    [InlineData(100000000, 0.84, IntakeRunCodes.TriageLowConfidence)] // NDA below the 0.85 threshold
    public async Task ProcessAsync_NoHarnessOrConfidenceBelowThreshold_HandsOffToTriageAndCompletesNotFailed(
        int requestType, double confidence, string expectedCode)
    {
        var bed = new IntakeRunTestBed(requestType, confidence);
        var runId = await bed.CreateRunAsync();

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Completed, "a triaged run ends Completed (a no-op), never Failed");
        var run = bed.Run(runId);
        run.State.Route!.TriageCode.Should().Be(expectedCode);
        run.State.Markers.Should().ContainKey("triage");
        bed.RequestStage().Should().Be(IntakeRequestStages.NeedsTriage);
        bed.Harness.StepCalls.Should().BeEmpty("no AI output is produced for a triaged request");
        bed.Harness.PersistCalls.Should().Be(0);
        bed.Dataverse.RunWrites(runId)[^1].Fields[IntakeRunStore.RunStatus].Should().BeOfType<OptionSetValue>()
            .Which.Value.Should().Be((int)ProcessingJobStatus.Completed, "the terminal write is last");
    }

    [Fact]
    public async Task ProcessAsync_ConfidenceExactlyAtThreshold_RoutesToTheHarness()
    {
        var bed = new IntakeRunTestBed(confidence: 0.85);
        var runId = await bed.CreateRunAsync();

        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        bed.Run(runId).State.Route!.HarnessKey.Should().Be("nda");
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
    }

    // ── NFR-08: harness kill switch ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_HarnessDisabled_HoldsWithAttention_AndReEnablingAndRedrivingResumes()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        bed.Options.CurrentValue = new IntakePipelineOptions { Harnesses = { ["nda"] = false } };

        var held = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        held.Status.Should().Be(JobStatus.Poisoned);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress, "held is neither Failed nor Completed");
        var row = bed.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeRunCodes.HarnessDisabled);
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorMessage).Should().Contain("re-drive");
        bed.Harness.StepCalls.Should().BeEmpty();
        bed.ClassifyCalls.Should().Be(1);

        bed.Options.CurrentValue = new IntakePipelineOptions();
        var resumed = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        resumed.Status.Should().Be(JobStatus.Completed);
        bed.ClassifyCalls.Should().Be(1, "the stored classification is not repeated on resume");
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).Contains(IntakeRunStore.RunErrorCode).Should().BeFalse("resuming clears the attention text");
    }

    [Fact]
    public async Task ProcessAsync_AiSwitchedOff_HoldsTheRunInsteadOfFailingIt()
    {
        var bed = new IntakeRunTestBed();
        bed.AgreementAi.Setup(a => a.ClassifyAsync(It.IsAny<AgreementClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureDisabledException("ai.agreement.disabled", "off"));
        var runId = await bed.CreateRunAsync();

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.InProgress);
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeRunCodes.AiDisabled);
    }

    // ── FR-07d: exhausted run ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_FinalDeliveryFails_IsDeadLetteredWithAttentionAndNeverCompleted()
    {
        var bed = new IntakeRunTestBed();
        bed.AgreementAi.Setup(a => a.ClassifyAsync(It.IsAny<AgreementClassifyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AgreementAiException(AgreementAiException.CompletionFailed, ConsumerTypes.AgreementClassify, "model call failed"));
        var runId = await bed.CreateRunAsync();

        var early = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, deliveryCount: 1), CancellationToken.None);

        early.Status.Should().Be(JobStatus.Failed, "a transient failure before the final delivery is retried");
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().BeNull("a retried failure writes no attention text");

        var final = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, deliveryCount: JobContract.MaxDeliveryCount), CancellationToken.None);

        final.Status.Should().Be(JobStatus.Poisoned);
        final.ErrorMessage.Should().Be(IntakeRunCodes.Exhausted);
        var row = bed.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        row.GetAttributeValue<OptionSetValue>(IntakeRunStore.RunStatus).Value.Should().NotBe((int)ProcessingJobStatus.Completed);
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeRunCodes.Exhausted);
        row.GetAttributeValue<string>(IntakeRunStore.RunErrorMessage).Should().Contain(AgreementAiException.CompletionFailed);
        bed.Run(runId).State.Held!.Stage.Should().Be(nameof(IntakeRunStage.Classify));
    }

    // ── FR-07e / NFR-06: no content, correlation everywhere ───────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_PayloadLogsAndRunRows_CarryNoContentAndEveryLogScopeCarriesTheCorrelationId()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        var job = IntakeRunTestBed.Job(runId);

        await bed.CreateHandler().ProcessAsync(job, CancellationToken.None);
        bed.Approve(runId);
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        var payload = job.Payload!.RootElement.GetRawText();
        payload.Should().NotContain("CONFIDENTIAL").And.NotContain("CLASSIFIER");
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload)!.Keys.Should().BeEquivalentTo(["requestId", "runId"]);

        var written = IntakeAssertions.Json(bed.Dataverse.Writes.Select(w => w.Fields.ToDictionary(f => f.Key, f => f.Value?.ToString())));
        written.Should().NotContain("CONFIDENTIAL").And.NotContain("CLASSIFIER");

        var logs = bed.Logs.AllText();
        logs.Should().NotContain("CONFIDENTIAL").And.NotContain("CLASSIFIER");
        bed.Logs.Entries.Where(e => e.Message.Contains("Intake run", StringComparison.Ordinal) || e.Message.Contains("Intake request", StringComparison.Ordinal))
            .Should().NotBeEmpty()
            .And.OnlyContain(e => IntakeAssertions.HasCorrelation(e.Scopes, IntakeRunTestBed.CorrelationId),
                "every log line of the run is written inside the run's correlation scope");
    }

    // ── Intake rows only (notes/decisions.md 2026-11-27) ─────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_RequestWithNoIntakeStage_IsNeverProcessedOrWritten()
    {
        var bed = new IntakeRunTestBed(intakeStage: null); // an ontology outbound inquiry row
        var runId = await bed.CreateRunAsync();

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Failed);
        bed.Dataverse.Writes.Should().NotContain(w => w.Entity == IntakeRunStore.RequestEntity);
        bed.ClassifyCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_ContentThatIsNotADocumentOfTheRequest_FailsBeforeAnyAiCall()
    {
        var bed = new IntakeRunTestBed();
        var stranger = Guid.Parse("01300000-0000-4000-8000-0000000000e9");
        bed.Dataverse.Seed(new Entity(IntakeRunStore.DocumentEntity, stranger)
        {
            ["statecode"] = new OptionSetValue(0),
            [IntakeRunStore.DocumentRequest] = new EntityReference(IntakeRunStore.RequestEntity, Guid.NewGuid()),
        });
        var runId = await bed.CreateRunAsync("other-key", stranger);

        var outcome = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Failed);
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunErrorCode).Should().Be(IntakeRunCodes.ContentInvalid);
        bed.TextReads.Should().Be(0);
        bed.ClassifyCalls.Should().Be(0);
    }

    // ── Run create (doors) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRunAsync_SamePassKeyTwice_CreatesOneRunWhoseRequestIsAReference()
    {
        var bed = new IntakeRunTestBed();

        var first = await bed.CreateRunAsync("front-door-submit");
        var second = await bed.CreateRunAsync("front-door-submit");

        second.Should().Be(first);
        bed.Dataverse.Writes.Where(w => w.Entity == IntakeRunStore.RunEntity).Should().ContainSingle();
        var row = bed.Dataverse.Get(IntakeRunStore.RunEntity, first);
        row[IntakeRunStore.RunRequest].Should().BeOfType<EntityReference>().Which.Id.Should().Be(IntakeRunTestBed.RequestId);
        row.GetAttributeValue<OptionSetValue>(IntakeRunStore.RunJobType).Value.Should().Be((int)ProcessingJobType.IntakeRun);
        row.GetAttributeValue<OptionSetValue>(IntakeRunStore.RunStatus).Value.Should().Be((int)ProcessingJobStatus.Pending);
    }

    private static Task SignalThenWait(TaskCompletionSource signal, Task wait)
    {
        signal.TrySetResult();
        return wait.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void SetClaim(IntakeRunTestBed bed, Guid runId, string owner, DateTimeOffset at)
    {
        var row = bed.Dataverse.Get(IntakeRunStore.RunEntity, runId);
        var state = IntakeRunState.Parse(row.GetAttributeValue<string>(IntakeRunStore.RunState));
        state.Claim = new IntakeRunClaim(owner, at);
        row[IntakeRunStore.RunState] = state.Serialize();
        bed.Dataverse.Seed(row);
    }
}
