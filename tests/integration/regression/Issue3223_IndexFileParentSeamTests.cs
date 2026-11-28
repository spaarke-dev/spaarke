// Regression anchor for GitHub issue #3223, the route seam. POST /api/ai/rag/index-file indexes a file the caller can read. Before the
// fix a request that named neither a document nor a parent was read as "no parent" and wrote explicit null parents onto the chunks of a
// document already filed under a record, so anyone with read access to the file could un-file it. The parent on the chunks is replaced
// only where it was DECIDED: a parent named by the caller, or the answer from the document's row; a row that cannot be read decides
// nothing and leaves the stored parent alone.
//
// The route handler is private; it is driven through reflection with its real collaborators except the module boundaries (the index
// pipeline captures the request it is handed, the document service answers the row).

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3223_IndexFileParentSeamTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private static readonly Guid DocumentGuid = Guid.Parse("3223d000-0000-4000-8000-000000000001");

    private readonly Mock<IFileIndexingService> _files = new();
    private readonly Mock<IDocumentDataverseService> _documents = new();
    private readonly Mock<ISearchIndexNameResolver> _names = new();
    private FileIndexRequest? _indexed;

    public Issue3223_IndexFileParentSeamTests()
    {
        _names.Setup(n => n.GetDefaultIndexName()).Returns("spaarke-files-index");
        _files
            .Setup(f => f.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((request, _, _) => _indexed = request)
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 2, duration: TimeSpan.FromSeconds(1)));
    }

    private static HttpContext Caller() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tid", Tenant)], "test")),
    };

    private static FileIndexRequest Body(string? documentId = null, ParentEntityContext? parent = null) => new()
    {
        TenantId = Tenant,
        DriveId = "drive-3223",
        ItemId = "item-3223",
        FileName = "doc.docx",
        DocumentId = documentId,
        ParentEntity = parent,
    };

    private static DocumentEntity Row() => new()
    {
        Id = DocumentGuid.ToString(),
        Name = "doc.docx",
        GraphDriveId = "drive-3223",
        GraphItemId = "item-3223",
    };

    private async Task CallIndexFileAsync(FileIndexRequest body, DocumentIndexParentResolver resolver)
    {
        var handler = typeof(RagEndpoints).GetMethod("IndexFile", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<IResult>)handler.Invoke(null,
        [
            body, _files.Object, _documents.Object, _names.Object, resolver, Caller(), NullLoggerFactory.Instance, CancellationToken.None,
        ])!;
        await task;
    }

    [Fact]
    public async Task IndexFile_WithNeitherADocumentNorAParent_LeavesTheStoredParentAlone()
    {
        await CallIndexFileAsync(Body(), TestDocumentIndexParentResolver.Over());

        _indexed.Should().NotBeNull();
        _indexed!.ParentEntity.Should().BeNull();
        _indexed.ReplaceParentEntity.Should().BeFalse("a caller that named no document and no parent decided nothing about the parent");
    }

    [Fact]
    public async Task IndexFile_WithAParentAndNoDocument_ReplacesTheStoredParent()
    {
        var parent = new ParentEntityContext("matter", Guid.NewGuid().ToString(), "Acme");

        await CallIndexFileAsync(Body(parent: parent), TestDocumentIndexParentResolver.Over());

        _indexed!.ParentEntity.Should().Be(parent);
        _indexed.ReplaceParentEntity.Should().BeTrue();
    }

    [Fact]
    public async Task IndexFile_AdocumentRowThatNamesNoRecord_ReplacesTheStoredParentWithNone()
    {
        _documents.Setup(d => d.GetDocumentAsync(DocumentGuid.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync(Row());
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.RetrieveAsync("sprk_document", DocumentGuid, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_document", DocumentGuid));

        await CallIndexFileAsync(Body(DocumentGuid.ToString()), TestDocumentIndexParentResolver.Over(dataverse.Object));

        _indexed!.ParentEntity.Should().BeNull();
        _indexed.ReplaceParentEntity.Should().BeTrue("the row was read and names no record: a decided none clears the old parent");
    }

    [Fact]
    public async Task IndexFile_AnUnreadableDocumentRow_LeavesTheStoredParentAlone()
    {
        _documents.Setup(d => d.GetDocumentAsync(DocumentGuid.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync(Row());
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.RetrieveAsync("sprk_document", DocumentGuid, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse throttled"));

        await CallIndexFileAsync(Body(DocumentGuid.ToString()), TestDocumentIndexParentResolver.Over(dataverse.Object));

        _indexed.Should().NotBeNull();
        _indexed!.ParentEntity.Should().BeNull();
        _indexed.ReplaceParentEntity.Should().BeFalse("a row that could not be read decided nothing; the index is written without touching the parent");
    }

    [Fact]
    public async Task IndexFile_ADocumentRowThatNamesARecord_ReplacesTheStoredParentWithIt()
    {
        var matter = Guid.Parse("3223d000-0000-4000-8000-000000000002");
        var row = Row();
        row.MatterId = matter.ToString();
        row.MatterName = "Acme v. Widget";
        _documents.Setup(d => d.GetDocumentAsync(DocumentGuid.ToString(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var dataverse = new Mock<IGenericEntityService>();
        dataverse.Setup(d => d.RetrieveAsync("sprk_document", DocumentGuid, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_document", DocumentGuid));

        await CallIndexFileAsync(Body(DocumentGuid.ToString()), TestDocumentIndexParentResolver.Over(dataverse.Object));

        _indexed!.ParentEntity.Should().Be(new ParentEntityContext("matter", matter.ToString(), "Acme v. Widget"));
        _indexed.ReplaceParentEntity.Should().BeTrue();
    }
}
