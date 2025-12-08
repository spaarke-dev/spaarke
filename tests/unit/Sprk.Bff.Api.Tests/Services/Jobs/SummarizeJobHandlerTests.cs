using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs;

public class SummarizeJobHandlerTests
{
    private readonly Mock<ISummarizeService> _summarizeServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<SummarizeJobHandler>> _loggerMock;
    private readonly SummarizeJobHandler _handler;

    public SummarizeJobHandlerTests()
    {
        _summarizeServiceMock = new Mock<ISummarizeService>(MockBehavior.Strict);
        _idempotencyServiceMock = new Mock<IIdempotencyService>(MockBehavior.Strict);
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<SummarizeJobHandler>>();

        _handler = new SummarizeJobHandler(
            _summarizeServiceMock.Object,
            _idempotencyServiceMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object);
    }

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsExpectedValue()
    {
        _handler.JobType.Should().Be("ai-summarize");
    }

    #endregion

    #region ProcessAsync - Success Tests

    [Fact]
    public async Task ProcessAsync_WhenSummarizationSucceeds_ReturnsSuccessOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        var result = SummarizeResult.Succeeded("This is the summary", 500, TextExtractionMethod.Native);
        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.JobId.Should().Be(job.JobId);
        outcome.JobType.Should().Be("ai-summarize");

        _summarizeServiceMock.Verify(
            x => x.SummarizeAsync(
                It.Is<SummarizeRequest>(r => r.DocumentId == documentId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenSuccessful_MarksEventAsProcessed()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        var result = SummarizeResult.Succeeded("Summary", 100, TextExtractionMethod.Native);
        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(expectedKey, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UsesCustomIdempotencyKeyWhenProvided()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var customKey = "custom-idempotency-key";
        var job = CreateJob(documentId, idempotencyKey: customKey);

        SetupIdempotencyForProcessing(customKey, isProcessed: false, lockAcquired: true);

        var result = SummarizeResult.Succeeded("Summary", 100, TextExtractionMethod.Native);
        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(customKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Idempotency Tests

    [Fact]
    public async Task ProcessAsync_WhenAlreadyProcessed_SkipsProcessingAndReturnsSuccess()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);

        // Verify SummarizeService was NOT called
        _summarizeServiceMock.Verify(
            x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenCannotAcquireLock_ReturnsSuccessWithoutProcessing()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(expectedKey, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);

        // Verify SummarizeService was NOT called (another instance is processing)
        _summarizeServiceMock.Verify(
            x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AlwaysReleasesLockAfterProcessing()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        var result = SummarizeResult.Succeeded("Summary", 100, TextExtractionMethod.Native);
        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync - Failure Tests

    [Fact]
    public async Task ProcessAsync_WhenSummarizationFails_StillReturnsSuccess()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        var result = SummarizeResult.Failed("File not found");
        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // Act
        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - still returns success (job processed, even if summarization failed)
        outcome.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_WhenInvalidPayload_ThrowsArgumentException()
    {
        // Arrange
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "ai-summarize",
            SubjectId = Guid.NewGuid().ToString(),
            Payload = null // No payload
        };

        var expectedKey = $"summarize:{job.SubjectId}:{job.JobId}";
        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _handler.ProcessAsync(job, CancellationToken.None));

        // Verify lock was still released
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_PropagatesException()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.ProcessAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_WhenExceptionThrown_StillReleasesLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJob(documentId);
        var expectedKey = $"summarize:{documentId}:{job.JobId}";

        SetupIdempotencyForProcessing(expectedKey, isProcessed: false, lockAcquired: true);

        _summarizeServiceMock
            .Setup(x => x.SummarizeAsync(It.IsAny<SummarizeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        // Act
        try
        {
            await _handler.ProcessAsync(job, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert - lock should still be released
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private static JobContract CreateJob(
        Guid documentId,
        string? idempotencyKey = null,
        string driveId = "test-drive-id",
        string itemId = "test-item-id")
    {
        var payload = new SummarizeJobPayload(documentId, driveId, itemId);
        var payloadJson = JsonSerializer.Serialize(payload);

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "ai-summarize",
            SubjectId = documentId.ToString(),
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Payload = JsonDocument.Parse(payloadJson)
        };
    }

    private void SetupIdempotencyForProcessing(string key, bool isProcessed, bool lockAcquired)
    {
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isProcessed);

        if (!isProcessed)
        {
            _idempotencyServiceMock
                .Setup(x => x.TryAcquireProcessingLockAsync(key, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(lockAcquired);

            if (lockAcquired)
            {
                _idempotencyServiceMock
                    .Setup(x => x.ReleaseProcessingLockAsync(key, It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

                _idempotencyServiceMock
                    .Setup(x => x.MarkEventAsProcessedAsync(key, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            }
        }
    }

    #endregion
}

public class SummarizeJobPayloadTests
{
    [Fact]
    public void ToRequest_CreatesCorrectSummarizeRequest()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var driveId = "drive-123";
        var itemId = "item-456";
        var payload = new SummarizeJobPayload(documentId, driveId, itemId);

        // Act
        var request = payload.ToRequest();

        // Assert
        request.DocumentId.Should().Be(documentId);
        request.DriveId.Should().Be(driveId);
        request.ItemId.Should().Be(itemId);
    }

    [Fact]
    public void Payload_CanBeSerializedAndDeserialized()
    {
        // Arrange
        var original = new SummarizeJobPayload(Guid.NewGuid(), "drive-id", "item-id");

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<SummarizeJobPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DocumentId.Should().Be(original.DocumentId);
        deserialized.DriveId.Should().Be(original.DriveId);
        deserialized.ItemId.Should().Be(original.ItemId);
    }
}
