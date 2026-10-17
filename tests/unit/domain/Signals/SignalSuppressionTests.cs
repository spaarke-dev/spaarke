using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
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
public partial class SignalSuppressionTests
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
        var grainReadsAfterStamp = s.QueriesOf("sprk_signal");
        s.StampedIds.Clear();
        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        quiet.Failures.Should().Be(0);
        s.QueriesOf("sprk_signal").Should().Be(grainReadsAfterStamp,
            "a live stamp its own partition justifies ends the nightly work for the grain (its partition is placed, the grain is not read)");
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
        var access = new SignalCoreRecordAccess(users.Object, entities.Object, Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(), NullLogger<SignalCoreRecordAccess>.Instance);

        var result = await Route(signal, s, (access, users.Object));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404, "AuthorizeAsync fails closed to the uniform 404, as decision-plan does");
        s.Queries.Should().Be(before);
    }

    [Fact]
    public async Task TheRoute_Returns503_WhenANoCoreDoSignalsSubjectReadFaults_NotTheUniform404_D35()
    {
        // D-35 as amended by D-129: a no-core Do Signal needs the caller's read of its subject; a fault reading it is not "not yours".
        var s = new Store();
        var signal = s.Open(MatterId, Guid.NewGuid());
        var before = s.Queries;
        var todoSubject = Guid.NewGuid();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync("sprk_todo", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_todos");
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(new Dictionary<string, object?>
            {
                ["sprk_lane"] = (int)DecisionLane.Do,
                ["_sprk_policyversion_value"] = Guid.NewGuid(),
                // Task 141: the subject is the typed lookup that carries the pair id (the pair type is a lookup, not read).
                ["_sprk_regardingtodo_value"] = todoSubject.ToString(),
                ["sprk_regardingrecordid"] = todoSubject.ToString(),
            }));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fail(500, DataverseUserClientErrorCodes.ServiceError));
        var access = new SignalCoreRecordAccess(users.Object, entities.Object, Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(), NullLogger<SignalCoreRecordAccess>.Instance);

        var result = await Route(signal, s, (access, users.Object));

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(503);
        s.Queries.Should().Be(before, "nothing is counted for a caller whose access could not be decided");
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
    // The secure-family partition (#1661, owner decision D-123; uac-r2 audit F6): a Signal is counted and muted only among the Signals of
    // its own partition, the ordinary one or one per SET of secure roots.
    // =====================================================================================

    private static readonly Guid SecureTeamId = Store.SecureTeamId;
    private static readonly SuppressionPartition FamilyA = SuppressionPartition.Secure([("sprk_project", Store.ProjectA)]);
    private static readonly SuppressionPartition FamilyB = SuppressionPartition.Secure([("sprk_project", Store.ProjectB)]);

    /// <summary>The job's order: the hits on a stamped record are placed by the service's truth, then planned.</summary>
    private static async Task<EpisodePlan> PlanWithTruth(Store s, IReadOnlyList<SignalHit> hits, IReadOnlyList<ExistingSignal> signals)
    {
        var partitions = await s.Service.PartitionsOfHitsAsync(Code, SignalEpisodePlanner.HitsOnStampedGrains(hits, signals, Now), default);
        partitions.Should().NotBeNull("the job plans nothing for a policy whose hit partitions cannot be read");
        return SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, Now, hits, signals, partitions);
    }

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
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA)).DismissalsCounted.Should().Be(3);
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
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA)).DismissalsCounted
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
        var secure = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA);

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
    public async Task TheNoCoreGrain_FollowsItsItem_ASecureItemsSignalsAreItsFamilysWhateverTheirOwner()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        // The To Do is under secure project A. Two of its dismissed Signals are Secure-owned; the third is still the writer's (re-owning lags).
        s.Dismiss(core: null, subject: item, daysAgo: 3, episode: 1, subjectEntity: "sprk_todo", secure: true);
        s.Dismiss(core: null, subject: item, daysAgo: 2, episode: 2, subjectEntity: "sprk_todo", secure: true);
        s.Dismiss(core: null, subject: item, daysAgo: 1, episode: 3, subjectEntity: "sprk_todo", secure: false);

        var readable = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item), default);
        var family = await s.Service.ApplyAsync(PolicyId, Code, SuppressionGrain.ForItem("sprk_todo", item), default, report: FamilyA);

        readable.DismissalsCounted.Should().Be(0, "no Signal of a secure item is in the ordinary partition");
        family.DismissalsCounted.Should().Be(3);
        s.StampedIds.Should().HaveCount(3, "the item's three dismissals are one family's, and stamp that family's rows");
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
        var fresh = Guid.NewGuid();
        s.Subject(fresh);
        (await PlanWithTruth(s, [Hit(MatterId, fresh)], signals)).Creates
            .Should().ContainSingle("the readable pairing is not suppressed by dismissals it cannot see");
    }

    [Fact]
    public async Task Reconcile_NeverStampsTheUndeterminedBucket_OfANoCoreItem()
    {
        var s = new Store();
        var item = Guid.NewGuid();
        // Three Secure-owned dismissals of a To Do that is no longer under any secure root: their family cannot be named.
        s.Subject(item, "sprk_todo");
        for (var i = 0; i < 3; i++) s.Dismiss(core: null, subject: item, daysAgo: i + 1, episode: i + 1, subjectEntity: "sprk_todo", secure: true);
        var readableOpen = s.Open(core: null, subject: item, secure: false, subjectEntity: "sprk_todo");

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().BeEmpty("the undetermined bucket is never stamped, and its dismissals are nobody else's");
        signals.Where(x => x.Id != readableOpen).Should().OnlyContain(x => x.Partition == SuppressionPartition.Undetermined);
        signals.Single(x => x.Id == readableOpen).SuppressedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Reconcile_ReadsNoGrain_ForAMixedTwoPlusOne_AndDoesNotStampTheReadableThree()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);

        var (_, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().BeEmpty();
        s.QueriesOf("sprk_signal").Should().Be(0, "the partitions are placed in memory; neither has three dismissals, so no grain is read");
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

        var plan = await PlanWithTruth(s, [Hit(MatterId, s.SubjectOf(secureSubjects[0])), Hit(MatterId, s.SubjectOf(readableDismissed))], signals);

        plan.Suppressed.Should().Be(1, "the secure subject is muted by its family's stamp");
        plan.Creates.Should().ContainSingle().Which.Hit.SubjectId.Should().Be(s.SubjectOf(readableDismissed),
            "the readable subject re-raises: hidden secure dismissals never mute it");
    }

    [Fact]
    public async Task ThePlanner_AReadableStamp_DoesNotMuteASecureSubjectEither()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var secureDismissed = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20, secure: true);
        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        var plan = await PlanWithTruth(s, [Hit(MatterId, s.SubjectOf(secureDismissed))], signals);

        plan.Suppressed.Should().Be(0);
        plan.Creates.Should().ContainSingle("an ordinary mute never reaches a secure subject");
    }

    [Fact]
    public async Task Reconcile_ReportsThePartitionsUnavailable_AndWritesNothing_WhenTheSecureTeamCannotBeIdentified()
    {
        var s = new Store { SecureBusinessUnitAmbiguous = true };
        var teamOwned = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20, secure: true);
        var userOwned = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20);
        var stamp = Now.UtcDateTime.AddDays(10);
        s.Stamp(teamOwned, stamp);
        s.Stamp(userOwned, stamp);

        var result = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        (result.PartitionsUnavailable, result.Failures).Should().Be((true, 1),
            "K1 (round 2 F3): a stamp whose partition cannot be known is neither a mute nor no mute; the job plans nothing for the policy");
        s.StampedIds.Should().BeEmpty("nothing is written that night");
        result.Signals.Should().OnlyContain(x => x.SuppressedUntil == stamp, "the rows are handed back as read, never with stamps withheld");
    }

    [Fact]
    public async Task R2F3_AGrainWhoseCountFails_LeavesThePolicyAsItWas_NoOtherGrainIsStamped_AndThePartitionsAreReportedUnavailable()
    {
        var s = new Store { FailSignalReadNumber = 2 };
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        for (var i = 0; i < 3; i++) s.Dismiss(OtherMatterId, Guid.NewGuid(), daysAgo: i + 1);

        var result = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        (result.PartitionsUnavailable, result.Failures).Should().Be((true, 1), "a per-grain count failure is handled as the partition failure is");
        s.QueriesOf("sprk_signal").Should().Be(1, "the first grain was counted, the second grain's read timed out");
        s.StampedIds.Should().BeEmpty("every worked grain is counted before anything is written: the first grain's stamps wait for the next run");
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

        // No Signal is hidden from this caller (a member of the secure project, say): the partition, not the trim, keeps them apart.
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

    // ── #1661 fix round 2 (uac-r2 CHANGES REQUESTED at 35ce786, audit F6, adversarial review) ──────────────────────

    [Fact]
    public async Task R1_ASubjectThatLeftTheSecureFamily_IsNotMutedByTheFamilysStamp()
    {
        var s = new Store();
        var leaver = Guid.NewGuid();
        s.Dismiss(MatterId, leaver, daysAgo: 20, secure: true); // its latest episode: in the family, out of the quiet window
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        failures.Should().Be(0);
        signals.Single(x => x.SubjectId == leaver).SuppressedUntil.Should().NotBeNull("its family's stamp is on its own latest episode");

        s.Subject(leaver); // re-filed out of the secure project: the writer's truth makes its next episode ordinary
        var plan = await PlanWithTruth(s, [Hit(MatterId, leaver)], signals);

        plan.Suppressed.Should().Be(0, "the family's stamp comes from dismissals a reader of the new, ordinary episode cannot see");
        plan.Creates.Should().ContainSingle().Which.Episode.Should().Be(2);
    }

    [Fact]
    public async Task R1_AFirstEpisode_InASecureFamily_IsMutedByThatFamilysStamp_AndAnOrdinaryOneIsNot()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: true);
        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        var newcomer = Guid.NewGuid();
        s.Subject(newcomer, family: Store.ProjectA, owner: Store.SecureOwner);
        var ordinary = Guid.NewGuid();
        s.Subject(ordinary);

        var plan = await PlanWithTruth(s, [Hit(MatterId, newcomer), Hit(MatterId, ordinary)], signals);

        plan.Suppressed.Should().Be(1, "a subject with no episode yet is placed by the truth, not read as ordinary");
        plan.Creates.Should().ContainSingle().Which.Hit.SubjectId.Should().Be(ordinary);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("secure")]
    [InlineData("ordinary")]
    public async Task R2F2_AnUndeterminedHit_PlansExactlyAsItWouldWithNoStampOnTheRecord(string stampOnTheRecord)
    {
        var s = new Store();
        if (stampOnTheRecord != "none")
        {
            for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: stampOnTheRecord == "secure");
        }

        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        signals.Any(x => x.SuppressedUntil is not null).Should().Be(stampOnTheRecord != "none", "the precondition: the record carries a live stamp or none");
        var unplaceable = Guid.NewGuid(); // no subject row: the walk reports a missing parent (undetermined)

        var plan = await PlanWithTruth(s, [Hit(MatterId, unplaceable)], signals);

        (plan.Creates.Count, plan.Suppressed).Should().Be((1, 0),
            "the undetermined bucket never mutes and is never muted: whether a non-member's To Do is raised says nothing about a hidden family's dismissals");
    }

    [Fact]
    public async Task R2F2_ThePartitionsOfTheHits_AreNull_WhenTheyCannotBeRead_NeverAPartialAnswer()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        var fresh = Guid.NewGuid();
        s.Subject(fresh);
        s.SecureBusinessUnitAmbiguous = true;

        var partitions = await s.Service.PartitionsOfHitsAsync(Code, SignalEpisodePlanner.HitsOnStampedGrains([Hit(MatterId, fresh)], signals, Now), default);

        partitions.Should().BeNull("a fault is never answered per hit: the job then plans nothing for the policy");
    }

    [Fact]
    public async Task R2N2_AHitWhoseSubjectIsSecureOwnedWithNoSecureRoot_IsUndetermined_AndNotMutedByAnOrdinaryStamp()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1, secure: false);
        var (signals, _) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);
        var orphan = Guid.NewGuid();
        s.Subject(orphan, owner: Store.SecureOwner); // owned by the Secure team but filed under no secure root: its family cannot be named
        var ordinary = Guid.NewGuid();
        s.Subject(ordinary);

        var partitions = await s.Service.PartitionsOfHitsAsync(Code, [Hit(MatterId, orphan), Hit(MatterId, ordinary)], default);
        var plan = await PlanWithTruth(s, [Hit(MatterId, orphan), Hit(MatterId, ordinary)], signals);

        partitions![("sprk_communication", orphan)].Should().Be(SuppressionPartition.Undetermined, "the writer would give its Signal the Secure team");
        plan.Suppressed.Should().Be(1, "control: the ordinary subject is muted by the ordinary stamp");
        plan.Creates.Should().ContainSingle().Which.Hit.SubjectId.Should().Be(orphan, "an ordinary stamp never mutes a Signal that will be Secure-owned");
    }

    [Fact]
    public void Census182_TheProbeId_IsThePolicyAndSubjectsIdentity_DistinctPerHit_StablePerHit_AndVersion5()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = SignalSuppressionService.ProbeId(Code, Hit(MatterId, a));

        first.Should().Be(SignalSuppressionService.ProbeId(Code, Hit(MatterId, a)), "the same hit has the same probe id on every call");
        first.Should().NotBe(SignalSuppressionService.ProbeId(Code, Hit(MatterId, b)), "two hits (two subjects) must not collapse in the walk's (table, id) de-duplication");
        first.Should().NotBe(SignalSuppressionService.ProbeId("OTHER-POLICY", Hit(MatterId, a)), "the same subject under another policy is another hit");
        first.Should().NotBe(Guid.Empty);
        first.ToString("D")[14].Should().Be('5', "a name-based (RFC 4122 version 5) Guid");
    }

    [Fact]
    public async Task Census182_ASecureHit_IsStillPlacedUnderItsRoot_WhenAnOrdinaryHitComesFirst()
    {
        // The walk de-duplicates rows by (table, id): if every probe shared one id, the secure hit's probe (whose only root is its
        // core matter) would be dropped behind the ordinary one, and the whole set would be read as "no secure root anywhere".
        var s = new Store();
        var secureMatter = Guid.NewGuid();
        s.SecureMatter(secureMatter);
        var ordinary = Guid.NewGuid();
        s.Subject(ordinary);
        var underSecureMatter = Guid.NewGuid();
        s.Subject(underSecureMatter);

        var partitions = await s.Service.PartitionsOfHitsAsync(Code, [Hit(MatterId, ordinary), Hit(secureMatter, underSecureMatter)], default);

        partitions.Should().NotBeNull();
        partitions![("sprk_communication", ordinary)].Should().Be(SuppressionPartition.Ordinary);
        partitions[("sprk_communication", underSecureMatter)].Should().Be(SuppressionPartition.Secure([("sprk_matter", secureMatter)]));
    }

    [Fact]
    public async Task R2_AStampLeftByAMoveIntoASecureFamily_IsWithdrawnNightly_AndTheRouteShowsNoDate()
    {
        var s = new Store();
        var mover = Guid.NewGuid();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: false);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: false);
        s.Dismiss(MatterId, mover, daysAgo: 1, secure: false);
        var open = s.Open(MatterId, Guid.NewGuid(), secure: false);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        s.Signals.Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") != null, "three ordinary dismissals stamp the pairing");
        s.StampedIds.Clear();

        s.Subject(mover, family: Store.ProjectA, owner: Store.SecureOwner); // its project is made secure; its Signal is not re-owned yet
        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.Signals.Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null,
            "two ordinary dismissals and one secure one justify no stamp in either partition");
        signals.Should().OnlyContain(x => x.SuppressedUntil == null, "the planner sees the withdrawal this pass");
        var body = (await Route(open, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })))).Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.SuppressedUntil).Should().Be((2, (DateTime?)null), "a non-member is not told of a dismissal that is now hidden");
    }

    [Fact]
    public async Task R2_TheImmediateApply_WithdrawsAStampItsPartitionNoLongerJustifies()
    {
        var s = new Store();
        var mover = Guid.NewGuid();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: false);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: false);
        s.Dismiss(MatterId, mover, daysAgo: 1, secure: false);
        var open = s.Open(MatterId, Guid.NewGuid(), secure: false);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        s.Subject(mover, family: Store.ProjectA, owner: Store.SecureOwner);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.SuppressedUntil.Should().BeNull();
        s.Signals.Single(x => x.Id == open).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().BeNull();
    }

    [Fact]
    public async Task R2_ACrossWallStamp_WrittenBeforeTheFix_IsClearedFromTheReadableRows_AndTheFamilyKeepsItsOwn()
    {
        var s = new Store();
        var secure = new[] { 3, 2, 1 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, secure: true)).ToList();
        var readable = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);
        var preFix = Now.UtcDateTime.AddDays(-1).AddDays(30); // the pre-#1661 code counted the pairing whole and stamped every row
        foreach (var id in s.Signals.Select(x => x.Id).ToList()) s.Stamp(id, preFix);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().BeEquivalentTo([readable, readableOpen], "only rows whose stamp changes are written (D-61)");
        s.Signals.Where(x => x.Id == readable || x.Id == readableOpen).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null);
        s.Signals.Where(x => secure.Contains(x.Id)).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == preFix,
            "the family's own three dismissals justify exactly that date");
        signals.Single(x => x.Id == readableOpen).SuppressedUntil.Should().BeNull();
    }

    [Fact]
    public async Task R2_APolicySwitchedToNo_HasItsLiveStampsWithdrawn()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++) s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: i + 1);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        s.PolicyCounts = false;

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, false, s.Existing(), default);

        failures.Should().Be(0);
        s.Signals.Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null);
        signals.Should().OnlyContain(x => x.SuppressedUntil == null);
    }

    [Fact]
    public async Task R3_ASignalOwnedByAnOrdinaryUser_UnderASecureAncestor_CountsAsSecure()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++)
        {
            var subject = Guid.NewGuid();
            s.Subject(subject, family: Store.ProjectA, owner: Store.SecureOwner);
            var id = s.Dismiss(MatterId, subject, daysAgo: i + 1);
            s.OwnBy(id, new EntityReference("systemuser", Guid.NewGuid())); // the writer's user: not re-owned yet
        }

        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);

        var readable = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        readable.DismissalsCounted.Should().Be(0, "ownerid lags; the lineage puts these Signals in the family");
        s.StampedIds.Should().HaveCount(3).And.NotContain(readableOpen);
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA)).DismissalsCounted.Should().Be(3);
    }

    [Fact]
    public async Task R3_ASubjectStillOwnedByAnOrdinaryTeam_UnderASecureProject_PlacesItsSignalsInTheFamily()
    {
        var s = new Store();
        for (var i = 0; i < 3; i++)
        {
            var subject = Guid.NewGuid();
            // Filed under secure project A but still owned by an ordinary team while it waits to be re-owned: the Signal's own walk stops
            // at it (an intermediate row's ordinary team), so only the walk started FROM the subject reaches A (uac-r2's answer).
            s.Subject(subject, family: Store.ProjectA, owner: Store.OrdinaryTeam);
            s.Dismiss(MatterId, subject, daysAgo: i + 1, secure: false);
        }

        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);

        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().HaveCount(3).And.NotContain(readableOpen, "the subject's own roots make its Signals secure");
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA)).DismissalsCounted.Should().Be(3);
    }

    [Fact]
    public async Task Task142_PartitionsOfSignals_PlaceEachSignalInItsOwnFamily_InOrder_ThroughUac2sRealWalk()
    {
        // The decision route's two-wall check (contract C9) reads this: two secure projects under one readable matter are two
        // families, an ordinary Signal is a third, and a Secure-owned Signal under no secure root cannot be placed.
        var s = new Store();
        var inA = s.Open(MatterId, Guid.NewGuid(), secure: true, family: Store.ProjectA);
        var inB = s.Open(MatterId, Guid.NewGuid(), secure: true, family: Store.ProjectB);
        var alsoInA = s.Open(MatterId, Guid.NewGuid(), secure: true, family: Store.ProjectA);
        var ordinary = s.Open(MatterId, Guid.NewGuid(), secure: false);
        var orphanSubject = Guid.NewGuid();
        s.Subject(orphanSubject, owner: Store.SecureOwner);
        var orphan = s.Open(MatterId, orphanSubject, secure: true);

        var families = await s.Service.PartitionsOfSignalsAsync([inA, inB, alsoInA, ordinary, orphan], default);

        families.Should().Equal(FamilyA, FamilyB, FamilyA, SuppressionPartition.Ordinary, SuppressionPartition.Undetermined);

        s.FailReads = true;
        await s.Service.Invoking(x => x.PartitionsOfSignalsAsync([inA, inB], default)).Should().ThrowAsync<Exception>("a fault is never an answer");
    }

    [Fact]
    public async Task R4_TwoSecureFamilies_UnderOneReadableMatter_EachHaveTheirOwnCount()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 3, secure: true, family: Store.ProjectA);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true, family: Store.ProjectA);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 2, secure: true, family: Store.ProjectB);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: true, family: Store.ProjectB);
        var openA = s.Open(MatterId, Guid.NewGuid(), secure: true, family: Store.ProjectA);
        var openB = s.Open(MatterId, Guid.NewGuid(), secure: true, family: Store.ProjectB);

        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().BeEmpty("2 + 2 across two families is nobody's third: members of B never learn of A's dismissals");
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyA)).DismissalsCounted.Should().Be(2);
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default, report: FamilyB)).DismissalsCounted.Should().Be(2);

        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: true, family: Store.ProjectA);
        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().Contain(openA).And.NotContain(openB, "A's third dismissal stamps A only");
    }

    [Fact]
    public async Task R5_TheRoute_Returns503_WhenTheSecureTeamIsAmbiguous()
    {
        var s = new Store { SecureBusinessUnitAmbiguous = true };
        var signal = s.Open(MatterId, Guid.NewGuid());

        var result = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })), new DefaultHttpContext { TraceIdentifier = "trace-7" });

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503, "a count that cannot be partitioned is never answered");
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(DecisionPlanEndpoints.SuppressionUnavailableReasonCode);
    }

    [Fact]
    public async Task R5_TheRoute_Returns503_ForAnAnchorWhosePartitionIsUndetermined()
    {
        var s = new Store();
        var subject = Guid.NewGuid();
        s.Subject(subject); // an ordinary subject, yet the Signal is owned by the Secure team: which family is unknown
        var signal = s.Open(MatterId, subject, secure: true);

        var result = await Route(signal, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })));

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(503);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(DecisionPlanEndpoints.PartitionUndeterminedReasonCode,
            "round 2 K5: a data condition has its own reason code, distinct from a transient fault's");
        problem.ProblemDetails.Detail.Should().NotContainEquivalentOf("try again", "a retry never changes an undetermined placement");
    }

    // ── #1661 fix round 2 at 63e7a569 (second adversarial pass; uac-r2 re-check) ─────────────────────────────────

    [Fact]
    public async Task R2F1_AStamp_LivesItsFull30Days_AfterItsOldestDismissalLeavesTheTrailingCount_NightlyApplyAndRoute()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 29);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 10);
        var open = s.Open(MatterId, Guid.NewGuid());
        var until = Now.UtcDateTime.AddDays(-10).AddDays(30);
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default)).SuppressedUntil.Should().Be(until);
        s.StampedIds.Clear();

        s.Clock.Advance(TimeSpan.FromDays(4)); // the 29-day-old dismissal is now 33 days old: the trailing count is 2, the stamp has 16 days left

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.StampedIds.Should().BeEmpty("FR-17: the suppression takes precedence while it lasts; its three dismissals still justify it");
        signals.Should().OnlyContain(x => x.SuppressedUntil == until);
        SignalEpisodePlanner.Plan(SignalWriter.LaneDecide, 14, s.Clock.GetUtcNow(), [Hit(MatterId, Guid.NewGuid())], signals).Suppressed.Should().Be(1);

        var applied = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        (applied.DismissalsCounted, applied.SuppressedUntil).Should().Be((2, (DateTime?)until), "the count is the trailing 30 days; the date is the stamp's own");
        s.StampedIds.Should().BeEmpty("the immediate apply keeps it too");

        var body = (await Route(open, s, Caller(s, Ok(SignalRow()), Ok(new { createdon = "x" })))).Should().BeOfType<Ok<SuppressionResponse>>().Subject.Value!;
        (body.DismissalCount, body.SuppressedUntil).Should().Be((2, (DateTime?)until), "the route keeps showing the date");

        s.Clock.Advance(TimeSpan.FromDays(17)); // one day past the stamp
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default)).SuppressedUntil.Should().BeNull("it lapses on its own");
        s.StampedIds.Should().BeEmpty("a lapsed stamp is not live: it is neither lowered nor written again");
    }

    [Fact]
    public async Task R2F1_ThreeDismissalsFurtherThan30DaysApart_JustifyNoStamp()
    {
        var s = new Store();
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 45);
        s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 20);
        var latest = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 10);
        s.Stamp(latest, Now.UtcDateTime.AddDays(20)); // never justified: 45 and 10 days ago are 35 days apart

        await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        s.Signals.Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null, "three dismissals must fall within 30 days of each other");
    }

    [Fact]
    public async Task R2N1_ACrossWallStamp_OnAnOrdinaryPartition_WhoseLatestDismissalDoesNotCount_IsClearedNightly()
    {
        var s = new Store();
        // Family A: three counted dismissals. The ordinary partition: two counted and, latest, one that never counts.
        var family = new[] { 5, 4, 3 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, secure: true)).ToList();
        // The family's rows closed in the same second their record was decided, so ITS partition looks settled to any rule (resolved-on
        // or decided-on): only the ordinary partition's own count can pull this grain into the exact assessment.
        foreach (var (id, d) in family.Zip(new[] { 5, 4, 3 })) s.Signals.Single(x => x.Id == id)["sprk_resolvedon"] = Now.UtcDateTime.AddDays(-d);
        var readable = new List<Guid>
        {
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 5, secure: false),
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 4, secure: false),
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false, reason: WrongMatter),
            s.Open(MatterId, Guid.NewGuid(), secure: false),
        };
        var preFix = Now.UtcDateTime.AddDays(-3).AddDays(30); // the pre-#1661 whole-pairing stamp: family A's latest + 30
        foreach (var id in s.Signals.Select(x => x.Id).ToList()) s.Stamp(id, preFix);

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0);
        s.Signals.Where(x => readable.Contains(x.Id)).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == null,
            "two counted dismissals justify nothing; the non-counting one and the hidden family's three never help");
        signals.Where(x => readable.Contains(x.Id)).Should().OnlyContain(x => x.SuppressedUntil == null, "the planner sees the withdrawal this pass");
        s.Signals.Where(x => family.Contains(x.Id)).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == preFix);
        (await s.Service.ApplyAsync(PolicyId, Code, Matter(), default)).DismissalsCounted.Should().Be(2);
    }

    [Fact]
    public async Task R2FT2_AConditionalLowerThatThrows_IsACountedFailure_NotAChangedSinceCountedSkip()
    {
        // uac-r2 F-T2: only a row-version MISMATCH is the benign "changed since it was counted" case (K6). Any other fault of the
        // conditional lower is a failure: counted, the stamp not written, the planner still handed the corrected (cleared) stamp.
        var s = new Store();
        var family = new[] { 5, 4, 3 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, secure: true)).ToList();
        foreach (var (id, d) in family.Zip(new[] { 5, 4, 3 })) s.Signals.Single(x => x.Id == id)["sprk_resolvedon"] = Now.UtcDateTime.AddDays(-d);
        var readable = new List<Guid>
        {
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 5, secure: false),
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 4, secure: false),
            s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false, reason: WrongMatter),
            s.Open(MatterId, Guid.NewGuid(), secure: false),
        };
        var preFix = Now.UtcDateTime.AddDays(-3).AddDays(30); // the N1 cross-wall stamp
        foreach (var id in s.Signals.Select(x => x.Id).ToList()) s.Stamp(id, preFix);
        s.FailWrites = true; // every Dataverse update throws, the conditional (IfRowVersionMatches) lower included

        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(1, "a thrown conditional lower is a failure, not a row that changed since it was counted");
        signals.Where(x => readable.Contains(x.Id)).Should().OnlyContain(x => x.SuppressedUntil == null, "the planner still sees the withdrawal this pass");
        s.StampedIds.Should().BeEmpty("the throwing write stored nothing");
        s.Signals.Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == preFix, "no stored stamp changed");
    }

    [Fact]
    public async Task R2K6_ALower_IsConditionalOnTheRowVersion_ARowChangedSinceItWasCountedIsNotLowered()
    {
        var s = new Store();
        new[] { 3, 2, 1 }.Select(d => s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: d, secure: true)).ToList();
        var readable = s.Dismiss(MatterId, Guid.NewGuid(), daysAgo: 1, secure: false);
        var readableOpen = s.Open(MatterId, Guid.NewGuid(), secure: false);
        var preFix = Now.UtcDateTime.AddDays(-1).AddDays(30);
        foreach (var id in s.Signals.Select(x => x.Id).ToList()) s.Stamp(id, preFix);
        s.ChangeBeforeConditionalWrite.Add(readableOpen); // e.g. the decision route stamped it again after the count

        var (_, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default);

        failures.Should().Be(0, "a row that changed since it was read is the next pass's to count, not a failure");
        s.Signals.Single(x => x.Id == readable).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().BeNull();
        s.Signals.Single(x => x.Id == readableOpen).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().Be(preFix,
            "a lower is never written over a row another writer changed after the count");
    }

    // =====================================================================================
    // Ontology task 144 (uac-r2 #1746 K7, and a characterisation of K4): a Signal registered with the core-ancestor stamp reaches a
    // project or work-assignment core through its own sprk_regarding{project,workassignment} stamp, where the typed lookup it lacks
    // (audit F5) never could.
    // =====================================================================================

    private static readonly Guid SecureWorkAssignmentId = Guid.Parse("5ec0be00-0000-4000-8000-0000000000c1");

    public static TheoryData<string, string> ProjectAndWorkAssignmentCores => new()
    {
        { "sprk_project", "sprk_regardingproject" },
        { "sprk_workassignment", "sprk_regardingworkassignment" },
    };

    private static (Guid Core, Guid Type) SecureCore(Store s, string table)
    {
        if (table == "sprk_project") return (Store.ProjectA, ProjectType);
        s.SecureWorkAssignment(SecureWorkAssignmentId);
        return (SecureWorkAssignmentId, WorkAssignmentType);
    }

    [Theory]
    [MemberData(nameof(ProjectAndWorkAssignmentCores))]
    public async Task Characterisation_AStampOnTheSignal_IsALineageLookupTheWalkFollows_ToAProjectOrWorkAssignmentCore(string coreTable, string stampColumn)
    {
        // CHARACTERISATION (review F3: not acceptance-7 evidence). The stamp is written here by hand; it pins only that the partition
        // walk follows sprk_regarding{project,workassignment} on a Signal, so that a stamp the restamper writes (proven in
        // OntologyStampedChildRestampTests and SecureChildOwnershipAiToolTests.OntologyRestamp) moves a Secure-owned Signal out of the
        // undetermined bucket.
        var s = new Store();
        var (core, type) = SecureCore(s, coreTable);
        var subject = Guid.NewGuid();
        // The subject does not reach the root: an ordinary team's communication filed under nothing the walk can follow (#1746 K4).
        s.Subject(subject);
        var signal = s.Dismiss(core, subject, daysAgo: 1, coreType: type, secure: true);

        (await s.Service.PartitionsOfSignalsAsync([signal], default)).Should().Equal([SuppressionPartition.Undetermined],
            "before the stamp: a Secure-owned Signal with no secure root found is undetermined");

        s.Signals.Single(x => x.Id == signal)[stampColumn] = new EntityReference(coreTable, core); // the restamper's write

        (await s.Service.PartitionsOfSignalsAsync([signal], default)).Should().Equal([SuppressionPartition.Secure([(coreTable, core)])],
            "the stamp reaches the secure core, so the Signal is counted and muted in its own family");
    }

    [Theory]
    [MemberData(nameof(ProjectAndWorkAssignmentCores))]
    public async Task K7_AHitOnASecureProjectOrWorkAssignmentCore_IsPlacedInItsSecurePartition_NeverOrdinary(string coreTable, string _)
    {
        var s = new Store();
        var (core, type) = SecureCore(s, coreTable);
        var subject = Guid.NewGuid();
        s.Subject(subject); // an ordinary team's subject that does not reach the core: before 144 the hit was Ordinary (#1746 K7)
        var hit = new SignalHit("sprk_communication", subject, "A communication", null, new SignalCoreRecord(coreTable, core, type, null),
            null, null, new Dictionary<string, object?>(), []);

        var placed = await s.Service.PartitionsOfHitsAsync(Code, [hit], default);

        placed.Should().NotBeNull();
        placed![("sprk_communication", subject)].Should().Be(SuppressionPartition.Secure([(coreTable, core)]),
            "the hit's Signal will carry the core's stamp, so an ordinary partition's stamp never mutes it");
    }

    [Fact]
    public async Task K7_AHitOnAnOrdinaryProjectCore_StaysOrdinary()
    {
        // Must-not-fire control: the stamp column names an ordinary core, so nothing moves to a secure partition.
        var s = new Store();
        var subject = Guid.NewGuid();
        s.Subject(subject);
        var hit = new SignalHit("sprk_communication", subject, "A communication", null, new SignalCoreRecord("sprk_project", ProjectId, ProjectType, null),
            null, null, new Dictionary<string, object?>(), []);
        s.OrdinaryProject(ProjectId);

        var placed = await s.Service.PartitionsOfHitsAsync(Code, [hit], default);

        placed![("sprk_communication", subject)].Should().Be(SuppressionPartition.Ordinary);
    }

    // =====================================================================================
    // Fixtures
    // =====================================================================================

    private static SuppressionGrain Matter() => SuppressionGrain.ForCore(MatterType, MatterId);

    // =====================================================================================
    // Task 145 (D-123, uac-r2 #1747 finding e): witness detail never crosses a secure-family wall
    // =====================================================================================

    private const string WitnessSubject = "Rate increase from Firm LLP";
    private const string WitnessSender = "partner@firm.example";

    /// <summary>A matter hit (subject and core: the matter) quoting one communication as its witness, as the evaluator builds it.</summary>
    private static SignalHit MatterHitQuoting(Guid matterId, Guid communicationId) =>
        new("sprk_matter", matterId, "Acme v Beta", "MAT-0001", new SignalCoreRecord("sprk_matter", matterId, MatterType, null),
            null, null, new Dictionary<string, object?>(),
            [
                new SignalEvidenceRef("communication", $"sprk_communication:{communicationId:D}", "Fact")
                {
                    Text = WitnessSubject, Source = WitnessSender, AsOf = "2026-10-05T09:15:00Z", Clause = "all[0]",
                },
            ]);

    /// <summary>A communication filed under <paramref name="matterId"/> and, when given, also under a secure project (another filing).</summary>
    private static Guid FiledCommunication(Store s, Guid matterId, Guid? secureProject = null, string owner = Store.OrdinaryTeam)
    {
        var id = Guid.NewGuid();
        s.Subject(id, family: secureProject, owner: owner);
        s.Others.Single(r => r.Id == id)["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        return id;
    }

    [Fact]
    public async Task T145_ACommunicationFiledToOrdinaryMAndSecureX_GivesMsSignalNoSubjectOrSender()
    {
        var s = new Store();
        var mixed = FiledCommunication(s, MatterId, secureProject: Store.ProjectA, owner: Store.SecureOwner);
        var hit = MatterHitQuoting(MatterId, mixed);

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        var line = evidence[("sprk_matter", MatterId)].Should().ContainSingle().Subject;
        line.Text.Should().BeNull("the communication is secure through project A; M's Signal is read by M's business unit");
        line.Source.Should().BeNull();
        line.Should().BeEquivalentTo(hit.Evidence[0] with { Text = null, Source = null },
            "the rest of the line (kind, ref, as-of, clause) stays: the planner's dated-evidence rule reads it (D-119)");
    }

    [Fact]
    public async Task T145_AWitnessFiledUnderSecureXButStillOwnedByAnOrdinaryTeam_IsInXsFamily_AndGivesNoDetail()
    {
        // Pass 2 K1: the re-own window. The communication is already filed under secure project X but uac-r2's reconciler has not yet
        // re-owned it to the Secure team. Its family comes from its ROOTS, not its owner: {X}, never Ordinary and never Undetermined. A
        // regression to an owner-based check would keep the detail.
        var s = new Store();
        var windowed = FiledCommunication(s, MatterId, secureProject: Store.ProjectA, owner: Store.OrdinaryTeam);
        var hit = MatterHitQuoting(MatterId, windowed);

        var partitions = await s.Service.PartitionsOfHitsAsync(Code, [Hit(MatterId, windowed)], default);
        var family = partitions![("sprk_communication", windowed)];
        family.IsUndetermined.Should().BeFalse("its secure root is known");
        family.Should().Be(SuppressionPartition.Secure([("sprk_project", Store.ProjectA)]), "the family is {X}, read from the roots");

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        var line = evidence[("sprk_matter", MatterId)].Should().ContainSingle().Subject;
        line.Text.Should().BeNull("an ordinary owner does not make a communication filed under secure X ordinary");
        line.Source.Should().BeNull();
    }

    [Fact]
    public async Task T145_APurelyOrdinaryCommunication_StillGivesItsSubjectAndSender()
    {
        // Must-not-fire control for the test above: the same shape with no secure filing keeps its detail.
        var s = new Store();
        var ordinary = FiledCommunication(s, MatterId);
        var hit = MatterHitQuoting(MatterId, ordinary);

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        evidence[("sprk_matter", MatterId)].Should().ContainSingle().Which.Should().Be(hit.Evidence[0]);
    }

    [Fact]
    public async Task T145_ASecureSignalQuotingAWitnessOfItsOwnFamily_KeepsTheDetail()
    {
        // Beyond the named cases (one line): proves the rule compares FAMILIES, not "secure witness → clear", so a secure matter's own
        // Signal (owned by the Secure team, read by the family) still shows what its own secure communication said.
        var s = new Store();
        var secureMatter = Guid.NewGuid();
        s.SecureMatter(secureMatter);
        var own = FiledCommunication(s, secureMatter, owner: Store.SecureOwner);
        var hit = MatterHitQuoting(secureMatter, own);

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        evidence[("sprk_matter", secureMatter)].Should().ContainSingle().Which.Text.Should().Be(WitnessSubject);
    }

    [Fact]
    public async Task T145_AWitnessWhoseFamilyIsUndetermined_CountsAsSecure_AndGivesNoDetail()
    {
        // Undetermined counts as secure (D-123): Secure-team-owned but filed under no secure root, so its family cannot be named.
        var s = new Store();
        var orphan = FiledCommunication(s, MatterId, owner: Store.SecureOwner);
        var hit = MatterHitQuoting(MatterId, orphan);

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        evidence[("sprk_matter", MatterId)].Should().ContainSingle().Which.Text.Should().BeNull();
    }

    [Fact]
    public async Task T145_ASignalAndAWitnessBothUndetermined_AreNeverTheSameFamily_AndGiveNoDetail()
    {
        // Review F3: two families that cannot be named are never equal, so Undetermined == Undetermined keeps no detail (D-123).
        var s = new Store();
        var subject = FiledCommunication(s, MatterId, owner: Store.SecureOwner);
        var witness = FiledCommunication(s, MatterId, owner: Store.SecureOwner);
        var hit = Hit(MatterId, subject) with { Evidence = MatterHitQuoting(MatterId, witness).Evidence };
        (await s.Service.PartitionsOfHitsAsync(Code, [hit], default))![("sprk_communication", subject)].IsUndetermined
            .Should().BeTrue("the precondition: the Signal's own family is Undetermined");

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        evidence[("sprk_communication", subject)].Should().ContainSingle().Which.Text.Should().BeNull();
    }

    [Fact]
    public async Task T145_WhenTheFamiliesCannotBeRead_EveryLineLosesItsDetail_FailClosed()
    {
        var s = new Store();
        var ordinary = FiledCommunication(s, MatterId);
        var hit = MatterHitQuoting(MatterId, ordinary);
        s.FailReads = true;

        var evidence = await s.Service.EvidenceWithinPartitionAsync(Code, [hit], default);

        var line = evidence[("sprk_matter", MatterId)].Should().ContainSingle().Subject;
        line.Text.Should().BeNull("a family that cannot be read is never assumed equal");
        line.Source.Should().BeNull();
        line.AsOf.Should().Be("2026-10-05T09:15:00Z", "the line itself is still written");
    }

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
        return (new SignalCoreRecordAccess(users.Object, entities.Object, Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(), NullLogger<SignalCoreRecordAccess>.Instance), users.Object);
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
    /// changes the answer), the roots and subjects uac-r2's REAL <see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/> walks
    /// (matters and projects with their owner team and <c>sprk_issecure</c>; communications and To Dos with their lookups and owner), and a
    /// writer organization service that records every stamp.
    /// </summary>
    private sealed class Store
    {
        public List<Entity> Signals { get; } = new();
        public List<Entity> Records { get; } = new();
        public List<Entity> Others { get; } = new();
        public List<Guid> StampedIds { get; } = new();
        public bool PolicyCounts { get; set; } = true;

        /// <summary>Task 152: the policy's <c>sprk_lane</c> (null: not set, as every earlier test reads it).</summary>
        public int? PolicyLane { get; set; }
        public bool FailWrites { get; set; }
        public int FailWriteNumber { get; set; }
        private int _writes;
        public bool FailReads { get; set; }

        /// <summary>uac-r2 K1 on ontology #1891: every query of this one table faults (the walker's batched parent read).</summary>
        public string? FailReadsOf { get; set; }

        /// <summary>The n-th <c>sprk_signal</c> query (a grain read) faults, like a Dataverse timeout (#1661 round 2 F3).</summary>
        public int FailSignalReadNumber { get; set; }
        private int _signalReads;

        /// <summary>Rows another writer changes between the count and the conditional lower (#1661 round 2 K6).</summary>
        public HashSet<Guid> ChangeBeforeConditionalWrite { get; } = new();

        public FakeTimeProvider Clock { get; } = new(Now);
        public int Queries { get; private set; }
        private readonly Dictionary<string, int> _queriesOf = new(StringComparer.Ordinal);

        public int QueriesOf(string entity) => _queriesOf.GetValueOrDefault(entity);

        // uac-r2's identification of the Secure Record owner team: the configured BU (default name), and in it a NAMED, non-default Owner team.
        public static readonly Guid SecureTeamId = Guid.Parse("5ec0be00-0000-4000-8000-000000000001");
        public static readonly Guid OperationsTeamId = Guid.Parse("0e1a7100-0000-4000-8000-000000000002");
        private static readonly Guid SecureBuId = Guid.Parse("5ec0be00-0000-4000-8000-0000000000b1");

        /// <summary>Two secure projects (two families) under the readable matters: owned by the Secure team, flagged secure.</summary>
        public static readonly Guid ProjectA = Guid.Parse("5ec0be00-0000-4000-8000-0000000000a1");
        public static readonly Guid ProjectB = Guid.Parse("5ec0be00-0000-4000-8000-0000000000a2");

        public const string SecureOwner = "secure";
        public const string OrdinaryTeam = "ordinary-team";
        public const string User = "user";

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
            var clock = Clock;
            var configuration = new ConfigurationBuilder().Build();
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_policy", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                {
                    var policy = new Entity("sprk_policy", id) { ["sprk_countstowardsuppression"] = PolicyCounts, ["sprk_policycode"] = Code };
                    if (PolicyLane is { } lane) policy["sprk_lane"] = new OptionSetValue(lane);
                    return policy;
                });
            entities.Setup(e => e.RetrieveAsync("sprk_signal", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                    FailReads ? throw new InvalidOperationException("dataverse is down") : Signals.Single(x => x.Id == id));
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => Run(q));

            var org = new Mock<IOrganizationServiceAsync2>();
            Task Write(Entity update)
            {
                if (FailWrites || (FailWriteNumber > 0 && ++_writes == FailWriteNumber)) throw new InvalidOperationException("Simulated Dataverse failure on the update.");
                update.Attributes.Keys.Should().Equal(["sprk_suppresseduntil"], "the stamp touches that one column and nothing else");
                StampedIds.Add(update.Id);
                var row = Signals.Single(x => x.Id == update.Id);
                row["sprk_suppresseduntil"] = update["sprk_suppresseduntil"];
                row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
                return Task.CompletedTask;
            }

            org.Setup(o => o.UpdateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Returns((Entity update, CancellationToken _) => Write(update));
            // A lower is conditional on the row version it was counted at (#1661 round 2 K6), as Dataverse applies IfRowVersionMatches.
            org.Setup(o => o.ExecuteAsync(It.IsAny<OrganizationRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (OrganizationRequest request, CancellationToken _) =>
                {
                    var update = request.Should().BeOfType<UpdateRequest>().Subject;
                    update.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
                    var row = Signals.Single(x => x.Id == update.Target.Id);
                    if (ChangeBeforeConditionalWrite.Remove(row.Id)) row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
                    if (update.Target.RowVersion != row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture))
                    {
                        throw new System.ServiceModel.FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060882), Message = "The version of the existing record doesn't match." },
                            "The version of the existing record doesn't match.");
                    }

                    await Write(update.Target);
                    return new UpdateResponse();
                });
            var writer = new OntologyWriterDataverseClient(() => org.Object, NullLogger<OntologyWriterDataverseClient>.Instance);

            // uac-r2's real synchronizer, scoped as in ExternalAccessModule; the walk reads through the same in-memory Dataverse.
            var services = new ServiceCollection();
            services.AddScoped(_ => new SecureChildShareSynchronizer(entities.Object, null!, null!, configuration, NullLogger<SecureChildShareSynchronizer>.Instance));
            var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
            Service = new SignalSuppressionService(entities.Object, writer, clock, configuration, scopes, NullLogger<SignalSuppressionService>.Instance);

            Others.Add(Root("sprk_matter", MatterId, secure: false));
            Others.Add(Root("sprk_matter", OtherMatterId, secure: false));
            Others.Add(Root("sprk_project", ProjectA, secure: true));
            Others.Add(Root("sprk_project", ProjectB, secure: true));
        }

        /// <summary>A secure matter (owned by the Secure team, flagged secure): a hit whose core record is this matter sits under it.</summary>
        public void SecureMatter(Guid id) => Others.Add(Root("sprk_matter", id, secure: true));

        /// <summary>Ontology task 144: a secure work assignment (owned by the Secure team, flagged secure).</summary>
        public void SecureWorkAssignment(Guid id) => Others.Add(Root("sprk_workassignment", id, secure: true));

        /// <summary>Ontology task 144: an ordinary project (user-owned, not flagged).</summary>
        public void OrdinaryProject(Guid id) => Others.Add(Root("sprk_project", id, secure: false));

        private static Entity Root(string table, Guid id, bool secure)
        {
            var row = new Entity(table, id) { ["sprk_issecure"] = secure };
            if (secure) row["owningteam"] = new EntityReference("team", SecureTeamId);
            else row["owninguser"] = new EntityReference("systemuser", Guid.NewGuid());
            return row;
        }

        /// <summary>A subject row (replacing any earlier one): filed under <paramref name="family"/> (a secure project) or under nothing, and
        /// owned by the Secure team, an ordinary team or a user.</summary>
        public void Subject(Guid id, string entity = "sprk_communication", Guid? family = null, string owner = OrdinaryTeam)
        {
            Others.RemoveAll(r => r.LogicalName == entity && r.Id == id);
            var row = new Entity(entity, id);
            switch (owner)
            {
                case SecureOwner: row["owningteam"] = new EntityReference("team", SecureTeamId); break;
                case OrdinaryTeam: row["owningteam"] = new EntityReference("team", OperationsTeamId); break;
                default: row["owninguser"] = new EntityReference("systemuser", Guid.NewGuid()); break;
            }

            if (family is { } project) row["sprk_regardingproject"] = new EntityReference("sprk_project", project);
            Others.Add(row);
        }

        public void OwnBy(Guid signalId, EntityReference owner) => Signals.Single(x => x.Id == signalId)["ownerid"] = owner;

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
            Guid? record = null, Guid? policy = null, Guid? coreType = null, int episode = 1, string subjectEntity = "sprk_communication", bool? secure = null,
            Guid? family = null) =>
            Add(core, subject, SignalWriter.ResolutionDismissed, daysAgo, record ?? Record(recordClass, reason, daysAgo), policy, coreType, episode, subjectEntity, secure, family);

        public Guid Closed(Guid core, int resolution, int daysAgo) =>
            Add(core, Guid.NewGuid(), resolution, daysAgo, null, null, null, 1, "sprk_communication");

        public Guid Open(Guid? core, Guid subject, bool? secure = null, string subjectEntity = "sprk_communication", Guid? family = null) =>
            Add(core, subject, null, 0, null, null, null, 1, subjectEntity, secure, family);

        public void Stamp(Guid signalId, DateTime until) => Signals.Single(x => x.Id == signalId)["sprk_suppresseduntil"] = until;

        public Guid SubjectOf(Guid signalId)
        {
            SignalWriter.TryParseDedupeKey(Signals.Single(x => x.Id == signalId).GetAttributeValue<string>("sprk_dedupekey"), out _, out _, out var id, out _);
            return id;
        }

        private Guid Add(
            Guid? core, Guid subject, int? resolution, int daysAgo, Guid? record, Guid? policy, Guid? coreType, int episode, string subjectEntity,
            bool? secure = null, Guid? family = null)
        {
            var id = Guid.NewGuid();
            var row = new Entity("sprk_signal", id)
            {
                ["sprk_policy"] = new EntityReference("sprk_policy", policy ?? PolicyId),
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(Code, subjectEntity, subject.ToString("D").ToLowerInvariant(), episode),
                ["sprk_signalstatus"] = new OptionSetValue(resolution is null ? SignalWriter.SignalStatusOpen : SignalWriter.SignalStatusResolved),
                // As SignalWriter writes it: the subject's regarding lookup (the walk's way up), and sprk_matter for a matter core.
                [SignalWriter.RegardingLookupByEntity[subjectEntity]] = new EntityReference(subjectEntity, subject),
                ["versionnumber"] = 1L,
            };
            // secure: null = no owner on the row (every pre-#1661 test); false = an ordinary team (Operations); true = the Secure Record owner team.
            if (secure is { } isSecure) row["ownerid"] = new EntityReference("team", isSecure ? SecureTeamId : OperationsTeamId);
            if (core is { } coreId)
            {
                row["sprk_corerecordid"] = coreId.ToString("D").ToLowerInvariant();
                row["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", coreType ?? MatterType);
                if ((coreType ?? MatterType) == MatterType) row["sprk_matter"] = new EntityReference("sprk_matter", coreId);
            }

            // The subject, once: a secure Signal's subject is filed under its family's project and owned by the Secure team; any other is an
            // ordinary team's row filed under nothing secure.
            if (!Others.Any(r => r.LogicalName == subjectEntity && r.Id == subject))
            {
                Subject(subject, subjectEntity, secure == true ? family ?? ProjectA : null, secure == true ? SecureOwner : OrdinaryTeam);
            }

            if (resolution is { } r)
            {
                row["sprk_resolutiontype"] = new OptionSetValue(r);
                // As the decision route writes them (#1661 round 2 F4): the Signals close first and the Decision Record is written after,
                // so its sprk_decidedon (Record: Now - daysAgo) is a few seconds LATER than the Signal's sprk_resolvedon.
                row["sprk_resolvedon"] = Now.UtcDateTime.AddDays(-daysAgo).AddSeconds(-5);
            }

            if (record is { } rec) row["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", rec);
            Signals.Add(row);
            return id;
        }

        /// <summary>The Signals as the nightly job reads them (policy-wide; keyed by the dedupe key; the row itself as the lineage input).</summary>
        public IReadOnlyList<ExistingSignal> Existing() => Signals
            .Where(x => x.GetAttributeValue<EntityReference>("sprk_policy").Id == PolicyId)
            .Select(x =>
            {
                SignalWriter.TryParseDedupeKey(x.GetAttributeValue<string>("sprk_dedupekey"), out _, out var entity, out var subject, out var episode);
                return new ExistingSignal(
                    x.Id, x.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture), entity, subject, episode,
                    x.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value,
                    x.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")?.Value,
                    x.GetAttributeValue<DateTime?>("sprk_resolvedon"),
                    x.GetAttributeValue<DateTime?>("sprk_suppresseduntil"),
                    null,
                    x.GetAttributeValue<EntityReference>("sprk_corerecordtype")?.Id,
                    Guid.TryParse(x.GetAttributeValue<string>("sprk_corerecordid"), out var core) ? core : null,
                    x.GetAttributeValue<EntityReference>("ownerid") ?? new EntityReference("systemuser", Guid.NewGuid()),
                    DecisionRecordId: x.GetAttributeValue<EntityReference>("sprk_decisionrecord")?.Id,
                    LineageRow: x);
            }).ToList();

        private EntityCollection Run(QueryExpression q)
        {
            if (FailReads) throw new InvalidOperationException("dataverse is down");
            if (q.EntityName == FailReadsOf) throw new InvalidOperationException($"{q.EntityName} cannot be read");
            if (q.EntityName == "sprk_signal" && ++_signalReads == FailSignalReadNumber) throw new TimeoutException("Dataverse timed out on a grain read.");
            Queries++;
            _queriesOf[q.EntityName] = QueriesOf(q.EntityName) + 1;
            var source = q.EntityName switch
            {
                "sprk_signal" => Signals,
                "sprk_decisionrecord" => Records,
                "businessunit" => SecureBusinessUnits(),
                "team" => CountTeamRead(),
                "sprk_matter" or "sprk_project" or "sprk_workassignment" or "sprk_communication" or "sprk_todo" or "sprk_event" =>
                    Others.Where(r => r.LogicalName == q.EntityName).ToList(),
                _ => throw new NotSupportedException(q.EntityName),
            };
            var result = new EntityCollection();
            foreach (var row in source.Where(r => Matches(r, q.Criteria))) result.Entities.Add(Projected(row, q.ColumnSet));
            return result;
        }

        /// <summary>The row as Dataverse returns it for <paramref name="columns"/>: only the asked columns (all only for AllColumns), so
        /// code that reads a column it did not ask for sees null, as it would live.</summary>
        private static Entity Projected(Entity row, ColumnSet columns)
        {
            if (columns.AllColumns) return row;
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var column in columns.Columns)
            {
                if (row.Contains(column)) copy[column] = row[column];
            }

            return copy;
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
                ConditionOperator.GreaterThan => actual is DateTime g && g > (DateTime)c.Values[0],
                _ => throw new NotSupportedException(c.Operator.ToString()),
            };
        }
    }
}
