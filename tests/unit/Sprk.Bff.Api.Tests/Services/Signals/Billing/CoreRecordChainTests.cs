using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals.Billing;

/// <summary>
/// ebilling-r1 task 082 (D-172, model 6.4; uac-r2 round 2 items 082.1 / 082.2): <see cref="CoreRecordChain"/> over an in-memory Dataverse
/// that evaluates each query (<see cref="SecureChildShareWorld"/>). The chain is the invoice's core record and its DIRECT parents only;
/// a project's pair goes through uac-r2's <see cref="SecureRootInheritance.NamePairParentAsync"/>, whose <c>Unknown</c> fails closed.
/// </summary>
public sealed class CoreRecordChainTests
{
    private const string Matter = SecureRootInheritance.Matter;
    private const string Project = SecureRootInheritance.Project;
    private const string WorkAssignment = SecureRootInheritance.WorkAssignment;
    private const string RecordType = SecureRootInheritance.RecordTypeRefEntity;

    private static readonly Guid M = Guid.Parse("08200000-0000-4000-8000-0000000000a1");
    private static readonly Guid M2 = Guid.Parse("08200000-0000-4000-8000-0000000000a2");
    private static readonly Guid P = Guid.Parse("08200000-0000-4000-8000-0000000000b1");
    private static readonly Guid P2 = Guid.Parse("08200000-0000-4000-8000-0000000000b2");
    private static readonly Guid W = Guid.Parse("08200000-0000-4000-8000-0000000000c1");
    private static readonly Guid Invoice = Guid.Parse("08200000-0000-4000-8000-0000000000d1");
    private static readonly Guid Event = Guid.Parse("08200000-0000-4000-8000-0000000000d2");
    private static readonly Guid MatterType = Guid.Parse("08200000-0000-4000-8000-0000000000e1");
    private static readonly Guid ProjectType = Guid.Parse("08200000-0000-4000-8000-0000000000e2");
    private static readonly Guid DocumentType = Guid.Parse("08200000-0000-4000-8000-0000000000e3");
    private static readonly Guid BlankType = Guid.Parse("08200000-0000-4000-8000-0000000000e4");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Mock<IGenericEntityService> _entities;

    public CoreRecordChainTests()
    {
        _entities = SecureChildShareWorld.EntitiesOver(() => _world);
        _world.Add(RecordType, MatterType, ("sprk_recordlogicalname", "sprk_matter"));
        _world.Add(RecordType, ProjectType, ("sprk_recordlogicalname", "sprk_project"));
        _world.Add(RecordType, DocumentType, ("sprk_recordlogicalname", "sprk_document"));
        _world.Add(RecordType, BlankType, ("sprk_recordlogicalname", " "));
        _world.Add(Matter, M);
        _world.Add(Matter, M2);
    }

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    private Task<IReadOnlyList<CoreRecordLink>> ChainOf(CoreRecordKind kind, Guid id) =>
        CoreRecordChain.ReadAsync(_entities.Object, NullLogger.Instance, new CoreRecordReference(kind, id), CancellationToken.None);

    private void ProjectFiledUnder(Guid project, string? pairId, Guid? pairType)
    {
        var columns = new List<(string, object)>();
        if (pairId is not null) columns.Add((SecureRootInheritance.PairIdColumn, pairId));
        if (pairType is { } t) columns.Add((SecureRootInheritance.PairTypeColumn, Ref(RecordType, t)));
        _world.Add(Project, project, columns.ToArray());
    }

    private void WorkAssignmentFiled(params (string Column, object Value)[] columns) => _world.Add(WorkAssignment, W, columns);

    // ── Matter ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082 chain: a matter's chain is the matter alone, and nothing is read")]
    public async Task Matter_IsItsOwnChain_WithNoRead()
    {
        var chain = await ChainOf(CoreRecordKind.Matter, M);

        chain.Should().Equal(new CoreRecordLink(Matter, M, 0));
        _world.Queries.Should().BeEmpty();
    }

