using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using static Sprk.Bff.Api.Tests.Integration.DataMutation.Intake.IntakeApprovalTestBed;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;

/// <summary>
/// The intake approval's write path (spaarke-intake-pipeline-r1 task 017, spec FR-11): what claim, approve and reject write to
/// <c>sprk_servicerequest</c> and <c>sprk_processingjob</c>, what they enqueue, and what they never write twice. Real service, store
/// and orchestrator over the in-memory Dataverse of <see cref="IntakeRunTestBed"/>; doubles only at the access and SPE boundaries.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the claim's single winner, the approval's one Return, the rejection's Cancelled end and the
/// kill switch's "nothing written" are what the Decide-lane item and the door rely on.</remarks>
public sealed class IntakeApprovalServiceTests
{
    // ── The orchestrator raises the trigger ─────────────────────────────────────────────────────

    [Fact]
    public async Task AwaitApproval_MovesTheRequestToAwaitingApproval_AndEnqueuesOneIntakeTriggerAboutTheRequest()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();

        bed.Run.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
        bed.Run.Triggers.Should().ContainSingle().Which.Should().Be(
            (SignalTriggerKind.IntakeAwaitingApproval, IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId, bed.RunId));
        SignalEvaluationTrigger.KindName(SignalTriggerKind.IntakeAwaitingApproval).Should().Be("intake-awaiting-approval");
        SignalEvaluationTrigger.IncludesDoLane("intake-awaiting-approval").Should().BeFalse("the intake approval is a Decide-lane item");
    }

    [Fact]
    public async Task AwaitApproval_Redelivered_WritesNoSecondMarker_AndItsRepeatedTriggerHasTheSameKeyParts()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();

        await bed.Run.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(bed.RunId), CancellationToken.None);

        var expected = (SignalTriggerKind.IntakeAwaitingApproval, IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId, bed.RunId);
        bed.Run.Triggers.Should().Equal([expected, expected],
            "the same subject and source: the same idempotency key, and the evaluation writes only what changed");
        bed.Run.Enqueued.Should().BeEmpty("waiting for approval enqueues no continuation");
    }

    // ── Claim ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_OwningTeamMember_SetsOnlyTheClaimerColumn_ToTheCallersOwnContact()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        bed.ClaimedBy().Should().Be(Alice.ContactId);
        var write = bed.WritesSince(before).Should().ContainSingle().Subject;
        write.Entity.Should().Be(IntakeRunStore.RequestEntity);
        write.Fields.Keys.Should().Equal(IntakeRunStore.RequestClaimedBy);
        ((EntityReference)write.Fields[IntakeRunStore.RequestClaimedBy]!).LogicalName.Should().Be("contact");
    }

    [Fact]
    public async Task Claim_TheCallerAlreadyHoldsIt_IsDone_AndWritesNothing()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        bed.WritesSince(before).Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_SomeoneElseHoldsIt_IsAlreadyClaimed_AndNothingChanges()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Bob.ContactId);
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.AlreadyClaimed);
        bed.ClaimedBy().Should().Be(Bob.ContactId);
        bed.WritesSince(before).Should().BeEmpty();
    }

    [Fact]
    public async Task Claim_TwoAtOnce_ExactlyOneWins_AndTheOtherIsAlreadyClaimed()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var service = bed.Service();
        // Both claims read the request (same row version) before either writes: the run read is held until both have arrived.
        var arrived = 0;
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bed.Run.Dataverse.RunReadGate = async () =>
        {
            if (Interlocked.Increment(ref arrived) >= 2) bothRead.TrySetResult();
            await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var results = await Task.WhenAll(
            Task.Run(() => service.ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)),
            Task.Run(() => service.ClaimAsync(Http(Bob), IntakeRunTestBed.RequestId, CancellationToken.None)));
        bed.Run.Dataverse.RunReadGate = null;

        results.Select(r => r.Outcome).Should().BeEquivalentTo(new[] { IntakeApprovalOutcome.Done, IntakeApprovalOutcome.AlreadyClaimed });
        var winner = results[0].IsDone ? Alice : Bob;
        bed.ClaimedBy().Should().Be(winner.ContactId);
        bed.Run.Dataverse.Writes.Count(w => w.Entity == IntakeRunStore.RequestEntity && w.Fields.ContainsKey(IntakeRunStore.RequestClaimedBy))
            .Should().Be(1, "the losing write was conditional on the version both read");
    }

    [Fact]
    public async Task Claim_RequestNotAwaitingApproval_IsWrongState_AndWritesNothing()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetRequestColumn(IntakeRunStore.RequestIntakeStage, new OptionSetValue(IntakeRequestStages.InReview));
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ClaimAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.WrongState);
        bed.WritesSince(before).Should().BeEmpty();
    }

    // ── Approve ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_Claimer_RecordsTheOutputVersion_AndApprovedOn_AndEnqueuesOneReturnWithTheDoorsKey()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);

        var result = await bed.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        var state = bed.Run.Run(bed.RunId).State;
        state.ApprovedOn.Should().Be(bed.Run.Time.GetUtcNow());
        state.ApprovedVersions.Should().Equal(new Dictionary<Guid, string> { [IntakeRunTestBed.OutputDocumentId] = ApprovedVersion });
        state.Markers.Should().ContainKey(IntakeApprovalService.ApprovalContinuedMarker);
        var job = bed.Run.Enqueued.Should().ContainSingle().Subject;
        job.JobType.Should().Be(IntakeRunJobHandler.JobTypeName);
        job.IdempotencyKey.Should().Be($"intake-return:{bed.RunId:N}");
        job.CorrelationId.Should().Be(IntakeRunTestBed.CorrelationId, "the request's correlation id is carried end to end (NFR-06)");
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.InProgress, "Return runs in the continuation, not in the approval");
    }

    [Fact]
    public async Task Approve_Repeated_WritesNoSecondApproval_AndEnqueuesNoSecondReturn()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        var service = bed.Service();
        await service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);
        var before = bed.Run.Dataverse.Writes.Count;

        var again = await service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        again.Outcome.Should().Be(IntakeApprovalOutcome.Done, "a retry of an approval already recorded is answered as done");
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().ContainSingle();
    }

    [Fact]
    public async Task Approve_TheEnqueueFailedAfterTheApprovalWasWritten_ARetryEnqueuesTheReturnOnce()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        var service = bed.Service();
        bed.Run.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("queue"));

        var first = () => service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);
        await first.Should().ThrowAsync<TimeoutException>("the executor reports it as a failed step; the approval itself is recorded");
        bed.Run.Run(bed.RunId).State.ApprovedOn.Should().NotBeNull();
        bed.Run.Run(bed.RunId).State.Markers.Should().NotContainKey(IntakeApprovalService.ApprovalContinuedMarker, "the marker is written after the send");

        bed.Run.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((JobContract job, CancellationToken _) => bed.Run.Enqueued.Add(job)).Returns(Task.CompletedTask);
        var retry = await service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        retry.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        bed.Run.Enqueued.Should().ContainSingle().Which.IdempotencyKey.Should().Be($"intake-return:{bed.RunId:N}");
        bed.Run.Dataverse.RunWrites(bed.RunId).Count(w => w.Fields.ContainsKey(IntakeRunStore.RunState)
            && IntakeRunState.Parse((string)w.Fields[IntakeRunStore.RunState]!).ApprovedOn is not null
            && IntakeRunState.Parse((string)w.Fields[IntakeRunStore.RunState]!).Markers.ContainsKey(IntakeApprovalService.ApprovalContinuedMarker))
            .Should().Be(1);
    }

    [Fact]
    public async Task Approve_AnOutputVersionCannotBeRead_IsUnavailable_AndWritesAndEnqueuesNothing()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        bed.Files.Setup(f => f.GetCurrentVersionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Unavailable);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_ThenTheContinuation_ReturnsOnceThroughTheDoor_AndTheRunCompletes()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        await bed.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        // The queued continuation is delivered (twice: at-least-once), through the real orchestrator.
        var job = bed.Run.Enqueued.Single();
        await bed.Run.CreateHandler().ProcessAsync(job, CancellationToken.None);
        await bed.Run.CreateHandler().ProcessAsync(job, CancellationToken.None);

        bed.Run.Door.Keys.Should().Equal($"intake-return:{bed.RunId:N}");
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.Completed);
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.Returned);
    }

    [Fact]
    public async Task Approve_Unclaimed_IsWrongState_AndWritesNothing()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var before = bed.Run.Dataverse.Writes.Count;

        var result = await bed.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.WrongState, "the decision is the claimer's: claim first");
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    // ── Reject ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_Claimer_EndsTheRunCancelledNotFailed_AndMovesTheRequestBackToInReview()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);

        var result = await bed.Service().RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        result.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.Cancelled);
        bed.Run.Run(bed.RunId).State.RejectedOn.Should().NotBeNull();
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.InReview);
        bed.Run.Enqueued.Should().BeEmpty("nothing is returned");

        // A late delivery of the run's job is a no-op on the terminal run: no Return.
        await bed.Run.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(bed.RunId), CancellationToken.None);
        bed.Run.Door.Keys.Should().BeEmpty();
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.Cancelled);
    }

    [Fact]
    public async Task Reject_TheStageWriteFailedAfterTheRunEnded_ARetryFinishesTheStageOnly()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        var service = bed.Service();
        await service.RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);
        // As if the request write had not landed: the stage is still Awaiting approval.
        bed.SetRequestColumn(IntakeRunStore.RequestIntakeStage, new OptionSetValue(IntakeRequestStages.AwaitingApproval));
        var runWrites = bed.Run.Dataverse.RunWrites(bed.RunId).Count;

        var retry = await service.RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);

        retry.Outcome.Should().Be(IntakeApprovalOutcome.Done);
        bed.Run.RequestStage().Should().Be(IntakeRequestStages.InReview);
        bed.Run.Dataverse.RunWrites(bed.RunId).Should().HaveCount(runWrites, "the run is not written again");
    }

    [Fact]
    public async Task ApproveAfterReject_AndRejectAfterApprove_AreWrongState_AndWriteNothing()
    {
        var rejected = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        rejected.SetClaimer(Alice.ContactId);
        await rejected.Service().RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);
        var before = rejected.Run.Dataverse.Writes.Count;
        (await rejected.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)).Outcome.Should().Be(IntakeApprovalOutcome.WrongState);
        rejected.WritesSince(before).Should().BeEmpty();
        rejected.Run.Enqueued.Should().BeEmpty();

        var approved = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        approved.SetClaimer(Alice.ContactId);
        await approved.Service().ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None);
        before = approved.Run.Dataverse.Writes.Count;
        (await approved.Service().RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None)).Outcome.Should().Be(IntakeApprovalOutcome.WrongState);
        approved.WritesSince(before).Should().BeEmpty();
        approved.Run.RunStatus(approved.RunId).Should().Be((int)ProcessingJobStatus.InProgress);
    }

    // ── ADR-032: the run's harness switch ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(IntakeApprovalAction.Claim)]
    [InlineData(IntakeApprovalAction.Approve)]
    [InlineData(IntakeApprovalAction.Reject)]
    public async Task HarnessSwitchedOff_EveryActionIsDisabled_AndNothingIsWrittenOrEnqueued(IntakeApprovalAction action)
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        bed.Run.Options.CurrentValue = new IntakePipelineOptions { Harnesses = { ["nda"] = false } };
        var before = bed.Run.Dataverse.Writes.Count;
        var service = bed.Service();

        var result = action switch
        {
            IntakeApprovalAction.Claim => await service.ClaimAsync(Http(Bob), IntakeRunTestBed.RequestId, CancellationToken.None),
            IntakeApprovalAction.Approve => await service.ApproveAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None),
            _ => await service.RejectAsync(Http(Alice), IntakeRunTestBed.RequestId, CancellationToken.None),
        };

        result.Outcome.Should().Be(IntakeApprovalOutcome.Disabled);
        bed.WritesSince(before).Should().BeEmpty();
        bed.Run.Enqueued.Should().BeEmpty();
    }

    // ── The executors over the service ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectExecutor_TheReasonIsNeverLogged_AndTheRunEnds()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        const string reason = "REJECT-REASON-names-the-counterparty";
        var executor = new RejectIntakeExecutor(bed.Service());

        var outcome = await executor.ExecuteAsync(ActionRequest(Alice, new Dictionary<string, string?> { ["reason"] = reason }), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Done);
        outcome.Written.Should().Equal(new DecisionRecordRef(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId));
        bed.Run.RunStatus(bed.RunId).Should().Be((int)ProcessingJobStatus.Cancelled);
        bed.Run.Logs.AllText().Should().NotContain(reason).And.NotContain(IntakeRunTestBed.SecretDocumentText);
    }

    [Fact]
    public async Task ApproveExecutor_HarnessOff_IsRefusedDisabled_SoTheCommitRouteAnswers503()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        bed.Run.Options.CurrentValue = new IntakePipelineOptions { Harnesses = { ["nda"] = false } };
        var executor = new ApproveIntakeExecutor(bed.Service());

        var preflight = await executor.PreflightAsync(ActionRequest(Alice), CancellationToken.None);

        preflight!.Status.Should().Be(DecisionActionStatus.Refused);
        preflight.ReasonCode.Should().Be(DecisionActionReasons.Disabled);
    }

    [Fact]
    public async Task Executors_ASubjectThatIsNotTheRequest_IsSubjectUnsupported()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        var request = ActionRequest(Alice) with { Subject = new DecisionRecordRef("sprk_todo", IntakeRunTestBed.RequestId) };

        (await new ApproveIntakeExecutor(bed.Service()).PreflightAsync(request, CancellationToken.None))!.ReasonCode.Should().Be(DecisionActionReasons.SubjectUnsupported);
        (await new RejectIntakeExecutor(bed.Service()).PreflightAsync(request, CancellationToken.None))!.ReasonCode.Should().Be(DecisionActionReasons.SubjectUnsupported);
    }

    [Fact]
    public async Task ApproveExecutor_TheJobQueueFails_IsAFailedStep_NotAThrow()
    {
        var bed = await new IntakeApprovalTestBed().AwaitingApprovalAsync();
        bed.SetClaimer(Alice.ContactId);
        bed.Run.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("queue"));

        var outcome = await new ApproveIntakeExecutor(bed.Service()).ExecuteAsync(ActionRequest(Alice), CancellationToken.None);

        outcome.Status.Should().Be(DecisionActionStatus.Failed);
        outcome.PossiblyWritten.Should().Equal(new DecisionRecordRef(IntakeRunStore.RequestEntity, IntakeRunTestBed.RequestId));
    }
}
