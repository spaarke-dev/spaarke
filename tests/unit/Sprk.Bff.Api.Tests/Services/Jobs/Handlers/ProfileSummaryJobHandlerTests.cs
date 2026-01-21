using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using DocumentAnalysisResult = Sprk.Bff.Api.Services.Ai.DocumentAnalysisResult;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// Unit tests for <see cref="ProfileSummaryJobHandler"/>.
/// Tests profile extraction job processing including idempotency, success/failure handling,
/// skip logic, and next-stage queueing.
/// </summary>
public class ProfileSummaryJobHandlerTests
{
    private readonly Mock<IAppOnlyAnalysisService> _analysisServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<ServiceBusClient> _serviceBusClientMock;
    private readonly DocumentTelemetry _telemetry;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<ProfileSummaryJobHandler>> _loggerMock;

    public ProfileSummaryJobHandlerTests()
    {
        _analysisServiceMock = new Mock<IAppOnlyAnalysisService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _serviceBusClientMock = new Mock<ServiceBusClient>();
        _telemetry = new DocumentTelemetry();
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<ProfileSummaryJobHandler>>();

        // Setup default configuration
        _configurationMock.Setup(c => c["Jobs:ServiceBus:QueueName"]).Returns("sdap-jobs");
    }

    private ProfileSummaryJobHandler CreateHandler()
    {
        return new ProfileSummaryJobHandler(
            _analysisServiceMock.Object,
            _idempotencyServiceMock.Object,
            _serviceBusClientMock.Object,
            _telemetry,
            _configurationMock.Object,
            _loggerMock.Object);
    }

    private static JobContract CreateValidJobContract(ProfileSummaryPayload? payload = null)
    {
        payload ??= new ProfileSummaryPayload
        {
            DocumentId = Guid.NewGuid(),
            ContentType = "Document",
            TriggerAiProcessing = true,
            QueueIndexing = true,
            Source = "OutlookSave"
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = ProfileSummaryJobHandler.JobTypeName,
            SubjectId = "test-subject",
            CorrelationId = "test-correlation",
            IdempotencyKey = $"profile-{payload.DocumentId}",
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }))
        };
    }

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectJobType()
    {
        // Arrange
        var handler = CreateHandler();

        // Act & Assert
        handler.JobType.Should().Be(ProfileSummaryJobHandler.JobTypeName);
        handler.JobType.Should().Be("ProfileSummary");
    }

    #endregion

    #region Success Tests

    [Fact]
    public async Task ProcessAsync_ValidJob_AnalyzesAndReturnsSuccess()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            ContentType = "Document",
            TriggerAiProcessing = true,
            QueueIndexing = false
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, null!, Guid.NewGuid()));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);

        // Verify idempotency was marked
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify lock was released
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithQueueIndexing_QueuesNextStage()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            ContentType = "Document",
            TriggerAiProcessing = true,
            QueueIndexing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, null!, Guid.NewGuid()));

        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Verify indexing job was queued
        senderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Skip Logic Tests

    [Fact]
    public async Task ProcessAsync_AiProcessingNotEnabled_SkipsAnalysis()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.NewGuid(),
            ContentType = "Document",
            TriggerAiProcessing = false, // AI processing disabled
            QueueIndexing = false
        };
        var job = CreateValidJobContract(payload);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Verify analysis was NOT called
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AiProcessingDisabled_StillQueuesIndexing()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.NewGuid(),
            ContentType = "Document",
            TriggerAiProcessing = false, // AI processing disabled
            QueueIndexing = true // But indexing enabled
        };
        var job = CreateValidJobContract(payload);

        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Verify analysis was NOT called
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // But indexing was queued
        senderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Idempotency Tests

    [Fact]
    public async Task ProcessAsync_DuplicateJob_SkipsProcessing()
    {
        // Arrange
        var handler = CreateHandler();
        var job = CreateValidJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Verify analysis was NOT called
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
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

        // Verify analysis was NOT called
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateJob_StillQueuesIndexingForConsistency()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.NewGuid(),
            TriggerAiProcessing = true,
            QueueIndexing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        var senderMock = new Mock<ServiceBusSender>();
        _serviceBusClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);

        // Indexing should still be queued for consistency
        senderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Failure Tests

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsPoisoned()
    {
        // Arrange
        var handler = CreateHandler();
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = ProfileSummaryJobHandler.JobTypeName,
            Payload = JsonDocument.Parse(@"{ ""invalid"": ""payload"" }")
        };

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("Invalid job payload");
    }

    [Fact]
    public async Task ProcessAsync_EmptyDocumentId_ReturnsPoisoned()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.Empty, // Invalid
            TriggerAiProcessing = true
        };
        var job = CreateValidJobContract(payload);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("Invalid job payload");
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_ReturnsFailure()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true,
            QueueIndexing = false
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Service unavailable"));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Transient failure
        result.ErrorMessage.Should().Contain("Service unavailable");

        // Verify lock was released even on failure
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify NOT marked as processed on failure
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Document not found")]
    [InlineData("File type not supported")]
    [InlineData("no file reference")]
    [InlineData("access denied")]
    [InlineData("Playbook not found")]
    public async Task ProcessAsync_PermanentFailure_ReturnsPoisoned(string errorMessage)
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true,
            QueueIndexing = false
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, errorMessage));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned); // Permanent failure
        result.ErrorMessage.Should().Contain(errorMessage);
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsFailure()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // HttpRequestException is retryable
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task ProcessAsync_TaskCanceledException_ReturnsFailure()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Request timeout"));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Timeout is retryable
    }

    [Fact]
    public async Task ProcessAsync_UnknownException_ReturnsPoisoned()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned); // Unknown exception is not retryable
    }

    #endregion

    #region Playbook Selection Tests

    [Theory]
    [InlineData("Email", "Email Analysis")]
    [InlineData("Attachment", "Document Profile")]
    [InlineData("Document", "Document Profile")]
    [InlineData(null, "Document Profile")]
    [InlineData("", "Document Profile")]
    public async Task ProcessAsync_SelectsCorrectPlaybook_BasedOnContentType(string? contentType, string expectedPlaybook)
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            ContentType = contentType,
            TriggerAiProcessing = true,
            QueueIndexing = false
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string? capturedPlaybookName = null;
        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, CancellationToken>((_, playbook, _) => capturedPlaybookName = playbook)
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, null!, Guid.NewGuid()));

        // Act
        await handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        capturedPlaybookName.Should().Be(expectedPlaybook);
    }

    #endregion

    #region Indexing Queue Tests

    [Fact]
    public async Task ProcessAsync_IndexingQueueFails_DoesNotFailProfileJob()
    {
        // Arrange
        var handler = CreateHandler();
        var documentId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            TriggerAiProcessing = true,
            QueueIndexing = true
        };
        var job = CreateValidJobContract(payload);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, null!, Guid.NewGuid()));

        // Make Service Bus sender throw
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Queue unavailable", ServiceBusFailureReason.ServiceBusy));

        _serviceBusClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(senderMock.Object);

        // Act
        var result = await handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Profile job should still succeed
        result.Status.Should().Be(JobStatus.Completed);
    }

    #endregion
}
