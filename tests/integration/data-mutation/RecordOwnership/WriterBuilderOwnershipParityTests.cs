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
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
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

    // ── field-mapping-server-write-path-r1 task 042b (FR-41, #2483, DI-009): the ONE server create against the reconciler ──
    //
    // For every stamped table the record-creation registry creates (CoreAncestorResolver.IsStampedChildEntity), a row filed
    // under a TEAM-OWNED intermediate (an invoice) of a Secure matter or of an ordinary matter is created through the REAL
    // RecordCreationService (as the system principal: step 6½ and step 7 run for every principal, and its derived stamp is
    // bound by the post-create re-derivation, so the written row is what Dataverse holds), and the owner it wrote is compared
    // with the reconciler's answer for that written row (RecordOwnershipContext.ForExistingRowAsync; a message in a record
    // thread also takes its thread's filing, as SecureChildReconciler.ContextFor does). A create the service refuses writes
    // nothing, so it can never disagree with the reconciler; the refusal is pinned with its code.

    /// <summary>The tables the theory covers: every registry table that carries the I-1 stamp, asserted complete below.</summary>
    public static readonly TheoryData<string> StampedRegistryTables = new()
    {
        "sprk_todo", "sprk_event", "sprk_memo", "sprk_analysis", "sprk_communication", "sprk_signal", "sprk_decisionrecord",
    };

    [Fact(DisplayName = "042b parity: the theory covers every RecordTableRules table that carries the I-1 stamp")]
    public void TheParityTheory_CoversEveryStampedRegistryTable()
    {
        var registered = RecordTableRules.All.Select(r => r.Table)
            .Where(CoreAncestorResolver.IsStampedChildEntity)
            .Order(StringComparer.Ordinal);

        registered.Should().Equal(StampedRegistryTables.Select(t => (string)t[0]).Order(StringComparer.Ordinal));
    }

    [Theory(DisplayName = "042b parity (secure world): a row filed under a team-owned invoice of a Secure matter is owned by the Secure team at create, as the reconciler owns the written row")]
    [MemberData(nameof(StampedRegistryTables))]
    public async Task ACreateUnderATeamOwnedIntermediateOfASecureMatter_IsOwnedAsTheReconcilerOwnsTheWrittenRow(string table)
    {
        var world = new CreateParityWorld();
        world.InvoiceUnder(world.SecureMatter);

        var (outcome, written) = await world.CreateAsync(table, ("sprk_regardinginvoice", "sprk_invoice", CreateParityWorld.Invoice));
        var reconciler = await world.ReconcilerAnswerAsync(written ?? world.RowAsItWouldBeWritten(table,
            ("sprk_regardinginvoice", "sprk_invoice", CreateParityWorld.Invoice), ("sprk_regardingmatter", "sprk_matter", world.SecureMatter)));

        reconciler.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "the reconciler's question includes the stamp's root");
        if (CoreAncestorResolver.IsStampedOutsideAccessTaxonomy(table))
        {
            // A Signal or Decision Record is not a reparentable child: the service refuses a Secure owner for it with the
            // existing words (OwnerDecided; its own writer owns it into isolation). Nothing is written, so the reconciler
            // never sees an ordinary row (before FR-41 the create wrote ChildTeam: the #2483 window on these tables too).
            outcome.Refusal.Should().NotBeNull();
            outcome.Refusal!.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
            outcome.Refusal.OwnerDecision!.IsSecureOwner.Should().BeTrue();
            world.Creates.Should().BeEmpty();
            return;
        }

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        written.Should().NotBeNull();
        written!.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(world.SecureMatter, "the stamp is bound");
        outcome.OwningTeamId.Should().Be(reconciler.OwningTeamId, "the create asks the reconciler's question (#2483)");
    }

    [Theory(DisplayName = "042b parity (ordinary world): a row filed under a team-owned invoice of an ordinary matter in another business unit keeps the invoice's team, as the reconciler does")]
    [MemberData(nameof(StampedRegistryTables))]
    public async Task ACreateUnderATeamOwnedIntermediateOfAnOrdinaryMatter_IsOwnedAsTheReconcilerOwnsTheWrittenRow(string table)
    {
        var world = new CreateParityWorld();
        world.InvoiceUnder(world.MatterInY);

        var (outcome, written) = await world.CreateAsync(table, ("sprk_regardinginvoice", "sprk_invoice", CreateParityWorld.Invoice));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        written!.GetAttributeValue<EntityReference>("sprk_regardingmatter")?.Id.Should().Be(world.MatterInY);
        var reconciler = await world.ReconcilerAnswerAsync(written);
        reconciler.OwningTeamId.Should().Be(Directory.ChildTeam, "the invoice ranks above the matter on every stamped table");
        outcome.OwningTeamId.Should().Be(reconciler.OwningTeamId);
    }

    /// <summary>The report-card column of each table the report card files (memo's is sprk_reportcard; SecureChildLineage).</summary>
    public static readonly TheoryData<string, string> ReportCardChildren = new()
    {
        { "sprk_todo", "sprk_regardingreportcard" },
        { "sprk_event", "sprk_regardingreportcard" },
        { "sprk_communication", "sprk_regardingreportcard" },
        { "sprk_memo", "sprk_reportcard" },
    };

    [Theory(DisplayName = "042b parity (change 1): a row with no pair under a team-owned report card in X whose matter is in Y is owned by Y's team at create, as the reconciler owns it")]
    [MemberData(nameof(ReportCardChildren))]
    public async Task ACreateUnderAReportCard_IsOwnedByItsMattersTeam_AsTheReconcilerOwnsIt(string table, string column)
    {
        var world = new CreateParityWorld();
        world.ReportCardUnder(world.MatterInY);

        var (outcome, written) = await world.CreateAsync(table, (column, "sprk_reportcard", CreateParityWorld.ReportCard));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.OwningTeamId.Should().Be(Directory.CustomerChildTeam,
            "the matter (Y) ranks above the report card (X) in the table's lineage order (change 1, owner 2026-11-02)");
        (await world.ReconcilerAnswerAsync(written!)).OwningTeamId.Should().Be(outcome.OwningTeamId);
    }

    [Theory(DisplayName = "042b parity (row 7, UAC-r2): a report-card child whose matter's business unit has no default team is refused at create (409) and refused by the reconciler, with the same code")]
    [MemberData(nameof(ReportCardChildren))]
    public async Task ACreateUnderAReportCardWhoseMattersUnitHasNoDefaultTeam_IsRefused_AsTheReconcilerRefusesIt(string table, string column)
    {
        var world = new CreateParityWorld();
        world.ReportCardUnder(world.MatterInAUnitWithNoDefaultTeam);

        var (outcome, _) = await world.CreateAsync(table, (column, "sprk_reportcard", CreateParityWorld.ReportCard));
        var reconciler = await world.ReconcilerAnswerAsync(world.RowAsItWouldBeWritten(table,
            (column, "sprk_reportcard", CreateParityWorld.ReportCard),
            ("sprk_regardingmatter", "sprk_matter", world.MatterInAUnitWithNoDefaultTeam)));

        outcome.Refusal.Should().NotBeNull("the matter's unit has no default team to own the row");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved, "a 409");
        world.Creates.Should().BeEmpty();
        reconciler.IsOwned.Should().BeFalse("the reconciler asks the same question of the written row");
        reconciler.RefusalCode.Should().Be(outcome.Refusal.Code);
    }

    [Fact(DisplayName = "042b parity (Q3, UAC-r2): a communication created in a record thread of a Secure matter, filed under an ordinary matter, is owned by the Secure team at create, as the reconciler owns it with the thread's filing")]
    public async Task ACommunicationCreatedInASecureRecordThread_IsOwnedAsTheReconcilerOwnsItWithTheThreadsFiling()
    {
        var world = new CreateParityWorld();
        world.RecordThreadUnder(world.SecureMatter);

        var (outcome, written) = await world.CreateAsync("sprk_communication",
            ("sprk_regardingmatter", "sprk_matter", world.MatterInY),
            ("sprk_communicationthread", "sprk_communicationthread", CreateParityWorld.Thread));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var reconciler = await world.ReconcilerAnswerAsync(written!);
        reconciler.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "the thread files the message under the Secure matter (S6)");
        outcome.OwningTeamId.Should().Be(reconciler.OwningTeamId, "the create asks the thread's filing too (Q3)");
    }

    /// <summary>
    /// The REAL <see cref="RecordCreationService"/> over an
    /// <see cref="Directory"/> (the resolver's rows), a <see cref="CoreAncestorStamping.StampWorld"/> (the I-1 derivation and
    /// the post-create re-stamp) and the standard share world; the app-only create materializes the row it is asked to write
    /// into the stamp world, so the post-create re-derivation stamps it there and the written row is read back from it.
    /// </summary>
    private sealed class CreateParityWorld
    {
        public static readonly Guid Invoice = Guid.Parse("0b000000-0000-4000-8000-000000042b01");
        public static readonly Guid ReportCard = Guid.Parse("0b000000-0000-4000-8000-000000042b02");
        public static readonly Guid Thread = Guid.Parse("0b000000-0000-4000-8000-000000042b03");
        private static readonly Guid NoTeamUnit = Guid.Parse("0b000000-0000-4000-8000-000000042b04");

        public Guid SecureMatter { get; } = Guid.Parse("0b000000-0000-4000-8000-000000042b11");
        public Guid MatterInY { get; } = Guid.Parse("0b000000-0000-4000-8000-000000042b12");
        public Guid MatterInAUnitWithNoDefaultTeam { get; } = Guid.Parse("0b000000-0000-4000-8000-000000042b13");

        private readonly Directory _directory;
        private readonly Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld _stamps = new();
        private readonly Mock<IFieldMappingDataverseService> _appOnly =
            new(MockBehavior.Strict);

        public List<(string Table, Guid Id)> Creates { get; } = [];

        public CreateParityWorld()
        {
            _directory = Directory.Standard()
                .WithSecureRoot("sprk_matter", SecureMatter)
                .WithRecord("sprk_matter", MatterInY, Directory.CustomerChildBu, isSecure: false, owningTeam: Directory.CustomerChildTeam)
                .WithBusinessUnit(NoTeamUnit, "No default team", parent: Directory.ChildBu)
                .WithRecord("sprk_matter", MatterInAUnitWithNoDefaultTeam, NoTeamUnit, isSecure: false);
            foreach (var matter in new[] { SecureMatter, MatterInY, MatterInAUnitWithNoDefaultTeam })
                _stamps.Row("sprk_matter", matter);

            _appOnly
                .Setup(a => a.UpdateRecordFieldsAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(),
                    It.IsAny<CancellationToken>(), null))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, _, _, _) =>
                {
                    Creates.Add((table, id));
                    _stamps.Row(table, id, [.. _lookups]); // the row as Dataverse stores it: the lookups the body binds
                })
                .Returns(Task.CompletedTask);
        }

        private (string Column, string Target, Guid Id)[] _lookups = [];

        /// <summary>The invoice, team-owned by the child unit's team (not yet reached by Make Secure), filed under <paramref name="matter"/>.</summary>
        public void InvoiceUnder(Guid matter)
        {
            _directory.WithRecord("sprk_invoice", Invoice, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", matter) });
            _stamps.Row("sprk_invoice", Invoice, [("sprk_matter", "sprk_matter", matter)]);
        }

        /// <summary>The report card, team-owned in the child unit (X), filed under <paramref name="matter"/>.</summary>
        public void ReportCardUnder(Guid matter)
        {
            _directory.WithRecord("sprk_reportcard", ReportCard, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
            _stamps.Row("sprk_reportcard", ReportCard, [("sprk_regardingmatter", "sprk_matter", matter)]);
        }

        /// <summary>A RECORD thread (type not Direct) filed under <paramref name="matter"/>.</summary>
        public void RecordThreadUnder(Guid matter) =>
            _directory.WithRecord(RecordThreadFiling.ThreadTable, Thread, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new()
                {
                    ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
                    [RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(100000000),
                });

        /// <summary>Creates a <paramref name="table"/> row naming <paramref name="lookups"/>, as the system principal; the written row (null when refused).</summary>
        public async Task<(RecordCreationOutcome Outcome, Entity? Written)> CreateAsync(
            string table, params (string Column, string Target, Guid Id)[] lookups)
        {
            _lookups = lookups;
            var item = new DataverseWriteItemMapper.MappedItem(
                System.Text.Json.JsonSerializer.Serialize(lookups.ToDictionary(
                    l => $"{l.Column}@odata.bind", l => $"/{l.Target}s({l.Id:D})")),
                [.. lookups.Select(l => l.Column)])
            {
                Lookups = [.. lookups.Select(l => new DataverseWriteItemMapper.MappedLookup(l.Column, l.Target, l.Target + "s", l.Id))],
            };

            var outcome = await RouteRecordCreation.Over(
                    _directory.Resolver(), _appOnly.Object, _stamps.Restamper,
                    SecureChildShareWorld.Standard().Synchronizer(new FakeRecordShareTable()),
                    Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(), entities: Entities())
                .CreateAsync(
                    new RecordCreationRequest(
                        table, new RecordCreationPayload.Mapped(item)),
                    RecordCreationPrincipal.ForSystem(), CancellationToken.None);
            return (outcome, outcome.CreatedId is { } id ? _stamps.RowOf(table, id) : null);
        }

        /// <summary>A row of <paramref name="table"/> holding <paramref name="lookups"/> (for a create that was refused).</summary>
        public Entity RowAsItWouldBeWritten(string table, params (string Column, string Target, Guid Id)[] lookups)
        {
            var row = new Entity(table, Guid.NewGuid());
            foreach (var (column, target, id) in lookups)
                row[column] = new EntityReference(target, id);
            return row;
        }

        /// <summary>
        /// The reconciler's answer for <paramref name="written"/>: its question (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>),
        /// with a message's record-thread filing as <c>SecureChildReconciler.ContextFor</c> adds it, through the same resolver.
        /// </summary>
        public async Task<RecordOwnerResolution> ReconcilerAnswerAsync(Entity written)
        {
            var question = await RecordOwnershipContext.ForExistingRowAsync(
                written, new ExistingRowReads(Entities(), null, null), CancellationToken.None);
            question.Undetermined.Should().BeNull();
            var context = question.Context;
            if (RecordThreadFiling.ThreadOf(written) is { } thread)
            {
                context = context with
                {
                    Parents = RecordThreadFiling.QuestionWithThreadFiling(
                            written, RecordThreadFiling.ParentsOf(_directory.Row(RecordThreadFiling.ThreadTable, thread)))
                        .Parents.Distinct().ToArray(),
                };
            }

            return await _directory.Resolver().ResolveOwnerAsync(context, CancellationToken.None);
        }

        /// <summary>The service's own app-only reads: the directory's rows and queries (a thread, a team's unit).</summary>
        private IGenericEntityService Entities()
        {
            var entities = new Mock<IGenericEntityService>();
            entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) => _directory.Answer(query));
            entities
                .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => _directory.Row(table, id));
            return entities.Object;
        }
    }
}
