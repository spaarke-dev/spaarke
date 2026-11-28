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
/// spaarke-intake-pipeline-r1 task 017 (ontology-r1 condition (c)): an intake approval item, one whose plan offers
/// <c>approve-intake</c> or <c>reject-intake</c>, is decided on its own through the commit route. It cannot be dismissed (so it never
/// counts toward suppression, D-11), it resolves no other item, and no other decision resolves it, each refused before any executor or
/// write. A preflight that answers <see cref="DecisionActionReasons.Disabled"/> (the intake harness switched off) is 503, nothing
/// written. The must-not-fire rows keep every other item as it was: an ordinary dismissal still records, and an ordinary service
/// request item can still be resolved alongside.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private static readonly Guid IntakeVersion = Guid.Parse("c0430000-0000-4000-8000-000000000e01");
    private static readonly Guid IntakeRequest = Guid.Parse("c0430000-0000-4000-8000-000000000e02");
    private const string IntakePlan = "{\"actions\":[\"approve-intake\",\"reject-intake\"],\"nextSteps\":[]}";

    private static Rig IntakeRig(bool primaryIsIntake = true)
    {
        var rig = new Rig();
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", IntakeVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policyversion", IntakeVersion) { ["sprk_decisionplan"] = IntakePlan });
        rig.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_servicerequest", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_servicerequests");
        if (primaryIsIntake)
            rig.AddSignal(Primary, DecisionLane.Decide, Matter, IntakeVersion, subjectEntity: "sprk_servicerequest", subjectId: IntakeRequest);
        return rig;
    }

    private static DecisionReview IntakeReview(bool approve = true, IReadOnlyList<Guid>? also = null) =>
        new()
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            AlsoResolve = also,
            Actions = approve
                ? [Take("approve-intake"), Skip("reject-intake")]
                : [Skip("approve-intake"), Take("reject-intake", Values(("reason", "The output misses the indemnity clause")))],
            Because = "Reviewed the output",
        };

    private static DecisionReview IntakeDismissal() =>
        new()
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Skip("approve-intake"), Skip("reject-intake")],
            Dismissal = new DecisionReviewDismissal { Code = "not-material" },
        };

    private static void AssertNothingRanOrWasWritten(Rig rig, string because)
    {
        rig.Log.Should().NotContain(l => l.StartsWith("preflight:", StringComparison.Ordinal), because);
        rig.Writes.Should().BeEmpty(because);
        rig.Records.Should().BeEmpty(because);
        rig.SignalUpdates.Should().BeEmpty(because);
        rig.Status(Primary).Should().Be(Open);
    }

    [Fact]
    public async Task Task017_AnIntakeApproval_IsNeverDismissed_RefusedBeforeAnyWrite()
    {
        var rig = IntakeRig();

        var result = await rig.CommitAsync(IntakeDismissal());

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.DismissalNotAllowed).And.Be("ontology.decision.dismissal_not_allowed");
        AssertNothingRanOrWasWritten(rig, "an intake approval is approved or rejected, never dismissed");
        var problem = DecisionResultMapper.ToHttp(result, Http()).Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(DecisionCommitReasons.DismissalNotAllowed);
    }

    [Fact]
    public async Task Task017_MustNotFire_AnOrdinaryDismissal_StillRecords()
    {
        var rig = IntakeRig(primaryIsIntake: false);

        var result = await rig.CommitAsync(Dismissal("not-material"));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionDismissed);
    }

    [Fact]
    public async Task Task017_AnIntakeApproval_ResolvesNoOtherItem()
    {
        var rig = IntakeRig();
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);

        var result = await rig.CommitAsync(IntakeReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        AssertNothingRanOrWasWritten(rig, "an intake approval is decided on its own");
        rig.Status(Second).Should().Be(Open);
    }

    [Fact]
    public async Task Task017_NoOtherDecision_ResolvesAnIntakeApproval()
    {
        var rig = IntakeRig(primaryIsIntake: false);
        rig.AddSignal(Second, DecisionLane.Decide, Matter, IntakeVersion, subjectEntity: "sprk_servicerequest", subjectId: IntakeRequest);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.AlsoResolveNotAllowed);
        AssertNothingRanOrWasWritten(rig, "an intake approval in Also resolve refuses the whole review");
        rig.Status(Second).Should().Be(Open);
    }

    [Fact]
    public async Task Task017_AnAlsoResolveServiceRequestItemWhosePlanCannotBeRead_IsUnavailable_NeverTakenAsNotIntake()
    {
        var rig = IntakeRig(primaryIsIntake: false);
        rig.AddSignal(Second, DecisionLane.Decide, Matter, IntakeVersion, subjectEntity: "sprk_servicerequest", subjectId: IntakeRequest);
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", IntakeVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        result.ReasonCode.Should().Be(DecisionCommitReasons.SignalUnreadable);
        AssertNothingRanOrWasWritten(rig, "an unreadable plan fails closed");
    }

    [Fact]
    public async Task Task017_MustNotFire_AnOrdinaryServiceRequestItem_IsStillResolvedAlongside()
    {
        // An outbound inquiry's item is also about a service request; its plan is not an intake approval, so nothing changes for it.
        var rig = IntakeRig(primaryIsIntake: false);
        rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_servicerequest", subjectId: IntakeRequest);

        var result = await rig.CommitAsync(PathBReview(also: [Second]));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Status(Second).Should().Be(Resolved);
    }

    [Fact]
    public async Task Task017_ApprovingAnIntake_RunsTheApproveExecutor_AndWritesOneJudgementRecord()
    {
        var rig = IntakeRig();
        var review = IntakeReview();

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Log.Should().Contain("execute:approve-intake").And.NotContain("execute:reject-intake");
        rig.Record.GetAttributeValue<OptionSetValue>("sprk_recordclass").Value.Should().Be((int)RecordedDecisionClass.Judgement);
        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().NotBe(SignalWriter.ResolutionDismissed);
        // The record's requester is the deciding caller (stamped from the ownership context, sprk_createdbyperson).
        rig.OwnerAsks.Should().NotBeEmpty();
        rig.OwnerAsks[0].RequestedBy!.SystemUserId.Should().Be(Caller);

        // The same review again writes no second record and runs no executor again.
        var executed = rig.Log.Count(l => l == "execute:approve-intake");
        var again = await rig.CommitAsync(review);
        again.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Records.Should().ContainSingle();
        rig.Log.Count(l => l == "execute:approve-intake").Should().Be(executed);
    }

    [Fact]
    public async Task Task017_RejectingAnIntake_RunsTheRejectExecutor_WritesOneRecord_AndResolvesTheItem()
    {
        var rig = IntakeRig();

        var result = await rig.CommitAsync(IntakeReview(approve: false));

        result.Status.Should().Be(DecisionCommitStatus.Recorded);
        rig.Log.Should().Contain("execute:reject-intake").And.NotContain("execute:approve-intake");
        rig.Records.Should().ContainSingle();
        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().NotBe(SignalWriter.ResolutionDismissed);
        // ADR-015 (POML constraint): the reject reason is STORED on the Decision Record, as the step's value, and appears in no text
        // column (name, summary, the narrative) of it.
        rig.Record.Attributes.Where(a => a.Value is string s && s.Contains("indemnity", StringComparison.Ordinal)).Select(a => a.Key)
            .Should().Equal("sprk_steps");
        Steps(rig.Record).GetProperty("steps")[1].GetProperty("values").GetProperty("reason").GetString()
            .Should().Be("The output misses the indemnity clause");
    }

    [Fact]
    public async Task Task017_ApproveAndRejectTogether_AreRefused_AsMutuallyExclusive()
    {
        var rig = IntakeRig();
        var review = IntakeReview() with
        {
            Actions = [Take("approve-intake"), Take("reject-intake", Values(("reason", "no")))],
        };

        var result = await rig.CommitAsync(review);

        result.Status.Should().Be(DecisionCommitStatus.Refused);
        result.ReasonCode.Should().Be(DecisionCommitReasons.ActionsExclude);
        AssertNothingRanOrWasWritten(rig, "exactly one of approve and reject is taken");
    }

    [Fact]
    public async Task Task017_APreflightThatAnswersDisabled_Is503_AndNothingIsWritten()
    {
        var rig = IntakeRig();
        rig.Executors["approve-intake"].Refusal =
            DecisionActionOutcome.Refused("approve-intake", DecisionActionReasons.Disabled, "Intake approval is switched off.");

        var result = await rig.CommitAsync(IntakeReview());

        result.Status.Should().Be(DecisionCommitStatus.Unavailable);
        result.ReasonCode.Should().Be(DecisionActionReasons.Disabled);
        rig.Writes.Should().BeEmpty();
        rig.Records.Should().BeEmpty();
        rig.Status(Primary).Should().Be(Open);
        var problem = DecisionResultMapper.ToHttp(result, Http()).Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Task017_MustNotFire_AnotherPreflightRefusal_IsStillARefusal()
    {
        var rig = IntakeRig();
        rig.Executors["approve-intake"].Refusal =
            DecisionActionOutcome.Refused("approve-intake", DecisionActionReasons.InvalidState, "Not awaiting approval.");

        var result = await rig.CommitAsync(IntakeReview());

        result.Status.Should().Be(DecisionCommitStatus.Refused);
    }
}
