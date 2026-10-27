// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194 (#2339, owner round 128 item 3; rule I-6) — the children of an ORDINARY record reassigned
//     to another business unit follow it: SecureChildReconciler.ReconcileOrdinaryParentMoveAsync and the
//     SecureChildReconciliationJob pass that drives it. Pins the must-fire move (grandchildren included, read back), the
//     must-not-fire cases (Secure-team-owned, user-owned, non-default team, already matching, switch off), the never-release
//     rule and the multi-parent primary-parent answer, and the document whose file follows in the same run.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL reconciler, resolver, job, relocator and container resolver over
// task 148's world (SecureChildShareWorld) and task 166's pointer world.

using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using R = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.RefileFileRelocationTests;
using T = Sprk.Bff.Api.Tests.DataMutation.DocumentContainer.DocumentContainerRelocatorTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

public class SecureChildOrdinaryParentMoveTests
{
    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";

    private static readonly Guid GeneralBu = SecureChildShareWorld.GeneralBu;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureBu = SecureChildShareWorld.SecureBu;

    /// <summary>Business unit B, where the matter was reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000194001");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000194002");

    /// <summary>An owner team in business unit A that is NOT its default team.</summary>
    private static readonly Guid NonDefaultTeamA = Guid.Parse("0a000000-0000-4000-8000-000000194003");

    /// <summary>Task 178's shape: another owner team in the Secure Record business unit.</summary>
    private static readonly Guid SecureFamilyTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000194004");

