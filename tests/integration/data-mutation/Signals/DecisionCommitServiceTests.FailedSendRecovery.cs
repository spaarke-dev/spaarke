using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
        steps[0].GetProperty("written").EnumerateArray().Select(w => w.GetProperty("id").GetGuid())
            .Should().Equal(new[] { ServiceRequestId, CommunicationId }, "the landed service request and the communication the retry sent");
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
        // Fix round 2: a resumable failure with no landed inquiry (the To Do refused) has nothing to send or not send.
        var noInquiry = new Rig();
        noInquiry.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        (await noInquiry.CommitAsync(review)).Resumable.Should().BeTrue();
        var log = noInquiry.Log.Count;

        var choiceWithoutInquiry = await noInquiry.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.RecordWithoutSending });

        choiceWithoutInquiry.Status.Should().Be(DecisionCommitStatus.Refused);
        choiceWithoutInquiry.ReasonCode.Should().Be(DecisionCommitReasons.ReviewInvalid);
        noInquiry.Log.Should().HaveCount(log);

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
    public async Task AnUnfinishedReview_FinishedAfterThePlanReadWindow_StillReclosesTheSignalTheTriggerClosed_Acted()
    {
        // Fix round 1 (D-222): the window does not apply to a review whose writes landed; the person may take days to check Sent Items.
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        CloseByTheSystem(rig, TimeSpan.FromSeconds(33));
        rig.Clock.Advance(TimeSpan.FromDays(3));
        rig.Executors["send-budget-inquiry"].Result = null;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Recorded, retried.Detail);
        retried.Warnings.Should().Contain("reclosed_after_system_resolution");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed, "the decision being finished is what resolved it");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        Count(rig, "execute:revise-budget").Should().Be(1);
    }

    // ── Fix round 1 (criterion 134): the unfinished review is reachable from the item, and nothing can start beside it ──────

    [Fact]
    public async Task ThePlanRead_ReturnsTheUnfinishedReview_ToItsOwner_AndPostingItBackFinishesIt()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.Unknown);
        var review = PathBReview();
        await rig.CommitAsync(review);

        var unfinished = await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None);

        unfinished.Should().NotBeNull("the wizard reopened on the item resumes this review instead of starting another");
        unfinished!.ReviewId.Should().Be(review.ReviewId);
        unfinished.Review.Retry.Should().BeFalse();
        unfinished.Review.SendChoice.Should().BeNull();
        unfinished.Failure.Status.Should().Be(DecisionCommitStatus.Failed);
        unfinished.Failure.Replayed.Should().BeTrue();
        unfinished.Failure.Resumable.Should().BeTrue();
        unfinished.Failure.SendChoiceRequired.Should().BeTrue("the client offers Send again / Record without sending from it");
        unfinished.Failure.Landed.Should().Contain(new DecisionCommitRef("sprk_servicerequest", ServiceRequestId));
        unfinished.Failure.Steps.Select(s => (s.Code, s.Status)).Should().Contain(("revise-budget", "Done"));
        var plan = DecisionPlanResponse.From(Primary, DecisionLane.Decide, Guid.NewGuid(), new ResolvedDecisionPlan([], [], []), unfinished: unfinished);
        plan.Unfinished.Should().BeSameAs(unfinished);

        // The review as stored is posted back unchanged (same content hash) with the choice.
        rig.Executors["send-budget-inquiry"].Result = null;
        var finished = await rig.CommitAsync(unfinished.Review with { Retry = true, SendChoice = DecisionReview.SendAgain });

        finished.Status.Should().Be(DecisionCommitStatus.Recorded, finished.Detail);
        Count(rig, "execute:revise-budget").Should().Be(1);
        rig.Executors["send-budget-inquiry"].Requests[1].Landed.Should().Equal(LandedServiceRequest);
        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().BeNull("a recorded review is finished");
        (await rig.CommitAsync(PathBReview())).ReasonCode.Should().NotBe(DecisionCommitReasons.ReviewUnfinished, "nothing holds the item any more");
    }

    [Fact]
    public async Task ANewReview_OnASignalWithAnUnfinishedOne_IsRefused409_AndRunsNothing()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        await rig.CommitAsync(PathBReview());
        var log = rig.Log.Count;

        // An old client (or a reopened wizard that ignores the plan's unfinished review) starts a new review id.
        var fresh = await rig.CommitAsync(PathBReview());

        fresh.Status.Should().Be(DecisionCommitStatus.Conflict);
        fresh.ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished);
        ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)DecisionCommitEndpoints.ToHttp(fresh, Http())).StatusCode.Should().Be(409);
        rig.Log.Should().HaveCount(log, "nothing runs: no second revision, no second service request");
        Count(rig, "execute:revise-budget").Should().Be(1);
        rig.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherPerson_DoesNotGetTheUnfinishedReview_AndCannotStartANewOne()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        var other = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("tid", Tenant), new System.Security.Claims.Claim("oid", Guid.NewGuid().ToString("D"))], "test")),
        };
        other.Request.Headers.Authorization = "Bearer other-token";
        var log = rig.Log.Count;

        (await rig.Service().ReadUnfinishedAsync(other, Primary, CancellationToken.None)).Should().BeNull();
        var theirs = await rig.CommitAsync(review, http: other);

        theirs.ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished, "even the same review id from another person would run the landed writes again");
        theirs.Detail.Should().StartWith("Another user started a decision on this item on ", "the author's token carried no name")
            .And.Contain(" UTC that is unfinished. Ask them to finish it first.");
        rig.Log.Should().HaveCount(log);
    }

    [Fact]
    public async Task FixRound3_TheRefusalAnotherPersonSees_NamesWhoStartedTheUnfinishedDecision_AndWhen()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var started = rig.Clock.GetUtcNow();
        var author = Http();
        ((System.Security.Claims.ClaimsIdentity)author.User.Identity!).AddClaim(new System.Security.Claims.Claim("name", "Pat Author"));
        await rig.CommitAsync(PathBReview(), http: author);
        var other = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("tid", Tenant), new System.Security.Claims.Claim("oid", Guid.NewGuid().ToString("D"))], "test")),
        };
        other.Request.Headers.Authorization = "Bearer other-token";

        var theirs = await rig.CommitAsync(PathBReview(), http: other);

        theirs.ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished);
        theirs.Detail.Should().Be(
            $"Pat Author started a decision on this item on {started.UtcDateTime:yyyy-MM-dd HH:mm} UTC that is unfinished. Ask them to finish it first. Nothing was done.");
    }

    [Fact]
    public async Task AFailureThatCannotBeResumed_HoldsNothing_MustNotFire()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = DecisionActionOutcome.Failed("send-budget-inquiry", DecisionActionReasons.WriteFailed, "no",
            written: [LandedServiceRequest]);
        await rig.CommitAsync(PathBReview());

        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().BeNull("only a resumable failure is unfinished");
        rig.Executors["send-budget-inquiry"].Result = null;
        var next = await rig.CommitAsync(PathBReview());
        next.ReasonCode.Should().NotBe(DecisionCommitReasons.ReviewUnfinished);
    }

    [Fact]
    public async Task APointerThatCannotBeRead_RefusesANewReview_503_FailClosed()
    {
        var rig = new Rig();
        rig.LedgerStore = new UnfinishedFaultLedger(rig.Ledger);
        var log = rig.Log.Count;

        var result = await rig.CommitAsync(PathBReview());

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        rig.Log.Should().HaveCount(log);
        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().BeNull("the plan read never fails on it");
    }

    // ── Fix round 2 ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANotSentSendThatKeepsFailing_IsFinishedByRecordWithoutSending_AndTheItemIsReleased()
    {
        // e.g. 403 SendAsDenied: the same NotSent on every retry. The person records the decision without sending.
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        (await rig.CommitAsync(review with { Retry = true })).Status.Should().Be(DecisionCommitStatus.Failed);
        (await rig.CommitAsync(review with { Retry = true })).Resumable.Should().BeTrue("it keeps failing NotSent");

        var recorded = await rig.CommitAsync(review with { Retry = true, SendChoice = DecisionReview.RecordWithoutSending });

        recorded.Status.Should().Be(DecisionCommitStatus.Recorded, recorded.Detail);
        recorded.Steps.Should().Contain(s => s.Code == "send-budget-inquiry" && s.Status == DecisionCommitService.NotSentByChoiceStatus);
        Count(rig, "execute:send-budget-inquiry").Should().Be(3, "the record-without-sending attempt sends nothing");
        Count(rig, "execute:revise-budget").Should().Be(1);
        rig.Records.Should().ContainSingle();
        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().BeNull("the item no longer holds it");
        (await rig.CommitAsync(PathBReview())).ReasonCode.Should().NotBe(DecisionCommitReasons.ReviewUnfinished);
    }

    [Fact]
    public async Task AnAlsoResolveItem_HeldByTheUnfinishedReview_OffersIt_NamingThePrimary_AndPostingThereFinishesBoth()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview(also: [Second]);
        await rig.CommitAsync(review);

        (await rig.CommitAsync(PathBReview(), Second)).ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished);
        var offered = await rig.Service().ReadUnfinishedAsync(Http(), Second, CancellationToken.None);

        offered.Should().NotBeNull("reopening the also-resolve item must offer the review that holds it");
        offered!.ReviewId.Should().Be(review.ReviewId);
        offered.SignalId.Should().Be(Primary, "the review is finished on its primary item");
        rig.Executors["send-budget-inquiry"].Result = null;
        var finished = await rig.CommitAsync(offered.Review with { Retry = true }, offered.SignalId);
        finished.Status.Should().Be(DecisionCommitStatus.Recorded, finished.Detail);
        rig.LinkedRecord(Second).Should().Be(RecordId);
        (await rig.Service().ReadUnfinishedAsync(Http(), Second, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ALateResume_DoesNotOverwriteASupersededClosure_ItIsTheWindowsRefusal_AndReleasesTheItem()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        CloseByTheSystem(rig, TimeSpan.FromSeconds(33), SignalWriter.ResolutionSuperseded);
        rig.Clock.Advance(TimeSpan.FromHours(5));
        rig.Executors["send-budget-inquiry"].Result = null;
        var log = rig.Log.Count;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Conflict);
        retried.ReasonCode.Should().Be(DecisionCommitReasons.SignalNotOpen);
        rig.Log.Should().HaveCount(log, "nothing runs");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionSuperseded, "a supersede is never turned into Acted");
        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().BeNull("a resume that cannot proceed releases the item");
    }

    [Fact]
    public async Task ADoReviewWhoseOwnReassignLanded_DoesNotHoldTheItem_D141()
    {
        var rig = ReassignRig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = ReassignThenAddTodo();
        var failed = await rig.CommitAsync(review, DoSignal);
        failed.Resumable.Should().BeTrue();

        (await rig.Service().ReadUnfinishedAsync(Http(), DoSignal, CancellationToken.None)).Should().BeNull(
            "its author's plan read is the uniform 404 now, so the item is released to its new owner");
        rig.Executors["add-todo"].Result = null;
        (await rig.CommitAsync(review with { Retry = true }, DoSignal)).Status.Should().Be(DecisionCommitStatus.Recorded, "the open wizard still finishes it");
    }

    // ── Fix round 3: never release landed, unrecorded writes on an open item unless it is gone for good ───────────────────

    /// <summary>A Do review whose Mark complete lands (a write) and whose To Do (a test fails it) does not: stored Failed, resumable.</summary>
    private static DecisionReview MarkCompleteThenAddTodo() =>
        new()
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("mark-complete"), Skip("reschedule")],
            NextSteps = [new DecisionReviewNextStep { Code = "add-todo", Values = TodoValues }],
        };

    [Fact]
    public async Task FixRound3_AResumeWhoseMineCheckIsUnavailable_KeepsThePointer()
    {
        var rig = new Rig();
        rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
        rig.Executors["mark-complete"].Result = DecisionActionOutcome.Done("mark-complete", [new("sprk_todo", ItemTodo)]);
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = MarkCompleteThenAddTodo();
        var failed = await rig.CommitAsync(review, DoSignal);
        failed.Resumable.Should().BeTrue(failed.Detail);
        failed.Landed.Should().NotBeEmpty();
        rig.MineReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");

        var retried = await rig.CommitAsync(review with { Retry = true }, DoSignal);

        retried.Status.Should().Be(DecisionCommitStatus.Unavailable);
        (await rig.Service().ReadUnfinishedAsync(Http(), DoSignal, CancellationToken.None)).Should().NotBeNull(
            "a transient fault must not release landed, unrecorded writes");
        rig.MineReadFails = null;
        (await rig.CommitAsync(MarkComplete(), DoSignal)).ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished);
    }

    [Theory]
    [InlineData(DecisionActionReasons.NotAuthorized, DecisionCommitStatus.Forbidden)]
    [InlineData(DecisionActionReasons.InvalidState, DecisionCommitStatus.Refused)]
    public async Task FixRound3_AResumeRefusedOrForbidden_KeepsThePointer_TheItemStaysHeld(string reason, DecisionCommitStatus status)
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        await rig.CommitAsync(review);
        rig.Executors["send-budget-inquiry"].Result = null;
        rig.Executors["send-budget-inquiry"].Refusal = DecisionActionOutcome.Refused("send-budget-inquiry", reason, "no");
        var log = rig.Log.Count;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(status, retried.Detail);
        rig.Log.Skip(log).Should().NotContain(e => e.StartsWith("execute:"), "the preflight refused it; nothing runs");
        (await rig.Service().ReadUnfinishedAsync(Http(), Primary, CancellationToken.None)).Should().NotBeNull(
            "the Signal is open and the revision is unrecorded: the item stays held");
        (await rig.CommitAsync(PathBReview())).ReasonCode.Should().Be(DecisionCommitReasons.ReviewUnfinished, "no second revision");
        Count(rig, "execute:revise-budget").Should().Be(1);
    }

    [Fact]
    public async Task FixRound3_AConditionClearedClosureBeforeTheReviewStarted_IsStillSignalNotOpen()
    {
        var rig = new Rig();
        rig.Executors["send-budget-inquiry"].Result = InquiryFailed(DecisionSendFailure.NotSent);
        var review = PathBReview();
        var started = rig.Clock.GetUtcNow();
        await rig.CommitAsync(review);
        CloseByTheSystem(rig, TimeSpan.FromSeconds(33));
        rig.Signals[Primary]["sprk_resolvedon"] = started.UtcDateTime.AddMinutes(-1);   // cleared BEFORE this review started
        rig.Executors["send-budget-inquiry"].Result = null;
        var log = rig.Log.Count;

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Conflict);
        retried.ReasonCode.Should().Be(DecisionCommitReasons.SignalNotOpen);
        rig.Log.Should().HaveCount(log, "nothing runs");
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionConditionCleared);
    }

    private sealed class UnfinishedFaultLedger(ITenantCache inner) : ITenantCache
    {
        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            resource == DecisionCommitService.UnfinishedResource
                ? throw new InvalidOperationException("cache down")
                : inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default) =>
            inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);

        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
            inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
            inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);
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

    private static void CloseByTheSystem(Rig rig, TimeSpan after, int resolution = SignalWriter.ResolutionConditionCleared)
    {
        rig.Clock.Advance(after);
        rig.Signals[Primary]["sprk_signalstatus"] = new OptionSetValue(Resolved);
        rig.Signals[Primary]["sprk_resolutiontype"] = new OptionSetValue(resolution);
        rig.Signals[Primary]["sprk_resolvedon"] = rig.Clock.GetUtcNow().UtcDateTime;
        rig.Versions[Primary]++;
    }
}
