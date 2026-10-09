// unified-access-control-r2 task 177 (#1510): the ONE derivation of an index chunk's parent from the document row.
// The substitute is the Dataverse seam only (IGenericEntityService: the document row, and an event's row for the event
// hop); DocumentIndexParentResolver and the CoreAncestorResolver it reuses are real.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class DocumentIndexParentResolverTests
{
    private static readonly Guid Document = Guid.Parse("4d000000-0000-4000-8000-0000000017c1");
    private static readonly Guid Linked = Guid.Parse("4d000000-0000-4000-8000-0000000017c2");
    private static readonly Guid Event = Guid.Parse("4d000000-0000-4000-8000-0000000017c3");

    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);

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

    [Theory]
    [InlineData("sprk_matter", "sprk_matter", "matter")]
    [InlineData("sprk_project", "sprk_project", "project")]
    [InlineData("sprk_workassignment", "sprk_workassignment", "workassignment")]
    [InlineData("sprk_invoice", "sprk_invoice", "invoice")]
    public async Task TheRecordTheDocumentIsFiledTo_IsItsParent_NamedAsScopeEntityNamesIt(string column, string target, string searchType)
    {
        DocumentRow((column, target, Linked, "Filed Record"));

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext(searchType, Linked.ToString(), "Filed Record"));
    }

    [Fact]
    public async Task ADocumentUnderAnEvent_IsFiledUnderTheEventsCoreRecord()
    {
        DocumentRow(("sprk_relatedevent", "sprk_event", Event, "Hearing"));
        _dataverse.Setup(d => d.RetrieveAsync("sprk_event", Event, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_event", Event) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Linked) });

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("matter", Linked.ToString(), "Unknown Matter"),
            "search cannot authorize an event parent; the event inherits its access from its matter");
    }

    [Fact]
    public async Task ADocumentWithNoParentLink_HasNoParent()
    {
        DocumentRow(("sprk_relatedcontact", "contact", Linked, "A Person"));

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull("a contact is a party, not a record the document is filed under");
    }

    [Fact]
    public async Task AnUnreadableRow_HasNoParent_AndDoesNotThrow()
    {
        _dataverse.Setup(d => d.RetrieveAsync("sprk_document", Document, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse did not answer"));

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().BeNull();
    }

    /// <summary>
    /// A document that names several records is filed under the MOST SPECIFIC one (owner decision 2026-10-09, task 177
    /// Q1): the record that governs its access is the one search authorizes against, which fails closed for a secure child.
    /// </summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_matter", "sprk_project", "sprk_project", "project")]
    [InlineData("sprk_matter", "sprk_matter", "sprk_workassignment", "sprk_workassignment", "workassignment")]
    [InlineData("sprk_project", "sprk_project", "sprk_workassignment", "sprk_workassignment", "workassignment")]
    public async Task ADocumentNamingTwoRecords_IsFiledUnderTheMoreSpecificOne(
        string broadColumn, string broadTarget, string specificColumn, string specificTarget, string searchType)
    {
        DocumentRow((broadColumn, broadTarget, Guid.NewGuid(), "Broad"), (specificColumn, specificTarget, Linked, "Specific"));

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(Document.ToString(), CancellationToken.None);

        parent.Should().Be(new ParentEntityContext(searchType, Linked.ToString(), "Specific"));
    }

    /// <summary>
    /// Send-to-Index's own path (an already-read <see cref="DocumentEntity"/>, which has no work-assignment column): the same
    /// rule. The entity names a matter and a project; the read of the links it lacks finds a work assignment, which wins.
    /// Beyond the named cases: it pins that Send-to-Index takes the same answer as the job path.
    /// </summary>
    [Fact]
    public async Task SendToIndexsPath_TakesTheMostSpecificRecord_IncludingTheWorkAssignmentTheEntityCannotCarry()
    {
        var document = new DocumentEntity
        {
            Id = Document.ToString(), Name = "memo.docx", MatterId = Guid.NewGuid().ToString(), ProjectId = Guid.NewGuid().ToString(),
        };
        DocumentRow(("sprk_workassignment", "sprk_workassignment", Linked, "Diligence"));

        var parent = await TestDocumentIndexParentResolver.Over(_dataverse.Object).ResolveAsync(document, CancellationToken.None);

        parent.Should().Be(new ParentEntityContext("workassignment", Linked.ToString(), "Diligence"));
    }
}
