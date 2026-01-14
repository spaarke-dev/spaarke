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
/// Unit tests for AppOnlyDocumentAnalysisJobHandler.
/// Tests job processing, idempotency, and error handling.
/// </summary>
public class AppOnlyDocumentAnalysisJobHandlerTests
{
    private readonly Mock<IAppOnlyAnalysisService> _analysisServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<DocumentTelemetry> _telemetryMock;
    private readonly Mock<ILogger<AppOnlyDocumentAnalysisJobHandler>> _loggerMock;
    private readonly AppOnlyDocumentAnalysisJobHandler _handler;

    public AppOnlyDocumentAnalysisJobHandlerTests()
    {
        _analysisServiceMock = new Mock<IAppOnlyAnalysisService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _telemetryMock = new Mock<DocumentTelemetry>();
        _loggerMock = new Mock<ILogger<AppOnlyDocumentAnalysisJobHandler>>();

        _handler = new AppOnlyDocumentAnalysisJobHandler(
            _analysisServiceMock.Object,
            _idempotencyServiceMock.Object,
            _telemetryMock.Object,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static JobContract CreateJobContract(Guid? documentId = null, string? idempotencyKey = null)
    {
        var docId = documentId ?? Guid.NewGuid();
        var payload = new AppOnlyDocumentAnalysisPayload
        {
            DocumentId = docId,
            PlaybookName = null,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
            SubjectId = docId.ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? $"analysis-{docId}-documentprofile",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    private static JobContract CreateJobContractWithPlaybook(Guid documentId, string playbookName)
    {
        var payload = new AppOnlyDocumentAnalysisPayload
        {
            DocumentId = documentId,
            PlaybookName = playbookName,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
            SubjectId = documentId.ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = $"analysis-{documentId}-documentprofile",
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

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectValue()
    {
        // Assert
        _handler.JobType.Should().Be("AppOnlyDocumentAnalysis");
    }

    [Fact]
    public void JobTypeName_Constant_HasExpectedValue()
    {
        // Assert
        AppOnlyDocumentAnalysisJobHandler.JobTypeName.Should().Be("AppOnlyDocumentAnalysis");
    }

    #endregion

    #region ProcessAsync Tests - New Jobs

    [Fact]
    public async Task ProcessAsync_NewJob_ChecksIdempotency()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_AcquiresProcessingLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.TryAcquireProcessingLockAsync(
                job.IdempotencyKey,
                It.Is<TimeSpan>(t => t == TimeSpan.FromMinutes(10)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_CallsAnalysisService()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_Success_ReturnsSuccessOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);
        result.JobType.Should().Be(AppOnlyDocumentAnalysisJobHandler.JobTypeName);
    }

    [Fact]
    public async Task ProcessAsync_NewJob_Success_MarksAsProcessed()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
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
    public async Task ProcessAsync_NewJob_Success_ReleasesLock()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCustomPlaybook_PassesPlaybookToService()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var playbookName = "Custom Analysis";
        var job = CreateJobContractWithPlaybook(documentId, playbookName);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, playbookName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Success(documentId, new Spaarke.Dataverse.UpdateDocumentRequest()));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        _analysisServiceMock.Verify(
            x => x.AnalyzeDocumentAsync(documentId, playbookName, It.IsAny<CancellationToken>()),
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
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
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
        var payload = new AppOnlyDocumentAnalysisPayload
        {
            DocumentId = Guid.Empty,
            Source = "UnitTest"
        };

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
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

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Document not found")); // Permanent failure

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessAsync_AnalysisFails_UnsupportedFileType_ReturnsPoisonedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "File type not supported")); // Permanent failure

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
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Failed); // Retryable
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_ReturnsPoisonedOutcome()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var job = CreateJobContract(documentId);
        SetupSuccessfulIdempotencyFlow();

        _analysisServiceMock
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
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
            .Setup(x => x.AnalyzeDocumentAsync(documentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DocumentAnalysisResult.Failed(documentId, "Analysis failed"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert - Lock should be released even on failure
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(job.IdempotencyKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    // Note: Telemetry tests removed because DocumentTelemetry is a concrete class
    // without virtual methods and cannot be mocked with Moq.
    // Telemetry behavior is verified through integration tests.

    #region AppOnlyDocumentAnalysisPayload Tests

    [Fact]
    public void AppOnlyDocumentAnalysisPayload_CanBeCreated()
    {
        // Arrange & Act
        var payload = new AppOnlyDocumentAnalysisPayload
        {
            DocumentId = Guid.NewGuid(),
            PlaybookName = "Test Playbook",
            Source = "EmailAttachment",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        // Assert
        payload.DocumentId.Should().NotBeEmpty();
        payload.PlaybookName.Should().Be("Test Playbook");
        payload.Source.Should().Be("EmailAttachment");
        payload.EnqueuedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AppOnlyDocumentAnalysisPayload_SerializesCorrectly()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var payload = new AppOnlyDocumentAnalysisPayload
        {
            DocumentId = documentId,
            PlaybookName = "Document Profile",
            Source = "Test"
        };

        // Act
        var json = JsonSerializer.Serialize(payload);
        var deserialized = JsonSerializer.Deserialize<AppOnlyDocumentAnalysisPayload>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.DocumentId.Should().Be(documentId);
        deserialized.PlaybookName.Should().Be("Document Profile");
    }

    #endregion
}
