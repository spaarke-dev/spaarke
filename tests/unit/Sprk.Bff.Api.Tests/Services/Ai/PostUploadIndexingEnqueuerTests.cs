using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the centralized post-upload RAG indexing helper.
///
/// Two dispatch paths covered:
///   1. EnqueueIfApplicableAsync — sync OBO indexing via IFileIndexingService.IndexFileAsync
///      (for USER-OBO-uploaded files where MI cannot read; per sdap-auth-patterns.md Pattern 4).
///   2. EnqueueAppOnlyIfApplicableAsync — async Service Bus enqueue via JobSubmissionService
///      (for MI-WRITTEN files where MI handler can read; Phase 2 background-worker contexts).
///
/// Applicability checks (skip conditions) are tested via the OBO method but apply identically
/// to both paths.
/// </summary>
public sealed class PostUploadIndexingEnqueuerTests
{
    private readonly Mock<IFileIndexingService> _fileIndexingMock = new();
    private readonly Mock<JobSubmissionService> _jobSubmissionMock;
    private readonly Mock<ILogger<PostUploadIndexingEnqueuer>> _loggerMock = new();
    private readonly PostUploadIndexingOptions _options = new();

    public PostUploadIndexingEnqueuerTests()
    {
        var sbOptions = new Mock<IOptions<ServiceBusOptions>>();
        sbOptions.Setup(o => o.Value).Returns(new ServiceBusOptions
        {
            QueueName = "test-jobs",
            CommunicationQueueName = "test-comms",
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        _jobSubmissionMock = new Mock<JobSubmissionService>(
            MockBehavior.Strict,
            sbOptions.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<ServiceBusClient>().Object);
    }

    private PostUploadIndexingEnqueuer CreateSut() =>
        new(_fileIndexingMock.Object, _jobSubmissionMock.Object, Options.Create(_options), _loggerMock.Object);

    private static HttpContext CreateHttpContext() => new DefaultHttpContext();

    private static PostUploadIndexingRequest ValidRequest(
        long? size = 1024,
        string contentType = "application/pdf",
        string fileName = "contract.pdf",
        string tenantId = "tenant-1",
        string? searchIndexName = null,
        ParentEntityContext? parentEntity = null) =>
        new(
            TenantId: tenantId,
            DriveId: "drive-abc",
            ItemId: "item-xyz",
            FileName: fileName,
            FileSizeBytes: size,
            ContentType: contentType,
            DocumentId: "doc-guid-123",
            ParentEntity: parentEntity,
            SearchIndexName: searchIndexName,
            Source: "TestSource",
            CorrelationId: "corr-id-1");

    // ===== OBO sync path =====================================================

    [Fact]
    public async Task EnqueueIfApplicableAsync_HappyPath_CallsFileIndexingService_WithOboContext()
    {
        FileIndexRequest? capturedRequest = null;
        HttpContext? capturedContext = null;
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((req, ctx, _) => { capturedRequest = req; capturedContext = ctx; })
            .ReturnsAsync(new FileIndexingResult { Success = true, ChunksIndexed = 3, Duration = TimeSpan.FromSeconds(2) });

        var sut = CreateSut();
        var ctx = CreateHttpContext();
        var request = ValidRequest(searchIndexName: "spaarke-file-index");

        var result = await sut.EnqueueIfApplicableAsync(request, ctx, CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DriveId.Should().Be("drive-abc");
        capturedRequest.SearchIndexName.Should().Be("spaarke-file-index");
        capturedContext.Should().BeSameAs(ctx);
        _fileIndexingMock.Verify(
            s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_IndexFileAsyncReturnsFailure_ReturnsFailed()
    {
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult { Success = false, ErrorMessage = "extraction error" });

        var result = await CreateSut().EnqueueIfApplicableAsync(ValidRequest(), CreateHttpContext(), CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.FailureReason.Should().Be("extraction error");
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_IndexFileAsyncThrows_LogsAndReturnsFailed()
    {
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Access denied"));

        var result = await CreateSut().EnqueueIfApplicableAsync(ValidRequest(), CreateHttpContext(), CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.FailureReason.Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_FeatureFlagOff_Skips()
    {
        _options.PostUploadEnqueueEnabled = false;

        var result = await CreateSut().EnqueueIfApplicableAsync(ValidRequest(), CreateHttpContext(), CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("FeatureFlagDisabled");
        _fileIndexingMock.Verify(
            s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_MissingTenant_SkipsAndLogsError()
    {
        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(tenantId: string.Empty),
            CreateHttpContext(),
            CancellationToken.None);

        result.SkipReason.Should().Be("MissingTenantId");
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_EmptyFile_Skips()
    {
        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(size: 0),
            CreateHttpContext(),
            CancellationToken.None);

        result.SkipReason.Should().Be("EmptyFile");
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_FileTooLarge_Skips()
    {
        _options.MaxIndexableBytes = 100;
        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(size: 1024),
            CreateHttpContext(),
            CancellationToken.None);

        result.SkipReason.Should().Be("FileTooLarge");
    }

    [Theory]
    [InlineData("video/mp4", "movie.mp4")]
    [InlineData("audio/wav", "podcast.wav")]
    [InlineData("application/zip", "archive.zip")]
    [InlineData("application/x-7z-compressed", "archive.7z")]
    public async Task EnqueueIfApplicableAsync_NonIndexableContentType_Skips(string contentType, string fileName)
    {
        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(contentType: contentType, fileName: fileName),
            CreateHttpContext(),
            CancellationToken.None);

        result.SkipReason.Should().Be("NonIndexableContentType");
    }

    [Theory]
    [InlineData("application/octet-stream", "installer.exe")]
    [InlineData(null, "disk.iso")]
    public async Task EnqueueIfApplicableAsync_BinaryExtensionWithOctetStream_Skips(string? contentType, string fileName)
    {
        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(contentType: contentType!, fileName: fileName),
            CreateHttpContext(),
            CancellationToken.None);

        result.SkipReason.Should().Be("NonIndexableContentType");
    }

    [Theory]
    [InlineData("application/octet-stream", "email.msg")]
    [InlineData("application/octet-stream", "scan.pdf")]
    [InlineData("application/pdf", "contract.pdf")]
    public async Task EnqueueIfApplicableAsync_LegitimateBusinessFile_DoesNotSkip(string contentType, string fileName)
    {
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult { Success = true });

        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(contentType: contentType, fileName: fileName),
            CreateHttpContext(),
            CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_NullSize_BypassesSizeChecks()
    {
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult { Success = true });
        _options.MaxIndexableBytes = 100;

        var result = await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(size: null),
            CreateHttpContext(),
            CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_ParentEntityProvided_FlowsThroughToFileIndexRequest()
    {
        FileIndexRequest? captured = null;
        _fileIndexingMock
            .Setup(s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, HttpContext, CancellationToken>((r, _, _) => captured = r)
            .ReturnsAsync(new FileIndexingResult { Success = true });

        var parent = new ParentEntityContext("matter", "matter-guid", "Acme Matter");
        await CreateSut().EnqueueIfApplicableAsync(
            ValidRequest(parentEntity: parent),
            CreateHttpContext(),
            CancellationToken.None);

        captured!.ParentEntity.Should().NotBeNull();
        captured.ParentEntity!.EntityType.Should().Be("matter");
        captured.ParentEntity.EntityId.Should().Be("matter-guid");
    }

    // ===== App-only Service Bus path =========================================

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_HappyPath_SubmitsServiceBusJob()
    {
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(ValidRequest(), CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        capturedJob.Should().NotBeNull();
        capturedJob!.IdempotencyKey.Should().Be("rag-index-drive-abc-item-xyz");
        capturedJob.MaxAttempts.Should().Be(3);

        // OBO path must NOT have been called for the app-only method
        _fileIndexingMock.Verify(
            s => s.IndexFileAsync(It.IsAny<FileIndexRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_SubmitThrows_LogsAndReturnsFailed()
    {
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service Bus down"));

        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(ValidRequest(), CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.FailureReason.Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task EnqueueAppOnlyIfApplicableAsync_FeatureFlagOff_Skips()
    {
        _options.PostUploadEnqueueEnabled = false;
        var result = await CreateSut().EnqueueAppOnlyIfApplicableAsync(ValidRequest(), CancellationToken.None);
        result.SkipReason.Should().Be("FeatureFlagDisabled");
    }
}
