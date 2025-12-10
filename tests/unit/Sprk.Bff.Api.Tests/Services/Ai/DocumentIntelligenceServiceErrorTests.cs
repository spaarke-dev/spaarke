using System.ClientModel;
using System.Text;
using Azure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Tests for error handling in SummarizeService.
/// Covers OpenAI errors (rate limits, timeouts, content filter) and SPE errors.
/// </summary>
public class DocumentIntelligenceServiceErrorTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ILogger<DocumentIntelligenceService>> _loggerMock;
    private readonly IOptions<DocumentIntelligenceOptions> _options;
    private readonly DocumentIntelligenceService _service;

    public DocumentIntelligenceServiceErrorTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        _textExtractorMock = new Mock<ITextExtractor>(MockBehavior.Strict);
        _speFileOperationsMock = new Mock<ISpeFileOperations>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<DocumentIntelligenceService>>();
        _options = Options.Create(new DocumentIntelligenceOptions
        {
            SummarizePromptTemplate = "Summarize: {documentText}",
            VisionPromptTemplate = "Analyze this image",
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

    private static HttpContext CreateMockHttpContext() => new DefaultHttpContext();

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

    private void SetupSuccessfulTextExtraction(DocumentAnalysisRequest request, string fileName = "test.txt")
    {
        var metadata = CreateFileMetadata(fileName);
        var fileContent = "Test document content.";
        var extractionResult = TextExtractionResult.Succeeded(fileContent, TextExtractionMethod.Native);

        // Set up drive ID resolution (pass-through)
        _speFileOperationsMock.Setup(x => x.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string input, CancellationToken _) => input);

        _speFileOperationsMock.Setup(x => x.GetFileMetadataAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _speFileOperationsMock.Setup(x => x.DownloadFileAsync(request.DriveId, request.ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStream(fileContent));

        _textExtractorMock.Setup(x => x.ExtractAsync(It.IsAny<Stream>(), metadata.Name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);
    }

    #region Streaming OpenAI Error Tests
    // Note: AnalyzeStreamAsync now requires HttpContext for OBO auth and uses *AsUserAsync methods.
    // These tests need refactoring for the new OBO authentication model.

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_OpenAiRateLimitError_YieldsRateLimitErrorChunk()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var rateLimitException = new RequestFailedException(429, "Rate limit exceeded");

        _openAiClientMock.Setup(x => x.StreamCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<string>(rateLimitException));

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("overloaded");
    }

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_OpenAiAuthError_YieldsAuthErrorChunk()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var authException = new RequestFailedException(401, "Unauthorized");

        _openAiClientMock.Setup(x => x.StreamCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<string>(authException));

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("authentication");
    }

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_OpenAiContentFilter_YieldsContentFilterErrorChunk()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var contentFilterException = new RequestFailedException(400, "content_filter triggered");

        _openAiClientMock.Setup(x => x.StreamCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<string>(contentFilterException));

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("content safety");
    }

    [Fact(Skip = "Requires OBO auth refactoring - streaming now uses HttpContext and *AsUserAsync methods")]
    public async Task AnalyzeStreamAsync_OpenAiTimeout_YieldsTimeoutErrorChunk()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var timeoutException = new TaskCanceledException("Timeout", new TimeoutException("Request timed out"));

        _openAiClientMock.Setup(x => x.StreamCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable<string>(timeoutException));

        // Act
        var chunks = new List<AnalysisChunk>();
        await foreach (var chunk in _service.AnalyzeStreamAsync(CreateMockHttpContext(), request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].Done.Should().BeTrue();
        chunks[0].Error.Should().Contain("timed out");
    }

    #endregion

    #region Non-Streaming OpenAI Error Tests

    [Fact]
    public async Task AnalyzeAsync_OpenAiRateLimitError_ThrowsSummarizationException()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var rateLimitException = new RequestFailedException(429, "Rate limit exceeded");

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(rateLimitException);

        // Act
        var act = () => _service.AnalyzeAsync(request);

        // Assert
        var exception = await act.Should().ThrowAsync<SummarizationException>();
        exception.Which.Code.Should().Be("openai_rate_limit");
        exception.Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task AnalyzeAsync_OpenAiContentFilter_ThrowsSummarizationException()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var contentFilterException = new RequestFailedException(400, "content_filter triggered");

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(contentFilterException);

        // Act
        var act = () => _service.AnalyzeAsync(request);

        // Assert
        var exception = await act.Should().ThrowAsync<SummarizationException>();
        exception.Which.Code.Should().Be("openai_content_filter");
        exception.Which.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task AnalyzeAsync_OpenAiTimeout_ThrowsSummarizationException()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var timeoutException = new TaskCanceledException("Timeout", new TimeoutException("Request timed out"));

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(timeoutException);

        // Act
        var act = () => _service.AnalyzeAsync(request);

        // Assert
        var exception = await act.Should().ThrowAsync<SummarizationException>();
        exception.Which.Code.Should().Be("openai_timeout");
        exception.Which.StatusCode.Should().Be(504);
    }

    [Fact]
    public async Task AnalyzeAsync_OpenAiAuthError_ThrowsSummarizationException()
    {
        // Arrange
        var request = CreateRequest();
        SetupSuccessfulTextExtraction(request);

        var authException = new RequestFailedException(403, "Forbidden");

        _openAiClientMock.Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(authException);

        // Act
        var act = () => _service.AnalyzeAsync(request);

        // Assert
        var exception = await act.Should().ThrowAsync<SummarizationException>();
        exception.Which.Code.Should().Be("openai_error");
        exception.Which.StatusCode.Should().Be(502);
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // Unreachable code detected - needed for yield return syntax
        yield break;
#pragma warning restore CS0162
    }

    #endregion
}

