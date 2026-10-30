// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/RecordOwnership/**`
//   - Justification: #2468 — a document whose primary parent is a communication filed under NOTHING (an unfiled capture keeps
//     its creating identity, E1: the application user or a root team) is not owned from that communication: it counts for
//     secure-if-any only and the document takes its question's unfiled answer — an Office save the saver's team (task 080), the
//     reconciler the owner it has. A FILED communication still names its documents' owner (the family rule). Pinned through the
//     writer's question (OfficeDocumentPersistence.DocumentOwnershipOf) and the reconciler's (ForExistingRowAsync, asked as
//     SecureChildReconciler.ContextFor asks it), over the REAL resolver.
//
// Doubles are module boundaries only (ADR-038 §4): the shared ownership directory and the secure-child world.

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

[Trait("status", "issue-2468")]
public class UnfiledCommunicationDocumentOwnerTests
{
    private static readonly Guid RootBu = FakeDirectory.GeneralBu;       // "Spaarke", the root business unit
    private static readonly Guid RootTeam = FakeDirectory.GeneralTeam;   // its default team
    private static readonly Guid ChildBu = FakeDirectory.ChildBu;        // the saver's unit
    private static readonly Guid ChildTeam = FakeDirectory.ChildTeam;
    private static readonly Guid OtherBu = FakeDirectory.CustomerChildBu; // another unit, holding a matter
    private static readonly Guid OtherTeam = FakeDirectory.CustomerChildTeam;

