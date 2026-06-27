using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for FileIndexingService - RAG file indexing pipeline.
/// Tests all three entry points and error handling scenarios.
/// </summary>
public class FileIndexingServiceTests
{
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<ITextChunkingService> _chunkingServiceMock;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly Mock<ILogger<FileIndexingService>> _loggerMock;

    public FileIndexingServiceTests()
    {
        _speFileOperationsMock = new Mock<ISpeFileOperations>();
        _textExtractorMock = new Mock<ITextExtractor>();
        _chunkingServiceMock = new Mock<ITextChunkingService>();
        _ragServiceMock = new Mock<IRagService>();
        _loggerMock = new Mock<ILogger<FileIndexingService>>();
    }

    private FileIndexingService CreateService()
    {
        return new FileIndexingService(
            _speFileOperationsMock.Object,
            _textExtractorMock.Object,
            _chunkingServiceMock.Object,
            _ragServiceMock.Object,
            _loggerMock.Object);
    }

    private static FileIndexRequest CreateFileIndexRequest() => new()
    {
        DriveId = "test-drive-id",
        ItemId = "test-item-id",
        FileName = "test-document.pdf",
        TenantId = "test-tenant-id",
        DocumentId = "test-doc-id"
    };

    private static ContentIndexRequest CreateContentIndexRequest() => new()
    {
        Content = "This is test content for indexing.",
        FileName = "test-document.txt",
        TenantId = "test-tenant-id",
        SpeFileId = "test-spe-file-id",
        DocumentId = "test-doc-id"
    };

    #region IndexFileAppOnlyAsync Tests

