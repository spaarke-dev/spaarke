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
}