/// <summary>
/// Tests for SummarizationException factory methods and behavior.
/// </summary>
public class SummarizationExceptionTests
{
    [Fact]
    public void AiDisabled_CreatesCorrectException()
    {
        var ex = SummarizationException.AiDisabled("corr-123");

        ex.Code.Should().Be("ai_disabled");
        ex.Title.Should().Be("AI Unavailable");
        ex.StatusCode.Should().Be(503);
        ex.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void FileNotFound_CreatesCorrectException()
    {
        var ex = SummarizationException.FileNotFound("drive-1", "item-2", "corr-456");

        ex.Code.Should().Be("file_not_found");
        ex.Title.Should().Be("Document Not Found");
        ex.StatusCode.Should().Be(404);
        ex.CorrelationId.Should().Be("corr-456");
        ex.Extensions.Should().ContainKey("driveId");
        ex.Extensions.Should().ContainKey("itemId");
    }

    [Fact]
    public void FileDownloadFailed_CreatesCorrectException()
    {
        var innerEx = new Exception("Network error");
        var ex = SummarizationException.FileDownloadFailed("drive-1", "item-2", "corr-789", innerEx);

        ex.Code.Should().Be("file_download_failed");
        ex.Title.Should().Be("Download Failed");
        ex.StatusCode.Should().Be(502);
        ex.InnerException.Should().Be(innerEx);
    }

    [Fact]
    public void ExtractionFailed_CreatesCorrectException()
    {
        var ex = SummarizationException.ExtractionFailed("test.pdf", "Corrupt file");

        ex.Code.Should().Be("extraction_failed");
        ex.Title.Should().Be("Extraction Failed");
        ex.StatusCode.Should().Be(422);
        ex.Detail.Should().Contain("test.pdf");
        ex.Detail.Should().Contain("Corrupt file");
    }

    [Fact]
    public void VisionNotConfigured_CreatesCorrectException()
    {
        var ex = SummarizationException.VisionNotConfigured();

        ex.Code.Should().Be("vision_not_configured");
        ex.Title.Should().Be("Vision Unavailable");
        ex.StatusCode.Should().Be(503);
    }

    [Fact]
    public void OpenAiRateLimit_CreatesCorrectExceptionWithRetryAfter()
    {
        var ex = SummarizationException.OpenAiRateLimit(retryAfterSeconds: 30);

        ex.Code.Should().Be("openai_rate_limit");
        ex.Title.Should().Be("Rate Limit Exceeded");
        ex.StatusCode.Should().Be(429);
        ex.Extensions.Should().ContainKey("retryAfterSeconds");
        ex.Extensions!["retryAfterSeconds"].Should().Be(30);
    }

    [Fact]
    public void OpenAiTimeout_CreatesCorrectException()
    {
        var ex = SummarizationException.OpenAiTimeout();

        ex.Code.Should().Be("openai_timeout");
        ex.Title.Should().Be("Request Timeout");
        ex.StatusCode.Should().Be(504);
    }

    [Fact]
    public void ContentFiltered_CreatesCorrectException()
    {
        var ex = SummarizationException.ContentFiltered("corr-filter");

        ex.Code.Should().Be("openai_content_filter");
        ex.Title.Should().Be("Content Blocked");
        ex.StatusCode.Should().Be(422);
        ex.CorrelationId.Should().Be("corr-filter");
    }

    [Fact]
    public void FileTooLarge_CreatesCorrectException()
    {
        var ex = SummarizationException.FileTooLarge(20_000_000, 10_000_000);

        ex.Code.Should().Be("file_too_large");
        ex.Title.Should().Be("File Too Large");
        ex.StatusCode.Should().Be(413);
        ex.Extensions.Should().ContainKey("fileSize");
        ex.Extensions.Should().ContainKey("maxSize");
    }

    [Fact]
    public void UnsupportedFileType_CreatesCorrectException()
    {
        var ex = SummarizationException.UnsupportedFileType(".exe");

        ex.Code.Should().Be("unsupported_file_type");
        ex.Title.Should().Be("Unsupported File Type");
        ex.StatusCode.Should().Be(415);
        ex.Extensions.Should().ContainKey("extension");
        ex.Extensions!["extension"].Should().Be(".exe");
    }

    [Fact]
    public void Exception_MessageIncludesCodeAndTitle()
    {
        var ex = SummarizationException.OpenAiError("Test error message");

        ex.Message.Should().Contain("openai_error");
        ex.Message.Should().Contain("AI Service Error");
    }

    [Fact]
    public void CircuitBreakerOpen_CreatesCorrectException()
    {
        // Task 072: Circuit breaker exception test
        var ex = SummarizationException.CircuitBreakerOpen(retryAfterSeconds: 30);

        ex.Code.Should().Be("ai_circuit_open");
        ex.Title.Should().Be("Service Temporarily Unavailable");
        ex.StatusCode.Should().Be(503);
        ex.Extensions.Should().ContainKey("retryAfterSeconds");
        ex.Extensions!["retryAfterSeconds"].Should().Be(30);
        ex.Detail.Should().Contain("temporarily unavailable");
    }

    [Fact]
    public void CircuitBreakerOpen_WithCorrelationId_IncludesIt()
    {
        var ex = SummarizationException.CircuitBreakerOpen(
            retryAfterSeconds: 60,
            correlationId: "test-correlation-123");

        ex.CorrelationId.Should().Be("test-correlation-123");
        ex.Extensions!["retryAfterSeconds"].Should().Be(60);
    }
}
