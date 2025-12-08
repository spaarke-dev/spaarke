using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class SummarizeServiceTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ILogger<SummarizeService>> _loggerMock;
    private readonly IOptions<AiOptions> _options;
    private readonly SummarizeService _service;

    public SummarizeServiceTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        _textExtractorMock = new Mock<ITextExtractor>(MockBehavior.Strict);
        _speFileOperationsMock = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<SummarizeService>>();
        _options = Options.Create(new AiOptions
        {
            SummarizePromptTemplate = "Summarize: {documentText}"
        });

        _service = new SummarizeService(
            _openAiClientMock.Object,
            _textExtractorMock.Object,
            _speFileOperationsMock.Object,
            _options,
            _loggerMock.Object);
    }

    private static SummarizeRequest CreateRequest() => new(
        DocumentId: Guid.NewGuid(),
        DriveId: "test-drive-id",
        ItemId: "test-item-id");

    private static FileHandleDto CreateFileMetadata(string name = "test.txt") => new(
        Id: "test-item-id",
        Name: name,
        ParentId: null,
        Size: 1000,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "etag",
        IsFolder: false,
        WebUrl: null);

    private static MemoryStream CreateStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    #region SummarizeStreamAsync Tests

    [Fact]
    public async Task SummarizeStreamAsync_Success_YieldsChunksAndCompletedResult()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var fileContent = "This is the document content.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream(fileContent));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        _openAiClientMock.Setup(x => x.StreamCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable(["This ", "is ", "a ", "summary."]));

        // Act
        var chunks = new List<SummarizeChunk>();
        await foreach (var chunk in _service.SummarizeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(5); // 4 content chunks + 1 completed
        chunks[0].Content.Should().Be("This ");
        chunks[0].Done.Should().BeFalse();
        chunks[1].Content.Should().Be("is ");
        chunks[2].Content.Should().Be("a ");
        chunks[3].Content.Should().Be("summary.");
        chunks[4].Done.Should().BeTrue();
        chunks[4].Summary.Should().Be("This is a summary.");
    }

    [Fact]
    public async Task SummarizeStreamAsync_FileNotFound_YieldsError()
    {
        // Arrange
        var request = CreateRequest();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        // Act
        var chunks = new List<SummarizeChunk>();
        await foreach (var chunk in _service.SummarizeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("not found");
    }

    [Fact]
    public async Task SummarizeStreamAsync_DownloadFailed_YieldsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var chunks = new List<SummarizeChunk>();
        await foreach (var chunk in _service.SummarizeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("download");
    }

    [Fact]
    public async Task SummarizeStreamAsync_TextExtractionFailed_YieldsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var extractionResult = TextExtractionResult.Failed("Extraction failed", TextExtractionMethod.Native);

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream("content"));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        // Act
        var chunks = new List<SummarizeChunk>();
        await foreach (var chunk in _service.SummarizeStreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Be("Extraction failed");
    }

    #endregion

    #region SummarizeAsync Tests

    [Fact]
    public async Task SummarizeAsync_Success_ReturnsSummary()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var fileContent = "Document content to summarize.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);
        var summary = "This is a summary.";

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream(fileContent));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        // Act
        var result = await _service.SummarizeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Summary.Should().Be(summary);
        result.SourceCharacterCount.Should().Be(fileContent.Length);
        result.ExtractionMethod.Should().Be(TextExtractionMethod.Native);
    }

    [Fact]
    public async Task SummarizeAsync_FileNotFound_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        // Act
        var result = await _service.SummarizeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task SummarizeAsync_DownloadFailed_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await _service.SummarizeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("download");
    }

    [Fact]
    public async Task SummarizeAsync_TextExtractionFailed_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var extractionResult = TextExtractionResult.Failed("Unsupported file type", TextExtractionMethod.NotSupported);

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream("content"));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        // Act
        var result = await _service.SummarizeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    #endregion

    #region Prompt Building Tests

    [Fact]
    public async Task SummarizeAsync_BuildsPromptWithTemplate()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var fileContent = "Test document content.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);
        string? capturedPrompt = null;

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream(fileContent));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
            .ReturnsAsync("Summary");

        // Act
        await _service.SummarizeAsync(request);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Be("Summarize: Test document content.");
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<string> AsyncEnumerable(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    #endregion
}

public class SummarizeResultTests
{
    [Fact]
    public void Succeeded_CreatesSuccessfulResult()
    {
        var result = SummarizeResult.Succeeded("This is the summary", 500, TextExtractionMethod.Native);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be("This is the summary");
        result.SourceCharacterCount.Should().Be(500);
        result.ExtractionMethod.Should().Be(TextExtractionMethod.Native);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failed_CreatesFailedResult()
    {
        var result = SummarizeResult.Failed("Something went wrong");

        result.Success.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.ErrorMessage.Should().Be("Something went wrong");
    }
}

public class SummarizeRequestTests
{
    [Fact]
    public void SummarizeRequest_PropertiesAreSet()
    {
        var docId = Guid.NewGuid();
        var request = new SummarizeRequest(docId, "drive-123", "item-456");

        request.DocumentId.Should().Be(docId);
        request.DriveId.Should().Be("drive-123");
        request.ItemId.Should().Be("item-456");
    }
}

public class SummarizeChunkTests
{
    [Fact]
    public void FromContent_CreatesContentChunk()
    {
        var chunk = SummarizeChunk.FromContent("test content");

        chunk.Content.Should().Be("test content");
        chunk.Done.Should().BeFalse();
        chunk.Summary.Should().BeNull();
        chunk.Error.Should().BeNull();
    }

    [Fact]
    public void Completed_CreatesCompletedChunk()
    {
        var chunk = SummarizeChunk.Completed("Full summary text");

        chunk.Content.Should().BeEmpty();
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().Be("Full summary text");
        chunk.Error.Should().BeNull();
    }

    [Fact]
    public void FromError_CreatesErrorChunk()
    {
        var chunk = SummarizeChunk.FromError("Something failed");

        chunk.Content.Should().BeEmpty();
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().BeNull();
        chunk.Error.Should().Be("Something failed");
    }
}