    private static readonly Guid MatterId = Guid.Parse("1b000000-0000-4000-8000-000000194010");
    private static readonly Guid EventId = Guid.Parse("1b000000-0000-4000-8000-000000194011");
    private static readonly Guid TodoId = Guid.Parse("1b000000-0000-4000-8000-000000194012");
    private static readonly Guid DocumentId = Guid.Parse("1b000000-0000-4000-8000-000000194013");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0)
        .Team(NonDefaultTeamA, GeneralBu, "Spaarke Paralegals", isDefault: false, teamType: 0)
        .Team(SecureFamilyTeam, SecureBu, "Secure Matter Owners", isDefault: false, teamType: 0);

    private readonly ConditionalOwnerWebApi _webApi;

    public SecureChildOrdinaryParentMoveTests() => _webApi = new ConditionalOwnerWebApi(() => _world);

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    /// <summary>The matter, ordinary, reassigned from business unit A (the general unit) to B: owned by B's default team.</summary>
    private void MatterMovedToB(Guid? id = null) =>
        _world.Add(Matter, id ?? MatterId, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false));

    /// <summary>A child row owned by <paramref name="owner"/>, filed through the lookups given (in this order).</summary>
    private void Row(string table, Guid id, EntityReference owner, params (string Column, string Target, Guid TargetId)[] lookups)
    {
        var columns = lookups.Select(l => (l.Column, (object)new EntityReference(l.Target, l.TargetId)))
            .Prepend((owner.LogicalName == "team" ? "owningteam" : "owninguser", owner))
            .Append(("versionnumber", (object)1L))
            .ToArray();
        _world.Add(table, id, columns);
    }

    private static EntityReference TeamRef(Guid id) => new("team", id);

    /// <summary>The event under the matter, a To Do under the event (a grandchild), a document under the matter — all in A.</summary>
    private void ChildrenStillInA()
    {
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam), ("sprk_regardingevent", "sprk_event", EventId));
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "194 must fire: a matter moved A to B — its default-team children AND grandchild are re-owned to B's default team, conditionally, read back")]
    public async Task ChildrenOfAMovedMatter_AreReownedToTheNewBusinessUnitsDefaultTeam()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        report.ChildrenReowned.Should().Be(3);
        report.OrdinaryWritesAttempted.Should().Be(3);
        foreach (var (table, id) in new[] { ("sprk_event", EventId), ("sprk_todo", TodoId), ("sprk_document", DocumentId) })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(TeamB), $"{table} follows its matter to B");
            report.Changes.Should().ContainSingle(c => c.Id == id).Which.Should().Be(
                new SecureChildRowChange(table, id, DataversePrincipalRef.Team(GeneralTeam), TeamB, SecureChildRowOutcome.Changed, null),
                "the previous owner is the reversal evidence");
        }

        _webApi.ConditionalWrites.Select(w => w.Id).Should().BeEquivalentTo(new[] { EventId, TodoId, DocumentId },
            "every move is the guarded If-Match re-own");
        _webApi.ConditionalWrites.Single(w => w.Id == TodoId).Should().Be(("sprk_todos", TodoId, "W/\"1\""));
    }

    [Fact(DisplayName = "194 must fire: descendants with NO core stamp, reached only through a lineage hop (a file version and a To Do under a document), follow the document to B")]
    public async Task DescendantsReachedOnlyThroughALineageHop_FollowTheirParent()
    {
        var fileVersion = Guid.NewGuid();
        var todoOnDocument = Guid.NewGuid();
        MatterMovedToB();
        Row("sprk_document", DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_fileversion", fileVersion, TeamRef(GeneralTeam), ("sprk_document", "sprk_document", DocumentId));
        Row("sprk_todo", todoOnDocument, TeamRef(GeneralTeam), ("sprk_regardingdocument", "sprk_document", DocumentId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_fileversion", fileVersion).Should().Be(DataversePrincipalRef.Team(TeamB),
            "a file version carries no core stamp; the walk reaches it through its document, and it follows the document's new unit");
        _world.OwnerOf("sprk_todo", todoOnDocument).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_document", DocumentId).Should().Be(DataversePrincipalRef.Team(TeamB));
        report.ChildrenReowned.Should().Be(3);
    }

    [Fact(DisplayName = "194 F1-b must fire: a budget's buckets follow the budget when its matter moves to B")]
    public async Task ABudgetsBuckets_FollowAnOrdinaryMove()
    {
        var (budget, bucket) = (Guid.NewGuid(), Guid.NewGuid());
        MatterMovedToB();
        Row("sprk_budget", budget, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_budgetbucket", bucket, TeamRef(GeneralTeam), ("sprk_budget", "sprk_budget", budget));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_budget", budget).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_budgetbucket", bucket).Should().Be(DataversePrincipalRef.Team(TeamB));
    }

    [Fact(DisplayName = "194 must fire: report-only plans every move, the grandchild through its planned parent, and writes nothing")]
    public async Task ReportOnly_PlansEveryMove_AndWritesNothing()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.ReportOnly, maxWrites: 200, CancellationToken.None);

        report.Changes.Where(c => c.Outcome == SecureChildRowOutcome.WouldChange).Select(c => c.Id)
            .Should().BeEquivalentTo(new[] { EventId, TodoId, DocumentId });
        report.Changes.Should().OnlyContain(c => c.TargetTeamId == TeamB);
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194: the write cap — past it nothing is written, the row is reported with the cap detail and the pass is incomplete")]
    public async Task PastTheWriteCap_NothingIsWritten_AndThePassIsIncomplete()
    {
        MatterMovedToB();
        ChildrenStillInA();

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 1, CancellationToken.None);

        report.OrdinaryWritesAttempted.Should().Be(1);
        _webApi.ConditionalWrites.Should().HaveCount(1);
        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete);
        report.Changes.Where(c => c.Outcome == SecureChildRowOutcome.Failed)
            .Should().NotBeEmpty().And.OnlyContain(c => c.Detail == SecureChildReconciler.OrdinaryMoveCapDetail);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Must not fire
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "194 must not fire / never release: a child owned in the Secure Record business unit is never decided or written, even when every parent is ordinary")]
    [InlineData("named")]
    [InlineData("family")]
    [InlineData("default")]
    public async Task ASecureTeamOwnedChild_IsNeverTouched(string which)
    {
        var owner = which switch
        {
            "named" => _world.OwnerTeam,
            "family" => SecureFamilyTeam,
            _ => SecureChildShareWorld.SecureDefaultTeam,
        };
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(owner), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(owner), "this pass never releases isolation");
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
        report.Changes.Should().BeEmpty("the secure pass owns it; this pass neither moves nor reports it");
        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
    }

    [Fact(DisplayName = "194 never release: a mixed tree — the ordinary children move, the Secure-team-owned child under them stays isolated")]
    public async Task AMixedTree_MovesOnlyTheOrdinaryRows()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(_world.OwnerTeam), ("sprk_regardingevent", "sprk_event", EventId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam));
        _world.OwnerWrites.Select(w => w.Id).Should().Equal(EventId);
        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
    }

    [Fact(DisplayName = "194 must not fire: an ordinary child the rule gives the Secure team (also filed under a secure project) is reported and left to the secure pass")]
    public async Task AChildTheRuleGivesTheSecureTeam_IsReported_NotMoved()
    {
        var secureProject = Guid.NewGuid();
        MatterMovedToB();
        _world.SecureRoot(Project, secureProject);
        Row("sprk_todo", TodoId, TeamRef(GeneralTeam),
            ("sprk_regardingmatter", Matter, MatterId), ("sprk_regardingproject", Project, secureProject));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "only the secure pass moves a row INTO isolation");
        report.Changes.Should().ContainSingle(c => c.Id == TodoId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == _world.OwnerTeam
            && c.Detail!.Contains("Secure Record owner team"));
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194 must not fire: a user-owned child is reported (its rule answer is in another business unit), never written")]
    public async Task AUserOwnedChild_IsReported_NotMoved()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, new EntityReference("systemuser", SecureChildShareWorld.SomeUser),
            ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle().Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.TargetTeamId == TeamB && c.Detail!.Contains("owned by a user"));
        report.Status.Should().Be(SecureChildReconcileStatus.Completed, "a reported row is not unfinished work");
    }

    [Fact(DisplayName = "194 must not fire: a child owned by a team that is not a business unit's default team is reported, never written")]
    public async Task ANonDefaultTeamOwnedChild_IsReported_NotMoved()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(NonDefaultTeamA), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(NonDefaultTeamA));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle().Which.Detail.Should().Contain("not a business unit's default team");
    }

    [Fact(DisplayName = "194 must not fire: children that already match their moved matter are AlreadyCorrect — nothing written, nothing listed")]
    public async Task ChildrenThatAlreadyMatch_AreNotWritten()
    {
        MatterMovedToB();
        Row("sprk_event", EventId, TeamRef(TeamB), ("sprk_regardingmatter", Matter, MatterId));
        Row("sprk_todo", TodoId, TeamRef(TeamB), ("sprk_regardingevent", "sprk_event", EventId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed);
        report.Tables.Sum(t => t.AlreadyCorrect).Should().Be(2);
        report.Changes.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Theory(DisplayName = "194 must not fire: a secure, isolated or unreadable-flag root is NotApplicable — nothing read below it, nothing written")]
    [InlineData("secure")]
    [InlineData("flagged-not-isolated")]
    [InlineData("masked-flag")]
    public async Task ASecureRoot_IsNotApplicable(string kind)
    {
        _ = kind switch
        {
            "secure" => _world.SecureRoot(Matter, MatterId),
            "flagged-not-isolated" => _world.FlaggedNotIsolatedRoot(Matter, MatterId),
            _ => _world.MaskedFlagNotIsolatedRoot(Matter, MatterId),
        };
        Row("sprk_event", EventId, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.NotApplicable);
        _world.OwnerWrites.Should().BeEmpty();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Multi-parent: the resolver's primary parent decides
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "194 multi-parent: a child filed under the moved matter (B) and a project in A takes the resolver's primary-parent answer")]
    [InlineData(true)]  // the matter is the first lookup: the primary parent — the child moves to B
    [InlineData(false)] // the project is the first lookup: the primary parent — the child stays in A
    public async Task AMultiParentChild_TakesThePrimaryParentsAnswer(bool matterFirst)
    {
        var projectInA = Guid.NewGuid();
        MatterMovedToB();
        _world.OrdinaryRoot(Project, projectInA);
        var lookups = new[] { ("sprk_regardingmatter", Matter, MatterId), ("sprk_regardingproject", Project, projectInA) };
        Row("sprk_event", EventId, TeamRef(GeneralTeam), matterFirst ? lookups : lookups.Reverse().ToArray());
        var expected = await new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
                SecureChildShareWorld.EntitiesOver(() => _world).Object, SecureChildShareWorld.Configuration(),
                NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance)
            .ResolveOwnerAsync(new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext
            {
                Parents = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ParentsOf(
                    _world.Retrieve("sprk_event", EventId, null)).ToArray(),
                WhenUnfiled = Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator,
            }, CancellationToken.None);

        await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        expected.OwningTeamId.Should().Be(matterFirst ? TeamB : GeneralTeam, "the resolver's primary parent is the first lookup");
        _world.OwnerOf("sprk_event", EventId).Should().Be(DataversePrincipalRef.Team(expected.OwningTeamId!.Value));
        _world.OwnerWrites.Should().HaveCount(matterFirst ? 1 : 0);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The job: the switch, the window, and the document's file in the same run
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "194: the switch defaults to off, under SecureChild:Reconciliation:OrdinaryMovesWritesEnabled; the move cap is 200")]
    public void TheSwitchesAndCaps()
    {
        SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey.Should().Be("SecureChild:Reconciliation:OrdinaryMovesWritesEnabled");
        SecureChildReconciliationJob.MaxOrdinaryMovesPerRunConfigKey.Should().Be("SecureChild:Reconciliation:MaxOrdinaryMovesPerRun");
        SecureChildReconciliationJob.DefaultMaxOrdinaryMovesPerRun.Should().Be(200);
    }

    [Fact(DisplayName = "194 must not fire: with the switch off the job writes nothing and reports the planned moves")]
    public async Task TheJob_WithTheSwitchOff_ReportsAndWritesNothing()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: null);

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        ordinary.GetProperty("rootsListed").GetInt32().Should().Be(1);
        ordinary.GetProperty("wouldMove").GetInt32().Should().Be(3);
        ordinary.GetProperty("moved").GetInt32().Should().Be(0);
        _world.OwnerWrites.Should().BeEmpty();
        _webApi.ConditionalWrites.Should().BeEmpty();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 must fire: with the switch on the job moves the children of a reassigned matter; an unchanged matter is not listed")]
    public async Task TheJob_WithTheSwitchOn_MovesTheChildren()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var untouchedMatter = Guid.NewGuid();
        var untouchedEvent = Guid.NewGuid();
        MatterMovedToB(untouchedMatter); // reassigned long ago: outside the window
        Row("sprk_event", untouchedEvent, TeamRef(GeneralTeam), ("sprk_regardingmatter", Matter, untouchedMatter));
        var harness = new JobHarness(_world, _webApi);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeWrite);
        ordinary.GetProperty("rootsListed").GetInt32().Should().Be(1);
        ordinary.GetProperty("moved").GetInt32().Should().Be(3);
        _world.OwnerOf("sprk_todo", TodoId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerOf("sprk_event", untouchedEvent).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Fact(DisplayName = "194 must not fire: the recent-changes emergency stop also stops the ordinary moves")]
    public async Task TheEmergencyStop_StopsTheOrdinaryMoves()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (_, ordinary) = await harness.RunAsync(ordinaryWrites: "true", recentWrites: false);

        ordinary.GetProperty("mode").GetString().Should().Be(SecureChildReconciliationJob.ModeReportOnly);
        _world.OwnerWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "194 must fire: a capped run carries the rest and the next run finishes them")]
    public async Task ACappedRun_CarriesTheRest()
    {
        MatterMovedToB();
        _world.Modified(Matter, MatterId, DateTime.UtcNow);
        ChildrenStillInA();
        var harness = new JobHarness(_world, _webApi);

        var (first, run1) = await harness.RunAsync(ordinaryWrites: "true", maxMoves: "2");
        _world.Modified(Matter, MatterId, SecureChildShareWorld.RecordsLastModified); // the next run finds it only through the carry
        var (second, run2) = await harness.RunAsync(ordinaryWrites: "true", maxMoves: "2");

        run1.GetProperty("moved").GetInt32().Should().Be(2);
        run1.GetProperty("capReached").GetBoolean().Should().BeTrue();
        run1.GetProperty("carriedRoots").GetInt32().Should().Be(1);
        first.Success.Should().BeTrue("a capped root is carried, not failed: " + first.ErrorMessage);
        run2.GetProperty("retriedRoots").GetInt32().Should().Be(1);
        run2.GetProperty("moved").GetInt32().Should().Be(1);
        run2.GetProperty("carriedRoots").GetInt32().Should().Be(0);
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact(DisplayName = "194 must fire: a re-owned document's file moves to the new business unit's container in the same run (the re-file pass)")]
    public async Task TheJob_ReownsTheDocument_AndItsFileFollows()
    {
        // The pointer world: the document is filed under the plain matter, whose container (derived from its owning business
        // unit) is CustomerA1Container; its file still sits in CustomerBContainer, the container of the unit it left.
        var pointer = R.Env();
        pointer.Rows[("sprk_document", T.DocumentId)] = R.Doc(T.DocumentId, T.PlainMatter, T.CustomerBContainer);
        var rig = new T.Rig(pointer);

        // The job's world: the same matter, reassigned to B; the document still owned by A's default team.
        _world.Add("businessunit", T.CustomerA, ("sprk_containerid", T.CustomerAContainer));
        MatterMovedToB(T.PlainMatter);
        _world.Modified(Matter, T.PlainMatter, DateTime.UtcNow);
        Row("sprk_document", T.DocumentId, TeamRef(GeneralTeam), ("sprk_matter", Matter, T.PlainMatter));
        _world.Set("sprk_document", T.DocumentId, "sprk_graphdriveid", T.CustomerBContainer);
        var harness = new JobHarness(_world, _webApi, rig);

        var (result, ordinary) = await harness.RunAsync(ordinaryWrites: "true");
        var refile = harness.Last.GetProperty("refileRelocations");

        ordinary.GetProperty("moved").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_document", T.DocumentId).Should().Be(DataversePrincipalRef.Team(TeamB));
        R.Ids(refile, "movedDocuments").Should().Equal(T.DocumentId);
        rig.DriveOf(T.DocumentId).Should().Be(T.CustomerA1Container);
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // Doubles
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The REAL job over this world, with the conditional Web API double (and, when given, task 166's relocator).</summary>
    private sealed class JobHarness
    {
        private readonly IConfigurationRoot _configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        private readonly SecureChildReconciliationJob _job;

        public JobHarness(SecureChildShareWorld world, ConditionalOwnerWebApi webApi, T.Rig? rig = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(SecureChildShareWorld.EntitiesOver(() => world).Object);
            services.AddScoped(_ => SecureChildShareWorld.ReconcilerOver(() => world, new FakeRecordShareTable(), webApi));
            services.AddScoped(_ => SecureChildShareWorld.SynchronizerOver(() => world, new FakeRecordShareTable()));
            if (rig is not null)
            {
                services.AddScoped(_ => rig.Relocator);
                services.AddScoped(_ => rig.Resolver);
            }

            services.AddSingleton(_ => SecureChildShareWorld.CoreAncestorsOver(() => world));
            var provider = services.BuildServiceProvider();
            foreach (var (key, value) in SecureChildShareWorld.Configuration().AsEnumerable())
                _configuration[key] = value;
            _job = new SecureChildReconciliationJob(
                provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, _configuration,
                NullLogger<SecureChildReconciliationJob>.Instance);
        }

        public JsonElement Last { get; private set; }

        public async Task<(JobRunResult Result, JsonElement Ordinary)> RunAsync(
            string? ordinaryWrites, bool recentWrites = true, string? maxMoves = null)
        {
            _configuration[SecureChildReconciliationJob.OrdinaryMovesWritesEnabledConfigKey] = ordinaryWrites;
            _configuration[SecureChildReconciliationJob.RecentChangesWritesEnabledConfigKey] = recentWrites ? null : "false";
            _configuration[SecureChildReconciliationJob.MaxOrdinaryMovesPerRunConfigKey] = maxMoves;
            var result = await _job.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                CancellationToken.None);
            using var doc = JsonDocument.Parse(result.ResultJson!);
            Last = doc.RootElement.Clone();
            return (result, Last.GetProperty("ordinaryMoves"));
        }
    }

    /// <summary>
    /// The conditional owner write of the Web API (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>) over the world's rows
    /// and their <c>versionnumber</c> (as task 183's tests answer it): a stale version is Dataverse's 412, a missing row its
    /// 404; a write that lands bumps the version and, as Dataverse does, the row's <c>modifiedon</c>.
    /// </summary>
    private sealed class ConditionalOwnerWebApi : DataverseWebApiClient
    {
        private readonly Func<SecureChildShareWorld> _world;

        public ConditionalOwnerWebApi(Func<SecureChildShareWorld> world)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
            => _world = world;

        public List<(string EntitySet, Guid Id, string ETag)> ConditionalWrites { get; } = new();

        public override Task UpdateIfMatchAsync(
            string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default)
        {
            ConditionalWrites.Add((entitySetName, id, etag));
            var table = SecureChildLineage.Children.Values.Single(t => t.EntitySet == entitySetName).LogicalName;
            if (!_world().Has(table, id))
                throw new KeyNotFoundException($"{entitySetName}({id}) was not found; nothing was created.");
            if (etag != $"W/\"{_world().ValueOf<long>(table, id, "versionnumber")}\"")
                throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read.");
            var fields = (IDictionary<string, object>)entity;
            fields.Keys.Should().Equal(new[] { "ownerid@odata.bind" }, "the re-own writes the owner and nothing else");
            var bind = (string)fields["ownerid@odata.bind"];
            var team = Guid.Parse(bind["/teams(".Length..^1]);
            _world().Update(table, id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", team) });
            _world().Set(table, id, "versionnumber", _world().ValueOf<long>(table, id, "versionnumber") + 1);
            _world().Modified(table, id, DateTime.UtcNow);
            return Task.CompletedTask;
        }
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}
