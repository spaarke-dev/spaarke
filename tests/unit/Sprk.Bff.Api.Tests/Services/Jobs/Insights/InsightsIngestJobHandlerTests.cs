using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Insights;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Insights;

/// <summary>
/// Unit tests for <see cref="InsightsIngestJobHandler"/> (task 050, D-P8).
/// Verifies JobType routing, idempotency short-circuit + lock acquisition, payload validation,
/// dispatch shape to <see cref="IInsightsAi.RunIngestAsync"/>, success / retry / poison
/// outcome mapping, and lock release on both success and failure paths.
/// </summary>
public class InsightsIngestJobHandlerTests
{
    private readonly Mock<IInsightsAi> _insightsAiMock = new(MockBehavior.Strict);
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock = new();
    private readonly Mock<ILogger<InsightsIngestJobHandler>> _loggerMock = new();
    private readonly InsightsIngestJobHandler _handler;

    public InsightsIngestJobHandlerTests()
    {
        _handler = new InsightsIngestJobHandler(
            _insightsAiMock.Object,
            _idempotencyServiceMock.Object,
            _loggerMock.Object);
    }

    // ---------- Helpers ----------

    private static JobContract CreateJob(
        string? documentId = "doc-001",
        string? matterId = "mat-001",
        string? tenantId = "tenant-001",
        string? idempotencyKey = null)
    {
        var payload = new InsightsIngestPayload
        {
            DocumentId = documentId,
            MatterId = matterId,
            TenantId = tenantId,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = InsightsIngestJobHandler.JobTypeName,
            SubjectId = documentId ?? "doc-unknown",
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Attempt = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
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
            .Setup(x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _idempotencyServiceMock
            .Setup(x => x.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static InsightsIngestResult SuccessResult(int observations = 4, string? layer1 = "closing_letter", bool layer2 = true)
        => new(observations, layer1, layer2);

    // ---------- Constructor + JobType ----------

    [Fact]
    public void Constructor_NullInsightsAi_Throws()
    {
        var act = () => new InsightsIngestJobHandler(null!, _idempotencyServiceMock.Object, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("insightsAi");
    }

    [Fact]
    public void Constructor_NullIdempotencyService_Throws()
    {
        var act = () => new InsightsIngestJobHandler(_insightsAiMock.Object, null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("idempotencyService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new InsightsIngestJobHandler(_insightsAiMock.Object, _idempotencyServiceMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void JobType_ReturnsConstantValue()
    {
        _handler.JobType.Should().Be("InsightsUniversalIngest");
        InsightsIngestJobHandler.JobTypeName.Should().Be("InsightsUniversalIngest");
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task ProcessAsync_ValidPayload_DispatchesToFacadeWithCorrectRequest()
    {
        // Arrange
        var job = CreateJob(documentId: "doc-42", matterId: "mat-99", tenantId: "tenant-x");
        SetupSuccessfulIdempotencyFlow();

        InsightsIngestRequest? captured = null;
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InsightsIngestRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(SuccessResult());

        // Act
        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        captured.Should().NotBeNull();
        captured!.DocumentId.Should().Be("doc-42");
        captured.MatterId.Should().Be("mat-99");
        captured.TenantId.Should().Be("tenant-x");
    }

    [Fact]
    public async Task ProcessAsync_ValidPayload_ReturnsSuccessOutcome()
    {
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.JobId.Should().Be(job.JobId);
        outcome.JobType.Should().Be("InsightsUniversalIngest");
    }

    [Fact]
    public async Task ProcessAsync_ValidPayload_MarksAsProcessedAfterSuccess()
    {
        var job = CreateJob(documentId: "doc-abc", matterId: "mat-xyz");
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        await _handler.ProcessAsync(job, CancellationToken.None);

        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(
                "insights-ingest-doc-abc-mat-xyz",
                It.Is<TimeSpan?>(t => t == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ValidPayload_AcquiresAndReleasesLock()
    {
        var job = CreateJob(documentId: "d", matterId: "m");
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        await _handler.ProcessAsync(job, CancellationToken.None);

        _idempotencyServiceMock.Verify(
            x => x.TryAcquireProcessingLockAsync("insights-ingest-d-m", TimeSpan.FromMinutes(10), It.IsAny<CancellationToken>()),
            Times.Once);
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync("insights-ingest-d-m", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_JobContractIdempotencyKey_OverridesComposedKey()
    {
        var job = CreateJob(idempotencyKey: "custom-key-123");
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResult());

        await _handler.ProcessAsync(job, CancellationToken.None);

        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync("custom-key-123", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ZeroObservationsEmitted_StillReturnsSuccess()
    {
        // D-P9 + D-P10 gates legitimately produce zero Observations; this is success.
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsIngestResult(0, "correspondence_email", false));

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
    }

    // ---------- Idempotency short-circuits ----------

    [Fact]
    public async Task ProcessAsync_AlreadyProcessed_SkipsFacadeCall()
    {
        var job = CreateJob();
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        _insightsAiMock.Verify(
            x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_SkipsFacadeCall()
    {
        var job = CreateJob();
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        _insightsAiMock.Verify(
            x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------- Payload validation ----------

    [Fact]
    public async Task ProcessAsync_NullPayload_Poisons()
    {
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = InsightsIngestJobHandler.JobTypeName,
            SubjectId = "doc",
            CorrelationId = "corr",
            Attempt = 1,
            Payload = null
        };

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.ErrorMessage.Should().Contain("Invalid InsightsIngest payload");
    }

    [Theory]
    [InlineData(null, "mat", "tenant")]
    [InlineData("doc", null, "tenant")]
    [InlineData("doc", "mat", null)]
    [InlineData("", "mat", "tenant")]
    [InlineData("doc", "", "tenant")]
    [InlineData("doc", "mat", "")]
    [InlineData("  ", "mat", "tenant")]
    public async Task ProcessAsync_MissingRequiredField_Poisons(string? doc, string? mat, string? tenant)
    {
        var job = CreateJob(documentId: doc, matterId: mat, tenantId: tenant);

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        _insightsAiMock.Verify(
            x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NullJob_Throws()
    {
        Func<Task> act = () => _handler.ProcessAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ---------- Failure semantics (Retry vs Poison) ----------

    [Fact]
    public async Task ProcessAsync_HttpRequestException_ReturnsFailureForRetry()
    {
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection reset"));

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed); // Allows Service Bus redelivery
    }

    [Fact]
    public async Task ProcessAsync_TaskCanceledException_ReturnsFailureForRetry()
    {
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HTTP timeout"));

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task ProcessAsync_ArgumentException_PoisonsImmediately()
    {
        // The facade itself raises ArgumentException for bad payload — never retryable.
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("DocumentId is required."));

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_UnknownException_PoisonsImmediately()
    {
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var outcome = await _handler.ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_FacadeFailure_StillReleasesLock()
    {
        var job = CreateJob(documentId: "dx", matterId: "mx");
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("downstream 503"));

        await _handler.ProcessAsync(job, CancellationToken.None);

        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync("insights-ingest-dx-mx", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_FacadeFailure_DoesNotMarkAsProcessed()
    {
        // Don't mark as processed so retry will be allowed when message redelivers.
        var job = CreateJob();
        SetupSuccessfulIdempotencyFlow();
        _insightsAiMock
            .Setup(x => x.RunIngestAsync(It.IsAny<InsightsIngestRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"));

        await _handler.ProcessAsync(job, CancellationToken.None);

        _idempotencyServiceMock.Verify(
            x => x.MarkEventAsProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
