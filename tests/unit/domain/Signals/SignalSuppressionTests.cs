using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
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

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

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
        var quiet = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        s.Queries.Should().Be(0, "two dismissals cannot reach the threshold: no query");

        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3);
        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        var queriesAfterStamp = s.Queries;
        s.StampedIds.Clear();
        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        quiet.Failures.Should().Be(0);
        s.Queries.Should().Be(queriesAfterStamp, "a live stamp ends the nightly work for the grain");
        s.StampedIds.Should().BeEmpty("D-61: unchanged Signals are never stamped nightly");
    }

    [Fact]
    public async Task Reconcile_RepairsAPartlyWrittenStamp_TheNextNight()
    {
        var s = new Store { FailWriteNumber = 2 };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        var open = s.Open(MatterId, Guid.NewGuid());

        var first = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        first.Failures.Should().Be(1);
        s.StampedIds.Should().HaveCount(1, "the 2nd of 4 row writes failed: one row carries a live stamp, three do not");

        var second = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        second.Failures.Should().Be(0);
        s.StampedIds.Should().HaveCount(4, "every row of the grain, the open Signal included, now carries the stamp");
        s.StampedIds.Distinct().Should().HaveCount(4, "D-61: the row that already had its stamp is not written again");
        s.Signals.Single(x => x.Id == open).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().NotBeNull();
        s.StampedIds.Clear();
        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        s.StampedIds.Should().BeEmpty("a fully stamped grain is left alone");
    }

    [Fact]
    public async Task Reconcile_SkipsAPolicyThatDoesNotCount_WithNoGrainReads()
    {
        var s = new Store { PolicyCounts = false };
        for (var i = 0; i < 4; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, false, s.Existing(), default);

        failures.Should().Be(0);
        s.Queries.Should().Be(0, "a policy that never counts can never reach the threshold: no read at all, every night");
        s.StampedIds.Should().BeEmpty();
        signals.Should().OnlyContain(x => x.SuppressedUntil == null);
    }

    [Fact]
    public async Task Reconcile_DoesNotReadAGroup_WhoseDismissalsAreOneReview()
    {
        var s = new Store();
        var record = s.Record(RecordedDecisionClass.Dismissal, Counts, daysAgo: 2);
        for (var i = 0; i < 4; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, record: record);

        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        s.Queries.Should().Be(0, "four Signals of one Decision Record are one dismissal: below the threshold before any read");
    }

    [Fact]
    public async Task Reconcile_CountsAFailureAndStillSuppressesThisPass_WhenTheStampWriteFails()
    {
        var s = new Store { FailWrites = true };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(1, "the run must not look healthy when a stamp was not written");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], signals).Creates
            .Should().BeEmpty("a failed write must not let a pairing at the threshold resurface this pass");
    }

    // =====================================================================================
    // The count read (wizard dismissal step, admin): the CALLER's count
    // =====================================================================================

    private static Task<IResult> Route(Guid signal, Store s, (SignalCoreRecordAccess Access, IDataverseUserClient Users) c, HttpContext? http = null, CancellationToken ct = default) =>
        DecisionPlanEndpoints.GetSuppressionAsync(
            signal, http ?? new DefaultHttpContext(), c.Access, s.Service, c.Users, NullLogger<SignalSuppressionService>.Instance, ct);

    [Fact]
    public async Task TheRoute_Returns200WithTheCountAndTheUntilDate_ForACallerWhoCanReadEverySignal()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        var signal = s.Open(MatterId, Guid.NewGuid());

        var result = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        var body = result.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.DismissalsRecorded, body.Threshold, body.WindowDays).Should().Be((3, 3, 3, 30));
        body.SuppressedUntil.Should().Be(Now.UtcDateTime.AddDays(-1).AddDays(30));
        body.CountsTowardSuppression.Should().BeTrue();
    }

    [Fact]
    public async Task TheRoute_NeverCountsASignalTheCallerCannotRead_NorItsStamp()
    {
        var s = new Store();
        // Two readable dismissals; two more on the same (policy, matter) owned by a Secure team: the caller's read does not return them.
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 5);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 4);
        var hidden1 = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2);
        var hidden2 = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1);
        s.Stamp(hidden1, Now.UtcDateTime.AddDays(20));
        s.Stamp(hidden2, Now.UtcDateTime.AddDays(25));
        var mine = s.Open(MatterId, Guid.NewGuid());

        var trimmed = await Route(mine, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }), hide: [hidden1, hidden2]));
        var control = await Route(mine, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        var seen = trimmed.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (seen.DismissalCount, seen.DismissalsRecorded).Should().Be((2, 2), "hidden dismissals are neither counted nor recorded for this caller");
        seen.SuppressedUntil.Should().BeNull("a hidden Signal's live stamp is not the caller's to see");
        var all = control.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (all.DismissalCount, all.DismissalsRecorded).Should().Be((4, 4), "control: a caller who can read every Signal sees them all");
        all.SuppressedUntil.Should().Be(Now.UtcDateTime.AddDays(-1).AddDays(30), "four counted dismissals: the stamp the grain would carry");
    }

    [Fact]
    public async Task TheRoute_AsksTheCallersOwnClient_ForTheGrainsSignals_WithTheCoreFilter()
    {
        var s = new Store();
        var mine = s.Open(MatterId, Guid.NewGuid());
        var c = Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }));

        await Route(mine, s, c);

        var listPath = Mock.Get(c.Users).Invocations.Select(i => (string)i.Arguments[0]).Single(p => p.StartsWith("sprk_signals?", StringComparison.Ordinal));
        var filter = Uri.UnescapeDataString(listPath);
        filter.Should().Contain($"_sprk_policy_value eq {PolicyId}").And.Contain($"sprk_corerecordid eq '{MatterId}'")
            .And.Contain($"_sprk_corerecordtype_value eq {MatterType}")
            .And.Contain("sprk_signalstatus eq 100000000 or sprk_signalstatus eq 100000001")
            .And.Contain("sprk_resolutiontype eq 100000001 and sprk_resolvedon ge ");
    }

    [Fact]
    public async Task TheRoute_Returns404_WhenTheCallerCannotReadTheMatter_AndReadsNoCount()
    {
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());
        var before = s.Queries;

        var result = await Route(signal, s, Caller(s, Ok(SignalRow()), Fail(404, DataverseUserClientErrorCodes.NotFound)));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
        s.Queries.Should().Be(before);
    }

    [Fact]
    public async Task TheRoute_Returns403_ForAnUnresolvedCaller()
    {
        var s = new Store();

        var result = await Route(Guid.NewGuid(), s, Caller(s, Fail(0, DataverseUserClientErrorCodes.UserContextRequired), Ok(new { createdon = "x" })));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TheRoute_DeniedAndNotFound_AreTheSameBody_NoOracle()
    {
        var s = new Store();
        var noPolicy = s.Open(MatterId, Guid.NewGuid());
        s.Signals.Single(x => x.Id == noPolicy).Attributes.Remove("sprk_policy");
        var readable = s.Open(MatterId, Guid.NewGuid());

        static HttpContext Http() => new DefaultHttpContext { TraceIdentifier = "trace-1" };
        static string Body(IResult r) => JsonSerializer.Serialize(r.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails);

        var denied = await Route(readable, s, Caller(s, Ok(SignalRow()), Fail(404, DataverseUserClientErrorCodes.NotFound)), Http());
        var unknownPolicy = await Route(noPolicy, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })), Http());
        var unknownSignal = await Route(Guid.NewGuid(), s, Caller(s, Fail(404, DataverseUserClientErrorCodes.NotFound), Ok(new { createdon = "x" })), Http());

        unknownPolicy.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404, "a Signal with no policy has no grain: never a guess");
        Body(denied).Should().Be(Body(unknownPolicy)).And.Be(Body(unknownSignal), "byte-identical apart from nothing: same trace id, same shape");
    }

    [Fact]
    public async Task TheRoute_Returns503_WhenTheCountCannotBeRead_AfterAuthorization_NeverAGuess()
    {
        var s = new Store { FailReads = true };
        var signal = s.Open(MatterId, Guid.NewGuid());

        var result = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })), new DefaultHttpContext { TraceIdentifier = "trace-9" });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("trace-9");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(DecisionPlanEndpoints.SuppressionUnavailableReasonCode);
    }

    [Fact]
    public async Task TheRoute_Returns503_WhenTheCallersGrainReadFaults_TimesOut_OrIsNotASuccess_AfterAuthorization()
    {
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());

        var faulted = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }), listFault: new InvalidOperationException("boom")));
        // An HttpClient timeout is a cancellation the caller did not ask for (ct is NOT cancelled).
        var timedOut = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }), listFault: new TaskCanceledException("timeout")));
        var notSuccess = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }), listResponse: Fail(500, DataverseUserClientErrorCodes.ServiceError)));

        faulted.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(503);
        timedOut.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(503);
        notSuccess.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(503, "the trim read is fail-closed: a non-success is never an empty or partial count");
    }

    [Fact]
    public async Task TheRoute_Returns404_ForAFaultDuringAuthorization_NotA503()
    {
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());
        var before = s.Queries;
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("timeout"));
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(Ok(SignalRow()));
        var access = new SignalCoreRecordAccess(users.Object, entities.Object, NullLogger<SignalCoreRecordAccess>.Instance);

        var result = await Route(signal, s, (access, users.Object));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404, "AuthorizeAsync fails closed to the uniform 404, as decision-plan does");
        s.Queries.Should().Be(before);
    }

    [Fact]
    public async Task TheRoute_LetsARealCancellationPropagate()
    {
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" }), listFault: new OperationCanceledException(cts.Token)), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TheNoCoreGrain_IgnoresEpisodesThatLaterGotACoreRecord()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        s.Dismiss(core: null, subject: item, daysAgo: 3, episode: 1, subjectEntity: "sprk_todo");
        s.Dismiss(core: null, subject: item, daysAgo: 2, episode: 2, subjectEntity: "sprk_todo");
        // Episode 3 was raised after the To Do was filed under a matter: it belongs to the matter's grain, not the item's.
        s.Dismiss(core: MatterId, subject: item, daysAgo: 1, episode: 3, subjectEntity: "sprk_todo");

        var item2 = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item), default);
        var matter = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        item2.DismissalsCounted.Should().Be(2);
        item2.SuppressedUntil.Should().BeNull();
        matter.DismissalsCounted.Should().Be(1);
    }

    // =====================================================================================
    // The secure partition (#1661, owner decision D-123): a Secure-owned Signal is counted and muted only among Secure-owned Signals
    // =====================================================================================

    private static readonly Guid SecureTeamId = Store.SecureTeamId;

    [Fact]
    public async Task ThreeSecureDismissals_UnderAReadableMatter_StampOnlyTheSecureSignals_AndTheReadableOnesAreUntouched()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        var secureOpen = s.Open(MatterId, Guid.NewGuid(), secure: true);
        var readableDismissed = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);

        var readable = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        readable.DismissalsCounted.Should().Be(1, "only the ordinary partition's own dismissal is the readable pairing's count");
        readable.SuppressedUntil.Should().BeNull("no hidden dismissal may show in what a non-member can read");
        s.StampedIds.Should().NotContain([readableDismissed, readableOpen], "the readable Signals of the pairing are never stamped by hidden dismissals");
        s.StampedIds.Should().HaveCount(4, "the three secure dismissals and the open secure Signal are stamped");
        s.StampedIds.Should().Contain(secureOpen);
        s.Signals.Where(x => x.GetAttributeValue<EntityReference>("ownerid").Id == SecureTeamId)
            .Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") != null);
        s.Signals.Where(x => x.GetAttributeValue<EntityReference>("ownerid").Id != SecureTeamId)
            .Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null);
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, secureOwned: true)).DismissalsCounted.Should().Be(3);
    }

    [Fact]
    public async Task ThreeReadableDismissals_DoNotStampTheSecureSignals_OfTheSamePairing()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var secureOpen = s.Open(MatterId, Guid.NewGuid(), secure: true);
        var secureDismissed = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: true);

        var readable = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        readable.SuppressedUntil.Should().NotBeNull();
        s.StampedIds.Should().HaveCount(3).And.NotContain([secureOpen, secureDismissed],
            "the secure wall holds both ways: an ordinary mute never reaches a Secure-owned row");
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, secureOwned: true)).DismissalsCounted
            .Should().Be(1, "and the ordinary dismissals are not the secure partition's");
    }

    [Fact]
    public async Task TwoSecureAndOneReadableDismissal_Mixed_StampNobody()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        s.Open(MatterId, Guid.NewGuid(), secure: false);
        s.Open(MatterId, Guid.NewGuid(), secure: true);

        var readable = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        var secure = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, secureOwned: true);

        (readable.DismissalsCounted, secure.DismissalsCounted).Should().Be((1, 2), "three dismissals across the wall are 1 + 2, never 3");
        s.StampedIds.Should().BeEmpty();
        readable.SuppressedUntil.Should().BeNull();
        secure.SuppressedUntil.Should().BeNull();
    }

    [Fact]
    public async Task ATeamOwnedSignal_OfAnotherTeam_IsOrdinary_AndSoIsEverySignalWhenNoSecureTeamExists()
    {
        var s = new Store { SecureTeamExists = false };
        // Owned by a team (Operations) in an environment that has no Secure Record owner team: nothing can be secure-owned.
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var open = s.Open(MatterId, Guid.NewGuid(), secure: true); // a team id that is not any existing Secure team: still ordinary here

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.SuppressedUntil.Should().NotBeNull();
        s.StampedIds.Should().Contain(open, "with no Secure team to identify, every Signal is in the one ordinary partition");
    }

    [Fact]
    public async Task TheNoCoreGrain_IsPartitionedTheSameWay()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        for (var i = 0; i < 3; i++) s.Dismiss(core: null, subject: item, daysAgo: i + 1, episode: i + 1, subjectEntity: "sprk_todo", secure: true);
        var readableOpen = s.Open(core: null, subject: item, secure: false, subjectEntity: "sprk_todo");
        var secureOpen = s.Open(core: null, subject: item, secure: true, subjectEntity: "sprk_todo");

        var readable = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item), default);

        readable.SuppressedUntil.Should().BeNull();
        s.StampedIds.Should().HaveCount(4).And.Contain(secureOpen).And.NotContain(readableOpen);

        // The other way round, and mixed 2 + 1, for a second item.
        var item2 = Guid.NewGuid();
        s.StampedIds.Clear();
        s.Dismiss(core: null, subject: item2, daysAgo: 3, subjectEntity: "sprk_todo", episode: 1, secure: true);
        s.Dismiss(core: null, subject: item2, daysAgo: 2, subjectEntity: "sprk_todo", episode: 2, secure: true);
        s.Dismiss(core: null, subject: item2, daysAgo: 1, subjectEntity: "sprk_todo", episode: 3, secure: false);
        await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item2), default);
        s.StampedIds.Should().BeEmpty("2 secure + 1 readable dismissal of one item is nobody's third");
    }

    [Fact]
    public async Task Reconcile_StampsOnlyTheSecurePartition_AndHandsThePlannerNoStampOnAReadableRow()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);
        var secureOpen = s.Open(MatterId, Guid.NewGuid(), secure: true);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().HaveCount(4).And.Contain(secureOpen).And.NotContain(readableOpen);
        signals.Single(x => x.Id == readableOpen).SuppressedUntil.Should().BeNull("the nightly planner sees no stamp on a readable row");
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, Guid.NewGuid())], signals).Creates
            .Should().ContainSingle("the readable pairing is not suppressed by dismissals it cannot see");
    }

    [Fact]
    public async Task Reconcile_PartitionsTheNoCoreGrainToo()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        for (var i = 0; i < 3; i++) s.Dismiss(core: null, subject: item, daysAgo: i + 1, episode: i + 1, subjectEntity: "sprk_todo", secure: true);
        var readableOpen = s.Open(core: null, subject: item, secure: false, subjectEntity: "sprk_todo");

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().HaveCount(3).And.NotContain(readableOpen, "three Secure-owned dismissals of an item never mute its ordinary Signals");
        signals.Single(x => x.Id == readableOpen).SuppressedUntil.Should().BeNull();
    }
    [Fact]
    public async Task Reconcile_ReadsNothing_ForAMixedTwoPlusOne_AndDoesNotStampTheReadableThree()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);

        var (_, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().BeEmpty();
        s.Queries.Should().Be(2, "only the Secure team lookup (business unit, team): neither partition has three dismissals, so no grain is read");
    }

    [Fact]
    public async Task Reconcile_CountsAFailureAndStampsNothing_WhenTheSecureTeamCannotBeIdentified()
    {
        var s = new Store { SecureBusinessUnitAmbiguous = true };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        s.Open(MatterId, Guid.NewGuid(), secure: false);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(1, "a grain that cannot be partitioned is never counted whole: the run fails visibly and retries");
        s.StampedIds.Should().BeEmpty();
        signals.Should().OnlyContain(x => x.SuppressedUntil == null);
    }

    [Fact]
    public async Task ThePlanner_ReadsAStampOnlyWithinItsPartition_ASecureStampMutesASecureSubject_NeverAReadableOne()
    {
        var s = new Store();
        var secureSubjects = new[] { 3, 2, 1 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, secure: true)).ToList();
        var readableDismissed = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20, secure: false); // out of the 14-day quiet window
        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        failures.Should().Be(0);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, s.SubjectOf(secureSubjects[0])), Hit(MatterId, s.SubjectOf(readableDismissed))], signals);

        plan.Suppressed.Should().Be(1, "the Secure-owned subject is muted by the Secure partition's stamp");
        plan.Creates.Should().ContainSingle().Which.Hit.SubjectId.Should().Be(s.SubjectOf(readableDismissed),
            "the readable subject re-raises: hidden Secure dismissals never mute it");
    }

    [Fact]
    public async Task ThePlanner_AReadableStamp_DoesNotMuteASecureSubjectEither()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var secureDismissed = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20, secure: true);
        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        var plan = SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, [Hit(MatterId, s.SubjectOf(secureDismissed))], signals);

        plan.Suppressed.Should().Be(0);
        plan.Creates.Should().ContainSingle("an ordinary mute never reaches a Secure-owned subject");
    }

    [Fact]
    public async Task Reconcile_WithholdsATeamOwnedRowsStamp_FromThePlanner_WhenTheSecureTeamCannotBeIdentified()
    {
        var s = new Store { SecureBusinessUnitAmbiguous = true };
        var teamOwned = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20, secure: true);
        var ordinary = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20); // user-owned: cannot be the Secure team's
        var stamp = Now.UtcDateTime.AddDays(10);
        s.Stamp(teamOwned, stamp);
        s.Stamp(ordinary, stamp);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(1);
        signals.Single(x => x.Id == teamOwned).SuppressedUntil.Should().BeNull("a stamp that may be a Secure one is not read as an ordinary mute");
        signals.Single(x => x.Id == ordinary).SuppressedUntil.Should().Be(stamp, "only team-owned rows are withheld (in memory; nothing is written): a user-owned row cannot be Secure-owned");
        s.StampedIds.Should().BeEmpty();
    }
    [Fact]
    public async Task Apply_Throws_WhenTheSecureTeamCannotBeIdentified_NeverCountingTheWallAsOneGrain()
    {
        var s = new Store { SecureBusinessUnitAmbiguous = true };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);

        var act = () => s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Secure Record owner team*");
        s.StampedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRoute_ForAReadableSignal_NeverCountsSecureDismissals_EvenWhenTheCallerCanReadThem()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 4, secure: false);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: false);
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true, record: null);
        var mine = s.Open(MatterId, Guid.NewGuid(), secure: false);

        // No Signal is hidden from this caller (an owner of the Secure team's rows, say): the partition, not the trim, keeps them apart.
        var result = await Route(mine, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        var body = result.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.DismissalsRecorded).Should().Be((2, 2), "the five dismissals of the pairing are 2 readable and 3 secure");
        body.SuppressedUntil.Should().BeNull();
    }

    [Fact]
    public async Task TheRoute_ForASecureSignal_CountsOnlyTheSecurePartition()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        var mine = s.Open(MatterId, Guid.NewGuid(), secure: true);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        var result = await Route(mine, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        var body = result.Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.DismissalsRecorded).Should().Be((3, 3), "a member who can read the secure Signals counts the secure partition only");
        body.SuppressedUntil.Should().Be(Now.UtcDateTime.AddDays(-1).AddDays(30));
    }

    [Fact]
    public async Task TheRoute_ForAReadableSignal_AsksNoSecureTeamLookup_WhenNoSignalIsTeamOwned()
    {
        var s = new Store();
        var mine = s.Open(MatterId, Guid.NewGuid());

        await Route(mine, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        s.TeamReads.Should().Be(0, "an owner that is a user cannot be the Secure team: no lookup");
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

    /// <summary>The caller's Dataverse client and the access decision over it. <paramref name="hide"/> are Signals the caller's own read does
    /// not return (a Secure team's rows under a readable core record); every other Signal in the store is returned.</summary>
    private static (SignalCoreRecordAccess Access, IDataverseUserClient Users) Caller(
        Store s, DataverseUserResponse signalRow, DataverseUserResponse core, IEnumerable<Guid>? hide = null,
        Exception? listFault = null, DataverseUserResponse? listResponse = null)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
        entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        var hidden = (hide ?? []).ToHashSet();
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(signalRow);
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals?", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
            {
                if (listFault is not null) throw listFault;
                return Task.FromResult(listResponse ?? Ok(new { value = s.Signals.Where(x => !hidden.Contains(x.Id)).Select(x => new { sprk_signalid = x.Id }).ToArray() }));
            });
        users.Setup(u => u.GetAsync(It.Is<string>(p => !p.StartsWith("sprk_signals", StringComparison.Ordinal) && p != "WhoAmI"), It.IsAny<CancellationToken>())).ReturnsAsync(core);
        users.Setup(u => u.GetAsync("WhoAmI", It.IsAny<CancellationToken>())).ReturnsAsync(Fail(0, DataverseUserClientErrorCodes.UserContextRequired));
        return (new SignalCoreRecordAccess(users.Object, entities.Object, NullLogger<SignalCoreRecordAccess>.Instance), users.Object);
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
        public int FailWriteNumber { get; set; }
        private int _writes;
        public bool FailReads { get; set; }
        public int Queries { get; private set; }

        // uac-r2's identification of the Secure Record owner team: the configured BU (default name), and in it a NAMED, non-default Owner team.
        public static readonly Guid SecureTeamId = Guid.Parse("5ec0be00-0000-4000-8000-000000000001");
        public static readonly Guid OperationsTeamId = Guid.Parse("0e1a7100-0000-4000-8000-000000000002");
        private static readonly Guid SecureBuId = Guid.Parse("5ec0be00-0000-4000-8000-0000000000b1");
        public bool SecureTeamExists { get; set; } = true;
        public bool SecureBusinessUnitAmbiguous { get; set; }

        private List<Entity> SecureBusinessUnits() =>
            !SecureTeamExists ? [] : Enumerable.Range(0, SecureBusinessUnitAmbiguous ? 2 : 1)
                .Select(i => new Entity("businessunit", i == 0 ? SecureBuId : Guid.NewGuid()) { ["name"] = SecureRecordOwnerTeam.DefaultBusinessUnitName }).ToList();

        public int TeamReads { get; private set; }

        private List<Entity> CountTeamRead() { TeamReads++; return Teams(); }

        private List<Entity> Teams() =>
        [
            new Entity("team", SecureTeamId)
            {
                ["businessunitid"] = new EntityReference("businessunit", SecureBuId), ["name"] = SecureRecordOwnerTeam.DefaultOwnerTeamName,
                ["teamtype"] = new OptionSetValue(SecureRecordOwnerTeam.OwnerTeamType), ["isdefault"] = false,
            },
            new Entity("team", OperationsTeamId)
            {
                ["businessunitid"] = new EntityReference("businessunit", Guid.NewGuid()), ["name"] = "Operations",
                ["teamtype"] = new OptionSetValue(SecureRecordOwnerTeam.OwnerTeamType), ["isdefault"] = true,
            },
        ];
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
                    if (FailWrites || (FailWriteNumber > 0 && ++_writes == FailWriteNumber)) throw new InvalidOperationException("Simulated Dataverse failure on the update.");
                    update.Attributes.Keys.Should().Equal(["sprk_suppresseduntil"], "the stamp touches that one column and nothing else");
                    StampedIds.Add(update.Id);
                    Signals.Single(x => x.Id == update.Id)["sprk_suppresseduntil"] = update["sprk_suppresseduntil"];
                    return Task.CompletedTask;
                });
            var writer = new OntologyWriterDataverseClient(() => org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);
            Service = new SignalSuppressionService(entities.Object, writer, clock, new ConfigurationBuilder().Build(), NullLogger<SignalSuppressionService>.Instance);
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
            Guid? record = null, Guid? policy = null, Guid? coreType = null, int episode = 1, string subjectEntity = "sprk_communication", bool? secure = null) =>
            Add(core, subject, SignalWriter.ResolutionDismissed, daysAgo, record ?? Record(recordClass, reason, daysAgo), policy, coreType, episode, subjectEntity, secure);

        public Guid Closed(Guid core, int resolution, int daysAgo) =>
            Add(core, Guid.NewGuid(), resolution, daysAgo, null, null, null, 1, "sprk_communication");

        public Guid Open(Guid? core, Guid subject, bool? secure = null, string subjectEntity = "sprk_communication") =>
            Add(core, subject, null, 0, null, null, null, 1, subjectEntity, secure);

        public void Stamp(Guid signalId, DateTime until) => Signals.Single(x => x.Id == signalId)["sprk_suppresseduntil"] = until;

        public Guid SubjectOf(Guid signalId)
        {
            SignalWriter.TryParseDedupeKey(Signals.Single(x => x.Id == signalId).GetAttributeValue<string>("sprk_dedupekey"), out _, out _, out var id, out _);
            return id;
        }

        private Guid Add(Guid? core, Guid subject, int? resolution, int daysAgo, Guid? record, Guid? policy, Guid? coreType, int episode, string subjectEntity, bool? secure = null)
        {
            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id)
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policy ?? PolicyId),
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(Code, subjectEntity, subject.ToString("D").ToLowerInvariant(), episode),
                ["sprk_signalstatus"] = new OptionSetValue(resolution is null ? SignalWriter.SignalStatusOpen : SignalWriter.SignalStatusResolved),
            };
            // secure: null = no owner on the row (every pre-#1661 test); false = an ordinary team (Operations); true = the Secure Record owner team.
            if (secure is { } isSecure) row["ownerid"] = new EntityReference("team", isSecure ? SecureTeamId : OperationsTeamId);
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
                    x.GetAttributeValue<EntityReference>("ownerid") ?? new EntityReference("systemuser", Guid.NewGuid()),
                    DecisionRecordId: x.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id);
            }).ToList();

        private EntityCollection Run(QueryExpression q)
        {
            if (FailReads) throw new InvalidOperationException("dataverse is down");
            Queries++;
            var source = q.EntityName switch
            {
                "sprk_signal" => Signals,
                "sprk_decisionrecord" => Records,
                "businessunit" => SecureBusinessUnits(),
                "team" => CountTeamRead(),
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
                ConditionOperator.Null => actual is null,
                ConditionOperator.GreaterEqual => actual is DateTime d && d >= (DateTime)c.Values[0],
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }
    }
}
