// -----------------------------------------------------------------------------
// QuarantineClearServiceTests.cs
//
// L2 CONTROL-PLANE tests for <see cref="QuarantineClearService"/> — the
// Quarantined -> Failed transition service (task 061).
//
// TESTED BEHAVIORS (POML acceptance criteria):
//   - Happy path: Quarantined run + Operator + reason -> Success + Cosmos state
//     transitioned to Failed + QuarantineInfo.State = Cleared + ClearedBy/At
//     populated from injected TimeProvider.
//   - Wrong-state (Running, WaitingOnGate, Completed, Failed, Cancelled) ->
//     Conflict(CurrentStatus); NO state mutation persisted.
//   - Missing reason (null/whitespace/empty) -> ArgumentException.
//   - Not-found (run doesn't exist in partition) -> NotFound.
//   - ETag concurrency conflict on write -> ConcurrencyConflict(current).
//   - Time discipline: ClearedAt sourced from injected TimeProvider (never
//     DateTime.UtcNow) — verifies FakeTimeProvider-compatible ctor shape.
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   Hand-rolled in-memory <see cref="IProvisioningRunRepository"/> double
//   (matches the pattern in InMemoryRegistryConcurrencyStore + the reconciler
//   test doubles). No Moq — the seam is simple enough that a purpose-built
//   double reads better.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Rollback;

