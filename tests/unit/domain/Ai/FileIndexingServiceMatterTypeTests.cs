using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Issue #3165 (Matter Type search filter): every chunk <see cref="FileIndexingService"/> writes carries the matter type of the
/// matter or project the document belongs to, and the id of that source record, so the filter has something to match and the sync
/// job can find the chunks again when the type changes. The doubles are the seams (Dataverse, the search write, chunking);
/// <see cref="FileIndexingService"/> and <see cref="DocumentMatterTypeResolver"/> are real.
/// </summary>
public sealed class FileIndexingServiceMatterTypeTests
{
    private static readonly Guid Matter = Guid.Parse("3165b000-0000-4000-8000-000000000002");
    private static readonly Guid MatterType = Guid.Parse("3165b000-0000-4000-8000-0000000000a1");
    private const string DocumentId = "3165b000-0000-4000-8000-000000000001";

    private readonly Mock<IRagService> _rag = new();
    private readonly Mock<IGenericEntityService> _dataverse = new();
    private List<KnowledgeDocument> _written = [];

    private FileIndexingService CreateService(int chunkCount)
    {
        var chunker = new Mock<ITextChunkingService>();
        chunker
            .Setup(c => c.ChunkTextAsync(It.IsAny<string?>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, chunkCount)
                .Select(i => new TextChunk { Content = $"chunk {i}", Index = i, StartPosition = 0, EndPosition = 7 })
                .ToList());

        _rag
            .Setup(r => r.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeDocument>, string?, CancellationToken>((docs, _, _) => _written = docs.ToList())
            .ReturnsAsync(Enumerable.Range(0, chunkCount).Select(i => IndexResult.Success($"c{i}")).ToList());

        return new FileIndexingService(
            Mock.Of<ISpeFileOperations>(),
            Mock.Of<ITextExtractor>(),
            chunker.Object,
            _rag.Object,
            NullLogger<FileIndexingService>.Instance,
            TestRecordContainerResolver.ForBusinessUnitContainers("drive"),
            new DocumentMatterTypeResolver(_dataverse.Object, NullLogger<DocumentMatterTypeResolver>.Instance));
    }

    private void MatterWithType(Guid? type)
    {
        var row = new Entity("sprk_matter", Matter);
        if (type is not null)
        {
            row["sprk_mattertype"] = new EntityReference("sprk_mattertype", type.Value);
        }

        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private static ContentIndexRequest Request(ParentEntityContext? parent) => new()
    {
        Content = "some content",
        FileName = "doc.txt",
        TenantId = "tenant-3165",
        SpeFileId = "spe-3165",
        DocumentId = DocumentId,
        ParentEntity = parent,
    };

    [Fact]
    public async Task IndexContentAsync_DocumentUnderATypedMatter_StampsTheTypeAndSourceOnEveryChunk()
    {
        MatterWithType(MatterType);
        var service = CreateService(chunkCount: 3);

        var result = await service.IndexContentAsync(Request(new ParentEntityContext("matter", Matter.ToString(), "M")));

        result.Success.Should().BeTrue();
        _written.Should().HaveCount(3);
        _written.Should().OnlyContain(d => d.MatterTypeId == MatterType.ToString("D") && d.MatterTypeSourceId == Matter.ToString("D"));
    }

    [Fact]
    public async Task IndexContentAsync_MatterWithoutAType_StampsTheSourceOnlySoALaterTypeFindsTheChunks()
    {
        MatterWithType(type: null);
        var service = CreateService(chunkCount: 2);

        await service.IndexContentAsync(Request(new ParentEntityContext("matter", Matter.ToString(), "M")));

        _written.Should().OnlyContain(d => d.MatterTypeId == null && d.MatterTypeSourceId == Matter.ToString("D"));
    }

    [Fact]
    public async Task IndexContentAsync_DocumentWithNoMatterOrProject_StampsTheEmptySourceMarker()
    {
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_document", Guid.Parse(DocumentId), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", Guid.Parse(DocumentId)));
        var service = CreateService(chunkCount: 2);

        await service.IndexContentAsync(Request(parent: null));

        _written.Should().OnlyContain(d => d.MatterTypeId == null && d.MatterTypeSourceId == string.Empty,
            "an empty source id says 'resolved: belongs to no matter', so the backfill leaves the document alone");
    }

    [Fact]
    public async Task IndexContentAsync_WhenTheTypeCannotBeRead_StillIndexesAndLeavesBothFieldsUnset()
    {
        _dataverse
            .Setup(d => d.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse did not answer"));
        var service = CreateService(chunkCount: 2);

        var result = await service.IndexContentAsync(Request(new ParentEntityContext("matter", Matter.ToString(), "M")));

        result.Success.Should().BeTrue("a failed type read must never fail indexing");
        _written.Should().OnlyContain(d => d.MatterTypeId == null && d.MatterTypeSourceId == null,
            "unset fields are left for MatterTypeIndexSyncJob to fill on a later run");
    }
}
