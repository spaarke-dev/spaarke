using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 174 (D-207; ebilling EB-D-194): invoice and invoice-line decisions are made only in Invoice Review, so the commit route refuses
/// a Signal whose subject is <c>sprk_invoice</c> or <c>sprk_billingevent</c> with 409 <c>ontology.decision.decided_in_invoice_review</c>. The
/// refusal comes AFTER the caller's readability check (an item the caller cannot read is still the uniform 404) and the app-only read
/// of the Signals, and BEFORE the caller-rights probe, any executor and every write.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly Guid BillingSubjectId = Guid.Parse("c0430000-0000-4000-8000-0000000000b1");

    private static void AssertNoRightsAskedAndNothingWritten(Rig rig, string because)
    {
        rig.RightsAsked.Should().BeEmpty($"the caller-rights probe is never asked: {because}");
        rig.Probe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Org.Verify(o => o.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Org.Verify(o => o.ExecuteAsync(It.IsAny<Microsoft.Xrm.Sdk.Messages.UpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        rig.Writes.Should().BeEmpty(because);
        rig.Records.Should().BeEmpty(because);
        rig.SignalUpdates.Should().BeEmpty(because);
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal), because);
        rig.Status(Primary).Should().Be(Open);
    }

    [Theory]
    [InlineData("sprk_billingevent")]
    [InlineData("sprk_invoice")]
    public async Task Task174_ASignalWhoseSubjectIsAnInvoiceOrALine_Is409DecidedInInvoiceReview_BeforeTheRightsProbeAndAnyWrite(string subject)
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: subject, subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be("ontology.decision.decided_in_invoice_review");
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "an invoice or line is decided in Invoice Review");

        // The route's own mapping: 409 ProblemDetails with the reason code, never today's 403 SubjectWriteMissing.
        var reply = DecisionCommitEndpoints.ToHttp(result, Http());
        var problem = reply.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("ontology.decision.decided_in_invoice_review");
    }

    [Fact]
    public async Task Task174_ADismissalOfABillingSignal_IsRefusedToo_NothingIsWritten()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "a dismissal is a decision");
    }

    [Fact]
    public async Task Task174_ACallerWhoCannotReadABillingSignal_GetsTheUniform404_NotTheBillingRefusal()
    {
        // Nothing about an item shows to a caller who cannot read it, so the readability check (1a) answers before the subject is read.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);
        rig.CallerCannotRead.Add(Primary);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        result.ReasonCode.Should().BeNull();
        result.Detail.Should().BeNull();
        AssertNoRightsAskedAndNothingWritten(rig, "an unreadable item is the uniform 404");
    }

    [Fact]
    public async Task Task174_AnAlsoResolveBillingSignal_RefusesTheWholeReview_AndNothingIsWritten()
    {
        // The primary is an ordinary matter Signal the caller may decide; the second is a billing Signal. The review is one decision.
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Conflict);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        AssertNoRightsAskedAndNothingWritten(rig, "one billing Signal in the set refuses the whole review");
    }

    [Fact]
    public async Task Task174_AnAlsoResolveBillingSignalTheCallerCannotRead_IsTheUniform404_NotTheBillingRefusal()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_invoice", subjectId: BillingSubjectId);
        rig.CallerCannotRead.Add(Second);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.NotFound);
        result.ReasonCode.Should().BeNull();
        AssertNoRightsAskedAndNothingWritten(rig, "an unreadable Also resolve item is the uniform 404");
    }

    [Fact]
    public async Task Task174_AMatterSubjectSignal_IsUnchanged_AndStillAsksTheCallersRights()
    {
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_matter", subjectId: Matter);

        var result = await rig.CommitAsync(PathBReview(inquiry: false, todo: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.RightsAsked.Should().NotBeEmpty("the caller-rights probe still runs for every other subject");
    }

    // ── Replay: "already recorded" is decided only from what the server stored ─────────────────────────────────────────

    /// <summary>The Signal now has an invoice-line subject, as a Signal recorded BEFORE D-207 does (the decision predates the rule).</summary>
    private static void BecomesBilling(Rig rig, Guid signal)
    {
        rig.Signals[signal]["sprk_regardinginvoiceline"] = new EntityReference("sprk_billingevent", BillingSubjectId);
        rig.Signals[signal]["sprk_regardingrecordid"] = BillingSubjectId.ToString("D");
        // An authorized decider (the caller-rights probe resolves the line's table), as a pre-D-207 decision on a line needed.
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_billingevent", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_billingevents");
    }

    private static void BecomesOrdinary(Rig rig, Guid signal)
    {
        rig.Signals[signal].Attributes.Remove("sprk_regardinginvoiceline");
        rig.Signals[signal].Attributes.Remove("sprk_regardingrecordid");
    }

    [Fact]
    public async Task Task174_K1_ARealLedgerReplay_OfAReviewRecordedBeforeD207_StillFinishesItsCloses_ForABillingSignal()
    {
        // The first run recorded the decision but its close failed; the Signal is open, and (as a pre-D-207 decision) it is a billing Signal.
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(Primary).Should().Be(Open);
        BecomesBilling(rig, Primary);

        var replay = await rig.CommitAsync(review);

        replay.Status.Should().Be(DecisionCommitStatus.Recorded, replay.Detail);
        replay.Replayed.Should().BeTrue();
        replay.ReasonCode.Should().NotBe(DecisionCommitReasons.DecidedInInvoiceReview);
        rig.Status(Primary).Should().Be(Resolved, "a recorded decision is finished, not abandoned open");
        rig.LinkedRecord(Primary).Should().Be(RecordId);
        rig.Records.Should().ContainSingle("the replay writes no second record");
    }

    [Fact]
    public async Task Task174_K1_AFinishingReplay_WhenTheLedgerForgot_ButTheRecordStays_StillClosesABillingSignal()
    {
        var rig = new Rig();
        FirstCloseFails(rig);
        var review = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(review)).Status.Should().Be(DecisionCommitStatus.Recorded);
        BecomesBilling(rig, Primary);
        await LedgerForgetsButTheRecordStays(rig, review);

        var finished = await rig.CommitAsync(review);

        finished.Status.Should().Be(DecisionCommitStatus.Recorded, finished.Detail);
        finished.Replayed.Should().BeTrue();
        rig.Status(Primary).Should().Be(Resolved);
        rig.LinkedRecord(Primary).Should().Be(RecordId, "closed with the record the server found by its review id");
    }

    [Fact]
    public async Task Task174_K1_ARequestShapedLikeAReplay_WithNoRecordedReview_IsStillRefused_AndNothingIsWritten()
    {
        // Nothing in the body says "replay": a Retry flag and a review id are only a request. With no ledger entry and no stored record,
        // it is a new decision on a billing Signal.
        var rig = new Rig();
        rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);
        var review = PathBReview(inquiry: false, todo: false);

        var forged = await rig.CommitAsync(review with { Retry = true });

        forged.Status.Should().Be(DecisionCommitStatus.Conflict);
        forged.ReasonCode.Should().Be("ontology.decision.decided_in_invoice_review");
        forged.Replayed.Should().BeFalse();
        AssertNoRightsAskedAndNothingWritten(rig, "a replay-shaped request with nothing recorded is a new decision");
    }

    [Fact]
    public async Task Task174_K1_ARecordForAnotherReview_DoesNotMakeThisOneARecordedReplay()
    {
        // A record exists for review A on this Signal. Review B (another id) on the now-billing Signal is a new decision.
        var rig = new Rig();
        FirstCloseFails(rig);
        var reviewA = PathBReview(inquiry: false, todo: false);
        (await rig.CommitAsync(reviewA)).Status.Should().Be(DecisionCommitStatus.Recorded);
        BecomesBilling(rig, Primary);
        await LedgerForgetsButTheRecordStays(rig, reviewA);
        var records = rig.Records.Count;
        var closes = rig.SignalUpdates.Count;

        var other = await rig.CommitAsync(PathBReview(inquiry: false, todo: false) with { Retry = true });

        other.Status.Should().Be(DecisionCommitStatus.Conflict);
        other.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        rig.Records.Should().HaveCount(records);
        rig.SignalUpdates.Should().HaveCount(closes);
        rig.Status(Primary).Should().Be(Open);
    }

    // ── The refusal frees the review id; Retry restores the failure it resumes ────────────────────────────────────────

    [Fact]
    public async Task Task174_K4a_A409ForABillingAlsoResolve_ThenTheSameReviewIdWithoutIt_IsRecorded()
    {
        var rig = new Rig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);
        var reviewId = Guid.NewGuid();

        var refused = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, reviewId: reviewId, also: [Second]));
        refused.Status.Should().Be(DecisionCommitStatus.Conflict);
        refused.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);

        var resent = await rig.CommitAsync(PathBReview(inquiry: false, todo: false, reviewId: reviewId));

        resent.Status.Should().Be(DecisionCommitStatus.Recorded, resent.Detail);
        resent.ReasonCode.Should().NotBe(DecisionCommitReasons.ReviewChanged).And.NotBe(DecisionCommitReasons.ReviewInProgress);
        rig.Status(Primary).Should().Be(Resolved);
        rig.Status(Second).Should().Be(Open, "the billing Signal was never part of the recorded decision");
    }

    [Fact]
    public async Task Task174_K4b_ABillingSignalUnderRetry_Is409_AndTheResumedFailureIsRestoredForTheNextRetry()
    {
        var rig = new Rig();
        rig.Executors["add-todo"].Result = DecisionActionOutcome.Refused("add-todo", DecisionActionReasons.WriteFailed, "no");
        var review = PathBReview(inquiry: false);
        (await rig.CommitAsync(review)).Resumable.Should().BeTrue();
        BecomesBilling(rig, Primary);
        var rightsAsked = rig.RightsAsked.Count;
        var executes = rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal));

        var retried = await rig.CommitAsync(review with { Retry = true });

        retried.Status.Should().Be(DecisionCommitStatus.Conflict);
        retried.ReasonCode.Should().Be(DecisionCommitReasons.DecidedInInvoiceReview);
        rig.RightsAsked.Should().HaveCount(rightsAsked, "the rights probe is not asked again");
        rig.Log.Count(l => l.StartsWith("execute:", StringComparison.Ordinal)).Should().Be(executes, "nothing runs");

        // The stored failure is back (ReleaseLedgerAsync): a plain repeat still returns it, and once the Signal is ordinary a retry resumes it.
        var repeat = await rig.CommitAsync(review);
        repeat.Status.Should().Be(DecisionCommitStatus.Failed);
        repeat.Replayed.Should().BeTrue();

        BecomesOrdinary(rig, Primary);
        rig.Executors["add-todo"].Result = null;
        var resumed = await rig.CommitAsync(review with { Retry = true });
        resumed.Status.Should().Be(DecisionCommitStatus.Recorded, resumed.Detail);
        rig.Log.Count(l => l == "execute:revise-budget").Should().Be(1, "the step that landed on the first attempt is never run again");
    }
}
