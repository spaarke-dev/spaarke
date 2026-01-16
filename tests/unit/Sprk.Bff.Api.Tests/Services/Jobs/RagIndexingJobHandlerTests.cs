using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs;

/// <summary>
/// Unit tests for RagIndexingJobHandler - RAG indexing job processing.
/// Tests idempotency, success/failure handling, and retry logic.
/// </summary>
public class RagIndexingJobHandlerTests
{
    private readonly Mock<IFileIndexingService> _fileIndexingServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly RagTelemetry _telemetry;
    private readonly Mock<ILogger<RagIndexingJobHandler>> _loggerMock;

    public RagIndexingJobHandlerTests()
    {
        _fileIndexingServiceMock = new Mock<IFileIndexingService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _telemetry = new RagTelemetry();
        _loggerMock = new Mock<ILogger<RagIndexingJobHandler>>();
    }

    private RagIndexingJobHandler CreateHandler()
    {
        return new RagIndexingJobHandler(
            _fileIndexingServiceMock.Object,
            _idempotencyServiceMock.Object,
            _telemetry,
            _loggerMock.Object);
    }

    private static JobContract CreateValidJobContract(RagIndexingJobPayload? payload = null)
    {
        payload ??= new RagIndexingJobPayload
        {
            TenantId = "test-tenant",
            DriveId = "test-drive",
            ItemId = "test-item",
            FileName = "test-document.pdf",
            DocumentId = "test-doc-id"
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = "test-subject",
            CorrelationId = "test-correlation",
            IdempotencyKey = $"rag-index-{payload.DriveId}-{payload.ItemId}",
            Attempt = 1,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    #region ProcessAsync Success Tests

    [Fact]
    public async Task ProcessAsync_ValidJob_IndexesAndReturnsSuccess()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult
            {
                Success = true,
                ChunksIndexed = 5
            });

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);

        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task ProcessAsync_DuplicateJob_SkipsAndReturnsSuccess()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Should NOT call indexing service for duplicates
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_ReturnsSuccessWithoutProcessing()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Another instance is processing

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

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
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult
            {
                Success = false,
                ErrorMessage = "Service unavailable"
            });

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed);
        result.ErrorMessage.Should().Be("Service unavailable");

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
    public async Task ProcessAsync_PermanentFailure_ReturnsPoisoned()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileIndexingResult
            {
                Success = false,
                ErrorMessage = "File not found" // Permanent failure
            });

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsPoisoned()
    {
        // Arrange
        var handler = CreateHandler();
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = "test-subject",
            CorrelationId = "test-correlation",
            Attempt = 1,
            Payload = JsonDocument.Parse("{}") // Empty payload
        };

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("Invalid");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsFailureForRetry()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

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
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Retryable
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectJobTypeName()
    {
        // Arrange
        var handler = CreateHandler();

        // Act & Assert
        handler.JobType.Should().Be(RagIndexingJobHandler.JobTypeName);
        handler.JobType.Should().Be("RagIndexing");
    }

    #endregion
}
