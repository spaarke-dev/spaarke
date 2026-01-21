using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;

namespace Sprk.Bff.Api.Tests.Workers.Office;

/// <summary>
/// Unit tests for IndexingWorker - Office document RAG indexing job processing.
/// Tests idempotency, success/failure handling, skip logic, and retry behavior.
/// </summary>
public class IndexingWorkerTests
{
    private readonly Mock<IFileIndexingService> _fileIndexingServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<IOfficeJobStatusService> _jobStatusServiceMock;
    private readonly RagTelemetry _telemetry;
    private readonly Mock<ILogger<IndexingWorker>> _loggerMock;

    public IndexingWorkerTests()
    {
        _fileIndexingServiceMock = new Mock<IFileIndexingService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _jobStatusServiceMock = new Mock<IOfficeJobStatusService>();
        _telemetry = new RagTelemetry();
        _loggerMock = new Mock<ILogger<IndexingWorker>>();
    }

    private IndexingWorker CreateWorker()
    {
        return new IndexingWorker(
            _fileIndexingServiceMock.Object,
            _idempotencyServiceMock.Object,
            _jobStatusServiceMock.Object,
            _telemetry,
            _loggerMock.Object);
    }

    private static OfficeJobMessage CreateValidJobMessage(IndexingJobPayload? payload = null)
    {
        payload ??= new IndexingJobPayload
        {
            TenantId = "test-tenant",
            DriveId = "test-drive",
            ItemId = "test-item",
            FileName = "test-document.pdf",
            DocumentId = "test-doc-id",
            AiOptions = new AiProcessingOptions { RagIndex = true }
        };

        return new OfficeJobMessage
        {
            JobId = Guid.NewGuid(),
            JobType = OfficeJobType.Indexing,
            SubjectId = "test-subject",
            CorrelationId = "test-correlation",
            IdempotencyKey = $"rag-index-{payload.DriveId}-{payload.ItemId}",
            UserId = "test-user",
            Payload = JsonSerializer.SerializeToElement(payload)
        };
    }

    #region ProcessAsync Success Tests

    [Fact]
    public async Task ProcessAsync_ValidJob_IndexesAndReturnsSuccess()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(5, TimeSpan.FromSeconds(2)));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify job status was updated
        _jobStatusServiceMock.Verify(
            x => x.UpdateJobPhaseAsync(message.JobId, "Indexing", "Running", It.IsAny<CancellationToken>(), null),
            Times.Once);

        _jobStatusServiceMock.Verify(
            x => x.UpdateJobPhaseAsync(message.JobId, "Indexed", "Completed", It.IsAny<CancellationToken>(), null),
            Times.Once);

        // Verify idempotency was marked
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify lock was released
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Skip Logic Tests

    [Fact]
    public async Task ProcessAsync_RagIndexNotEnabled_SkipsAndReturnsSuccess()
    {
        // Arrange
        var worker = CreateWorker();
        var payload = new IndexingJobPayload
        {
            TenantId = "test-tenant",
            DriveId = "test-drive",
            ItemId = "test-item",
            FileName = "test-document.pdf",
            DocumentId = "test-doc-id",
            AiOptions = new AiProcessingOptions { RagIndex = false } // Disabled
        };
        var message = CreateValidJobMessage(payload);

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify status was updated to skipped
        _jobStatusServiceMock.Verify(
            x => x.UpdateJobPhaseAsync(message.JobId, "Indexed", "Skipped", It.IsAny<CancellationToken>(), null),
            Times.Once);

        // Verify indexing was NOT called
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AiOptionsNull_SkipsAndReturnsSuccess()
    {
        // Arrange
        var worker = CreateWorker();
        var payload = new IndexingJobPayload
        {
            TenantId = "test-tenant",
            DriveId = "test-drive",
            ItemId = "test-item",
            FileName = "test-document.pdf",
            DocumentId = "test-doc-id",
            AiOptions = null // No AI options
        };
        var message = CreateValidJobMessage(payload);

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Verify indexing was NOT called
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task ProcessAsync_DuplicateJob_SkipsAndReturnsSuccess()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Should NOT call indexing service for duplicates
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_ReturnsSuccessWithoutProcessing()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Another instance is processing

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Should NOT call indexing service when lock not acquired
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Failure Tests

    [Fact]
    public async Task ProcessAsync_IndexingFails_ReturnsFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Failed("Service unavailable"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OFFICE_012");
        result.ErrorMessage.Should().Be("Service unavailable");
        result.Retryable.Should().BeTrue(); // Transient failure

        // Should release lock even on failure
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Should NOT mark as processed on failure
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PermanentFailure_ReturnsNonRetryableFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Failed("File not found")); // Permanent failure

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OFFICE_012");
        result.ErrorMessage.Should().Contain("not found");
        result.Retryable.Should().BeFalse(); // Permanent failure
    }

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = new OfficeJobMessage
        {
            JobId = Guid.NewGuid(),
            JobType = OfficeJobType.Indexing,
            SubjectId = "test-subject",
            CorrelationId = "test-correlation",
            IdempotencyKey = "test-key",
            UserId = "test-user",
            Payload = JsonSerializer.SerializeToElement(new { Invalid = "payload" }) // Invalid payload
        };

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("OFFICE_001");
        result.ErrorMessage.Should().Contain("Invalid job payload");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsRetryableFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Retryable.Should().BeTrue(); // HttpRequestException is retryable
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task ProcessAsync_TaskCanceledException_ReturnsRetryableFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Request timeout"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Retryable.Should().BeTrue(); // Timeout is retryable
    }

    [Fact]
    public async Task ProcessAsync_UnknownException_ReturnsNonRetryableFailure()
    {
        // Arrange
        var worker = CreateWorker();
        var message = CreateValidJobMessage();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unknown error"));

        // Act
        var result = await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Retryable.Should().BeFalse(); // Unknown exceptions are not retryable
    }

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectJobType()
    {
        // Arrange
        var worker = CreateWorker();

        // Act & Assert
        worker.JobType.Should().Be(OfficeJobType.Indexing);
    }

    #endregion

    #region File Index Request Tests

    [Fact]
    public async Task ProcessAsync_PassesCorrectParametersToFileIndexingService()
    {
        // Arrange
        var worker = CreateWorker();
        var payload = new IndexingJobPayload
        {
            TenantId = "my-tenant",
            DriveId = "my-drive",
            ItemId = "my-item",
            FileName = "contract.pdf",
            DocumentId = "doc-123",
            KnowledgeSourceId = "ks-456",
            KnowledgeSourceName = "Legal Documents",
            Metadata = new Dictionary<string, string> { { "Category", "Contracts" } },
            AiOptions = new AiProcessingOptions { RagIndex = true }
        };
        var message = CreateValidJobMessage(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        FileIndexRequest? capturedRequest = null;
        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Callback<FileIndexRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(FileIndexingResult.Succeeded(3, TimeSpan.FromSeconds(1)));

        // Act
        await worker.ProcessAsync(message, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.TenantId.Should().Be("my-tenant");
        capturedRequest.DriveId.Should().Be("my-drive");
        capturedRequest.ItemId.Should().Be("my-item");
        capturedRequest.FileName.Should().Be("contract.pdf");
        capturedRequest.DocumentId.Should().Be("doc-123");
        capturedRequest.KnowledgeSourceId.Should().Be("ks-456");
        capturedRequest.KnowledgeSourceName.Should().Be("Legal Documents");
        capturedRequest.Metadata.Should().ContainKey("Category");
        capturedRequest.Metadata!["Category"].Should().Be("Contracts");
    }

    #endregion
}