    [Fact]
    public async Task IndexFileAppOnlyAsync_ValidFile_IndexesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();
        var fileContent = new MemoryStream("Test file content"u8.ToArray());
        var extractedText = "Extracted text from the document.";

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), request.FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(extractedText, TextExtractionMethod.DocumentIntelligence));

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(extractedText, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = extractedText, Index = 0, StartPosition = 0, EndPosition = extractedText.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        var result = await service.IndexFileAppOnlyAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ChunksIndexed.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
        result.SpeFileId.Should().Be(request.ItemId);
    }

    [Fact]
    public async Task IndexFileAppOnlyAsync_FileNotFound_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await service.IndexFileAppOnlyAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task IndexFileAppOnlyAsync_ExtractionFails_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();
        var fileContent = new MemoryStream("Test file content"u8.ToArray());

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), request.FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Failed("Extraction failed", TextExtractionMethod.DocumentIntelligence));

        // Act
        var result = await service.IndexFileAppOnlyAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Extraction failed");
    }

    #endregion

    #region IndexContentAsync Tests

    [Fact]
    public async Task IndexContentAsync_ValidContent_IndexesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var request = CreateContentIndexRequest();

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(request.Content, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = request.Content, Index = 0, StartPosition = 0, EndPosition = request.Content.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        var result = await service.IndexContentAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ChunksIndexed.Should().Be(1);
        result.SpeFileId.Should().Be(request.SpeFileId);
    }

    [Fact]
    public async Task IndexContentAsync_EmptyContent_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var request = new ContentIndexRequest
        {
            Content = "   ", // Whitespace only
            FileName = "test.txt",
            TenantId = "test-tenant",
            SpeFileId = "test-spe-id"
        };

        // Act
        var result = await service.IndexContentAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task IndexContentAsync_MultipleChunks_IndexesAllChunks()
    {
        // Arrange
        var service = CreateService();
        var request = CreateContentIndexRequest();

        var chunks = new List<TextChunk>
        {
            new() { Content = "Chunk 1", Index = 0, StartPosition = 0, EndPosition = 7 },
            new() { Content = "Chunk 2", Index = 1, StartPosition = 7, EndPosition = 14 },
            new() { Content = "Chunk 3", Index = 2, StartPosition = 14, EndPosition = 21 }
        };

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(request.Content, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult>
            {
                IndexResult.Success("chunk-0"),
                IndexResult.Success("chunk-1"),
                IndexResult.Success("chunk-2")
            });

        // Act
        var result = await service.IndexContentAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.ChunksIndexed.Should().Be(3);
    }

    [Fact]
    public async Task IndexContentAsync_PartialIndexFailure_ReturnsPartialFailure()
    {
        // Arrange
        var service = CreateService();
        var request = CreateContentIndexRequest();

        var chunks = new List<TextChunk>
        {
            new() { Content = "Chunk 1", Index = 0, StartPosition = 0, EndPosition = 7 },
            new() { Content = "Chunk 2", Index = 1, StartPosition = 7, EndPosition = 14 }
        };

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(request.Content, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult>
            {
                IndexResult.Success("chunk-0"),
                IndexResult.Failure("chunk-1", "Index error")
            });

        // Act
        var result = await service.IndexContentAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ChunksIndexed.Should().Be(1);
        result.ErrorMessage.Should().Contain("Failed to index 1 of 2 chunks");
    }

    #endregion

    #region IndexFileAsync (OBO) Tests

    [Fact]
    public async Task IndexFileAsync_ValidFile_IndexesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();
        var httpContext = new DefaultHttpContext();
        var fileContent = new MemoryStream("Test file content"u8.ToArray());
        var extractedText = "Extracted text from the document.";

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsUserAsync(httpContext, request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), request.FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(extractedText, TextExtractionMethod.DocumentIntelligence));

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(extractedText, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = extractedText, Index = 0, StartPosition = 0, EndPosition = extractedText.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(It.IsAny<IEnumerable<KnowledgeDocument>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        var result = await service.IndexFileAsync(request, httpContext);

        // Assert
        result.Success.Should().BeTrue();
        result.ChunksIndexed.Should().Be(1);
    }

    [Fact]
    public async Task IndexFileAsync_DownloadFails_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();
        var httpContext = new DefaultHttpContext();

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsUserAsync(httpContext, request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await service.IndexFileAsync(request, httpContext);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task IndexFileAppOnlyAsync_ExceptionThrown_ReturnsFailureWithMessage()
    {
        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest();

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network error"));

        // Act
        var result = await service.IndexFileAppOnlyAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Network error");
    }

    [Fact]
    public async Task IndexContentAsync_NoChunksGenerated_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var request = CreateContentIndexRequest();

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(request.Content, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>());

        // Act
        var result = await service.IndexContentAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No chunks");
    }

    #endregion

    #region SearchIndexName Thread-Through (multi-container-multi-index-r1 indexer-routing-fix Tier 3)

    [Fact]
    public async Task IndexFileAppOnlyAsync_WithSearchIndexName_ThreadsThroughToRagService()
    {
        // FR-BFF-07 write path: when the FileIndexRequest.SearchIndexName is set, the
        // internal pipeline MUST pass it verbatim into IRagService.IndexDocumentsBatchAsync
        // (3-arg overload). This is the chokepoint the indexer-routing-fix targets.

        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest() with { SearchIndexName = "spaarke-file-index" };
        var fileContent = new MemoryStream("Test file content"u8.ToArray());
        var extractedText = "Extracted text from the document.";

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), request.FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(extractedText, TextExtractionMethod.DocumentIntelligence));

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(extractedText, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = extractedText, Index = 0, StartPosition = 0, EndPosition = extractedText.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                "spaarke-file-index",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        await service.IndexFileAppOnlyAsync(request);

        // Assert — 3-arg overload invoked with the explicit index name verbatim
        _ragServiceMock.Verify(
            x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                "spaarke-file-index",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexContentAsync_WithSearchIndexName_ThreadsThroughToRagService()
    {
        // FR-BFF-07 — Email pre-extracted content path also routes the per-record index name.

        // Arrange
        var service = CreateService();
        var request = CreateContentIndexRequest() with { SearchIndexName = "spaarke-knowledge-index-v2" };

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(request.Content, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = request.Content, Index = 0, StartPosition = 0, EndPosition = request.Content.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                "spaarke-knowledge-index-v2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        await service.IndexContentAsync(request);

        // Assert
        _ragServiceMock.Verify(
            x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                "spaarke-knowledge-index-v2",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexFileAppOnlyAsync_WithoutSearchIndexName_PassesNullToRagService()
    {
        // NFR-02 regression: when SearchIndexName is not set on the request, the internal
        // pipeline MUST pass null to IRagService — which then falls through to the tenant
        // default chain. Existing callers that never set the value see byte-for-byte
        // backward-compat.

        // Arrange
        var service = CreateService();
        var request = CreateFileIndexRequest(); // SearchIndexName not set → null
        var fileContent = new MemoryStream("Test file content"u8.ToArray());
        var extractedText = "Extracted text.";

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), request.FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(extractedText, TextExtractionMethod.DocumentIntelligence));

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(extractedText, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TextChunk>
            {
                new() { Content = extractedText, Index = 0, StartPosition = 0, EndPosition = extractedText.Length }
            });

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                (string?)null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IndexResult> { IndexResult.Success("chunk-id-0") });

        // Act
        await service.IndexFileAppOnlyAsync(request);

        // Assert — null was passed (tenant-default fall-through behavior)
        _ragServiceMock.Verify(
            x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                (string?)null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
