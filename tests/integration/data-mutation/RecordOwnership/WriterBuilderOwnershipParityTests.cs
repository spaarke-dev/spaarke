// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: UAC task 194b fix round, slice 3 — the reconciler's question about an EXISTING analysis or email document
//     (RecordOwnershipContext.ForExistingRowAsync) is the one its WRITER asked: pinned against the writers' OWN builders
//     (AppOnlyAnalysisService.OwnershipOfNewAnalysis, AnalysisEndpoints.OwnershipOfPromotedAnalysis,
//     EmailAttachmentProcessor.OwnershipOfAttachmentDocument, IncomingCommunicationProcessor.ArchivedEmlDocument and
//     IncomingAssociationResolver.OwnershipContextOf), never a copy of them, in either attribute order.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL resolver and the REAL CoreAncestorResolver over task 148's world.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Entity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

public class WriterBuilderOwnershipParityTests
{
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-0000001945d1");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-0000001945d2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);

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

    private async Task<ExistingRowOwnership> QuestionAsync(Entity row) =>
        await RecordOwnershipContext.ForExistingRowAsync(
            row, new ExistingRowReads(Entities, CoreAncestors, new AssignedAccessTestDoubles.FakeAssignedAccessStore()),
            CancellationToken.None);

    private async Task<IReadOnlyList<RecordOwnershipParent>> ExistingParentsAsync(Entity row)
    {
        var question = await QuestionAsync(row);
        question.Undetermined.Should().BeNull();
        return question.Context.Parents;
    }

    // ── The analysis create paths ─────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b parity: an analysis asks about its document first — AnalysisEndpoints' promote builder (document, then regarding) and the new-analysis builder (create route, AppOnlyAnalysisService, AnalysisResultPersistence)")]
    public async Task AnAnalysis_IsAskedAsItsWritersAsk()
    {
        var (document, matter) = (Guid.NewGuid(), Guid.NewGuid());
        var promote = AnalysisEndpoints.OwnershipOfPromotedAnalysis(document, "sprk_matter", matter);
        var create = AppOnlyAnalysisService.OwnershipOfNewAnalysis(document);
        var (promoted, promotedReversed) = RowBothWays("sprk_analysis",
            ("sprk_regardingmatter", Ref("sprk_matter", matter)), ("sprk_documentid", Ref("sprk_document", document)));
        var (created, _) = RowBothWays("sprk_analysis", ("sprk_documentid", Ref("sprk_document", document)));

        promote.Parents.Should().HaveCount(2).And.HaveElementAt(0, new RecordOwnershipParent("sprk_document", document));
        (await ExistingParentsAsync(promoted)).Should().Equal(promote.Parents);
        (await ExistingParentsAsync(promotedReversed)).Should().Equal(promote.Parents);
        (await ExistingParentsAsync(created)).Should().Equal(create.Parents);
    }

    [Fact(DisplayName = "194b parity: a promoted analysis with no document asks about its regarding record alone, as AnalysisEndpoints' builder does")]
    public async Task ADocumentlessPromotedAnalysis_IsAskedAboutItsRegardingRecord()
    {
        var matter = Guid.NewGuid();
        var (row, _) = RowBothWays("sprk_analysis", ("sprk_regardingmatter", Ref("sprk_matter", matter)));

        (await ExistingParentsAsync(row)).Should().Equal(AnalysisEndpoints.OwnershipOfPromotedAnalysis(null, "sprk_matter", matter).Parents);
    }

    // ── The inbound communication writer (fix round R, B1) ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "194b R (B1) parity must fire: an inbound communication whose decision writes the project FIRST and the matter is asked in sprk_communication's primary order — the writer's primary is the reconciler's (the matter), and both give the same owner, in either attribute order")]
    public async Task AnInboundCommunication_FiledProjectFirst_IsAskedInThePrimaryOrder()
    {
        var (matterInB, generalProject) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_matter", matterInB, ("owningteam", Ref("team", TeamB)), ("sprk_issecure", false));
        _world.Add("sprk_project", generalProject, ("owningteam", Ref("team", SecureChildShareWorld.GeneralTeam)), ("sprk_issecure", false));
        // The decision's RegardingWrites, project first (the order a rung wrote them in).
        var writes = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_regardingproject"] = Ref("sprk_project", generalProject),
            ["sprk_regardingmatter"] = Ref("sprk_matter", matterInB),
        };

