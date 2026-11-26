using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Issue #3165 (Matter Type search filter): the type a document is indexed under is the <c>sprk_mattertype</c> of the matter or
/// project it belongs to, one hop from the parent the index already records. The only double is <see cref="IGenericEntityService"/>,
/// the Dataverse seam; <see cref="DocumentMatterTypeResolver"/> is the real class.
/// </summary>
public sealed class DocumentMatterTypeResolverTests
{
    private static readonly Guid Document = Guid.Parse("3165a000-0000-4000-8000-000000000001");
    private static readonly Guid Matter = Guid.Parse("3165a000-0000-4000-8000-000000000002");
    private static readonly Guid Project = Guid.Parse("3165a000-0000-4000-8000-000000000003");
    private static readonly Guid WorkAssignment = Guid.Parse("3165a000-0000-4000-8000-000000000004");
    private static readonly Guid Invoice = Guid.Parse("3165a000-0000-4000-8000-000000000005");
    private static readonly Guid Email = Guid.Parse("3165a000-0000-4000-8000-000000000006");
    private static readonly Guid TypeA = Guid.Parse("3165a000-0000-4000-8000-0000000000a1");
    private static readonly Guid TypeB = Guid.Parse("3165a000-0000-4000-8000-0000000000b2");

    private readonly Dictionary<(string Table, Guid Id), Entity> _rows = new();
    private readonly Mock<IGenericEntityService> _dataverse = new();

    public DocumentMatterTypeResolverTests()
    {
        // A row that is not in _rows is a row that does not exist (null), as the live service answers.
        _dataverse
            .Setup(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                _rows.TryGetValue((table, id), out var row) ? row : null!);
    }

    private DocumentMatterTypeResolver Sut() => new(_dataverse.Object, NullLogger<DocumentMatterTypeResolver>.Instance);

    private void Row(string table, Guid id, params (string Column, string Target, Guid Target_Id)[] links)
    {
        var row = new Entity(table, id);
        foreach (var (column, target, targetId) in links)
        {
            row[column] = new EntityReference(target, targetId);
        }

        _rows[(table, id)] = row;
    }

    private void TypedMatter(Guid id, Guid? type) => Typed("sprk_matter", id, type);

    private void TypedProject(Guid id, Guid? type) => Typed("sprk_project", id, type);

    private void Typed(string table, Guid id, Guid? type)
    {
        if (type is null)
        {
            Row(table, id);
        }
        else
        {
            Row(table, id, ("sprk_mattertype", "sprk_mattertype", type.Value));
        }
    }

    private static ParentEntityContext Parent(string type, Guid id) => new(type, id.ToString(), "name");

    private static string Id(Guid id) => id.ToString("D");

    [Fact]
    public async Task ResolveAsync_MatterParent_ReturnsTheMattersType()
    {
        TypedMatter(Matter, TypeA);

        var result = await Sut().ResolveAsync(Id(Document), Parent("matter", Matter), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeA), Id(Matter)));
    }

    [Fact]
    public async Task ResolveAsync_ProjectParent_ReturnsTheProjectsType()
    {
        TypedProject(Project, TypeB);

        var result = await Sut().ResolveAsync(Id(Document), Parent("project", Project), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeB), Id(Project)));
    }

    [Fact]
    public async Task ResolveAsync_WorkAssignmentRegardingMatter_ReturnsTheMattersType()
    {
        Row("sprk_workassignment", WorkAssignment, ("sprk_regardingmatter", "sprk_matter", Matter));
        TypedMatter(Matter, TypeA);

        var result = await Sut().ResolveAsync(Id(Document), Parent("workassignment", WorkAssignment), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeA), Id(Matter)));
    }

    [Fact]
    public async Task ResolveAsync_WorkAssignmentRegardingOnlyAProject_ReturnsTheProjectsType()
    {
        Row("sprk_workassignment", WorkAssignment, ("sprk_regardingproject", "sprk_project", Project));
        TypedProject(Project, TypeB);

        var result = await Sut().ResolveAsync(Id(Document), Parent("workassignment", WorkAssignment), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeB), Id(Project)));
    }

    [Fact]
    public async Task ResolveAsync_InvoiceNamingAMatter_ReturnsTheMattersType()
    {
        Row("sprk_invoice", Invoice, ("sprk_matter", "sprk_matter", Matter));
        TypedMatter(Matter, TypeA);

        var result = await Sut().ResolveAsync(Id(Document), Parent("invoice", Invoice), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeA), Id(Matter)));
    }

    [Fact]
    public async Task ResolveAsync_InvoiceNamingOnlyAProject_ReturnsTheProjectsType()
    {
        Row("sprk_invoice", Invoice, ("sprk_project", "sprk_project", Project));
        TypedProject(Project, TypeB);

        var result = await Sut().ResolveAsync(Id(Document), Parent("invoice", Invoice), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeB), Id(Project)));
    }

    [Fact]
    public async Task ResolveAsync_NoParentButTheDocumentsEmailIsFiledUnderAProject_ReturnsTheProjectsType()
    {
        Row("sprk_document", Document, ("sprk_email", "email", Email));
        Row("email", Email, ("sprk_project", "sprk_project", Project));
        TypedProject(Project, TypeB);

        var result = await Sut().ResolveAsync(Id(Document), parent: null, CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeB), Id(Project)));
    }

    [Fact]
    public async Task ResolveAsync_ParentWithNoMatterOrProjectFallsBackToTheDocumentsEmailMatter()
    {
        Row("sprk_invoice", Invoice);
        Row("sprk_document", Document, ("sprk_email", "email", Email));
        Row("email", Email, ("sprk_matter", "sprk_matter", Matter));
        TypedMatter(Matter, TypeA);

        var result = await Sut().ResolveAsync(Id(Document), Parent("invoice", Invoice), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(Id(TypeA), Id(Matter)));
    }

    [Fact]
    public async Task ResolveAsync_NoParentAndNoEmail_IsResolvedWithNoSource()
    {
        Row("sprk_document", Document);

        var result = await Sut().ResolveAsync(Id(Document), parent: null, CancellationToken.None);

        result.Should().Be(MatterTypeResolution.NoSource);
        result.SourceId.Should().Be(string.Empty, "an empty source id marks the document as resolved, so the backfill does not pick it up again");
        result.IsResolved.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_SourceWithoutAType_KeepsTheSourceIdSoALaterTypeFindsTheChunks()
    {
        TypedMatter(Matter, type: null);

        var result = await Sut().ResolveAsync(Id(Document), Parent("matter", Matter), CancellationToken.None);

        result.Should().Be(new MatterTypeResolution(null, Id(Matter)));
    }

    [Fact]
    public async Task ResolveAsync_AFailedRead_IsUnresolvedAndDoesNotThrow()
    {
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse did not answer"));

        var result = await Sut().ResolveAsync(Id(Document), Parent("matter", Matter), CancellationToken.None);

        result.Should().Be(MatterTypeResolution.Unresolved);
        result.IsResolved.Should().BeFalse("an unresolved document is retried by the sync job, never stamped as 'no source'");
    }

    [Fact]
    public async Task ResolveAsync_CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => Sut().ResolveAsync(Id(Document), Parent("matter", Matter), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
