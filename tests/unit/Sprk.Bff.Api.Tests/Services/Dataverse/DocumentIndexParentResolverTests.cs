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

    public DocumentIndexParentResolverTests()
    {
        // The filing walk: nothing is filed under anything.
        _dataverse.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
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
}