    // ── Project: the pair through NamePairParentAsync ───────────────────────────────────────────────────────

    [Fact(DisplayName = "082 chain: a project filed under a matter is [P 0, M 1] (Parent)")]
    public async Task Project_UnderAMatter_HasTheMatterAtDistanceOne()
    {
        ProjectFiledUnder(P, M.ToString(), MatterType);

        var chain = await ChainOf(CoreRecordKind.Project, P);

        chain.Should().Equal(new CoreRecordLink(Project, P, 0), new CoreRecordLink(Matter, M, 1));
    }

    [Fact(DisplayName = "082 chain: a project filed under a project is [P 0, P' 1], and P''s own parent is never read (no second hop)")]
    public async Task Project_UnderAProject_StopsAtTheDirectParent()
    {
        ProjectFiledUnder(P2, M.ToString(), MatterType);
        ProjectFiledUnder(P, P2.ToString(), ProjectType);

        var chain = await ChainOf(CoreRecordKind.Project, P);

        chain.Should().Equal(new CoreRecordLink(Project, P, 0), new CoreRecordLink(Project, P2, 1));
        _world.Queries.Count(q => q.EntityName == Project).Should().Be(1, "only the invoice's own project row is read");
    }

    [Fact(DisplayName = "082 chain: a project with no pair is [P 0] (None, no fault)")]
    public async Task Project_WithNoPair_IsItsOwnChain()
    {
        ProjectFiledUnder(P, null, null);

        (await ChainOf(CoreRecordKind.Project, P)).Should().Equal(new CoreRecordLink(Project, P, 0));
    }

    [Fact(DisplayName = "082 chain: a project pair naming a readable type that is not a core record is [P 0] (NotAFilingParent, no fault)")]
    public async Task Project_PairNamingANonCoreType_GivesNoParent_AndNoFault()
    {
        ProjectFiledUnder(P, Guid.NewGuid().ToString(), DocumentType);

        (await ChainOf(CoreRecordKind.Project, P)).Should().Equal(new CoreRecordLink(Project, P, 0));
    }

    [Theory(DisplayName = "082 chain: a project pair UAC answers Unknown for fails closed (the chain read throws; no partial chain)")]
    [InlineData("not-a-guid", "type")]
    [InlineData("00000000-0000-0000-0000-000000000000", "type")]
    [InlineData("pair-id", "none")]
    [InlineData("pair-id", "blank")]
    [InlineData("pair-id", "missing")]
    public async Task Project_UnknownPair_FailsClosed(string pairId, string type)
    {
        var id = pairId == "pair-id" ? M.ToString() : pairId;
        Guid? typeRef = type switch
        {
            "type" => MatterType,
            "blank" => BlankType,
            "missing" => Guid.Parse("08200000-0000-4000-8000-0000000000ef"),
            _ => null,
        };
        ProjectFiledUnder(P, id, typeRef);

        var read = () => ChainOf(CoreRecordKind.Project, P);

        await read.Should().ThrowAsync<CoreRecordChainUnreadableException>();
    }

    [Fact(DisplayName = "082 chain: a project pair whose record type cannot be read fails closed")]
    public async Task Project_PairTypeReadFault_FailsClosed()
    {
        ProjectFiledUnder(P, M.ToString(), MatterType);
        _world.FailingQueriesOf(RecordType);

        var read = () => ChainOf(CoreRecordKind.Project, P);

        await read.Should().ThrowAsync<CoreRecordChainUnreadableException>().WithMessage("*could not be read*");
    }

