using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// The secure-child lineage reads a Signal's and Decision Record's core record pair (<c>sprk_corerecordtype</c>, a record-type
/// catalog row, + <c>sprk_corerecordid</c>) as a parent, beside its lookups (<c>SecureChildLineage.CorePairParent</c>).
/// Driven over the REAL <see cref="SecureChildShareSynchronizer"/>, the REAL <see cref="SecureChildReconciler"/> and one
/// in-memory Dataverse (<see cref="SecureChildShareWorld"/>): the core is a root of the row even when its subject is filed under
/// another record; a pair that repeats a lookup adds no root; a service-request core, a type that maps to no core table and no
/// pair add nothing; a malformed pair is held (narrow only); a catalog read fault leaves the row for the next pass.
/// </summary>
public class SecureChildCorePairLineageTests
{
    private const int ViewOnly = 1;

    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;

    // The record-type catalog (sprk_recordtype_ref): each type names its table.
    private static readonly Guid ProjectType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a1");
    private static readonly Guid WorkAssignmentType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a2");
    private static readonly Guid ServiceRequestType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a3");
    private static readonly Guid MatterType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a4");
    private static readonly Guid NotACoreType = Guid.Parse("c0e00000-0000-4000-8000-0000000000a5");
    private static readonly Guid TypeNotInTheCatalog = Guid.Parse("c0e00000-0000-4000-8000-0000000000a6");

    // Roots: P (the core) and Q (the subject's root) are secure; O is ordinary.
    private static readonly Guid P = Guid.Parse("c0e00000-0000-4000-8000-0000000000b1");
    private static readonly Guid Q = Guid.Parse("c0e00000-0000-4000-8000-0000000000b2");
    private static readonly Guid O = Guid.Parse("c0e00000-0000-4000-8000-0000000000b3");
    private static readonly Guid W = Guid.Parse("c0e00000-0000-4000-8000-0000000000b4");
    private static readonly Guid M = Guid.Parse("c0e00000-0000-4000-8000-0000000000b5");
    private static readonly Guid ServiceRequest = Guid.Parse("c0e00000-0000-4000-8000-0000000000b6");

    private static readonly Guid EventUnderQ = Guid.Parse("c0e00000-0000-4000-8000-0000000000c1");
    private static readonly Guid EventUnderO = Guid.Parse("c0e00000-0000-4000-8000-0000000000c2");
    private static readonly Guid Signal = Guid.Parse("c0e00000-0000-4000-8000-0000000000d1");
    private static readonly Guid Record = Guid.Parse("c0e00000-0000-4000-8000-0000000000d2");

    private static readonly Guid UserA = Guid.Parse("c0e00000-0000-4000-8000-00000000000a"); // on P and Q
    private static readonly Guid UserB = Guid.Parse("c0e00000-0000-4000-8000-00000000000b"); // on P only
    private static readonly Guid UserC = Guid.Parse("c0e00000-0000-4000-8000-00000000000c"); // on Q only

    private static DataversePrincipalRef User(Guid id) => DataversePrincipalRef.User(id);

    private readonly FakeRecordShareTable _shares = new();

    private static SecureChildShareWorld World() => SecureChildShareWorld.Standard()
        .SecureRoot("sprk_project", P)
        .SecureRoot("sprk_project", Q)
        .OrdinaryRoot("sprk_project", O)
        .SecureRoot("sprk_workassignment", W)
        .SecureRoot("sprk_matter", M)
        .Add("sprk_recordtype_ref", ProjectType, ("sprk_recordlogicalname", "sprk_project"))
        .Add("sprk_recordtype_ref", WorkAssignmentType, ("sprk_recordlogicalname", "sprk_workassignment"))
        .Add("sprk_recordtype_ref", ServiceRequestType, ("sprk_recordlogicalname", "sprk_servicerequest"))
        .Add("sprk_recordtype_ref", MatterType, ("sprk_recordlogicalname", "sprk_matter"))
        .Add("sprk_recordtype_ref", NotACoreType, ("sprk_recordlogicalname", "sprk_invoice"))
        .SecureChild("sprk_event", EventUnderQ, ("sprk_regardingproject", "sprk_project", Q))
        .OrdinaryChild("sprk_event", EventUnderO, ("sprk_regardingproject", "sprk_project", O));

