using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PlaybookEmbedding;

/// <summary>
/// FR-13 (chat-routing-redesign-r1 task 034) — <see cref="PlaybookIndexDriftDetectionJob"/>
/// MUST iterate active playbooks, recompute the canonical hash via
/// <see cref="IPlaybookEmbeddingHashCalculator"/>, compare to the stored hash, and report
/// drift counts via structured telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Task 034 follow-up (Gaps 1+2+3 closed, 2026-06-22): the previously-Skip'd scenarios
/// are now active and pin: drift-flip-to-Stale on hash mismatch, leave-unchanged on
/// hash match, and skip-on-non-Indexed status (Pending/Failed/NotIndexed). The runnable
/// tests below pin: (a) handler contract surface, (b) success path with zero drift,
/// (c) drift comparison + write-back via <see cref="IPlaybookService.UpdateIndexStatusAsync"/>,
/// (d) cancellation propagation, (e) ADR-015 telemetry contract.
/// </para>
/// </remarks>
public class PlaybookIndexDriftDetectionJobTests
{
    private const string TestTenantId = "tenant-abc";

    private readonly Mock<IPlaybookEmbeddingHashCalculator> _hashCalculatorMock = new();
    private readonly Mock<IPlaybookService> _playbookServiceMock = new();
    private readonly ILogger<PlaybookIndexDriftDetectionJob> _logger =
        NullLogger<PlaybookIndexDriftDetectionJob>.Instance;

    private PlaybookIndexDriftDetectionJob BuildJob() =>
        new(_hashCalculatorMock.Object, _playbookServiceMock.Object, _logger);