    // ── Work assignment: typed lookups only ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082 chain: a work assignment typed under a project is [W 0, P 1]; the project's matter is not in it (no second hop)")]
    public async Task WorkAssignment_UnderAProject_StopsAtTheProject()
    {
        ProjectFiledUnder(P, M.ToString(), MatterType);
        WorkAssignmentFiled(("sprk_regardingproject", Ref(Project, P)));

        var chain = await ChainOf(CoreRecordKind.WorkAssignment, W);

        chain.Should().Equal(new CoreRecordLink(WorkAssignment, W, 0), new CoreRecordLink(Project, P, 1));
        _world.QueriedTables.Should().Equal(WorkAssignment);
    }

    [Fact(DisplayName = "082 chain: a work assignment typed under a matter is [W 0, M 1]")]
    public async Task WorkAssignment_UnderAMatter_HasTheMatter()
    {
        WorkAssignmentFiled(("sprk_regardingmatter", Ref(Matter, M)));

        (await ChainOf(CoreRecordKind.WorkAssignment, W)).Should().Equal(
            new CoreRecordLink(WorkAssignment, W, 0), new CoreRecordLink(Matter, M, 1));
    }

    [Fact(DisplayName = "082 chain: a work assignment typed under both a matter and a project has both at distance 1")]
    public async Task WorkAssignment_UnderBoth_HasTwoDirectParents()
    {
        WorkAssignmentFiled(("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));

        (await ChainOf(CoreRecordKind.WorkAssignment, W)).Should().Equal(
            new CoreRecordLink(WorkAssignment, W, 0), new CoreRecordLink(Matter, M, 1), new CoreRecordLink(Project, P, 1));
    }

    [Fact(DisplayName = "082 chain: a work assignment under an invoice or event only is [W 0]; the intermediate is never read (#1630 not followed)")]
    public async Task WorkAssignment_UnderAnIntermediateOnly_IsItsOwnChain()
    {
        _world.Add("sprk_invoice", Invoice, ("sprk_matter", Ref(Matter, M)));
        _world.Add("sprk_event", Event, ("sprk_regardingmatter", Ref(Matter, M2)));
        WorkAssignmentFiled(("sprk_regardinginvoice", Ref("sprk_invoice", Invoice)), ("sprk_regardingevent", Ref("sprk_event", Event)));

        var chain = await ChainOf(CoreRecordKind.WorkAssignment, W);

        chain.Should().Equal(new CoreRecordLink(WorkAssignment, W, 0));
        _world.QueriedTables.Should().Equal(WorkAssignment);
        _world.Queries.Single().ColumnSet.Columns.Should().BeEquivalentTo("sprk_regardingmatter", "sprk_regardingproject");
    }

    [Theory(DisplayName = "082 chain: a work assignment's pair is not read; a pair with no typed lookup, or one that cannot be parsed, adds nothing and never faults (#1616)")]
    [InlineData("matter")]
    [InlineData("garbage")]
    public async Task WorkAssignment_PairWithoutATypedLookup_AddsNothing(string pair)
    {
        WorkAssignmentFiled(
            (SecureRootInheritance.PairIdColumn, pair == "matter" ? M.ToString() : "not-a-guid"),
            (SecureRootInheritance.PairTypeColumn, Ref(RecordType, MatterType)));

        (await ChainOf(CoreRecordKind.WorkAssignment, W)).Should().Equal(new CoreRecordLink(WorkAssignment, W, 0));
        _world.QueriedTables.Should().NotContain(RecordType);
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "082 chain: an unreadable core-record row propagates the fault (no partial chain)")]
    [InlineData(CoreRecordKind.Project)]
    [InlineData(CoreRecordKind.WorkAssignment)]
    public async Task UnreadableRow_Propagates(CoreRecordKind kind)
    {
        ProjectFiledUnder(P, M.ToString(), MatterType);
        WorkAssignmentFiled(("sprk_regardingproject", Ref(Project, P)));
        _world.FailingQueriesOf(kind == CoreRecordKind.Project ? Project : WorkAssignment);

        var read = () => ChainOf(kind, kind == CoreRecordKind.Project ? P : W);

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("Test:*");
    }

    [Theory(DisplayName = "082 chain: a core record that is not found fails closed")]
    [InlineData(CoreRecordKind.Project)]
    [InlineData(CoreRecordKind.WorkAssignment)]
    public async Task MissingRow_FailsClosed(CoreRecordKind kind)
    {
        var read = () => ChainOf(kind, Guid.NewGuid());

        await read.Should().ThrowAsync<CoreRecordChainUnreadableException>().WithMessage("*was not found*");
    }

    [Fact(DisplayName = "082 chain: a record filed under itself names no parent")]
    public async Task Project_FiledUnderItself_NamesNoParent()
    {
        ProjectFiledUnder(P, P.ToString(), ProjectType);

        (await ChainOf(CoreRecordKind.Project, P)).Should().Equal(new CoreRecordLink(Project, P, 0));
    }

    // ── The extracted entry point (uac-r2 082.1): the four answers ──────────────────────────────────────────

    [Fact(DisplayName = "082 NamePairParentAsync: a parent-typed pair on a work assignment is NotAFilingParent with the #1616 reason")]
    public async Task NamePair_OnAWorkAssignment_IsUnrepeated()
    {
        var answer = await SecureRootInheritance.NamePairParentAsync(
            _entities.Object, NullLogger.Instance, WorkAssignment, M.ToString(), MatterType, new ConcurrentDictionary<Guid, string?>(),
            CancellationToken.None);

        answer.Should().BeOfType<PairParentAnswer.NotAFilingParent>()
            .Which.Unrepeated.Should().Contain("sprk_regardingmatter");
    }

    [Fact(DisplayName = "082 NamePairParentAsync: a pair repeating a typed lookup is None, and its type is not read")]
    public async Task NamePair_RepeatingATypedLookup_IsNone()
    {
        var answer = await SecureRootInheritance.NamePairParentAsync(
            _entities.Object, NullLogger.Instance, WorkAssignment, M.ToString(), MatterType, new ConcurrentDictionary<Guid, string?>(),
            CancellationToken.None, new HashSet<Guid> { M });

        answer.Should().BeSameAs(PairParentAnswer.None.Instance);
        _world.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "082 NamePairParentAsync: a type already faulted in this call is Unknown without a second read")]
    public async Task NamePair_FaultedTypeInThisCall_IsUnknown_WithoutARead()
    {
        var answer = await SecureRootInheritance.NamePairParentAsync(
            _entities.Object, NullLogger.Instance, Project, M.ToString(), MatterType, new ConcurrentDictionary<Guid, string?>(),
            CancellationToken.None, faultedTypes: new HashSet<Guid> { MatterType });

        answer.Should().BeOfType<PairParentAnswer.Unknown>();
        _world.Queries.Should().BeEmpty();
    }