public sealed class QuarantineClearServiceTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";
    private const string TestActorOid = "22222222-2222-2222-2222-222222222222";
    private const string TestReason = "operator manually restored missing SPE container-type";

    // -----------------------------------------------------------------------
    // Happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_QuarantinedRun_TransitionsToFailed_AndPopulatesClearedBy()
    {
        // Arrange
        var frozenNow = DateTimeOffset.Parse("2026-08-18T12:34:56Z");
        var timeProvider = new FrozenTimeProvider(frozenNow);
        var repo = new InMemoryRunRepository();
        var run = MakeQuarantinedRun();
        repo.Seed(run, etag: "\"etag-1\"");

        var sut = new QuarantineClearService(repo, timeProvider, NullLogger<QuarantineClearService>.Instance);

        // Act
        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        // Assert
        var success = result.Should().BeOfType<QuarantineClearResult.Success>().Subject;
        success.Run.Status.Should().Be(RunStatus.Failed);
        success.Run.CompletedOn.Should().Be(frozenNow);
        success.Run.Quarantine.Should().NotBeNull();
        success.Run.Quarantine!.State.Should().Be(QuarantineState.Cleared);
        success.Run.Quarantine.ClearedBy.Should().Be(TestActorOid);
        success.Run.Quarantine.ClearedAt.Should().Be(frozenNow);
        success.Run.Quarantine.Reason.Should().Be("original-quarantine-reason",
            "existing quarantine reason preserved for audit trail");
        success.Run.Quarantine.QuarantinedByHandler.Should().Be("H2a",
            "originating handler preserved for audit trail");

        repo.ReplaceCalls.Should().Be(1);
    }

    [Fact]
    public async Task ClearAsync_QuarantinedRun_MissingQuarantineInfo_SynthesizesFromReason()
    {
        // Defensive path: run.Status = Quarantined but Quarantine metadata
        // missing (data-shape drift). Service synthesizes a minimal record.
        var frozenNow = DateTimeOffset.Parse("2026-08-18T12:34:56Z");
        var timeProvider = new FrozenTimeProvider(frozenNow);
        var repo = new InMemoryRunRepository();
        var run = MakeQuarantinedRun();
        run.Quarantine = null; // Corrupted / missing metadata.
        repo.Seed(run, etag: "\"etag-1\"");

        var sut = new QuarantineClearService(repo, timeProvider, NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        var success = result.Should().BeOfType<QuarantineClearResult.Success>().Subject;
        success.Run.Quarantine.Should().NotBeNull();
        success.Run.Quarantine!.State.Should().Be(QuarantineState.Cleared);
        success.Run.Quarantine.Reason.Should().Be(TestReason,
            "synthesized quarantine record uses the clear-quarantine reason");
        success.Run.Quarantine.ClearedBy.Should().Be(TestActorOid);
        success.Run.Quarantine.ClearedAt.Should().Be(frozenNow);
    }

    // -----------------------------------------------------------------------
    // Wrong-state (409)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.NotStarted)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.WaitingOnGate)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public async Task ClearAsync_NonQuarantinedRun_ReturnsConflict_WithoutPersisting(RunStatus currentStatus)
    {
        var repo = new InMemoryRunRepository();
        var run = MakeQuarantinedRun();
        run.Status = currentStatus;
        repo.Seed(run, etag: "\"etag-1\"");

        var sut = new QuarantineClearService(
            repo,
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        var conflict = result.Should().BeOfType<QuarantineClearResult.Conflict>().Subject;
        conflict.CurrentStatus.Should().Be(currentStatus);

        // No ReplaceRunAsync fired on the wrong-state path.
        repo.ReplaceCalls.Should().Be(0,
            "wrong-state guard MUST short-circuit before any Cosmos write");
    }

    // -----------------------------------------------------------------------
    // Not-found (404)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_RunNotInPartition_ReturnsNotFound()
    {
        var repo = new InMemoryRunRepository(); // empty — no seed.
        var sut = new QuarantineClearService(
            repo,
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        result.Should().BeOfType<QuarantineClearResult.NotFound>();
        repo.ReplaceCalls.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Concurrent write (409)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_ConcurrentWriter_AdvancedEtag_ReturnsConcurrencyConflict()
    {
        var repo = new InMemoryRunRepository();
        var run = MakeQuarantinedRun();
        repo.Seed(run, etag: "\"etag-1\"");

        // Simulate a concurrent writer having advanced the ETag AFTER our read
        // but BEFORE our write. InMemoryRunRepository's ReplaceRunAsync will
        // return Conflict if the current stored ETag doesn't match the
        // supplied ifMatchEtag — we force it by mutating the seeded ETag after
        // seed but the service's ReadRunAsync captures the "stale" etag-1.
        repo.ForceNextReplaceConflict = true;

        var sut = new QuarantineClearService(
            repo,
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        var conflict = result.Should().BeOfType<QuarantineClearResult.ConcurrencyConflict>().Subject;
        conflict.Current.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Missing-reason (ArgumentException — endpoint layer typically catches
    // as 400, but service enforces the contract at boundary).
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearAsync_MissingReason_ThrowsArgumentException(string? badReason)
    {
        var sut = new QuarantineClearService(
            new InMemoryRunRepository(),
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        Func<Task> act = () => sut.ClearAsync(TestCustomerId, TestRunId, badReason!, TestActorOid, default);
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*reason*");
    }

    [Theory]
    [InlineData(null, TestRunId, "reason ok")]
    [InlineData("", TestRunId, "reason ok")]
    [InlineData(TestCustomerId, null, "reason ok")]
    [InlineData(TestCustomerId, "", "reason ok")]
    public async Task ClearAsync_MissingRouteParams_ThrowsArgumentException(
        string? customerId, string? runId, string reason)
    {
        var sut = new QuarantineClearService(
            new InMemoryRunRepository(),
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        Func<Task> act = () => sut.ClearAsync(customerId!, runId!, reason, TestActorOid, default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ClearAsync_NullActorOid_StillTransitions_ClearedByPersistsAsNull()
    {
        // Actor OID may be null on unauthenticated test paths OR when a JWT
        // has no oid claim (extremely rare in production). Service MUST NOT
        // throw — persists ClearedBy = null.
        var repo = new InMemoryRunRepository();
        var run = MakeQuarantinedRun();
        repo.Seed(run, etag: "\"etag-1\"");

        var sut = new QuarantineClearService(
            repo,
            TimeProvider.System,
            NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, actorObjectId: null, default);

        var success = result.Should().BeOfType<QuarantineClearResult.Success>().Subject;
        success.Run.Quarantine!.ClearedBy.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Time discipline (TEST-ARCHITECTURE.md §4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_UsesInjectedTimeProvider_ForClearedAt()
    {
        var frozenNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var timeProvider = new FrozenTimeProvider(frozenNow);
        var repo = new InMemoryRunRepository();
        repo.Seed(MakeQuarantinedRun(), etag: "\"etag-1\"");

        var sut = new QuarantineClearService(repo, timeProvider, NullLogger<QuarantineClearService>.Instance);

        var result = await sut.ClearAsync(TestCustomerId, TestRunId, TestReason, TestActorOid, default);

        var success = result.Should().BeOfType<QuarantineClearResult.Success>().Subject;
        success.Run.Quarantine!.ClearedAt.Should().Be(frozenNow,
            "ClearedAt MUST come from injected TimeProvider — never DateTime.UtcNow");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ProvisioningRun MakeQuarantinedRun() => new()
    {
        RunId = TestRunId,
        CustomerId = TestCustomerId,
        EnvironmentId = "env-1",
        TenancyModel = "Model2Dedicated",
        Profile = "spaarke-hosted-model2",
        Status = RunStatus.Quarantined,
        CurrentPhase = "H2a",
        Quarantine = new QuarantineInfo
        {
            State = QuarantineState.Quarantined,
            Reason = "original-quarantine-reason",
            QuarantinedByHandler = "H2a",
            QuarantinedAt = DateTimeOffset.Parse("2026-08-18T10:00:00Z"),
        },
    };

    // In-memory repository double — same shape as InMemoryRegistryConcurrencyStore.
    private sealed class InMemoryRunRepository : IProvisioningRunRepository
    {
        private readonly Dictionary<string, (ProvisioningRun Run, string ETag)> _store = new();
        public int ReplaceCalls { get; private set; }
        public bool ForceNextReplaceConflict { get; set; }

        public void Seed(ProvisioningRun run, string etag)
        {
            _store[Key(run.CustomerId, run.RunId)] = (run, etag);
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(
            string customerId, string runId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
            ArgumentException.ThrowIfNullOrWhiteSpace(runId);
            if (_store.TryGetValue(Key(customerId, runId), out var stored))
            {
                return Task.FromResult<ProvisioningRunReadResult?>(
                    new ProvisioningRunReadResult(stored.Run, stored.ETag));
            }
            return Task.FromResult<ProvisioningRunReadResult?>(null);
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(
            ProvisioningRun run, CancellationToken cancellationToken)
        {
            var etag = "\"etag-new\"";
            _store[Key(run.CustomerId, run.RunId)] = (run, etag);
            return Task.FromResult(new ProvisioningRunReadResult(run, etag));
        }

        public Task<ReplaceRunResult> ReplaceRunAsync(
            ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            var key = Key(run.CustomerId, run.RunId);

            if (ForceNextReplaceConflict)
            {
                ForceNextReplaceConflict = false;
                var stored = _store[key];
                return Task.FromResult<ReplaceRunResult>(
                    new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(stored.Run, stored.ETag)));
            }

            if (!_store.TryGetValue(key, out var current))
            {
                return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.NotFound());
            }
            if (!string.Equals(current.ETag, ifMatchEtag, StringComparison.Ordinal))
            {
                return Task.FromResult<ReplaceRunResult>(
                    new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(current.Run, current.ETag)));
            }
            var newEtag = $"\"etag-{Guid.NewGuid():N}\"";
            _store[key] = (run, newEtag);
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, newEtag));
        }

        private static string Key(string customerId, string runId) => $"{customerId}::{runId}";
    }

    // FrozenTimeProvider — no wall-clock dependency; parity with the
    // TestTimeProvider in StateReconcilerServiceTests + the "MutableTimeProvider"
    // in InMemoryTenantTokenLedgerTests (tests/CLAUDE.md-approved pattern).
    private sealed class FrozenTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FrozenTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
