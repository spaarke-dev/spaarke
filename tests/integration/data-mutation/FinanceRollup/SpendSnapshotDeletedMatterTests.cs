using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceRollup;

/// <summary>
/// A spend-snapshot job for a matter deleted since it was queued COMPLETES with a logged skip — it is not
/// poisoned (unified-access-control-r2 task 130).
/// </summary>
/// <remarks>
/// <para>Task 130 made the rollup write update-only (<c>If-Match: *</c>) so it can no longer recreate a deleted
/// matter. The price is that a missing matter now surfaces as <see cref="KeyNotFoundException"/>, which the
/// handler's retry classification treats as permanent: without the skip, every such job would be poisoned and
/// dead-lettered for a matter that simply no longer exists.</para>
/// <para>The rollup service is substituted at its <c>virtual</c> entry point (its reads unwrap a concrete
/// <c>ServiceClient</c>, which no double can stand in for); the handler is the production type.</para>
/// </remarks>
public class SpendSnapshotDeletedMatterTests
{
    private readonly Mock<ISpendSnapshotService> _snapshots = new(MockBehavior.Strict);
    private readonly Mock<ISignalEvaluationService> _signals = new(MockBehavior.Strict);

    [Fact]
    public async Task MatterDeletedBeforeTheRollup_CompletesTheJob_InsteadOfPoisoningIt()
    {
        var matterId = Guid.NewGuid();
        ArrangeSnapshotAndSignals(matterId);
        var rollup = new RollupDouble(new KeyNotFoundException("sprk_matter record was not found."));

        var outcome = await Handler(rollup).ProcessAsync(Job(matterId, attempt: 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        rollup.MatterCalls.Should().Equal(matterId);
        _snapshots.Verify(s => s.GenerateAsync(matterId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _signals.Verify(s => s.EvaluateAsync(matterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransientRollupFailure_IsStillRetried_TheSkipIsOnlyForAMissingMatter()
    {
        var matterId = Guid.NewGuid();
        ArrangeSnapshotAndSignals(matterId);
        var rollup = new RollupDouble(new HttpRequestException("429 throttled"));

        var outcome = await Handler(rollup).ProcessAsync(Job(matterId, attempt: 1), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Failed, "a throttled write must be retried, not swallowed as a skip");
    }

    private void ArrangeSnapshotAndSignals(Guid matterId)
    {
        _snapshots.Setup(s => s.GenerateAsync(matterId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _signals.Setup(s => s.EvaluateAsync(matterId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
    }

    private SpendSnapshotGenerationJobHandler Handler(FinanceRollupService rollup) => new(
        _snapshots.Object, _signals.Object, rollup, new FinanceTelemetry(),
        NullLogger<SpendSnapshotGenerationJobHandler>.Instance);

    private static JobContract Job(Guid matterId, int attempt) => new()
    {
        JobId = Guid.NewGuid(),
        JobType = SpendSnapshotGenerationJobHandler.JobTypeName,
        SubjectId = matterId.ToString(),
        CorrelationId = "corr-130",
        Attempt = attempt,
        MaxAttempts = 3,
        Payload = JsonSerializer.SerializeToDocument(new { matterId }),
    };

    /// <summary>The rollup service at its virtual seam: records the matter it was asked about, then throws.</summary>
    private sealed class RollupDouble : FinanceRollupService
    {
        private readonly Exception _failure;

        public RollupDouble(Exception failure)
            : base(Mock.Of<IDataverseService>(), Mock.Of<IFieldMappingDataverseService>(),
                NullLogger<FinanceRollupService>.Instance)
        {
            _failure = failure;
        }

        public List<Guid> MatterCalls { get; } = new();

        public override Task<RecalculateFinanceResponse> RecalculateMatterAsync(Guid matterId, CancellationToken ct = default)
        {
            MatterCalls.Add(matterId);
            return Task.FromException<RecalculateFinanceResponse>(_failure);
        }
    }
}