        var writer = IncomingAssociationResolver.OwnershipContextOf(writes, []);
        var (row, reversed) = RowBothWays("sprk_communication",
            ("sprk_regardingproject", Ref("sprk_project", generalProject)), ("sprk_regardingmatter", Ref("sprk_matter", matterInB)));

        writer.Parents.Should().HaveElementAt(0, new RecordOwnershipParent("sprk_matter", matterInB),
            "the matter is sprk_communication's primary before the project");
        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
        var written = await Resolver.ResolveOwnerAsync(writer, CancellationToken.None);
        written.OwningTeamId.Should().Be(TeamB);
        (await Resolver.ResolveOwnerAsync((await QuestionAsync(row)).Context, CancellationToken.None)).OwningTeamId
            .Should().Be(written.OwningTeamId);
    }

    // ── The email document writers ────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "194b parity: an attachment document asks about its parent document first, then the associated record, as EmailAttachmentProcessor's builder does — whichever spelling the request used")]
    [InlineData("matter")]
    [InlineData("sprk_matter")]
    public async Task AnAttachmentDocument_IsAskedAsEmailAttachmentProcessorAsks(string associatedType)
    {
        var (parentDocument, matter) = (Guid.NewGuid(), Guid.NewGuid());
        var writer = EmailAttachmentProcessor.OwnershipOfAttachmentDocument(parentDocument, associatedType, matter);
        // The processor's update writes ParentDocumentLookup and, through DocumentAssociationMap, the matter's lookup.
        var (row, reversed) = RowBothWays("sprk_document",
            ("sprk_matter", Ref("sprk_matter", matter)), ("sprk_parentdocument", Ref("sprk_document", parentDocument)));

        writer.Parents.Should().HaveElementAt(0, new RecordOwnershipParent("sprk_document", parentDocument));
        (await ExistingParentsAsync(row)).Should().Equal(writer.Parents);
        (await ExistingParentsAsync(reversed)).Should().Equal(writer.Parents);
    }

    [Theory(DisplayName = "194b parity: an .eml document built by IncomingCommunicationProcessor is owned as its communication is, and the reconciler's question about it gives the same owner — in either attribute order")]
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

        // The communication's owner, as the inbound path asks it: its regarding write and the stamps derived from it.
        var stamps = (await CoreAncestors.ResolveStampsAsync("sprk_matter", matterInB, CancellationToken.None)).Stamps
            .Select(s => System.Collections.Generic.KeyValuePair.Create(s.LookupAttribute, Ref(s.EntityType, s.RecordId)));
        var communicationOwner = await Resolver.ResolveOwnerAsync(
            IncomingAssociationResolver.OwnershipContextOf([System.Collections.Generic.KeyValuePair.Create("sprk_regardingmatter", Ref("sprk_matter", matterInB))], stamps),
            CancellationToken.None);
        var built = IncomingCommunicationProcessor.ArchivedEmlDocument(
            new Message { Subject = "Re: terms" }, "email.eml", communication, "item-1", "drive-1", communicationOwner);

        var writtenOwner = built.GetAttributeValue<EntityReference>("ownerid");
        writtenOwner.Should().Be(Ref("team", TeamB), "the archive is owned exactly as its communication is");
        var lookups = built.Attributes.Where(a => a.Value is EntityReference && a.Key != "ownerid")
            .Select(a => (a.Key, a.Value)).ToArray();
        lookups.Select(l => l.Key).Should().Equal(new[] { "sprk_relatedcommunication" }, "the builder files it under its communication alone");
        var (row, reversed) = RowBothWays("sprk_document", lookups.Append(("sprk_matter", (object)Ref("sprk_matter", matterInB))).ToArray());
        var (asBuilt, _) = RowBothWays("sprk_document", lookups);

        foreach (var document in new[] { asBuilt, row, reversed })
        {
            var question = await QuestionAsync(document);
            (await Resolver.ResolveOwnerAsync(question.Context, CancellationToken.None)).OwningTeamId.Should().Be(writtenOwner.Id);
        }
    }
}
