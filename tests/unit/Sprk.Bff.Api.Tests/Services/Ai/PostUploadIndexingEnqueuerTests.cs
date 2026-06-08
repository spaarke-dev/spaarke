using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the centralized post-upload RAG indexing helper introduced by
/// the upload-indexing centralization scope extension to multi-container-multi-index-r1.
/// Covers the 8 fail-protection branches enumerated in design §4 + §5.1.
/// </summary>
public sealed class PostUploadIndexingEnqueuerTests
{
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

    private PostUploadIndexingEnqueuer CreateSut()
    {
        var optionsWrapper = Options.Create(_options);
        return new PostUploadIndexingEnqueuer(_jobSubmissionMock.Object, optionsWrapper, _loggerMock.Object);
    }

    private static PostUploadIndexingRequest ValidRequest(
        long size = 1024,
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

    // ---- Happy path -----------------------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_HappyPath_SubmitsJobWithCorrectPayload()
    {
        // Arrange
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var request = ValidRequest(searchIndexName: "spaarke-file-index");

        // Act
        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        // Assert
        result.JobSubmitted.Should().BeTrue();
        result.JobId.Should().NotBeNull();
        result.SkipReason.Should().BeNull();
        result.FailureReason.Should().BeNull();

        capturedJob.Should().NotBeNull();
        capturedJob!.JobType.Should().Be(RagIndexingJobHandler.JobTypeName);
        capturedJob.IdempotencyKey.Should().Be("rag-index-drive-abc-item-xyz");
        capturedJob.MaxAttempts.Should().Be(3);
        capturedJob.CorrelationId.Should().Be("corr-id-1");

        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueIfApplicableAsync_NullSearchIndexName_StillEnqueues()
    {
        // The handler runs the ISearchIndexNameResolver chain when SearchIndexName is null —
        // helper must NOT fail-closed here (design §4.6).
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var request = ValidRequest(searchIndexName: null);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Feature flag ---------------------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_FeatureFlagOff_SkipsEnqueue()
    {
        _options.PostUploadEnqueueEnabled = false;
        var sut = CreateSut();

        var result = await sut.EnqueueIfApplicableAsync(ValidRequest(), CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("FeatureFlagDisabled");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Tenant context required ---------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_MissingTenantId_SkipsAndLogsError()
    {
        var sut = CreateSut();
        var request = ValidRequest(tenantId: string.Empty);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("MissingTenantId");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);

        // ERROR-level log (not INFO) since this indicates a misconfigured upload path
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ---- Empty file -----------------------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_EmptyFile_SkipsEnqueue()
    {
        var sut = CreateSut();
        var request = ValidRequest(size: 0);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("EmptyFile");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Size cap -------------------------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_FileExceedsMaxIndexableBytes_SkipsEnqueue()
    {
        _options.MaxIndexableBytes = 1000; // 1 KB cap for test
        var sut = CreateSut();
        var request = ValidRequest(size: 1001);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("FileTooLarge");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Content-type skip-list ----------------------------------------------

    [Theory]
    [InlineData("video/mp4", "movie.mp4")]
    [InlineData("audio/wav", "podcast.wav")]
    [InlineData("application/zip", "archive.zip")]
    [InlineData("application/x-7z-compressed", "archive.7z")]
    public async Task EnqueueIfApplicableAsync_NonIndexableContentType_SkipsEnqueue(string contentType, string fileName)
    {
        var sut = CreateSut();
        var request = ValidRequest(contentType: contentType, fileName: fileName);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("NonIndexableContentType");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("application/octet-stream", "installer.exe")]
    [InlineData("application/octet-stream", "library.dll")]
    [InlineData(null, "disk.iso")]
    public async Task EnqueueIfApplicableAsync_BinaryExtensionWithOctetStream_SkipsEnqueue(string? contentType, string fileName)
    {
        var sut = CreateSut();
        var request = ValidRequest(contentType: contentType!, fileName: fileName);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("NonIndexableContentType");
    }

    /// <summary>
    /// Critical: design §8.5.2 mandates .msg / .eml are NOT skipped (Outlook attachments
    /// often arrive as octet-stream with these extensions and DO have indexable text).
    /// Same for .pdf — even scanned PDFs may extract via OCR.
    /// </summary>
    [Theory]
    [InlineData("application/octet-stream", "email.msg")]
    [InlineData("application/octet-stream", "letter.eml")]
    [InlineData("application/octet-stream", "scan.pdf")]
    [InlineData("application/pdf", "contract.pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "memo.docx")]
    public async Task EnqueueIfApplicableAsync_LegitimateBusinessFile_EnqueuesEvenAsOctetStream(string contentType, string fileName)
    {
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = CreateSut();
        var request = ValidRequest(contentType: contentType, fileName: fileName);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        result.SkipReason.Should().BeNull();
    }

    // ---- Submit failure is non-fatal -----------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_SubmitThrows_LogsWarningDoesNotPropagate()
    {
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service Bus auth failed"));

        var sut = CreateSut();
        var request = ValidRequest();

        // Act — must not throw
        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.FailureReason.Should().Be("InvalidOperationException");
        result.SkipReason.Should().BeNull();

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ---- Missing SPE identifiers ---------------------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_MissingDriveId_SkipsEnqueue()
    {
        var sut = CreateSut();
        var request = ValidRequest() with { DriveId = string.Empty };

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeFalse();
        result.SkipReason.Should().Be("MissingSpeIdentifiers");
        _jobSubmissionMock.Verify(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Parent entity context flows through ----------------------------------

    [Fact]
    public async Task EnqueueIfApplicableAsync_ParentEntityProvided_FlowsThroughToPayload()
    {
        JobContract? capturedJob = null;
        _jobSubmissionMock
            .Setup(s => s.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback<JobContract, CancellationToken>((job, _) => capturedJob = job)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var parent = new ParentEntityContext("matter", "matter-guid-1", "Acme Litigation");
        var request = ValidRequest(parentEntity: parent);

        var result = await sut.EnqueueIfApplicableAsync(request, CancellationToken.None);

        result.JobSubmitted.Should().BeTrue();
        capturedJob.Should().NotBeNull();

        // Verify payload deserialization
        var payloadJson = capturedJob!.Payload!.RootElement.GetRawText();
        payloadJson.Should().Contain("matter-guid-1");
        payloadJson.Should().Contain("Acme Litigation");
    }
}