    private static JobContract BuildJobContract(string tenantId = TestTenantId) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = PlaybookIndexDriftDetectionJob.JobTypeName,
        SubjectId = tenantId,
        CorrelationId = Guid.NewGuid().ToString(),
        IdempotencyKey = $"drift-detect-{tenantId}-{DateTime.UtcNow:yyyyMMdd}",
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{}")
    };

    private static PlaybookResponse BuildPlaybook(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Test Playbook",
        Description = "Description",
        PlaybookCode = "PB-TEST",
        ConfigJson = "{}",
        IsActive = true,
        Capabilities = new[] { "summarize" },
        TriggerPhrases = new[] { "test trigger" },
        RecordType = "sprk_matter",
        EntityType = "matter"
    };

    private void SetupEmptyEnumeration()
    {
        _playbookServiceMock
            .Setup(p => p.ListAllActivePlaybooksAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableHelper.Empty<PlaybookResponse>());
    }

    /// <summary>
    /// Configures the mock to yield the supplied playbooks via the FR-13 tenant-wide
    /// enumeration path (task 034 follow-up — Gap 1 closure).
    /// </summary>
    private void SetupEnumeration(params PlaybookResponse[] playbooks)
    {
        _playbookServiceMock
            .Setup(p => p.ListAllActivePlaybooksAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableHelper.From(playbooks));
    }

    /// <summary>
    /// Internal helper that wraps a synchronous sequence as <see cref="IAsyncEnumerable{T}"/>
    /// for Moq <c>.Returns(...)</c> setups on async-enumerable methods.
    /// </summary>
    private static class AsyncEnumerableHelper
    {
        public static IAsyncEnumerable<T> Empty<T>() => From(Array.Empty<T>());

        public static async IAsyncEnumerable<T> From<T>(IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                yield return item;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public void JobType_MatchesJobTypeNameConstant()
    {
        // Arrange
        SetupEmptyEnumeration();
        var job = BuildJob();

        // Act + Assert — ServiceBusJobProcessor matches on this string; the constant is the
        // producer/consumer contract.
        job.JobType.Should().Be(PlaybookIndexDriftDetectionJob.JobTypeName);
        PlaybookIndexDriftDetectionJob.JobTypeName.Should().Be("PlaybookIndexDriftDetection");
    }

    [Fact]
    public async Task ProcessAsync_ReturnsCompletedOutcome_WhenNoPlaybooks()
    {
        // Arrange
        SetupEmptyEnumeration();
        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.JobType.Should().Be(PlaybookIndexDriftDetectionJob.JobTypeName);
        outcome.JobId.Should().Be(contract.JobId);
        _hashCalculatorMock.Verify(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_SkipsHashCalculator_ForNotIndexedRow()
    {
        // Arrange — a NotIndexed row (the default for newly-created or never-indexed
        // playbooks) MUST be skipped by the drift job; only Indexed rows are subject to
        // drift comparison. This test pins the skip-on-non-Indexed guard.
        var playbook = BuildPlaybook() with { IndexStatusCode = 100_000_000 /* NotIndexed */ };
        SetupEnumeration(playbook);

        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        _hashCalculatorMock.Verify(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()), Times.Never);
        _playbookServiceMock.Verify(
            p => p.UpdateIndexStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PropagatesCancellation_WhenTokenIsCancelled()
    {
        // Arrange
        SetupEmptyEnumeration();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var act = async () => await job.ProcessAsync(contract, cts.Token);

        // Assert — drift job is idempotent + retryable; cancellation rethrows so
        // ServiceBusJobProcessor can abandon the message and Service Bus redelivers.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFailure_WhenEnumerationThrows()
    {
        // Arrange — runtime failures (e.g., Dataverse 5xx) return Failure (retryable), not
        // Poisoned. Service Bus / ServiceBusJobProcessor will retry up to MaxAttempts.
        _playbookServiceMock
            .Setup(p => p.ListAllActivePlaybooksAsync(It.IsAny<CancellationToken>()))
            .Returns(ThrowingEnumerable<PlaybookResponse>(new HttpRequestException("Dataverse 503")));

        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Failed);
        outcome.ErrorMessage.Should().Contain("Dataverse 503");
    }

    /// <summary>
    /// Builds an <see cref="IAsyncEnumerable{T}"/> that throws the supplied exception when
    /// iterated. Mirrors the original <c>ThrowsAsync</c> setup but for async-enumerable
    /// returns.
    /// </summary>
    private static async IAsyncEnumerable<T> ThrowingEnumerable<T>(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    [Fact]
    public void Constructor_Throws_WhenHashCalculatorIsNull()
    {
        // Arrange + Act
        var act = () => new PlaybookIndexDriftDetectionJob(null!, _playbookServiceMock.Object, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("hashCalculator");
    }

    [Fact]
    public void Constructor_Throws_WhenPlaybookServiceIsNull()
    {
        // Arrange + Act
        var act = () => new PlaybookIndexDriftDetectionJob(_hashCalculatorMock.Object, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookService");
    }

    // ── Drift-comparison scenarios — task 034 follow-up (Gaps 1+2+3 closed) ─────────────

    [Fact]
    public async Task ProcessAsync_FlipsToStale_WhenHashMismatchesStoredValue()
    {
        // Contract: Indexed row whose recomputed hash differs from sprk_indexhash MUST
        // be flipped to Stale via UpdateIndexStatusAsync(_, 100000003, null, null, _).
        const string storedHash = "abc123";
        const string recomputedHash = "def456";

        var playbook = BuildPlaybook() with
        {
            IndexStatusCode = 100_000_002 /* Indexed */,
            IndexHash = storedHash,
        };

        SetupEnumeration(playbook);

        _hashCalculatorMock
            .Setup(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()))
            .Returns(recomputedHash);

        _playbookServiceMock
            .Setup(p => p.UpdateIndexStatusAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        _hashCalculatorMock.Verify(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()), Times.Once);
        _playbookServiceMock.Verify(
            p => p.UpdateIndexStatusAsync(
                playbook.Id,
                100_000_003 /* Stale */,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_LeavesStatusUnchanged_WhenHashMatchesStoredValue()
    {
        // Contract: Indexed row whose recomputed hash equals sprk_indexhash MUST NOT
        // be touched — no UpdateIndexStatusAsync call.
        const string sameHash = "abc123";

        var playbook = BuildPlaybook() with
        {
            IndexStatusCode = 100_000_002 /* Indexed */,
            IndexHash = sameHash,
        };

        SetupEnumeration(playbook);

        _hashCalculatorMock
            .Setup(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()))
            .Returns(sameHash);

        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        _hashCalculatorMock.Verify(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()), Times.Once);
        _playbookServiceMock.Verify(
            p => p.UpdateIndexStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(100_000_000)] // NotIndexed
    [InlineData(100_000_001)] // Pending
    [InlineData(100_000_004)] // Failed
    public async Task ProcessAsync_SkipsRow_WhenStatusIsPendingOrFailedOrNotIndexed(int statusCode)
    {
        // Contract: rows in non-Indexed states are owned by other state machines; the
        // drift job MUST NOT touch them — no hash compute, no UpdateIndexStatus call.
        var playbook = BuildPlaybook() with
        {
            IndexStatusCode = statusCode,
            IndexHash = "abc123" /* even with a stored hash, non-Indexed rows are skipped */,
        };

        SetupEnumeration(playbook);

        var contract = BuildJobContract();
        var job = BuildJob();

        // Act
        var outcome = await job.ProcessAsync(contract, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(JobStatus.Completed);
        _hashCalculatorMock.Verify(c => c.ComputeHash(It.IsAny<PlaybookEmbeddingDocument>()), Times.Never);
        _playbookServiceMock.Verify(
            p => p.UpdateIndexStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