    private readonly SecureChildShareWorld _world = World();

    /// <summary>A Secure-team-owned Signal about <see cref="EventUnderQ"/> (re-filed there), its pair naming <paramref name="core"/>.</summary>
    private static SecureChildShareWorld WithSignalAboutTheEventUnderQ(SecureChildShareWorld world, Guid coreType, string coreId)
    {
        world.SecureChild("sprk_signal", Signal, ("sprk_regardingevent", "sprk_event", EventUnderQ));
        return Pair(world, "sprk_signal", Signal, coreType, coreId);
    }

    private static SecureChildShareWorld Pair(SecureChildShareWorld world, string table, Guid id, Guid coreType, string coreId)
    {
        world.Set(table, id, "sprk_corerecordtype", new EntityReference("sprk_recordtype_ref", coreType));
        world.Set(table, id, "sprk_corerecordid", coreId);
        return world;
    }

    /// <summary>P is shared with A and B; Q with A and C. The intersection is A.</summary>
    private void SeedRootShares()
    {
        _shares.Seed("sprk_project", P, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", P, User(UserB), ViewOnly);
        _shares.Seed("sprk_project", Q, User(UserA), ViewOnly);
        _shares.Seed("sprk_project", Q, User(UserC), ViewOnly);
    }

    private static Task<SecureRootsAbove> RootsAboveAsync(SecureChildShareWorld world, string table, Guid id) =>
        world.Synchronizer(new FakeRecordShareTable()).SecureRootsAboveAsync([world.Retrieve(table, id, null)], CancellationToken.None);

    // ── 1. The core is a root ───────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "core pair must-fire: a Signal's core project is a root beside its subject's root; the mirror is the intersection")]
    public async Task ASignalsCoreProject_IsARoot_AndTheMirrorIsTheIntersection()
    {
        WithSignalAboutTheEventUnderQ(_world, ProjectType, P.ToString("D"));
        SeedRootShares();

        var above = await RootsAboveAsync(_world, "sprk_signal", Signal);
        above.Roots.Should().BeEquivalentTo(new[] { ("sprk_project", P), ("sprk_project", Q) });
        above.Undetermined.Should().BeEmpty();

        var result = await _world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.ChildrenNotUpdated.Should().Be(0, result.Detail);
        _shares.MaskOf("sprk_signal", Signal, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_signal", Signal, User(UserB)).Should().BeNull("B is not shared on the subject's root Q");
        _shares.MaskOf("sprk_signal", Signal, User(UserC)).Should().BeNull("C is not shared on the core P");
    }

    [Fact(DisplayName = "core pair must-fire: the pass over the core project reaches the Signal through its pair and narrows its mirror")]
    public async Task ThePassOverTheCore_ReachesTheSignalThroughItsPair()
    {
        WithSignalAboutTheEventUnderQ(_world, ProjectType, P.ToString("D"));
        SeedRootShares();
        _shares.Seed("sprk_signal", Signal, User(UserA), ViewOnly);
        _shares.Seed("sprk_signal", Signal, User(UserC), ViewOnly); // the mirror of Q alone

        var result = await _world.Synchronizer(_shares).SyncRootAsync("sprk_project", P, CancellationToken.None);

        result.ChildrenNotUpdated.Should().Be(0, result.Detail);
        _shares.MaskOf("sprk_signal", Signal, User(UserC)).Should().BeNull("the Signal is under P too, and P is not shared with C");
        _shares.MaskOf("sprk_signal", Signal, User(UserA)).Should().Be(ViewOnly);
        _shares.WriteLog.Where(w => w.RecordId == Signal).Should().ContainSingle().Which.Action.Should().Be("RevokeAccess");
    }

    [Fact(DisplayName = "core pair must-fire: the reconciler's pass over the core project takes an ordinary-owned Signal into isolation")]
    public async Task TheReconcilersPassOverTheCore_ReownsTheSignalItsPairNames()
    {
        // The Signal's subject is an ordinary event under an ordinary project: only its core is secure.
        _world.OrdinaryChild("sprk_signal", Signal, ("sprk_regardingevent", "sprk_event", EventUnderO));
        _world.Set("sprk_signal", Signal, "sprk_regardingrecordid", EventUnderO.ToString("D"));
        Pair(_world, "sprk_signal", Signal, ProjectType, P.ToString("D"));

        var report = await SecureChildShareWorld.ReconcilerOver(() => _world, _shares, new Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient())
            .ReconcileAsync("sprk_project", P, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_signal", Signal).Should().Be(DataversePrincipalRef.Team(SecureTeam), "its core record P is secure");
    }

    // ── 2. A pair that repeats a lookup ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "core pair must-not-fire: a Signal about its own core work assignment keeps one root and gets no share write")]
    public async Task ASignalAboutItsOwnCoreWorkAssignment_KeepsOneRoot_AndNoShareIsWritten()
    {
        _world.SecureChild("sprk_signal", Signal, ("sprk_regardingworkassignment", "sprk_workassignment", W));
        Pair(_world, "sprk_signal", Signal, WorkAssignmentType, W.ToString("D"));
        _shares.Seed("sprk_workassignment", W, User(UserA), ViewOnly);
        _shares.Seed("sprk_signal", Signal, User(UserA), ViewOnly); // already the mirror of W

        var above = await RootsAboveAsync(_world, "sprk_signal", Signal);
        above.Roots.Should().Equal([("sprk_workassignment", W)]);
        above.Undetermined.Should().BeEmpty();

        var all = await _world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);
        var scoped = await _world.Synchronizer(_shares).SyncRootAsync("sprk_workassignment", W, CancellationToken.None);

        all.ChildrenNotUpdated.Should().Be(0, all.Detail);
        scoped.ChildrenNotUpdated.Should().Be(0, scoped.Detail);
        _shares.WriteLog.Where(w => w.RecordId == Signal).Should().BeEmpty("the Signal's mirror is unchanged");
        _shares.MaskOf("sprk_signal", Signal, User(UserA)).Should().Be(ViewOnly);
    }

    // ── 3. Fail closed ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "core pair fail closed: a catalog read fault leaves the Signal for the next pass, never widened")]
    [InlineData("fault")]
    [InlineData("missing catalog row")]
    public async Task ACatalogReadFault_LeavesTheSignalUnwritten_AndTheNextPassMirrorsIt(string fault)
    {
        var faulty = World();
        if (fault == "fault")
        {
            WithSignalAboutTheEventUnderQ(faulty, ProjectType, P.ToString("D"));
            faulty.FailingRowReadsOf("sprk_recordtype_ref", ProjectType);
        }
        else
        {
            WithSignalAboutTheEventUnderQ(faulty, TypeNotInTheCatalog, P.ToString("D"));
        }

        SeedRootShares();
        var current = faulty;
        var synchronizer = SecureChildShareWorld.SynchronizerOver(() => current, _shares);

        var first = await synchronizer.ReconcileAllAsync(CancellationToken.None);

        first.ChildrenNotUpdated.Should().BeGreaterThan(0, "the Signal's lineage could not be read this pass");
        _shares.WriteLog.Where(w => w.RecordId == Signal).Should().BeEmpty("a Signal whose core cannot be read is never granted the subject root's sharees");
        var reading = () => RootsAboveAsync(faulty, "sprk_signal", Signal);
        await reading.Should().ThrowAsync<Exception>("the fault propagates to the recent-changes pass, which retries its window");

        // The next pass, the catalog answering.
        current = WithSignalAboutTheEventUnderQ(World(), ProjectType, P.ToString("D"));
        var second = await SecureChildShareWorld.SynchronizerOver(() => current, _shares).ReconcileAllAsync(CancellationToken.None);

        second.ChildrenNotUpdated.Should().Be(0, second.Detail);
        _shares.MaskOf("sprk_signal", Signal, User(UserA)).Should().Be(ViewOnly);
        _shares.MaskOf("sprk_signal", Signal, User(UserC)).Should().BeNull();
    }

    [Fact(DisplayName = "core pair fail closed: a malformed pair holds the Signal (narrow only)")]
    public async Task AMalformedPair_HoldsTheSignal_AndGrantsNothing()
    {
        WithSignalAboutTheEventUnderQ(_world, ProjectType, "not-a-guid");
        SeedRootShares();
        _shares.Seed("sprk_signal", Signal, User(UserB), ViewOnly); // wider than any determined mirror

        var above = await RootsAboveAsync(_world, "sprk_signal", Signal);
        above.Undetermined.Should().ContainSingle().Which.Should().Contain("its core record pair is malformed");

        var result = await _world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.ChildrenHeld.Should().Be(1);
        _shares.WriteLog.Where(w => w.RecordId == Signal).Should().OnlyContain(w => w.Action == "RevokeAccess", "a held row is only narrowed");
        _shares.MaskOf("sprk_signal", Signal, User(UserA)).Should().BeNull("nothing is granted to a held row");
        _shares.MaskOf("sprk_signal", Signal, User(UserC)).Should().BeNull("nothing is granted to a held row");
    }

    // ── 4. Decision Records ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "core pair must-not-fire: a Decision Record whose typed core and pair name one project has one root")]
    public async Task ADecisionRecordsTypedCoreAndPair_NameOneRoot()
    {
        _world.SecureChild("sprk_decisionrecord", Record,
            ("sprk_project", "sprk_project", P), ("sprk_regardingproject", "sprk_project", P));
        Pair(_world, "sprk_decisionrecord", Record, ProjectType, P.ToString("D"));

        var above = await RootsAboveAsync(_world, "sprk_decisionrecord", Record);

        above.Roots.Should().Equal([("sprk_project", P)]);
        above.Undetermined.Should().BeEmpty();
    }

    [Theory(DisplayName = "core pair must-not-fire: a service-request core, or a type that maps to no core table, adds nothing")]
    [InlineData("service request")]
    [InlineData("not a core table")]
    public async Task ADecisionRecordWithAServiceRequestCore_KeepsTodaysRoots(string core)
    {
        _world.SecureChild("sprk_decisionrecord", Record, ("sprk_regardingmatter", "sprk_matter", M));
        Pair(_world, "sprk_decisionrecord", Record, core == "service request" ? ServiceRequestType : NotACoreType, ServiceRequest.ToString("D"));
        _shares.Seed("sprk_matter", M, User(UserA), ViewOnly);

        var above = await RootsAboveAsync(_world, "sprk_decisionrecord", Record);

        above.Roots.Should().Equal([("sprk_matter", M)]);
        above.Undetermined.Should().BeEmpty();
        _world.QueriedTables.Should().NotContain("sprk_servicerequest", "the walk does not look through a service request")
            .And.NotContain("sprk_invoice", "a record the catalog does not map to a core table is not followed");

        var result = await _world.Synchronizer(_shares).ReconcileAllAsync(CancellationToken.None);

        result.ChildrenHeld.Should().Be(0);
        _shares.MaskOf("sprk_decisionrecord", Record, User(UserA)).Should().Be(ViewOnly);
    }

    // ── 5. No pair ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "core pair must-not-fire: a Signal with no pair keeps its subject's root and reads no catalog row")]
    public async Task ASignalWithNoPair_KeepsItsSubjectsRoot_AndReadsNoCatalogRow()
    {
        _world.SecureChild("sprk_signal", Signal, ("sprk_regardingevent", "sprk_event", EventUnderQ));
        var row = _world.Retrieve("sprk_signal", Signal, null);
        var reads = _world.RowReads;

        var above = await _world.Synchronizer(new FakeRecordShareTable()).SecureRootsAboveAsync([row], CancellationToken.None);

        above.Roots.Should().Equal([("sprk_project", Q)]);
        above.Undetermined.Should().BeEmpty();
        _world.RowReads.Should().Be(reads, "a row with no pair needs no catalog read");
    }
}
