// unified-access-control-r2 task 177 (#1510): the ONE decision of an index chunk's parent — the record whose access governs
// the document. The substitutes are the seams only: IGenericEntityService (the document row, an event's row, the filing walk)
// and the secure-flag reader (FlagStubParticipationService). DocumentIndexParentResolver, CoreAncestorResolver and the
// task-174 filing walk are real.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

public class DocumentIndexParentResolverTests
{
    private static readonly Guid Document = Guid.Parse("4d000000-0000-4000-8000-0000000017c1");
    private static readonly Guid Linked = Guid.Parse("4d000000-0000-4000-8000-0000000017c2");
    private static readonly Guid Event = Guid.Parse("4d000000-0000-4000-8000-0000000017c3");
    private static readonly Guid Other = Guid.Parse("4d000000-0000-4000-8000-0000000017c4");

    private static readonly RootRecordFlags NotSecure = new(IsSecure: false, IsRestricted: false);
    private static readonly RootRecordFlags Secure = new(IsSecure: true, IsRestricted: false);

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _flags = new(NotSecure);

    /// <summary>The filing world the REAL task-174 walk reads: (table, id) → row. Empty = nothing is filed under anything.</summary>
    private readonly Dictionary<(string Table, Guid Id), Entity> _filing = new();

    public DocumentIndexParentResolverTests()
    {
        // The filing walk's queries, answered from _filing by the id condition each one carries (Equal or In).
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var ids = query.Criteria.Conditions
                    .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(c => c.Values.Select(v => (Guid)v))
                    .ToHashSet();
                return new EntityCollection(_filing
                    .Where(kv => string.Equals(kv.Key.Table, query.EntityName, StringComparison.OrdinalIgnoreCase) && ids.Contains(kv.Key.Id))
                    .Select(kv => kv.Value)
                    .ToList());
            });
    }

    /// <summary>A work assignment filed under <paramref name="matter"/> (its typed <c>sprk_regardingmatter</c>), and that
    /// matter's own row (secure), as the walk reads them.</summary>
    private void WorkAssignmentFiledUnderSecureMatter(Guid workAssignment, Guid matter)
    {
        _filing[("sprk_workassignment", workAssignment)] = new Entity("sprk_workassignment", workAssignment)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter),
            ["sprk_issecure"] = false,
        };
        _filing[("sprk_matter", matter)] = new Entity("sprk_matter", matter)
        {
            ["sprk_issecure"] = true,
            ["sprk_mattername"] = "Secure Matter",
        };
    }

    private Sprk.Bff.Api.Services.Dataverse.DocumentIndexParentResolver Resolver()
        => TestDocumentIndexParentResolver.Over(_dataverse.Object, _flags);

    private void DocumentRow(params (string Column, string Target, Guid Id, string? Name)[] links)
    {
        var row = new Entity("sprk_document", Document);
        foreach (var (column, target, id, name) in links)
        {
            row[column] = new EntityReference(target, id) { Name = name };
        }

        _dataverse.Setup(d => d.RetrieveAsync("sprk_document", Document, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private void EventStamps(params (string Column, string Target, Guid Id)[] stamps)
    {
        DocumentRow(("sprk_relatedevent", "sprk_event", Event, "Hearing"));
        var row = new Entity("sprk_event", Event);
        foreach (var (column, target, id) in stamps)
        {
            row[column] = new EntityReference(target, id);
        }

        _dataverse.Setup(d => d.RetrieveAsync("sprk_event", Event, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_matter", "matter")]
    [InlineData("sprk_project", "sprk_project", "project")]
    [InlineData("sprk_workassignment", "sprk_workassignment", "workassignment")]
    [InlineData("sprk_invoice", "sprk_invoice", "invoice")]
    public async Task TheRecordTheDocumentIsFiledTo_IsItsParent_NamedAsScopeEntityNamesIt(string column, string target, string searchType)
    {
        DocumentRow((column, target, Linked, "Filed Record"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext(searchType, Linked.ToString(), "Filed Record"));
        _flags.Reads.Should().BeEmpty("one record is its own answer; no secure-state read");
    }

    [Fact]
    public async Task ADocumentUnderAnEvent_IsFiledUnderTheEventsCoreRecord()
    {
        EventStamps(("sprk_regardingmatter", "sprk_matter", Linked));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("matter", Linked.ToString(), "Unknown Matter"),
            "search cannot authorize an event parent; the event inherits its access from its matter");
    }

    [Fact]
    public async Task ADocumentWithNoParentLink_HasNoParent()
    {
        DocumentRow(("sprk_relatedcontact", "contact", Linked, "A Person"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull("a contact is a party, not a record the document is filed under");
    }

    [Fact]
    public async Task AnUnreadableRow_HasNoParent_AndDoesNotThrow()
    {
        _dataverse.Setup(d => d.RetrieveAsync("sprk_document", Document, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse did not answer"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>Rule (2): with no secure record among them, the most specific record the document names wins.</summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_matter", "sprk_project", "sprk_project", "project")]
    [InlineData("sprk_matter", "sprk_matter", "sprk_workassignment", "sprk_workassignment", "workassignment")]
    [InlineData("sprk_project", "sprk_project", "sprk_workassignment", "sprk_workassignment", "workassignment")]
    public async Task ADocumentNamingTwoRecords_NeitherSecure_IsFiledUnderTheMoreSpecificOne(
        string broadColumn, string broadTarget, string specificColumn, string specificTarget, string searchType)
    {
        DocumentRow((broadColumn, broadTarget, Other, "Broad"), (specificColumn, specificTarget, Linked, "Specific"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext(searchType, Linked.ToString(), "Specific"));
    }

    /// <summary>Rule (1), the verifier's case: an event stamped with a non-secure matter AND a secure project files its
    /// documents under the project — the record that governs them (storage puts them in the project's container).</summary>
    [Fact]
    public async Task AnEventUnderANonSecureMatterAndASecureProject_FilesItsDocumentsUnderTheProject()
    {
        EventStamps(("sprk_regardingmatter", "sprk_matter", Other), ("sprk_regardingproject", "sprk_project", Linked));
        _flags.Flags[Linked] = Secure;

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("project", Linked.ToString(), "Unknown Project"));
    }

    /// <summary>Rule (1) for the document's own links: the secure record wins over a more specific non-secure one.</summary>
    [Fact]
    public async Task ADocumentUnderASecureMatterAndANonSecureWorkAssignment_IsFiledUnderTheMatter()
    {
        DocumentRow(("sprk_matter", "sprk_matter", Linked, "Secure Matter"), ("sprk_workassignment", "sprk_workassignment", Other, "WA"));
        _flags.Flags[Linked] = Secure;

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("matter", Linked.ToString(), "Secure Matter"));
    }

    /// <summary>Rule (3): two different secure roots — no parent (fail closed).</summary>
    [Fact]
    public async Task TwoDifferentSecureRoots_GiveNoParent()
    {
        DocumentRow(("sprk_matter", "sprk_matter", Other, "Secure Matter"), ("sprk_project", "sprk_project", Linked, "Secure Project"));
        _flags.Flags[Other] = Secure;
        _flags.Flags[Linked] = Secure;

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>Rule (3): a secure state that cannot be read — no parent (fail closed), whichever record would have won.</summary>
    [Theory]
    [InlineData("throws")]
    [InlineData("unreadable")]
    public async Task AnUnreadableSecureState_GivesNoParent(string how)
    {
        DocumentRow(("sprk_matter", "sprk_matter", Other, "Matter"), ("sprk_project", "sprk_project", Linked, "Project"));
        if (how == "throws")
        {
            _flags.ThrowOnRead = true;
        }
        else
        {
            _flags.Flags[Other] = RootRecordFlags.Unreadable;
        }

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>
    /// Send-to-Index's / index-file's path (an already-read <see cref="DocumentEntity"/>, which has no work-assignment
    /// column): the same rule. The entity names a matter and a project; the read of the links it lacks finds a work
    /// assignment, which wins when none is secure. Beyond the named cases: it pins that both paths take the same answer.
    /// </summary>
    [Fact]
    public async Task SendToIndexsPath_TakesTheSameDecision_IncludingTheWorkAssignmentTheEntityCannotCarry()
    {
        var document = new DocumentEntity
        {
            Id = Document.ToString(), Name = "memo.docx", MatterId = Guid.NewGuid().ToString(), ProjectId = Guid.NewGuid().ToString(),
        };
        DocumentRow(("sprk_workassignment", "sprk_workassignment", Linked, "Diligence"));

        var decision = await Resolver().DecideAsync(document, CancellationToken.None);

        decision.Parent.Should().Be(new ParentEntityContext("workassignment", Linked.ToString(), "Diligence"));
        decision.Named.Should().HaveCount(3);
    }

    /// <summary>
    /// Rule (1) through the REAL filing walk: a work assignment filed under secure matter M, the document naming both. Both
    /// are secure (the work assignment through M) and of one family, so the more specific — the work assignment — wins.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentUnderTheSecureMatterTheDocumentAlsoNames_IsOneFamily_TheWorkAssignmentWins()
    {
        WorkAssignmentFiledUnderSecureMatter(Linked, Other);
        _flags.Flags[Other] = Secure;
        DocumentRow(("sprk_matter", "sprk_matter", Other, "Secure Matter"), ("sprk_workassignment", "sprk_workassignment", Linked, "Diligence"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("workassignment", Linked.ToString(), "Diligence"));
    }

    /// <summary>
    /// Rule (3) through the REAL filing walk: a work assignment under secure matter M1, and an unrelated secure matter M2
    /// the document also names — two different secure roots, so no parent.
    /// </summary>
    [Fact]
    public async Task AWorkAssignmentUnderOneSecureMatter_AndAnUnrelatedSecureMatter_GiveNoParent()
    {
        var unrelated = Guid.Parse("4d000000-0000-4000-8000-0000000017c5");
        WorkAssignmentFiledUnderSecureMatter(Linked, Other);
        _flags.Flags[Other] = Secure;
        DocumentRow(("sprk_matter", "sprk_matter", unrelated, "Other Matter"), ("sprk_workassignment", "sprk_workassignment", Linked, "Diligence"));

        // Control: with M2 NOT secure the walk is read and the work assignment (secure through M1) wins — so the null
        // below is the two-roots rule, not an unreadable walk.
        (await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None))
            .Should().Be(new ParentEntityContext("workassignment", Linked.ToString(), "Diligence"));

        _flags.Flags[unrelated] = Secure;
        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>A document filed only through a <c>sprk_related*</c> twin is still filed there (verifier K6).</summary>
    [Fact]
    public async Task ADocumentFiledOnlyThroughARelatedTwin_IsFiledUnderThatRecord()
    {
        DocumentRow(("sprk_relatedmatter", "sprk_matter", Linked, "Related Matter"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("matter", Linked.ToString(), "Related Matter"));
    }

    /// <summary>
    /// A document related only to a communication is filed under the communication's core record (verifier K6: the
    /// communication's <c>sprk_regarding{core}</c>, one hop, like the event). Beyond the one K6 test: it is the second new
    /// branch, and the only one that reads a communication.
    /// </summary>
    [Fact]
    public async Task ADocumentRelatedOnlyToACommunication_IsFiledUnderTheCommunicationsCoreRecord()
    {
        var communication = Guid.Parse("4d000000-0000-4000-8000-0000000017c6");
        DocumentRow(("sprk_relatedcommunication", "sprk_communication", communication, "RE: Closing"));
        _dataverse.Setup(d => d.RetrieveAsync("sprk_communication", communication, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communication", communication) { ["sprk_regardingproject"] = new EntityReference("sprk_project", Linked) });

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("project", Linked.ToString(), "Unknown Project"));
    }

    // ── GitHub #1839 (R3 ISS-007): a document filed only to a service request ───────────────────────────────────────

    private static readonly Guid ServiceRequest = Guid.Parse("4d000000-0000-4000-8000-0000000017c7");
    private static readonly Guid Unrelated = Guid.Parse("4d000000-0000-4000-8000-0000000017c8");

    /// <summary>The document is related only to <see cref="ServiceRequest"/>; the request's row carries <paramref name="regardings"/>.
    /// Returns the columns each read of the request asked for.</summary>
    private List<string[]> ServiceRequestRow(params (string Column, string Target, Guid Id)[] regardings)
    {
        DocumentRow(("sprk_relatedservicerequest", "sprk_servicerequest", ServiceRequest, "SR-0042"));
        var row = new Entity("sprk_servicerequest", ServiceRequest);
        foreach (var (column, target, id) in regardings)
        {
            row[column] = new EntityReference(target, id);
        }

        var asked = new List<string[]>();
        _dataverse.Setup(d => d.RetrieveAsync("sprk_servicerequest", ServiceRequest, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, string[] columns, CancellationToken _) => asked.Add(columns))
            .ReturnsAsync(row);
        return asked;
    }

    [Theory]
    [InlineData("sprk_regardingmatter", "sprk_matter", "matter", "Unknown Matter")]
    [InlineData("sprk_regardingproject", "sprk_project", "project", "Unknown Project")]
    [InlineData("sprk_regardingworkassignment", "sprk_workassignment", "workassignment", "Unknown Work Assignment")]
    public async Task ADocumentRelatedOnlyToAServiceRequest_IsFiledUnderTheRecordTheRequestRegards(
        string column, string target, string searchType, string expectedName)
    {
        ServiceRequestRow((column, target, Linked));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext(searchType, Linked.ToString(), expectedName),
            "search cannot authorize a service request parent; the request hangs off the record that governs its documents");
        _flags.Reads.Should().BeEmpty("one candidate is its own answer");
    }

    /// <summary>Send-to-Index's / index-file's path: the same answer, and the request is one of the records the row names
    /// (so the body-parent check of <c>/index-file</c> sees it).</summary>
    [Fact]
    public async Task SendToIndexsPath_ADocumentRelatedOnlyToAServiceRequest_IsFiledUnderTheRequestsRecord()
    {
        ServiceRequestRow(("sprk_regardingmatter", "sprk_matter", Linked));
        var document = new DocumentEntity { Id = Document.ToString(), Name = "intake.pdf" };

        var decision = await Resolver().DecideAsync(document, CancellationToken.None);

        decision.Parent.Should().NotBeNull();
        decision.Parent!.EntityType.Should().Be("matter");
        decision.Parent.EntityId.Should().Be(Linked.ToString());
        decision.Decided.Should().BeTrue();
        decision.Named.Should().ContainSingle(p => p.EntityType == "servicerequest" && p.EntityId == ServiceRequest.ToString());
    }

    /// <summary>Control: only the three core regardings are candidates. The request's invoice, to-do, communication and party
    /// lookups are NOT parents, and the read does not even ask for them.</summary>
    [Fact]
    public async Task AServiceRequest_AddsOnlyItsCoreRegardings_NeverItsInvoiceTodoCommunicationOrParties()
    {
        var asked = ServiceRequestRow(
            ("sprk_regardingmatter", "sprk_matter", Linked),
            ("sprk_regardinginvoice", "sprk_invoice", Unrelated),
            ("sprk_regardingtodo", "sprk_todo", Unrelated),
            ("sprk_regardingcommunication", "sprk_communication", Unrelated),
            ("sprk_regardingcontact", "contact", Unrelated),
            ("sprk_regardingorganization", "sprk_organization", Unrelated));

        var decision = await Resolver().DecideAsync(new DocumentEntity { Id = Document.ToString(), Name = "intake.pdf" }, CancellationToken.None);

        decision.Parent!.EntityId.Should().Be(Linked.ToString());
        decision.Parent.EntityType.Should().Be("matter");
        asked.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new[] { "sprk_regardingworkassignment", "sprk_regardingproject", "sprk_regardingmatter" });
    }

    /// <summary>Control: a request that regards no matter, project or work assignment (only an invoice) is ambiguous for
    /// search — no parent, decided (fail closed), never the invoice.</summary>
    [Fact]
    public async Task AServiceRequestRegardingNoCoreRecord_GivesNoParent()
    {
        ServiceRequestRow(("sprk_regardinginvoice", "sprk_invoice", Unrelated));

        var decision = await Resolver().DecideAsync(new DocumentEntity { Id = Document.ToString(), Name = "intake.pdf" }, CancellationToken.None);

        decision.Parent.Should().BeNull();
        decision.Decided.Should().BeTrue();
        decision.Named.Should().ContainSingle(p => p.EntityType == "servicerequest");
    }

    /// <summary>Control: a request that cannot be read, or is not found, gives no parent AND is undecided (a caller must not
    /// substitute a parent of its own).</summary>
    [Theory]
    [InlineData("throws")]
    [InlineData("missing")]
    public async Task AnUnreadableServiceRequest_GivesNoParent_AndIsUndecided(string how)
    {
        DocumentRow(("sprk_relatedservicerequest", "sprk_servicerequest", ServiceRequest, "SR-0042"));
        var setup = _dataverse.Setup(d => d.RetrieveAsync("sprk_servicerequest", ServiceRequest, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (how == "throws")
        {
            setup.ThrowsAsync(new TimeoutException("Dataverse did not answer"));
        }
        else
        {
            setup.ReturnsAsync((Entity)null!);
        }

        var decision = await Resolver().DecideAsync(new DocumentEntity { Id = Document.ToString(), Name = "intake.pdf" }, CancellationToken.None);

        decision.Parent.Should().BeNull();
        decision.Decided.Should().BeFalse();
        (await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None)).Should().BeNull();
    }

    /// <summary>Control: a document that names a record directly keeps it; the service request it is also related to is not
    /// read (the Strict mock has no setup for it) and its record is not added.</summary>
    [Fact]
    public async Task ADocumentNamingARecordDirectly_IgnoresTheServiceRequestItIsAlsoRelatedTo()
    {
        DocumentRow(
            ("sprk_matter", "sprk_matter", Linked, "Direct Matter"),
            ("sprk_relatedservicerequest", "sprk_servicerequest", ServiceRequest, "SR-0042"));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("matter", Linked.ToString(), "Direct Matter"));
        _dataverse.Verify(d => d.RetrieveAsync("sprk_servicerequest", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Rule (2) over a request's records: none secure — the more specific one.</summary>
    [Fact]
    public async Task AServiceRequestRegardingAMatterAndAProject_NeitherSecure_IsFiledUnderTheProject()
    {
        ServiceRequestRow(("sprk_regardingmatter", "sprk_matter", Other), ("sprk_regardingproject", "sprk_project", Linked));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent!.EntityType.Should().Be("project");
        parent.EntityId.Should().Be(Linked.ToString());
    }

    /// <summary>Rule (1) over a request's records: the secure record governs, whichever is more specific.</summary>
    [Fact]
    public async Task AServiceRequestRegardingASecureMatterAndANonSecureProject_IsFiledUnderTheSecureMatter()
    {
        ServiceRequestRow(("sprk_regardingmatter", "sprk_matter", Other), ("sprk_regardingproject", "sprk_project", Linked));
        _flags.Flags[Other] = Secure;

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent!.EntityType.Should().Be("matter");
        parent.EntityId.Should().Be(Other.ToString());
    }

    /// <summary>Rule (3) over a request's records: two different secure roots — no parent.</summary>
    [Fact]
    public async Task AServiceRequestRegardingTwoDifferentSecureRoots_GivesNoParent()
    {
        ServiceRequestRow(("sprk_regardingmatter", "sprk_matter", Other), ("sprk_regardingproject", "sprk_project", Linked));
        _flags.Flags[Other] = Secure;
        _flags.Flags[Linked] = Secure;

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>Known limit, pinned: an event (or communication) stamped only with a service request still has no parent —
    /// the storage resolver HOLDS that shape, so search has no governing record to agree with. The request is not read.</summary>
    [Fact]
    public async Task AnEventStampedOnlyWithAServiceRequest_StillGivesNoParent_AndTheRequestIsNotRead()
    {
        EventStamps(("sprk_regardingservicerequest", "sprk_servicerequest", ServiceRequest));

        var parent = await Resolver().ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
        _dataverse.Verify(d => d.RetrieveAsync("sprk_servicerequest", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
