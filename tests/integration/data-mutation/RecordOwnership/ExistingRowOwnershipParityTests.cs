// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: UAC task 194b — the ownership question the reconciler asks about an EXISTING row
//     (RecordOwnershipContext.ForExistingRowAsync) is the one the row's WRITER asked when it created it: the same parents in the
//     same order, so the same primary and the same secure-if-any set. Pins that parity for SignalWriter and
//     DecisionRecordWriter through the writers' own seams (the analysis and email document writers:
//     WriterBuilderOwnershipParityTests), and the order-invariance that was the defect (49 of 52 planned moves on
//     dev were wrong because the attribute order chose the primary).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL resolver and the REAL CoreAncestorResolver over task 148's world.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

public class ExistingRowOwnershipParityTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-0000001945b1");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-0000001945b2");
    private static readonly Guid CatalogMatter = Guid.Parse("4b000000-0000-4000-8000-0000001945c1");
    private static readonly Guid CatalogWorkAssignment = Guid.Parse("4b000000-0000-4000-8000-0000001945c2");
    private static readonly Guid CatalogProject = Guid.Parse("4b000000-0000-4000-8000-0000001945c3");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0)
        .Add("sprk_recordtype_ref", CatalogMatter, ("sprk_recordlogicalname", "sprk_matter"))
        .Add("sprk_recordtype_ref", CatalogWorkAssignment, ("sprk_recordlogicalname", "sprk_workassignment"))
        .Add("sprk_recordtype_ref", CatalogProject, ("sprk_recordlogicalname", "sprk_project"));

    private IGenericEntityService Entities => SecureChildShareWorld.EntitiesOver(() => _world).Object;

    private CoreAncestorResolver CoreAncestors => SecureChildShareWorld.CoreAncestorsOver(() => _world);

    private RecordOwnershipResolver Resolver => new(
        Entities, SecureChildShareWorld.Configuration(), NullLogger<RecordOwnershipResolver>.Instance);

    /// <summary>The To Do assignee links (nobody linked): the To Do job's predicate reads them.</summary>
    private readonly AssignedAccessTestDoubles.FakeAssignedAccessStore _links = new();

    /// <summary>One pass's reads (194b fix round K5): a fresh set per question unless a test shares one.</summary>
    private ExistingRowReads Reads() => new(Entities, CoreAncestors, _links);

    private Task<ExistingRowOwnership> QuestionAsync(Entity row, ExistingRowReads? reads = null) =>
        RecordOwnershipContext.ForExistingRowAsync(row, reads ?? Reads(), CancellationToken.None);

    /// <summary>The REAL SignalWriter over this world (194b fix round F3): its core derivation (PickStampAsync included) and its
    /// owner, through its parity seam (<see cref="SignalWriter.OwnershipOfAsync"/>); no write is made.</summary>
    private SignalWriter SignalWriter()
    {
        var services = new ServiceCollection();
        services.AddScoped<AssignedAccessStore>(_ => _links);
        return new SignalWriter(
            new OntologyWriterDataverseClient(() => throw new InvalidOperationException("194b parity: no write"),
                NullLogger<OntologyWriterDataverseClient>.Instance),
            Entities, CoreAncestors,
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
            Resolver, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<SignalWriter>.Instance);
    }

    /// <summary>The REAL DecisionRecordWriter over this world, through its parity seam
    /// (<see cref="DecisionRecordWriter.OwnershipParentsForAsync"/>); no write is made.</summary>
    private DecisionRecordWriter DecisionRecordWriter() => new(
        new OntologyWriterDataverseClient(() => throw new InvalidOperationException("194b parity: no write"),
            NullLogger<OntologyWriterDataverseClient>.Instance),
        Resolver, CoreAncestors, Entities,
        new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub.SpaarkeDev1),
        TimeProvider.System, NullLogger<DecisionRecordWriter>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    /// <summary>A Decision Record's <c>sprk_steps</c> as <c>DecisionRecordWriter</c> writes it: the review, the Signals it resolved.</summary>
    private static string Steps(params Guid[] resolvedSignals) => JsonSerializer.Serialize(new
    {
        reviewId = Guid.NewGuid().ToString("D"),
        resolvedSignalIds = resolvedSignals.Select(s => s.ToString("D")),
        steps = Array.Empty<object>(),
    });

    /// <summary>A Signal about <paramref name="subject"/> (its pair id repeated by its subject lookup), closed during the review:
    /// resolved, and NOT linked to the record (no <c>sprk_decisionrecord</c>) — only the record's <c>sprk_steps</c> names it.</summary>
    private Guid ClosedSignalAbout(string subjectLookup, string subjectTable, Guid subject, params (string Column, object Value)[] more)
    {
        var signal = Guid.NewGuid();
        _world.Add("sprk_signal", signal, more
            .Prepend(("sprk_signalstatus", new Microsoft.Xrm.Sdk.OptionSetValue(Sprk.Bff.Api.Services.Signals.SignalWriter.SignalStatusResolved)))
            .Prepend(("sprk_regardingrecordid", subject.ToString("D")))
            .Prepend((subjectLookup, Ref(subjectTable, subject)))
            .ToArray());
        return signal;
    }

    /// <summary>A row with <paramref name="columns"/> in the order given, and the same row with them reversed.</summary>
    private static (Entity Row, Entity Reversed) RowBothWays(string table, params (string Column, object Value)[] columns)
    {
        var id = Guid.NewGuid();
        Entity Build(IEnumerable<(string Column, object Value)> ordered)
        {
            var row = new Entity(table, id);
            foreach (var (column, value) in ordered)
                row[column] = value;
            return row;
        }

        return (Build(columns), Build(columns.Reverse()));
    }

    private async Task<IReadOnlyList<RecordOwnershipParent>> ExistingParentsAsync(Entity row)
    {
        var question = await QuestionAsync(row);
        question.Undetermined.Should().BeNull();
        return question.Context.Parents;
    }

    // ── SignalWriter ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: a Signal's existing-row question is SignalWriter's — core (PickStampAsync), subject, every stamp — in either attribute order, and a SECURE stamp under an ordinary core decides alike")]
    public async Task ASignal_IsAskedExactlyAsSignalWriterAsks()
    {
        var (matter, secureProject, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).SecureRoot("sprk_project", secureProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingmatter", "sprk_matter", matter), ("sprk_regardingproject", "sprk_project", secureProject));
        var (writer, writerOwner) = await SignalWriter().OwnershipOfAsync("sprk_todo", todo, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDecide, CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingtodo", Ref("sprk_todo", todo)),
            ("sprk_matter", Ref("sprk_matter", matter)),
            ("sprk_regardingmatter", Ref("sprk_matter", matter)),
            ("sprk_regardingproject", Ref("sprk_project", secureProject)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogMatter)),
            ("sprk_corerecordid", matter.ToString("D")),
            ("sprk_regardingrecordid", todo.ToString("D")),
            ("sprk_policy", Ref("sprk_policy", Guid.NewGuid()))); // #2084: a citation, never a parent

        writer.Parents[0].Should().Be(new RecordOwnershipParent("sprk_matter", matter), "PickStampAsync: matter over project (D-37)");
        writer.Parents.Should().Contain(new RecordOwnershipParent("sprk_project", secureProject), "the to-do's stamps name the secure project");
        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents, "the full ordered list, primary first");
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents, "whatever order the attributes come back in");
        writerOwner.OwningTeamId.Should().Be(SecureTeam, "secure-if-any: the secure stamp wins over the ordinary core");
        (await Resolver.ResolveOwnerAsync((await QuestionAsync(row)).Context, CancellationToken.None))
            .OwningTeamId.Should().Be(writerOwner.OwningTeamId);
    }

    [Fact(DisplayName = "194b parity: a Signal whose core is a user-owned work assignment asks about the work assignment first, as SignalWriter does; the event subject read first changes nothing")]
    public async Task ASignalAboutAWorkAssignment_IsAskedExactlyAsSignalWriterAsks()
    {
        var (workAssignment, @event) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_workassignment", workAssignment, ("owninguser", Ref("systemuser", SecureChildShareWorld.SomeUser)), ("sprk_issecure", false));
        _world.Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)), ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)));
        var (writer, writerOwner) = await SignalWriter().OwnershipOfAsync("sprk_event", @event, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDecide, CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingevent", Ref("sprk_event", @event)),
            ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogWorkAssignment)),
            ("sprk_corerecordid", workAssignment.ToString("D")),
            ("sprk_regardingrecordid", @event.ToString("D")));

        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
        writer.Parents[0].Should().Be(new RecordOwnershipParent("sprk_workassignment", workAssignment));
        writerOwner.OwningTeamId.Should().Be(GeneralTeam,
            "a user-owned work assignment filed under nothing keeps its own unit (owner decision 2026-10-29)");
    }

    // ── Owner decision 2026-10-29: a person-owned work assignment that is filed answers with its matter's unit ─────────

    /// <summary>A person-owned work assignment (the general unit) filed under an ordinary matter in BuB, and an event about it
    /// owned in the general unit — so the matter's team (TeamB) is reached only through the work assignment's filing.</summary>
    private (Guid WorkAssignment, Guid Matter, Guid Event) AFiledPersonOwnedWorkAssignment()
    {
        var (workAssignment, matter, @event) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_matter", matter, ("owningteam", Ref("team", TeamB)), ("sprk_issecure", false))
            .Add("sprk_workassignment", workAssignment, ("owninguser", Ref("systemuser", SecureChildShareWorld.SomeUser)),
                ("sprk_issecure", false), ("sprk_regardingmatter", Ref("sprk_matter", matter)))
            .Add("sprk_event", @event, ("owningteam", Ref("team", GeneralTeam)),
                ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)));
        return (workAssignment, matter, @event);
    }

    [Fact(DisplayName = "Owner decision 2026-10-29 must fire: a Signal whose core is a FILED person-owned work assignment gets its matter's team — from SignalWriter and from the existing-row question alike")]
    public async Task ASignalAboutAFiledPersonOwnedWorkAssignment_GetsItsMattersTeam_OnBothPaths()
    {
        var (workAssignment, _, @event) = AFiledPersonOwnedWorkAssignment();
        var (writer, writerOwner) = await SignalWriter().OwnershipOfAsync("sprk_event", @event, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDecide, CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingevent", Ref("sprk_event", @event)),
            ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogWorkAssignment)),
            ("sprk_corerecordid", workAssignment.ToString("D")),
            ("sprk_regardingrecordid", @event.ToString("D")));

        writer.Parents[0].Should().Be(new RecordOwnershipParent("sprk_workassignment", workAssignment));
        writerOwner.OwningTeamId.Should().Be(TeamB, "the work assignment belongs to its matter: the matter's unit, never its own");
        foreach (var shape in new[] { row, reversed })
        {
            (await ExistingParentsAsync(shape)).Should().Equal(writer.Parents);
            (await Resolver.ResolveOwnerAsync((await QuestionAsync(shape)).Context, CancellationToken.None))
                .OwningTeamId.Should().Be(TeamB, "the reconciler's answer is the writer's");
        }
    }

    [Fact(DisplayName = "Owner decision 2026-10-29 must fire: a Decision Record whose core is a FILED person-owned work assignment gets its matter's team — from the review writer and from the existing-row question alike")]
    public async Task ADecisionRecordAboutAFiledPersonOwnedWorkAssignment_GetsItsMattersTeam_OnBothPaths()
    {
        var (workAssignment, _, @event) = AFiledPersonOwnedWorkAssignment();
        var request = new DecisionRecordRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DecisionRecordCore("sprk_workassignment", workAssignment, CatalogWorkAssignment),
            new DecisionRecordRef("sprk_event", @event), Guid.NewGuid(), null, [], [], null, [], new Dictionary<string, object?>(),
            null, null, null);
        var reviewOwner = await DecisionRecordWriter().ResolveOwnerAsync(request, CancellationToken.None);
        var record = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardingevent"] = Ref("sprk_event", @event),
            ["sprk_regardingworkassignment"] = Ref("sprk_workassignment", workAssignment),
            ["sprk_workassignment"] = Ref("sprk_workassignment", workAssignment),
            ["sprk_corerecordtype"] = Ref("sprk_recordtype_ref", CatalogWorkAssignment),
            ["sprk_corerecordid"] = workAssignment.ToString("D"),
            ["sprk_regardingrecordid"] = @event.ToString("D"),
            ["sprk_steps"] = Steps(),
        };

        reviewOwner.Parents[0].Should().Be(new RecordOwnershipParent("sprk_workassignment", workAssignment));
        reviewOwner.Resolution.OwningTeamId.Should().Be(TeamB, "the work assignment belongs to its matter: the matter's unit");
        (await ExistingParentsAsync(record)).Should().Equal(reviewOwner.Parents);
        (await Resolver.ResolveOwnerAsync((await QuestionAsync(record)).Context, CancellationToken.None))
            .OwningTeamId.Should().Be(TeamB, "the reconciler's answer is the review writer's");
        // The gate writer asks the same parents on the content path (DecisionRecordWriter: KeepCreatorUnlessTargetIsTeamOwned);
        // its full write is pinned in DecisionRecordWriterTests.GateDecision.
        (await Resolver.ResolveOwnerAsync(
                RecordOwnershipContext.ForParents(reviewOwner.Parents) with { KeepCreatorUnlessTargetIsTeamOwned = true },
                CancellationToken.None))
            .OwningTeamId.Should().Be(TeamB, "the gate's content-path question answers the same");
    }

    [Fact(DisplayName = "194b F1-c must fire: a no-core Signal about a PERSONAL To Do the To Do job keeps names its subject first and says its owner is the assignee rule's")]
    public async Task ANoCoreSignal_NamesItsSubject_AndTheAssigneeRule()
    {
        var todo = Guid.NewGuid();
        _world.OrdinaryChild("sprk_todo", todo); // filed under nothing, no assignee: the job's Fallback (NoAssignee) — it keeps the rows
        var (writer, _) = await SignalWriter().OwnershipOfAsync("sprk_todo", todo, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDo, CancellationToken.None);
        var (row, _) = RowBothWays("sprk_signal", ("sprk_regardingtodo", Ref("sprk_todo", todo)), ("sprk_regardingrecordid", todo.ToString("D")));

        var question = await QuestionAsync(row);

        question.Context.Parents.Should().Equal(writer.Parents);
        question.NoCoreOwnerRule.Should().Contain("TodoAssigneeOwnerReconciliationJob");
        (await TodoAssigneeOwnerReconciliationJob.SelectsAsync(row, Entities, _links, CoreAncestors, CancellationToken.None))
            .Should().BeTrue("the rule is set exactly when the job's own predicate selects the row");
    }

    [Fact(DisplayName = "194b F1-c must fire: a no-core Signal about an EVENT (no To Do) is decided over its parents, as its writer decided it — never the assignee rule")]
    public async Task ANoCoreSignalAboutAnEvent_IsDecidedOverItsParents()
    {
        var @event = Guid.NewGuid();
        _world.Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)));
        var (writer, writerOwner) = await SignalWriter().OwnershipOfAsync("sprk_event", @event, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDo, CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_signal", ("sprk_regardingevent", Ref("sprk_event", @event)), ("sprk_regardingrecordid", @event.ToString("D")));

        foreach (var shape in new[] { row, reversed })
        {
            var question = await QuestionAsync(shape);
            question.NoCoreOwnerRule.Should().BeNull("only a row the To Do job keeps takes the assignee rule");
            question.Undetermined.Should().BeNull();
            question.Context.Parents.Should().Equal(writer.Parents);
            (await Resolver.ResolveOwnerAsync(question.Context, CancellationToken.None)).OwningTeamId.Should().Be(writerOwner.OwningTeamId).And.Be(TeamB);
        }
    }

    [Fact(DisplayName = "194b F1-c must fire: a no-core Signal whose To Do has since been FILED under a core record is decided over its parents — the job leaves it to the re-file rule")]
    public async Task ANoCoreSignalWhoseToDoIsNowCored_IsDecidedOverItsParents()
    {
        var (matter, todo) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_matter", matter, ("owningteam", Ref("team", TeamB)), ("sprk_issecure", false))
            .OrdinaryChild("sprk_todo", todo, ("sprk_regardingmatter", "sprk_matter", matter));
        var (row, _) = RowBothWays("sprk_signal", ("sprk_regardingtodo", Ref("sprk_todo", todo)), ("sprk_regardingrecordid", todo.ToString("D")));

        var question = await QuestionAsync(row);

        question.NoCoreOwnerRule.Should().BeNull("the job classifies a cored To Do's rows as the re-file rule's (SkippedCored)");
        question.Undetermined.Should().BeNull();
        question.Context.Parents.Should().Equal(new RecordOwnershipParent("sprk_todo", todo));
        (await TodoAssigneeOwnerReconciliationJob.SelectsAsync(row, Entities, _links, CoreAncestors, CancellationToken.None)).Should().BeFalse();
    }

    [Fact(DisplayName = "194b F1-c must fire: a gate Decision Record about an UNFILED communication has no assignee rule (it is no To Do's)")]
    public async Task AGateDecisionRecordOnAnUnfiledCommunication_HasNoAssigneeRule()
    {
        var communication = Guid.NewGuid();
        _world.UserOwnedChild("sprk_communication", communication);
        var (row, _) = RowBothWays("sprk_decisionrecord",
            ("sprk_regardingcommunication", Ref("sprk_communication", communication)), ("sprk_regardingrecordid", communication.ToString("D")));

        var question = await QuestionAsync(row);

        question.NoCoreOwnerRule.Should().BeNull();
        question.Undetermined.Should().BeNull();
        question.Context.Parents.Should().Equal(new RecordOwnershipParent("sprk_communication", communication));
    }

    [Fact(DisplayName = "194b F1-c fail closed: a no-core row about a To Do is undetermined when the job's predicate cannot be asked — no links, or a To Do read that faults")]
    public async Task ANoCoreToDoRow_IsUndetermined_WhenTheJobsPredicateCannotBeAsked()
    {
        var todo = Guid.NewGuid();
        _world.OrdinaryChild("sprk_todo", todo);
        var (row, _) = RowBothWays("sprk_signal", ("sprk_regardingtodo", Ref("sprk_todo", todo)), ("sprk_regardingrecordid", todo.ToString("D")));

        (await QuestionAsync(row, new ExistingRowReads(Entities, CoreAncestors, links: null))).Undetermined.Should().NotBeNull();
        _world.FailingRowReadsOf("sprk_todo", todo);
        var faulted = await QuestionAsync(row);
        faulted.Undetermined.Should().NotBeNull();
        faulted.NoCoreOwnerRule.Should().BeNull();
    }

    [Fact(DisplayName = "194b fail closed: a Signal whose core pair names a type the catalog does not hold as a core table is undetermined, never decided on a guess")]
    public async Task ASignalWithAnUnknownCoreType_IsUndetermined()
    {
        var unknownType = Guid.NewGuid();
        _world.Add("sprk_recordtype_ref", unknownType, ("sprk_recordlogicalname", "sprk_todo"));
        var (row, _) = RowBothWays("sprk_signal",
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", unknownType)), ("sprk_corerecordid", Guid.NewGuid().ToString("D")));

        (await QuestionAsync(row)).Undetermined.Should().NotBeNull();
        (await QuestionAsync(row, new ExistingRowReads(Entities, coreAncestors: null, _links))).Undetermined.Should().NotBeNull();
    }

    // ── DecisionRecordWriter ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: a Decision Record's existing-row question is DecisionRecordWriter's — the Signal's core (PickStampAsync), subject, derived cores, every resolved Signal's subject — in either attribute order")]
    public async Task ADecisionRecord_IsAskedExactlyAsDecisionRecordWriterAsks()
    {
        var (matter, project, todo, @event) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).OrdinaryRoot("sprk_project", project)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingproject", "sprk_project", project))
            .Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)));
        // The core the review is about is the Signal's (SignalWriter's derivation); here the To Do's only stamp, the project.
        var (signalQuestion, _) = await SignalWriter().OwnershipOfAsync("sprk_todo", todo, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDecide, CancellationToken.None);
        var core = signalQuestion.Parents[0];
        core.Should().Be(new RecordOwnershipParent("sprk_project", project));
        var recordId = Guid.NewGuid();
        // The also-resolved Signal, about the event: closed during the review, so only the record's sprk_steps names it (round R).
        var alsoResolved = ClosedSignalAbout("sprk_regardingevent", "sprk_event", @event);
        var request = new DecisionRecordRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DecisionRecordCore(core.EntityLogicalName, core.RecordId, CatalogMatter), new DecisionRecordRef("sprk_todo", todo),
            Guid.NewGuid(), null, [], [], null, [], new Dictionary<string, object?>(), null, null, null,
            SignalSubjects: [new DecisionRecordRef("sprk_event", @event)]);
        var writer = await DecisionRecordWriter().OwnershipParentsForAsync(request, CancellationToken.None);
        writer.Should().Contain(new RecordOwnershipParent("sprk_event", @event), "the writer asks about every resolved Signal's subject");
        Entity Shaped(bool reverse)
        {
            var columns = new (string Column, object Value)[]
            {
                ("sprk_regardingtodo", Ref("sprk_todo", todo)),
                ("sprk_regardingproject", Ref("sprk_project", project)),
                ("sprk_project", Ref("sprk_project", project)),
                ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogProject)),
                ("sprk_corerecordid", project.ToString("D")),
                ("sprk_regardingrecordid", todo.ToString("D")),
                ("sprk_steps", Steps(alsoResolved)),
            };
            var shaped = new Entity("sprk_decisionrecord", recordId);
            foreach (var (column, value) in reverse ? columns.Reverse() : columns)
                shaped[column] = value;
            return shaped;
        }

        (await ExistingParentsAsync(Shaped(false))).Should().Equal(writer);
        (await ExistingParentsAsync(Shaped(true))).Should().Equal(writer);
        (await QuestionAsync(Shaped(false))).Incomplete.Should().BeNull("every Signal its sprk_steps lists was read");
    }

    /// <summary>A Decision Record about the ordinary <paramref name="matter"/> (core and subject), with <paramref name="steps"/>.</summary>
    private static Entity DecisionRecordAbout(Guid matter, string? steps)
    {
        var row = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_matter"] = Ref("sprk_matter", matter),
            ["sprk_regardingmatter"] = Ref("sprk_matter", matter),
            ["sprk_corerecordtype"] = Ref("sprk_recordtype_ref", CatalogMatter),
            ["sprk_corerecordid"] = matter.ToString("D"),
            ["sprk_regardingrecordid"] = matter.ToString("D"),
        };
        if (steps is not null)
            row["sprk_steps"] = steps;
        return row;
    }

    [Fact(DisplayName = "194b R (ontology c) must fire: a Decision Record's question takes the SECURE subject of a Signal its sprk_steps lists — closed during the review, never linked — and no link to it is read")]
    public async Task ADecisionRecord_TakesTheSubjectsOfTheSignalsItsStepsList()
    {
        var (matter, secureEvent) = (Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).SecureChild("sprk_event", secureEvent);
        var closed = ClosedSignalAbout("sprk_regardingevent", "sprk_event", secureEvent);
        var row = DecisionRecordAbout(matter, Steps(closed));

        var question = await QuestionAsync(row);

        question.Incomplete.Should().BeNull();
        question.Context.Parents.Should().Contain(new RecordOwnershipParent("sprk_event", secureEvent));
        (await Resolver.ResolveOwnerAsync(question.Context, CancellationToken.None)).OwningTeamId.Should().Be(SecureTeam);
        _world.Queries.Where(q => q.EntityName == "sprk_signal").SelectMany(q => q.Criteria.Conditions)
            .Should().OnlyContain(c => c.AttributeName == "sprk_signalid", "the Signals are read by the ids the record lists, never by a link");
    }

    [Fact(DisplayName = "194b R (K1) must fire: a Decision Record that resolved more Signals than one read names is read whole, in pages — the secure subject of the LAST listed Signal is asked")]
    public async Task ADecisionRecordsSignals_AreReadInPages_NoneLeftOut()
    {
        var (matter, @event, secureEvent) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)))
            .SecureChild("sprk_event", secureEvent);
        var listed = Enumerable.Range(0, 2 * DecisionCommitService.ResolvedSignalReadPage)
            .Select(_ => ClosedSignalAbout("sprk_regardingevent", "sprk_event", @event))
            .Append(ClosedSignalAbout("sprk_regardingevent", "sprk_event", secureEvent))
            .ToArray();

        var question = await QuestionAsync(DecisionRecordAbout(matter, Steps(listed)));

        question.Incomplete.Should().BeNull();
        question.Context.Parents.Should().Contain(new RecordOwnershipParent("sprk_event", secureEvent));
        _world.Queries.Count(q => q.EntityName == "sprk_signal").Should().Be(3, "201 ids in pages of 100");
        _world.Queries.Where(q => q.EntityName == "sprk_signal")
            .Should().OnlyContain(q => q.Criteria.Conditions.Single().Values.Count <= DecisionCommitService.ResolvedSignalReadPage);
    }

    [Fact(DisplayName = "194b R (ontology c) must fire: a Decision Record's question is INCOMPLETE — never undetermined — when the Signals it resolved cannot all be known: no sprk_steps, no resolvedSignalIds, a Signal gone, unreadable, or with an underivable subject")]
    public async Task ADecisionRecordsQuestion_IsIncomplete_WhenItsSignalsCannotAllBeKnown()
    {
        var (matter, @event) = (Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)));
        var known = ClosedSignalAbout("sprk_regardingevent", "sprk_event", @event);
        var unreadable = ClosedSignalAbout("sprk_regardingevent", "sprk_event", @event);
        // Pair id set, but no subject lookup repeats it: its subject table cannot be derived.
        var underivable = ClosedSignalAbout("sprk_regardingevent", "sprk_event", Guid.NewGuid());
        _world.Set("sprk_signal", underivable, "sprk_regardingevent", Ref("sprk_event", @event));
        _world.FailingIdListReadsOf("sprk_signal", unreadable);

        var cases = new (string? Steps, string Why)[]
        {
            (null, "no sprk_steps"),
            ("{\"reviewId\":\"x\",\"steps\":[]}", "does not list resolvedSignalIds"),
            ("not json", "does not list resolvedSignalIds"),
            (Steps(known, Guid.NewGuid()), "no longer exists"),
            (Steps(known, unreadable), "could not be read"),
            (Steps(known, underivable), "cannot be derived"),
        };
        foreach (var (steps, why) in cases)
        {
            var question = await QuestionAsync(DecisionRecordAbout(matter, steps));
            question.Undetermined.Should().BeNull(why);
            question.Incomplete.Should().Contain(why);
            question.Context.Parents[0].Should().Be(new RecordOwnershipParent("sprk_matter", matter), "what is known is still asked");
        }

        var whole = await QuestionAsync(DecisionRecordAbout(matter, Steps(known)));
        whole.Incomplete.Should().BeNull();
        whole.Context.Parents.Should().Contain(new RecordOwnershipParent("sprk_event", @event));
        (await QuestionAsync(DecisionRecordAbout(matter, Steps()))).Incomplete.Should().BeNull("a record that resolved none (the gate's) is whole");
    }

    [Fact(DisplayName = "194b R (ontology c) parity must fire: a Decision Record whose resolved Signal has a SECURE core and stamp but an ordinary subject gets the writer's answer (ordinary) — only the Signal's subject is asked")]
    public async Task ADecisionRecordWhoseSignalHasASecureCore_GetsTheWritersAnswer()
    {
        var (matter, secureMatter, @event) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).SecureRoot("sprk_matter", secureMatter)
            .Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)));
        var row = DecisionRecordAbout(matter, steps: null);
        // Linked to the record too (it was not closed during the review): a link names the Signal, never its core or stamps.
        var signal = ClosedSignalAbout("sprk_regardingevent", "sprk_event", @event,
            ("sprk_matter", Ref("sprk_matter", secureMatter)), ("sprk_regardingmatter", Ref("sprk_matter", secureMatter)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogMatter)), ("sprk_corerecordid", secureMatter.ToString("D")),
            ("sprk_decisionrecord", Ref("sprk_decisionrecord", row.Id)));
        row["sprk_steps"] = Steps(signal);
        var request = new DecisionRecordRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DecisionRecordCore("sprk_matter", matter, CatalogMatter), new DecisionRecordRef("sprk_matter", matter),
            Guid.NewGuid(), null, [], [], null, [signal], new Dictionary<string, object?>(), null, null, null,
            SignalSubjects: [new DecisionRecordRef("sprk_event", @event)]);
        var writerOwner = await DecisionRecordWriter().ResolveOwnerAsync(request, CancellationToken.None);
        writerOwner.Resolution.IsSecureOwner.Should().BeFalse("the writer asks about the Signal's subject only, never its core or stamps");

        var question = await QuestionAsync(row);

        question.Incomplete.Should().BeNull();
        question.Context.Parents.Should().NotContain(new RecordOwnershipParent("sprk_matter", secureMatter));
        var answer = await Resolver.ResolveOwnerAsync(question.Context with { WhenUnfiled = UnfiledOwnership.KeepCreator }, CancellationToken.None);
        answer.OwningTeamId.Should().Be(writerOwner.Resolution.OwningTeamId);
    }

    [Fact(DisplayName = "194b K5 must fire: one pass reads a record type's catalog row and a subject's stamps ONCE, however many rows share them")]
    public async Task OnePass_ReadsTheCatalogAndTheStampsOnce()
    {
        var (matter, todo) = (Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).UserOwnedChild("sprk_todo", todo, ("sprk_regardingmatter", "sprk_matter", matter));
        Entity SignalRow() => new("sprk_signal", Guid.NewGuid())
        {
            ["sprk_regardingtodo"] = Ref("sprk_todo", todo),
            ["sprk_corerecordtype"] = Ref("sprk_recordtype_ref", CatalogMatter),
            ["sprk_corerecordid"] = matter.ToString("D"),
            ["sprk_regardingrecordid"] = todo.ToString("D"),
        };
        var reads = Reads();

        var first = await QuestionAsync(SignalRow(), reads);
        var second = await QuestionAsync(SignalRow(), reads);
        var third = await QuestionAsync(SignalRow(), reads);

        new[] { first, second, third }.Should().OnlyContain(q => q.Undetermined == null);
        second.Context.Parents.Should().Equal(first.Context.Parents);
        third.Context.Parents.Should().Equal(first.Context.Parents);
        reads.CatalogReads.Should().Be(1);
        reads.StampDerivations.Should().Be(1);
        Reads().CatalogReads.Should().Be(0, "a new pass reads again");
    }

    [Fact(DisplayName = "194b F2 must fire: a no-core Signal about a user-owned FILED communication — the writer's OWNER equals the pass's (TeamB, the look-through of its filing), not just the parents")]
    public async Task ANoCoreSignalOnAUserOwnedFiledCommunication_TheWritersOwnerIsThePasss()
    {
        var (@event, communication) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_event", @event, ("owningteam", Ref("team", TeamB))) // team-owned, filed under nothing: no core
            .UserOwnedChild("sprk_communication", communication, ("sprk_regardingevent", "sprk_event", @event));
        var (writer, writerOwner) = await SignalWriter().OwnershipOfAsync("sprk_communication", communication, Sprk.Bff.Api.Services.Signals.SignalWriter.LaneDo, CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingcommunication", Ref("sprk_communication", communication)), ("sprk_regardingrecordid", communication.ToString("D")));

        writer.Parents.Should().Equal(new[] { new RecordOwnershipParent("sprk_communication", communication) }, "no core: the subject alone");
        writerOwner.OwningTeamId.Should().Be(TeamB, "B: the user-owned filed communication's filing decides, not its creator's unit");
        foreach (var shape in new[] { row, reversed })
        {
            var question = await QuestionAsync(shape);
            question.Context.Parents.Should().Equal(writer.Parents);
            // The pass asks with ContextFor's settings (an unfiled row keeps its creator); B is the resolver's, shared by both.
            var passContext = question.Context with { WhenUnfiled = UnfiledOwnership.KeepCreator };
            (await Resolver.ResolveOwnerAsync(passContext, CancellationToken.None)).OwningTeamId.Should().Be(writerOwner.OwningTeamId);
        }
    }
}
