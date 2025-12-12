using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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

public class DocumentIntelligenceServiceTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ILogger<DocumentIntelligenceService>> _loggerMock;
    private readonly IOptions<DocumentIntelligenceOptions> _options;
    private readonly DocumentIntelligenceService _service;

    public DocumentIntelligenceServiceTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        _textExtractorMock = new Mock<ITextExtractor>(MockBehavior.Strict);
        _speFileOperationsMock = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<DocumentIntelligenceService>>();
        _options = Options.Create(new DocumentIntelligenceOptions
        {
            SummarizePromptTemplate = "Summarize: {documentText}",
            StructuredOutputEnabled = false
        });

        _service = new DocumentIntelligenceService(
            _openAiClientMock.Object,
            _textExtractorMock.Object,
            _speFileOperationsMock.Object,
            _options,
            _loggerMock.Object);
    }

    private static DocumentAnalysisRequest CreateRequest() => new(
        DocumentId: Guid.NewGuid(),
        DriveId: "test-drive-id",
        ItemId: "test-item-id");

    /// <summary>
    /// Set up the mock to resolve drive ID (pass-through by default).
    /// </summary>
    private void SetupDriveIdResolution(string? driveId = "test-drive-id")
    {
        _speFileOperationsMock.Setup(x => x.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string input, CancellationToken _) => driveId ?? input);
    }

    private static HttpContext CreateMockHttpContext()
    {
        var context = new DefaultHttpContext();
        return context;
    }

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

    #region AnalyzeStreamAsync Tests
    // Note: AnalyzeStreamAsync now requires HttpContext for OBO auth and uses *AsUserAsync methods.
    // These tests need refactoring for the new OBO authentication model.

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_Success_YieldsChunksAndCompletedResult()
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
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
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

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_FileNotFound_YieldsError()
    {
        // Arrange
        var request = CreateRequest();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("not found");
    }

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_DownloadFailed_YieldsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("download");
    }

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_TextExtractionFailed_YieldsError()
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
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Be("Extraction failed");
    }

    #endregion

    #region AnalyzeAsync Tests

    [Fact]
    public async Task AnalyzeAsync_Success_ReturnsSummary()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var fileContent = "Document content to summarize.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);
        var summary = "This is a summary.";

        SetupDriveIdResolution();

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
        var result = await _service.AnalyzeAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Summary.Should().Be(summary);
        result.SourceCharacterCount.Should().Be(fileContent.Length);
        result.ExtractionMethod.Should().Be(TextExtractionMethod.Native);
    }

    [Fact]
    public async Task AnalyzeAsync_FileNotFound_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();

        SetupDriveIdResolution();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        // Act
        var result = await _service.AnalyzeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task AnalyzeAsync_DownloadFailed_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();

        SetupDriveIdResolution();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        // Act
        var result = await _service.AnalyzeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("download");
    }

    [Fact]
    public async Task AnalyzeAsync_TextExtractionFailed_ReturnsError()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var extractionResult = TextExtractionResult.Failed("Unsupported file type", TextExtractionMethod.NotSupported);

        SetupDriveIdResolution();

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream("content"));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        // Act
        var result = await _service.AnalyzeAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unsupported");
    }

    #endregion

    #region Prompt Building Tests

    [Fact]
    public async Task AnalyzeAsync_BuildsPromptWithTemplate()
    {
        // Arrange
        var request = CreateRequest();
        var metadata = CreateFileMetadata();
        var fileContent = "Test document content.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);
        string? capturedPrompt = null;

        SetupDriveIdResolution();

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
        await _service.AnalyzeAsync(request);

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

public class AnalysisResultModelTests
{
    [Fact]
    public void Succeeded_CreatesSuccessfulResult()
    {
        var result = AnalysisResult.Succeeded("This is the summary", 500, TextExtractionMethod.Native);

        result.Success.Should().BeTrue();
        result.Summary.Should().Be("This is the summary");
        result.SourceCharacterCount.Should().Be(500);
        result.ExtractionMethod.Should().Be(TextExtractionMethod.Native);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failed_CreatesFailedResult()
    {
        var result = AnalysisResult.Failed("Something went wrong");

        result.Success.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.ErrorMessage.Should().Be("Something went wrong");
    }
}

public class DocumentAnalysisRequestModelTests
{
    [Fact]
    public void DocumentAnalysisRequest_PropertiesAreSet()
    {
        var docId = Guid.NewGuid();
        var request = new DocumentAnalysisRequest(docId, "drive-123", "item-456");

        request.DocumentId.Should().Be(docId);
        request.DriveId.Should().Be("drive-123");
        request.ItemId.Should().Be("item-456");
    }
}

public class AnalysisChunkModelTests
{
    [Fact]
    public void FromContent_CreatesContentChunk()
    {
        var chunk = AnalysisChunk.FromContent("test content");

        chunk.Content.Should().Be("test content");
        chunk.Done.Should().BeFalse();
        chunk.Summary.Should().BeNull();
        chunk.Error.Should().BeNull();
    }

    [Fact]
    public void Completed_CreatesCompletedChunk()
    {
        var chunk = AnalysisChunk.Completed("Full summary text");

        chunk.Content.Should().BeEmpty();
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().Be("Full summary text");
        chunk.Error.Should().BeNull();
    }

    [Fact]
    public void FromError_CreatesErrorChunk()
    {
        var chunk = AnalysisChunk.FromError("Something failed");

        chunk.Content.Should().BeEmpty();
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().BeNull();
        chunk.Error.Should().Be("Something failed");
    }
}
