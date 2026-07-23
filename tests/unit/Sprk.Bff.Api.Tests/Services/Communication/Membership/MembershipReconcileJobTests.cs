using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication.Membership;

/// <summary>
/// Behavior of <see cref="MembershipReconcileJob"/> — the non-fatal / DLQ outcome mapping (task 041 / FR-07,
/// NFR-02). The reconcile runs off the shared Service Bus queue: a transient failure is RETRIED, a repeated or
/// terminal failure is POISONED (dead-lettered, never dropped), and a successful/idempotent run completes.
/// A reconcile failure surfaces as a <see cref="JobOutcome"/> — it NEVER throws to a send/capture path
/// (reconcile is background work, decoupled from persistence).
/// </summary>
public class MembershipReconcileJobTests
{
    private static readonly Guid ThreadId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IMembershipReconciler> _reconciler = new();

    private MembershipReconcileJob BuildSut() => new(_reconciler.Object, Mock.Of<ILogger<MembershipReconcileJob>>());

    private static JobContract JobWith(MembershipReconcileJobPayload payload, int attempt = 1, int maxAttempts = 3) => new()
    {
        JobType = MembershipReconcileJobPayload.JobType,
        Attempt = attempt,
        MaxAttempts = maxAttempts,
        CorrelationId = "corr-1",
        Payload = JsonSerializer.SerializeToDocument(payload),
    };

    [Fact]
    public async Task ProcessAsync_WhenReconcileSucceeds_CompletesJob()
    {
        _reconciler
            .Setup(r => r.ReconcileAsync(ThreadId, It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipReconcileResult { ThreadId = ThreadId, Added = 1, Removed = 0 });

        var outcome = await BuildSut().ProcessAsync(
            JobWith(new MembershipReconcileJobPayload { ThreadId = ThreadId, Trigger = MembershipReconcileTrigger.RecordAccessChanged }),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_WhenReconcileConsistent_IsIdempotentCompletion()
    {
        _reconciler
            .Setup(r => r.ReconcileAsync(ThreadId, It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MembershipReconcileResult { ThreadId = ThreadId, Added = 0, Removed = 0 }); // no-op

        var outcome = await BuildSut().ProcessAsync(
            JobWith(new MembershipReconcileJobPayload { ThreadId = ThreadId, Trigger = MembershipReconcileTrigger.PeriodicSweep }),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_WhenTransientFailureBelowMaxAttempts_ReturnsFailedForRetry()
    {
        _reconciler
            .Setup(r => r.ReconcileAsync(ThreadId, It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("ACS transient"));

        var outcome = await BuildSut().ProcessAsync(
            JobWith(new MembershipReconcileJobPayload { ThreadId = ThreadId, Trigger = MembershipReconcileTrigger.PeriodicSweep }, attempt: 1, maxAttempts: 3),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed); // retried by the processor
    }

    [Fact]
    public async Task ProcessAsync_WhenTransientFailureAtMaxAttempts_PoisonsForDeadLetter()
    {
        _reconciler
            .Setup(r => r.ReconcileAsync(ThreadId, It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("ACS transient"));

        var outcome = await BuildSut().ProcessAsync(
            JobWith(new MembershipReconcileJobPayload { ThreadId = ThreadId, Trigger = MembershipReconcileTrigger.PeriodicSweep }, attempt: 3, maxAttempts: 3),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned); // dead-lettered, never dropped
    }

    [Fact]
    public async Task ProcessAsync_WhenInvariantGuardTrips_PoisonsImmediately()
    {
        // The projection-invariant guard throws InvalidOperationException (non-retryable) → dead-letter, do not loop.
        _reconciler
            .Setup(r => r.ReconcileAsync(ThreadId, It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("projection invariant: ACS membership ⊆ Dataverse-derived access"));

        var outcome = await BuildSut().ProcessAsync(
            JobWith(new MembershipReconcileJobPayload { ThreadId = ThreadId, Trigger = MembershipReconcileTrigger.RecordAccessChanged }, attempt: 1, maxAttempts: 3),
            CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadNull_PoisonsWithoutInvokingReconcile()
    {
        var job = new JobContract { JobType = MembershipReconcileJobPayload.JobType, Payload = null };

        var outcome = await BuildSut().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        _reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_WhenPayloadMalformed_Poisons()
    {
        // A JSON scalar cannot deserialize into the payload record → JsonException → dead-letter.
        var job = new JobContract { JobType = MembershipReconcileJobPayload.JobType, Payload = JsonDocument.Parse("\"not-an-object\"") };

        var outcome = await BuildSut().ProcessAsync(job, CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Poisoned);
        _reconciler.Verify(r => r.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<MembershipReconcileTrigger>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
