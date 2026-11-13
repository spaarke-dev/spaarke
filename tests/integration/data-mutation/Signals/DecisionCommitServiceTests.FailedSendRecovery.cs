using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 191 (D-222, D-223): a budget inquiry whose service request landed and whose email did not go out (NotSent) or
/// may have gone out (Unknown). NotSent resumes on retry with the landed service request (no second create), sends once and
/// writes the one Decision Record. Unknown needs the person's choice (Send again / Record without sending) on the same POST; a
/// retry without one is the stored failure again (409) and does nothing. A replayed failure is never a 500.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly DecisionRecordRef LandedServiceRequest = new("sprk_servicerequest", ServiceRequestId);

    private static DecisionActionOutcome InquiryFailed(DecisionSendFailure failure) =>
        DecisionActionOutcome.Failed("send-budget-inquiry", DecisionActionReasons.WriteFailed, "The service request was created; the email was not sent.",
            written: [LandedServiceRequest]) with
        { SendFailure = failure };

    private static int Count(Rig rig, string entry) => rig.Log.Count(l => l == entry);

    // ── NotSent: resumable, reuses the landed service request, sends once, records ─────────────────────────────────

    [Fact]
    public async Task ANotSentInquiry_IsResumable_AndTheRetryReusesTheLandedServiceRequest_SendsOnce_AndRecords()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();

        var failed = await rig.CommitAsync(review);

        failed.Status.Should().Be(DecisionCommitStatus.Failed);
        failed.Resumable.Should().BeTrue("the email definitely did not go out and the service request id is known");
        failed.SendChoiceRequired.Should().BeFalse();
        failed.Landed.Should().Contain(new DecisionCommitRef("sprk_servicerequest", ServiceRequestId));

        rig.Executors["send-budget-inquiry"].Result = null;
        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        Count(rig, "execute:revise-budget").Should().Be(1, "the revision that landed is never written again");
        Count(rig, "execute:add-todo").Should().Be(1, "the To Do that landed is never written again");
        Count(rig, "execute:send-budget-inquiry").Should().Be(2, "the retry sends once");
        rig.Executors["send-budget-inquiry"].Requests[0].Landed.Should().BeNull("the first attempt creates the service request");
        rig.Executors["send-budget-inquiry"].Requests[1].Landed.Should().Equal(LandedServiceRequest);
        rig.Records.Should().ContainSingle();
        var steps = Steps(rig.Record).GetProperty("steps");
        steps[0].GetProperty("outcome").GetString().Should().Be("Done");
        steps[1].GetProperty("outcome").GetString().Should().Be("Done");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);

        var again = await rig.CommitAsync(review with { Retry = true });
        again.Status.Should().Be(DecisionCommitStatus.Recorded);
        again.Replayed.Should().BeTrue();
        Count(rig, "execute:send-budget-inquiry").Should().Be(2, "a recorded decision sends nothing more");
    }

    [Fact]
    public async Task ANotSentInquiry_HandsOnlyTheOneRevision_ToTheBudgetRevisionTrigger()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();

        await rig.CommitAsync(review);
        rig.Executors["send-budget-inquiry"].Result = null;
        await rig.CommitAsync(review with { Retry = true });

        rig.FailedHandedOn.SelectMany(s => s).SelectMany(s => s.Written).Where(w => w.Entity == "sprk_budgetrevision")
            .Select(w => w.Id).Distinct().Should().Equal(RevisionId);
        Count(rig, "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task ANotSentInquiry_ThatFailsAgainNotSent_StaysResumable_WithTheSameServiceRequest()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();

        await rig.CommitAsync(review);
        var second = await rig.CommitAsync(review with { Retry = true });

        second.Status.Should().Be(DecisionCommitStatus.Failed);
        second.Replayed.Should().BeFalse("the retry ran");
        second.Resumable.Should().BeTrue();
        rig.Executors["send-budget-inquiry"].Requests[1].Landed.Should().Equal(LandedServiceRequest);

        rig.Executors["send-budget-inquiry"].Result = null;
        var third = await rig.CommitAsync(review with { Retry = true });

        third.Status.Should().Be(DecisionCommitStatus.Recorded, third.Detail);
        rig.Executors["send-budget-inquiry"].Requests[2].Landed.Should().Equal(LandedServiceRequest);
        Count(rig, "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task AFailedInquiryWithNoKnownSendOutcome_IsNotResumable_MustNotFire()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = DecisionActionOutcome.Failed("send-budget-inquiry", DecisionActionReasons.WriteFailed, "no",
            written: [LandedServiceRequest]);
        var review = PathBReview();

        var failed = await rig.CommitAsync(review);
        var retried = await rig.CommitAsync(review with { Retry = true });

        failed.Resumable.Should().BeFalse("only a failure that names its send outcome resumes");
        retried.ReasonCode.Should().Be(DecisionCommitReasons.NotResumable);
        Count(rig, "execute:send-budget-inquiry").Should().Be(1);
    }

    // ── Unknown: the person's choice, on the same POST ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AMaybeSentInquiry_RetryWithoutAChoice_IsTheStoredFailureAgain_NamingTheChoice_AndDoesNothing()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var review = PathBReview();

        var failed = await rig.CommitAsync(review);
        failed.Resumable.Should().BeTrue();
        failed.SendChoiceRequired.Should().BeTrue();
        var log = rig.Log.Count;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Failed);
        retried.Replayed.Should().BeTrue();
        retried.SendChoiceRequired.Should().BeTrue();
        retried.ReasonCode.Should().Be(DecisionCommitReasons.SendChoiceRequired);
        retried.Detail.Should().Contain("Sent Items");
        retried.Landed.Should().Equal(failed.Landed);
        rig.Log.Should().HaveCount(log, "no preflight, no write, no record");
        rig.Records.Should().BeEmpty();

        var reply = DecisionCommitEndpoints.ToHttp(retried, Http());
        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)reply).StatusCode.Should().Be(409, "a replayed known failure is never a 500");
        var problem = reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject.ProblemDetails;
        problem.Extensions["sendChoiceRequired"].Should().Be(true);
        problem.Extensions["replayed"].Should().Be(true);
        problem.Extensions["reasonCode"].Should().Be(DecisionCommitReasons.SendChoiceRequired);
    }

    [Fact]
    public async Task AMaybeSentInquiry_SendAgain_SendsOnceWithTheLandedServiceRequest_AndRecords()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var review = PathBReview();
        await rig.CommitAsync(review);
        rig.Executors["send-budget-inquiry"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.SendAgain });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        Count(rig, "execute:send-budget-inquiry").Should().Be(2);
        rig.Executors["send-budget-inquiry"].Requests[1].Landed.Should().Equal(LandedServiceRequest);
        Count(rig, "execute:revise-budget").Should().Be(1);
        rig.Records.Should().ContainSingle();
        Steps(rig.Record).GetProperty("steps")[0].GetProperty("outcome").GetString().Should().Be("Done");
    }

    [Fact]
    public async Task AMaybeSentInquiry_RecordWithoutSending_SendsNothing_AndRecordsTheStepAsNotSentByChoice()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var review = PathBReview();
        await rig.CommitAsync(review);
        var preflights = Count(rig, "preflight:send-budget-inquiry");

        var retried = await rig.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.RecordWithoutSending });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        Count(rig, "execute:send-budget-inquiry").Should().Be(1, "nothing is sent");
        Count(rig, "preflight:send-budget-inquiry").Should().Be(preflights, "a step that will not run is not checked");
        Count(rig, "execute:revise-budget").Should().Be(1);
        retried.Steps.Should().Contain(s => s.Code == "send-budget-inquiry" && s.Status == DecisionCommitService.NotSentByChoiceStatus);
        var inquiry = Steps(rig.Record).GetProperty("steps")[0];
        inquiry.GetProperty("outcome").GetString().Should().Be(DecisionCommitService.NotSentByChoiceStatus);
        inquiry.GetProperty("written").EnumerateArray().Select(w => w.GetProperty("id").GetGuid()).Should().Equal(new[] { ServiceRequestId },
            "the service request that landed stands");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
    }

    [Fact]
    public async Task ASendChoice_WhereNoneIsNeeded_IsRefused_AndDoesNothing()
    {
        var notSent = new Rig();
        notSent.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await notSent.CommitAsync(review);
        var log = notSent.Log.Count;

        var choiceOnNotSent = await notSent.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.RecordWithoutSending });

        choiceOnNotSent.Status.Should().Be(DecisionCommitStatus.Refused);
        choiceOnNotSent.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInvalid);
        notSent.Log.Should().HaveCount(log);

        var fresh = new Rig();
        var firstWithChoice = await fresh.CommitAsync(PathBReview() with { SendChoice = DecisionReview.SendAgain });
        firstWithChoice.Status.Should().Be(DecisionCommitStatus.Refused);
        firstWithChoice.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInvalid);
        fresh.Writes.Should().BeEmpty();

        var unknownChoice = new Rig();
        unknownChoice.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var maybe = PathBReview();
        await unknownChoice.CommitAsync(maybe);
        var bogus = await unknownChoice.CommitAsync(maybe with { Retry = true, SendChoice = "send-twice" });
        bogus.Status.Should().Be(DecisionCommitStatus.Refused);
        bogus.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInvalid);
        Count(unknownChoice, "execute:send-budget-inquiry").Should().Be(1);
    }

    // ── The Signal while the decision is unfinished (both orders) ───────────────────────────────────────────────

    [Fact]
    public async Task TheRetryBeforeTheTriggerClosesTheSignal_ClosesItActed_WithTheRecord()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        rig.Status(Primary).Should().NotBe(Resolved, "a failed commit closes nothing");
        rig.Executors["send-budget-inquiry"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        rig.LinkedRecord(Primary).Should().Be(RecordId);
    }

    [Fact]
    public async Task TheTriggerClosingTheSignalAfterTheFailure_DoesNotLeaveItClosed_TheRetryReclosesItActed()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var review = PathBReview();
        await rig.CommitAsync(review);

        // The budget-revision trigger closes the Signal ConditionCleared 33 s after the failure (as on dev).
        CloseByTheSystem(rig, TimeSpan.FromSeconds(33));
        rig.Executors["send-budget-inquiry"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.SendAgain });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed, "the decision is what resolved it, so Signal and record agree");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
    }

    [Fact]
    public async Task ARetryAfterThePlanReadWindow_OfASignalTheSystemClosed_IsAConflict_AndDoesNothing()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        CloseByTheSystem(rig, TimeSpan.FromSeconds(33));
        rig.Clock.Advance(TimeSpan.FromHours(5));
        var log = rig.Log.Count;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Conflict);
        rig.Log.Should().HaveCount(log, "nothing runs");
        rig.Records.Should().BeEmpty();
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionConditionCleared);
    }

    // ── The HTTP mapping of a replayed failure ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AReplayedFailure_Is409_WithTheFirstFailuresBody_WhileAFirstFailureStays500()
    {
        var steps = new[] { new DecisionCommitStep("action:revise-budget", "revise-budget", 2, "Done", [new("sprk_budgetrevision", RevisionId)], [], null, null) };
        DecisionCommitResult Failed(bool replayed) => new()
        {
            Status = DecisionCommitStatus.Failed,
            ReviewId = Guid.NewGuid(),
            ReasonCode = DecisionCommitReasons.NotResumable,
            Steps = steps,
            Replayed = replayed,
        };

        var replay = DecisionCommitEndpoints.ToHttp(Failed(replayed: true), Http());
        var first = DecisionCommitEndpoints.ToHttp(Failed(replayed: false), Http());

        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)replay).StatusCode.Should().Be(409);
        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)first).StatusCode.Should().Be(500);
        var problem = replay.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject.ProblemDetails;
        problem.Extensions["steps"].Should().BeEquivalentTo(steps);
        problem.Title.Should().Be("The decision was not recorded");
    }

    private static void CloseByTheSystem(Rig rig, TimeSpan after)
    {
        rig.Clock.Advance(after);
        rig.Signals[Primary]["sprk_signalstatus"] = new OptionSetValue(Resolved);
        rig.Signals[Primary]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionConditionCleared);
        rig.Signals[Primary]["sprk_resolvedon"] = rig.Clock.GetUtcNow().UtcDateTime;
        rig.Versions[Primary]++;
    }
}