    private static readonly Guid Saver = Guid.Parse("24680000-0000-4000-8000-000000000001");
    private static readonly Guid SaverOid = Guid.Parse("24680000-0000-4000-8000-000000000002");
    private static readonly Guid Communication = Guid.Parse("24680000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherMatter = Guid.Parse("24680000-0000-4000-8000-0000000000a1");
    private static readonly Guid RootMatter = Guid.Parse("24680000-0000-4000-8000-0000000000a2");
    private static readonly Guid Document = Guid.Parse("24680000-0000-4000-8000-0000000000d1");

    /// <summary>The owner shapes an unfiled capture has on dev (2026-10-29): the application user, or the root team.</summary>
    public enum UnfiledOwner { ApplicationUser, RootTeam }

    private static FakeDirectory World() => FakeDirectory.Standard()
        .WithUser(Saver, SaverOid, ChildBu)
        .WithRecord("sprk_matter", OtherMatter, OtherBu, owningTeam: OtherTeam)
        .WithRecord("sprk_matter", RootMatter, RootBu, owningTeam: RootTeam);

    private static FakeDirectory WithCommunication(
        FakeDirectory world, Guid businessUnit, Guid? owningTeam, Dictionary<string, object>? filing = null) =>
        world.WithRecord("sprk_communication", Communication, businessUnit, owningTeam: owningTeam, extra: filing);

    private static FakeDirectory WithUnfiledCommunication(FakeDirectory world, UnfiledOwner owner) =>
        WithCommunication(world, RootBu, owner == UnfiledOwner.RootTeam ? RootTeam : null);

    /// <summary>The writer's question for an Office save of the email (the .eml or an attachment).</summary>
    private static RecordOwnershipContext SaveQuestion(string? associationType = null, Guid? associationId = null) =>
        OfficeDocumentPersistence.DocumentOwnershipOf(associationType, associationId, Communication, SaverOid);

    /// <summary>The document as that save writes it.</summary>
    private static Entity WrittenDocument(string? associationColumn = null, Guid? associationId = null)
    {
        var row = new Entity("sprk_document", Document)
        {
            [OfficeDocumentPersistence.RelatedCommunicationColumn] = new EntityReference("sprk_communication", Communication),
        };
        if (associationColumn is not null)
            row[associationColumn] = new EntityReference("sprk_matter", associationId!.Value);
        return row;
    }

    /// <summary>The reconciler's question for the written row, as <c>SecureChildReconciler.ContextFor</c> asks it.</summary>
    private static async Task<RecordOwnershipContext> ReconcilerQuestionAsync(Entity written)
    {
        var question = await RecordOwnershipContext.ForExistingRowAsync(
            written, new ExistingRowReads(Moq.Mock.Of<IGenericEntityService>(), null, null), CancellationToken.None);
        return new RecordOwnershipContext
        {
            Parents = question.Context.Parents,
            SecureOnlyParents = question.Context.SecureOnlyParents,
            ChildEntityLogicalName = written.LogicalName,
            WhenUnfiled = UnfiledOwnership.KeepCreator,
        };
    }

    // ── Must fail before #2468: an unfiled save of an unfiled email ─────────────────────────────────────────────────

    [Theory(DisplayName = "#2468 must-fire: an unfiled save of an unfiled email is owned by the saver's team, not the root's")]
    [InlineData(UnfiledOwner.ApplicationUser)]
    [InlineData(UnfiledOwner.RootTeam)]
    public async Task AnUnfiledSaveOfAnUnfiledEmail_IsOwnedByTheSaversTeam(UnfiledOwner owner)
    {
        var resolver = WithUnfiledCommunication(World(), owner).Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(ChildTeam,
            "an unfiled communication keeps a technical owner in the root; it never names its documents' owner (task 080)");
        answer.IsSecureOwner.Should().BeFalse();
    }

    [Theory(DisplayName = "#2468 must-fire: the reconciler's question for that written document keeps it where the save put it")]
    [InlineData(UnfiledOwner.ApplicationUser)]
    [InlineData(UnfiledOwner.RootTeam)]
    public async Task TheReconcilersQuestionForTheWrittenDocument_LeavesItsOwnerUnchanged(UnfiledOwner owner)
    {
        var resolver = WithUnfiledCommunication(World(), owner).Resolver();
        var written = WrittenDocument();
        var reconciler = await ReconcilerQuestionAsync(written);

        var answer = await resolver.ResolveOwnerAsync(reconciler, CancellationToken.None);

        reconciler.Parents.Should().Equal(SaveQuestion().Parents, "one question: the writer's and the reconciler's");
        reconciler.SecureOnlyParents.Should().Equal(SaveQuestion().SecureOnlyParents);
        answer.Outcome.Should().Be(RecordOwnerOutcome.Unchanged,
            "the reconciler never moves the saver's document to the root team");
    }

    [Fact(DisplayName = "#2468 must-fire: an email in a Direct thread only is unfiled; the save is the saver's team")]
    public async Task AnEmailInADirectThreadOnly_IsUnfiled_SoTheSaveIsTheSaversTeam()
    {
        var thread = Guid.NewGuid();
        var resolver = WithCommunication(World(), RootBu, RootTeam, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread),
            })
            .WithRecord("sprk_communicationthread", thread, RootBu, owningTeam: RootTeam, extra: new()
            {
                ["sprk_threadtype"] = new OptionSetValue(RecordThreadFiling.DirectThreadType),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter), // a Direct thread files nothing
            })
            .Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(ChildTeam);
    }

    // ── Secure-if-any and fail-closed: unchanged ───────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "#2468 must-not-fire: a Secure unfiled communication still makes its documents Secure (writer and reconciler)")]
    public async Task ASecureUnfiledCommunication_StillMakesItsDocumentsSecure()
    {
        var resolver = WithCommunication(World(), FakeDirectory.SecureBu, FakeDirectory.SecureNamedTeam).Resolver();

        var writer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);
        var reconciler = await resolver.ResolveOwnerAsync(await ReconcilerQuestionAsync(WrittenDocument()), CancellationToken.None);

        writer.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam);
        writer.IsSecureOwner.Should().BeTrue();
        reconciler.OwningTeamId.Should().Be(FakeDirectory.SecureNamedTeam);
    }

    [Fact(DisplayName = "#2468 must-not-fire: a communication that cannot be found refuses the save")]
    public async Task ACommunicationThatCannotBeFound_RefusesTheSave()
    {
        var answer = await World().Resolver().ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact(DisplayName = "#2468 must-not-fire: a communication whose thread cannot be read refuses, never the unfiled answer")]
    public async Task ACommunicationWhoseThreadCannotBeRead_Refuses()
    {
        var resolver = WithCommunication(World(), RootBu, RootTeam, new()
        {
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", Guid.NewGuid()), // not seeded
        }).Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        answer.RefusalCode.Should().Be(RecordOwnerRefusal.ParentUnresolved);
    }

    [Fact(DisplayName = "#2468 must-not-fire: a faulted filing read propagates; no owner is given")]
    public async Task AFaultedFilingRead_Propagates_NoOwnerIsGiven()
    {
        var directory = WithUnfiledCommunication(World(), UnfiledOwner.ApplicationUser);
        var resolver = directory.Resolver(fault: new TimeoutException("Dataverse timed out"));

        var act = () => resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>("a fault is not an answer: never the saver's team on a guess");
    }

    [Fact(DisplayName = "#2468 must-not-fire: a fault on the communication's filing read ALONE propagates; no owner is looked up")]
    public async Task AFaultOnTheCommunicationsFilingReadAlone_Propagates_AndNoOwnerIsLookedUp()
    {
        // Review F3-1: only the communication's whole-row (filing) read faults; its owner-facts read and every team, user and
        // business-unit read answer. A filing read whose fault were taken as "unfiled" would go on to the saver's team. The
        // root-team shape: a team-owned communication is never re-read by the secure lineage walk, so nothing else faults.
        var directory = WithUnfiledCommunication(World(), UnfiledOwner.RootTeam);
        var queried = new List<string>();
        var entities = new Moq.Mock<IGenericEntityService>(Moq.MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), Moq.It.IsAny<CancellationToken>()))
            .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) =>
            {
                queried.Add(query.EntityName);
                return query.EntityName == "sprk_communication" && query.ColumnSet.AllColumns
                    ? Task.FromException<EntityCollection>(new TimeoutException("the filing read timed out"))
                    : Task.FromResult(directory.Answer(query));
            });
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureRecord:BusinessUnitName"] = FakeDirectory.SecureBuName,
                ["SecureRecord:OwnerTeamName"] = FakeDirectory.SecureOwnerTeamName,
            })
            .Build();
        var resolver = new RecordOwnershipResolver(
            entities.Object, configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordOwnershipResolver>.Instance);

        var act = () => resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>("the filing read's fault is not an answer");
        queried.Should().NotContain(new[] { "team", "systemuser" },
            "no owner is looked up once whether the communication is filed cannot be read");
    }

    [Fact(DisplayName = "#2471 follow-up must-not-fire: a thread read that FAULTS propagates; it is not taken as a missing thread")]
    public async Task AThreadReadThatFaults_Propagates_ItIsNotTakenAsAMissingThread()
    {
        // The thread read alone faults, with the exception type the old catch swallowed into a refusal. A fault is not "the row
        // is missing": it propagates (fail closed, as every resolver read does), and no owner is looked up.
        var thread = Guid.NewGuid();
        var directory = WithCommunication(World(), RootBu, RootTeam, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread),
            })
            .WithRecord("sprk_communicationthread", thread, RootBu, owningTeam: RootTeam);
        var queried = new List<string>();
        var resolver = StrictResolverOver(directory, query =>
        {
            queried.Add(query.EntityName);
            return query.EntityName == RecordThreadFiling.ThreadTable
                ? new InvalidOperationException("the thread read faulted")
                : null;
        });

        var act = () => resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("a faulted read is not an answer, and not a missing row");
        queried.Should().NotContain(new[] { "team", "systemuser" }, "no owner is looked up once the thread read faulted");
    }

    [Fact(DisplayName = "#2471 follow-up must-not-fire: a thread that is not there (zero rows) refuses, decided without an exception")]
    public async Task AThreadThatIsNotThere_Refuses_DecidedFromTheZeroRowResult()
    {
        var directory = WithCommunication(World(), RootBu, RootTeam, new()
        {
            ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", Guid.NewGuid()), // not seeded
        });
        var threadReads = 0;
        var resolver = StrictResolverOver(directory, query =>
        {
            if (query.EntityName == RecordThreadFiling.ThreadTable)
                threadReads++;
            return null;
        });

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        threadReads.Should().Be(1, "the thread was asked for and came back with no row");
        answer.Outcome.Should().Be(RecordOwnerOutcome.Refused, "a missing thread's filing is not known: never the unfiled answer");
    }

    /// <summary>The real resolver over <paramref name="directory"/>; <paramref name="faultFor"/> may fault one query.</summary>
    private static RecordOwnershipResolver StrictResolverOver(
        FakeDirectory directory, Func<Microsoft.Xrm.Sdk.Query.QueryExpression, Exception?> faultFor)
    {
        var entities = new Moq.Mock<IGenericEntityService>(Moq.MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(Moq.It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), Moq.It.IsAny<CancellationToken>()))
            .Returns((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) =>
                faultFor(query) is { } fault
                    ? Task.FromException<EntityCollection>(fault)
                    : Task.FromResult(directory.Answer(query)));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureRecord:BusinessUnitName"] = FakeDirectory.SecureBuName,
                ["SecureRecord:OwnerTeamName"] = FakeDirectory.SecureOwnerTeamName,
            })
            .Build();
        return new RecordOwnershipResolver(
            entities.Object, configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<RecordOwnershipResolver>.Instance);
    }

    [Theory(DisplayName = "#2468 must-not-fire: a communication filed only through its record thread names its documents' owner")]
    [InlineData(null)]
    [InlineData(100000000)]
    public async Task ACommunicationFiledOnlyThroughItsRecordThread_NamesItsDocumentsOwner(int? threadType)
    {
        // Review F3-2: no ownership lookup of its own; its thread (a record thread, or type unset: counted as one) regards a
        // matter in another unit.
        var thread = Guid.NewGuid();
        var threadColumns = new Dictionary<string, object>
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter),
        };
        if (threadType is { } type)
            threadColumns["sprk_threadtype"] = new OptionSetValue(type);
        var resolver = WithCommunication(World(), OtherBu, OtherTeam, new()
            {
                ["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", thread),
            })
            .WithRecord("sprk_communicationthread", thread, OtherBu, owningTeam: OtherTeam, extra: threadColumns)
            .Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(OtherTeam, "a filed communication (here through its record thread) names the family's owner");
    }

    // ── A filed communication still names its documents' owner (the family rule) ───────────────────────────────────

    [Fact(DisplayName = "#2468 intended: an unassociated save of an email filed to another unit's record goes to that unit's team")]
    public async Task AnUnassociatedSaveOfAnEmailFiledToAnotherUnitsRecord_GoesToThatUnitsTeam_TheFamilyRule()
    {
        var resolver = WithCommunication(World(), OtherBu, OtherTeam, new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter),
        }).Resolver();

        var writer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);
        var reconciler = await resolver.ResolveOwnerAsync(await ReconcilerQuestionAsync(WrittenDocument()), CancellationToken.None);

        writer.OwningTeamId.Should().Be(OtherTeam, "a family shares one ownership: the email's documents follow the filed email");
        reconciler.OwningTeamId.Should().Be(OtherTeam);
    }

    [Fact(DisplayName = "#2468 must-not-fire: a root-team communication FILED to a root matter is followed, never the saver's team")]
    public async Task ARootTeamCommunicationFiledToARootMatter_IsFollowed_NotTheSaversTeam()
    {
        var resolver = WithCommunication(World(), RootBu, RootTeam, new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RootMatter),
        }).Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(RootTeam, "owner kind never decides 'unfiled': the filing does");
    }

    [Fact(DisplayName = "#2468 must-not-fire: an app-user communication FILED to a matter (before any owner move) is followed through its filing")]
    public async Task AnAppUserCommunicationFiledToAMatter_IsFollowedThroughItsFiling_NotTheSaversTeam()
    {
        // As FileIfUnfiledAsync leaves it: the regarding is written, the owner not yet moved.
        var resolver = WithCommunication(World(), RootBu, owningTeam: null, new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter),
        }).Resolver();

        var answer = await resolver.ResolveOwnerAsync(SaveQuestion(), CancellationToken.None);

        answer.OwningTeamId.Should().Be(OtherTeam);
    }

    [Theory(DisplayName = "#2468 must-not-fire: a filed Office save is owned from its association, whatever the email's owner")]
    [InlineData(UnfiledOwner.ApplicationUser)]
    [InlineData(UnfiledOwner.RootTeam)]
    public async Task AFiledSave_IsOwnedFromItsAssociation(UnfiledOwner owner)
    {
        var resolver = WithUnfiledCommunication(World(), owner).Resolver();

        var writer = await resolver.ResolveOwnerAsync(SaveQuestion("sprk_matter", OtherMatter), CancellationToken.None);
        var reconciler = await resolver.ResolveOwnerAsync(
            await ReconcilerQuestionAsync(WrittenDocument("sprk_matter", OtherMatter)), CancellationToken.None);

        writer.OwningTeamId.Should().Be(OtherTeam);
        reconciler.OwningTeamId.Should().Be(OtherTeam);
    }

    // ── Content rows of a communication (inbound, archive, materialized attachments): unchanged ───────────────────

    [Fact(DisplayName = "#2468 must-not-fire: a ContentOf document of an unfiled app-user email keeps its creator, as before")]
    public async Task AContentOfDocumentOfAnUnfiledAppUserEmail_KeepsItsCreator()
    {
        var resolver = WithUnfiledCommunication(World(), UnfiledOwner.ApplicationUser).Resolver();

        var answer = await resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", Communication), CancellationToken.None);

        answer.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "E1: content of an unfiled communication keeps its creator");
    }

    [Fact(DisplayName = "#2468 must-not-fire: a ContentOf document of a team-owned communication follows it, as before")]
    public async Task AContentOfDocumentOfATeamOwnedEmail_FollowsIt()
    {
        var resolver = WithCommunication(World(), OtherBu, OtherTeam, new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatter),
        }).Resolver();

        var answer = await resolver.ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf("sprk_communication", Communication), CancellationToken.None);

        answer.OwningTeamId.Should().Be(OtherTeam);
    }
}