    // ── 082c C19: the reverse read (ReadChildrenAsync) ─────────────────────────────────────────────────────

    private Task<IReadOnlyList<IReadOnlyList<CoreRecordLink>>> ChildrenOf(string table, Guid id, int max = 10) =>
        CoreRecordChain.ReadChildrenAsync(_entities.Object, NullLogger.Instance, table, id, max, CancellationToken.None);

    private static readonly Guid W2 = Guid.Parse("08200000-0000-4000-8000-0000000000c2");
    private static readonly Guid W3 = Guid.Parse("08200000-0000-4000-8000-0000000000c3");

    [Fact(DisplayName = "082c C19 children: a matter's children are the work assignments typed under it and the projects whose pair names it, each with its own chain; a grandchild is not one (no second hop)")]
    public async Task Children_OfAMatter_AreItsDirectFilings_Only()
    {
        ProjectFiledUnder(P, M.ToString("B").ToUpperInvariant(), MatterType); // any spelling Guid.TryParse accepts
        ProjectFiledUnder(P2, P.ToString(), ProjectType); // under P, so a grandchild of M
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)));
        _world.Add(WorkAssignment, W2, ("sprk_regardingproject", Ref(Project, P))); // a grandchild of M
        _world.Add(WorkAssignment, W3, ("sprk_regardingmatter", Ref(Matter, M2))); // another matter's

        var children = await ChildrenOf(Matter, M);

        children.Should().HaveCount(2);
        children[0].Should().Equal(new CoreRecordLink(Project, P, 0), new CoreRecordLink(Matter, M, 1));
        children[1].Should().Equal(new CoreRecordLink(WorkAssignment, W, 0), new CoreRecordLink(Matter, M, 1));
    }

    [Fact(DisplayName = "082c C19 children: a project's children are its projects and work assignments; a pair that only shares the id fragment, or names another type, is dropped by the forward chain")]
    public async Task Children_OfAProject_AreVerifiedByTheForwardChain()
    {
        ProjectFiledUnder(P, M.ToString(), MatterType);
        ProjectFiledUnder(P2, P.ToString(), ProjectType);
        var lookalike = Guid.Parse(P.ToString("N")[..8] + "-9999-4999-8999-999999999999");
        var other = Guid.NewGuid();
        ProjectFiledUnder(other, lookalike.ToString(), ProjectType); // the fragment matches; the id does not
        var asDocument = Guid.NewGuid();
        ProjectFiledUnder(asDocument, P.ToString(), DocumentType); // the id matches; the type is not a filing parent
        _world.Add(WorkAssignment, W, ("sprk_regardingproject", Ref(Project, P)));

        var children = await ChildrenOf(Project, P);

        children.Select(c => c[0]).Should().Equal(new CoreRecordLink(Project, P2, 0), new CoreRecordLink(WorkAssignment, W, 0));
        children.Should().OnlyContain(c => c.Contains(new CoreRecordLink(Project, P, 1)));
    }

    [Fact(DisplayName = "082c C19 children: a work assignment has no children, and nothing is read")]
    public async Task Children_OfAWorkAssignment_AreNone_WithNoRead()
    {
        (await ChildrenOf(WorkAssignment, W)).Should().BeEmpty();
        _world.Queries.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c C19 children: more candidates than the bound fails closed before any child chain is read")]
    public async Task Children_PastTheBound_FailClosed()
    {
        foreach (var id in new[] { W, W2, W3 })
        {
            _world.Add(WorkAssignment, id, ("sprk_regardingmatter", Ref(Matter, M)));
        }

        var read = () => ChildrenOf(Matter, M, max: 2);

        await read.Should().ThrowAsync<CoreRecordChainUnreadableException>().WithMessage("More than 2 core records*");
        _world.Queries.Should().NotContain(q => q.TopCount == 1, "no candidate's own chain was read");
    }

    [Fact(DisplayName = "082c C19 children: exactly the bound is read in full")]
    public async Task Children_AtTheBound_AreRead()
    {
        foreach (var id in new[] { W, W2 })
        {
            _world.Add(WorkAssignment, id, ("sprk_regardingmatter", Ref(Matter, M)));
        }

        (await ChildrenOf(Matter, M, max: 2)).Should().HaveCount(2);
    }

    [Fact(DisplayName = "082c C19 children: a candidate whose chain cannot be told fails closed (no partial list)")]
    public async Task Children_WithAnUnknownPair_FailClosed()
    {
        ProjectFiledUnder(P, M.ToString(), null); // the pair names M without a type: UAC answers Unknown
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)));

        var read = () => ChildrenOf(Matter, M);

        await read.Should().ThrowAsync<CoreRecordChainUnreadableException>();
    }

    [Fact(DisplayName = "082c C19 children: a read fault propagates (no partial list)")]
    public async Task Children_ReadFault_Propagates()
    {
        _world.Add(WorkAssignment, W, ("sprk_regardingmatter", Ref(Matter, M)));
        _world.FailingQueriesOf(Project);

        var read = () => ChildrenOf(Matter, M);

        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("Test:*");
    }
}
