using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using System.Text.Json;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 034: three counted dismissals per (policy, core record) suppress that pairing for 30 days (spec FR-17, success criterion 4;
/// D-11 as amended by D-34/D-36/D-39; R-8/R-10; D-13).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The real <see cref="SignalSuppressionService"/> and
/// <see cref="SignalEpisodePlanner"/> run over an in-memory store that evaluates the very <see cref="QueryExpression"/>s the service
/// sends (equality, begins-with, in, greater-or-equal, nested filters), so a column the service filters on but the store lacks fails
/// the test rather than matching everything. Column and option names are the live <c>sprk_signal</c> / <c>sprk_decisionrecord</c>
/// schema already pinned by <c>SignalWriter</c> / <c>DecisionRecordWriter</c>; the real-schema pairing for these columns is the
/// existing <c>SignalWriterSeamTests</c> and the 031/040 live proofs (this task adds no column).
/// </remarks>
public class SignalSuppressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PolicyId = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid OtherPolicyId = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fd");
    private static readonly Guid MatterType = Guid.Parse("a4a606e6-cf62-f111-ab0c-70a8a58ae145");
    private static readonly Guid ProjectType = Guid.Parse("a5a606e6-cf62-f111-ab0c-70a8a58ae145");
    private static readonly Guid WorkAssignmentType = Guid.Parse("a6a606e6-cf62-f111-ab0c-70a8a58ae145");
    private static readonly Guid ServiceRequestType = Guid.Parse("a7a606e6-cf62-f111-ab0c-70a8a58ae145");
    private static readonly Guid FifthCoreType = Guid.Parse("b8a606e6-cf62-f111-ab0c-70a8a58ae145");
    private static readonly Guid MatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid OtherMatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc989");
    private static readonly Guid ProjectId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000b1");
    private static readonly Guid WorkAssignmentId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000b2");
    private static readonly Guid ServiceRequestId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000b3");
    private static readonly Guid FifthCoreId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000b4");
    private const string Code = "POL-COMMIT-BUDGET";
    private const string Counts = "not-material - the fee note was already agreed";
    private const string WrongMatter = "wrong-matter-misresolved - this belongs to another matter";

    // =====================================================================================
    // The counting rule
    // =====================================================================================

    [Fact]
    public async Task ThreeDismissals_OfThreeDifferentSubjects_OnOneMatter_SuppressThePairing_ForThirtyDaysFromTheThird()
    {
        var s = new Store();
        s.Dismiss(MatterId, subject: Guid.NewGuid(), daysAgo: 6);
        s.Dismiss(MatterId, subject: Guid.NewGuid(), daysAgo: 3);
        s.Dismiss(MatterId, subject: Guid.NewGuid(), daysAgo: 1);
        var open = s.Open(MatterId, Guid.NewGuid());

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(3);
        var expected = Now.UtcDateTime.AddDays(-1).AddDays(30);
        status.SuppressedUntil.Should().Be(expected);
        s.Signals.Where(x => x.Id != open).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == expected);
        s.StampedIds.Should().HaveCount(4, "the three dismissed Signals and the open one of the pairing are denormalized");
        s.StampedIds.Should().Contain(open, "an open Signal of a suppressed pairing must not stay on the worklist");
    }

    [Fact]
    public async Task ThenTheNextEvaluation_RaisesNothingForThePairing_AndASecondApplyWritesNothing()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        s.StampedIds.Clear();

        var second = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().BeEmpty("D-61: nothing is stamped twice");
        second.SuppressedUntil.Should().NotBeNull();

        // Task 031's planner (the re-raise rule) reads the stamp: the same condition holds on a NEW subject of the same matter.
        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], s.Existing());
        plan.Creates.Should().BeEmpty("no new Signal for a suppressed pairing surfaces");
        plan.Suppressed.Should().Be(1);
    }

    [Fact]
    public async Task TwoDismissals_DoNotSuppress_AndASignalStillSurfaces()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(2);
        status.SuppressedUntil.Should().BeNull();
        s.StampedIds.Should().BeEmpty();
        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], s.Existing());
        plan.Creates.Should().ContainSingle("two dismissals leave the pairing live");
    }

    [Fact]
    public async Task Dismissals_OlderThanTheWindow_AreNotCounted()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 45);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 35);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(1);
        s.StampedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoClosures_NeverCount()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1);
        s.Closed(MatterId, SignalWriter.ResolutionConditionCleared, daysAgo: 1);
        s.Closed(MatterId, SignalWriter.ResolutionSuperseded, daysAgo: 1);
        s.Closed(MatterId, SignalWriter.ResolutionPolicyRetired, daysAgo: 1);
        s.Closed(MatterId, SignalWriter.ResolutionActed, daysAgo: 1);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(2, "three system closures and an Acted resolution are not human dismissals");
        status.DismissalsRecorded.Should().Be(2);
        s.StampedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ADismissalRecordOfAnotherClass_IsNotADismissal()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, recordClass: RecordedDecisionClass.Judgement);

        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default)).DismissalsCounted.Should().Be(2);
    }

    [Fact]
    public async Task OneReview_ThatDismissesSeveralSignals_IsOneDismissal()
    {
        var s = new Store();
        var record = s.Record(RecordedDecisionClass.Dismissal, Counts, daysAgo: 2);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, record: record);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, record: record);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, record: record);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(1, "one human decision, however many Signals it also resolved");
        s.StampedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task APolicy_WithCountsTowardSuppressionNo_RecordsButNeverSuppresses()
    {
        var s = new Store { PolicyCounts = false };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.PolicyCountsTowardSuppression.Should().BeFalse();
        status.DismissalsCounted.Should().Be(0);
        status.DismissalsRecorded.Should().Be(3, "the dismissals are still recorded, queryable and clusterable");
        status.SuppressedUntil.Should().BeNull();
        s.StampedIds.Should().BeEmpty();
        s.Records.Should().HaveCount(3, "the Decision Records themselves are untouched");
    }

    [Fact]
    public async Task WrongMatterMisresolved_IsRecordedButNeverCounted_R10()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, reason: WrongMatter);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(0);
        status.DismissalsRecorded.Should().Be(3);
        status.SuppressedUntil.Should().BeNull();
        s.StampedIds.Should().BeEmpty();
        SignalSuppressionService.IsNonCountingReason(WrongMatter).Should().BeTrue();
    }

    [Fact]
    public async Task ANonCountingReason_DoesNotHelpTwoCountingOnesReachTheThreshold()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, reason: WrongMatter);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        (status.DismissalsCounted, status.DismissalsRecorded).Should().Be((2, 3));
        status.SuppressedUntil.Should().BeNull();
    }

    [Theory]
    [InlineData("not-material - detail", "not-material", false)]
    [InlineData("other - a - b", "other", false)]
    [InlineData("wrong-matter-misresolved", "wrong-matter-misresolved", true)]
    [InlineData("wrong-matter-misresolved - x", "wrong-matter-misresolved", true)]
    [InlineData("  wrong-matter-misresolved  - x", "wrong-matter-misresolved", true)]
    [InlineData("a code nobody declared - x", "a code nobody declared", false)]
    [InlineData(null, "", false)]
    public void TheReasonCode_IsTheTextBeforeTheSeparator_AndOnlyDeclaredCodesAreNonCounting(string? reason, string code, bool nonCounting)
    {
        SignalSuppressionService.ReasonCode(reason).Should().Be(code);
        SignalSuppressionService.IsNonCountingReason(reason).Should().Be(nonCounting);
    }

    // =====================================================================================
    // Expiry (30 days) and the new episode
    // =====================================================================================

    [Fact]
    public async Task ASuppression_SetThirtyOneDaysAgo_HasLapsed_AndTheConditionRaisesANewEpisode()
    {
        var s = new Store();
        // Three dismissals 34, 33 and 32 days ago stamped 'until' 2 days ago (the third + 30 days): lapsed, and the dismissals aged out.
        var ids = new[] { 34, 33, 32 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, episode: 1)).ToList();
        foreach (var id in ids) s.Stamp(id, Now.UtcDateTime.AddDays(-32).AddDays(30));

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.SuppressedUntil.Should().BeNull();
        status.DismissalsCounted.Should().Be(0, "the dismissals that made the stamp are older than the window");
        s.StampedIds.Should().BeEmpty("a lapse is never re-stamped");

        // The latest episode of a subject was dismissed 32 days ago; the predicate still holds: a NEW episode, not a reopen.
        var subject = s.SubjectOf(ids[2]);
        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, subject)], s.Existing());
        plan.Suppressed.Should().Be(0);
        plan.Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Fact]
    public async Task ASuppression_StillLive_StopsEvenAFirstEpisodeOnTheMatter()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now.AddDays(10), [Hit(MatterId, Guid.NewGuid())], s.Existing());

        plan.Creates.Should().BeEmpty();
        var after = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now.AddDays(40), [Hit(MatterId, Guid.NewGuid())], s.Existing());
        after.Creates.Should().ContainSingle("the 30-day expiry is reachable: a permanent mute would be indistinguishable from a broken rule");
    }

    // =====================================================================================
    // Grain (D-34, D-36, D-39)
    // =====================================================================================

    [Fact]
    public async Task TheGrain_IsPerPolicy_NotAcrossPolicies()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, policy: OtherPolicyId);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1);

        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default)).DismissalsCounted.Should().Be(1);
    }

    [Fact]
    public async Task TheGrain_IsPerCoreRecord_AnotherMatterIsUnaffected()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        s.Dismiss(OtherMatterId, Guid.NewGuid(), daysAgo: 1);
        var otherOpen = s.Open(OtherMatterId, Guid.NewGuid());

        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().NotContain(otherOpen);
        (await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForCore(MatterType, OtherMatterId), default))
            .SuppressedUntil.Should().BeNull();
    }

    [Fact]
    public async Task APair_OfDismissals_OnOneProject_NoMatter_Suppresses_D34()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(ProjectId, Guid.NewGuid(), daysAgo: i + 1, coreType: ProjectType);

        var status = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForCore(ProjectType, ProjectId), default);

        status.SuppressedUntil.Should().NotBeNull();
        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(ProjectId, Guid.NewGuid(), ProjectType)], s.Existing());
        plan.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task EachCoreRecord_OfEveryCoreType_IsSuppressedIndependently_D36_AndTheServiceBranchesOnNoType()
    {
        var s = new Store();
        (Guid Type, Guid Id)[] cores =
        [
            (WorkAssignmentType, WorkAssignmentId), (ServiceRequestType, ServiceRequestId), (MatterType, MatterId), (FifthCoreType, FifthCoreId),
        ];
        // A fifth core type exists only as a catalog row (a type ref id) and an id: nothing in the service names it.
        for (var n = 0; n < 3; n++)
        {
            s.Dismiss(WorkAssignmentId, Guid.NewGuid(), daysAgo: n + 1, coreType: WorkAssignmentType);
            s.Dismiss(ServiceRequestId, Guid.NewGuid(), daysAgo: n + 1, coreType: ServiceRequestType);
            s.Dismiss(FifthCoreId, Guid.NewGuid(), daysAgo: n + 1, coreType: FifthCoreType);
        }

        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1); // the matter has only one

        var results = new List<DateTime?>();
        foreach (var (type, id) in cores)
        {
            results.Add((await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForCore(type, id), default)).SuppressedUntil);
        }

        results.Select(r => r is not null).Should().Equal(true, true, false, true);
    }

    [Fact]
    public async Task ANoCoreItem_IsCountedPerItem_D39()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        var otherItem = Guid.NewGuid();
        // Three episodes of ONE To Do dismissed (an item's own dismissals), and a different To Do dismissed once.
        for (var i = 0; i < 3; i++) s.Dismiss(core: null, subject: item, daysAgo: i + 1, episode: i + 1, subjectEntity: "sprk_todo");
        s.Dismiss(core: null, subject: otherItem, daysAgo: 1, subjectEntity: "sprk_todo");

        var status = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item), default);
        var other = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", otherItem), default);

        status.SuppressedUntil.Should().NotBeNull("three dismissals of one item for one policy suppress it for 30 days");
        other.SuppressedUntil.Should().BeNull();
        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, Now, [NoCoreHit(item)], s.Existing());
        plan.Creates.Should().BeEmpty();
    }

    // =====================================================================================
    // Nightly backstop (task 031's job calls ReconcileAsync) and D-61
    // =====================================================================================

    [Fact]
    public async Task Reconcile_StampsAGrainFoundAtTheThreshold_AndHandsThePlannerTheStamp()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().HaveCount(3);
        signals.Should().OnlyContain(x => x.SuppressedUntil != null);
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], signals).Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task Reconcile_ReadsNothing_WhenNoGrainIsAtTheThreshold_OrAlreadyStamped()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);
        var quiet = await s.Service.ReconcileAsync(PolicyId, Code, s.Existing(), default);
        s.Queries.Should().Be(0, "two dismissals cannot reach the threshold: no query");

        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3);
        await s.Service.ReconcileAsync(PolicyId, Code, s.Existing(), default);
        var queriesAfterStamp = s.Queries;
        s.StampedIds.Clear();
        await s.Service.ReconcileAsync(PolicyId, Code, s.Existing(), default);

        quiet.Failures.Should().Be(0);
        s.Queries.Should().Be(queriesAfterStamp, "a live stamp ends the nightly work for the grain");
        s.StampedIds.Should().BeEmpty("D-61: unchanged Signals are never stamped nightly");
    }

    [Fact]
    public async Task Reconcile_CountsAFailureAndStillSuppressesThisPass_WhenTheStampWriteFails()
    {
        var s = new Store { FailWrites = true };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, s.Existing(), default);

        failures.Should().Be(1, "the run must not look healthy when a stamp was not written");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], signals).Creates
            .Should().BeEmpty("a failed write must not let a pairing at the threshold resurface this pass");
    }

    // =====================================================================================
    // The count read (wizard dismissal step, admin)
    // =====================================================================================

    [Fact]
    public async Task TheRoute_Returns200WithTheCountAndTheUntilDate_ForACallerWhoCanReadTheMatter()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        var signal = s.Open(MatterId, Guid.NewGuid());
        var access = Access(signalRow: Ok(SignalRow()), core: Ok(new { createdon = "x" }));

        var result = await DecisionPlanEndpoints.GetSuppressionAsync(
            signal, new DefaultHttpContext(), access, s.Service, NullLogger<SignalSuppressionService>.Instance, default);

        var body = result.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.DismissalsRecorded, body.Threshold, body.WindowDays).Should().Be((3, 3, 3, 30));
        body.SuppressedUntil.Should().Be(Now.UtcDateTime.AddDays(-1).AddDays(30));
        body.CountsTowardSuppression.Should().BeTrue();
    }

    [Fact]
    public async Task TheRoute_Returns404_WhenTheCallerCannotReadTheMatter_AndReadsNoCount()
    {
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());
        var access = Access(signalRow: Ok(SignalRow()), core: Fail(404, DataverseUserClientErrorCodes.NotFound));
        var before = s.Queries;

        var result = await DecisionPlanEndpoints.GetSuppressionAsync(
            signal, new DefaultHttpContext(), access, s.Service, NullLogger<SignalSuppressionService>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
        s.Queries.Should().Be(before);
    }

    [Fact]
    public async Task TheRoute_Returns403_ForAnUnresolvedCaller()
    {
        var s = new Store();
        var access = Access(signalRow: Fail(0, DataverseUserClientErrorCodes.UserContextRequired), core: Ok(new { createdon = "x" }));

        var result = await DecisionPlanEndpoints.GetSuppressionAsync(
            Guid.NewGuid(), new DefaultHttpContext(), access, s.Service, NullLogger<SignalSuppressionService>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TheRoute_Returns404_WhenTheCountCannotBeRead_NeverAGuess()
    {
        var s = new Store { FailReads = true };
        var signal = s.Open(MatterId, Guid.NewGuid());
        var access = Access(signalRow: Ok(SignalRow()), core: Ok(new { createdon = "x" }));

        var result = await DecisionPlanEndpoints.GetSuppressionAsync(
            signal, new DefaultHttpContext(), access, s.Service, NullLogger<SignalSuppressionService>.Instance, default);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
    }

    // =====================================================================================
    // Fixtures
    // =====================================================================================

    private static SuppressionGrain Matter() => SuppressionGrain.ForCore(MatterType, MatterId);

    private static SignalHit Hit(Guid coreId, Guid subjectId, Guid? type = null) =>
        new("sprk_communication", subjectId, "A communication", null, new SignalCoreRecord("sprk_matter", coreId, type ?? MatterType, null),
            null, null, new Dictionary<string, object?>(), []);

    private static SignalHit NoCoreHit(Guid itemId) =>
        new("sprk_todo", itemId, "A To Do", null, new SignalCoreRecord(null, Guid.Empty, null, new EntityReference("systemuser", Guid.NewGuid())),
            null, null, new Dictionary<string, object?>(), []);

    private static SignalCoreRecordAccess Access(DataverseUserResponse signalRow, DataverseUserResponse core)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
        entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(signalRow);
        users.Setup(u => u.GetAsync(It.Is<string>(p => !p.StartsWith("sprk_signals(", StringComparison.Ordinal) && p != "WhoAmI"), It.IsAny<CancellationToken>())).ReturnsAsync(core);
        users.Setup(u => u.GetAsync("WhoAmI", It.IsAny<CancellationToken>())).ReturnsAsync(Fail(0, DataverseUserClientErrorCodes.UserContextRequired));
        return new SignalCoreRecordAccess(users.Object, entities.Object, NullLogger<SignalCoreRecordAccess>.Instance);
    }

    private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    private static DataverseUserResponse Fail(int status, string code) => DataverseUserResponse.Fail(status, code, "test");

    private static object SignalRow() => new Dictionary<string, object?>
    {
        ["sprk_lane"] = (int)DecisionLane.Decide,
        ["_sprk_policyversion_value"] = Guid.NewGuid(),
        ["_ownerid_value"] = Guid.NewGuid(),
        ["_sprk_corerecordtype_value"] = MatterType,
        ["sprk_corerecordid"] = MatterId.ToString(),
    };

    /// <summary>
    /// The in-memory Dataverse: Signals and Decision Records the service queries (evaluated by condition, so a wrong column or filter
    /// changes the answer), and a writer organization service that records every stamp.
    /// </summary>
    private sealed class Store
    {
        public List<Entity> Signals { get; } = new();
        public List<Entity> Records { get; } = new();
        public List<Guid> StampedIds { get; } = new();
        public bool PolicyCounts { get; set; } = true;
        public bool FailWrites { get; set; }
        public bool FailReads { get; set; }
        public int Queries { get; private set; }
        public SignalSuppressionService Service { get; }

        public Store()
        {
            var clock = new FakeTimeProvider(Now);
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_policy", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                    new Entity("sprk_policy", id) { ["sprk_countstowardsuppression"] = PolicyCounts, ["sprk_policycode"] = Code });
            entities.Setup(e => e.RetrieveAsync("sprk_signal", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                    FailReads ? throw new InvalidOperationException("dataverse is down") : Signals.Single(x => x.Id == id));
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => Run(q));

            var org = new Mock<IOrganizationServiceAsync2>();
            org.Setup(o => o.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns((Entity update, CancellationToken _) =>
                {
                    if (FailWrites) throw new InvalidOperationException("Simulated Dataverse failure on the update.");
                    update.Attributes.Keys.Should().Equal(["sprk_suppresseduntil"], "the stamp touches that one column and nothing else");
                    StampedIds.Add(update.Id);
                    Signals.Single(x => x.Id == update.Id)["sprk_suppresseduntil"] = update["sprk_suppresseduntil"];
                    return Task.CompletedTask;
                });
            var writer = new OntologyWriterDataverseClient(() => org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
            Service = new SignalSuppressionService(entities.Object, writer, clock, NullLogger<SignalSuppressionService>.Instance);
        }

        public Guid Record(RecordedDecisionClass cls, string? reason, int daysAgo)
        {
            var id = Guid.NewGuid();
            Records.Add(new Entity("sprk_decisionrecord", id)
            {
                ["sprk_recordclass"] = new OptionSetValue((int)cls),
                ["sprk_reason"] = reason,
                ["sprk_decidedon"] = Now.UtcDateTime.AddDays(-daysAgo),
            });
            return id;
        }

        public Guid Dismiss(
            Guid? core, Guid subject, int daysAgo, string reason = Counts, RecordedDecisionClass recordClass = RecordedDecisionClass.Dismissal,
            Guid? record = null, Guid? policy = null, Guid? coreType = null, int episode = 1, string subjectEntity = "sprk_communication") =>
            Add(core, subject, SignalWriter.ResolutionDismissed, daysAgo, record ?? Record(recordClass, reason, daysAgo), policy, coreType, episode, subjectEntity);

        public Guid Closed(Guid core, int resolution, int daysAgo) =>
            Add(core, Guid.NewGuid(), resolution, daysAgo, null, null, null, 1, "sprk_communication");

        public Guid Open(Guid core, Guid subject) =>
            Add(core, subject, null, 0, null, null, null, 1, "sprk_communication");

        public void Stamp(Guid signalId, DateTime until) => Signals.Single(x => x.Id == signalId)["sprk_suppresseduntil"] = until;

        public Guid SubjectOf(Guid signalId)
        {
            SignalWriter.TryParseDedupeKey(Signals.Single(x => x.Id == signalId).GetAttributeValue<string>("sprk_dedupekey"), out _, out _, out var id, out _);
            return id;
        }

        private Guid Add(Guid? core, Guid subject, int? resolution, int daysAgo, Guid? record, Guid? policy, Guid? coreType, int episode, string subjectEntity)
        {
            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id)
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policy ?? PolicyId),
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(Code, subjectEntity, subject.ToString("D").ToLowerInvariant(), episode),
                ["sprk_signalstatus"] = new OptionSetValue(resolution is null ? SignalWriter.SignalStatusOpen : SignalWriter.SignalStatusResolved),
            };
            if (core is { } coreId)
            {
                row["sprk_corerecordid"] = coreId.ToString("D").ToLowerInvariant();
                row["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", coreType ?? MatterType);
            }

            if (resolution is { } r)
            {
                row["sprk_resolutiontype"] = new OptionSetValue(r);
                row["sprk_resolvedon"] = Now.UtcDateTime.AddDays(-daysAgo);
            }

            if (record is { } rec) row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", rec);
            Signals.Add(row);
            return id;
        }

        /// <summary>The Signals as the nightly job reads them (policy-wide; keyed by the dedupe key).</summary>
        public IReadOnlyList<ExistingSignal> Existing() => Signals
            .Where(x => x.GetAttributeValue<EntityReference>("sprk_policy").Id == PolicyId)
            .Select(x =>
            {
                SignalWriter.TryParseDedupeKey(x.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subject, out var episode);
                return new ExistingSignal(
                    x.Id, "1", entity, subject, episode,
                    x.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value,
                    x.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
                    x.GetAttributeValue<DateTime?>("sprk_resolvedon"),
                    x.GetAttributeValue<DateTime?>("sprk_suppresseduntil"),
                    null,
                    x.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id,
                    Guid.TryParse(x.GetAttributeValue<string>("sprk_corerecordid"), out var core) ? core : null,
                    new EntityReference("systemuser", Guid.NewGuid()));
            }).ToList();

        private EntityCollection Run(QueryExpression q)
        {
            if (FailReads) throw new InvalidOperationException("dataverse is down");
            Queries++;
            var source = q.EntityName switch
            {
                "sprk_signal" => Signals,
                "sprk_decisionrecord" => Records,
                _ => throw new NotSupportedException(q.EntityName),
            };
            var result = new EntityCollection();
            foreach (var row in source.Where(r => Matches(r, q.Criteria))) result.Entities.Add(row);
            return result;
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            var parts = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
            return parts.Count == 0 || (filter.FilterOperator == LogicalOperator.Or ? parts.Any(p => p) : parts.All(p => p));
        }

        private static bool Matches(Entity row, ConditionExpression c)
        {
            // The primary key is the entity id, not an attribute.
            object? actual = c.AttributeName == row.LogicalName + "id" ? row.Id : (row.Contains(c.AttributeName) ? row[c.AttributeName] : null);
            actual = actual switch { OptionSetValue o => o.Value, EntityReference er => er.Id, _ => actual };
            return c.Operator switch
            {
                ConditionOperator.Equal => Equals(actual, c.Values[0]),
                ConditionOperator.In => c.Values.Any(v => Equals(actual, v)),
                ConditionOperator.BeginsWith => actual is string s && s.StartsWith((string)c.Values[0], StringComparison.Ordinal),
                ConditionOperator.GreaterEqual => actual is DateTime d && d >= (DateTime)c.Values[0],
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }
    }
}