/// <summary>
/// #2468: Unsecure of a Secure matter still carries its email's inbound/archive documents (which hold only
/// <c>sprk_relatedcommunication</c>) out of isolation — the REAL reconciler and resolver over one in-memory Dataverse. A rule that
/// made the communication a Secure-only parent of every document would leave them isolated.
/// </summary>
[Trait("status", "issue-2468")]
public class UnfiledCommunicationDocumentUnsecureTests
{
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    [Fact(DisplayName = "#2468 must-not-fire: Unsecure carries a Secure matter's inbound email documents out of isolation")]
    public async Task Unsecure_CarriesAMattersInboundEmailDocumentsOutOfIsolation()
    {
        var (matter, communication, document) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", matter)
            .SecureChild("sprk_communication", communication, ("sprk_regardingmatter", "sprk_matter", matter))
            .SecureChild("sprk_document", document, ("sprk_relatedcommunication", "sprk_communication", communication));

        var report = await SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi).ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_communication", communication).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam));
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureChildShareWorld.GeneralTeam),
            "the email's documents leave isolation with it: a filed communication names its documents' owner");
    }
}

/// <summary>
/// #2468 review K-1: one REAL ordinary pass (<see cref="SecureChildReconciler.ReconcileOrdinaryParentMoveAsync"/>) reaches an
/// unfiled save's document (a copy of a canonical filed under a matter that moved) whose communication is unfiled: the
/// reconciler's answer for it is "unchanged", so the saver's document is left where the save put it, never moved to the root.
/// </summary>
[Trait("status", "issue-2468")]
public class UnfiledCommunicationDocumentOrdinaryPassTests
{
    private static readonly Guid BuB = Guid.Parse("24680000-0000-4000-8000-0000000000b1");
    private static readonly Guid TeamB = Guid.Parse("24680000-0000-4000-8000-0000000000b2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);

    public enum UnfiledOwner { ApplicationUser, RootTeam }

    [Theory(DisplayName = "#2468 must-fire: a real ordinary pass leaves an unfiled save's document alone when its email is unfiled")]
    [InlineData(UnfiledOwner.ApplicationUser)]
    [InlineData(UnfiledOwner.RootTeam)]
    public async Task ARealOrdinaryPass_LeavesAnUnfiledSavesDocumentAlone(UnfiledOwner owner)
    {
        var (matter, canonical, copy, communication) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var saverTeam = SecureChildShareWorld.CustomerTeam; // the saver's unit (a child of the root): its default team
        _world.Add("sprk_matter", matter, ("owningteam", new EntityReference("team", TeamB)), ("sprk_issecure", false)); // moved to B
        _world.Add("sprk_document", canonical, ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam)),
            ("sprk_matter", new EntityReference("sprk_matter", matter)), ("versionnumber", 1L));
        _world.Add("sprk_communication", communication, owner == UnfiledOwner.RootTeam
            ? ("owningteam", new EntityReference("team", SecureChildShareWorld.GeneralTeam))
            : ("owninguser", new EntityReference("systemuser", SecureChildShareWorld.SomeUser)));
        _world.Add("sprk_document", copy, ("owningteam", new EntityReference("team", saverTeam)),
            ("sprk_canonicaldocument", new EntityReference("sprk_document", canonical)),
            ("sprk_relatedcommunication", new EntityReference("sprk_communication", communication)), ("versionnumber", 1L));
        var webApi = new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(() => _world);

        var report = await SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), webApi)
            .ReconcileOrdinaryParentMoveAsync("sprk_matter", matter, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_document", canonical).Should().Be(DataversePrincipalRef.Team(TeamB),
            "the pass ran and reached the matter's document");
        _world.OwnerOf("sprk_document", copy).Should().Be(DataversePrincipalRef.Team(saverTeam),
            "the saver's document stays the saver's team's");
        webApi.ConditionalWrites.Should().NotContain(w => w.Id == copy, "no owner write for the unfiled save's document");
        _world.OwnerWrites.Should().NotContain(w => w.Id == copy);
    }
}
