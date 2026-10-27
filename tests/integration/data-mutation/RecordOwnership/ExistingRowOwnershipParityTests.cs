// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: UAC task 194b — the ownership question the reconciler asks about an EXISTING row
//     (RecordOwnershipContext.ForExistingRowAsync) is the one the row's WRITER asked when it created it: the same parents in the
//     same order, so the same primary and the same secure-if-any set. Pins that parity per writer (SignalWriter,
//     DecisionRecordWriter, the analysis create paths, EmailAttachmentProcessor, IncomingCommunicationProcessor) through the
//     writers' own context builders where they have one, and the order-invariance that was the defect (49 of 52 planned moves on
//     dev were wrong because the attribute order chose the primary).
//
// Doubles are module boundaries only (ADR-038 §4): the REAL resolver and the REAL CoreAncestorResolver over task 148's world.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
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

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0)
        .Add("sprk_recordtype_ref", CatalogMatter, ("sprk_recordlogicalname", "sprk_matter"))
        .Add("sprk_recordtype_ref", CatalogWorkAssignment, ("sprk_recordlogicalname", "sprk_workassignment"));

    private IGenericEntityService Entities => SecureChildShareWorld.EntitiesOver(() => _world).Object;

    private CoreAncestorResolver CoreAncestors => SecureChildShareWorld.CoreAncestorsOver(() => _world);

    private RecordOwnershipResolver Resolver => new(
        Entities, SecureChildShareWorld.Configuration(), NullLogger<RecordOwnershipResolver>.Instance);

    private static EntityReference Ref(string table, Guid id) => new(table, id);

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
        var question = await RecordOwnershipContext.ForExistingRowAsync(row, Entities, CoreAncestors, CancellationToken.None);
        question.Undetermined.Should().BeNull();
        return question.Context.Parents;
    }

    private async Task<IReadOnlyList<CoreAncestorStamp>> StampsAsync(string subjectTable, Guid subjectId) =>
        (await CoreAncestors.ResolveStampsAsync(subjectTable, subjectId, CancellationToken.None)).Stamps;

    // ── SignalWriter ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: a Signal's existing-row question is SignalWriter's — core, subject, every stamp — in either attribute order, and a SECURE stamp under an ordinary core decides alike")]
    public async Task ASignal_IsAskedExactlyAsSignalWriterAsks()
    {
        var (matter, secureProject, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).SecureRoot("sprk_project", secureProject)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingmatter", "sprk_matter", matter), ("sprk_regardingproject", "sprk_project", secureProject));
        var stamps = await StampsAsync("sprk_todo", todo);
        var writer = SignalWriter.OwnershipContextOf("sprk_matter", matter, stamps, "sprk_todo", todo, requestedBy: null);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingtodo", Ref("sprk_todo", todo)),
            ("sprk_matter", Ref("sprk_matter", matter)),
            ("sprk_regardingmatter", Ref("sprk_matter", matter)),
            ("sprk_regardingproject", Ref("sprk_project", secureProject)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogMatter)),
            ("sprk_corerecordid", matter.ToString("D")),
            ("sprk_regardingrecordid", todo.ToString("D")),
            ("sprk_policy", Ref("sprk_policy", Guid.NewGuid()))); // #2084: a citation, never a parent

        stamps.Select(s => s.RecordId).Should().Contain(secureProject, "the to-do's stamps name the secure project");
        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents, "the full ordered list, primary first");
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents, "whatever order the attributes come back in");
        var answer = await Resolver.ResolveOwnerAsync(writer, CancellationToken.None);
        answer.OwningTeamId.Should().Be(SecureTeam, "secure-if-any: the secure stamp wins over the ordinary core");
        (await Resolver.ResolveOwnerAsync((await RecordOwnershipContext.ForExistingRowAsync(row, Entities, CoreAncestors, CancellationToken.None)).Context,
            CancellationToken.None)).OwningTeamId.Should().Be(answer.OwningTeamId);
    }

    [Fact(DisplayName = "194b parity: a Signal whose core is a user-owned work assignment asks about the work assignment first, as SignalWriter does; the event subject read first changes nothing")]
    public async Task ASignalAboutAWorkAssignment_IsAskedExactlyAsSignalWriterAsks()
    {
        var (workAssignment, @event) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_workassignment", workAssignment, ("owninguser", Ref("systemuser", SecureChildShareWorld.SomeUser)), ("sprk_issecure", false));
        _world.Add("sprk_event", @event, ("owningteam", Ref("team", TeamB)), ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)));
        var stamps = await StampsAsync("sprk_event", @event);
        var writer = SignalWriter.OwnershipContextOf("sprk_workassignment", workAssignment, stamps, "sprk_event", @event, requestedBy: null);
        var (row, reversed) = RowBothWays("sprk_signal",
            ("sprk_regardingevent", Ref("sprk_event", @event)),
            ("sprk_regardingworkassignment", Ref("sprk_workassignment", workAssignment)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogWorkAssignment)),
            ("sprk_corerecordid", workAssignment.ToString("D")),
            ("sprk_regardingrecordid", @event.ToString("D")));

        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
        (await Resolver.ResolveOwnerAsync(writer, CancellationToken.None)).OwningTeamId.Should().Be(GeneralTeam,
            "a user-owned core is a root: its own unit, never looked through (ontology: SignalWriter's nightly owner check)");
    }

    [Fact(DisplayName = "194b: a no-core Signal names its subject first and says its owner is the assignee rule's (TodoAssigneeOwnerReconciliationJob)")]
    public async Task ANoCoreSignal_NamesItsSubject_AndTheAssigneeRule()
    {
        var todo = Guid.NewGuid();
        _world.OrdinaryChild("sprk_todo", todo);
        var writer = SignalWriter.OwnershipContextOf(null, Guid.Empty, [], "sprk_todo", todo, requestedBy: null);
        var (row, _) = RowBothWays("sprk_signal", ("sprk_regardingtodo", Ref("sprk_todo", todo)), ("sprk_regardingrecordid", todo.ToString("D")));

        var question = await RecordOwnershipContext.ForExistingRowAsync(row, Entities, CoreAncestors, CancellationToken.None);

        question.Context.Parents.Should().Equal(writer.Parents);
        question.NoCoreOwnerRule.Should().Contain("TodoAssigneeOwnerReconciliationJob");
    }

    [Fact(DisplayName = "194b fail closed: a Signal whose core pair names a type the catalog does not hold as a core table is undetermined, never decided on a guess")]
    public async Task ASignalWithAnUnknownCoreType_IsUndetermined()
    {
        var unknownType = Guid.NewGuid();
        _world.Add("sprk_recordtype_ref", unknownType, ("sprk_recordlogicalname", "sprk_todo"));
        var (row, _) = RowBothWays("sprk_signal",
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", unknownType)), ("sprk_corerecordid", Guid.NewGuid().ToString("D")));

        (await RecordOwnershipContext.ForExistingRowAsync(row, Entities, CoreAncestors, CancellationToken.None))
            .Undetermined.Should().NotBeNull();
        (await RecordOwnershipContext.ForExistingRowAsync(row, Entities, coreAncestors: null, CancellationToken.None))
            .Undetermined.Should().NotBeNull();
    }

    // ── DecisionRecordWriter ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: a Decision Record's existing-row question is DecisionRecordWriter's — typed core, subject, derived cores — in either attribute order")]
    public async Task ADecisionRecord_IsAskedExactlyAsDecisionRecordWriterAsks()
    {
        var (matter, project, todo) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).OrdinaryRoot("sprk_project", project)
            .UserOwnedChild("sprk_todo", todo, ("sprk_regardingproject", "sprk_project", project));
        var stamps = await StampsAsync("sprk_todo", todo);
        var request = new DecisionRecordRequest(
            Guid.NewGuid(), Guid.NewGuid(), new DecisionRecordCore("sprk_matter", matter, CatalogMatter), new DecisionRecordRef("sprk_todo", todo),
            Guid.NewGuid(), null, [], [], null, [], new Dictionary<string, object?>(), null, null, null);
        var writer = DecisionRecordWriter.OwnershipContextOf(request, stamps, noRequester: true);
        var (row, reversed) = RowBothWays("sprk_decisionrecord",
            ("sprk_regardingtodo", Ref("sprk_todo", todo)),
            ("sprk_regardingproject", Ref("sprk_project", project)),
            ("sprk_matter", Ref("sprk_matter", matter)),
            ("sprk_corerecordtype", Ref("sprk_recordtype_ref", CatalogMatter)),
            ("sprk_corerecordid", matter.ToString("D")),
            ("sprk_regardingrecordid", todo.ToString("D")));

        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
    }

    // ── The analysis create paths ─────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: an analysis asks about its document first, as AnalysisEndpoints (create, promote) and AppOnlyAnalysisService do")]
    public async Task AnAnalysis_IsAskedAsItsWritersAsk()
    {
        var (document, matter) = (Guid.NewGuid(), Guid.NewGuid());
        // AnalysisEndpoints' promote path: the document, then the regarding record; create and AppOnlyAnalysisService: the document.
        var promote = RecordOwnershipContext.ForParents(new[]
        {
            new RecordOwnershipParent("sprk_document", document), new RecordOwnershipParent("sprk_matter", matter),
        });
        var create = RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_document", document) });
        var (promoted, promotedReversed) = RowBothWays("sprk_analysis",
            ("sprk_regardingmatter", Ref("sprk_matter", matter)), ("sprk_documentid", Ref("sprk_document", document)));
        var (created, _) = RowBothWays("sprk_analysis", ("sprk_documentid", Ref("sprk_document", document)));

        (await ExistingParentsAsync(promoted)).Should().Equal(promote.Parents);
        (await ExistingParentsAsync(promotedReversed)).Should().Equal(promote.Parents);
        (await ExistingParentsAsync(created)).Should().Equal(create.Parents);
    }

    // ── The email document writers ────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: an attachment document asks about its parent document first, then the associated record, as EmailAttachmentProcessor does")]
    public async Task AnAttachmentDocument_IsAskedAsEmailAttachmentProcessorAsks()
    {
        var (parentDocument, matter) = (Guid.NewGuid(), Guid.NewGuid());
        var writer = RecordOwnershipContext.ForParents(new[]
        {
            new RecordOwnershipParent("sprk_document", parentDocument), new RecordOwnershipParent("sprk_matter", matter),
        });
        var (row, reversed) = RowBothWays("sprk_document",
            ("sprk_matter", Ref("sprk_matter", matter)), ("sprk_parentdocument", Ref("sprk_document", parentDocument)));

        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
    }

    [Theory(DisplayName = "194b parity: an .eml document of a communication is owned as IncomingCommunicationProcessor owns it — like its communication's filing — in either attribute order")]
    [InlineData(true)]  // the communication kept its user owner (run-as-user, client-created, pre-146): looked through (I-6)
    [InlineData(false)] // the communication is team-owned by its filing's team
    public async Task AnEmlDocument_IsOwnedAsIncomingCommunicationProcessorOwnsIt(bool userOwnedCommunication)
    {
        var (matterInB, communication) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_matter", matterInB, ("owningteam", Ref("team", TeamB)), ("sprk_issecure", false));
        if (userOwnedCommunication)
            _world.UserOwnedChild("sprk_communication", communication, ("sprk_regardingmatter", "sprk_matter", matterInB));
        else
            _world.Add("sprk_communication", communication, ("owningteam", Ref("team", TeamB)), ("sprk_regardingmatter", Ref("sprk_matter", matterInB)));
        // The processor owns the archive with the owner it resolved for the communication: the records the email is filed to.
        var writerAnswer = await Resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new[] { new RecordOwnershipParent("sprk_matter", matterInB) }), CancellationToken.None);
        var (row, reversed) = RowBothWays("sprk_document",
            ("sprk_relatedcommunication", Ref("sprk_communication", communication)), ("sprk_matter", Ref("sprk_matter", matterInB)));
        var (onlyTheCommunication, _) = RowBothWays("sprk_document", ("sprk_relatedcommunication", Ref("sprk_communication", communication)));

        foreach (var document in new[] { row, reversed, onlyTheCommunication })
        {
            var question = await RecordOwnershipContext.ForExistingRowAsync(document, Entities, CoreAncestors, CancellationToken.None);
            (await Resolver.ResolveOwnerAsync(question.Context, CancellationToken.None)).OwningTeamId
                .Should().Be(writerAnswer.OwningTeamId).And.Be(TeamB);
        }
    }
}
