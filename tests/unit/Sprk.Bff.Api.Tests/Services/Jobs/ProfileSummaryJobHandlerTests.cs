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

namespace Sprk.Bff.Api.Tests.Services.Jobs;

/// <summary>
/// Unit tests for ProfileSummaryJobHandler.
/// Tests job processing, idempotency, AI processing toggle, and error handling.
/// </summary>
public class ProfileSummaryJobHandlerTests
{
    private readonly Mock<IAppOnlyAnalysisService> _analysisServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<ServiceBusClient> _serviceBusClientMock;
    private readonly Mock<ServiceBusSender> _serviceBusSenderMock;
    private readonly Mock<DocumentTelemetry> _telemetryMock;
    private readonly Mock<ILogger<ProfileSummaryJobHandler>> _loggerMock;
    private readonly ProfileSummaryJobHandler _handler;

    public ProfileSummaryJobHandlerTests()
    {
        _analysisServiceMock = new Mock<IAppOnlyAnalysisService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _serviceBusClientMock = new Mock<ServiceBusClient>();
        _serviceBusSenderMock = new Mock<ServiceBusSender>();
        _telemetryMock = new Mock<DocumentTelemetry>();
        _loggerMock = new Mock<ILogger<ProfileSummaryJobHandler>>();

        // Setup Service Bus client to return mock sender
        _serviceBusClientMock
            .Setup(x => x.CreateSender(It.IsAny<string>()))
            .Returns(_serviceBusSenderMock.Object);

        // Setup configuration
        var configValues = new Dictionary<string, string?>
        {
            { "Jobs:ServiceBus:QueueName", "sdap-jobs" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        _handler = new ProfileSummaryJobHandler(
            _analysisServiceMock.Object,
            _idempotencyServiceMock.Object,
            _serviceBusClientMock.Object,
            _telemetryMock.Object,
            configuration,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static JobContract CreateJobContract(
        Guid? documentId = null,
        string? idempotencyKey = null,
        bool triggerAiProcessing = true,
        bool queueIndexing = true,
        string? contentType = "Document")
    {
        var docId = documentId ?? Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = docId,
            ContentType = contentType,
            TriggerAiProcessing = triggerAiProcessing,
            QueueIndexing = queueIndexing,
            Source = "UnitTest",
            ProcessingJobId = Guid.NewGuid(),
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = ProfileSummaryJobHandler.JobTypeName,
            SubjectId = docId.ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? $"profile-{docId}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    private void SetupSuccessfulIdempotencyFlow()
    {
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _idempotencyServiceMock
            .Setup(x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _idempotencyServiceMock
            .Setup(x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupSuccessfulServiceBusSend()
    {
        _serviceBusSenderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _serviceBusSenderMock
            .Setup(x => x.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
    }

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectValue()
    {
        // Assert
        _handler.JobType.Should().Be("ProfileSummary");
    }

    [Fact]
    public void JobTypeName_Constant_HasExpectedValue()
    {
        // Assert
        ProfileSummaryJobHandler.JobTypeName.Should().Be("ProfileSummary");
    }

    [Fact]
    public void IndexingJobType_Constant_HasExpectedValue()
    {
        // Assert
        ProfileSummaryJobHandler.IndexingJobType.Should().Be("RagIndexing");
    }

    #endregion

    #region ProcessAsync Tests - AI Processing Enabled

    [Fact]
    public async Task ProcessAsync_AiEnabled_ChecksIdempotency()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_AcquiresProcessingLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.TryAcquireProcessingLockAsync(
                job.IdempotencyKey,
                It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(15)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_CallsAnalysisService()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_Success_ReturnsSuccessOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);
        result.JobType.Should().Be(ProfileSummaryJobHandler.JobTypeName);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_Success_MarksAsProcessed()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(
                job.IdempotencyKey,
                It.Is<TimeSpan>(t => t == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_Success_QueuesIndexingJob()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true, queueIndexing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _serviceBusSenderMock.Verify(
            x => x.SendMessageAsync(
                It.Is<ServiceBusMessage>(m => m.ContentType == "application/json"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiEnabled_Success_ReleasesLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync Tests - AI Processing Disabled

    [Fact]
    public async Task ProcessAsync_AiDisabled_SkipsAnalysis()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: false, queueIndexing: true);
        SetupSuccessfulServiceBusSend();

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AiDisabled_ReturnsSuccess()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: false, queueIndexing: true);
        SetupSuccessfulServiceBusSend();

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_AiDisabled_StillQueuesIndexing()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: false, queueIndexing: true);
        SetupSuccessfulServiceBusSend();

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _serviceBusSenderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AiDisabled_NoIndexing_DoesNotQueueJob()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, triggerAiProcessing: false, queueIndexing: false);

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _serviceBusSenderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync Tests - Content Type / Playbook Selection

    [Fact]
    public async Task ProcessAsync_EmailContentType_UsesEmailAnalysisPlaybook()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, contentType: "Email", triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, "Email Analysis", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, "Email Analysis", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_AttachmentContentType_UsesDocumentProfilePlaybook()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, contentType: "Attachment", triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, IAppOnlyAnalysisService.DefaultPlaybookName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, IAppOnlyAnalysisService.DefaultPlaybookName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_DocumentContentType_UsesDocumentProfilePlaybook()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, contentType: "Document", triggerAiProcessing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, IAppOnlyAnalysisService.DefaultPlaybookName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, IAppOnlyAnalysisService.DefaultPlaybookName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProcessAsync Tests - Duplicate Jobs (Idempotency)

    [Fact]
    public async Task ProcessAsync_DuplicateJob_AlreadyProcessed_ReturnsSuccess()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulServiceBusSend();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateJob_AlreadyProcessed_DoesNotCallAnalysisService()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulServiceBusSend();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateJob_StillQueuesIndexing()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, queueIndexing: true);
        SetupSuccessfulServiceBusSend();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // Already processed

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Should still queue indexing for consistency
        _serviceBusSenderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_ReturnsSuccessWithoutProcessing()
    {
        // Arrange - Another instance is processing this job
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(job.IdempotencyKey, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Lock not acquired - another instance has it

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Returns success to prevent retry
        result.Status.Should().Be(JobStatus.Completed);
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ProcessAsync Tests - Failure Scenarios

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsPoisonedOutcome()
    {
        // Arrange - Job with null payload
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = ProfileSummaryJobHandler.JobTypeName,
            SubjectId = "test",
            CorrelationId = Guid.NewGuid().ToString(),
            Attempt = 1,
            Payload = null
        };

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("payload");
    }

    [Fact]
    public async Task ProcessAsync_EmptyDocumentId_ReturnsPoisonedOutcome()
    {
        // Arrange - Job with empty document ID
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.Empty,
            Source = "UnitTest"
        };

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = ProfileSummaryJobHandler.JobTypeName,
            SubjectId = "test",
            CorrelationId = Guid.NewGuid().ToString(),
            Attempt = 1,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_PermanentError_ReturnsPoisonedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Document not found")); // Permanent failure

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_AccessDenied_ReturnsPoisonedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Access denied")); // Permanent failure

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_TransientError_ReturnsFailedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Service temporarily unavailable")); // Transient

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Should allow retry
    }

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsFailedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Retryable
    }

    [Fact]
    public async Task ProcessAsync_TaskCanceledException_ReturnsFailedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Retryable (timeout)
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_ReturnsPoisonedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned); // Not retryable
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_StillReleasesLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Analysis failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Lock should be released even on failure
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_PermanentFailure_StillQueuesIndexing()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId, queueIndexing: true);
        SetupSuccessfulIdempotencyFlow();
        SetupSuccessfulServiceBusSend();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Document not found")); // Permanent

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Should still queue indexing with partial data
        _serviceBusSenderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ProfileSummaryPayload Tests

    [Fact]
    public void ProfileSummaryPayload_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var payload = new ProfileSummaryPayload();

        // Assert
        payload.TriggerAiProcessing.Should().BeTrue();
        payload.QueueIndexing.Should().BeTrue();
    }

    [Fact]
    public void ProfileSummaryPayload_CanBeCreated()
    {
        // Arrange & Act
        var payload = new ProfileSummaryPayload
        {
            DocumentId = Guid.NewGuid(),
            ContentType = "Document",
            TriggerAiProcessing = true,
            QueueIndexing = true,
            Source = "OutlookSave",
            ProcessingJobId = Guid.NewGuid(),
            EnqueuedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                { "FileName", "test.pdf" },
                { "FileSize", 1024L }
            }
        };

        // Assert
        payload.DocumentId.Should().NotBeEmpty();
        payload.ContentType.Should().Be("Document");
        payload.Source.Should().Be("OutlookSave");
        payload.Metadata.Should().ContainKey("FileName");
    }

    [Fact]
    public void ProfileSummaryPayload_SerializesCorrectly()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var processingJobId = Guid.NewGuid();
        var payload = new ProfileSummaryPayload
        {
            DocumentId = documentId,
            ContentType = "Attachment",
            TriggerAiProcessing = false,
            QueueIndexing = true,
            Source = "WordSave",
            ProcessingJobId = processingJobId
        };

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<ProfileSummaryPayload>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DocumentId.Should().Be(documentId);
        deserialized.ContentType.Should().Be("Attachment");
        deserialized.TriggerAiProcessing.Should().BeFalse();
        deserialized.QueueIndexing.Should().BeTrue();
        deserialized.Source.Should().Be("WordSave");
        deserialized.ProcessingJobId.Should().Be(processingJobId);
    }

    #endregion
}
