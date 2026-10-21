using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology task 152 (owner D-145a, issue #1921): Do-lane suppression counts per person. A dismissal of one person's Do item never
/// suppresses another person's Do item; a person's own Do items on one core record still count together (D-11); an item that names no
/// individual is its own grain (D-39); the Decide lane is unchanged. The subjects are To Dos, whose person is <c>sprk_assignedto</c>
/// (the "mine" rule's column); each dismissal's decider is its Decision Record's <c>sprk_assignedtointernal</c>.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the real service and planner over the in-memory store.</remarks>
public partial class SignalSuppressionTests
{
    private static readonly Guid Bob = Guid.Parse("15200000-0000-4000-8000-0000000000b0");
    private static readonly Guid Alice = Guid.Parse("15200000-0000-4000-8000-0000000000a0");

    [Fact]
    public async Task T152_BobsThreeDismissals_NeverMuteAlicesDoItem_OnTheSameMatter()
    {
        var s = BobDismissedThreeOfHisOwn();
        var aliceOpen = OpenDo(s, MatterId, Alice);
        var aliceNext = DoHit(MatterId, AssignedTodo(s, Alice));

        var plan = await NightlyPlanAsync(s, aliceNext);

        s.StampedIds.Should().NotContain(aliceOpen, "Bob's dismissals are Bob's: Alice's open Do item stays on her worklist");
        plan.Creates.Should().ContainSingle("the nightly pass still raises Alice's Do item for the policy on the matter")
            .Which.Hit.SubjectId.Should().Be(aliceNext.SubjectId);
        var status = await StatusAsync(s, aliceOpen, callerContact: Alice);
        status!.SuppressedUntil.Should().BeNull("Alice is not told her item is suppressed");
        status.DismissalsCounted.Should().Be(0, "none of the counted dismissals is hers");
    }

    [Fact]
    public async Task T152_BobsDismissals_OfToDosReassignedToAliceSince_NeverMuteAlice()
    {
        // Bob leaves; his three dismissed To Dos are reassigned to Alice. The dismissals are still Bob's (his Decision Records).
        var s = BobDismissedThreeOfHisOwn();
        foreach (var signal in s.Signals.ToList()) AssignTo(s, s.SubjectOf(signal.Id), Alice);
        var aliceNext = DoHit(MatterId, AssignedTodo(s, Alice));

        var plan = await NightlyPlanAsync(s, aliceNext);

        s.StampedIds.Should().BeEmpty("no dismissal Alice decided: nothing on her Do items is stamped");
        plan.Creates.Should().ContainSingle().Which.Hit.SubjectId.Should().Be(aliceNext.SubjectId);
    }

    [Fact]
    public async Task T152_BobsNextDoItem_OnTheMatter_IsSuppressed_D11ForOnePerson()
    {
        var s = BobDismissedThreeOfHisOwn();
        var bobOpen = OpenDo(s, MatterId, Bob);
        var bobNext = DoHit(MatterId, AssignedTodo(s, Bob));

        var plan = await NightlyPlanAsync(s, bobNext);

        var until = Now.UtcDateTime.AddDays(-1).AddDays(30);
        s.Signals.Single(x => x.Id == bobOpen).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().Be(until);
        plan.Creates.Should().BeEmpty("three of Bob's own dismissals on the matter suppress his next Do item for the policy");
        plan.Suppressed.Should().Be(1);
        // A colleague's Do item on the matter: her holder (…a0) sorts before Bob's (…b0), so Bob's count must be read from his own
        // holder, not the partition's first one (review pass 2, F3-2).
        OpenDo(s, MatterId, Alice);
        var status = await StatusAsync(s, bobOpen, callerContact: Bob);
        status!.DismissalsCounted.Should().Be(3);
        status.SuppressedUntil.Should().Be(until);
        (await StatusAsync(s, bobOpen, callerContact: Alice))!.DismissalsCounted.Should().Be(0,
            "a Do item's count is reported only to its own person (D-141)");
    }

    [Fact]
    public async Task T152_TheCommitTimePass_SplitsTheDoLaneByPerson_Too()
    {
        // Review pass 2, F3-1: the decision route's ApplyAsync (commit time), not only the nightly pass, counts a Do-lane grain per person.
        var s = BobDismissedThreeOfHisOwn();
        var aliceOpen = OpenDo(s, MatterId, Alice);
        var bobOpen = OpenDo(s, MatterId, Bob);

        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        s.StampedIds.Should().NotContain(aliceOpen, "Bob's dismissals never mute Alice's Do item at commit time either");
        s.StampedIds.Should().Contain(bobOpen, "Bob's own open Do item on the matter is suppressed by his three dismissals");
    }

    [Fact]
    public async Task T152_ADoItemWithNoIndividual_IsCountedPerItem_D39()
    {
        var s = LaneStore(SignalWriter.LaneDo);
        // Three DIFFERENT unassigned To Dos on the matter, each dismissed once: per item, none reaches the threshold.
        for (var i = 1; i <= 3; i++) DismissDo(s, MatterId, person: null, daysAgo: i, decider: Bob);
        var unassignedOpen = OpenDo(s, MatterId, person: null);
        // ONE unassigned To Do whose three episodes were dismissed: its own grain reaches it.
        var item = Guid.NewGuid();
        for (var episode = 1; episode <= 3; episode++) DismissDo(s, MatterId, person: null, daysAgo: episode, decider: Bob, todo: item, episode: episode);

        var plan = await NightlyPlanAsync(s, DoHit(MatterId, item), DoHit(MatterId, AssignedTodo(s, person: null)));

        s.StampedIds.Should().NotContain(unassignedOpen, "unassigned items do not pool on the matter");
        plan.Suppressed.Should().Be(1, "the item dismissed three times is suppressed on its own");
        plan.Creates.Should().ContainSingle("another unassigned item on the matter is raised");
    }

    [Fact]
    public async Task T152_TheDecideLane_StillCountsPerPolicyAndCoreRecord()
    {
        var s = BobDismissedThreeOfHisOwn(SignalWriter.LaneDecide);
        var aliceOpen = OpenDo(s, MatterId, Alice);

        var status = await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);

        status.DismissalsCounted.Should().Be(3, "a Decide-lane grain is (policy, core record), whoever the subjects name");
        s.StampedIds.Should().Contain(aliceOpen);
    }

    [Fact]
    public async Task T152_APreExistingCoreStamp_OnADoItem_StopsMutingOtherPeople_AfterOneNightlyPass()
    {
        // Written before task 152 at (policy, matter): the same stamp on every Do item of the policy on the matter.
        var s = BobDismissedThreeOfHisOwn();
        var aliceOpen = OpenDo(s, MatterId, Alice);
        var until = Now.UtcDateTime.AddDays(-1).AddDays(30);
        foreach (var row in s.Signals) row["sprk_suppresseduntil"] = until;
        var bobRows = s.Signals.Where(x => x.Id != aliceOpen).Select(x => x.Id).ToList();

        var plan = await NightlyPlanAsync(s, DoHit(MatterId, s.SubjectOf(aliceOpen)), DoHit(MatterId, AssignedTodo(s, Bob)));

        s.Signals.Single(x => x.Id == aliceOpen).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().BeNull(
            "Alice's own dismissals justify nothing: the old stamp is withdrawn from her item");
        s.StampedIds.Should().Equal([aliceOpen], "Bob's rows keep the stamp his own dismissals justify and are not rewritten (D-61)");
        s.Signals.Where(x => bobRows.Contains(x.Id)).Should().OnlyContain(x => x.GetAttributeValue<DateTime?>("sprk_suppresseduntil") == until);
        plan.Suppressed.Should().Be(1, "Bob's next Do item is still suppressed");
        plan.Creates.Should().BeEmpty("Alice's open item is unchanged (not re-raised, not muted)");
        plan.Unchanged.Should().Be(1);
        s.StampedIds.Clear();
        await NightlyPlanAsync(s);
        s.StampedIds.Should().BeEmpty("a second pass writes nothing");
    }

    [Theory]
    [InlineData("sprk_workassignment")] // the assigner (D-44): "mine", not the item's person
    [InlineData("sprk_event")]          // the creator: "mine", not the item's person
    public async Task T152_ACallerWhoIsNotTheItemsPerson_IsToldTheirDismissalCountsTowardNothing(string table)
    {
        // Review F1: Alice's item; Bob may see and dismiss it, but his dismissal of it counts toward no one, so the wizard must not
        // tell him "1 of 3".
        var s = BobDismissedThreeOfHisOwn();
        var subject = Guid.NewGuid();
        var anchor = s.Open(MatterId, subject, subjectEntity: table);
        var row = s.Others.Single(r => r.LogicalName == table && r.Id == subject);
        row[SignalReadService.DoLaneMineRules[table].PersonColumn] = new EntityReference("contact", Alice);
        row["sprk_issecure"] = false; // a work assignment is a lineage root: an ordinary one, as the store's own roots are

        var status = await StatusAsync(s, anchor, callerContact: Bob);

        status.Should().Be(new SuppressionStatus(0, 0, null, false), "the caller's dismissal of another person's Do item counts toward nothing");
        (await StatusAsync(s, anchor, callerContact: Alice))!.PolicyCountsTowardSuppression.Should().BeTrue("the item's own person's does");
    }

    [Fact]
    public async Task T152_ADoSignalOfAPolicyNoLongerInTheDoLane_IsNotAnsweredFromTheUnsplitCount()
    {
        // Review K3: the policy was switched to the Decide lane; its Signals were raised in the Do lane and still are Do items to the
        // route's gate. Bob's three dismissals now pool at (policy, matter); Alice must not be shown them.
        var s = BobDismissedThreeOfHisOwn(SignalWriter.LaneDecide);
        var aliceOpen = OpenDo(s, MatterId, Alice);
        foreach (var row in s.Signals) row["sprk_lane"] = new OptionSetValue(SignalWriter.LaneDo);

        var status = await StatusAsync(s, aliceOpen, callerContact: Alice);

        status.Should().Be(new SuppressionStatus(0, 0, null, false));
    }

    [Fact]
    public async Task T152_BobsThreeSecureDoDismissals_NeverMuteHisOrdinaryDoItem_OnTheSameMatter_D123()
    {
        // uac-r2 review F3: the secure wall (D-123) holds inside the Do lane too. Bob's three dismissals are of To Dos under secure
        // project A (Secure-owned Signals); his open Do item on the same matter is ordinary. Same person, same matter, other partition.
        var s = LaneStore(SignalWriter.LaneDo);
        foreach (var days in new[] { 6, 3, 1 }) DismissDo(s, MatterId, Bob, days, secure: true);
        var ordinaryOpen = OpenDo(s, MatterId, Bob, secure: false);
        var ordinaryNext = DoHit(MatterId, AssignedTodo(s, Bob));

        await s.Service.ApplyAsync(PolicyId, Code, Matter(), default);
        s.StampedIds.Should().HaveCount(3, "the three secure dismissals are stamped in their own family")
            .And.NotContain(ordinaryOpen, "commit time: a secure family's count never reaches an ordinary row");

        // Service level: the nightly suppression pass and the planner, as NightlyPlanAsync drives them (the job's World cannot reach a
        // worked grain, see the job tests).
        s.StampedIds.Clear();
        var plan = await NightlyPlanAsync(s, ordinaryNext);
        s.StampedIds.Should().NotContain(ordinaryOpen, "the nightly pass leaves the ordinary row alone too");
        s.Signals.Single(x => x.Id == ordinaryOpen).GetAttributeValue<DateTime?>("sprk_suppresseduntil").Should().BeNull();
        plan.Creates.Should().ContainSingle("Bob's next ordinary Do item on the matter is raised")
            .Which.Hit.SubjectId.Should().Be(ordinaryNext.SubjectId);

        var status = await StatusAsync(s, ordinaryOpen, callerContact: Bob);
        status!.DismissalsCounted.Should().Be(0, "none of Bob's ordinary-partition Do items was dismissed");
        status.SuppressedUntil.Should().BeNull();
    }

    // ---- Fixtures --------------------------------------------------------------------------------------------------------------

    private static Store LaneStore(int lane) => new() { PolicyLane = lane };

    /// <summary>Bob dismisses three of his own Do items (three To Dos assigned to him) for the policy on the matter, 6, 3 and 1 days ago.</summary>
    private static Store BobDismissedThreeOfHisOwn(int lane = SignalWriter.LaneDo)
    {
        var s = LaneStore(lane);
        foreach (var days in new[] { 6, 3, 1 }) DismissDo(s, MatterId, Bob, days);
        return s;
    }

    /// <summary>A dismissed Do-lane Signal of a To Do for <paramref name="person"/>, whose Decision Record names <paramref name="decider"/>
    /// (default: the person) as the one who decided.</summary>
    private static Guid DismissDo(
        Store s, Guid core, Guid? person, int daysAgo, Guid? decider = null, Guid? todo = null, int episode = 1, bool? secure = null)
    {
        var subject = todo ?? Guid.NewGuid();
        var id = s.Dismiss(core, subject, daysAgo, episode: episode, subjectEntity: "sprk_todo", secure: secure);
        AssignTo(s, subject, person);
        var record = s.Signals.Single(x => x.Id == id).GetAttributeValue<EntityReference>("sprk_decisionrecord").Id;
        if ((decider ?? person) is { } who) s.Records.Single(r => r.Id == record)["sprk_assignedtointernal"] = new EntityReference("contact", who);
        return id;
    }

    private static Guid OpenDo(Store s, Guid core, Guid? person, bool? secure = null)
    {
        var subject = Guid.NewGuid();
        var id = s.Open(core, subject, subjectEntity: "sprk_todo", secure: secure);
        AssignTo(s, subject, person);
        return id;
    }

    /// <summary>A To Do with no Signal yet, for <paramref name="person"/> (none: unassigned).</summary>
    private static Guid AssignedTodo(Store s, Guid? person)
    {
        var subject = Guid.NewGuid();
        s.Subject(subject, "sprk_todo");
        AssignTo(s, subject, person);
        return subject;
    }

    private static void AssignTo(Store s, Guid todo, Guid? person)
    {
        var row = s.Others.Single(r => r.LogicalName == "sprk_todo" && r.Id == todo);
        if (person is { } contact) row["sprk_assignedto"] = new EntityReference("contact", contact);
    }

    private static SignalHit DoHit(Guid core, Guid todo) =>
        new("sprk_todo", todo, "A To Do", null, new SignalCoreRecord("sprk_matter", core, MatterType, null),
            null, null, new Dictionary<string, object?>(), []);

    /// <summary>The nightly job's steps for a Do-lane policy: the suppression pass, then the hits' partitions and holders, then the planner.</summary>
    private static async Task<EpisodePlan> NightlyPlanAsync(Store s, params SignalHit[] hits)
    {
        var (signals, failures) = await s.Service.ReconcileAsync(PolicyId, Code, true, s.Existing(), default, doLane: true);
        failures.Should().Be(0);
        var stamped = SignalEpisodePlanner.HitsOnStampedGrains(hits, signals, Now);
        var partitions = await s.Service.PartitionsOfHitsAsync(Code, stamped, default);
        var holders = await s.Service.HoldersOfHitsAsync(Code, stamped, default);
        return SignalEpisodePlanner.Plan(SignalWriter.LaneDo, 14, Now, hits, signals, partitions, hitHolders: holders);
    }

    /// <summary>The count <c>GET /suppression</c> answers for <paramref name="signalId"/> to a caller whose linked contact is
    /// <paramref name="callerContact"/> (the caller's own read returns every Signal).</summary>
    private static Task<SuppressionStatus?> StatusAsync(Store s, Guid signalId, Guid callerContact) =>
        s.Service.GetStatusForSignalAsync(signalId, Caller(s, Ok(SignalRow()), Ok(new { })).Users, default, callerContact);
}
