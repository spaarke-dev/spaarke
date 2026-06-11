using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Jobs.Handlers;

/// <summary>
/// Unit tests for RagIndexingJobHandler.
///
/// Verifies:
/// - Successful pipeline call emits JobOutcome.Completed
/// - Idempotency key format: rag-index-{driveId}-{itemId} when not pre-set
/// - Idempotency check prevents duplicate processing
/// - Exception from pipeline produces a failure/poisoned outcome
/// </summary>
public class RagIndexingJobHandlerTests
{
    private readonly Mock<IFileIndexingService> _fileIndexingServiceMock;
    private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ISearchIndexNameResolver> _searchIndexNameResolverMock;
    private readonly Mock<ILogger<RagIndexingJobHandler>> _loggerMock;
    private readonly RagIndexingJobHandler _handler;

    private const string TestTenantId = "tenant-abc";
    private const string TestDriveId = "drive-001";
    private const string TestItemId = "item-001";
    private const string TestDocumentId = "doc-001";
    private const string TestFileName = "contract.pdf";

    public RagIndexingJobHandlerTests()
    {
        _fileIndexingServiceMock = new Mock<IFileIndexingService>();
        _idempotencyServiceMock = new Mock<IIdempotencyService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _searchIndexNameResolverMock = new Mock<ISearchIndexNameResolver>();
        _loggerMock = new Mock<ILogger<RagIndexingJobHandler>>();

        var analysisOptions = Options.Create(new AnalysisOptions
        {
            SharedIndexName = "spaarke-knowledge-index-v2"
        });

        // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — default the resolver to
        // return null (tenant-default fall-through) so existing tests behave UNCHANGED. Tests
        // exercising explicit-routing branches override this Setup.
        _searchIndexNameResolverMock
            .Setup(x => x.ResolveAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // RagTelemetry is a concrete class — create a real instance.
        // Its methods are not virtual so cannot be mocked; telemetry
        // behaviour is verified through integration tests (see note in
        // AppOnlyDocumentAnalysisJobHandlerTests).
        var telemetry = new RagTelemetry();

        _handler = new RagIndexingJobHandler(
            _fileIndexingServiceMock.Object,
            _idempotencyServiceMock.Object,
            _dataverseServiceMock.Object,
            _searchIndexNameResolverMock.Object,
            analysisOptions,
            telemetry,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static JobContract CreateJobContract(
        string? driveId = null,
        string? itemId = null,
        string? documentId = null,
        string? idempotencyKey = null)
    {
        var payload = new RagIndexingJobPayload
        {
            TenantId = TestTenantId,
            DriveId = driveId ?? TestDriveId,
            ItemId = itemId ?? TestItemId,
            FileName = TestFileName,
            DocumentId = documentId ?? TestDocumentId,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = documentId ?? TestDocumentId,
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = idempotencyKey ?? string.Empty,
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    private void SetupSuccessfulIdempotencyFlow(string idempotencyKey = "")
    {
        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _idempotencyServiceMock
            .Setup(x => x.MarkEventAsProcessedAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _idempotencyServiceMock
            .Setup(x => x.ReleaseProcessingLockAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region JobType Tests

    [Fact]
    public void JobType_ReturnsCorrectValue()
    {
        _handler.JobType.Should().Be("RagIndexing");
    }

    [Fact]
    public void JobTypeName_Constant_HasExpectedValue()
    {
        RagIndexingJobHandler.JobTypeName.Should().Be("RagIndexing");
    }

    #endregion

    #region Successful Pipeline Call

    [Fact]
    public async Task ProcessAsync_SuccessfulIndexing_CallsFileIndexingService()
    {
        // Arrange
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(
                chunksIndexed: 5,
                duration: TimeSpan.FromSeconds(1)));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — FileIndexingService was called with correct parameters
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.Is<FileIndexRequest>(r =>
                    r.DriveId == TestDriveId &&
                    r.ItemId == TestItemId &&
                    r.TenantId == TestTenantId &&
                    r.FileName == TestFileName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulIndexing_ReturnsJobOutcomeSucceeded()
    {
        // Arrange
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(
                chunksIndexed: 10,
                duration: TimeSpan.FromSeconds(2)));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — JobOutcome.Success maps to JobStatus.Completed
        result.Should().NotBeNull();
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);
        result.JobType.Should().Be(RagIndexingJobHandler.JobTypeName);
    }

    #endregion

    #region Idempotency Key Format

    [Fact]
    public async Task ProcessAsync_NoIdempotencyKey_BuildsKeyFromDriveIdAndItemId()
    {
        // Arrange — no pre-set idempotency key, handler should build it as rag-index-{driveId}-{itemId}
        var driveId = "drive-xyz";
        var itemId = "item-xyz";
        var job = CreateJobContract(driveId: driveId, itemId: itemId, idempotencyKey: string.Empty);
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(
                chunksIndexed: 3,
                duration: TimeSpan.FromSeconds(1)));

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — idempotency service called with the rag-index-{driveId}-{itemId} key
        var expectedKey = $"rag-index-{driveId}-{itemId}";

        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(
                expectedKey,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithPreSetIdempotencyKey_UsesProvidedKey()
    {
        // Arrange — when AnalysisOrchestrationService sets the key to "{tenantId}:{documentId}"
        var customKey = $"{TestTenantId}:{TestDocumentId}";
        var job = CreateJobContract(idempotencyKey: customKey);
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(
                chunksIndexed: 3,
                duration: TimeSpan.FromSeconds(1)));

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — the pre-set key is used as-is (handler does not override when key is non-empty)
        _idempotencyServiceMock.Verify(
            x => x.IsEventProcessedAsync(customKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Idempotency — Duplicate Prevention

    [Fact]
    public async Task ProcessAsync_AlreadyProcessed_ReturnsSuccessWithoutCallingPipeline()
    {
        // Arrange — mark as already processed
        var job = CreateJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — success returned, indexing service never called
        result.Status.Should().Be(JobStatus.Completed);

        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Failure Scenarios

    [Fact]
    public async Task ProcessAsync_InvalidPayload_ReturnsPoisonedOutcome()
    {
        // Arrange — job with null payload (cannot parse driveId/itemId)
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = "test",
            CorrelationId = Guid.NewGuid().ToString(),
            Attempt = 1,
            Payload = null
        };

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessAsync_PipelineThrowsHttpRequestException_ReturnsFailedOutcome()
    {
        // Arrange — transient HTTP error should produce a retryable failure
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service temporarily unavailable"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — HTTP errors are transient → retryable → JobStatus.Failed
        result.Status.Should().Be(JobStatus.Failed);
        result.ErrorMessage.Should().Contain("temporarily unavailable");
    }

    [Fact]
    public async Task ProcessAsync_PipelineThrowsPermanentException_ReturnsPoisonedOutcome()
    {
        // Arrange — non-retryable exception should produce a poisoned (dead-letter) outcome
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected configuration error"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — non-HTTP exceptions are permanent → dead-lettered → JobStatus.Poisoned
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("Unexpected configuration error");
    }

    [Fact]
    public async Task ProcessAsync_IndexingFails_PermanentError_ReturnsPoisonedOutcome()
    {
        // Arrange — pipeline returns failure with "not found" message → permanent
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Failed("Document not found in SharePoint Embedded"));

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — "not found" is a permanent failure
        result.Status.Should().Be(JobStatus.Poisoned);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ProcessAsync_LockNotAcquired_ReturnsSuccessWithoutProcessing()
    {
        // Arrange — another instance holds the processing lock
        var job = CreateJobContract();

        _idempotencyServiceMock
            .Setup(x => x.IsEventProcessedAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _idempotencyServiceMock
            .Setup(x => x.TryAcquireProcessingLockAsync(
                It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Lock held by another instance

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — success returned to prevent retry (another instance is processing)
        result.Status.Should().Be(JobStatus.Completed);

        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Lock Release

    [Fact]
    public async Task ProcessAsync_IndexingFails_StillReleasesProcessingLock()
    {
        // Arrange
        var job = CreateJobContract();
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Failed("Transient error"));

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — lock is always released in the finally block
        _idempotencyServiceMock.Verify(
            x => x.ReleaseProcessingLockAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SearchIndexName Threading (multi-container-multi-index-r1 indexer-routing-fix Tier 3)

    private static JobContract CreateJobContractWithSearchIndexName(string? searchIndexName)
    {
        var payload = new RagIndexingJobPayload
        {
            TenantId = TestTenantId,
            DriveId = TestDriveId,
            ItemId = TestItemId,
            FileName = TestFileName,
            DocumentId = TestDocumentId,
            Source = "UnitTest",
            EnqueuedAt = DateTimeOffset.UtcNow,
            SearchIndexName = searchIndexName
        };

        return new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = TestDocumentId,
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = string.Empty,
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload))
        };
    }

    [Fact]
    public async Task ProcessAsync_PayloadSearchIndexNameSet_ThreadsToFileIndexRequest()
    {
        // Arrange — payload carries an explicit SearchIndexName (enqueueing site set it).
        // The handler MUST pass it through to FileIndexRequest verbatim WITHOUT calling
        // the resolver (the resolver is a fall-back only).
        var job = CreateJobContractWithSearchIndexName("spaarke-file-index");
        SetupSuccessfulIdempotencyFlow();

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1)));

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — FileIndexRequest.SearchIndexName carries the payload value
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.Is<FileIndexRequest>(r => r.SearchIndexName == "spaarke-file-index"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Resolver MUST NOT be invoked when payload value is present
        _searchIndexNameResolverMock.Verify(
            x => x.ResolveAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PayloadSearchIndexNameNull_InvokesResolverAsFallback()
    {
        // Arrange — payload.SearchIndexName is null. Handler MUST call the resolver and
        // pass the result to FileIndexRequest.
        var job = CreateJobContractWithSearchIndexName(searchIndexName: null);
        SetupSuccessfulIdempotencyFlow();

        _searchIndexNameResolverMock
            .Setup(x => x.ResolveAsync(
                TestDocumentId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("spaarke-knowledge-index-v2");

        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1)));

        // Act
        await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — resolver consulted, FileIndexRequest carries resolver result
        _searchIndexNameResolverMock.Verify(
            x => x.ResolveAsync(
                TestDocumentId, It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.Is<FileIndexRequest>(r => r.SearchIndexName == "spaarke-knowledge-index-v2"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_IndexNotAllowed_FallsBackToTenantDefaultAndRetries()
    {
        // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — user-confirmed
        // decision: background jobs default-fall-back on INDEX_NOT_ALLOWED. Handler MUST
        // catch the exception, log WARN, and retry IndexFileAppOnlyAsync with
        // SearchIndexName=null so the batch still lands in the tenant default.

        // Arrange — payload has a stale searchIndexName that the allow-list rejects.
        var job = CreateJobContractWithSearchIndexName("stale-index-name");
        SetupSuccessfulIdempotencyFlow();

        var callCount = 0;
        _fileIndexingServiceMock
            .Setup(x => x.IndexFileAppOnlyAsync(
                It.IsAny<FileIndexRequest>(), It.IsAny<CancellationToken>()))
            .Returns<FileIndexRequest, CancellationToken>((req, ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: throw INDEX_NOT_ALLOWED (simulating allow-list rejection).
                    throw new Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException(
                        code: "INDEX_NOT_ALLOWED",
                        title: "AI Search index not allowed",
                        detail: $"The requested AI Search index '{req.SearchIndexName}' is not in the allow-list.",
                        statusCode: 400);
                }
                // Second call: retried with SearchIndexName=null → succeeds.
                return Task.FromResult(FileIndexingResult.Succeeded(chunksIndexed: 3, duration: TimeSpan.FromSeconds(1)));
            });

        // Act
        var result = await _handler.ProcessAsync(job, CancellationToken.None);

        // Assert — job ultimately succeeds (default-fall-back retry succeeded)
        result.Status.Should().Be(JobStatus.Completed);

        // First call had the rejected index name, second call had null
        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.Is<FileIndexRequest>(r => r.SearchIndexName == "stale-index-name"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _fileIndexingServiceMock.Verify(
            x => x.IndexFileAppOnlyAsync(
                It.Is<FileIndexRequest>(r => r.SearchIndexName == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
